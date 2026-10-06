using Microsoft.UI;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Live-WinUI coverage for <see cref="ReactorHostControl.WaitForIdleAsync"/> /
/// <see cref="ReactorHostControl.IsIdle"/> and <see cref="Theme.NotifyResourcesChanged"/>.
/// The headless halves (the shared idle loop, the cache clear, the listener fan-out) are
/// pinned in <c>Reactor.Tests/HostIdleAndThemeResourcesTests</c>; these prove the real
/// hosts are wired to them.
/// </summary>
internal static class HostIdleAndThemeResourceFixtures
{
    /// <summary>
    /// A standalone island used to need wall-clock waits (see HostingCoverageFixtures);
    /// WaitForIdleAsync must make the render observable deterministically, with no delay.
    /// </summary>
    internal class HostControlWaitForIdle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = new ReactorHostControl();
            Action<int>? setCount = null;
            host.Mount(ctx =>
            {
                var (count, set) = ctx.UseState(0);
                setCount = set;
                return TextBlock($"Idle:{count}");
            });
            H.SetContent(new Border { Child = host });
            try
            {
                await host.WaitForIdleAsync();
                H.Check("HostCtrlIdle_IdleAfterWait", host.IsIdle);
                H.Check("HostCtrlIdle_InitialRendered", (host.Content as TextBlock)?.Text == "Idle:0",
                    $"content={(host.Content as TextBlock)?.Text ?? host.Content?.GetType().Name ?? "null"}");

                // <snippet:host-control-wait-for-idle>
                setCount!(1);
                H.Check("HostCtrlIdle_BusyAfterSetState", !host.IsIdle);

                await host.WaitForIdleAsync();   // no Task.Delay: the island's tree is now realized
                H.Check("HostCtrlIdle_UpdatedWithoutDelay", (host.Content as TextBlock)?.Text == "Idle:1",
                    $"content={(host.Content as TextBlock)?.Text ?? "null"}");
                // </snippet:host-control-wait-for-idle>

                host.Dispose();
                H.Check("HostCtrlIdle_DisposedIsIdle", host.IsIdle);
                H.Check("HostCtrlIdle_DisposedWaitCompletes", host.WaitForIdleAsync().IsCompleted);
            }
            finally
            {
                H.SetContent(null);
            }
        }
    }

    /// <summary>
    /// <see cref="Theme.NotifyResourcesChanged"/> reaches every live host, and the harness
    /// keeps earlier fixtures' hosts alive on the shared content area. Wait for all of them
    /// to settle so their re-renders land before this fixture (or the next) reads anything.
    /// </summary>
    private static async Task WaitForAllHostsIdleAsync()
    {
        foreach (var listener in ThemeResourceListeners.LiveListenersForTest())
        {
            if (listener is ReactorHost windowHost) await windowHost.WaitForIdleAsync();
            else if (listener is ReactorHostControl island) await island.WaitForIdleAsync();
        }
    }

    /// <summary>
    /// A runtime edit of an app resource is invisible to a ThemeRef resource override until
    /// <see cref="Theme.NotifyResourcesChanged"/> — the (key, theme) resolution cache is only
    /// cleared on a theme or palette change. The "stale" check is the negative control: it
    /// proves an ordinary re-render does NOT pick the edit up, so the final check measures
    /// the notification rather than any re-render. Reads the island directly, so other
    /// hosts re-rendering into the harness content area cannot affect it.
    /// </summary>
    internal class NotifyResourcesChangedRefreshesThemeRefOverrides(Harness h) : SelfTestFixtureBase(h)
    {
        private const string AppKey = "ReactorSelfTestNotifyResourcesBrush";

        public override async Task RunAsync()
        {
            // The app dictionary has a Source (XamlControlsResources) and rejects local
            // values; use our own merged dictionary, which ThemeRef resolution also scans.
            var resources = new ResourceDictionary { [AppKey] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();

            using var host = new ReactorHostControl();
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, set) = ctx.UseState(0);
                setTick = set;
                return Border(TextBlock($"tick:{tick}"))
                    .Resources(r => r.Set("ProbeBrush", Theme.Ref(AppKey)));
            });
            H.SetContent(new Border { Child = host });
            try
            {
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_InitialRed", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_RerenderAloneIsStale", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");

                Theme.NotifyResourcesChanged();
                await WaitForAllHostsIdleAsync();
                H.Check("ThemeNotify_NotifiedIsBlue", ProbeColor(host) == Colors.Blue, $"color={ProbeColor(host)}");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
                H.SetContent(null);
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

    /// <summary>
    /// The window <see cref="ReactorHost"/> path, a reference-equal (UseMemo'd) subtree, an
    /// ordinary <c>.Foreground(Theme.Ref(...))</c> modifier and a background-thread call.
    /// A memoized subtree is skipped wholesale by a plain re-render — and by a hot-reload
    /// style force pass, which only declines wrapper skips — so its <c>{ThemeResource}</c>
    /// style setter is never re-applied. The resource-refresh pass must walk into it, and into
    /// a keyed <c>Memo(key, ...)</c> with an unchanged key and an ItemsHost (ComboBox item elements) whose items
    /// are kept, which skip their children by other routes.
    /// "RerenderAloneIsStale" is the negative control.
    /// </summary>
    internal class NotifyResourcesChangedRefreshesMemoizedThemeModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        private const string AppKey = "ReactorSelfTestNotifyResourcesMemoBrush";

        public override async Task RunAsync()
        {
            // <snippet:runtime-resource-dictionary>
            var resources = new ResourceDictionary { [AppKey] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            // </snippet:runtime-resource-dictionary>
            ThemeRef.InvalidateResolutionCache();

            // A private ContentTarget on the (already settled) harness window: the other
            // hosts this notification re-renders write into the shared content area, never
            // into this Border, which is read directly.
            var previousActiveHost = ReactorApp.ActiveHostInternal;
            var target = new Border();
            H.SetContent(target);
            try
            {
                // Disposed at the end of this block, before finally restores the active host.
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                Action<int>? setTick = null;
                host.Mount(ctx =>
                {
                    var (tick, set) = ctx.UseState(0);
                    setTick = set;
                    var memo = ctx.UseMemo<Element>(
                        () => Border(TextBlock("ThemeMemoProbe").Foreground(Theme.Ref(AppKey))));
                    // An ItemsHost (ComboBox item elements) whose items are kept, and a keyed Memo with an
                    // unchanged key: both skip their children outside a resource refresh.
                    var listItems = ctx.UseMemo<Element[]>(
                        () => [TextBlock("ListItemProbe").Foreground(Theme.Ref(AppKey))]);
                    return VStack(
                        TextBlock($"tick:{tick}"),
                        memo,
                        Memo("keyed", () => TextBlock("KeyedMemoProbe").Foreground(Theme.Ref(AppKey))),
                        // A concrete brush resolved eagerly inside the factory: equal on both
                        // sides of a self-diff, so only a diff against the mounted output writes it.
                        Memo("resolved", () => TextBlock("ResolvedMemoProbe").Foreground(ThemeRef.Resolve(AppKey, isDark: false)!)),
                        // Nested keyed memos share one realized control.
                        Memo("outer", () => Memo("inner",
                            () => TextBlock("NestedMemoProbe").Foreground(ThemeRef.Resolve(AppKey, isDark: false)!))),
                        ComboBox(listItems, default, null));
                });
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_InitialRed", ProbeColor(target) == Colors.Red, $"color={ProbeColor(target)}");

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_Rerendered", FindText(target, "tick:1") is not null);
                H.Check("ThemeMemo_RerenderAloneIsStale", ProbeColor(target) == Colors.Red, $"color={ProbeColor(target)}");
                H.Check("ThemeMemo_NestedMemoRerenderAloneIsStale", ProbeColor(target, "NestedMemoProbe") == Colors.Red, $"color={ProbeColor(target, "NestedMemoProbe")}");
                H.Check("ThemeMemo_ResolvedMemoRerenderAloneIsStale", ProbeColor(target, "ResolvedMemoProbe") == Colors.Red, $"color={ProbeColor(target, "ResolvedMemoProbe")}");
                H.Check("ThemeMemo_KeyedMemoRerenderAloneIsStale", ProbeColor(target, "KeyedMemoProbe") == Colors.Red, $"color={ProbeColor(target, "KeyedMemoProbe")}");
                H.Check("ThemeMemo_ListItemRerenderAloneIsStale", ListItemColor(target) == Colors.Red, $"color={ListItemColor(target)}");

                await Task.Run(Theme.NotifyResourcesChanged);
                await WaitForAllHostsIdleAsync();
                H.Check("ThemeMemo_NotifiedFromBackgroundIsBlue", ProbeColor(target) == Colors.Blue, $"color={ProbeColor(target)}");
                H.Check("ThemeMemo_NestedMemoNotifiedIsBlue", ProbeColor(target, "NestedMemoProbe") == Colors.Blue, $"color={ProbeColor(target, "NestedMemoProbe")}");
                H.Check("ThemeMemo_ResolvedMemoNotifiedIsBlue", ProbeColor(target, "ResolvedMemoProbe") == Colors.Blue, $"color={ProbeColor(target, "ResolvedMemoProbe")}");
                H.Check("ThemeMemo_KeyedMemoNotifiedIsBlue", ProbeColor(target, "KeyedMemoProbe") == Colors.Blue, $"color={ProbeColor(target, "KeyedMemoProbe")}");
                H.Check("ThemeMemo_ListItemNotifiedIsBlue", ListItemColor(target) == Colors.Blue, $"color={ListItemColor(target)}");

                // A second edit, notified from the UI thread: the call is repeatable.
                // <snippet:runtime-resource-edit>
                resources[AppKey] = new SolidColorBrush(Colors.Green);
                Theme.NotifyResourcesChanged();
                // </snippet:runtime-resource-edit>
                await WaitForAllHostsIdleAsync();
                H.Check("ThemeMemo_SecondEditIsGreen", ProbeColor(target) == Colors.Green, $"color={ProbeColor(target)}");
            }
            finally
            {
                ReactorApp.ActiveHostInternal = previousActiveHost;
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }

        private static global::Windows.UI.Color? ProbeColor(Border target, string probe = "ThemeMemoProbe")
            => (FindText(target, probe)?.Foreground as SolidColorBrush)?.Color;

        // Read through ComboBox.Items: the item is the TextBlock itself, whether or not the
        // drop-down has ever been opened.
        private static global::Windows.UI.Color? ListItemColor(Border target)
            => (FindComboBox(target)?.Items.OfType<TextBlock>().FirstOrDefault(t => t.Text == "ListItemProbe")
                    ?.Foreground as SolidColorBrush)?.Color;

        private static ComboBox? FindComboBox(DependencyObject? root)
        {
            if (root is null) return null;
            if (root is ComboBox cb) return cb;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (FindComboBox(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            }
            return null;
        }

        private static TextBlock? FindText(DependencyObject? root, string text)
        {
            if (root is null) return null;
            if (root is TextBlock tb && tb.Text == text) return tb;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (FindText(VisualTreeHelper.GetChild(root, i), text) is { } found) return found;
            }
            return null;
        }
    }
}