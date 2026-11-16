using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1506: resolved theme brushes follow Windows contrast themes, once WinUI has applied
/// them. A real contrast theme changes the whole desktop, so these fixtures play it:
/// <see cref="HighContrastState.ReaderForTest"/> stands in for the contrast state,
/// <see cref="HighContrastState.SystemReaderForTest"/> for the setting Windows announces, and
/// <see cref="HighContrastState.ProbeReaderForTest"/> for what a host's
/// <see cref="ThemeProbe"/> reads from WinUI. <see cref="ProbeFollowsWinUITheme"/> shows, with a
/// real Light/Dark switch, that WinUI itself updates the probe. The headless halves are pinned
/// in <c>Reactor.Tests/HighContrastThemeTests</c>.
/// </summary>
internal static class HighContrastThemeFixtures
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

    private static void RestoreState()
    {
        HighContrastState.ReaderForTest = null;
        HighContrastState.SystemReaderForTest = null;
        HighContrastState.ProbeReaderForTest = null;
        HighContrastState.ResetForTest();
        ThemeRef.InvalidateResolutionCache();
    }

    // A border whose .Resources(...) override, under `slot`, uses the test's theme key.
    private static Element Overridden(string text, string slot = "ProbeBrush") =>
        Border(TextBlock(text)).Resources(r => r.Set(slot, Theme.Ref(OverrideKey)));

    private static global::Windows.UI.Color? OverrideColor(object? root, string slot = "ProbeBrush") => root switch
    {
        Border b when b.Resources is { } res && res.TryGetValue(slot, out var v) && v is SolidColorBrush s => s.Color,
        Border b => OverrideColor(b.Child, slot),
        Panel p => p.Children.Select(c => OverrideColor(c, slot)).FirstOrDefault(c => c is not null),
        ContentControl c => OverrideColor(c.Content, slot),
        _ => null,
    };

    private static string Describe(Brush? brush) => brush is SolidColorBrush s ? s.Color.ToString() : brush?.GetType().Name ?? "null";

    // A probe's tree check runs on a later Low-priority turn, which a slow machine may not reach
    // within one render: poll for the state instead of reading it once.
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        for (var waited = 0; !condition() && waited < timeoutMs; waited += 50)
            await Task.Delay(50);
        return condition();
    }

    // Lets a probe report that was queued at Low priority run, then waits for the refresh it causes.
    // A probe reports only while its root is in the live tree, so first wait for it to load.
    private static async Task ReportAsync(ThemeProbe probe, Func<Task> waitForIdle)
    {
        await WaitUntilAsync(() => probe.IsLive);
        probe.SimulateChangeForTest();
        var queue = DispatcherQueue.GetForCurrentThread();
        var yielded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(DispatcherQueuePriority.Low, () => yielded.TrySetResult()))
            yielded.TrySetResult();
        await yielded.Task;
        await waitForIdle();
    }

    /// <summary>
    /// While WinUI is in a contrast theme, <see cref="ThemeRef.Resolve(string, bool)"/> reads only
    /// the <c>HighContrast</c> dictionary, whatever Light/Dark is requested. A key without a
    /// contrast value resolves to nothing: reading the light or dark entry instead would create
    /// it with contrast values for the whole app.
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

                // WinUI's plain lookup also reads the theme dictionaries of the theme it uses.
                // That is why the resolver skips it during a contrast theme: for a key without a
                // contrast value it would read WinUI's fallback dictionary.
                H.Check("HC_PlainLookupReadsTheThemeDictionaries",
                    Application.Current.Resources.TryGetValue(LightOnlyKey, out var plain) && ReferenceEquals(plain, LightOnlyBrush),
                    $"found={plain}");

                contrastOn = true;
                ThemeRef.InvalidateResolutionCache();
                H.Check("HC_On_LightRequestUsesContrast", ReferenceEquals(ThemeRef.Resolve(Key, isDark: false), ContrastBrush));
                H.Check("HC_On_DarkRequestUsesContrast", ReferenceEquals(ThemeRef.Resolve(Key, isDark: true), ContrastBrush));
                var withoutContrastValue = ThemeRef.Resolve(LightOnlyKey, isDark: false);
                H.Check("HC_On_KeyWithoutContrastValueResolvesNothing", withoutContrastValue is null,
                    $"resolved={Describe(withoutContrastValue)}");

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
                RestoreState();
            }
        }
    }

    /// <summary>
    /// A host merges a <see cref="ThemeProbe"/> into its content root, and WinUI itself updates it
    /// when it applies a theme. A real contrast switch changes the desktop, so a Light/Dark switch
    /// stands in: WinUI applies both the same way. A probe that replaces a live one counts at
    /// once; the probe follows a new root, counts only while it is in the live tree (WinUI does
    /// not update it anywhere else), goes with empty content, and leaves with the host. Other
    /// fixtures' probes come and go on later turns, so the checks read each probe's own state;
    /// <c>HighContrastThemeTests</c> pins what the count does.
    /// </summary>
    internal class ProbeFollowsWinUITheme(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // A probe that replaces a live one counts at once, so a root swap never leaves a moment
            // without a counted probe, where the system setting would stand in. One that replaces
            // nothing waits for its tree check.
            var first = new Border();
            var second = new Border();
            var third = new Border();
            H.SetContent(new StackPanel { Children = { first, second, third } });
            await Harness.Render();
            var replaced = ThemeProbe.TryAttach(first);
            if (replaced is not null)
                await WaitUntilAsync(() => replaced.IsLive);
            var replacedWasLive = replaced?.IsLive == true;
            var replacing = ThemeProbe.TryAttach(second, replaced);
            var fresh = ThemeProbe.TryAttach(third);
            var atOnce = replacing is { IsLive: true } && fresh is { IsLive: false };
            replaced?.Dispose();
            var afterHandOver = replacing?.IsLive == true;
            replacing?.Dispose();
            fresh?.Dispose();
            H.SetContent(null);
            H.Check("HC_Probe_ReplacementCountsAtOnce", replacedWasLive && atOnce && afterHandOver,
                $"replacedWasLive={replacedWasLive} atOnce={atOnce} afterHandOver={afterHandOver}");

            var host = new ReactorHostControl();
            Action<int>? setRoot = null;
            host.Mount(ctx =>
            {
                var (root, set) = ctx.UseState(0);
                setRoot = set;
                return root switch
                {
                    0 => Border(TextBlock("probe")),
                    1 => VStack(TextBlock("swapped")),
                    _ => Empty(),
                };
            });
            var container = new Border { Child = host };
            H.SetContent(container);
            ThemeProbe? last = null;
            try
            {
                await host.WaitForIdleAsync();
                await Harness.Render();
                var probe = host.ThemeProbeForTest;
                var root = host.ThemeListenerElement;
                var attachedLive = probe is not null && await WaitUntilAsync(() => probe.IsLive);
                H.Check("HC_Probe_AttachedToContentRoot",
                    probe is not null && root is not null && ReferenceEquals(probe.Owner, root) && attachedLive,
                    $"probe={probe is not null} owner={ReferenceEquals(probe?.Owner, root)} live={probe?.IsLive}");
                if (probe is null || root is null) return;

                var expected = root.ActualTheme == ElementTheme.Dark ? ThemeProbe.DefaultColor : ThemeProbe.LightColor;
                H.Check("HC_Probe_ReadsTheAppliedTheme", probe.ColorForTest == expected && !probe.IsHighContrast,
                    $"color={probe.ColorForTest} expected={expected}");

                var changes = probe.ChangeCountForTest;
                var flipTo = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                host.RequestedTheme = flipTo;
                await Harness.Render();
                var flipped = flipTo == ElementTheme.Dark ? ThemeProbe.DefaultColor : ThemeProbe.LightColor;
                H.Check("HC_Probe_WinUIUpdatesItOnAThemeSwitch",
                    probe.ChangeCountForTest > changes && probe.ColorForTest == flipped && !probe.IsHighContrast,
                    $"changes={probe.ChangeCountForTest - changes} color={probe.ColorForTest} expected={flipped}");
                host.RequestedTheme = ElementTheme.Default;
                await Harness.Render();

                setRoot!(1);
                await host.WaitForIdleAsync();
                await Harness.Render();
                var newProbe = host.ThemeProbeForTest;
                var newRoot = host.ThemeListenerElement;
                var newLive = newProbe is not null && await WaitUntilAsync(() => newProbe.IsLive);
                H.Check("HC_Probe_FollowsANewRoot",
                    newProbe is not null && !ReferenceEquals(newProbe, probe) && newRoot is not null && !ReferenceEquals(newRoot, root)
                    && ReferenceEquals(newProbe.Owner, newRoot) && newLive && !probe.IsLive
                    && root.Resources.MergedDictionaries.Count == 0,
                    $"new={newProbe is not null && !ReferenceEquals(newProbe, probe)} live={newProbe?.IsLive} oldLive={probe.IsLive} oldRootDictionaries={root.Resources.MergedDictionaries.Count}");
                if (newProbe is null || newRoot is null) return;

                // Off the tree WinUI no longer updates the probe, so a report from it could be stale.
                H.SetContent(null);
                await Harness.Render();
                var withdrawn = await WaitUntilAsync(() => !newProbe.IsLive);
                H.SetContent(container);
                await Harness.Render();
                var back = await WaitUntilAsync(() => newProbe.IsLive);
                H.Check("HC_Probe_CountsOnlyInTheLiveTree", withdrawn && back, $"withdrawnOffTree={withdrawn} liveBack={back}");

                setRoot!(2);
                await host.WaitForIdleAsync();
                await Harness.Render();
                H.Check("HC_Probe_GoesWithEmptyContent",
                    host.ThemeProbeForTest is null && !newProbe.IsLive && newRoot.Resources.MergedDictionaries.Count == 0,
                    $"probe={host.ThemeProbeForTest is not null} live={newProbe.IsLive} dictionaries={newRoot.Resources.MergedDictionaries.Count}");

                setRoot!(0);
                await host.WaitForIdleAsync();
                await Harness.Render();
                last = host.ThemeProbeForTest;
            }
            finally
            {
                host.Dispose();
                H.SetContent(null);
            }
            H.Check("HC_Probe_DetachedWithTheHost", last is not null && !last.IsLive && last.Owner.Resources.MergedDictionaries.Count == 0,
                $"probe={last is not null} live={last?.IsLive}");
        }
    }

    /// <summary>
    /// A <c>.Resources(...)</c> theme override follows a contrast switch once WinUI has applied it,
    /// reported by a <see cref="ReactorWindow"/>'s probe, and not when Windows announces it: a
    /// re-render in between still resolves the theme WinUI is using.
    /// </summary>
    internal class WindowOverrideWaitsForWinUI(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class Blank : Component
        {
            public override Element Render() => TextBlock("contrast");
        }

        public override async Task RunAsync()
        {
            if (ReactorApp.UIDispatcher is null)
                ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            var window = ReactorApp.OpenWindow(new WindowSpec { Title = "Contrast", Width = 240, Height = 160 }, () => new Blank());
            await window.Host.WaitForIdleAsync();
            await Harness.Render();
            var probe = window.Host.ThemeProbeForTest;

            var dict = CreateOverrideDictionary();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            var announced = false;
            var applied = false;
            HighContrastState.SystemReaderForTest = () => announced;
            HighContrastState.ProbeReaderForTest = () => applied;
            ThemeRef.InvalidateResolutionCache();

            using var host = new ReactorHostControl();
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, set) = ctx.UseState(0);
                setTick = set;
                return Overridden($"tick:{tick}");
            });
            H.SetContent(new Border { Child = host });
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            try
            {
                H.Check("HC_Window_HasAProbe", probe is not null && ReferenceEquals(probe.Owner, window.Host.ThemeListenerElement));
                if (probe is null) return;
                await ReportAsync(probe, () => host.WaitForIdleAsync());
                H.Check("HC_Window_InitialOff", OverrideColor(host.Content) == Colors.Red, $"color={OverrideColor(host.Content)}");

                announced = true;
                ThemeRef.InvalidateResolutionCache();   // e.g. UISettings.ColorValuesChanged
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("HC_Window_AnnouncedSwitchWaitsForWinUI", OverrideColor(host.Content) == Colors.Red, $"color={OverrideColor(host.Content)}");

                applied = true;
                await ReportAsync(probe, () => host.WaitForIdleAsync());
                H.Check("HC_Window_AppliedSwitchUsesContrast", OverrideColor(host.Content) == Colors.Yellow, $"color={OverrideColor(host.Content)}");

                announced = false;
                ThemeRef.InvalidateResolutionCache();
                setTick!(2);
                await host.WaitForIdleAsync();
                H.Check("HC_Window_AnnouncedOffWaitsForWinUI", OverrideColor(host.Content) == Colors.Yellow, $"color={OverrideColor(host.Content)}");

                applied = false;
                await ReportAsync(probe, () => host.WaitForIdleAsync());
                H.Check("HC_Window_AppliedOffRestores", OverrideColor(host.Content) == Colors.Red, $"color={OverrideColor(host.Content)}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreState();
                H.SetContent(null);
                try { window.Close(); } catch (global::System.Runtime.InteropServices.COMException) { }
            }
        }
    }

    /// <summary>
    /// A host built over a raw <see cref="Window"/> (no <see cref="ReactorWindow"/>, like a tray
    /// flyout) has its own probe.
    /// </summary>
    internal class RawHostFollowsProbe(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var dict = CreateOverrideDictionary();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            var applied = false;
            HighContrastState.ProbeReaderForTest = () => applied;
            ThemeRef.InvalidateResolutionCache();
            var target = new Border();
            H.SetContent(target);
            try
            {
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                using var isolation = ThemeResourceListeners.IsolateForTest(host);
                host.Mount(_ => Overridden("raw"));
                await host.WaitForIdleAsync();
                await Harness.Render();
                var probe = host.ThemeProbeForTest;
                H.Check("HC_RawHost_HasAProbe",
                    host.OwningWindow is null && probe is not null && ReferenceEquals(probe.Owner, host.ThemeListenerElement));
                if (probe is null) return;
                // The probe must not keep WinUI from loading the root: subscribing to the root's
                // Loaded while it entered the tree during a render did, for a root like this one.
                H.Check("HC_RawHost_RootStillLoads", probe.Owner.IsLoaded);
                await ReportAsync(probe, () => host.WaitForIdleAsync());
                H.Check("HC_RawHost_InitialOff", OverrideColor(target.Child) == Colors.Red, $"color={OverrideColor(target.Child)}");

                applied = true;
                await ReportAsync(probe, () => host.WaitForIdleAsync());
                H.Check("HC_RawHost_AppliedSwitchUsesContrast", OverrideColor(target.Child) == Colors.Yellow,
                    $"color={OverrideColor(target.Child)} live={probe.IsLive}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreState();
                H.SetContent(null);
            }
        }
    }

    /// <summary>
    /// An embedded <see cref="ReactorHostControl"/>'s probe refreshes memoized subtrees too. The
    /// "stale" check is the negative control: a re-render that already resolves the contrast
    /// state does not reach the memoized override, so the refresh must bypass memoization.
    /// </summary>
    internal class HostControlProbeRefreshesMemo(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var dict = CreateOverrideDictionary();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            var applied = false;
            HighContrastState.ProbeReaderForTest = () => applied;
            ThemeRef.InvalidateResolutionCache();

            using var host = new ReactorHostControl();
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, set) = ctx.UseState(0);
                setTick = set;
                return VStack(Overridden($"tick:{tick}", "LiveBrush"), Memo(_ => Overridden("memo")));
            });
            H.SetContent(new Border { Child = host });
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            try
            {
                await host.WaitForIdleAsync();
                await Harness.Render();
                var probe = host.ThemeProbeForTest;
                H.Check("HC_HostControl_HasAProbe", probe is not null && ReferenceEquals(probe.Owner, host.ThemeListenerElement));
                if (probe is null) return;
                await ReportAsync(probe, () => host.WaitForIdleAsync());
                H.Check("HC_HostControl_InitialOff", OverrideColor(host.Content) == Colors.Red, $"color={OverrideColor(host.Content)}");

                HighContrastState.ReaderForTest = static () => true;
                ThemeRef.InvalidateResolutionCache();
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("HC_HostControl_MemoStaleAfterPartialRerender",
                    OverrideColor(host.Content, "LiveBrush") == Colors.Yellow && OverrideColor(host.Content) == Colors.Red,
                    $"live={OverrideColor(host.Content, "LiveBrush")} memo={OverrideColor(host.Content)}");
                HighContrastState.ReaderForTest = null;

                applied = true;
                await ReportAsync(probe, () => host.WaitForIdleAsync());
                H.Check("HC_HostControl_MemoUsesContrast", OverrideColor(host.Content) == Colors.Yellow, $"color={OverrideColor(host.Content)}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                RestoreState();
                H.SetContent(null);
            }
        }
    }
}
