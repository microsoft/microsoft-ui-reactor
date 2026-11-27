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
/// Issue #1506: a host resolves its theme references again once WinUI has switched it into or
/// out of a Windows contrast theme. <see cref="HighContrastProbe"/> hears WinUI apply the theme.
/// A real contrast switch is a desktop-wide setting, so these fixtures never make one: the first
/// proves WinUI updates the probe when it applies a theme (a Light/Dark switch, the same theme
/// walk), and the others play what the probes read (<see cref="HighContrastProbe.ReaderForTest"/>)
/// and check what each host does with it. All of them assume the desktop is not in a contrast
/// theme, and skip when it is.
/// </summary>
internal static class HighContrastRefreshFixtures
{
    private const string AppKey = "ReactorSelfTestContrastBrush";

    /// <summary>
    /// WinUI updates the probe itself when it applies a theme to the host. A Light/Dark switch is
    /// not a contrast switch, so it must not refresh the host (ActualThemeChanged covers it).
    /// </summary>
    internal class ProbeFollowsWinUITheme(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            if (SkipInContrastTheme(H, "HCProbe_FollowsWinUITheme")) return;
            using var host = new ReactorHostControl();
            host.Mount(_ => TextBlock("contrast-probe"));
            var parent = new Border { RequestedTheme = ElementTheme.Light, Child = host };
            H.SetContent(parent);
            try
            {
                await host.WaitForIdleAsync();
                var probe = host.ContrastProbeForTest;
                H.Check("HCProbe_AttachedToHostControl", probe is not null && ReferenceEquals(probe.Owner, host),
                    $"owner={probe?.Owner?.GetType().Name ?? "none"}");
                if (probe is null) return;
                H.Check("HCProbe_LightReadsLightDictionary", probe.ColorForTest == HighContrastProbe.LightColor,
                    $"color={probe.ColorForTest}");
                H.Check("HCProbe_NotContrastInLight", !probe.IsHighContrast);

                int changes = probe.ChangeCountForTest;
                parent.RequestedTheme = ElementTheme.Dark;
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCProbe_WinUIUpdatesProbeOnThemeWalk", probe.ChangeCountForTest > changes
                    && probe.ColorForTest == HighContrastProbe.DefaultColor,
                    $"changes={probe.ChangeCountForTest - changes} color={probe.ColorForTest}");
                H.Check("HCProbe_LightDarkSwitchDoesNotRefresh", host.ContrastRefreshCountForTest == 0,
                    $"refreshes={host.ContrastRefreshCountForTest}");

                host.Dispose();
                H.Check("HCProbe_DisposeRemovesDictionary", !HoldsProbe(host), "probe dictionary still merged");
            }
            finally
            {
                H.SetContent(null);
            }
        }
    }

    /// <summary>
    /// An embedded <see cref="ReactorHostControl"/>: a contrast switch WinUI applied re-resolves
    /// <c>.Resources(...)</c> overrides, memoized ones included, and <see cref="ThemeRef.Resolve(string, bool)"/>
    /// in a render, once per switch. The app brush is edited first without notifying, so
    /// "RerenderAloneIsStale" is the negative control: an ordinary re-render, and a probe change
    /// that is not a contrast switch, leave both stale.
    /// </summary>
    internal class HostControlRefreshesOnContrastSwitch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            if (SkipInContrastTheme(H, "HCHostCtrl_RefreshesOnContrastSwitch")) return;
            var resources = new ResourceDictionary { [AppKey] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();

            using var host = new ReactorHostControl();
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, set) = ctx.UseState(0);
                setTick = set;
                return VStack(
                    TextBlock($"tick:{tick}"),
                    MemoizedOverride(),
                    TextBlock("RenderResolved").Foreground(ThemeRef.Resolve(AppKey, isDark: false)!));
            });
            H.SetContent(new Border { Child = host });
            try
            {
                await host.WaitForIdleAsync();
                var probe = host.ContrastProbeForTest;
                H.Check("HCHostCtrl_ProbeAttached", probe is not null);
                if (probe is null) return;
                H.Check("HCHostCtrl_InitialRed", OverrideColor(host) == Colors.Red && RenderResolvedColor(host) == Colors.Red,
                    $"override={OverrideColor(host)} resolved={RenderResolvedColor(host)}");

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                setTick!(1);
                await host.WaitForIdleAsync();
                probe.SimulateChangeForTest();   // WinUI changed the probe, still out of contrast
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_RerenderAloneIsStale", OverrideColor(host) == Colors.Red
                    && RenderResolvedColor(host) == Colors.Red && host.ContrastRefreshCountForTest == 0,
                    $"override={OverrideColor(host)} resolved={RenderResolvedColor(host)} refreshes={host.ContrastRefreshCountForTest}");

                int reads = 0;
                HighContrastProbe.ReaderForTest = () => { reads++; return true; };
                probe.SimulateChangeForTest();
                // A second callback in the same theme walk is one check.
                probe.SimulateChangeForTest();
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_ContrastOnRefreshes", OverrideColor(host) == Colors.Blue && RenderResolvedColor(host) == Colors.Blue,
                    $"override={OverrideColor(host)} resolved={RenderResolvedColor(host)}");
                H.Check("HCHostCtrl_OneCheckPerThemeWalk", host.ContrastRefreshCountForTest == 1 && reads == 1,
                    $"refreshes={host.ContrastRefreshCountForTest} reads={reads}");

                resources[AppKey] = new SolidColorBrush(Colors.Green);
                probe.SimulateChangeForTest();   // still in contrast: nothing switched
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_SameStateDoesNotRefresh", host.ContrastRefreshCountForTest == 1
                    && OverrideColor(host) == Colors.Blue,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(host)}");

                HighContrastProbe.ReaderForTest = static () => false;
                probe.SimulateChangeForTest();
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_ContrastOffRefreshes", host.ContrastRefreshCountForTest == 2
                    && OverrideColor(host) == Colors.Green,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(host)}");
            }
            finally
            {
                HighContrastProbe.ReaderForTest = null;
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    /// <summary>
    /// A window <see cref="ReactorHost"/> with no chart (issue #1506's second gap: it heard no
    /// palette change at all). Its probe lives on the content root, follows a root swap, goes when
    /// the host renders nothing, and goes with the host. A switch the old probe could not report
    /// (its check still queued when the root was swapped, or no probe at all) still refreshes the
    /// host: the next probe starts from the state the host last rendered with.
    /// </summary>
    internal class WindowHostRefreshesOnContrastSwitch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            if (SkipInContrastTheme(H, "HCWindow_RefreshesOnContrastSwitch")) return;
            var resources = new ResourceDictionary { [AppKey] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();

            var previousActiveHost = ReactorApp.ActiveHostInternal;
            var target = new Border();
            H.SetContent(target);
            try
            {
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                Action<int>? setRoot = null;
                host.Mount(ctx =>
                {
                    var (root, set) = ctx.UseState(0);
                    setRoot = set;
                    return root switch
                    {
                        0 => VStack(TextBlock("window"), MemoizedOverride()),
                        1 => Border(VStack(TextBlock("swapped"), MemoizedOverride())),
                        _ => Empty(),
                    };
                });
                await host.WaitForIdleAsync();

                var probe = host.ContrastProbeForTest;
                var root = target.Child as FrameworkElement;
                H.Check("HCWindow_ProbeOnContentRoot", probe is not null && root is not null && ReferenceEquals(probe.Owner, root)
                    && HoldsProbe(root), $"owner={probe?.Owner?.GetType().Name ?? "none"} root={root?.GetType().Name ?? "none"}");
                if (probe is null || root is null) return;

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                HighContrastProbe.ReaderForTest = static () => true;
                probe.SimulateChangeForTest();
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCWindow_ContrastOnRefreshesMemoizedOverride", host.ContrastRefreshCountForTest == 1
                    && OverrideColor(target) == Colors.Blue,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(target)}");

                // WinUI switches back, and a render swaps the root before the old probe's check
                // runs (a Normal-priority render goes ahead of the Low-priority check).
                resources[AppKey] = new SolidColorBrush(Colors.Green);
                HighContrastProbe.ReaderForTest = static () => false;
                probe.SimulateChangeForTest();
                setRoot!(1);
                await SettleAsync(host);
                var swapped = target.Child as FrameworkElement;
                var moved = host.ContrastProbeForTest;
                H.Check("HCWindow_ProbeFollowsRootSwap", swapped is Border && moved is not null && ReferenceEquals(moved.Owner, swapped)
                    && HoldsProbe(swapped) && !HoldsProbe(root),
                    $"root={swapped?.GetType().Name ?? "none"} owner={moved?.Owner?.GetType().Name ?? "none"} oldHolds={HoldsProbe(root)}");
                H.Check("HCWindow_SwitchPendingAtRootSwapRefreshes", host.ContrastRefreshCountForTest == 2
                    && OverrideColor(target) == Colors.Green,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(target)}");
                if (swapped is null) return;

                setRoot!(2);
                await host.WaitForIdleAsync();
                H.Check("HCWindow_EmptyRenderRemovesProbe", host.ContrastProbeForTest is null && !HoldsProbe(swapped));

                // A switch while the host shows nothing reaches the next root.
                resources[AppKey] = new SolidColorBrush(Colors.Yellow);
                HighContrastProbe.ReaderForTest = static () => true;
                setRoot!(0);
                await SettleAsync(host);
                H.Check("HCWindow_SwitchWithoutProbeRefreshesNextRoot", host.ContrastRefreshCountForTest == 3
                    && host.ContrastProbeForTest is not null && OverrideColor(target) == Colors.Yellow,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(target)}");

                var last = host.ContrastProbeForTest?.Owner;
                host.Dispose();
                await YieldLowAsync();
                H.Check("HCWindow_DisposeRemovesProbe", host.ContrastProbeForTest is null && last is not null && !HoldsProbe(last));
            }
            finally
            {
                HighContrastProbe.ReaderForTest = null;
                ReactorApp.ActiveHostInternal = previousActiveHost;
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // In a Windows contrast theme WinUI ignores RequestedTheme and the probes start in contrast,
    // so the plays above would not be switches.
    private static bool SkipInContrastTheme(Harness h, string name)
    {
        if (!new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return false;
        h.Skip(name, "a Windows contrast theme is on");
        return true;
    }

    // A reference-equal memoized subtree: an ordinary re-render skips it, so only a
    // resource-refresh pass re-resolves its override.
    private static Element MemoizedOverride()
        => Memo("contrastOverride", () => Border(TextBlock("override"))
            .Resources(r => r.Set("ContrastProbeBrush", Theme.Ref(AppKey))));

    private static global::Windows.UI.Color? OverrideColor(DependencyObject? node)
    {
        if (node is FrameworkElement fe && fe.Resources is { } res
            && res.TryGetValue("ContrastProbeBrush", out var value) && value is SolidColorBrush brush)
            return brush.Color;
        // The element tree, not the visual tree: a host that has not been laid out yet has no
        // visual children.
        return node switch
        {
            Panel panel => panel.Children.Select(OverrideColor).FirstOrDefault(c => c is not null),
            Border border => OverrideColor(border.Child),
            ContentControl content => OverrideColor(content.Content as DependencyObject),
            _ => null,
        };
    }

    private static global::Windows.UI.Color? RenderResolvedColor(ReactorHostControl host)
        => host.Content is Panel { Children.Count: > 2 } panel
           && panel.Children[2] is TextBlock { Foreground: SolidColorBrush brush }
            ? brush.Color
            : null;

    private static bool HoldsProbe(FrameworkElement element)
        => element.Resources is { } resources
           && resources.MergedDictionaries.Any(d => d.ContainsKey(HighContrastProbe.BrushKey));

    // The new probe's check is queued at Low priority by the render, after the host went idle.
    private static async Task SettleAsync(ReactorHost host)
    {
        await host.WaitForIdleAsync();
        await YieldLowAsync();
        await host.WaitForIdleAsync();
    }

    private static Task YieldLowAsync()
    {
        var done = new TaskCompletionSource();
        if (!DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, () => done.SetResult()))
            done.SetResult();
        return done.Task;
    }
}
