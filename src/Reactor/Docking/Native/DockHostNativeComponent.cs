using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.16 / §2.17 — DockManager renderer (Reactor-native, no XAML).
//
//  The native registration mounts a Border whose Child is reconciled from
//  the element this component returns. Translates DockManager.Layout into:
//    DockSplit       → FlexElement + DockSplitterElement (§2.1)
//    DockTabGroup    → TabViewElement (§2.2)
//    DockableContent → its Content element (leaf)
//
//  The component owns:
//    • a stable DockHostModel instance — `UseRef`-cached so identity is
//      preserved across renders; only mount/unmount invalidates it. The
//      model's Root / sides / ActiveContent are synced from the immutable
//      element snapshot each render (controlled-input pattern; live
//      mutation will follow at §2.4 drag pipeline).
//    • per-DockSplit ratio state (ConditionalWeakTable keyed by node ref).
//
//  Context publication (§2.17): the rendered subtree is wrapped with
//  Provide(Host=model), Provide(ActivePaneKey=active key),
//  Provide(LayoutSnapshot=snapshot). Each pane's Content is further
//  wrapped with Provide(Pane=DockPaneInfo) so UsePane() resolves.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Props for <see cref="DockHostNativeComponent"/> — the immutable input
/// from the parent render pass. Equality on the inner element drives
/// <see cref="Component{TProps}.ShouldUpdate"/>.
/// </summary>
internal sealed record DockHostNativeProps(DockManager Manager);

internal sealed class DockHostNativeComponent : Component<DockHostNativeProps>
{
    public override Element Render()
    {
        var manager = Props.Manager;

        // ── Spec 045 §2.4 — drag pipeline state ───────────────────────────
        //
        // The drag pipeline is owned by the host component so the overlay
        // toggle + layout mutation can share state without re-routing
        // through the app. The override is a transparent shadow over
        // Manager.Layout: when set, it replaces the prop until the app
        // passes a new Layout reference (controlled-input pattern).
        //
        // The drag-active flag drives ShowDropTargets — apps don't need
        // to wire that explicitly to enable tab tear-out + dock-by-drop.
        var (layoutOverride, setLayoutOverride) = UseState<DockNode?>(null);
        var (dragActive, setDragActive) = UseState(false);
        var (hoveredTarget, setHoveredTarget) = UseState<DockTarget?>(null);
        var hoveredTargetRef = UseRef<DockTarget?>(null);
        hoveredTargetRef.Current = hoveredTarget;

        // The effective layout the renderer sees. Apps changing
        // Manager.Layout out-of-band will replace the prop; if the new
        // reference differs from our override, we surrender the override
        // (controlled-input convergence).
        var effectiveLayout = layoutOverride ?? manager.Layout;

        // Per-DockSplit ratio state. The store survives renders via UseRef
        // (state participates in equality and silently no-ops on
        // same-reference setters; refs don't).
        //
        // Keyed by **tree position path** (e.g. "0", "0/1", "0/1/0")
        // rather than DockSplit reference — apps typically rebuild
        // `Layout = new DockSplit(…)` inside Render(), so reference keys
        // get orphaned every frame and ratios snap back to bootstrap
        // each render. The path is stable for a stable tree shape; if
        // the app reorders panes, ratios reset at the touched positions,
        // which is the correct behavior anyway.
        //
        // A separate UseReducer tick supplies the re-render trigger
        // (mutating the ratio array in place doesn't change any
        // UseState-comparable value).
        //
        // SplitRatios escape hatch (spec 045 §2.1): when the app supplies
        // its own dictionary via DockManager.SplitRatios, use that. The
        // app's own state-change mechanism drives re-renders; the
        // internal tick is reserved for splitter-driven mutations.
        var ratioStoreRef = UseRef<Dictionary<string, double[]>>(new Dictionary<string, double[]>());
        var ratioStore = manager.SplitRatios ?? ratioStoreRef.Current;
        var (_, bumpTick) = UseReducer(0);
        void RequestRatioRerender() => bumpTick(t => t + 1);

        // Stable DockHostModel instance for the lifetime of this component
        // (§2.16). UseRef keeps the same model object across renders so
        // UseDockHost() consumers don't churn on each layout-prop change.
        var modelRef = UseRef<DockHostModel?>(null);
        var model = modelRef.Current ??= new DockHostModel();
        SyncModelFromElement(model, manager, effectiveLayout);

        var activeKey = manager.ActiveDocument?.Key;
        var snapshot = BuildSnapshot(model);

        // §2.4 — tab-drag callbacks fed to every DockTabGroup so any tab
        // in the layout can begin a session. Captures `manager` from the
        // current render closure for OnContentFloating/Floated event
        // routing.
        void HandleTabDragStarting(DockableContent pane, int tabIndex)
        {
            // Refuse a second concurrent drag — spec §4.6 single-drag
            // contract carried into P2.
            if (DockDragSession.Current is { IsActive: true }) return;
            var args = new DockContentFloatingEventArgs { Content = pane };
            manager.OnContentFloating?.Invoke(args);
            if (args.Cancel) return;
            DockDragSession.Begin(pane, manager, tabIndex);
            setDragActive(true);
        }

        void HandleTabDragCompleted(DockableContent pane, int tabIndex, bool wasOutside)
        {
            _ = tabIndex; // pane reference is the source of truth
            var session = DockDragSession.Current;
            if (session is null || !session.IsActive) return;

            // If the user released over a drop target, the overlay's
            // OnConfirm callback already fired and tore the session down;
            // we shouldn't double-handle here. The session.IsActive guard
            // covers that case.
            if (wasOutside)
            {
                // Tear-out: open a floating window with the dragged pane.
                // Pane has to be removed from the current layout first so
                // it doesn't appear in both places.
                var (afterRemove, removed) = DockLayoutMutator.RemovePane(effectiveLayout, pane);
                if (removed)
                {
                    setLayoutOverride(afterRemove);
                    try { DockFloatingWindow.Open(pane); }
                    catch { /* tear-out best-effort; surface via OnContentFloated */ }
                    manager.OnContentFloated?.Invoke(new DockContentFloatedEventArgs { Content = pane });
                }
            }

            session.End();
            setDragActive(false);
        }

        Element BuildNode(DockNode node, string path) => node switch
        {
            DockSplit split => RenderSplit(split, path, ratioStore, RequestRatioRerender, BuildNode),
            DockTabGroup grp => DockTabGroupRenderer.Render(
                grp,
                renderLeafContent: doc => WrapLeafWithPaneContext(doc),
                onSelectedIndexChanged: null,
                onTabClosing: null,
                onTabDragStarting: HandleTabDragStarting,
                onTabDragCompleted: HandleTabDragCompleted),
            DockableContent leaf => WrapLeafWithPaneContext(leaf),
            _ => new BorderElement(null),
        };

        Element body = effectiveLayout is null
            ? new BorderElement(null)
            : BuildNode(effectiveLayout, path: "0");

        // ── Side strips + side popup (§2.5). Elide entirely when no
        // sides are populated so the visual matches the P1 baseline for
        // layouts that don't pin. Otherwise compose strips + a shared
        // light-dismiss Popup overlay; click on a strip button toggles
        // expansion of the matching pane.
        var hasSides =
            (manager.LeftSide is { Count: > 0 }) ||
            (manager.TopSide is { Count: > 0 }) ||
            (manager.RightSide is { Count: > 0 }) ||
            (manager.BottomSide is { Count: > 0 });

        var (expandedSideKey, setExpandedSideKey) = UseState<object?>(null);

        Element composed = hasSides
            ? DockSideStripRenderer.Compose(manager, body, expandedSideKey, setExpandedSideKey)
            : body;

        // §2.3 — drop-target overlay. Composed last so it paints above the
        // dock subtree (Grid same-cell stacking ⇒ later children on top).
        // Two paths feed into showing it:
        //   • manager.ShowDropTargets — app/test escape hatch (e.g. Scene H).
        //   • dragActive — §2.4 drag pipeline flipped it mid-gesture.
        //
        // Defensive: when dragActive is true but the session is gone (e.g.
        // TabDragCompleted didn't fire), hide the overlay anyway so it
        // can't get stuck visible across re-renders. The next render that
        // observes setDragActive(false) catches up.
        var dragActuallyActive = dragActive && DockDragSession.Current is { IsActive: true };
        if (dragActive && !dragActuallyActive)
        {
            // Session vanished out from under us — schedule a state clear
            // for the next render so dragActive catches up.
            QueueMicrotaskClearDrag(setDragActive);
        }
        var showOverlay = manager.ShowDropTargets || dragActuallyActive;
        if (showOverlay)
        {
            var overlay = new DockDropTargetOverlayElement(
                OnHover: target =>
                {
                    setHoveredTarget(target);
                    manager.OnDropTargetHovered?.Invoke(target);
                },
                OnConfirm: target =>
                {
                    // App-supplied confirm handler runs first so apps can
                    // observe even when the docking pipeline takes care
                    // of the layout mutation.
                    manager.OnDropTargetConfirmed?.Invoke(target);

                    var session = DockDragSession.Current;
                    if (session is { IsActive: true })
                    {
                        var newLayout = DockLayoutMutator.MovePaneToTarget(
                            effectiveLayout, session.Source, target);
                        setLayoutOverride(newLayout);
                        manager.OnContentDocked?.Invoke(
                            new DockContentDockedEventArgs { Content = session.Source, Target = target });
                        session.End();
                    }
                    setDragActive(false);
                    setHoveredTarget(null);
                },
                OnDismiss: () =>
                {
                    manager.OnDropTargetsDismissed?.Invoke();
                    var session = DockDragSession.Current;
                    session?.Cancel();
                    setDragActive(false);
                    setHoveredTarget(null);
                });

            composed = Grid(
                new[] { GridSize.Star(1) },
                new[] { GridSize.Star(1) },
                composed.Grid(row: 0, column: 0),
                overlay.Grid(row: 0, column: 0));
        }

        // §2.17 — publish the host model + active-key + layout-snapshot
        // context slots so descendant function components hooked into
        // DockContexts.Host / ActivePaneKey / LayoutSnapshot resolve to
        // the live state.
        return composed
            .Provide(DockContexts.Host, model)
            .Provide(DockContexts.ActivePaneKey, activeKey)
            .Provide(DockContexts.LayoutSnapshot, snapshot);
    }

    /// <summary>
    /// Defer a setDragActive(false) call to the dispatcher tail so it
    /// doesn't recurse the current render. Used by the in-render safety
    /// check that catches a stuck overlay when the drag session has been
    /// disposed but the host's state hasn't caught up.
    /// </summary>
    private static void QueueMicrotaskClearDrag(Action<bool> setDragActive)
    {
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq is null) { setDragActive(false); return; }
        dq.TryEnqueue(() => setDragActive(false));
    }

    private static Element WrapLeafWithPaneContext(DockableContent leaf)
    {
        // Match WinUI.Dock's Document.xaml default: 16-DIP content padding
        // inside a transparent border, so visual rhythm carries from P1.
        // Tool windows in upstream don't carry the same padding; §2.8
        // splits ToolWindow into a separate type — when the renderer
        // distinguishes them we can drop padding on the tool variant.
        var content = leaf.Content ?? (Element)new BorderElement(null);
        var padded = new BorderElement(content)
        {
            Background = null,
            BorderThickness = 0,
        };
        var info = new DockPaneInfo(leaf.Key, leaf.Title ?? string.Empty, leaf);
        // PaneState for a docked leaf in the center tree is always Docked.
        // Floating / AutoHidden states are published by the floating window
        // host (§2.6) and the side-popup host (§2.5) respectively.
        return padded
            .Padding(16)
            .Provide(DockContexts.Pane, (DockPaneInfo?)info)
            .Provide(DockContexts.PaneState, DockPaneState.Docked);
    }

    private static void SyncModelFromElement(DockHostModel model, DockManager element, DockNode? effectiveLayout)
    {
        model.Root = effectiveLayout;
        model.LeftSide = SideSlice(element.LeftSide);
        model.TopSide = SideSlice(element.TopSide);
        model.RightSide = SideSlice(element.RightSide);
        model.BottomSide = SideSlice(element.BottomSide);
        model.ActiveContent = element.ActiveDocument;
        // Floating window state survives the §2.6 wire-up; today it stays
        // empty until the floating renderer publishes entries.
    }

    private static IReadOnlyList<ToolWindow> SideSlice(IReadOnlyList<DockableContent>? items)
    {
        if (items is null or { Count: 0 }) return Array.Empty<ToolWindow>();
        var buffer = new List<ToolWindow>(items.Count);
        foreach (var item in items)
        {
            if (item is ToolWindow tw) buffer.Add(tw);
            // Bare DockableContent in a side slot is a P1 carry-over shape;
            // §2.8 deprecates the bare base type. Drop silently — the model
            // exposes only ToolWindow per the spec's typed surface.
        }
        return buffer;
    }

    private static DockLayoutSnapshot BuildSnapshot(DockHostModel model) =>
        new(
            Root: model.Root,
            LeftSide: model.LeftSide,
            TopSide: model.TopSide,
            RightSide: model.RightSide,
            BottomSide: model.BottomSide,
            Floating: model.Floating,
            ActiveContent: model.ActiveContent);

    private static Element RenderSplit(
        DockSplit split,
        string path,
        Dictionary<string, double[]> ratioStore,
        Action requestRerender,
        Func<DockNode, string, Element> renderChild)
    {
        var children = split.Children;
        if (!ratioStore.TryGetValue(path, out var ratios) || ratios is null || ratios.Length != children.Count)
        {
            ratios = BootstrapRatios(split);
            ratioStore[path] = ratios;
        }

        // renderChild for each child threads through a path suffix so
        // nested DockSplits get their own stable ratio slot. e.g. the
        // outer Vertical split at "0" houses a Horizontal at "0/0" and
        // another at "0/1"; their ratios never alias.
        Element ChildAt(int i) => renderChild(children[i], $"{path}/{i}");

        return DockSplitRenderer.Render(
            split,
            ratios,
            renderChild: node =>
            {
                var idx = -1;
                for (int i = 0; i < children.Count; i++)
                {
                    if (ReferenceEquals(children[i], node)) { idx = i; break; }
                }
                return idx >= 0 ? ChildAt(idx) : new BorderElement(null);
            },
            onSplitterDelta: (idx, delta, hostExtent, isFinal) =>
            {
                if (delta == 0 && !isFinal) return;
                if (hostExtent < 1) return;

                var perChild = new DockSplitChild[children.Count];
                for (int i = 0; i < children.Count; i++)
                    perChild[i] = new DockSplitChild(ratios[i], MinDip: 60, MaxDip: double.PositiveInfinity);

                var sol = DockSplitSolver.ApplyDelta(perChild, idx, delta, totalDip: hostExtent);
                var newRatios = sol.Ratios;
                // Mutate the live array so the ratio store reflects the
                // latest values. The DockSplitterControl applies the new
                // grow values DIRECTLY to its sibling FlexPanel children
                // during the drag (WPF GridSplitter pattern) — re-render
                // is reserved for the terminal isFinal event so the model
                // catches up after the drag completes.
                for (int i = 0; i < ratios.Length; i++) ratios[i] = newRatios[i];
                if (isFinal) requestRerender();
            });
    }

    private static double[] BootstrapRatios(DockSplit split)
    {
        var n = split.Children.Count;
        if (n == 0) return [];

        // Read per-child Width/Height hints along the split axis. When ALL
        // children carry a positive hint we can normalize them as a ratio
        // tuple; mixed (some hinted, some null) is the model author's way
        // of saying "this one is absolute, the others fill the rest" —
        // ratio space can't represent that without knowing the host
        // extent at render time. Until the renderer supports per-child
        // basis-mode flex distribution (a later §2.1 follow-up), fall
        // back to equal share whenever any child is hint-less rather
        // than collapse the unhinted children to ratio 0.
        var raw = new double[n];
        int hintedCount = 0;
        for (int i = 0; i < n; i++)
        {
            double? hint = split.Orientation == Microsoft.UI.Xaml.Controls.Orientation.Horizontal
                ? (split.Children[i] as DockSplit)?.Width
                    ?? (split.Children[i] as DockTabGroup)?.Width
                    ?? (split.Children[i] as DockableContent)?.Width
                : (split.Children[i] as DockSplit)?.Height
                    ?? (split.Children[i] as DockTabGroup)?.Height
                    ?? (split.Children[i] as DockableContent)?.Height;
            if (hint is double v and > 0)
            {
                raw[i] = v;
                hintedCount++;
            }
        }
        return hintedCount == n ? DockSplitSolver.Normalize(raw) : DockSplitSolver.EqualShare(n);
    }
}
