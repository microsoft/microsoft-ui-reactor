using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Elements;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Mount-theme bug — a ThemeRef-backed <c>ResourceOverrides</c>
/// (<c>.Resources(r =&gt; r.Set(key, Theme.Ref(srcKey)))</c>) on a subtree whose
/// effective theme comes from an <b>ancestor's</b> <c>RequestedTheme</c> (not the
/// element's own) must resolve its concrete brush against that ancestor theme at
/// mount — NOT the app fallback.
///
/// <para>ROOT CAUSE: <c>ApplyResourceOverrides</c> resolved via
/// <c>ThemeRef.Resolve(key, fe)</c>, whose effective-theme walk needs the element to
/// be parented. At mount the control is not yet in the visual tree, so an ancestor
/// <c>RequestedTheme</c> is unreachable and resolution falls back to
/// <c>Application.Current.RequestedTheme</c>. A subtree with ancestor
/// <c>RequestedTheme=Dark</c> under a Light app got the LIGHT brush. FIX: the
/// reconciler threads the ancestor-aware effective theme top-down
/// (<c>_ambientRequestedTheme</c>) and passes it to <c>ApplyResourceOverrides</c>,
/// which resolves via <c>ThemeRef.Resolve(key, isDark)</c>.</para>
///
/// <para>DETERMINISM: the ancestor theme is always set to the OPPOSITE of the live
/// app theme, so "resolve against ancestor" and "resolve against app" produce
/// different brushes regardless of whether the host app runs Light or Dark. A unique
/// themed <c>ResourceDictionary</c> (explicit Light + Dark <c>ThemeDictionaries</c>)
/// is merged into <c>Application.Current.Resources</c> so <c>ThemeRef.Resolve</c>
/// returns the exact shared brush instance per theme — asserted by reference.
/// RED before the fix (resolves the app variant), GREEN after (the ancestor variant).</para>
/// </summary>
internal static class ResourceOverrideMountThemeFixtures
{
    private static SolidColorBrush MakeBrush(byte r, byte g, byte b) =>
        new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, r, g, b));

    private static SolidColorBrush? ResourceBrush(FrameworkElement? fe, string key)
    {
        if (fe?.Resources is { } res && res.TryGetValue(key, out var v))
            return v as SolidColorBrush;
        return null;
    }

    // The app-level ResourceDictionary has a Source (XamlControlsResources) and rejects
    // direct local values, but a merged Source-less dictionary is fine. Install one whose
    // ThemeDictionaries carry a different brush per theme under a unique key; ThemeRef.Resolve
    // discovers it via its ThemeDictionaries scan over MergedDictionaries. #660 — the
    // (key,theme)->Brush cache is invalidated on install so a prior run's entry can't leak.
    private static ResourceDictionary InstallThemedDict(string key, SolidColorBrush light, SolidColorBrush dark)
    {
        var dict = new ResourceDictionary();
        dict.ThemeDictionaries["Light"] = new ResourceDictionary { [key] = light };
        dict.ThemeDictionaries["Dark"] = new ResourceDictionary { [key] = dark };
        Application.Current.Resources.MergedDictionaries.Add(dict);
        global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
        return dict;
    }

    // ancestorTheme is the OPPOSITE of the live app theme; the matching ancestor brush is the
    // value the fix must resolve, and the app-theme brush is the (wrong) pre-fix value.
    private static (ElementTheme ancestorTheme, SolidColorBrush ancestorBrush, SolidColorBrush appBrush)
        AncestorAgainstApp(SolidColorBrush light, SolidColorBrush dark)
    {
        var appIsDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return appIsDark
            ? (ElementTheme.Light, light, dark)
            : (ElementTheme.Dark, dark, light);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Single-level inheritance — RED pre-fix, GREEN post-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A child carrying ONLY a ThemeRef-backed ResourceOverride (no own RequestedTheme)
    /// sits directly under an ancestor whose RequestedTheme is the opposite of the app
    /// theme. At mount the child must resolve the ancestor brush. RED pre-fix (resolved
    /// the app brush via the unparented fe walk falling back to app theme), GREEN after.
    /// </summary>
    internal sealed class AncestorThemeResolvesAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_Single";
        private const string TargetKey = "Item1Target_Single";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 220);
            var dark = MakeBrush(220, 0, 0);
            var (ancestorTheme, ancestorBrush, appBrush) = AncestorAgainstApp(light, dark);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                    VStack(
                        TextBlock("singleChild")
                            .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                    .RequestedTheme(ancestorTheme));

                await Harness.Render();

                var tb = H.FindControl<TextBlock>(t => t.Text == "singleChild");
                H.Check("Item1Single_ChildPresent", tb is not null);
                var resolved = ResourceBrush(tb, TargetKey);
                // The crux: ancestor brush (opposite of app), not the app fallback.
                H.Check("Item1Single_ResolvedAncestorBrush", ReferenceEquals(resolved, ancestorBrush));
                H.Check("Item1Single_NotAppBrush", !ReferenceEquals(resolved, appBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Multi-level inheritance through a themeless intermediate — RED pre-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The ancestor RequestedTheme is inherited across an intermediate container that
    /// declares no theme of its own. The deeply-nested child's override must still
    /// resolve the ancestor brush at mount — proving the ambient threads through the
    /// whole subtree, not just one level.
    /// </summary>
    internal sealed class NestedAncestorThemeResolvesAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_Nested";
        private const string TargetKey = "Item1Target_Nested";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 200);
            var dark = MakeBrush(200, 0, 0);
            var (ancestorTheme, ancestorBrush, appBrush) = AncestorAgainstApp(light, dark);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                    VStack(
                        VStack( // intermediate: no RequestedTheme of its own
                            TextBlock("nestedChild")
                                .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))))
                    .RequestedTheme(ancestorTheme));

                await Harness.Render();

                var tb = H.FindControl<TextBlock>(t => t.Text == "nestedChild");
                H.Check("Item1Nested_ChildPresent", tb is not null);
                var resolved = ResourceBrush(tb, TargetKey);
                H.Check("Item1Nested_ResolvedAncestorBrush", ReferenceEquals(resolved, ancestorBrush));
                H.Check("Item1Nested_NotAppBrush", !ReferenceEquals(resolved, appBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Element's OWN RequestedTheme still wins — regression guard (GREEN both)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the override-bearing element declares its OWN RequestedTheme (differing from
    /// the ancestor), its own theme must win — the ambient only supplies the inherited
    /// fallback. This stayed correct pre-fix (fe.RequestedTheme is set before
    /// ApplyResourceOverrides) and must remain correct after; a guard that the fix never
    /// lets an inherited ancestor theme override an explicit own theme.
    /// </summary>
    internal sealed class OwnThemeWinsAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_OwnWins";
        private const string TargetKey = "Item1Target_OwnWins";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 180);
            var dark = MakeBrush(180, 0, 0);
            // Ancestor = opposite of app; the child's OWN theme = the app theme (so it
            // differs from the ancestor). The child must resolve its OWN-theme brush.
            var (ancestorTheme, _, ownBrush) = AncestorAgainstApp(light, dark);
            var ownTheme = ancestorTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            var ancestorBrush = ownTheme == ElementTheme.Dark ? light : dark; // the brush the child must NOT pick
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                    VStack(
                        TextBlock("ownChild")
                            .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))
                            .RequestedTheme(ownTheme))
                    .RequestedTheme(ancestorTheme));

                await Harness.Render();

                var tb = H.FindControl<TextBlock>(t => t.Text == "ownChild");
                H.Check("Item1OwnWins_ChildPresent", tb is not null);
                var resolved = ResourceBrush(tb, TargetKey);
                H.Check("Item1OwnWins_ResolvedOwnBrush", ReferenceEquals(resolved, ownBrush));
                H.Check("Item1OwnWins_NotAncestorBrush", !ReferenceEquals(resolved, ancestorBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }
}
