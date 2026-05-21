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

        // ── Spec 045 §2.10 — keyboard navigation state ─────────────────────
        //
        // Active pane key tracks the user's last tab selection so chords
        // like Ctrl+PageUp/Down + Ctrl+F4 can act on the right group. Seeds
        // from the app-supplied ActiveDocument; user tab clicks override.
        //
        // The selected-index store mirrors the ratio store: per-path int
        // overrides for tab group SelectedIndex. Writes happen via tab
        // clicks (OnSelectedIndexChanged) and chord-driven cycling. The
        // store reverts to the group's own SelectedIndex when a path is
        // absent — controlled-input convergence with the immutable model.
        //
        // The keyboard-overlay flag mirrors dragActive: Ctrl+Shift+M flips
        // it true to show the drop-target overlay without an in-flight
        // drag; Esc / OnDismiss clears it.
        var (activePaneKey, setActivePaneKey) = UseState<object?>(null);
        var selectedIndexStoreRef = UseRef<Dictionary<string, int>>(new Dictionary<string, int>());
        var selectedIndexStore = selectedIndexStoreRef.Current;
        var (keyboardOverlayActive, setKeyboardOverlayActive) = UseState(false);

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

        // Resolve effective active key: the app-supplied ActiveDocument
        // wins (controlled-input shape preserved). When the app doesn't
        // pin an ActiveDocument, fall back to the user's last tab-click
        // / chord-cycle target so Ctrl+PageUp/Down + Ctrl+F4 have a
        // sensible target. Reversing this order would let a stale
        // activePaneKey shadow a fresh ActiveDocument prop — the
        // DockHooks_IsActivePane_FlipsOnActiveChange regression.
        var appActiveKey = manager.ActiveDocument?.Key;
        var activeKey = appActiveKey ?? activePaneKey;
        var snapshot = BuildSnapshot(model);

        // ── Spec 045 operation log (Diagnostics.DockOperationLog) ─────────
        // On first render, append a Mount-kind entry so the initial layout
        // is captured as the replay anchor. Subsequent operations append
        // from the various event handlers below.
        var log = manager.OperationLog;
        var mountLoggedRef = UseRef(false);
        if (log is not null && !mountLoggedRef.Current)
        {
            mountLoggedRef.Current = true;
            log.Record(Diagnostics.DockOperationKind.Mount,
                description: "initial layout mounted",
                layout: effectiveLayout,
                ratios: ratioStore);
        }

        void LogOp(
            Diagnostics.DockOperationKind kind,
            string description,
            string? paneKey = null,
            DockTarget? target = null,
            DockNode? layoutOverride = null)
        {
            if (log is null) return;
            log.Record(kind, description,
                layout: layoutOverride ?? effectiveLayout,
                ratios: ratioStore,
                paneKey: paneKey,
                target: target);
        }

        // §2.4 — tab-drag callbacks fed to every DockTabGroup so any tab
        // in the layout can begin a session. Captures `manager` from the
        // current render closure for OnContentFloating/Floated event
        // routing.
        void HandleTabDragStarting(DockableContent pane, int tabIndex)
        {
            // Refuse a second concurrent drag — spec §4.6 single-drag
            // contract carried into P2.
            if (DockDragSession.Current is { IsActive: true }) return;
            // §2.14 — permission gating. Apps mark CanMove=false on panes
            // that must stay where they are (e.g. an anchored toolbox).
            if (!pane.CanMove)
            {
                LogOp(Diagnostics.DockOperationKind.Note,
                    $"refuse drag pane='{pane.Key}' CanMove=false",
                    paneKey: pane.Key?.ToString());
                return;
            }
            var args = new DockContentFloatingEventArgs { Content = pane };
            manager.OnContentFloating?.Invoke(args);
            if (args.Cancel) return;
            DockDragSession.Begin(pane, manager, tabIndex);
            setDragActive(true);
            LogOp(Diagnostics.DockOperationKind.DragStart,
                $"begin drag pane='{pane.Key}' fromTabIndex={tabIndex}",
                paneKey: pane.Key?.ToString());
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
                // §2.14 — refuse tear-out when the pane can't float. The
                // drag session ends without mutating the layout; the
                // dragged tab stays where it is.
                if (!pane.CanFloat)
                {
                    LogOp(Diagnostics.DockOperationKind.Note,
                        $"refuse tear-out pane='{pane.Key}' CanFloat=false",
                        paneKey: pane.Key?.ToString());
                    session.End();
                    setDragActive(false);
                    return;
                }
                // §2.15 — record container before tearing out so a later
                // re-dock can route via PreviousContainer.
                var container = DockLayoutMutator.FindContainer(effectiveLayout, pane);
                if (container is not null) PreviousContainerTracker.Set(pane, container);
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
                    // §2.4 — same as confirm path: surface the new tree.
                    manager.OnLiveLayoutChanged?.Invoke(afterRemove);
                    LogOp(Diagnostics.DockOperationKind.DragTearOut,
                        $"tear-out pane='{pane.Key}' to floating window",
                        paneKey: pane.Key?.ToString(),
                        layoutOverride: afterRemove);
                }
            }

            session.End();
            setDragActive(false);
        }

        // Splitter-final fan-out: wrap the user's optional
        // OnSplitterDragCompleted with a log-recording side-effect so
        // every splitter release lands a SplitterResize entry with the
        // post-drag ratio snapshot.
        Action splitterFinalWithLog = () =>
        {
            LogOp(Diagnostics.DockOperationKind.SplitterResize, "splitter drag completed");
            manager.OnSplitterDragCompleted?.Invoke();
        };

        // Splitter pointer-trace sink — every PRESS / MOVE / RELEASE
        // fires through here so the operation log captures the math
        // behind cursor tracking + the jump-back regression. Null when
        // no log is attached (cheap closure inside the splitter).
        Action<string>? splitterTraceSink = log is null ? null : msg =>
        {
            log.Record(Diagnostics.DockOperationKind.SplitterTrace, msg,
                layout: effectiveLayout,
                ratios: ratioStore);
        };

        Element BuildNode(DockNode node, string path) => node switch
        {
            DockSplit split => RenderSplit(split, path, ratioStore, RequestRatioRerender, BuildNode,
                onSplitterFinal: splitterFinalWithLog,
                splitterDiagnosticSink: splitterTraceSink),
            DockTabGroup grp => RenderTabGroup(grp, path),
            DockableContent leaf => WrapLeafWithPaneContext(leaf),
            _ => new BorderElement(null),
        };

        // §2.10 — tab-group render wrapper. Applies the selected-index
        // store override (chord-cycled) so Ctrl+PageUp/Down can target a
        // group that the app hasn't otherwise selected. The override is
        // ABSENT for groups that haven't been chord-cycled, in which case
        // the wrapper passes the original group through and the call is
        // shape-identical to the baseline DockTabGroupRenderer.Render
        // path (avoids regressions in TabView reconciliation / side-popup
        // click handling that triggered on a non-null callback).
        //
        // Tab-click-driven active-key tracking is intentionally omitted
        // from this wrap: the app owns ActiveDocument; chord cycling
        // writes into activePaneKey directly via the chord handler.
        //
        // §2.14 permission gating + §2.2 close: onTabClosing fires when
        // the user clicks a tab's X button. The handler routes through
        // the cancellable OnDocumentClosing event and the DockLayoutMutator
        // RemovePane path so the data flow matches the chord-driven
        // CloseActivePane (and any future programmatic close).
        Element RenderTabGroup(DockTabGroup grp, string path)
        {
            var hasOverride = selectedIndexStore.TryGetValue(path, out var overrideIdx);
            var effective = hasOverride
                ? grp with { SelectedIndex = ClampIndex(overrideIdx, grp.Documents.Count) }
                : grp;
            return DockTabGroupRenderer.Render(
                effective,
                renderLeafContent: doc => WrapLeafWithPaneContext(doc),
                onSelectedIndexChanged: null,
                onTabClosing: pane => CloseTabViaButton(pane),
                onTabDragStarting: HandleTabDragStarting,
                onTabDragCompleted: HandleTabDragCompleted);
        }

        void CloseTabViaButton(DockableContent pane)
        {
            // §2.14 — even though TabView only surfaces an X button when
            // IsClosable=CanClose, defensively re-check here in case
            // CanClose was flipped between render and click.
            if (!pane.CanClose) return;
            var closingArgs = new DockDocumentClosingEventArgs { Document = pane };
            manager.OnDocumentClosing?.Invoke(closingArgs);
            if (closingArgs.Cancel) return;
            // §2.15 — record the pane's container before removing so a
            // later show-from-history lands it back in the same group.
            var container = DockLayoutMutator.FindContainer(effectiveLayout, pane);
            if (container is not null) PreviousContainerTracker.Set(pane, container);
            var (afterRemove, removed) = DockLayoutMutator.RemovePane(effectiveLayout, pane);
            if (!removed) return;
            setLayoutOverride(afterRemove);
            manager.OnDocumentClosed?.Invoke(new DockDocumentClosedEventArgs { Document = pane });
            manager.OnLiveLayoutChanged?.Invoke(afterRemove);
            LogOp(Diagnostics.DockOperationKind.LayoutChange,
                $"close pane='{pane.Key}' via tab button",
                paneKey: pane.Key?.ToString(),
                layoutOverride: afterRemove);
        }

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
        var showOverlay = manager.ShowDropTargets || dragActuallyActive || keyboardOverlayActive;
        if (showOverlay)
        {
            var overlay = new DockDropTargetOverlayElement(
                OnHover: target =>
                {
                    setHoveredTarget(target);
                    manager.OnDropTargetHovered?.Invoke(target);
                    if (target is DockTarget tgt)
                        LogOp(Diagnostics.DockOperationKind.DragHover,
                            $"hover {tgt}", target: tgt,
                            paneKey: DockDragSession.Current?.Source.Key?.ToString());
                },
                OnConfirm: target =>
                {
                    // App-supplied confirm handler runs first so apps can
                    // observe even when the docking pipeline takes care
                    // of the layout mutation.
                    manager.OnDropTargetConfirmed?.Invoke(target);

                    var session = DockDragSession.Current;
                    DockableContent? sourcePane = session is { IsActive: true } ? session.Source : null;
                    // §2.10 keyboard-initiated mode: no drag session, but
                    // the user has chosen a target via arrow keys + Enter.
                    // The active pane is the implicit source.
                    if (sourcePane is null && keyboardOverlayActive)
                        sourcePane = ResolvePane(effectiveLayout, activePaneKey ?? appActiveKey);
                    // §2.14 — refuse the drop when the source pane is
                    // pinned (CanMove=false). The drag-start path already
                    // gates this for the mouse-drag case; the keyboard
                    // path can still arrive here when a CanMove=false
                    // pane is the active document at the moment of
                    // Ctrl+Shift+M, so re-check defensively.
                    if (sourcePane is { CanMove: false }) sourcePane = null;
                    if (sourcePane is not null)
                    {
                        var newLayout = DockLayoutMutator.MovePaneToTarget(
                            effectiveLayout, sourcePane, target);
                        setLayoutOverride(newLayout);
                        manager.OnContentDocked?.Invoke(
                            new DockContentDockedEventArgs { Content = sourcePane, Target = target });
                        // §2.4 — surface the new whole-tree layout for
                        // apps that want to mirror it (e.g. JSON viewer).
                        manager.OnLiveLayoutChanged?.Invoke(newLayout);
                        LogOp(Diagnostics.DockOperationKind.DragConfirm,
                            $"confirm {target} on pane='{sourcePane.Key}'",
                            paneKey: sourcePane.Key?.ToString(),
                            target: target,
                            layoutOverride: newLayout);
                        session?.End();
                    }
                    setDragActive(false);
                    setKeyboardOverlayActive(false);
                    setHoveredTarget(null);
                },
                OnDismiss: () =>
                {
                    manager.OnDropTargetsDismissed?.Invoke();
                    var session = DockDragSession.Current;
                    if (session is not null)
                        LogOp(Diagnostics.DockOperationKind.DragCancel,
                            $"cancel drag pane='{session.Source.Key}'",
                            paneKey: session.Source.Key?.ToString());
                    session?.Cancel();
                    setDragActive(false);
                    setKeyboardOverlayActive(false);
                    setHoveredTarget(null);
                });

            composed = Grid(
                new[] { GridSize.Star(1) },
                new[] { GridSize.Star(1) },
                composed.Grid(row: 0, column: 0),
                overlay.Grid(row: 0, column: 0));
        }

        // §2.10 — keyboard chord wiring. Bridges chord-handler delegates
        // into DockChordBridge per render so the mount-time KeyboardAccelerators
        // (registered in DockingNativeInterop.AttachChordAccelerators) can
        // invoke the right closures for the current state. The chord lookup
        // for "which group has the active pane" prefers the user-driven
        // activePaneKey (chord-cycled or future tab-focus) over the
        // app-supplied ActiveDocument, so successive chord cycles target
        // the group the user just navigated into.
        var chordTargetKey = activePaneKey ?? appActiveKey;
        void CycleActiveTab(int delta)
        {
            var (group, path, idx) = DockHostKeyboard.FindGroupContainingKey(effectiveLayout, chordTargetKey);
            if (group is null || path is null)
            {
                var first = DockHostKeyboard.FindFirstGroup(effectiveLayout);
                if (first.Group is null || first.Path is null || first.Group.Documents.Count == 0) return;
                group = first.Group;
                path = first.Path;
                idx = ClampIndex(selectedIndexStore.TryGetValue(first.Path, out var stored) ? stored : group.SelectedIndex, group.Documents.Count);
            }
            var next = DockHostKeyboard.CycleIndex(idx, delta, group.Documents.Count);
            if (next == idx) return;
            selectedIndexStore[path] = next;
            var newActive = group.Documents[next];
            var prev = ResolvePane(effectiveLayout, chordTargetKey);
            if (!ReferenceEquals(prev, newActive))
            {
                manager.OnActiveContentChanged?.Invoke(
                    new DockActiveContentChangedEventArgs
                    {
                        ActiveContent = newActive,
                        PreviousContent = prev,
                    });
            }
            setActivePaneKey((object?)newActive.Key);
            RequestRatioRerender();
        }

        void CloseActivePane()
        {
            var pane = ResolvePane(effectiveLayout, chordTargetKey);
            if (pane is null)
            {
                // Fall back to the first document under the layout root.
                var first = DockHostKeyboard.FindFirstGroup(effectiveLayout);
                if (first.Group is null || first.Group.Documents.Count == 0) return;
                pane = first.Group.Documents[first.Group.SelectedIndex >= 0 && first.Group.SelectedIndex < first.Group.Documents.Count
                    ? first.Group.SelectedIndex : 0];
            }
            if (!pane.CanClose) return;
            // Fire the cancellable Closing event before mutating.
            var closingArgs = new DockDocumentClosingEventArgs { Document = pane };
            manager.OnDocumentClosing?.Invoke(closingArgs);
            if (closingArgs.Cancel) return;
            // §2.15 — record container for show-from-history.
            var container = DockLayoutMutator.FindContainer(effectiveLayout, pane);
            if (container is not null) PreviousContainerTracker.Set(pane, container);
            var (afterRemove, removed) = DockLayoutMutator.RemovePane(effectiveLayout, pane);
            if (!removed) return;
            setLayoutOverride(afterRemove);
            manager.OnDocumentClosed?.Invoke(new DockDocumentClosedEventArgs { Document = pane });
            manager.OnLiveLayoutChanged?.Invoke(afterRemove);
            LogOp(Diagnostics.DockOperationKind.LayoutChange,
                $"close pane='{pane.Key}' via keyboard",
                paneKey: pane.Key?.ToString(),
                layoutOverride: afterRemove);
            // Re-anchor the active key on a sibling so subsequent chords
            // have a sensible target.
            var firstAfter = DockHostKeyboard.FindFirstGroup(afterRemove);
            object? newActiveKey = null;
            if (firstAfter.Group is { } g && g.Documents.Count > 0)
            {
                var clamped = g.SelectedIndex >= 0 && g.SelectedIndex < g.Documents.Count ? g.SelectedIndex : 0;
                newActiveKey = g.Documents[clamped].Key;
            }
            setActivePaneKey(newActiveKey);
        }

        void EnterKeyboardDropMode()
        {
            // Toggle: hitting Ctrl+Shift+M while the overlay is up dismisses
            // it (parity with Esc) so a fat-fingered second press doesn't
            // strand the user.
            if (keyboardOverlayActive)
            {
                setKeyboardOverlayActive(false);
                return;
            }
            // No-op when there's no active pane to move — the overlay would
            // open with nothing to dock and Enter would fizzle.
            // §2.14 — same no-op when the active pane is pinned
            // (CanMove=false). Opening the overlay would just guarantee
            // a refused drop on Enter.
            var active = ResolvePane(effectiveLayout, chordTargetKey);
            if (active is null || !active.CanMove) return;
            setKeyboardOverlayActive(true);
        }

        // §2.10 — register the keyboard chord handlers in the host bridge
        // slot. The DockingNativeInterop mount handler attaches a single
        // set of KeyboardAccelerators on the Border once (mount-time) and
        // each Invoked event looks up the live delegates here. This avoids
        // adding a CommandHost layer (a fresh Grid every render perturbs
        // M19's outer FlexPanel ActualWidth and identity tests).
        DockChordBridge.Set(manager,
            new DockChordBridge.Handlers(
                NextTab: () => CycleActiveTab(+1),
                PrevTab: () => CycleActiveTab(-1),
                CloseActive: CloseActivePane,
                EnterDropMode: EnterKeyboardDropMode));

        // §2.17 — publish the host model + active-key + layout-snapshot
        // context slots so descendant function components hooked into
        // DockContexts.Host / ActivePaneKey / LayoutSnapshot resolve to
        // the live state.
        return composed
            .Provide(DockContexts.Host, model)
            .Provide(DockContexts.ActivePaneKey, activeKey)
            .Provide(DockContexts.LayoutSnapshot, snapshot);
    }

    private static int ClampIndex(int idx, int count)
    {
        if (count <= 0) return 0;
        if (idx < 0) return 0;
        if (idx >= count) return count - 1;
        return idx;
    }

    private static DockableContent? ResolvePane(DockNode? root, object? key)
    {
        if (root is null || key is null) return null;
        return Walk(root, key);

        static DockableContent? Walk(DockNode node, object key)
        {
            switch (node)
            {
                case DockableContent leaf:
                    return Equals(leaf.Key, key) ? leaf : null;
                case DockTabGroup grp:
                    foreach (var d in grp.Documents)
                        if (Equals(d.Key, key)) return d;
                    return null;
                case DockSplit split:
                    foreach (var c in split.Children)
                    {
                        var r = Walk(c, key);
                        if (r is not null) return r;
                    }
                    return null;
                default: return null;
            }
        }
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
        // §2.13 — mirror the LayoutStrategy onto the model so its Dock()
        // mutator can route through Before*/After* hooks.
        model.LayoutStrategy = element.LayoutStrategy;
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
        Func<DockNode, string, Element> renderChild,
        Action? onSplitterFinal = null,
        Action<string>? splitterDiagnosticSink = null)
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
                // Tracing the solver too — captures the input ratios,
                // delta + totalDip the solver received and the new
                // ratios it produced. Critical for diagnosing splitter
                // jump-back (math vs. visual).
                splitterDiagnosticSink?.Invoke(
                    $"SOLVE path={path} idx={idx} delta={delta:F1} totalDip={hostExtent:F1} " +
                    $"oldR=[{string.Join(",", perChild.Select(c => c.Ratio.ToString("F3")))}] " +
                    $"newR=[{string.Join(",", newRatios.Select(r => r.ToString("F3")))}] isFinal={isFinal}");
                for (int i = 0; i < ratios.Length; i++) ratios[i] = newRatios[i];
                if (isFinal)
                {
                    requestRerender();
                    onSplitterFinal?.Invoke();
                }
            },
            splitterDiagnosticSink: splitterDiagnosticSink);
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
