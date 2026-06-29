using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #721 — the reconciler skip fast-path
/// (<see cref="Element.ShallowEquals"/> → <see cref="Element.ModifiersEqual"/> →
/// ModifierCallbacksEqual) deliberately ignores the gesture
/// (Pan/Pinch/Rotate/LongPress) and drag-drop (DragSource/DropTarget) slots, so a
/// per-render gesture/drag closure stays skip-eligible. Those handlers dispatch
/// through cached per-element state (<c>GestureState.Pan</c> / <c>DragDropState.
/// Source</c>/<c>.Target</c>) that is refreshed on the non-skip Update path only.
///
/// Before the fix, a skipped render strands the PREVIOUS render's closure: a gesture
/// callback fires against stale captured state. These fixtures mount a live element,
/// change ONLY the gesture/drag closure across a render the reconciler skips, and
/// assert the LATEST closure is what dispatch sees — proving the skip arms now refresh
/// the cached dispatch state without re-subscribing the trampolines.
/// </summary>
internal static class SkipPathGestureDragFixtures
{
    /// <summary>
    /// A live Rectangle carries a per-render <c>.OnPan</c> closure that captures a
    /// <c>tick</c> counter and writes it into <c>fired</c>. The Rectangle is otherwise
    /// structurally constant, so a tick bump is skipped by the child-skip arm (the
    /// gesture slot is excluded from the diff). Invoking the cached pan closure must
    /// then fire the CURRENT (tick=1) delegate, not the stale (tick=0) one.
    /// </summary>
    internal class SkipRefreshesLivePanClosure(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                var (fired, setFired) = ctx.UseState(-1);

                // Fresh pan closure every render, capturing the CURRENT tick. Everything
                // else about the Rectangle is constant, so on a tick bump the reconciler
                // takes the gesture-excluding skip arm — the only thing that changed is
                // this excluded slot.
                return VStack(
                    TextBlock($"panfired:{fired}"),
                    Button("PanBumpTick", () => setTick(tick + 1)),
                    Factories.Rectangle()
                        .Size(80, 80)
                        .OnPan(_ => setFired(tick), axis: PanAxis.Both));
            });

            await Harness.Render();
            H.Check("SkipPan_FiredInitial", H.FindText("panfired:-1") is not null);

            // Re-render with a NEW pan closure (captures tick=1); the Rectangle is
            // structurally identical so the real reconciler skips it.
            H.ClickButton("PanBumpTick");
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("SkipPan_TargetMounted", rect is not null);

            var state = rect is not null ? Reconciler.DebugTryGetGestureState(rect) : null;
            H.Check("SkipPan_GestureStateCached", state?.Pan is not null);

            // Dispatch through the cached pan config exactly as the manipulation
            // trampoline would. Post-fix this is the tick=1 closure; pre-fix it is the
            // stale tick=0 closure.
            state?.Pan?.OnChanged(default);
            await Harness.Render();

            H.Check("SkipPan_CurrentClosureInvoked", H.FindText("panfired:1") is not null);
            H.Check("SkipPan_StaleClosureNotInvoked", H.FindText("panfired:0") is null);
        }
    }

    /// <summary>
    /// Same shape via the keyed prefix skip arm: the Rectangle is given a stable key so
    /// reconciliation takes the keyed path, whose prefix loop has its own
    /// CanSkipUpdate short-circuit. Proves the keyed arms refresh too.
    /// </summary>
    internal class SkipRefreshesLivePanClosure_Keyed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                var (fired, setFired) = ctx.UseState(-1);

                return VStack(
                    TextBlock($"kpanfired:{fired}"),
                    Button("KPanBumpTick", () => setTick(tick + 1)),
                    // Keyed children force ReconcileKeyed; the unchanged-but-for-gesture
                    // Rectangle rides the keyed prefix skip arm.
                    VStack(
                        Factories.Rectangle()
                            .Size(40, 40)
                            .WithKey("kpan-a")
                            .OnPan(_ => setFired(tick), axis: PanAxis.Both),
                        TextBlock("kpan-tail").WithKey("kpan-b")));
            });

            await Harness.Render();
            H.Check("SkipKPan_FiredInitial", H.FindText("kpanfired:-1") is not null);

            H.ClickButton("KPanBumpTick");
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("SkipKPan_TargetMounted", rect is not null);

            var state = rect is not null ? Reconciler.DebugTryGetGestureState(rect) : null;
            H.Check("SkipKPan_GestureStateCached", state?.Pan is not null);

            state?.Pan?.OnChanged(default);
            await Harness.Render();

            H.Check("SkipKPan_CurrentClosureInvoked", H.FindText("kpanfired:1") is not null);
            H.Check("SkipKPan_StaleClosureNotInvoked", H.FindText("kpanfired:0") is null);
        }
    }

    /// <summary>
    /// Drag-drop counterpart. A per-render <c>.OnDrop</c> closure is stashed into a
    /// reference-stable holder; after a skipped render the cached
    /// <c>DragDropState.Target.OnDrop</c> must be the LATEST closure instance, not the
    /// first. (Dispatching a real drag needs sealed DragEventArgs, so we assert on the
    /// cached config identity that the trampolines read at dispatch time.)
    /// </summary>
    internal class SkipRefreshesLiveDropTarget(Harness h) : SelfTestFixtureBase(h)
    {
        // Reference-stable holder the render writes the current handler into. Its identity
        // never changes, so it never drives a re-render — it only lets the test observe
        // which drop closure the latest render produced.
        private sealed class HandlerHolder { public Action<DragTargetArgs>? Latest; }

        public override async Task RunAsync()
        {
            var holder = new HandlerHolder();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);

                // Fresh drop closure every render; otherwise the Rectangle is constant.
                Action<DragTargetArgs> dropHandler = _ => { var _t = tick; };
                holder.Latest = dropHandler;

                return VStack(
                    TextBlock($"droptick:{tick}"),
                    Button("DropBumpTick", () => setTick(tick + 1)),
                    Factories.Rectangle()
                        .Size(80, 80)
                        .OnDrop(dropHandler));
            });

            await Harness.Render();
            var firstHandler = holder.Latest;
            H.Check("SkipDrop_FirstHandlerCaptured", firstHandler is not null);

            // Re-render with a NEW drop closure; the Rectangle is otherwise structurally
            // identical so the reconciler skips it (drag slots excluded from the diff).
            H.ClickButton("DropBumpTick");
            await Harness.Render();
            var secondHandler = holder.Latest;
            H.Check("SkipDrop_SecondHandlerDistinct",
                secondHandler is not null && !ReferenceEquals(firstHandler, secondHandler));

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("SkipDrop_TargetMounted", rect is not null);

            var state = rect is not null ? Reconciler.DebugTryGetDndState(rect) : null;
            H.Check("SkipDrop_DndStateCached", state?.Target is not null);

            // Post-fix the cached target carries the latest closure; pre-fix it is the stale first.
            H.Check("SkipDrop_CachedTargetIsLatest", ReferenceEquals(state?.Target?.OnDrop, secondHandler));
            H.Check("SkipDrop_CachedTargetNotStale", !ReferenceEquals(state?.Target?.OnDrop, firstHandler));
        }
    }

    /// <summary>
    /// #721 mid-gesture SAFETY proof. The simple stale-closure repros above show the
    /// refresh swaps the dispatch target; this proves the refresh is non-destructive to
    /// an IN-FLIGHT gesture: it does NOT re-subscribe the once-per-lifetime trampolines
    /// and does NOT reset the in-flight cursor — so a mid-interaction skip neither drops
    /// the gesture nor double-dispatches Began/Ended.
    ///
    /// We simulate "Began already fired" by setting the live cursor on the cached state
    /// (a real ManipulationDelta can't be synthesized — its args are sealed). Then a
    /// skip-path refresh is driven by bumping unrelated state, and we assert: (a) the
    /// cached pan config is the LATEST closure (staleness fix), (b) every manipulation
    /// trampoline delegate is the SAME instance (no re-subscribe → no duplicate event
    /// delivery), and (c) PanBeganDispatched is still true (cursor preserved → the delta
    /// path's `if (!PanBeganDispatched)` Began gate stays closed → no second Began).
    /// Finally we dispatch Changed + Ended through the cached config and confirm the
    /// LATEST closure receives them.
    /// </summary>
    internal class SkipMidGesturePreservesCursorsAndSubscriptions(Harness h) : SelfTestFixtureBase(h)
    {
        // Reference-stable event log the per-render closures write into. Identity never
        // changes, so it never drives a re-render — it just records which closure ran.
        private sealed class EventLog { public readonly List<(string Phase, int Tick)> Entries = new(); }

        public override async Task RunAsync()
        {
            var log = new EventLog();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);

                // Per-render pan config capturing the CURRENT tick into the log. The
                // Rectangle is otherwise constant, so a tick bump is taken by the skip arm.
                return VStack(
                    TextBlock($"mgtick:{tick}"),
                    Button("MgBumpTick", () => setTick(tick + 1)),
                    Factories.Rectangle()
                        .Size(80, 80)
                        .OnPan(
                            onChanged: _ => log.Entries.Add(("Changed", tick)),
                            onEnded: _ => log.Entries.Add(("Ended", tick)),
                            onBegan: _ => log.Entries.Add(("Began", tick)),
                            axis: PanAxis.Both));
            });

            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("MidGesture_TargetMounted", rect is not null);
            var state = rect is not null ? Reconciler.DebugTryGetGestureState(rect) : null;
            H.Check("MidGesture_GestureStateCached", state?.Pan is not null);

            // Capture the once-per-lifetime trampoline delegates + simulate an in-flight
            // gesture (Began already dispatched, partway through a drag).
            var started0 = state!.StartedTrampoline;
            var delta0 = state.DeltaTrampoline;
            var completed0 = state.CompletedTrampoline;
            var inertia0 = state.InertiaStartingTrampoline;
            state.PanBeganDispatched = true;
            state.PanLastTranslation = new global::Windows.Foundation.Point(12, 0);

            // Drive the skip-path refresh: bump unrelated state so the child-skip arm
            // engages while the gesture is "in flight".
            H.ClickButton("MgBumpTick");
            await Harness.Render();

            // (a) staleness fix — cached config is the latest (tick=1) closure.
            state.Pan!.OnChanged(default);
            H.Check("MidGesture_LatestClosureGetsChanged",
                log.Entries.Count > 0 && log.Entries[^1] == ("Changed", 1));
            state.Pan!.OnEnded?.Invoke(default);
            H.Check("MidGesture_LatestClosureGetsEnded",
                log.Entries.Count > 0 && log.Entries[^1] == ("Ended", 1));

            // (b) safety — no trampoline was re-subscribed (same delegate instances).
            H.Check("MidGesture_StartedTrampolineUnchanged", ReferenceEquals(state.StartedTrampoline, started0));
            H.Check("MidGesture_DeltaTrampolineUnchanged", ReferenceEquals(state.DeltaTrampoline, delta0));
            H.Check("MidGesture_CompletedTrampolineUnchanged", ReferenceEquals(state.CompletedTrampoline, completed0));
            H.Check("MidGesture_InertiaTrampolineUnchanged", ReferenceEquals(state.InertiaStartingTrampoline, inertia0));

            // (c) safety — the in-flight cursor was NOT reset, so the delta path would not
            // re-fire Began; and the refresh itself dispatched no Began (no duplicate).
            H.Check("MidGesture_CursorPreserved", state.PanBeganDispatched);
            H.Check("MidGesture_NoDuplicateBegan", !log.Entries.Exists(e => e.Phase == "Began"));
        }
    }

    /// <summary>
    /// #721 H1 — gesture PRESENCE TRANSITION (add-first) on the skip path. A gesture slot
    /// is excluded from the skip predicate, so adding <c>.OnPan</c> to an otherwise
    /// skip-equal element takes the skip arm. The config-only refresh can't wire a brand
    /// new gesture (no GestureState exists yet), so the skip path must route an
    /// add-transition to the full ApplyGestureHandlers — otherwise the gesture is silently
    /// dead. Asserts the gesture actually wires (state + ManipulationMode) and fires.
    /// </summary>
    internal class SkipAddFirstGestureWires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (hasGesture, setHasGesture) = ctx.UseState(false);
                var (fired, setFired) = ctx.UseState(false);

                var rect = Factories.Rectangle().Size(80, 80);
                // Gesture appears only on the 2nd render; the element is otherwise
                // structurally identical, so the reconciler skips it.
                if (hasGesture)
                    rect = rect.OnPan(_ => setFired(true), axis: PanAxis.Both);

                return VStack(
                    TextBlock($"addfired:{fired}"),
                    Button("AddGestureBtn", () => setHasGesture(true)),
                    rect);
            });

            await Harness.Render();
            var rect0 = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("AddGesture_NoStateInitially",
                rect0 is null || Reconciler.DebugTryGetGestureState(rect0)?.Pan is null);

            // Add the gesture on a skip-eligible render.
            H.ClickButton("AddGestureBtn");
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("AddGesture_TargetMounted", rect is not null);
            var state = rect is not null ? Reconciler.DebugTryGetGestureState(rect) : null;

            // Post-fix: the add-on-skip routed to the full handler → gesture wired.
            H.Check("AddGesture_Wired", state?.Pan is not null);
            H.Check("AddGesture_ManipulationModeSet",
                rect is not null && rect.ManipulationMode.HasFlag(ManipulationModes.TranslateX));

            state?.Pan?.OnChanged(default);
            await Harness.Render();
            H.Check("AddGesture_ClosureFires", H.FindText("addfired:True") is not null);
        }
    }

    /// <summary>
    /// #721 H1 — drag SOURCE removal MID-DRAG on the skip path must not strand the
    /// in-flight transfer. A naive config-only null of <c>DragDropState.Source</c> would
    /// make the pending <c>OnDropCompleted</c> early-return (<c>state.Source is null</c>),
    /// leaking the DragData registration and suppressing <c>OnEnd</c>. The remove is a
    /// presence transition routed to the full ApplyDragDropHandlers, whose in-flight guard
    /// preserves Source while a drag is active (ActiveTransferId set) yet still clears
    /// CanDrag so no NEW drag starts.
    /// </summary>
    internal class SkipRemoveDragSourceMidDragPreservesState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (hasDrag, setHasDrag) = ctx.UseState(true);

                var rect = Factories.Rectangle().Size(80, 80);
                if (hasDrag)
                    rect = rect.OnDragStart(() => new DragData());

                return VStack(
                    TextBlock("dragstate"),
                    Button("RemoveDragBtn", () => setHasDrag(false)),
                    rect);
            });

            await Harness.Render();
            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("RemoveDrag_TargetMounted", rect is not null);
            var state = rect is not null ? Reconciler.DebugTryGetDndState(rect) : null;
            H.Check("RemoveDrag_SourceWiredInitially", state?.Source is not null);
            H.Check("RemoveDrag_CanDragInitially", rect is not null && rect.CanDrag);

            // Simulate a drag in flight: DragStarting fired, transfer registered.
            if (state is not null) state.ActiveTransferId = Guid.NewGuid();
            var inflightSource = state?.Source;

            // Remove the drag source on a skip-eligible render.
            H.ClickButton("RemoveDragBtn");
            await Harness.Render();

            var state2 = rect is not null ? Reconciler.DebugTryGetDndState(rect) : null;
            // Post-fix: in-flight guard preserves Source so the pending DropCompleted can
            // still fire OnEnd + Unregister. Pre-fix the config-only null stranded it.
            H.Check("RemoveDrag_SourcePreservedMidDrag", ReferenceEquals(state2?.Source, inflightSource));
            H.Check("RemoveDrag_TransferStillInFlight", state2 is not null && state2.ActiveTransferId != Guid.Empty);
            // But a NEW drag can no longer start.
            H.Check("RemoveDrag_CanDragClearedAfterRemove", rect is not null && !rect.CanDrag);
        }
    }

    /// <summary>
    /// #721 H1 regression — the in-flight guard is in-flight-SPECIFIC: removing a drag
    /// source on the skip path while NO drag is active must still null the cached Source
    /// and clear CanDrag (full unwire), so the guard never over-preserves a stale source.
    /// </summary>
    internal class SkipRemoveDragSourceNotInFlightClears(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (hasDrag, setHasDrag) = ctx.UseState(true);
                var rect = Factories.Rectangle().Size(80, 80);
                if (hasDrag)
                    rect = rect.OnDragStart(() => new DragData());
                return VStack(
                    TextBlock("dragstate2"),
                    Button("RemoveDragBtn2", () => setHasDrag(false)),
                    rect);
            });

            await Harness.Render();
            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("RemoveDragIdle_SourceWiredInitially",
                rect is not null && Reconciler.DebugTryGetDndState(rect)?.Source is not null);

            // No ActiveTransferId set → not in flight.
            H.ClickButton("RemoveDragBtn2");
            await Harness.Render();

            var state2 = rect is not null ? Reconciler.DebugTryGetDndState(rect) : null;
            H.Check("RemoveDragIdle_SourceCleared", state2?.Source is null);
            H.Check("RemoveDragIdle_CanDragCleared", rect is not null && !rect.CanDrag);
        }
    }
}
