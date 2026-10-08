using Microsoft.UI;
using Microsoft.UI.Reactor.Controls.Validation;
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
    /// <see cref="Theme.NotifyResourcesChanged"/> re-renders every live host, and the harness keeps
    /// other fixtures' hosts alive, some deliberately left failing (RenderErrorHandlerFixtures) or
    /// on windows whose re-render touches shared app state. Hide them for the fixture, so it
    /// measures only its own hosts. Fan-out to several hosts is covered by unit tests.
    /// </summary>
    private static IDisposable OnlyTheseHostsListen(params IThemeResourceListener[] hosts)
        => ThemeResourceListeners.IsolateForTest(hosts);
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
            using var isolation = OnlyTheseHostsListen(host);
            try
            {
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_InitialRed", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_RerenderAloneIsStale", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");

                Theme.NotifyResourcesChanged();
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_NotifiedIsBlue", ProbeColor(host) == Colors.Blue, $"color={ProbeColor(host)}");

                // The source key goes away: the override it wrote must go too.
                resources.Remove(AppKey);
                setTick!(2);
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_RemovalRerenderAloneIsStale", ProbeColor(host) == Colors.Blue, $"color={ProbeColor(host)}");

                Theme.NotifyResourcesChanged();
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_RemovedSourceDropsOverride", ProbeColor(host) is null, $"color={ProbeColor(host)}");
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
    /// a keyed <c>Memo(key, ...)</c> with an unchanged key, ListView/GridView items whose array is kept, and an ItemsHost (ComboBox item elements) whose items
    /// are kept, which skip their children by other routes.
    /// "RerenderAloneIsStale" is the negative control.
    /// </summary>
    internal class NotifyResourcesChangedRefreshesMemoizedThemeModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // <snippet:runtime-resource-dictionary>
            const string AppKey = "SelfTestBrandBrush"; // the key the UI reads with Theme.Ref(AppKey)
            var resources = new ResourceDictionary { [AppKey] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            // </snippet:runtime-resource-dictionary>
            ThemeRef.InvalidateResolutionCache();

            // A private ContentTarget on the (already settled) harness window, read directly.
            var previousActiveHost = ReactorApp.ActiveHostInternal;
            var target = new Border();
            H.SetContent(target);
            int shapeItemCleanedUp = 0;
            int templatedItemCleanedUp = 0;
            int popupContentCleanedUp = 0;
            int visualizerContentMounts = 0;
            int visualizerContentCleanups = 0;
            int wrapperMounts = 0;
            int nestedInnerMounts = 0;
            int nestedOuterMounts = 0;
            try
            {
                // Disposed at the end of this block, before finally restores the active host.
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                using var isolation = OnlyTheseHostsListen(host);
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
                    // Virtualized lists whose item array is kept: their handlers realize items in
                    // containers and skip work when the array reference is unchanged.
                    var lvItems = ctx.UseMemo<Element[]>(
                        () => [TextBlock("LvItemProbe").Foreground(Theme.Ref(AppKey)), TextBlock("LvSecond")]);
                    var lazyItems = ctx.UseMemo<IReadOnlyList<string>>(() => ["lazy"]);
                    var templatedItems = ctx.UseMemo<IReadOnlyList<string>>(() => ["templated"]);
                    var treeNodes = ctx.UseMemo<TreeViewNodeData[]>(
                        () => [new TreeViewNodeData("TreeRoot", [new TreeViewNodeData("TreeChild")])]);
                    var gvItems = ctx.UseMemo<Element[]>(
                        () => [TextBlock("GvItemProbe").Foreground(Theme.Ref(AppKey))]);
                    var gapItems = ctx.UseMemo<Element[]>(
                        () => [
                            Empty(),
                            Memo("emptyItem", () => Empty()),
                            TextBlock("GapItemProbe").Foreground(Theme.Ref(AppKey)),
                            // Changes shape with the resource: the replaced item must be unmounted.
                            Memo("shapeItem", () => ThemeRef.Resolve(AppKey, isDark: false) is SolidColorBrush { Color: var c } && c == Colors.Red
                                ? RenderEachTime(fctx =>
                                {
                                    fctx.UseEffect(() => () => shapeItemCleanedUp++);
                                    return TextBlock("ShapeItemBefore");
                                })
                                : TextBlock("ShapeItemAfter")),
                        ]);
                    return VStack(
                        TextBlock($"tick:{tick}"),
                        memo,
                        Memo("keyed", () => TextBlock("KeyedMemoProbe").Foreground(Theme.Ref(AppKey))),
                        // A concrete brush resolved eagerly inside the factory: equal on both
                        // sides of a self-diff, so only a diff against the mounted output writes it.
                        Memo("resolved", () => TextBlock("ResolvedMemoProbe").Foreground(ThemeRef.Resolve(AppKey, isDark: false)!)),
                        // Nested keyed memos share one realized control.
                        // A factory whose output changes shape with the resource.
                        Memo("shape", () => ThemeRef.Resolve(AppKey, isDark: false) is SolidColorBrush { Color: var c } && c == Colors.Red
                            ? TextBlock("ShapeProbeBefore")
                            : Border(TextBlock("ShapeProbeAfter"))),
                        // Items that realize no ComboBox entry (Empty, a memo of Empty), ahead of a themed one.
                        ComboBox(gapItems, default, null),
                        ListView(lvItems),
                        GridView(gvItems),
                        TreeView(treeNodes),
                        // Not memoized: the DSL builds fresh but equivalent node arrays every render.
                        TreeView(new TreeViewNodeData("FreshRoot", [new TreeViewNodeData("FreshChild")])),
                        // Node content that switches to and from Empty with the resource.
                        ContentTogglingTree(AppKey),
                        // Wrapper modifiers on a memo whose output changes shape: the remounted control
                        // must get them with mount semantics.
                        Memo("wrapperShape", () => ThemeRef.Resolve(AppKey, isDark: false) is SolidColorBrush { Color: var c } && c == Colors.Red
                                ? TextBlock("WrapperBefore")
                                : Border(TextBlock("WrapperAfter")))
                            .Margin(7)
                            .OnMount(_ => wrapperMounts++),
                        // The same through a nested chain: both wrappers' modifiers apply to the fresh
                        // control with mount semantics.
                        Memo("nestedWrapOuter", () => Memo("nestedWrapInner",
                                () => ThemeRef.Resolve(AppKey, isDark: false) is SolidColorBrush { Color: var c } && c == Colors.Red
                                    ? TextBlock("NestedWrapBefore")
                                    : Border(TextBlock("NestedWrapAfter")))
                                .Opacity(0.5)
                                .OnMount(_ => nestedInnerMounts++))
                            .Margin(5)
                            .OnMount(_ => nestedOuterMounts++),
                        // A hosted slot that swaps an update's replacement in without unmounting the
                        // old control: the refresh must unmount it itself.
                        Popup(Memo("popupShape",
                            () => ThemeRef.Resolve(AppKey, isDark: false) is SolidColorBrush { Color: var c } && c == Colors.Red
                                ? RenderEachTime(fctx =>
                                {
                                    fctx.UseEffect(() => () => popupContentCleanedUp++);
                                    return TextBlock("PopupBefore");
                                })
                                : TextBlock("PopupAfter"))),
                        // A popup child whose update always hands back a fresh control: each outgoing
                        // subtree must be unmounted, so exactly one copy of its content stays live.
                        Popup(ValidationVisualizerDsl.ValidationVisualizer(VisualizerStyle.Inline,
                            RenderEachTime(fctx =>
                            {
                                fctx.UseEffect(() =>
                                {
                                    visualizerContentMounts++;
                                    return () => visualizerContentCleanups++;
                                });
                                return TextBlock("VisualizerProbe");
                            }))),
                        // A templated-list item that changes shape with the resource: the replaced
                        // item must be unmounted.
                        ListView(templatedItems, static s => s, (_, _) => Memo("templatedShape",
                            () => ThemeRef.Resolve(AppKey, isDark: false) is SolidColorBrush { Color: var c } && c == Colors.Red
                                ? RenderEachTime(fctx =>
                                {
                                    fctx.UseEffect(() => () => templatedItemCleanedUp++);
                                    return TextBlock("TemplatedBefore");
                                })
                                : TextBlock("TemplatedAfter"))),
                        LazyVStack(lazyItems, static s => s,
                            static (_, _) => TextBlock("LazyProbe").Foreground(Theme.Ref(AppKey))),
                        Memo("outer", () => Memo("inner",
                            () => TextBlock("NestedMemoProbe").Foreground(ThemeRef.Resolve(AppKey, isDark: false)!))),
                        ComboBox(listItems, default, null));
                });
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_InitialRed", ProbeColor(target) == Colors.Red, $"color={ProbeColor(target)}");
                // List containers realize during layout; wait for them before editing the resource.
                bool listsRealized = await Harness.WaitFor(
                    () => ProbeColor(target, "LvItemProbe") == Colors.Red && ProbeColor(target, "GvItemProbe") == Colors.Red
                          && ProbeColor(target, "LazyProbe") == Colors.Red && FindText(target, "TemplatedBefore") is not null,
                    maxPasses: 20, perPassMs: 20);
                H.Check("ThemeMemo_ListsInitialRed", listsRealized,
                    $"lv={ProbeColor(target, "LvItemProbe")} gv={ProbeColor(target, "GvItemProbe")}");
                // As DataGrid does while scrolling: the factory skips refreshing realized rows.
                var lazyFactory = FindDescendant<ItemsRepeater>(target)?.ItemTemplate as ElementFactory<string>;
                H.Check("ThemeMemo_LazyScrollGuardInstalled", lazyFactory is not null);
                if (lazyFactory is not null) lazyFactory.ShouldSkipRefresh = static () => true;

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_Rerendered", FindText(target, "tick:1") is not null);
                H.Check("ThemeMemo_RerenderAloneIsStale", ProbeColor(target) == Colors.Red, $"color={ProbeColor(target)}");
                H.Check("ThemeMemo_ListViewItemRerenderAloneIsStale", ProbeColor(target, "LvItemProbe") == Colors.Red, $"color={ProbeColor(target, "LvItemProbe")}");
                H.Check("ThemeMemo_GridViewItemRerenderAloneIsStale", ProbeColor(target, "GvItemProbe") == Colors.Red, $"color={ProbeColor(target, "GvItemProbe")}");
                H.Check("ThemeMemo_LazyRowRerenderAloneIsStale", ProbeColor(target, "LazyProbe") == Colors.Red, $"color={ProbeColor(target, "LazyProbe")}");
                // A user expansion, which rebuilding the TreeView's nodes would reset.
                var treeView = FindTreeView(target, "TreeRoot");
                var rootNode = treeView?.RootNodes[0];
                if (rootNode is not null) rootNode.IsExpanded = true;
                var freshTreeView = FindTreeView(target, "FreshRoot");
                var freshRoot = freshTreeView?.RootNodes[0];
                if (freshRoot is not null) freshRoot.IsExpanded = true;
                // A user selection, which an ItemsSource swap would clear.
                var listView = FindDescendant<ListView>(target);
                if (listView is not null) listView.SelectedIndex = 1;
                H.Check("ThemeMemo_ShapeRerenderAloneIsStale", FindText(target, "ShapeProbeBefore") is not null);
                H.Check("ThemeMemo_WrapperRerenderAloneMountedOnce", wrapperMounts == 1, $"mounts={wrapperMounts}");
                H.Check("ThemeMemo_NestedWrapperRerenderAloneMountedOnce", nestedInnerMounts == 1 && nestedOuterMounts == 1,
                    $"inner={nestedInnerMounts} outer={nestedOuterMounts}");
                H.Check("ThemeMemo_PopupContentRerenderAloneKept", popupContentCleanedUp == 0);
                H.Check("ThemeMemo_VisualizerRerenderOneLiveCopy", visualizerContentMounts >= 2 && visualizerContentMounts - visualizerContentCleanups == 1,
                    $"mounts={visualizerContentMounts} cleanups={visualizerContentCleanups}");
                H.Check("ThemeMemo_TreeContentRerenderAloneUnchanged", ToggleTree(target) is { } toggleBefore
                    && toggleBefore.RootNodes[0].Content is UIElement && toggleBefore.RootNodes[1].Content is null,
                    $"first={ToggleTree(target)?.RootNodes[0].Content?.GetType().Name ?? "null"} second={ToggleTree(target)?.RootNodes[1].Content?.GetType().Name ?? "null"}");
                H.Check("ThemeMemo_TemplatedItemRerenderAloneKeepsIt", templatedItemCleanedUp == 0 && FindText(target, "TemplatedBefore") is not null,
                    $"cleanedUp={templatedItemCleanedUp} before={FindText(target, "TemplatedBefore") is not null}");
                H.Check("ThemeMemo_ShapeItemRerenderAloneKeepsIt", shapeItemCleanedUp == 0 && !HasComboText(target, "ShapeItemAfter"));
                H.Check("ThemeMemo_GapItemRerenderAloneIsStale", ListItemColor(target, "GapItemProbe") == Colors.Red, $"color={ListItemColor(target, "GapItemProbe")}");
                H.Check("ThemeMemo_NestedMemoRerenderAloneIsStale", ProbeColor(target, "NestedMemoProbe") == Colors.Red, $"color={ProbeColor(target, "NestedMemoProbe")}");
                H.Check("ThemeMemo_ResolvedMemoRerenderAloneIsStale", ProbeColor(target, "ResolvedMemoProbe") == Colors.Red, $"color={ProbeColor(target, "ResolvedMemoProbe")}");
                H.Check("ThemeMemo_KeyedMemoRerenderAloneIsStale", ProbeColor(target, "KeyedMemoProbe") == Colors.Red, $"color={ProbeColor(target, "KeyedMemoProbe")}");
                H.Check("ThemeMemo_ListItemRerenderAloneIsStale", ListItemColor(target) == Colors.Red, $"color={ListItemColor(target)}");

                await Task.Run(Theme.NotifyResourcesChanged);
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_NotifiedFromBackgroundIsBlue", ProbeColor(target) == Colors.Blue, $"color={ProbeColor(target)}");
                H.Check("ThemeMemo_ListViewItemNotifiedIsBlue", ProbeColor(target, "LvItemProbe") == Colors.Blue, $"color={ProbeColor(target, "LvItemProbe")}");
                H.Check("ThemeMemo_GridViewItemNotifiedIsBlue", ProbeColor(target, "GvItemProbe") == Colors.Blue, $"color={ProbeColor(target, "GvItemProbe")}");
                H.Check("ThemeMemo_LazyRowNotifiedMidScrollIsBlue", ProbeColor(target, "LazyProbe") == Colors.Blue, $"color={ProbeColor(target, "LazyProbe")}");
                H.Check("ThemeMemo_TreeViewNodesKept", NodeKept(treeView, rootNode), DescribeNode(treeView, rootNode));
                var wrapperBorder = FindText(target, "WrapperAfter")?.Parent as Border;
                H.Check("ThemeMemo_WrapperRemountKeepsModifiers", wrapperBorder?.Margin == new Thickness(7),
                    $"margin={wrapperBorder?.Margin.ToString() ?? "(not found)"}");
                H.Check("ThemeMemo_WrapperRemountRunsOnMount", wrapperMounts == 2, $"mounts={wrapperMounts}");
                var nestedBorder = FindText(target, "NestedWrapAfter")?.Parent as Border;
                H.Check("ThemeMemo_NestedWrapperRemountKeepsModifiers",
                    nestedBorder is not null && nestedBorder.Margin == new Thickness(5) && Math.Abs(nestedBorder.Opacity - 0.5) < 0.001,
                    $"margin={nestedBorder?.Margin.ToString() ?? "(not found)"} opacity={nestedBorder?.Opacity}");
                H.Check("ThemeMemo_NestedWrapperRemountRunsOnMount", nestedInnerMounts == 2 && nestedOuterMounts == 2,
                    $"inner={nestedInnerMounts} outer={nestedOuterMounts}");
                H.Check("ThemeMemo_PopupContentReplacedAndUnmounted", popupContentCleanedUp == 1, $"cleanups={popupContentCleanedUp}");
                H.Check("ThemeMemo_VisualizerNotifiedOneLiveCopy", visualizerContentMounts >= 3 && visualizerContentMounts - visualizerContentCleanups == 1,
                    $"mounts={visualizerContentMounts} cleanups={visualizerContentCleanups}");
                H.Check("ThemeMemo_TreeContentVisibleToEmpty", ToggleTree(target)?.RootNodes[0].Content is null,
                    $"content={ToggleTree(target)?.RootNodes[0].Content?.GetType().Name ?? "null"}");
                H.Check("ThemeMemo_TreeContentEmptyToVisible", ToggleTree(target)?.RootNodes[1].Content is UIElement,
                    $"content={ToggleTree(target)?.RootNodes[1].Content?.GetType().Name ?? "null"}");
                H.Check("ThemeMemo_FreshTreeViewNodesKept", NodeKept(freshTreeView, freshRoot), DescribeNode(freshTreeView, freshRoot));
                bool templatedAfterShown = await Harness.WaitFor(() => FindText(target, "TemplatedAfter") is not null, maxPasses: 20, perPassMs: 20);
                H.Check("ThemeMemo_TemplatedItemReplacedAndUnmounted", templatedItemCleanedUp == 1 && templatedAfterShown,
                    $"cleanedUp={templatedItemCleanedUp} after={FindText(target, "TemplatedAfter") is not null}");
                H.Check("ThemeMemo_ListViewSelectionKept", listView?.SelectedIndex == 1, $"selected={listView?.SelectedIndex}");
                H.Check("ThemeMemo_ShapeChangeRemounts", FindText(target, "ShapeProbeAfter") is not null && FindText(target, "ShapeProbeBefore") is null);
                H.Check("ThemeMemo_ShapeItemReplacedAndUnmounted", shapeItemCleanedUp == 1 && HasComboText(target, "ShapeItemAfter"),
                    $"cleanedUp={shapeItemCleanedUp} after={HasComboText(target, "ShapeItemAfter")}");
                H.Check("ThemeMemo_GapItemNotifiedIsBlue", ListItemColor(target, "GapItemProbe") == Colors.Blue, $"color={ListItemColor(target, "GapItemProbe")}");
                H.Check("ThemeMemo_NestedMemoNotifiedIsBlue", ProbeColor(target, "NestedMemoProbe") == Colors.Blue, $"color={ProbeColor(target, "NestedMemoProbe")}");
                H.Check("ThemeMemo_ResolvedMemoNotifiedIsBlue", ProbeColor(target, "ResolvedMemoProbe") == Colors.Blue, $"color={ProbeColor(target, "ResolvedMemoProbe")}");
                H.Check("ThemeMemo_KeyedMemoNotifiedIsBlue", ProbeColor(target, "KeyedMemoProbe") == Colors.Blue, $"color={ProbeColor(target, "KeyedMemoProbe")}");
                H.Check("ThemeMemo_ListItemNotifiedIsBlue", ListItemColor(target) == Colors.Blue, $"color={ListItemColor(target)}");

                // A second edit, notified from the UI thread: the call is repeatable.
                // <snippet:runtime-resource-edit>
                resources[AppKey] = new SolidColorBrush(Colors.Green);
                Theme.NotifyResourcesChanged();
                // </snippet:runtime-resource-edit>
                await host.WaitForIdleAsync();
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
        private static global::Windows.UI.Color? ListItemColor(Border target, string probe = "ListItemProbe")
            => (FindComboBoxes(target).SelectMany(cb => cb.Items.OfType<TextBlock>()).FirstOrDefault(t => t.Text == probe)
                    ?.Foreground as SolidColorBrush)?.Color;

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T match) return match;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (FindDescendant<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            }
            return null;
        }

        // Legacy ContentElement nodes (deprecated, still supported): the first node has content
        // while the resource is red and is Empty after; the second the other way round.
#pragma warning disable CS0618
        private static Element ContentTogglingTree(string resourceKey)
        {
            bool red = ThemeRef.Resolve(resourceKey, isDark: false) is SolidColorBrush { Color: var c } && c == Colors.Red;
            return TreeView(
                new TreeViewNodeData("ToggleA") { ContentElement = red ? TextBlock("ToggleAContent") : Empty() },
                new TreeViewNodeData("ToggleB") { ContentElement = red ? Empty() : TextBlock("ToggleBContent") });
        }
#pragma warning restore CS0618

        private static TreeView? ToggleTree(DependencyObject root)
        {
            if (root is TreeView { RootNodes.Count: 2 } tv) return tv;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (ToggleTree(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            }
            return null;
        }

        // The TreeView whose first root node shows text, found by walking every TreeView.
        private static TreeView? FindTreeView(DependencyObject root, string rootText)
        {
            if (root is TreeView tv && tv.RootNodes.Count > 0
                && tv.RootNodes[0].Content is TreeViewNodeData { Content: var text } && text == rootText)
                return tv;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (FindTreeView(VisualTreeHelper.GetChild(root, i), rootText) is { } found) return found;
            }
            return null;
        }

        // The same node instance is still the tree's root and still expanded: not rebuilt.
        private static bool NodeKept(TreeView? tree, TreeViewNode? node)
            => tree is { RootNodes.Count: 1 } && node is not null
               && ReferenceEquals(tree.RootNodes[0], node) && node.IsExpanded;

        private static string DescribeNode(TreeView? tree, TreeViewNode? node)
            => $"tree={tree is not null} node={node is not null} same={tree is { RootNodes.Count: > 0 } && ReferenceEquals(tree.RootNodes[0], node)} expanded={node?.IsExpanded}";

        private static bool HasComboText(Border target, string text)
            => FindComboBoxes(target).SelectMany(cb => cb.Items.OfType<TextBlock>()).Any(t => t.Text == text);

        private static IEnumerable<ComboBox> FindComboBoxes(DependencyObject root)
        {
            if (root is ComboBox cb) yield return cb;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                foreach (var found in FindComboBoxes(VisualTreeHelper.GetChild(root, i))) yield return found;
            }
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