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
                    .Resources(r => r
                        .Set("ProbeBrush", Theme.Ref(AppKey))
                        // A literal and an unresolvable ThemeRef for one key: the literal stays.
                        .Set("MixedBrush", "#FF00FF00")
                        .Set("MixedBrush", Theme.Ref("ReactorSelfTestNoSuchBrush")));
            });
            H.SetContent(new Border { Child = host });
            using var isolation = OnlyTheseHostsListen(host);
            try
            {
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_InitialRed", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");
                H.Check("ThemeNotify_MixedLiteralKeptAtMount", ProbeColor(host, "MixedBrush") == Colors.Lime, $"color={ProbeColor(host, "MixedBrush")}");

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_RerenderAloneIsStale", ProbeColor(host) == Colors.Red, $"color={ProbeColor(host)}");

                Theme.NotifyResourcesChanged();
                await host.WaitForIdleAsync();
                H.Check("ThemeNotify_NotifiedIsBlue", ProbeColor(host) == Colors.Blue, $"color={ProbeColor(host)}");
                H.Check("ThemeNotify_MixedLiteralKeptOnRefresh", ProbeColor(host, "MixedBrush") == Colors.Lime, $"color={ProbeColor(host, "MixedBrush")}");

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

        private static global::Windows.UI.Color? ProbeColor(ReactorHostControl host, string key = "ProbeBrush")
            => host.Content is Border b
               && b.Resources is { } res
               && res.TryGetValue(key, out var value)
               && value is SolidColorBrush brush
                ? brush.Color
                : null;
    }

    /// <summary>
    /// A notification from another thread that finds the window host mid-render must not be lost
    /// when that render pass finishes between RequestRender's read of _isRendering and its flag
    /// write. The test hook runs in exactly that window and lets the pass (and its RenderLoop
    /// tail) complete before RequestRender continues; the refresh must still render.
    /// </summary>
    internal class NotifyDuringRenderPassSchedulesRender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previousActiveHost = ReactorApp.ActiveHostInternal;
            var target = new Border();
            H.SetContent(target);
            var dispatcher = global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            int renders = 0;
            bool holdNextRender = false;
            bool hookRanMidRender = false;
            using var renderStarted = new SemaphoreSlim(0);
            using var checkedRendering = new SemaphoreSlim(0);
            using var passFinished = new SemaphoreSlim(0);
            try
            {
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                using var isolation = OnlyTheseHostsListen(host);
                Action<int>? setTick = null;
                host.Mount(ctx =>
                {
                    var (tick, set) = ctx.UseState(0);
                    setTick = set;
                    renders++;
                    if (holdNextRender)
                    {
                        holdNextRender = false;
                        renderStarted.Release();
                        // Hold this pass until the other thread has read _isRendering.
                        hookRanMidRender = checkedRendering.Wait(TimeSpan.FromSeconds(5));
                    }
                    return TextBlock($"race:{tick}");
                });
                await host.WaitForIdleAsync();

                ReactorHost.RequestRenderAfterRenderingCheckForTest = caller =>
                {
                    if (!ReferenceEquals(caller, host) || dispatcher.HasThreadAccess) return;
                    checkedRendering.Release();
                    // Queued behind the rest of the in-flight pass; lets it finish first.
                    dispatcher.TryEnqueue(global::Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => passFinished.Release());
                    passFinished.Wait(TimeSpan.FromSeconds(5));
                };

                holdNextRender = true;
                setTick!(1);
                var notify = Task.Run(async () =>
                {
                    if (await renderStarted.WaitAsync(TimeSpan.FromSeconds(5)))
                        Theme.NotifyResourcesChanged();
                });
                await host.WaitForIdleAsync();
                await notify;
                ReactorHost.RequestRenderAfterRenderingCheckForTest = null;
                await host.WaitForIdleAsync();

                H.Check("RenderRace_HookRanMidRender", hookRanMidRender);
                // Mount, the tick render, and the refresh the notification asked for.
                H.Check("RenderRace_NotifyDuringPassStillRenders", renders == 3 && host.IsIdle,
                    $"renders={renders} idle={host.IsIdle}");
            }
            finally
            {
                ReactorHost.RequestRenderAfterRenderingCheckForTest = null;
                ReactorApp.ActiveHostInternal = previousActiveHost;
                H.SetContent(null);
            }
        }
    }
    /// <summary>
    /// The window <see cref="ReactorHost"/> path and a background-thread call. A memoized subtree
    /// is skipped wholesale by a plain re-render (a reference-equal UseMemo'd element, a keyed
    /// <c>Memo(key, ...)</c> with an unchanged key, ListView/GridView/ComboBox items whose list is
    /// kept, an unchanged TreeView), so its <c>Theme.Ref</c> values are never applied again. The
    /// resource-refresh pass must walk into each of them, without remounting anything (component
    /// state, list selection and tree expansion survive). "RerenderAloneIsStale" is the negative
    /// control: an ordinary re-render does not pick the edit up.
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
            // Mount/cleanup counts of stateful components inside nested memos: a refresh that
            // remounted them would lose their state.
            var sameKey = new Counter();
            var legacyNested = new Counter();
            var flyoutTarget = new Counter();
            // Hosted and transparent-wrapper slots whose child comes back as a different control.
            var replacedTarget = new Counter();
            var popupContent = new Counter();
            int visualizerWrapperMounts = 0;
            int flyoutWrapperMounts = 0;
            // A FlipView item object changed in place, then shown again in a fresh array.
            var flipItem = new FlipItem { Name = "flip-before" };
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
                    var listItems = ctx.UseMemo<Element[]>(
                        () => [TextBlock("ListItemProbe").Foreground(Theme.Ref(AppKey))]);
                    // Items that realize no ComboBox entry (Empty, a memo of Empty), ahead of a themed one.
                    var gapItems = ctx.UseMemo<Element[]>(
                        () => [Empty(), Memo("emptyItem", () => Empty()), TextBlock("GapItemProbe").Foreground(Theme.Ref(AppKey))]);
                    var lvItems = ctx.UseMemo<Element[]>(
                        () => [TextBlock("LvItemProbe").Foreground(Theme.Ref(AppKey)), TextBlock("LvSecond")]);
                    var flipGapItems = ctx.UseMemo<IReadOnlyList<string>>(() => ["gap", "shown"]);
                    IReadOnlyList<FlipItem> flipMutable = [flipItem];
                    var flipGapItemsHeld = ctx.UseMemo<IReadOnlyList<string>>(() => ["held"]);
                    var gvItems = ctx.UseMemo<Element[]>(
                        () => [TextBlock("GvItemProbe").Foreground(Theme.Ref(AppKey))]);
                    var lazyItems = ctx.UseMemo<IReadOnlyList<string>>(() => ["lazy"]);
                    var treeNodes = ctx.UseMemo<TreeViewNodeData[]>(
                        () => [new TreeViewNodeData("TreeRoot", [new TreeViewNodeData("TreeChild")])]);
                    return VStack(
                        TextBlock($"tick:{tick}"),
                        memo,
                        // Resolved during Render, which the refresh re-runs: picks the edit up too.
                        TextBlock("RenderResolvedProbe").Foreground(ThemeRef.Resolve(AppKey, isDark: false)!),
                        Memo("keyed", () => TextBlock("KeyedMemoProbe").Foreground(Theme.Ref(AppKey))),
                        Memo("outer", () => Memo("inner", () => TextBlock("NestedMemoProbe").Foreground(Theme.Ref(AppKey)))),
                        // Nested memos share one control. Their components keep their state.
                        Memo("sameKey", () => Memo("sameKey", () => sameKey.Probe("SameKeyProbe", AppKey))),
                        Memo("legacyNestedOuter", () => new ModifiedElement(
                            Memo("legacyNestedInner", () => legacyNested.Probe("LegacyNestedProbe", AppKey)),
                            new ElementModifiers { Margin = new Thickness(13) })),
                        Memo("flyoutOuter", () => Flyout(
                            Memo("flyoutInner", () => flyoutTarget.Probe("FlyoutTargetProbe", AppKey)),
                            TextBlock("FlyoutTip"))),
                        // A memo whose output's handler always remounts (ValidationVisualizer): the new
                        // control gets the memo's modifiers and OnMount, as at mount.
                        Memo("visualizerWrapper", () => ValidationVisualizerDsl.ValidationVisualizer(VisualizerStyle.Inline,
                                TextBlock("VisualizerWrapProbe")))
                            .Margin(9)
                            .OnMount(_ => visualizerWrapperMounts++),
                        // A flyout whose target changes type on an ordinary render: the old target is torn
                        // down exactly once, and the flyout's own modifiers reach the new one.
                        Flyout(tick == 0 ? replacedTarget.Probe("ReplacedTargetBefore", AppKey) : Border(TextBlock("ReplacedTargetAfter")),
                                TextBlock("ReplacedTargetTip"))
                            .Margin(19)
                            .OnMount(_ => flyoutWrapperMounts++),
                        // A popup child whose update always builds a fresh control: one live copy.
                        Popup(ValidationVisualizerDsl.ValidationVisualizer(VisualizerStyle.Inline,
                            popupContent.Probe("PopupContentProbe", AppKey))),
                        FlipView(flipMutable, static i => "only", static (i, _) => TextBlock(i.Name)),
                        // In its own panel, so removing it doesn't shift the outer positional diff.
                        VStack(tick < 2 ? FlipView(flipGapItemsHeld, static s => s, static (_, _) => TextBlock("HeldFlipProbe")) : Empty()),
                        // A FlipView whose first item mounts nothing.
                        FlipView(flipGapItems, static s => s, static (s, _) => s == "gap"
                            ? Empty()
                            : TextBlock("FlipGapProbe").Foreground(Theme.Ref(AppKey))),
                        ComboBox(gapItems, default, null),
                        ComboBox(listItems, default, null),
                        ListView(lvItems),
                        GridView(gvItems),
                        TreeView(treeNodes),
                        // Not memoized: the DSL builds fresh but equivalent node arrays every render.
                        TreeView(new TreeViewNodeData("FreshRoot", [new TreeViewNodeData("FreshChild")])),
                        // Node content that switches to and from Empty with the resource.
                        ContentTogglingTree(AppKey),
                        LazyVStack(lazyItems, static s => s,
                            static (_, _) => TextBlock("LazyProbe").Foreground(Theme.Ref(AppKey))));
                });
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_InitialRed", ProbeColor(target) == Colors.Red, $"color={ProbeColor(target)}");
                // List containers realize during layout; wait for them before editing the resource.
                bool listsRealized = await Harness.WaitFor(
                    () => ProbeColor(target, "LvItemProbe") == Colors.Red && ProbeColor(target, "GvItemProbe") == Colors.Red
                          && ProbeColor(target, "LazyProbe") == Colors.Red,
                    maxPasses: 20, perPassMs: 20);
                H.Check("ThemeMemo_ListsInitialRed", listsRealized,
                    $"lv={ProbeColor(target, "LvItemProbe")} gv={ProbeColor(target, "GvItemProbe")}");
                // As DataGrid does while scrolling: the factory skips refreshing realized rows.
                var lazyFactory = FindDescendant<ItemsRepeater>(target)?.ItemTemplate as ElementFactory<string>;
                H.Check("ThemeMemo_LazyScrollGuardInstalled", lazyFactory is not null);
                if (lazyFactory is not null) lazyFactory.ShouldSkipRefresh = static () => true;

                resources[AppKey] = new SolidColorBrush(Colors.Blue);
                flipItem.Name = "flip-after";
                setTick!(1);
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_Rerendered", FindText(target, "tick:1") is not null);
                H.Check("Replace_FlipViewItemChangedInPlaceUpdates", FlipViewHasText(target, "flip-after") && !FlipViewHasText(target, "flip-before"));
                var replacedBorder = FindText(target, "ReplacedTargetAfter")?.Parent as Border;
                H.Check("Replace_FlyoutTargetTornDownOnce", replacedTarget.Mounts == 1 && replacedTarget.Cleanups == 1,
                    $"mounts={replacedTarget.Mounts} cleanups={replacedTarget.Cleanups}");
                H.Check("Replace_FlyoutModifiersReachNewTarget", replacedBorder?.Margin == new Thickness(19) && flyoutWrapperMounts == 2,
                    $"margin={replacedBorder?.Margin.ToString() ?? "(not found)"} mounts={flyoutWrapperMounts}");
                H.Check("Replace_PopupContentOneLiveCopy", popupContent.Mounts >= 2 && popupContent.Mounts - popupContent.Cleanups == 1,
                    $"mounts={popupContent.Mounts} cleanups={popupContent.Cleanups}");
                H.Check("ThemeMemo_VisualizerWrapperRerenderAloneMountedOnce", visualizerWrapperMounts == 1, $"mounts={visualizerWrapperMounts}");
                // A user expansion and selection, which rebuilding the TreeView or swapping the
                // ListView's ItemsSource would reset.
                var treeView = FindTreeView(target, "TreeRoot");
                var rootNode = treeView?.RootNodes[0];
                if (rootNode is not null) rootNode.IsExpanded = true;
                var freshTreeView = FindTreeView(target, "FreshRoot");
                var freshRoot = freshTreeView?.RootNodes[0];
                if (freshRoot is not null) freshRoot.IsExpanded = true;
                var listView = FindDescendant<ListView>(target);
                if (listView is not null) listView.SelectedIndex = 1;

                var stale = new[]
                {
                    "ThemeMemoProbe", "KeyedMemoProbe", "NestedMemoProbe", "SameKeyProbe", "LegacyNestedProbe",
                    "FlyoutTargetProbe", "LvItemProbe", "GvItemProbe", "LazyProbe",
                };
                foreach (var probe in stale)
                    H.Check($"ThemeMemo_{probe}RerenderAloneIsStale", ProbeColor(target, probe) == Colors.Red, $"color={ProbeColor(target, probe)}");
                H.Check("ThemeMemo_ListItemRerenderAloneIsStale", ListItemColor(target) == Colors.Red, $"color={ListItemColor(target)}");
                H.Check("ThemeMemo_GapItemRerenderAloneIsStale", ListItemColor(target, "GapItemProbe") == Colors.Red);
                H.Check("ThemeMemo_TreeContentRerenderAloneUnchanged", ToggleTree(target) is { } toggleBefore
                    && toggleBefore.RootNodes[0].Content is UIElement && toggleBefore.RootNodes[1].Content is null);

                await Task.Run(Theme.NotifyResourcesChanged);
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_NotifiedFromBackgroundIsBlue", ProbeColor(target) == Colors.Blue, $"color={ProbeColor(target)}");
                foreach (var probe in stale.Append("RenderResolvedProbe"))
                    H.Check($"ThemeMemo_{probe}NotifiedIsBlue", ProbeColor(target, probe) == Colors.Blue, $"color={ProbeColor(target, probe)}");
                H.Check("ThemeMemo_ListItemNotifiedIsBlue", ListItemColor(target) == Colors.Blue, $"color={ListItemColor(target)}");
                H.Check("ThemeMemo_FlipGapNotifiedIsBlue", FlipViewItemColor(target, "FlipGapProbe") == Colors.Blue);
                H.Check("ThemeMemo_VisualizerWrapperRemountRunsOnMount", visualizerWrapperMounts == 2
                    && AncestorMargin(FindText(target, "VisualizerWrapProbe"), new Thickness(9)),
                    $"mounts={visualizerWrapperMounts} margin={AncestorMargin(FindText(target, "VisualizerWrapProbe"), new Thickness(9))}");
                H.Check("ThemeMemo_GapItemNotifiedIsBlue", ListItemColor(target, "GapItemProbe") == Colors.Blue);
                foreach (var (name, counter) in new[] { ("SameKey", sameKey), ("LegacyNested", legacyNested), ("FlyoutTarget", flyoutTarget) })
                    H.Check($"ThemeMemo_{name}KeptNotRemounted", counter.Mounts == 1 && counter.Cleanups == 0,
                        $"mounts={counter.Mounts} cleanups={counter.Cleanups}");
                H.Check("ThemeMemo_LegacyNestedKeepsModifiers", AncestorMargin(FindText(target, "LegacyNestedProbe"), new Thickness(13)));
                H.Check("ThemeMemo_TreeViewNodesKept", NodeKept(treeView, rootNode), DescribeNode(treeView, rootNode));
                H.Check("ThemeMemo_FreshTreeViewNodesKept", NodeKept(freshTreeView, freshRoot), DescribeNode(freshTreeView, freshRoot));
                H.Check("ThemeMemo_TreeContentVisibleToEmpty", ToggleTree(target)?.RootNodes[0].Content is null);
                H.Check("ThemeMemo_TreeContentEmptyToVisible", ToggleTree(target)?.RootNodes[1].Content is UIElement);
                H.Check("ThemeMemo_ListViewSelectionKept", listView?.SelectedIndex == 1, $"selected={listView?.SelectedIndex}");

                // A second edit, notified from the UI thread: the call is repeatable.
                // <snippet:runtime-resource-edit>
                resources[AppKey] = new SolidColorBrush(Colors.Green);
                Theme.NotifyResourcesChanged();
                // </snippet:runtime-resource-edit>
                await host.WaitForIdleAsync();
                H.Check("ThemeMemo_SecondEditIsGreen", ProbeColor(target) == Colors.Green, $"color={ProbeColor(target)}");

                // A FlipView an app holds after it leaves the tree does not keep its item elements.
                var heldFlip = FindFlipViewWithText(target, "HeldFlipProbe");
                bool flipRecorded = heldFlip is not null && Reconciler.TryGetReactorState(heldFlip, out var flipState)
                    && flipState.MountedItemElements is { Count: > 0 };
                setTick!(2);
                await host.WaitForIdleAsync();
                H.Check("Replace_UnmountedFlipViewDropsItemRecord", flipRecorded && FindFlipViewWithText(target, "HeldFlipProbe") is null
                    && !(Reconciler.TryGetReactorState(heldFlip!, out var heldState) && heldState.MountedItemElements is not null),
                    $"recorded={flipRecorded}");
            }
            finally
            {
                ReactorApp.ActiveHostInternal = previousActiveHost;
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }

        private sealed class FlipItem { public string Name = ""; }

        private static FlipView? FindFlipViewWithText(DependencyObject root, string text)
        {
            if (root is FlipView fv && fv.Items.OfType<TextBlock>().Any(t => t.Text == text)) return fv;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (FindFlipViewWithText(VisualTreeHelper.GetChild(root, i), text) is { } found) return found;
            }
            return null;
        }

        private static bool FlipViewHasText(DependencyObject root, string text)
        {
            if (root is FlipView fv && fv.Items.OfType<TextBlock>().Any(t => t.Text == text)) return true;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (FlipViewHasText(VisualTreeHelper.GetChild(root, i), text)) return true;
            }
            return false;
        }

        // A stateful component (an effect that runs once per mount) showing a themed TextBlock.
        private sealed class Counter
        {
            public int Mounts;
            public int Cleanups;

            public Element Probe(string text, string resourceKey) => RenderEachTime(ctx =>
            {
                ctx.UseEffect(() =>
                {
                    Mounts++;
                    return () => Cleanups++;
                });
                return TextBlock(text).Foreground(Theme.Ref(resourceKey));
            });
        }

        private static global::Windows.UI.Color? ProbeColor(Border target, string probe = "ThemeMemoProbe")
            => (FindText(target, probe)?.Foreground as SolidColorBrush)?.Color;

        // Read through ComboBox.Items: the item is the TextBlock itself, whether or not the
        // drop-down has ever been opened.
        private static global::Windows.UI.Color? ListItemColor(Border target, string probe = "ListItemProbe")
            => (FindComboBoxes(target).SelectMany(cb => cb.Items.OfType<TextBlock>()).FirstOrDefault(t => t.Text == probe)
                    ?.Foreground as SolidColorBrush)?.Color;

        // The item TextBlock with this text, in any FlipView under root.
        private static global::Windows.UI.Color? FlipViewItemColor(DependencyObject root, string text)
        {
            if (root is FlipView fv)
            {
                foreach (var item in fv.Items)
                {
                    if (item is TextBlock { Foreground: SolidColorBrush b } tb && tb.Text == text) return b.Color;
                }
            }
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (FlipViewItemColor(VisualTreeHelper.GetChild(root, i), text) is { } found) return found;
            }
            return null;
        }

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

        // Legacy ContentElement nodes (deprecated, still supported), read during Render: the first
        // node has content while the resource is red and is Empty after; the second the other way round.
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

        // Whether an element within a few levels above `fe` carries `margin`.
        private static bool AncestorMargin(FrameworkElement? fe, Thickness margin)
        {
            for (int i = 0; fe is not null && i < 4; i++, fe = VisualTreeHelper.GetParent(fe) as FrameworkElement)
            {
                if (fe.Margin == margin) return true;
            }
            return false;
        }
    }
}