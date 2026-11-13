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
/// invalidation, one refresh per change, the real system read) are pinned in
/// <c>Reactor.Tests/HighContrastThemeTests</c>.
/// </summary>
internal static partial class HighContrastThemeFixtures
{
    private const string Key = "ReactorSelfTestContrastBrush";
    private const string LightOnlyKey = "ReactorSelfTestNoContrastValueBrush";
    private const string OverrideKey = "ReactorSelfTestContrastOverrideBrush";

    private static readonly SolidColorBrush LightBrush = new(Colors.Red);
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Maroon);
    private static readonly SolidColorBrush ContrastBrush = new(Colors.Yellow);
    private static readonly SolidColorBrush LightOnlyBrush = new(Colors.Lime);

    // Our own merged dictionaries: the app dictionary has a Source (XamlControlsResources) and
    // rejects local values. ThemeRef resolution scans merged dictionaries too.
    private static ResourceDictionary CreateThemedDictionary()
    {
        var dict = new ResourceDictionary();
        dict.ThemeDictionaries["Light"] = new ResourceDictionary { [Key] = LightBrush, [LightOnlyKey] = LightOnlyBrush };
        dict.ThemeDictionaries["Default"] = new ResourceDictionary { [Key] = DefaultBrush, [LightOnlyKey] = LightOnlyBrush };
        dict.ThemeDictionaries[ThemeRef.HighContrastThemeName] = new ResourceDictionary { [Key] = ContrastBrush };
        return dict;
    }

    // The same off value for Light and Dark, so the host fixtures do not depend on the app theme.
    private static ResourceDictionary CreateOverrideDictionary()
    {
        var off = new SolidColorBrush(Colors.Red);
        var dict = new ResourceDictionary();
        dict.ThemeDictionaries["Light"] = new ResourceDictionary { [OverrideKey] = off };
        dict.ThemeDictionaries["Default"] = new ResourceDictionary { [OverrideKey] = off };
        dict.ThemeDictionaries[ThemeRef.HighContrastThemeName] = new ResourceDictionary { [OverrideKey] = new SolidColorBrush(Colors.Yellow) };
        return dict;
    }

    private static void RestoreSystemSetting()
    {
        HighContrastState.ReaderForTest = null;
        HighContrastState.ResetForTest();
        ThemeRef.InvalidateResolutionCache();
    }

    // The ThemeRef override under test; ProbeColor finds it anywhere in the rendered tree.
    private static Element Probe(string text) =>
        Border(TextBlock(text)).Resources(r => r.Set("ProbeBrush", Theme.Ref(OverrideKey)));

    private static global::Windows.UI.Color? ProbeColor(object? root) => root switch
    {
        Border b when b.Resources is { } res && res.TryGetValue("ProbeBrush", out var v) && v is SolidColorBrush s => s.Color,
        Border b => ProbeColor(b.Child),
        Panel p => p.Children.Select(ProbeColor).FirstOrDefault(c => c is not null),
        ContentControl c => ProbeColor(c.Content),
        _ => null,
    };

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
    /// A <c>.Resources(...)</c> theme override follows a contrast switch once a
    /// <see cref="ReactorWindow"/> gets the <c>WM_SETTINGCHANGE</c> Windows broadcasts for it. The
    /// "stale" check is the negative control: an ordinary re-render does not pick the switch up,
    /// so the following checks measure the window message rather than any re-render.
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

            var dict = CreateOverrideDictionary();
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
                return Probe($"tick:{tick}");
            });
            H.SetContent(new Border { Child = host });
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            try
            {
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_InitialOff", ProbeColor(host.Content) == Colors.Red, $"color={ProbeColor(host.Content)}");

                contrastOn = true;
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_RerenderAloneIsStale", ProbeColor(host.Content) == Colors.Red, $"color={ProbeColor(host.Content)}");

                SendSettingChange(window.Hwnd);
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_OnUsesContrast", ProbeColor(host.Content) == Colors.Yellow, $"color={ProbeColor(host.Content)}");

                contrastOn = false;
                SendSettingChange(window.Hwnd);
                await host.WaitForIdleAsync();
                H.Check("HC_SettingChange_OffRestores", ProbeColor(host.Content) == Colors.Red, $"color={ProbeColor(host.Content)}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreSystemSetting();
                H.SetContent(null);
                try { window.Close(); } catch (global::System.Runtime.InteropServices.COMException) { }
            }
        }
    }

    /// <summary>
    /// A host built over a raw <see cref="Window"/> (no <see cref="ReactorWindow"/>, like a tray
    /// flyout) watches <c>WM_SETTINGCHANGE</c> itself.
    /// </summary>
    internal class RawHostFollowsSettingChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var dict = CreateOverrideDictionary();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            var contrastOn = false;
            HighContrastState.ReaderForTest = () => contrastOn;
            ThemeRef.InvalidateResolutionCache();
            var target = new Border();
            H.SetContent(target);
            try
            {
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                using var isolation = ThemeResourceListeners.IsolateForTest(host);
                host.Mount(_ => Probe("raw"));
                await host.WaitForIdleAsync();
                H.Check("HC_RawHost_WatchesSettingChange", host.OwningWindow is null && host.HasSettingChangeMonitorForTest);
                H.Check("HC_RawHost_InitialOff", ProbeColor(target.Child) == Colors.Red, $"color={ProbeColor(target.Child)}");

                contrastOn = true;
                SendSettingChange(WinRT.Interop.WindowNative.GetWindowHandle(H.Window));
                await host.WaitForIdleAsync();
                H.Check("HC_RawHost_OnUsesContrast", ProbeColor(target.Child) == Colors.Yellow, $"color={ProbeColor(target.Child)}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreSystemSetting();
                H.SetContent(null);
            }
        }
    }

    /// <summary>
    /// An embedded <see cref="ReactorHostControl"/> has no window message monitor; its
    /// <c>UISettings.ColorValuesChanged</c> handler must fully refresh on a contrast switch,
    /// memoized subtrees included. The "stale" check is the negative control: a re-render that
    /// already resolves with the new setting does not reach the memoized override, and it must
    /// not stop the full refresh either.
    /// </summary>
    internal class HostControlColorChangeRefreshesMemo(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var dict = CreateOverrideDictionary();
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
                return VStack(TextBlock($"tick:{tick}"), Memo(_ => Probe("memo")));
            });
            H.SetContent(new Border { Child = host });
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            try
            {
                await host.WaitForIdleAsync();
                H.Check("HC_ColorChange_InitialOff", ProbeColor(host.Content) == Colors.Red, $"color={ProbeColor(host.Content)}");

                contrastOn = true;
                ThemeRef.InvalidateResolutionCache();   // e.g. an ActualThemeChanged arriving first
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("HC_ColorChange_MemoStaleAfterPartialRerender", ProbeColor(host.Content) == Colors.Red, $"color={ProbeColor(host.Content)}");

                host.RaiseColorValuesChangedForTest();
                await host.WaitForIdleAsync();
                H.Check("HC_ColorChange_MemoUsesContrast", ProbeColor(host.Content) == Colors.Yellow, $"color={ProbeColor(host.Content)}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreSystemSetting();
                H.SetContent(null);
            }
        }
    }

    // Literal values, not the product's constants, so a wrong constant is caught.
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const nint SPI_SETHIGHCONTRAST = 0x0043;

    private static void SendSettingChange(nint hwnd)
        => _ = SendMessageW(hwnd, WM_SETTINGCHANGE, SPI_SETHIGHCONTRAST, 0);

    [global::System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
}
