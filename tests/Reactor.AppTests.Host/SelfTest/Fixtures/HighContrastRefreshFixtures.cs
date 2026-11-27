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
/// walk), and the others play what the probe reads (<see cref="HighContrastProbe.ReaderForTest"/>)
/// and check what each host does with it.
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
    /// <c>.Resources(...)</c> overrides, memoized ones included, once per switch. The app brush
    /// is edited first without notifying, so "RerenderAloneIsStale" is the negative control: an
    /// ordinary re-render, and a probe change that is not a contrast switch, leave it stale.
    /// </summary>
    internal class HostControlRefreshesOnContrastSwitch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var resources = new ResourceDictionary { [AppKey] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();

            using var host = new ReactorHostControl();
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, set) = ctx.UseState(0);
                setTick = set;
                return VStack(TextBlock($"tick:{tick}"), MemoizedOverride());
            });
            H.SetContent(new Border { Child = host });
            try
            {
                await host.WaitForIdleAsync();
                var probe = host.ContrastProbeForTest;
                H.Check("HCHostCtrl_ProbeAttached", probe is not null);
                if (probe is null) return;
                H.Check("HCHostCtrl_InitialRed", OverrideColor(host.Content) == Colors.Red, $"color={OverrideColor(host.Content)}");

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                setTick!(1);
                await host.WaitForIdleAsync();
                probe.SimulateChangeForTest();   // WinUI changed the probe, still out of contrast
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_RerenderAloneIsStale", OverrideColor(host.Content) == Colors.Red
                    && host.ContrastRefreshCountForTest == 0,
                    $"color={OverrideColor(host.Content)} refreshes={host.ContrastRefreshCountForTest}");

                probe.ReaderForTest = static () => true;
                probe.SimulateChangeForTest();
                // A second callback in the same theme walk is one check.
                probe.SimulateChangeForTest();
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_ContrastOnRefreshesMemoizedOverride", OverrideColor(host.Content) == Colors.Blue,
                    $"color={OverrideColor(host.Content)}");
                H.Check("HCHostCtrl_OneRefreshPerSwitch", host.ContrastRefreshCountForTest == 1,
                    $"refreshes={host.ContrastRefreshCountForTest}");

                resources[AppKey] = new SolidColorBrush(Colors.Green);
                probe.SimulateChangeForTest();   // still in contrast: nothing switched
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_SameStateDoesNotRefresh", host.ContrastRefreshCountForTest == 1
                    && OverrideColor(host.Content) == Colors.Blue,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(host.Content)}");

                probe.ReaderForTest = static () => false;
                probe.SimulateChangeForTest();
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCHostCtrl_ContrastOffRefreshes", host.ContrastRefreshCountForTest == 2
                    && OverrideColor(host.Content) == Colors.Green,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(host.Content)}");
            }
            finally
            {
                if (host.ContrastProbeForTest is { } p) p.ReaderForTest = null;
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    /// <summary>
    /// A window <see cref="ReactorHost"/> with no chart (issue #1506's second gap: it heard no
    /// palette change at all). Its probe lives on the content root, follows a root swap, and goes
    /// with the host.
    /// </summary>
    internal class WindowHostRefreshesOnContrastSwitch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var resources = new ResourceDictionary { [AppKey] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();

            var previousActiveHost = ReactorApp.ActiveHostInternal;
            var target = new Border();
            H.SetContent(target);
            try
            {
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                Action<bool>? setGrid = null;
                host.Mount(ctx =>
                {
                    var (grid, set) = ctx.UseState(false);
                    setGrid = set;
                    return grid
                        ? Border(TextBlock("swapped"))
                        : VStack(TextBlock("window"), MemoizedOverride());
                });
                await host.WaitForIdleAsync();

                var probe = host.ContrastProbeForTest;
                var root = target.Child as FrameworkElement;
                H.Check("HCWindow_ProbeOnContentRoot", probe is not null && root is not null && ReferenceEquals(probe.Owner, root)
                    && HoldsProbe(root), $"owner={probe?.Owner?.GetType().Name ?? "none"} root={root?.GetType().Name ?? "none"}");
                if (probe is null || root is null) return;

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                probe.ReaderForTest = static () => true;
                probe.SimulateChangeForTest();
                await YieldLowAsync();
                await host.WaitForIdleAsync();
                H.Check("HCWindow_ContrastOnRefreshesMemoizedOverride", host.ContrastRefreshCountForTest == 1
                    && OverrideColor(target.Child) == Colors.Blue,
                    $"refreshes={host.ContrastRefreshCountForTest} color={OverrideColor(target.Child)}");
                probe.ReaderForTest = null;

                setGrid!(true);
                await host.WaitForIdleAsync();
                var swapped = target.Child as FrameworkElement;
                var moved = host.ContrastProbeForTest;
                H.Check("HCWindow_ProbeFollowsRootSwap", swapped is Border && moved is not null && ReferenceEquals(moved.Owner, swapped)
                    && HoldsProbe(swapped) && !HoldsProbe(root),
                    $"root={swapped?.GetType().Name ?? "none"} owner={moved?.Owner?.GetType().Name ?? "none"} oldHolds={HoldsProbe(root)}");

                host.Dispose();
                await YieldLowAsync();
                H.Check("HCWindow_DisposeRemovesProbe", host.ContrastProbeForTest is null && swapped is not null && !HoldsProbe(swapped));
            }
            finally
            {
                ReactorApp.ActiveHostInternal = previousActiveHost;
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // A reference-equal memoized subtree: an ordinary re-render skips it, so only a
    // resource-refresh pass re-resolves its override.
    private static Element MemoizedOverride()
        => Memo("contrastOverride", () => Border(TextBlock("override"))
            .Resources(r => r.Set("ContrastProbeBrush", Theme.Ref(AppKey))));

    private static global::Windows.UI.Color? OverrideColor(object? content)
        => content is Panel { Children.Count: > 1 } panel
           && panel.Children[1] is Border border
           && border.Resources.TryGetValue("ContrastProbeBrush", out var value)
           && value is SolidColorBrush brush
            ? brush.Color
            : null;

    private static bool HoldsProbe(FrameworkElement element)
        => element.Resources is { } resources
           && resources.MergedDictionaries.Any(d => d.ContainsKey(HighContrastProbe.BrushKey));

    private static Task YieldLowAsync()
    {
        var done = new TaskCompletionSource();
        if (!DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, () => done.SetResult()))
            done.SetResult();
        return done.Task;
    }
}
