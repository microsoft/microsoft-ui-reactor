using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Perf #85/#86 — <see cref="ThemeRef"/> resolution caches. These need
/// <c>Application.Current.Resources</c> + real <see cref="FrameworkElement"/>s,
/// so they live as a selftest (not a headless xUnit) fixture. The single
/// fixture guards, in order:
/// <list type="bullet">
///   <item>effective theme drives the resolved brush (Light vs Dark) — #85/#86
///     correctness;</item>
///   <item>a per-element <c>RequestedTheme</c> flip is picked up WITHOUT a
///     global <see cref="ThemeRef.InvalidateCache"/> — the per-element name
///     cache is validated against the element's cheap RequestedTheme/ActualTheme
///     DP reads (the regression the PR review's H1 finding flagged; #85);</item>
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
}
