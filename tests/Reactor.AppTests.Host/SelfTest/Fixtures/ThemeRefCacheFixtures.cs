using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Perf #85/#86 — <see cref="ThemeRef"/> resolution caches. These need
/// <c>Application.Current.Resources</c> + real <see cref="FrameworkElement"/>s,
/// so they live as selftest (not headless xUnit) fixtures. They guard:
/// <list type="bullet">
///   <item>effective theme drives the resolved brush (Light vs Dark) — #85/#86
///     correctness;</item>
///   <item>a per-element <c>RequestedTheme</c> flip is picked up WITHOUT a
///     global <see cref="ThemeRef.InvalidateCache"/> — the per-element name
///     cache is validated against the element's cheap RequestedTheme/ActualTheme
///     DP reads (#85);</item>
///   <item>a Default-themed child inheriting an ANCESTOR's <c>RequestedTheme</c>
///     re-resolves correctly when the ancestor flips and a new reconcile pass
///     opens — the reconcile-scoped name cache, fixing the PR-review H1 finding
///     that a generation-only cache could serve such a child a stale theme;</item>
///   <item>a Light-themed element falls back to the <c>"Default"</c> theme
///     dictionary when no <c>"Light"</c> entry exists (#86 resolver fallback);</item>
///   <item>the (key, theme) brush cache is real (a swapped dictionary brush is
///     returned stale) and <see cref="ThemeRef.InvalidateCache"/> drops it — the
///     invalidation the host wires to ActualThemeChanged / ColorValuesChanged
///     (#86).</item>
/// </list>
/// </summary>
internal static class ThemeRefCacheFixtures
{
    private const string Key = "ReactorThemeRefCacheTestBrush";

    private static SolidColorBrush NewBrush(byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(255, r, g, b));

    // Installs an isolated merged dictionary into Application.Current.Resources
    // whose ThemeDictionaries carry our unique test key for Light/Dark/Default.
    // Returns the merged dictionary (so the caller can remove it and avoid
    // polluting global app resources) and the Light theme dictionary (so the
    // caller can swap the brush instance to exercise cache invalidation).
    private static (ResourceDictionary merged, ResourceDictionary light) InstallThemeDictionaries(
        SolidColorBrush light, SolidColorBrush dark)
    {
        var lightDict = new ResourceDictionary { [Key] = light };
        var darkDict = new ResourceDictionary { [Key] = dark };
        var merged = new ResourceDictionary();
        merged.ThemeDictionaries["Light"] = lightDict;
        merged.ThemeDictionaries["Dark"] = darkDict;
        Application.Current.Resources.MergedDictionaries.Add(merged);
        return (merged, lightDict);
    }

    internal class ThemeRefCache_ResolvesCachesAndInvalidates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var light = NewBrush(10, 20, 30);
            var dark = NewBrush(200, 210, 220);
            var (merged, lightDict) = InstallThemeDictionaries(light, dark);
            // Start from a clean cache so a prior fixture's resolves can't leak in.
            ThemeRef.InvalidateCache();

            try
            {
                var feLight = new Border { RequestedTheme = ElementTheme.Light };
                var feDark = new Border { RequestedTheme = ElementTheme.Dark };
                var panel = new StackPanel();
                panel.Children.Add(feLight);
                panel.Children.Add(feDark);
                H.SetContent(panel);
                await Harness.Render();

                // ── #85/#86 correctness: effective theme drives the brush ──
                var lightResolved = ThemeRef.Resolve(Key, feLight);
                var darkResolved = ThemeRef.Resolve(Key, feDark);
                H.Check("ThemeRefCache_LightResolvesLightBrush", ReferenceEquals(lightResolved, light));
                H.Check("ThemeRefCache_DarkResolvesDarkBrush", ReferenceEquals(darkResolved, dark));

                // Repeated resolve is stable (cache hit returns the same brush).
                var lightAgain = ThemeRef.Resolve(Key, feLight);
                H.Check("ThemeRefCache_RepeatResolveStable", ReferenceEquals(lightAgain, light));

                // ── #85 (PR review H1): a per-element RequestedTheme flip is
                // picked up WITHOUT any global InvalidateCache. The element's
                // cached effective-theme name is validated against its cheap
                // RequestedTheme/ActualTheme DP reads, so a Light→Dark flip
                // resolves the dark brush even though the generation never bumped.
                feLight.RequestedTheme = ElementTheme.Dark;
                await Harness.Render();
                var afterFlip = ThemeRef.Resolve(Key, feLight);
                H.Check("ThemeRefCache_PerElementThemeFlipPickedUp", ReferenceEquals(afterFlip, dark));

                feLight.RequestedTheme = ElementTheme.Light;
                await Harness.Render();
                var afterFlipBack = ThemeRef.Resolve(Key, feLight);
                H.Check("ThemeRefCache_PerElementThemeFlipBack", ReferenceEquals(afterFlipBack, light));

                // ── #86 brush cache + invalidation: swap the underlying Light
                // brush instance. The cache is keyed by (key, theme) and is NOT
                // bumped by mutating the dictionary, so the stale instance is
                // returned UNTIL InvalidateCache clears it. (No awaits between the
                // swap and the stale read, so no dispatcher event can invalidate
                // out from under the assertion.)
                var lightV2 = NewBrush(11, 22, 33);
                lightDict[Key] = lightV2;
                var stillCached = ThemeRef.Resolve(Key, feLight);
                H.Check("ThemeRefCache_StaleUntilInvalidate", ReferenceEquals(stillCached, light));

                ThemeRef.InvalidateCache();
                var afterInvalidate = ThemeRef.Resolve(Key, feLight);
                H.Check("ThemeRefCache_FreshAfterInvalidate", ReferenceEquals(afterInvalidate, lightV2));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(merged);
                ThemeRef.InvalidateCache();
                H.SetContent(null);
            }
        }
    }

    // #85 (PR review H1): a Default-themed child inherits its effective theme
    // from the nearest ANCESTOR with an explicit RequestedTheme. The child's own
    // RequestedTheme stays Default across an ancestor flip, so the element-local
    // DP comparison cannot detect it — only re-walking the ancestors does. The
    // reconcile-scoped name cache forces that re-walk once per render
    // (BeginReconcilePass), so the child never serves a theme stale w.r.t. its
    // ancestor (the regression a generation-only cache left in the propagation
    // window before WinUI updates the child's ActualTheme).
    internal class ThemeRefCache_AncestorThemeFlipInheritedByDefaultChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var light = NewBrush(10, 20, 30);
            var dark = NewBrush(200, 210, 220);
            var (merged, _) = InstallThemeDictionaries(light, dark);
            ThemeRef.InvalidateCache();

            try
            {
                var ancestor = new StackPanel { RequestedTheme = ElementTheme.Light };
                var child = new Border();   // Default — no own RequestedTheme override
                ancestor.Children.Add(child);
                H.SetContent(ancestor);
                await Harness.Render();

                var inheritedLight = ThemeRef.Resolve(Key, child);
                H.Check("ThemeRefCache_DefaultChildInheritsAncestorLight",
                    ReferenceEquals(inheritedLight, light));

                // Flip the ANCESTOR, then open a new reconcile pass exactly as the
                // host does at the top of each render. The child re-walks to the
                // ancestor and resolves the dark brush WITHOUT relying on WinUI's
                // ActualTheme having propagated to the child yet — the propagation
                // window the previous generation-only cache could serve stale.
                ancestor.RequestedTheme = ElementTheme.Dark;
                ThemeRef.BeginReconcilePass();
                var inheritedDark = ThemeRef.Resolve(Key, child);
                H.Check("ThemeRefCache_DefaultChildPicksUpAncestorFlip",
                    ReferenceEquals(inheritedDark, dark));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(merged);
                ThemeRef.InvalidateCache();
                H.SetContent(null);
            }
        }
    }

    // #86 resolver fallback: when no theme-specific (e.g. "Light") entry exists,
    // ResolveForThemeUncached falls back to the "Default" theme dictionary — the
    // XamlControlsResources shape where the base brushes live under "Default".
    internal class ThemeRefCache_DefaultDictionaryFallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var def = NewBrush(123, 45, 67);
            var defaultDict = new ResourceDictionary { [Key] = def };
            var merged = new ResourceDictionary();
            merged.ThemeDictionaries["Default"] = defaultDict;
            Application.Current.Resources.MergedDictionaries.Add(merged);
            ThemeRef.InvalidateCache();

            try
            {
                var feLight = new Border { RequestedTheme = ElementTheme.Light };
                H.SetContent(feLight);
                await Harness.Render();

                // themeName resolves to "Light", which is absent, so the resolver
                // falls back to the "Default" theme dictionary entry.
                var resolved = ThemeRef.Resolve(Key, feLight);
                H.Check("ThemeRefCache_DefaultDictionaryFallbackResolves",
                    ReferenceEquals(resolved, def));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(merged);
                ThemeRef.InvalidateCache();
                H.SetContent(null);
            }
        }
    }

    // #86 (Copilot round-6): the PUBLIC Resolve(key, isDark) overload is UNCACHED.
    // It has no element/theme listener and there is no public InvalidateCache, so
    // caching it would strand a stale brush for a caller that swaps
    // Application.Current.Resources at runtime. This guards that a runtime brush
    // swap is observed immediately by the very next public-overload resolve, while
    // the internal FrameworkElement Resolve stays cached (covered above).
    internal class ThemeRefCache_PublicIsDarkOverloadUncached(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var light = NewBrush(10, 20, 30);
            var dark = NewBrush(200, 210, 220);
            var (merged, lightDict) = InstallThemeDictionaries(light, dark);
            ThemeRef.InvalidateCache();
            await Harness.Render();

            try
            {
                // The public isDark overload resolves the right theme brush…
                H.Check("ThemeRefPublic_ResolvesLight",
                    ReferenceEquals(ThemeRef.Resolve(Key, isDark: false), light));
                H.Check("ThemeRefPublic_ResolvesDark",
                    ReferenceEquals(ThemeRef.Resolve(Key, isDark: true), dark));

                // …and a runtime dictionary swap is observed IMMEDIATELY (uncached),
                // unlike the FrameworkElement hot path which holds the old brush
                // until InvalidateCache. No public invalidation API exists, so this
                // overload must never cache.
                var lightV2 = NewBrush(11, 22, 33);
                lightDict[Key] = lightV2;
                H.Check("ThemeRefPublic_RuntimeSwapObservedImmediately",
                    ReferenceEquals(ThemeRef.Resolve(Key, isDark: false), lightV2));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(merged);
                ThemeRef.InvalidateCache();
                H.SetContent(null);
            }
        }
    }
}
