using Microsoft.UI;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1506: resolved theme brushes follow Windows contrast themes. The contrast setting
/// is replaced through <see cref="HighContrastState.ReaderForTest"/>, because turning on a
/// real contrast theme changes the whole desktop. The headless halves (re-read on
/// invalidation, one notification per change) are pinned in
/// <c>Reactor.Tests/HighContrastThemeTests</c>.
/// </summary>
internal static partial class HighContrastThemeFixtures
{
    private const string Key = "ReactorSelfTestContrastBrush";
    private const string LightOnlyKey = "ReactorSelfTestNoContrastValueBrush";

    private static readonly SolidColorBrush LightBrush = new(Colors.Red);
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Maroon);
    private static readonly SolidColorBrush ContrastBrush = new(Colors.Yellow);
    private static readonly SolidColorBrush LightOnlyBrush = new(Colors.Lime);

    // Our own merged dictionary: the app dictionary has a Source (XamlControlsResources) and
    // rejects local values. ThemeRef resolution scans merged dictionaries too.
    private static ResourceDictionary CreateThemedDictionary()
    {
        var dict = new ResourceDictionary();
        dict.ThemeDictionaries["Light"] = new ResourceDictionary { [Key] = LightBrush, [LightOnlyKey] = LightOnlyBrush };
        dict.ThemeDictionaries["Default"] = new ResourceDictionary { [Key] = DefaultBrush, [LightOnlyKey] = LightOnlyBrush };
        dict.ThemeDictionaries[ThemeRef.HighContrastThemeName] = new ResourceDictionary { [Key] = ContrastBrush };
        return dict;
    }

    private static void RestoreSystemSetting()
    {
        HighContrastState.ReaderForTest = null;
        HighContrastState.ResetForTest();
        ThemeRef.InvalidateResolutionCache();
    }

    /// <summary>
    /// <see cref="ThemeRef.Resolve(string, bool)"/> uses the <c>HighContrast</c> dictionary while a
    /// contrast theme is on, whatever Light/Dark is requested, and falls back for keys that
    /// dictionary does not define.
    /// </summary>
    internal class ResolveUsesHighContrastDictionary(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.Yield();
            var dict = CreateThemedDictionary();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            var contrastOn = false;
            HighContrastState.ReaderForTest = () => contrastOn;
            try
            {
                ThemeRef.InvalidateResolutionCache();
                H.Check("HC_Off_LightRequestUsesLight", ReferenceEquals(ThemeRef.Resolve(Key, isDark: false), LightBrush));
                H.Check("HC_Off_DarkRequestUsesDefault", ReferenceEquals(ThemeRef.Resolve(Key, isDark: true), DefaultBrush));
                var fluentLight = ThemeRef.Resolve("CardBackgroundFillColorDefaultBrush", isDark: false);

                contrastOn = true;
                ThemeRef.InvalidateResolutionCache();
                H.Check("HC_On_LightRequestUsesContrast", ReferenceEquals(ThemeRef.Resolve(Key, isDark: false), ContrastBrush));
                H.Check("HC_On_DarkRequestUsesContrast", ReferenceEquals(ThemeRef.Resolve(Key, isDark: true), ContrastBrush));
                H.Check("HC_On_KeyWithoutContrastValueFallsBack", ReferenceEquals(ThemeRef.Resolve(LightOnlyKey, isDark: false), LightOnlyBrush));

                // The Fluent tokens themselves: XamlControlsResources ships a HighContrast dictionary.
                var fluentContrast = ThemeRef.Resolve("CardBackgroundFillColorDefaultBrush", isDark: false);
                H.Check("HC_On_FluentTokenUsesContrastBrush",
                    fluentLight is not null && fluentContrast is not null && !ReferenceEquals(fluentLight, fluentContrast),
                    $"light={Describe(fluentLight)} contrast={Describe(fluentContrast)}");

                contrastOn = false;
                ThemeRef.InvalidateResolutionCache();
                H.Check("HC_OffAgain_LightRequestUsesLight", ReferenceEquals(ThemeRef.Resolve(Key, isDark: false), LightBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreSystemSetting();
            }
        }

        private static string Describe(Brush? brush) => brush is SolidColorBrush s ? s.Color.ToString() : brush?.GetType().Name ?? "null";
    }

    /// <summary>
    /// A <c>.Resources(...)</c> theme override follows a contrast switch once the window gets the
    /// <c>WM_SETTINGCHANGE</c> Windows broadcasts for it. The "stale" check is the negative
    /// control: an ordinary re-render does not pick the switch up, so the final checks measure
    /// the window message rather than any re-render.
    /// </summary>
    internal class ResourceOverrideFollowsSettingChange(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class Blank : Component
        {
            public override Element Render() => TextBlock("contrast");
        }

        public override async Task RunAsync()
        {
            // A real ReactorWindow, so the WM_SETTINGCHANGE goes through its message monitor.
            if (ReactorApp.UIDispatcher is null)
                ReactorApp.UIDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            var window = ReactorApp.OpenWindow(new WindowSpec { Title = "Contrast", Width = 240, Height = 160 }, () => new Blank());
            await window.Host.WaitForIdleAsync();

            var dict = CreateThemedDictionary();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            var contrastOn = false;
            HighContrastState.ReaderForTest = () => contrastOn;
            ThemeRef.InvalidateResolutionCache();

            using var host = new ReactorHostControl();
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, set) = ctx.UseState(0);
                setTick = set;
                return Border(TextBlock($"tick:{tick}"))
                    .Resources(r => r.Set("ProbeBrush", Theme.Ref(Key)));
            });
            H.SetContent(new Border { Child = host });
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            try
            {
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_InitialLight", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");

                contrastOn = true;
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_RerenderAloneIsStale", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");

                SendSettingChange(window.Hwnd);
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_OnUsesContrast", ProbeColor(host) == Colors.Yellow, $"color={ProbeColor(host)}");

                contrastOn = false;
                SendSettingChange(window.Hwnd);
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_OffRestoresLight", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreSystemSetting();
                H.SetContent(null);
                try { window.Close(); } catch (global::System.Runtime.InteropServices.COMException) { }
            }
        }

        private static global::Windows.UI.Color? ProbeColor(ReactorHostControl host)
            => host.Content is Border b
               && b.Resources is { } res
               && res.TryGetValue("ProbeBrush", out var value)
               && value is SolidColorBrush brush
                ? brush.Color
                : null;
    }

    private const uint WM_SETTINGCHANGE = 0x001A;

    private static void SendSettingChange(nint hwnd)
        => _ = SendMessageW(hwnd, WM_SETTINGCHANGE, (nint)HighContrastState.SPI_SETHIGHCONTRAST, 0);

    [global::System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
}
