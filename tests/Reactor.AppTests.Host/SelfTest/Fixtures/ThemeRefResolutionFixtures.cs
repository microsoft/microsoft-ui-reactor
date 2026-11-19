using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1534 — <see cref="ThemeRef"/> resolves a theme resource through WinUI's own
/// <c>{ThemeResource}</c> lookup (<see cref="ThemeResourceProbe"/>) instead of reading the theme
/// dictionaries from code, and falls back to the dictionary walk only for keys WinUI cannot
/// resolve for that theme.
/// </summary>
/// <remarks>
/// The bug itself (a first read for the other theme fixing WinUI's entry for the whole app) is
/// process-wide and order-dependent, so it is tested in a fresh process by
/// <c>ThemeRefProbeProcessTests</c> driving <c>--themeref-probe</c>. These fixtures cover what
/// can be checked in a shared process: the resolved brush matches the theme asked for, odd keys
/// survive the markup, and the fallback keeps resolving keys the old walk found.
/// </remarks>
internal static class ThemeRefResolutionFixtures
{
    // Light and Dark values differ for each of these.
    private static readonly string[] Keys =
    [
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "SolidBackgroundFillColorBaseBrush",
        "CardBackgroundFillColorDefaultBrush",
        "LayerFillColorDefaultBrush",
        "ControlFillColorDefaultBrush",
        "ControlFillColorSecondaryBrush",
        "ControlFillColorTertiaryBrush",
        "ControlStrokeColorDefaultBrush",
        "DividerStrokeColorDefaultBrush",
        "SystemFillColorSuccessBrush",
        "SystemFillColorCriticalBrush",
        "SystemFillColorCriticalBackgroundBrush",
    ];

    private static global::Windows.UI.Color? ColorOf(object? brush) => (brush as SolidColorBrush)?.Color;

    /// <summary>
    /// The color <c>{ThemeResource key}</c> takes on an element in <paramref name="theme"/>, read
    /// from a border in the live tree. Never read the theme dictionaries from code for this.
    /// </summary>
    private static async Task<global::Windows.UI.Color?> TreeColorAsync(WinUI.Panel host, string key, ElementTheme theme)
    {
        var border = new WinUI.Border
        {
            Width = 4,
            Height = 4,
            RequestedTheme = theme,
            Style = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                $"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Border'><Setter Property='Background' Value='{{ThemeResource {key}}}'/></Style>"),
        };
        host.Children.Add(border);
        await Harness.Render();
        var color = ColorOf(border.Background);
        host.Children.Remove(border);
        return color;
    }

    /// <summary>
    /// <c>ThemeRef.Resolve(key, isDark)</c> returns the brush an element in that theme gets, for
    /// both themes, whichever theme the app runs in.
    /// </summary>
    internal sealed class MatchesThemeResourceInBothThemes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = new WinUI.StackPanel();
            H.SetContent(host);
            ThemeRef.InvalidateResolutionCache();
            try
            {
                int distinct = 0;
                foreach (var key in Keys)
                {
                    // The tree first: it is the reference, and an element reading a key creates
                    // WinUI's entry correctly, so a wrong Resolve below cannot hide behind it.
                    var treeDark = await TreeColorAsync(host, key, ElementTheme.Dark);
                    var treeLight = await TreeColorAsync(host, key, ElementTheme.Light);
                    if (treeDark is not null && treeLight is not null && treeDark != treeLight) distinct++;

                    var dark = ColorOf(ThemeRef.Resolve(key, isDark: true));
                    var light = ColorOf(ThemeRef.Resolve(key, isDark: false));
                    H.Check($"ThemeRefResolve_MatchesTree_{key}",
                        dark is not null && dark == treeDark && light is not null && light == treeLight,
                        $"resolve dark={dark} light={light}; tree dark={treeDark} light={treeLight}");
                }

                // Every key differs between the themes, so the checks above can tell a brush for
                // the wrong theme from the right one. If an earlier read had fixed WinUI's entries
                // for one theme, the tree would show the same color for both.
                H.Check("ThemeRefResolve_TreeThemesDistinct", distinct == Keys.Length,
                    $"{distinct} of {Keys.Length} keys differ between Light and Dark");
            }
            finally
            {
                H.SetContent(null);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    /// <summary>
    /// Keys with characters that are special in a markup extension or in XML resolve through the
    /// probe, to the instance in the dictionary.
    /// </summary>
    internal sealed class OddKeysResolveThroughProbe(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.Yield();
            string[] keys =
            [
                "ReactorTest Key With Space",
                "ReactorTest,Key,Comma",
                "ReactorTest{Key}Braces",
                "ReactorTest=Key",
                "ReactorTest'Key",
                "ReactorTest\"Key",
                "ReactorTest\\Key\\Backslash",
                "ReactorTest<Key>&Amp",
            ];
            const string BothQuotes = "ReactorTest'Both\"Quotes";
            var dict = new ResourceDictionary();
            foreach (var key in keys) dict[key] = new SolidColorBrush(Microsoft.UI.Colors.Red);
            dict[BothQuotes] = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            Application.Current.Resources.MergedDictionaries.Add(dict);
            ThemeRef.InvalidateResolutionCache();
            try
            {
                foreach (var key in keys)
                {
                    var found = ThemeResourceProbe.TryResolve(key, ElementTheme.Dark, out var value);
                    H.Check($"ThemeRefProbe_OddKey_{Array.IndexOf(keys, key)}",
                        found && ReferenceEquals(value, dict[key]),
                        $"key=[{key}] found={found} value={value}");
                }

                // A key with both quote characters cannot be written as a reference; the walk
                // still finds it.
                H.Check("ThemeRefProbe_BothQuotes_NotProbed", !ThemeResourceProbe.TryResolve(BothQuotes, ElementTheme.Dark, out _));
                H.Check("ThemeRefProbe_BothQuotes_ResolvesByWalk",
                    ReferenceEquals(ThemeRef.Resolve(BothQuotes, isDark: true), dict[BothQuotes]));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    /// <summary>
    /// An app dictionary that defines a key for only some themes: WinUI cannot resolve it for a
    /// theme other than the app's, and the dictionary walk keeps resolving it as before.
    /// </summary>
    internal sealed class PartialThemeKeysFallBackToWalk(Harness h) : SelfTestFixtureBase(h)
    {
        private const string OppositeOnlyKey = "ReactorTest_OppositeThemeOnlyBrush";
        private const string DefaultOnlyKey = "ReactorTest_DefaultOnlyBrush";

        public override async Task RunAsync()
        {
            await Task.Yield();
            var appIsDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            var opposite = appIsDark ? "Light" : "Dark";
            var oppositeBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
            var defaultBrush = new SolidColorBrush(Microsoft.UI.Colors.Green);

            var dict = new ResourceDictionary();
            dict.ThemeDictionaries["Light"] = opposite == "Light" ? new ResourceDictionary { [OppositeOnlyKey] = oppositeBrush } : new ResourceDictionary();
            dict.ThemeDictionaries["Dark"] = opposite == "Dark" ? new ResourceDictionary { [OppositeOnlyKey] = oppositeBrush } : new ResourceDictionary();
            dict.ThemeDictionaries["Default"] = new ResourceDictionary { [DefaultOnlyKey] = defaultBrush };
            Application.Current.Resources.MergedDictionaries.Add(dict);
            ThemeRef.InvalidateResolutionCache();
            try
            {
                // WinUI checks a reference against the app theme when it loads it, so neither
                // key resolves through the probe for the opposite theme: only the walk finds them.
                var probeOpposite = ThemeResourceProbe.TryResolve(OppositeOnlyKey, appIsDark ? ElementTheme.Light : ElementTheme.Dark, out _);
                var probeDefault = ThemeResourceProbe.TryResolve(DefaultOnlyKey, appIsDark ? ElementTheme.Light : ElementTheme.Dark, out _);
                H.Check("ThemeRefFallback_ProbeCannotResolve", !probeOpposite && !probeDefault,
                    $"probeOpposite={probeOpposite} probeDefault={probeDefault}");

                H.Check("ThemeRefFallback_OppositeOnlyKey",
                    ReferenceEquals(ThemeRef.Resolve(OppositeOnlyKey, isDark: !appIsDark), oppositeBrush));
                H.Check("ThemeRefFallback_OppositeOnlyKey_AppThemeNull",
                    ThemeRef.Resolve(OppositeOnlyKey, isDark: appIsDark) is null);
                H.Check("ThemeRefFallback_DefaultOnlyKey",
                    ReferenceEquals(ThemeRef.Resolve(DefaultOnlyKey, isDark: !appIsDark), defaultBrush)
                    && ReferenceEquals(ThemeRef.Resolve(DefaultOnlyKey, isDark: appIsDark), defaultBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    /// <summary>A missing key and a key that is not a brush both resolve to null.</summary>
    internal sealed class MissingAndNonBrushKeysResolveNull(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.Yield();
            ThemeRef.InvalidateResolutionCache();
            H.Check("ThemeRefResolve_MissingKey_Null",
                ThemeRef.Resolve("ReactorTest_NoSuchBrush_1534", isDark: true) is null
                && ThemeRef.Resolve("ReactorTest_NoSuchBrush_1534", isDark: false) is null);

            // The probe finds it, as a CornerRadius; Resolve only returns brushes.
            var found = ThemeResourceProbe.TryResolve("ControlCornerRadius", ElementTheme.Dark, out var radius);
            H.Check("ThemeRefResolve_NonBrush_ProbeFindsValue", found && radius is CornerRadius, $"found={found} value={radius}");
            H.Check("ThemeRefResolve_NonBrush_Null", ThemeRef.Resolve("ControlCornerRadius", isDark: true) is null);
        }
    }
}
