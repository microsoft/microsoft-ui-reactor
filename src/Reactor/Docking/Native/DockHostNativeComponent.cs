using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        // We wrap in a sentinel record so a closed-to-empty layout —
        // `LayoutOverride(Root: null)` — is distinguishable from "no
        // override at all" (state value null). Without that, closing
        // the last pane reverts to `manager.Layout` and the pane
        // reappears next render.
        //
        // The drag-active flag drives ShowDropTargets — apps don't need
        // to wire that explicitly to enable tab tear-out + dock-by-drop.
        var (layoutOverride, setLayoutOverride) = UseState<LayoutOverride?>(null);
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

        // ── Spec 045 §2.16 — side-strip override state ────────────────────
        //
        // Programmatic Hide / PinToSide mutations re-route ToolWindows
        // between the docked tree and a side strip. The element's
        // LeftSide / TopSide / RightSide / BottomSide props are the
        // controlled-input shape; the override layers on top so model
        // mutators can rearrange sides without requiring the app to
        // re-pass the lists. Apps assigning new side lists out-of-band
        // surrender the override on the next render (controlled-input
        // convergence, same as `layoutOverride`).
        var (sideOverride, setSideOverride) = UseState<SideOverride?>(null);

        // Spec 045 §2.30 — shape-only layoutOverride. The override
        // stores ONLY the user's drag-modified shape (split orientations,
        // tab-group structure, pane Keys). Per render we resolve the
        // effective layout by walking the shape and pulling each leaf's
        // full DockableContent from a `knownPanes` dictionary — which
        // tracks every pane the host has ever seen (from manager.Layout
        // AND from model-mutator additions like `model.Dock(newPane)`
        // that aren't in the app's tree yet). This is what lets app
        // state flow into pane bodies idiomatically even after the user
        // has dragged — the host owns shape, the app owns content, the
        // contract is honest.
        //
        // Reset / programmatic-layout-push: apps that want to discard
        // user drag state remount via `.WithKey(...)` bump on the
        // DockManager element — that fully unmounts and re-mounts the
        // host, clearing the override + knownPanes.
        var knownPanesRef = UseRef<Dictionary<object, DockableContent>>(new Dictionary<object, DockableContent>());
        var knownPanes = knownPanesRef.Current;
        // Refresh from the app's current tree every render so app
        // state updates (selection-driven Content changes, etc.) are
        // picked up by the next Resolve pass.
        DockLayoutMutator.IndexLeavesInto(manager.Layout, knownPanes);
        // App-driven layout invalidation (spec 046 §6.4). Track the leaf
        // KEY SET of the manager.Layout prop observed in the previous
        // render. When the app passes a Layout whose key set differs from
        // what we saw last time, the app has added or removed panes —
        // discard any stale override so manager.Layout drives this
        // render. Without this, OpenNewDoc / programmatic close / Reset
        // Layout after a drag are silently dropped because the override's
        // stripped shape has no leaf for the new pane.
        //
        // Why key-set instead of just reference: apps that build
        // manager.Layout inline inside Render() (the common pattern when
        // the docking host sits inside another Component) produce a NEW
        // DockNode reference every render even though the shape is
        // stable. A reference-change-gated check would also fire on
        // those re-renders and clobber the model.Dock additions that
        // live in the override but never appear in manager.Layout.
        //
        // The contract: app-driven changes to Layout's content must
        // manifest as a key-set change between renders. Model-mutator
        // additions (DockHostModel.Dock, Hide, etc.) preserve the
        // override because manager.Layout's key set is unchanged from
        // the app's perspective.
        var prevLayoutKeysRef = UseRef<HashSet<object>?>(null);
        var currentLayoutKeys = CollectLeafKeys(manager.Layout);
        bool appLayoutKeysChanged = prevLayoutKeysRef.Current is { } prev
            && !SetEquals(prev, currentLayoutKeys);
        prevLayoutKeysRef.Current = currentLayoutKeys;
        if (appLayoutKeysChanged
            && layoutOverride is { Root: not null }
            && !LeafKeysMatch(layoutOverride.Root, manager.Layout))
        {
            layoutOverride = null;
            setLayoutOverride(null);
        }

        // Override resolution: a null `layoutOverride` means "no
        // override — use the app's prop". A non-null wrapper whose
        // `Root` is null means "intentionally empty layout" (e.g.
        // user closed the last pane). The LayoutOverride wrapper
        // exists specifically to distinguish these two cases —
        // collapsing them by treating Root=null as no-override would
        // resurrect the closed pane on the next render.
        DockNode? effectiveLayout;
        if (layoutOverride is null) effectiveLayout = manager.Layout;
        else if (layoutOverride.Root is null) effectiveLayout = null;
        else effectiveLayout = DockLayoutMutator.ResolveContents(layoutOverride.Root, knownPanes);

        // Helper: store a freshly mutated tree as the shape-only
        // override. Captures every leaf into `knownPanes` first so
        // future renders can resolve the shape against the dict —
        // important for panes that didn't originate from `manager.Layout`
        // (cross-window drag-back, model-mutator additions, etc.).
        void StoreOverride(DockNode? tree)
        {
            DockLayoutMutator.IndexLeavesInto(tree, knownPanes);
            setLayoutOverride(new LayoutOverride(DockLayoutMutator.StripContent(tree)));
        }

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

        // §2.16 — install the re-render trigger on the model so calls to
        // model.Dock / Float / Hide / Show / Close / Activate / PinToSide
        // (whether app-driven or routed by an IDockLayoutStrategy) wake
        // the host into the next render where the drain runs. UseReducer's
        // setter is stable across renders, so the closure stays valid for
        // the component's lifetime.
        model.OnMutationQueued = () => bumpTick(t => t + 1);

        // Register this model in the bridge so external callers (tests,
        // devtools, future apps that hold a DockManager ref) can grab the
        // same instance the reconciler is reading from. Mirrors the
        // DockChordBridge wiring used by §2.10 keyboard accelerators.
        DockHostModelBridge.Set(manager, model);

        // Spec 045 §2.4 cross-window drag — subscribe to global drag
        // session events so this host's drop-target overlays surface
        // when a drag begins in another window (e.g. the floating
        // window's tab dragged back here). bumpTick forces a re-render
        // that re-evaluates `dragActuallyActive` below.
        UseEffect(() =>
        {
            Action onSessionChanged = () => bumpTick(t => t + 1);
            DockDragSession.SessionChanged += onSessionChanged;
            return () =>
            {
                DockDragSession.SessionChanged -= onSessionChanged;
                // Scene-switch leak fix: DockDragSession.Current is a
                // process-static. If this host owned an in-flight drag
                // when it unmounted, the static slot would stay
                // IsActive=true forever and HandleTabDragStarting would
                // silently refuse every future drag. Match by
                // OwnerToken (the host's stable DockHostModel) —
                // DockManager records are rebuilt every parent render
                // so ReferenceEquals on SourceManager always fails.
                // Caught by L12_HostUnmount_CancelsOwnedDragSession.
                var current = DockDragSession.Current;
                if (current is { IsActive: true } && ReferenceEquals(current.OwnerToken, model))
                {
                    current.Cancel();
                    // §2.6 — the tear-off tracker may also be holding a
                    // floating window for this drag. Stop it so the
                    // cursor-poll timer doesn't outlive the unmount.
                    DockTabTearOffTracker.Stop();
                }
            };
        });


        // §2.16 — resolve effective side strips. The override layers on
        // top of the controlled-input lists from the element; programmatic
        // Hide / PinToSide mutations populate it. Apps assigning new
        // LeftSide / TopSide / RightSide / BottomSide props pass them
        // through directly when no override is in flight.
        var effLeftSide   = sideOverride?.Left   ?? manager.LeftSide;
        var effTopSide    = sideOverride?.Top    ?? manager.TopSide;
        var effRightSide  = sideOverride?.Right  ?? manager.RightSide;
        var effBottomSide = sideOverride?.Bottom ?? manager.BottomSide;

        // Resolve effective active key: the app-supplied ActiveDocument
        // wins (controlled-input shape preserved). When the app doesn't
        // pin an ActiveDocument, fall back to the user's last tab-click
        // / chord-cycle target so Ctrl+PageUp/Down + Ctrl+F4 have a
        // sensible target. Reversing this order would let a stale
        // activePaneKey shadow a fresh ActiveDocument prop — the
        // DockHooks_IsActivePane_FlipsOnActiveChange regression.
        var appActiveKey = manager.ActiveDocument?.Key;
        var activeKey = appActiveKey ?? activePaneKey;

        // §2.16 — drain queued mutations into local layout / side / active
        // state. The drain runs synchronously inside Render so the
        // resulting tree paints in the same frame as the mutator call;
        // state setters persist the new state for subsequent renders and
        // lifecycle events fire exactly once per queued op (Pending is
        // cleared before the loop). When no mutations are queued the
        // drain is a no-op.
        IReadOnlyList<RoutingFallbackEvent>? pendingRoutingFallbacks = null;
        if (model.Pending.Count > 0)
        {
            var drain = DrainPendingMutations(
                manager, model,
                effectiveLayout,
                effLeftSide, effTopSide, effRightSide, effBottomSide,
                activeKey);
            if (drain.LayoutChanged)
            {
                effectiveLayout = drain.Layout;
                StoreOverride(drain.Layout);
                manager.OnLiveLayoutChanged?.Invoke(drain.Layout);
                manager.OnLayoutChanged?.Invoke(new DockLayoutChangedEventArgs());
            }
            if (drain.SidesChanged)
            {
                effLeftSide   = drain.LeftSide;
                effTopSide    = drain.TopSide;
                effRightSide  = drain.RightSide;
                effBottomSide = drain.BottomSide;
                setSideOverride(new SideOverride(
                    drain.LeftSide, drain.TopSide, drain.RightSide, drain.BottomSide));
            }
            if (drain.ActiveKeyChanged)
            {
                activeKey = drain.NewActiveKey;
                setActivePaneKey(drain.NewActiveKey);
            }
            // SyncModelFromElement runs again next render with the new
            // effective layout, so the model's read surface catches up.
            // Re-sync now too so any same-render consumers see fresh state.
            model.Root = effectiveLayout;
            model.ActiveContent = drain.NewActiveContent ?? model.ActiveContent;
            // Spec 046 §6.3 — capture role-aware routing fallbacks here;
            // emit via LogOp below once it's in scope. We carry the list
            // out of the drain block so the diagnostic carries the
            // post-drain effective layout.
            pendingRoutingFallbacks = drain.RoutingFallbacks;
        }

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

        // §2.6 tear-off diagnostic sink — route DockTabTearOff state
        // transitions into this host's operation log (when one is
        // attached). The sink is a process-wide static; multi-host
        // scenarios chain via the saved-previous restore on unmount.
        // Wired in a UseEffect (not inline) so unsubscription fires on
        // host unmount and the next host's mount restores its own sink.
        UseEffect(() =>
        {
            var previous = DockTabTearOff.DiagnosticSink;
            DockTabTearOff.DiagnosticSink = msg =>
            {
                LogOp(Diagnostics.DockOperationKind.Note, $"[tear-off] {msg}");
                previous?.Invoke(msg);
            };
            return () =>
            {
                DockTabTearOff.DiagnosticSink = previous;
            };
        });

        // Spec 046 §6.3 — drain fallback events out into DockOperationLog
        // now that LogOp is in scope. Each entry has already been captured
        // in DrainPendingMutations with the pane key + target; we route
        // them as Note-kind ops because they don't correspond to a
        // user-initiated drag (the layout effect IS the user-visible op
        // and is already logged elsewhere).
        if (pendingRoutingFallbacks is { Count: > 0 })
        {
            foreach (var ev in pendingRoutingFallbacks)
                LogOp(Diagnostics.DockOperationKind.Note, ev.Description,
                    paneKey: ev.PaneKey, target: ev.Target);
        }

        // Spec 045 §4.2 cross-window dock-in source hook. Re-installed
        // every render so the closure captures the latest
        // `effectiveLayout` / `StoreOverride` / `LogOp` references. A
        // foreign overlay (the floating window's CenterOnly drop target)
        // invokes this after it has added the pane into its own group;
        // we yank the pane out of this host's layout and fire the same
        // events the tear-out path fires so the app can persist /
        // observe the change.
        model.OnExternalCrossWindowDrop = pane =>
        {
            var (afterRemove, removed) = DockLayoutMutator.RemovePane(effectiveLayout, pane);
            if (!removed) return;
            StoreOverride(afterRemove);
            manager.OnContentFloated?.Invoke(new DockContentFloatedEventArgs { Content = pane });
            manager.OnLiveLayoutChanged?.Invoke(afterRemove);
            LogOp(Diagnostics.DockOperationKind.DragConfirm,
                $"cross-window dock-in: pane='{pane.Key}' removed from source",
                paneKey: pane.Key?.ToString(),
                layoutOverride: afterRemove);
        };

        // Spec 045 §2.6 — VS-style immediate tear-off entry point.
        // Called by DockTabTearOff when the user crosses the threshold
        // (mouse-press + ~4 DIP drag) on a tab header. Performs the same
        // pre-checks as HandleTabDragStarting, then synchronously tears
        // out the pane and opens a 50%-opacity floating window so the
        // cursor-poll timer can track it.
        DockTabTearOff.TearOffActive? BeginImmediateTearOff(DockTabTearOff.TearOffRequest req)
        {
            // Defensive cleanup — a previous botched drag could have left
            // the tracker holding a closed floating window or the
            // process-static session marked IsActive. Force-stop both
            // before checking pre-conditions so a stuck state doesn't
            // permanently block tear-offs. See the §2.6 stuck-state
            // investigation notes.
            if (DockTabTearOffTracker.IsActive)
            {
                LogOp(Diagnostics.DockOperationKind.Note,
                    "[tear-off] BeginImmediateTearOff found stale tracker → ForceCancel (strips stale floating window's drag styles)");
                DockTabTearOffTracker.ForceCancel();
            }
            if (DockDragSession.Current is { IsActive: true } stale)
            {
                LogOp(Diagnostics.DockOperationKind.Note,
                    $"[tear-off] BeginImmediateTearOff found stale session pane='{stale.Source.Key}' → cancelling");
                stale.Cancel();
            }
            var pane = req.Pane;
            if (!pane.CanMove)
            {
                LogOp(Diagnostics.DockOperationKind.Note,
                    $"refuse immediate tear-off pane='{pane.Key}' CanMove=false",
                    paneKey: pane.Key?.ToString());
                return null;
            }
            if (!pane.CanFloat)
            {
                LogOp(Diagnostics.DockOperationKind.Note,
                    $"refuse immediate tear-off pane='{pane.Key}' CanFloat=false",
                    paneKey: pane.Key?.ToString());
                return null;
            }
            var floatingArgs = new DockContentFloatingEventArgs { Content = pane };
            manager.OnContentFloating?.Invoke(floatingArgs);
            if (floatingArgs.Cancel) return null;

            // §2.15 — record container before tearing out so a future
            // re-dock can route via PreviousContainer.
            var container = DockLayoutMutator.FindContainer(effectiveLayout, pane);
            if (container is not null) PreviousContainerTracker.Set(pane, container);

            var (afterRemove, removed) = DockLayoutMutator.RemovePane(effectiveLayout, pane);
            if (!removed) return null;

            // Convert the desired physical-pixel top-left back to DIPs
            // for the WindowSpec.ManualPosition initial placement. This
            // initial value gets converted at Open() time via the
            // window's DPI — possibly stale (96) if WM_DPICHANGED hasn't
            // fired yet, but the tracker's first tick (AppWindow.Move
            // with absolute physical pixels) corrects within ~16ms.
            var initialTopLeft = (
                (double)(req.CursorScreenPhys.X - req.PressOffsetPhys.X) / req.SourceScale,
                (double)(req.CursorScreenPhys.Y - req.PressOffsetPhys.Y) / req.SourceScale);

            // DockFloatingWindow.Open is a normal-code-flow call: its
            // failure modes are bugs (WinUI window-creation refusal,
            // pre-condition violations) that we want to surface, not
            // swallow. The layout mutation (StoreOverride) is deferred
            // until after Open returns so a throw here leaves
            // effectiveLayout intact.
            var floatingWindow = DockFloatingWindow.Open(
                pane,
                manager: manager,
                opacity: 0.5,
                noActivate: true,
                ignorePointerInput: true,
                initialPosition: initialTopLeft);

            StoreOverride(afterRemove);
            manager.OnContentFloated?.Invoke(new DockContentFloatedEventArgs { Content = pane });
            manager.OnLiveLayoutChanged?.Invoke(afterRemove);
            LogOp(Diagnostics.DockOperationKind.DragTearOut,
                $"immediate tear-out pane='{pane.Key}' to floating window",
                paneKey: pane.Key?.ToString(),
                layoutOverride: afterRemove);
            DockHostLiveAnnouncer.Announce(manager,
                DockingStrings.LiveAnnouncement(DockingStringKeys.LiveFloated, pane.Title));

            // Begin the drag session AFTER the layout mutation so a
            // concurrent observer of SessionChanged sees a consistent
            // pane-not-in-source-layout state. setDragActive triggers
            // the drop-target overlays to render on the next render.
            DockDragSession.Begin(pane, manager, req.TabIndex, owner: model);
            setDragActive(true);
            LogOp(Diagnostics.DockOperationKind.DragStart,
                $"begin immediate-drag pane='{pane.Key}' fromTabIndex={req.TabIndex}",
                paneKey: pane.Key?.ToString());

            // On commit: walk the source XamlRoot for any visible
            // DockDropTargetOverlayControl, transform cursor → local
            // coords, call TryConfirmAt. First non-null wins. The
            // overlay's TargetConfirmed event raises the host's
            // OnConfirm closure (already in place from the existing
            // drop-target wiring) which does the layout mutation.
            // If no target was hit, leave the floating window in
            // place with opacity=1.0 (drop-outside semantics).
            Action confirm = () => FinalizeImmediateDrop(floatingWindow, pane, commit: true);
            Action cancel  = () => FinalizeImmediateDrop(floatingWindow, pane, commit: false);

            return new DockTabTearOff.TearOffActive
            {
                FloatingWindow = floatingWindow,
                Pane = pane,
                SourceXamlRoot = req.XamlRoot,
                ConfirmDropAtCursor = confirm,
                CancelDrop = cancel,
                OffsetPhys = req.PressOffsetPhys,
            };
        }

        // Final step of the immediate-tear-off pipeline. Called by the
        // cursor-poll tick when LBUTTON releases (commit=true) or Esc
        // is pressed (commit=false).
        //
        // We rely on the floating window being WS_EX_TRANSPARENT (set by
        // BeginImmediateTearOff via IgnorePointerInput=true) so pointer
        // events during drag pass through to the underlying source XAML
        // island. The drop-target overlay's existing PointerEntered /
        // PointerExited handlers on each button keep `_hoveredTarget`
        // current as the cursor moves over them. At release time we
        // simply ask each visible overlay "are you currently hovered?"
        // and if so, fire its confirm. No screen-DIP coord math needed.
        //
        // If no overlay has a latched hover, the cursor was over empty
        // space — drop-outside semantics. The floating window has its
        // drag styles stripped (Opacity=1, IgnorePointerInput=false,
        // NoActivate=false) and becomes a permanent floating pane at
        // the cursor's release position.
        void FinalizeImmediateDrop(ReactorWindow floatingWindow, DockableContent pane, bool commit)
        {
            var session = DockDragSession.Current;
            DockTarget? confirmedTarget = null;
            if (commit && session is { IsActive: true })
            {
                confirmedTarget = DockTabTearOff.TryConfirmHoveredTargetFor(manager);
            }

            if (confirmedTarget is not null)
            {
                // The overlay's TargetConfirmed event ran the host's
                // OnConfirm closure, which inserted the pane into the
                // layout and called session.End(). Close the floating
                // window — its pane is back in the docked tree.
                //
                // We DEFER the close via TryEnqueue: the synchronous
                // path from TryConfirmCurrentHover ran setState calls
                // that may already be processing on the dispatcher.
                // Closing a window during another render's reentry
                // breaks WinUI's dispatcher (see the spike doc's
                // "render-time cleanup" gotcha). One queued tick is
                // enough to let the host's pending re-render flush.
                // ReactorWindow.Close is idempotent (no-op after _disposed)
                // and internally swallows the COMException teardown-reentry
                // set, so calling it once per branch — enqueued OR sync
                // fallback, never both — is safe without an outer catch.
                var dq = global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                bool enqueued = dq is not null && dq.TryEnqueue(floatingWindow.Close);
                if (!enqueued) floatingWindow.Close();
                return;
            }

            // No target hit (drop-outside / cancel). Strip the
            // drag-only window styles so the floating window becomes a
            // real, interactive floating pane. All four ReactorWindow
            // mutators are no-op on _disposed and internally narrow the
            // teardown COMException set, so no outer catch is needed.
            floatingWindow.SetIgnorePointerInput(false);
            floatingWindow.SetOpacity(1.0);
            floatingWindow.SetNoActivate(false);
            floatingWindow.Activate();

            session?.End();
            setDragActive(false);
            setHoveredTarget(null);
            bumpTick(t => t + 1);
            LogOp(commit
                    ? Diagnostics.DockOperationKind.DragTearOut
                    : Diagnostics.DockOperationKind.DragCancel,
                commit
                    ? $"drop-outside pane='{pane.Key}' (floating window retained)"
                    : $"cancel immediate-drag pane='{pane.Key}'",
                paneKey: pane.Key?.ToString());
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
            _ => Border(null),
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
            // Spec 045 §2.6 — VS-style immediate tear-off. WinUI's
            // CanDragTabs is off on host tab views (the renderer maps null
            // onTabDrag* callbacks to CanDragTabs=false). The custom press
            // hook installed via Setters drives an immediate tear-off the
            // moment the cursor crosses the threshold; see DockTabTearOff
            // for the state machine. Floating windows keep WinUI's OLE
            // drag for their own tab strips (DockFloatingWindow.BuildChrome
            // passes non-null onTabDrag* callbacks).
            var tabView = DockTabGroupRenderer.Render(
                effective,
                renderLeafContent: doc => WrapLeafWithPaneContext(doc),
                onSelectedIndexChanged: null,
                onTabClosing: pane => CloseTabViaButton(pane),
                onTabDragStarting: null,
                onTabDragCompleted: null,
                onPinRequested: PinToSideViaTabButton,
                onTabImmediateTearOff: BeginImmediateTearOff);

            // §2.3 per-group drop overlay. Activates whenever any drag
            // session is in flight in the process (local OR cross-window
            // — see §2.4).
            var groupDragActive = DockDragSession.Current is { IsActive: true };
            if (!groupDragActive) return tabView;
            return BuildPerGroupDropOverlay(grp, tabView);
        }

        // Spec 046 §6.6 — resolve the active drag's payload pane, or null
        // if no drag is in flight. Keyboard-initiated drop mode also has
        // an implicit payload (the active pane); we surface it the same
        // way so the filter applies symmetrically.
        DockableContent? GetActiveDragPayload()
        {
            var session = DockDragSession.Current;
            if (session is { IsActive: true }) return session.Source;
            if (keyboardOverlayActive)
                return ResolvePane(effectiveLayout, activePaneKey ?? appActiveKey);
            return null;
        }

        // Spec 046 §6.6 — disabled targets for a per-group GroupInner overlay.
        // For each of (Center, SplitLeft, SplitTop, SplitRight, SplitBottom):
        //   • Center: AcceptsCategory(group.Role, payloadCategory).
        //   • Split*: same role check AND, for ToolWindow payloads, the
        //     AllowedSides mask is consulted using the logical side.
        DockTarget[]? ComputeDisabledTargetsForGroup(DockTabGroup grp)
        {
            var payload = GetActiveDragPayload();
            if (payload is null) return null;
            // Allocate up to 5 slots; the 5 inner-cluster targets are the
            // candidates. We return null when nothing is filtered to let
            // the reconciler short-circuit the diff.
            List<DockTarget>? disabled = null;
            void Disable(DockTarget t)
            {
                disabled ??= new List<DockTarget>(5);
                disabled.Add(t);
            }
            ReadOnlySpan<DockTarget> inner =
                [DockTarget.Center, DockTarget.SplitLeft, DockTarget.SplitTop,
                 DockTarget.SplitRight, DockTarget.SplitBottom];
            for (int i = 0; i < inner.Length; i++)
            {
                var t = inner[i];
                var side = DockDropFilter.SideOf(t);
                if (!DockDropFilter.CanDropInto(grp, payload, side))
                    Disable(t);
            }
            return disabled?.ToArray();
        }

        // Spec 046 §6.6 — disabled targets for the root-scope overlay.
        // Only Dock* edge targets are surfaced in Host mode; filter via
        // CanDockAtEdge (which only inspects ToolWindow.AllowedSides).
        DockTarget[]? ComputeDisabledTargetsForRoot()
        {
            var payload = GetActiveDragPayload();
            if (payload is null) return null;
            List<DockTarget>? disabled = null;
            void Disable(DockTarget t)
            {
                disabled ??= new List<DockTarget>(4);
                disabled.Add(t);
            }
            if (!DockDropFilter.CanDockAtEdge(payload, DockSide.Left))   Disable(DockTarget.DockLeft);
            if (!DockDropFilter.CanDockAtEdge(payload, DockSide.Top))    Disable(DockTarget.DockTop);
            if (!DockDropFilter.CanDockAtEdge(payload, DockSide.Right))  Disable(DockTarget.DockRight);
            if (!DockDropFilter.CanDockAtEdge(payload, DockSide.Bottom)) Disable(DockTarget.DockBottom);
            return disabled?.ToArray();
        }

        // Composes the per-group inner-target overlay (Center + 4 Splits)
        // over the supplied tab-view element. Confirm captures the group
        // reference and routes to MovePaneToGroupTarget so the dropped
        // pane lands relative to THIS group instead of the layout root.
        Element BuildPerGroupDropOverlay(DockTabGroup grp, Element tabView)
        {
            // Spec 046 §6.6 — compute the disabled targets for THIS group
            // against the active drag's payload. Stable across renders
            // until the source pane changes; the overlay reconciler
            // diff-checks the array before re-applying.
            var disabled = ComputeDisabledTargetsForGroup(grp);
            var overlay = new DockDropTargetOverlayElement(
                OnHover: target =>
                {
                    setHoveredTarget(target);
                    manager.OnDropTargetHovered?.Invoke(target);
                },
                OnConfirm: target =>
                {
                    manager.OnDropTargetConfirmed?.Invoke(target);
                    var session = DockDragSession.Current;
                    var sourcePane = session is { IsActive: true } ? session.Source : null;
                    if (sourcePane is { CanMove: false }) sourcePane = null;
                    if (sourcePane is not null)
                    {
                        // Spec 045 §2.4 cross-window drop: when the
                        // source pane is in another window (not in this
                        // host's layout), MovePaneToGroupTarget's
                        // remove step is a no-op (which makes it bail
                        // and return the original root unchanged); fall
                        // through to a group-targeted INSERT instead.
                        bool isLocalSource = DockLayoutMutator.FindContainer(effectiveLayout, sourcePane) is not null;
                        DockNode? newLayout;
                        if (isLocalSource)
                        {
                            newLayout = DockLayoutMutator.MovePaneToGroupTarget(
                                effectiveLayout, sourcePane, grp, target);
                        }
                        else
                        {
                            // Cross-window: insert into the target group
                            // without trying to remove from this layout.
                            // The floating window closes itself on
                            // session-consumed.
                            newLayout = target switch
                            {
                                DockTarget.Center => DockLayoutMutator.InsertPaneIntoGroup(effectiveLayout, sourcePane, grp),
                                _ => DockLayoutMutator.InsertPaneRelativeToGroup(effectiveLayout, sourcePane, grp, target),
                            };
                        }
                        if (newLayout is not null)
                        {
                            StoreOverride(newLayout);
                            manager.OnContentDocked?.Invoke(
                                new DockContentDockedEventArgs { Content = sourcePane, Target = target });
                            manager.OnLiveLayoutChanged?.Invoke(newLayout);
                            LogOp(Diagnostics.DockOperationKind.DragConfirm,
                                $"confirm group={grp.GetHashCode():X} target={target} pane='{sourcePane.Key}' (cross-window={!isLocalSource})",
                                paneKey: sourcePane.Key?.ToString(),
                                target: target,
                                layoutOverride: newLayout);
                            DockHostLiveAnnouncer.Announce(manager,
                                DockingStrings.LiveAnnouncement(DockingStringKeys.LiveDocked, sourcePane.Title));
                        }
                        // Mark the drop as consumed BEFORE ending the
                        // session so cross-window observers (the source
                        // floating window's TabDragCompleted handler)
                        // can distinguish "dropped on a dock surface"
                        // from "cancelled / went nowhere".
                        DockDragSession.MarkConsumed();
                        session?.End();
                    }
                    setDragActive(false);
                    setHoveredTarget(null);
                    // Spec 045 §2.3 — force a re-render even when the
                    // setState calls become no-ops (e.g. dragActive was
                    // already flipping false from a competing path).
                    // Without this, a stuck-overlay state can persist
                    // because the reconciler doesn't get a chance to
                    // diff the new tree.
                    bumpTick(t => t + 1);
                },
                OnDismiss: () =>
                {
                    // Spec 045 §2.3 — per-group overlay dismissal path
                    // (Esc, or a drop in the overlay's bounds that
                    // missed every button). Cancel the active session
                    // and clear drag state so the overlays disappear
                    // without triggering tear-out.
                    manager.OnDropTargetsDismissed?.Invoke();
                    var session = DockDragSession.Current;
                    if (session is not null)
                        LogOp(Diagnostics.DockOperationKind.DragCancel,
                            $"per-group cancel pane='{session.Source.Key}'",
                            paneKey: session.Source.Key?.ToString());
                    session?.Cancel();
                    setDragActive(false);
                    setHoveredTarget(null);
                    bumpTick(t => t + 1);
                },
                Mode: DockDropOverlayMode.GroupInner)
            { DisabledTargets = disabled };

            return Grid(
                new[] { GridSize.Star(1) },
                new[] { GridSize.Star(1) },
                tabView.Grid(row: 0, column: 0),
                overlay.Grid(row: 0, column: 0));
        }

        // Spec 045 §2.2 — pin button click handler. Routes through the
        // model's PinToSide mutator (§2.16) so the drain rebuilds the
        // layout + side strips and fires OnContentDocked / live-region
        // announcements per the documented contract. Re-checks
        // CanAutoHide defensively (matches CloseTabViaButton's
        // CanClose re-check).
        void PinToSideViaTabButton(ToolWindow tw)
        {
            if (!tw.CanAutoHide) return;
            // Default to the tool window's remembered side; otherwise
            // bias to left, matching upstream WinUI.Dock convention.
            // (PinToSide is the public-API verb; the side here is the
            // user-intended destination, not a guarantee about visual
            // layout after RTL flip.)
            var model = DockHostModelBridge.Get(manager);
            if (model is null) return;
            model.PinToSide(tw, DockSide.Left);
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
            StoreOverride(afterRemove);
            manager.OnDocumentClosed?.Invoke(new DockDocumentClosedEventArgs { Document = pane });
            manager.OnLiveLayoutChanged?.Invoke(afterRemove);
            LogOp(Diagnostics.DockOperationKind.LayoutChange,
                $"close pane='{pane.Key}' via tab button",
                paneKey: pane.Key?.ToString(),
                layoutOverride: afterRemove);
            DockHostLiveAnnouncer.Announce(manager,
                DockingStrings.LiveAnnouncement(DockingStringKeys.LiveClosed, pane.Title));
            // Spec 045 §2.22 focus invariant — fall back to the host
            // when the close leaves no pane to receive focus. Sibling-
            // pane focus is carried by the TabView's selection-change
            // path on the next render.
            if (afterRemove is null || DockHostKeyboard.FindFirstGroup(afterRemove).Group is null)
                DockHostLiveAnnouncer.FocusHostFallback(manager);
        }

        Element body = effectiveLayout is null
            ? Border(null)
            : BuildNode(effectiveLayout, path: "0");

        // ── Side strips + side popup (§2.5). Elide entirely when no
        // sides are populated so the visual matches the P1 baseline for
        // layouts that don't pin. Otherwise compose strips + a shared
        // light-dismiss Popup overlay; click on a strip button toggles
        // expansion of the matching pane.
        var hasSides =
            (effLeftSide is { Count: > 0 }) ||
            (effTopSide is { Count: > 0 }) ||
            (effRightSide is { Count: > 0 }) ||
            (effBottomSide is { Count: > 0 });

        var (expandedSideKey, setExpandedSideKey) = UseState<object?>(null);

        Element composed = hasSides
            ? DockSideStripRenderer.Compose(
                manager, body,
                effLeftSide, effTopSide, effRightSide, effBottomSide,
                expandedSideKey, setExpandedSideKey)
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
        // Spec 045 §2.4 cross-window: the overlay activates whenever
        // ANY drag is in flight in the process — local-host drags AND
        // drags initiated in another window (e.g. a floating window's
        // tab being dragged back home). The previous condition required
        // dragActive to also be true, which gated out cross-window
        // sessions started in another DockHostNativeComponent / a
        // floating window's tab-drag handler.
        var dragActuallyActive = DockDragSession.Current is { IsActive: true };
        if (dragActive && !dragActuallyActive)
        {
            // Session vanished out from under us — schedule a state clear
            // for the next render so dragActive catches up.
            QueueMicrotaskClearDrag(setDragActive);
        }
        var showOverlay = manager.ShowDropTargets || dragActuallyActive || keyboardOverlayActive;
        if (showOverlay)
        {
            // Spec 046 §6.6 — filter the Dock* edge targets at root scope.
            // Only the AllowedSides mask matters here (no group to consult
            // for role). Inner-cluster (Center + Split*) buttons are
            // hidden in Host mode, so they never need filtering at the
            // root overlay.
            var rootDisabled = ComputeDisabledTargetsForRoot();
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
                        // Spec 045 §2.4 cross-window drop: if the pane
                        // isn't in this host's layout (it's in a floating
                        // window's TabView), skip the remove and just
                        // insert. The floating window's TabDragCompleted
                        // sees session.Current=null afterwards and closes
                        // itself.
                        bool isLocalSource = DockLayoutMutator.FindContainer(effectiveLayout, sourcePane) is not null;
                        var newLayout = isLocalSource
                            ? DockLayoutMutator.MovePaneToTarget(effectiveLayout, sourcePane, target)
                            : DockLayoutMutator.InsertPaneAtTarget(effectiveLayout, sourcePane, target);
                        StoreOverride(newLayout);
                        manager.OnContentDocked?.Invoke(
                            new DockContentDockedEventArgs { Content = sourcePane, Target = target });
                        // §2.4 — surface the new whole-tree layout for
                        // apps that want to mirror it (e.g. JSON viewer).
                        manager.OnLiveLayoutChanged?.Invoke(newLayout);
                        LogOp(Diagnostics.DockOperationKind.DragConfirm,
                            $"confirm {target} on pane='{sourcePane.Key}' (cross-window={!isLocalSource})",
                            paneKey: sourcePane.Key?.ToString(),
                            target: target,
                            layoutOverride: newLayout);
                        DockHostLiveAnnouncer.Announce(manager,
                            DockingStrings.LiveAnnouncement(DockingStringKeys.LiveDocked, sourcePane.Title));
                        // Mark the drop as consumed BEFORE ending the
                        // session so cross-window observers (the source
                        // floating window's TabDragCompleted handler)
                        // can distinguish "dropped on a dock surface"
                        // from "cancelled / went nowhere".
                        DockDragSession.MarkConsumed();
                        session?.End();
                    }
                    setDragActive(false);
                    setKeyboardOverlayActive(false);
                    setHoveredTarget(null);
                    bumpTick(t => t + 1);
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
                    bumpTick(t => t + 1);
                })
            { DisabledTargets = rootDisabled };

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
            StoreOverride(afterRemove);
            manager.OnDocumentClosed?.Invoke(new DockDocumentClosedEventArgs { Document = pane });
            manager.OnLiveLayoutChanged?.Invoke(afterRemove);
            LogOp(Diagnostics.DockOperationKind.LayoutChange,
                $"close pane='{pane.Key}' via keyboard",
                paneKey: pane.Key?.ToString(),
                layoutOverride: afterRemove);
            DockHostLiveAnnouncer.Announce(manager,
                DockingStrings.LiveAnnouncement(DockingStringKeys.LiveClosed, pane.Title));
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
            // Spec 045 §2.22 — focus invariant: when no pane is left to
            // receive focus, hand focus to the host so keyboard events
            // (Ctrl+Tab, Esc, Alt+F4) stay reachable. When a sibling
            // pane remains, its TabView selection change carries focus
            // automatically on the next render.
            if (newActiveKey is null)
                DockHostLiveAnnouncer.FocusHostFallback(manager);
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

        // §2.10 — Alt+F7 hidden-pane picker. Re-opens the navigator
        // primitive with the union of side-stripped panes (panes that
        // were hidden via `Hide` / closed via `Close` on a CanHide=true
        // ToolWindow → routed to a side strip in the §2.16 drain).
        // Selecting an entry triggers `model.Show(pane)` which re-attaches
        // it to its previous container via §2.15.
        void OpenHiddenPicker()
        {
            var hostElement = DockHostLiveAnnouncer.GetHost(manager);
            if (hostElement is null) return;
            var hidden = new List<ToolWindow>();
            // §2.16 drain populates side overrides as DockableContent[] at
            // runtime (see AddToSide). IReadOnlyList<T> is covariant in T —
            // a DockableContent[] cannot be `as`-cast to IReadOnlyList<ToolWindow>,
            // so filter by element type instead, matching SideSlice.
            void AddSide(IReadOnlyList<DockableContent>? side)
            {
                if (side is null) return;
                foreach (var item in side)
                    if (item is ToolWindow tw) hidden.Add(tw);
            }
            AddSide(effLeftSide);
            AddSide(effTopSide);
            AddSide(effRightSide);
            AddSide(effBottomSide);
            if (hidden.Count == 0) return; // nothing to re-show
            var nav = DockNavigatorPopup.For(hostElement);
            var entries = new DockNavigatorPopup.Entry[hidden.Count];
            for (int i = 0; i < hidden.Count; i++)
                entries[i] = new DockNavigatorPopup.Entry(hidden[i].Key, hidden[i].Title ?? string.Empty);
            nav.OpenOrAdvance(entries, currentIndex: -1, delta: +1, committedKey =>
            {
                if (committedKey is null) return;
                ToolWindow? target = null;
                foreach (var tw in hidden)
                {
                    if (Equals(tw.Key, committedKey)) { target = tw; break; }
                }
                if (target is null) return;
                // Show via the model so the §2.16 drain routes back to
                // the previous container and clears the §2.15 tracker.
                model.Show(target);
            });
        }

        // §2.10 navigator — Ctrl+Tab opens the VS-style pane picker. The
        // popup primitive lives outside the Reactor reconciler so it
        // doesn't perturb the render tree; we just need to resolve the
        // host element and supply the pane list + commit callback.
        void OpenNavigator(int delta)
        {
            var leaves = DockHostKeyboard.EnumerateLeaves(effectiveLayout);
            if (leaves.Count == 0) return;
            var host = DockHostLiveAnnouncer.GetHost(manager);
            if (host is null) return;
            var nav = DockNavigatorPopup.For(host);
            var entries = new DockNavigatorPopup.Entry[leaves.Count];
            for (int i = 0; i < leaves.Count; i++)
                entries[i] = new DockNavigatorPopup.Entry(leaves[i].Key, leaves[i].Title ?? string.Empty);
            int currentIdx = DockHostKeyboard.IndexOfKey(leaves, chordTargetKey);
            nav.OpenOrAdvance(entries, currentIdx, delta, committedKey =>
            {
                if (committedKey is null) return;
                var newPane = ResolvePane(effectiveLayout, committedKey);
                if (newPane is null) return;
                var prev = ResolvePane(effectiveLayout, chordTargetKey);
                if (!ReferenceEquals(prev, newPane))
                {
                    manager.OnActiveContentChanged?.Invoke(new DockActiveContentChangedEventArgs
                    {
                        ActiveContent = newPane,
                        PreviousContent = prev,
                    });
                }
                setActivePaneKey((object?)newPane.Key);
                bumpTick(t => t + 1);
            });
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
                EnterDropMode: EnterKeyboardDropMode,
                OpenNavigator: OpenNavigator,
                OpenHiddenPicker: OpenHiddenPicker));

        // §2.14 — test seam for the drag-start permission gate. Mirrors
        // HandleTabDragStarting: returns false when the gate refuses (no
        // session started) and true when DockDragSession.Begin succeeded.
        // Used by the headless self-test harness, which has no
        // programmatic TabView.TabDragStarting surface.
        DockDragGateBridge.Set(manager, (pane, tabIndex) =>
        {
            // Gate-only: this is the same predicate the §2.6 tear-off
            // path inlines at the top of BeginImmediateTearOff (CanMove
            // + OnContentFloating). The seam exists so headless fixtures
            // can exercise the gate without spinning up a real floating
            // window. A passing gate begins a DockDragSession but does
            // NOT perform the layout mutation / window open; tests that
            // need those drive the overlay confirm path separately.
            if (DockDragSession.Current is { IsActive: true }) return false;
            if (!pane.CanMove)
            {
                LogOp(Diagnostics.DockOperationKind.Note,
                    $"refuse drag pane='{pane.Key}' CanMove=false",
                    paneKey: pane.Key?.ToString());
                return false;
            }
            var args = new DockContentFloatingEventArgs { Content = pane };
            manager.OnContentFloating?.Invoke(args);
            if (args.Cancel) return false;
            DockDragSession.Begin(pane, manager, tabIndex, owner: model);
            setDragActive(true);
            LogOp(Diagnostics.DockOperationKind.DragStart,
                $"begin drag pane='{pane.Key}' fromTabIndex={tabIndex}",
                paneKey: pane.Key?.ToString());
            return DockDragSession.Current is { IsActive: true };
        });

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
        var content = leaf.Content ?? (Element)Border(null);
        var padded = Border(content) with
        {
            Background = null,
            BorderThickness = 0,
        };
        var info = new DockPaneInfo(leaf.Key, leaf.Title ?? string.Empty, leaf);
        // PaneState for a docked leaf in the center tree is always Docked.
        // Floating / AutoHidden states are published by the floating window
        // host (§2.6) and the side-popup host (§2.5) respectively.
        // Spec 045 §2.22 a11y — stable AutomationId per pane derived from
        // its Key.ToString() so screen readers + selftests address panes
        // deterministically across re-renders.
        var wrapped = padded
            .Padding(16)
            .Provide(DockContexts.Pane, (DockPaneInfo?)info)
            .Provide(DockContexts.PaneState, DockPaneState.Docked);
        var paneAtId = AutomationIdForPane(leaf);
        if (!string.IsNullOrEmpty(paneAtId))
            wrapped = wrapped.AutomationId(paneAtId).AutomationName(leaf.Title ?? paneAtId);
        return wrapped;
    }

    /// <summary>
    /// Spec 045 §2.22 a11y — stable AutomationId derived from the pane's
    /// Key. The key may be any object; we serialize via ToString() and
    /// prefix with "pane:" so the AT tree carries an unambiguous
    /// docking-pane identifier separable from app-supplied ones. Returns
    /// null when the pane has no key (the AT tree falls back to the
    /// pane's Title or framework defaults).
    /// </summary>
    internal static string? AutomationIdForPane(DockableContent leaf)
    {
        var keyText = leaf.Key?.ToString();
        return string.IsNullOrEmpty(keyText) ? null : $"pane:{keyText}";
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
        // §2.6 — publish what's currently floating for this manager.
        // The tracker carries the pane + spec dimensions captured at
        // Open; live x/y/w/h tracking remains a future refinement
        // (entries report initial bounds, not the current window
        // position). Snapshots / persistence read off this list, so
        // model.Floating no longer silently lies about empty state.
        model.Floating = DockFloatingTracker.SnapshotPanesFor(element);
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

    /// <summary>
    /// True when the two trees contain the same set of leaf <see cref="DockableContent.Key"/>
    /// values (order- and shape-independent). Used by the host to detect
    /// app-driven pane additions/removals that the shape-only override
    /// can't carry across a render (Scene J open-new-doc-after-split repro).
    /// </summary>
    private static bool LeafKeysMatch(DockNode? a, DockNode? b)
    {
        var dictA = new Dictionary<object, DockableContent>();
        var dictB = new Dictionary<object, DockableContent>();
        DockLayoutMutator.IndexLeavesInto(a, dictA);
        DockLayoutMutator.IndexLeavesInto(b, dictB);
        if (dictA.Count != dictB.Count) return false;
        foreach (var k in dictA.Keys)
            if (!dictB.ContainsKey(k)) return false;
        return true;
    }

    /// <summary>
    /// Collect the leaf <see cref="DockableContent.Key"/> set from a
    /// layout into a fresh HashSet. Used across renders to detect when
    /// the app has added or removed panes through manager.Layout.
    /// </summary>
    private static HashSet<object> CollectLeafKeys(DockNode? root)
    {
        var dict = new Dictionary<object, DockableContent>();
        DockLayoutMutator.IndexLeavesInto(root, dict);
        return new HashSet<object>(dict.Keys);
    }

    private static bool SetEquals(HashSet<object> a, HashSet<object> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var k in a)
            if (!b.Contains(k)) return false;
        return true;
    }

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
                return idx >= 0 ? ChildAt(idx) : Border(null);
            },
            onSplitterDelta: (idx, delta, hostExtent, isFinal) =>
            {
                // Tolerance-based zero check: pointer drag can deliver
                // sub-pixel deltas under HiDPI / fractional DIPs.
                // 0.01 DIP is well below any user-visible threshold.
                if (Math.Abs(delta) < 0.01 && !isFinal) return;
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

    // ── §2.16 model-mutation drain ────────────────────────────────────────

    /// <summary>
    /// Outcome bundle from <see cref="DrainPendingMutations"/>. The host
    /// uses the <c>*Changed</c> flags to decide which state setter to call
    /// and which lifecycle event to fire, while the value fields carry the
    /// post-drain effective state used to paint the current render.
    /// </summary>
    private readonly record struct DrainResult(
        DockNode? Layout, bool LayoutChanged,
        IReadOnlyList<DockableContent>? LeftSide,
        IReadOnlyList<DockableContent>? TopSide,
        IReadOnlyList<DockableContent>? RightSide,
        IReadOnlyList<DockableContent>? BottomSide,
        bool SidesChanged,
        object? NewActiveKey, DockableContent? NewActiveContent, bool ActiveKeyChanged,
        // Spec 046 §6.3 — fallback descriptions collected during the drain,
        // logged to DockOperationLog by the caller (which owns the log
        // reference). Each entry: (description, paneKey, target).
        IReadOnlyList<RoutingFallbackEvent>? RoutingFallbacks = null);

    /// <summary>
    /// Spec 046 §6.3 — packaged routing fallback event collected during
    /// the §2.16 drain and surfaced through <see cref="DrainResult"/>.
    /// The render caller logs these via <c>DockOperationLog</c>.
    /// </summary>
    private readonly record struct RoutingFallbackEvent(string Description, string? PaneKey, DockTarget Target);

    /// <summary>
    /// Translates each <see cref="PendingMutation"/> queued on
    /// <paramref name="model"/> into a layout / side / active-key update,
    /// firing the matching per-pane lifecycle events. Clears
    /// <see cref="DockHostModel.Pending"/> before returning so subsequent
    /// renders skip the drain when nothing is queued.
    /// </summary>
    /// <remarks>
    /// Cancellable <c>*ing</c> events run inline: a <c>Cancel = true</c>
    /// response leaves the mutation un-applied and the layout untouched.
    /// The post-event <c>*ed</c> variants fire only when the mutation
    /// actually landed. Spec 045 §5.3.5 + §2.16.
    /// </remarks>
    private static DrainResult DrainPendingMutations(
        DockManager manager,
        DockHostModel model,
        DockNode? layout,
        IReadOnlyList<DockableContent>? leftSide,
        IReadOnlyList<DockableContent>? topSide,
        IReadOnlyList<DockableContent>? rightSide,
        IReadOnlyList<DockableContent>? bottomSide,
        object? activeKey)
    {
        // Snapshot then clear so any After*-hook re-entries that queue
        // further mutations land in the next render's drain instead of
        // recursing during this one. Today the strategy's After* hook
        // runs synchronously inside model.Dock(), so those entries are
        // already in `Pending` and we'll process them in the same loop;
        // future expansions (timers, async tasks) that queue mid-render
        // remain safe.
        var ops = model.Pending.ToArray();
        model.Pending.Clear();

        var workingLayout = layout;
        var workingLeft   = leftSide;
        var workingTop    = topSide;
        var workingRight  = rightSide;
        var workingBottom = bottomSide;
        bool layoutChanged = false;
        bool sidesChanged  = false;
        bool activeChanged = false;
        object? newActiveKey = activeKey;
        DockableContent? newActiveContent = null;
        // Spec 046 §6.3 — collected fallback events; surfaced via DrainResult
        // to the render caller which owns the DockOperationLog reference.
        List<RoutingFallbackEvent>? routingFallbacks = null;

        foreach (var op in ops)
        {
            switch (op)
            {
                case PendingMutation.DockOp dockOp:
                {
                    var changingArgs = new DockLayoutChangingEventArgs();
                    manager.OnLayoutChanging?.Invoke(changingArgs);
                    if (changingArgs.Cancel) break;
                    var dockingArgs = new DockContentDockingEventArgs
                    {
                        Content = dockOp.Content,
                        Target = dockOp.Target,
                    };
                    manager.OnContentDocking?.Invoke(dockingArgs);
                    if (dockingArgs.Cancel) break;
                    // Defensive: if the pane is already somewhere in the
                    // tree (an app racing two Docks for the same pane),
                    // move it rather than duplicate. Otherwise insert.
                    var beforeRemove = workingLayout;
                    var (afterRemove, found) = DockLayoutMutator.RemovePane(workingLayout, dockOp.Content);
                    workingLayout = DockLayoutMutator.InsertPaneAtTarget(
                        found ? afterRemove : beforeRemove, dockOp.Content, dockOp.Target,
                        out var routingFallback);
                    // Spec 046 §6.3 — when role-aware routing couldn't find an
                    // accepting group and degraded to leftmost-descendant,
                    // collect the fallback so the render caller can log it
                    // through DockOperationLog (which it owns, not us).
                    if (routingFallback is not null)
                    {
                        routingFallbacks ??= new List<RoutingFallbackEvent>(1);
                        routingFallbacks.Add(new RoutingFallbackEvent(
                            "spec-046 routing fallback: " + routingFallback.Description,
                            dockOp.Content.Key?.ToString(),
                            dockOp.Target));
                    }
                    layoutChanged = true;
                    // Side-strip presence has no overlap with the docked
                    // tree, so a Dock pulled a pane out of a side strip
                    // only when explicitly queued via PinToSide flip — we
                    // don't strip-remove here.
                    manager.OnContentDocked?.Invoke(new DockContentDockedEventArgs
                    {
                        Content = dockOp.Content,
                        Target = dockOp.Target,
                    });
                    DockHostLiveAnnouncer.Announce(manager,
                        DockingStrings.LiveAnnouncement(DockingStringKeys.LiveDocked, dockOp.Content.Title));
                    break;
                }

                case PendingMutation.DockToGroupOp groupOp:
                {
                    // Spec 046 §6.4 — group-targeted dock. Trusted insert
                    // (skips role compatibility checks per spec §9 Q3).
                    // Lifecycle events still fire so apps see the dock.
                    var changingArgs = new DockLayoutChangingEventArgs();
                    manager.OnLayoutChanging?.Invoke(changingArgs);
                    if (changingArgs.Cancel) break;
                    var dockingArgs = new DockContentDockingEventArgs
                    {
                        Content = groupOp.Content,
                        Target = groupOp.Target,
                    };
                    manager.OnContentDocking?.Invoke(dockingArgs);
                    if (dockingArgs.Cancel) break;

                    // De-duplicate: remove from wherever the pane lived first.
                    var (afterRemove, found) = DockLayoutMutator.RemovePane(workingLayout, groupOp.Content);
                    var sourceLayout = found ? afterRemove : workingLayout;
                    var result = DockLayoutMutator.TryInsertPaneAtGroupTarget(
                        sourceLayout, groupOp.Content, groupOp.TargetGroup, groupOp.Target);
                    if (result is null)
                    {
                        // Target group couldn't be resolved against the layout.
                        // No-op; surface a diagnostic for the caller.
                        routingFallbacks ??= new List<RoutingFallbackEvent>(1);
                        routingFallbacks.Add(new RoutingFallbackEvent(
                            "spec-046 group-target unresolvable: " +
                            "DockHostModel.Dock(content, group, target) could not match " +
                            "the supplied DockTabGroup against the current layout " +
                            "(neither by reference nor by content keys). Operation no-op.",
                            groupOp.Content.Key?.ToString(),
                            groupOp.Target));
                        // Don't fire OnContentDocked when the dock didn't land.
                        break;
                    }
                    workingLayout = result;
                    layoutChanged = true;
                    manager.OnContentDocked?.Invoke(new DockContentDockedEventArgs
                    {
                        Content = groupOp.Content,
                        Target = groupOp.Target,
                    });
                    DockHostLiveAnnouncer.Announce(manager,
                        DockingStrings.LiveAnnouncement(DockingStringKeys.LiveDocked, groupOp.Content.Title));
                    break;
                }

                case PendingMutation.FloatOp floatOp:
                {
                    var floatingArgs = new DockContentFloatingEventArgs { Content = floatOp.Content };
                    manager.OnContentFloating?.Invoke(floatingArgs);
                    if (floatingArgs.Cancel) break;
                    // Open the floating window FIRST so the layout mutation
                    // only commits when a real window was produced. If Open
                    // throws, the process tears down and no half-mutated
                    // state survives.
                    DockFloatingWindow.Open(floatOp.Content, manager: manager);
                    var (after, found) = DockLayoutMutator.RemovePane(workingLayout, floatOp.Content);
                    if (found)
                    {
                        // §2.15 — record container before tear-out so a
                        // later re-dock can route via PreviousContainer.
                        var container = DockLayoutMutator.FindContainer(workingLayout, floatOp.Content);
                        if (container is not null) PreviousContainerTracker.Set(floatOp.Content, container);
                        workingLayout = after;
                        layoutChanged = true;
                    }
                    manager.OnContentFloated?.Invoke(new DockContentFloatedEventArgs { Content = floatOp.Content });
                    DockHostLiveAnnouncer.Announce(manager,
                        DockingStrings.LiveAnnouncement(DockingStringKeys.LiveFloated, floatOp.Content.Title));
                    break;
                }

                case PendingMutation.HideOp hideOp:
                {
                    var hidingArgs = new DockToolWindowHidingEventArgs { ToolWindow = hideOp.ToolWindow };
                    manager.OnToolWindowHiding?.Invoke(hidingArgs);
                    if (hidingArgs.Cancel) break;
                    var (after, found) = DockLayoutMutator.RemovePane(workingLayout, hideOp.ToolWindow);
                    if (found)
                    {
                        var container = DockLayoutMutator.FindContainer(workingLayout, hideOp.ToolWindow);
                        if (container is not null) PreviousContainerTracker.Set(hideOp.ToolWindow, container);
                        workingLayout = after;
                        layoutChanged = true;
                    }
                    // Park the pane on the Left strip by default — the
                    // remembered-side heuristic lands when ToolWindow
                    // carries a PreferredSide hint (spec §2.5 follow-up).
                    (workingLeft, sidesChanged) = AddToSide(workingLeft, hideOp.ToolWindow, sidesChanged);
                    manager.OnToolWindowHidden?.Invoke(new DockToolWindowHiddenEventArgs { ToolWindow = hideOp.ToolWindow });
                    DockHostLiveAnnouncer.Announce(manager,
                        DockingStrings.LiveAnnouncement(DockingStringKeys.LiveHidden, hideOp.ToolWindow.Title));
                    break;
                }

                case PendingMutation.ShowOp showOp:
                {
                    // Re-attach the pane to its previous container if the
                    // §2.15 tracker remembers one; otherwise fall back to
                    // Center insertion at the root.
                    var changingArgs = new DockLayoutChangingEventArgs();
                    manager.OnLayoutChanging?.Invoke(changingArgs);
                    if (changingArgs.Cancel) break;
                    (workingLeft, sidesChanged)   = RemoveFromSide(workingLeft, showOp.Content, sidesChanged);
                    (workingTop, sidesChanged)    = RemoveFromSide(workingTop, showOp.Content, sidesChanged);
                    (workingRight, sidesChanged)  = RemoveFromSide(workingRight, showOp.Content, sidesChanged);
                    (workingBottom, sidesChanged) = RemoveFromSide(workingBottom, showOp.Content, sidesChanged);
                    // §2.15 — IDockLayoutStrategy.BeforeInsertToolWindow
                    // override path. Apps that want to route a re-shown
                    // tool window somewhere other than its remembered
                    // container return `true` from the strategy hook
                    // (and place the pane themselves via the model's
                    // public mutators). The strategy short-circuits the
                    // ShowFromHistory fallback.
                    bool strategyHandled = false;
                    if (model.LayoutStrategy is { } strategy && showOp.Content is ToolWindow tw)
                    {
                        strategyHandled = strategy.BeforeInsertToolWindow(model, tw);
                    }
                    if (!strategyHandled)
                    {
                        workingLayout = DockLayoutMutator.ShowFromHistory(
                            workingLayout, showOp.Content, DockTarget.Center);
                        layoutChanged = true;
                    }
                    PreviousContainerTracker.Clear(showOp.Content);
                    DockHostLiveAnnouncer.Announce(manager,
                        DockingStrings.LiveAnnouncement(DockingStringKeys.LiveShown, showOp.Content.Title));
                    break;
                }

                case PendingMutation.CloseOp closeOp:
                {
                    // §5.3.5 / §5.3.8 — ToolWindows with CanHide=true take
                    // the hide path; everything else closes.
                    if (closeOp.Content is ToolWindow tw && tw.CanHide)
                    {
                        var hidingArgs = new DockToolWindowHidingEventArgs { ToolWindow = tw };
                        manager.OnToolWindowHiding?.Invoke(hidingArgs);
                        if (hidingArgs.Cancel) break;
                        var (after, found) = DockLayoutMutator.RemovePane(workingLayout, tw);
                        if (found)
                        {
                            var container = DockLayoutMutator.FindContainer(workingLayout, tw);
                            if (container is not null) PreviousContainerTracker.Set(tw, container);
                            workingLayout = after;
                            layoutChanged = true;
                        }
                        (workingLeft, sidesChanged) = AddToSide(workingLeft, tw, sidesChanged);
                        manager.OnToolWindowHidden?.Invoke(new DockToolWindowHiddenEventArgs { ToolWindow = tw });
                        DockHostLiveAnnouncer.Announce(manager,
                            DockingStrings.LiveAnnouncement(DockingStringKeys.LiveHidden, tw.Title));
                        break;
                    }

                    if (closeOp.Content is ToolWindow twClose)
                    {
                        var closingArgs = new DockToolWindowClosingEventArgs { ToolWindow = twClose };
                        manager.OnToolWindowClosing?.Invoke(closingArgs);
                        if (closingArgs.Cancel) break;
                        var (after, found) = DockLayoutMutator.RemovePane(workingLayout, twClose);
                        if (!found) break;
                        var container = DockLayoutMutator.FindContainer(workingLayout, twClose);
                        if (container is not null) PreviousContainerTracker.Set(twClose, container);
                        workingLayout = after;
                        layoutChanged = true;
                        manager.OnToolWindowClosed?.Invoke(new DockToolWindowClosedEventArgs { ToolWindow = twClose });
                        DockHostLiveAnnouncer.Announce(manager,
                            DockingStrings.LiveAnnouncement(DockingStringKeys.LiveClosed, twClose.Title));
                        break;
                    }

                    // Document / bare DockableContent close.
                    var docClosingArgs = new DockDocumentClosingEventArgs { Document = closeOp.Content };
                    manager.OnDocumentClosing?.Invoke(docClosingArgs);
                    if (docClosingArgs.Cancel) break;
                    var (afterDoc, foundDoc) = DockLayoutMutator.RemovePane(workingLayout, closeOp.Content);
                    if (!foundDoc) break;
                    var docContainer = DockLayoutMutator.FindContainer(workingLayout, closeOp.Content);
                    if (docContainer is not null) PreviousContainerTracker.Set(closeOp.Content, docContainer);
                    workingLayout = afterDoc;
                    layoutChanged = true;
                    manager.OnDocumentClosed?.Invoke(new DockDocumentClosedEventArgs { Document = closeOp.Content });
                    DockHostLiveAnnouncer.Announce(manager,
                        DockingStrings.LiveAnnouncement(DockingStringKeys.LiveClosed, closeOp.Content.Title));
                    break;
                }

                case PendingMutation.ActivateOp activateOp:
                {
                    var prevContent = ResolvePane(workingLayout, newActiveKey);
                    newActiveKey = activateOp.Content.Key;
                    newActiveContent = activateOp.Content;
                    activeChanged = true;
                    manager.OnActiveContentChanged?.Invoke(new DockActiveContentChangedEventArgs
                    {
                        ActiveContent = activateOp.Content,
                        PreviousContent = prevContent,
                    });
                    break;
                }

                case PendingMutation.PinToSideOp pinOp:
                {
                    // PinToSide removes the tool window from the docked
                    // tree (if present) and lands it on the requested
                    // side strip. No dedicated lifecycle event today —
                    // OnLayoutChanged covers it via the wrapper at the
                    // host call site.
                    var (after, found) = DockLayoutMutator.RemovePane(workingLayout, pinOp.ToolWindow);
                    if (found)
                    {
                        var container = DockLayoutMutator.FindContainer(workingLayout, pinOp.ToolWindow);
                        if (container is not null) PreviousContainerTracker.Set(pinOp.ToolWindow, container);
                        workingLayout = after;
                        layoutChanged = true;
                    }
                    // Remove from any other side strip first so a re-pin
                    // from Left → Bottom doesn't leave the pane on both.
                    (workingLeft, sidesChanged)   = RemoveFromSide(workingLeft, pinOp.ToolWindow, sidesChanged);
                    (workingTop, sidesChanged)    = RemoveFromSide(workingTop, pinOp.ToolWindow, sidesChanged);
                    (workingRight, sidesChanged)  = RemoveFromSide(workingRight, pinOp.ToolWindow, sidesChanged);
                    (workingBottom, sidesChanged) = RemoveFromSide(workingBottom, pinOp.ToolWindow, sidesChanged);
                    switch (pinOp.Side)
                    {
                        case DockSide.Left:
                            (workingLeft, sidesChanged) = AddToSide(workingLeft, pinOp.ToolWindow, sidesChanged);
                            break;
                        case DockSide.Top:
                            (workingTop, sidesChanged) = AddToSide(workingTop, pinOp.ToolWindow, sidesChanged);
                            break;
                        case DockSide.Right:
                            (workingRight, sidesChanged) = AddToSide(workingRight, pinOp.ToolWindow, sidesChanged);
                            break;
                        case DockSide.Bottom:
                            (workingBottom, sidesChanged) = AddToSide(workingBottom, pinOp.ToolWindow, sidesChanged);
                            break;
                    }
                    DockHostLiveAnnouncer.Announce(manager,
                        DockingStrings.LiveAnnouncement(DockingStringKeys.LivePinned, pinOp.ToolWindow.Title));
                    break;
                }
            }
        }

        return new DrainResult(
            workingLayout, layoutChanged,
            workingLeft, workingTop, workingRight, workingBottom, sidesChanged,
            newActiveKey, newActiveContent, activeChanged,
            routingFallbacks);
    }

    private static (IReadOnlyList<DockableContent>? List, bool Changed) AddToSide(
        IReadOnlyList<DockableContent>? side, DockableContent pane, bool alreadyChanged)
    {
        if (side is not null)
        {
            foreach (var existing in side)
                if (ReferenceEquals(existing, pane)) return (side, alreadyChanged);
        }
        var count = side?.Count ?? 0;
        var next = new DockableContent[count + 1];
        if (side is not null)
            for (int i = 0; i < count; i++) next[i] = side[i];
        next[count] = pane;
        return (next, true);
    }

    private static (IReadOnlyList<DockableContent>? List, bool Changed) RemoveFromSide(
        IReadOnlyList<DockableContent>? side, DockableContent pane, bool alreadyChanged)
    {
        if (side is null or { Count: 0 }) return (side, alreadyChanged);
        int idx = -1;
        for (int i = 0; i < side.Count; i++)
            if (ReferenceEquals(side[i], pane)) { idx = i; break; }
        if (idx < 0) return (side, alreadyChanged);
        if (side.Count == 1) return (Array.Empty<DockableContent>(), true);
        var next = new DockableContent[side.Count - 1];
        int j = 0;
        for (int i = 0; i < side.Count; i++)
            if (i != idx) next[j++] = side[i];
        return (next, true);
    }

    /// <summary>
    /// Per-host wrapper around the docked layout root that distinguishes
    /// "no override — use <see cref="DockManager.Layout"/>" (state value
    /// is null) from "override with an explicit Root, which may itself be
    /// null after closing the last pane" (state value is a non-null
    /// record whose <see cref="Root"/> is null). Without the wrapper,
    /// closing the only pane sets the override to null and the next
    /// render falls back to the controlled-input prop, resurrecting the
    /// closed pane on screen.
    /// </summary>
    /// <remarks>
    /// Spec 045 §2.30 — <c>Root</c> is shape-only: leaf
    /// <see cref="DockableContent"/> records have only their
    /// <see cref="DockableContent.Key"/> retained (no Content / Title /
    /// CanClose / etc.). The host resolves the effective layout per
    /// render by calling <c>DockLayoutMutator.ResolveContents(Root,
    /// manager.Layout)</c>, which substitutes each shape leaf with the
    /// full <see cref="DockableContent"/> record from the app's prop
    /// (matched by Key). This is what lets app state (selection, etc.)
    /// flow into pane bodies AFTER a user-driven drag has set the
    /// override — without the app having to walk + refresh the tree
    /// itself.
    /// </remarks>
    private sealed record LayoutOverride(DockNode? Root);

    /// <summary>
    /// Per-host snapshot of the four side strips, layered on top of the
    /// controlled-input element props by the §2.16 drain when
    /// programmatic Hide / PinToSide mutations have moved panes between
    /// the docked tree and a side strip. A null entry means "no
    /// override — read from the element's matching prop"; an empty list
    /// means "the strip is intentionally cleared".
    /// </summary>
    private sealed record SideOverride(
        IReadOnlyList<DockableContent>? Left,
        IReadOnlyList<DockableContent>? Top,
        IReadOnlyList<DockableContent>? Right,
        IReadOnlyList<DockableContent>? Bottom);

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
