using global::Windows.Foundation;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Reactor.Core;

public sealed partial class Reconciler
{
    /// <summary>
    /// Per-element gesture dispatch state. Mirrors <see cref="ModifierEventHandlerState"/>'s
    /// trampoline pattern for the three manipulation events, plus per-gesture cursors
    /// that track distance thresholds, start anchors, and inertia flags so callback
    /// contracts stay clean ("<see cref="GesturePhase.Began"/> fires exactly once").
    /// </summary>
    internal sealed class GestureState
    {
        public PanGestureConfig? Pan;
        public PinchGestureConfig? Pinch;
        public RotateGestureConfig? Rotate;
        public LongPressGestureConfig? LongPress;

        // Pan cursor — tracks whether we've crossed the MinimumDistance threshold.
        public bool PanBeganDispatched;
        public Point PanStart;
        public Point PanLastTranslation;

        // Pinch cursor
        public bool PinchBeganDispatched;

        // Rotate cursor
        public bool RotateBeganDispatched;

        // Stable trampolines (attached once per element lifetime).
        public ManipulationStartedEventHandler? StartedTrampoline;
        public ManipulationDeltaEventHandler? DeltaTrampoline;
        public ManipulationCompletedEventHandler? CompletedTrampoline;
        public ManipulationInertiaStartingEventHandler? InertiaStartingTrampoline;

        // Whether inertia has started on the current manipulation.
        public bool InertiaActive;

        // LongPress trampolines (attached once per element lifetime).
        public HoldingEventHandler? LongPressHoldingTrampoline;
        public PointerEventHandler? LongPressPointerPressedTrampoline;
        public PointerEventHandler? LongPressPointerReleasedTrampoline;
        public PointerEventHandler? LongPressPointerMovedTrampoline;
        public PointerEventHandler? LongPressPointerCaptureLostTrampoline;

        // LongPress mouse-emulation cursor.
        public Microsoft.UI.Xaml.DispatcherTimer? LongPressMouseTimer;
        public Point LongPressPressedPosition;
        public DateTime LongPressPressedTime;
        public uint LongPressActivePointerId;
        public bool LongPressMouseArmed;
        public bool LongPressTriggeredForCurrentPress;
    }

    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, GestureState> _gestureStates = new();

    private static GestureState GetOrCreateGestureState(FrameworkElement fe)
    {
        if (!_gestureStates.TryGetValue(fe, out var state))
        {
            state = new GestureState();
            _gestureStates.AddOrUpdate(fe, state);
        }
        return state;
    }

    // Test-only accessor (InternalsVisibleTo Reactor.Tests / Reactor.AppTests.Host).
    // Lets the #721 skip-path regression fixture read the cached gesture dispatch
    // closures so it can invoke the latest OnChanged after a skipped render.
    internal static GestureState? DebugTryGetGestureState(FrameworkElement fe)
        => _gestureStates.TryGetValue(fe, out var state) ? state : null;

    /// <summary>
    /// #721 — cheap predicate: does this modifier set carry any gesture
    /// (Pan/Pinch/Rotate/LongPress) or drag-drop (DragSource/DropTarget) slot? Used to
    /// gate the skip-path dispatch-state refresh so a leaf with no gesture/drag slot
    /// (every grid cell) never probes the gesture/drag CWTs or fetches its control.
    /// </summary>
    internal static bool HasGestureOrDragSlots(ElementModifiers? m)
        => m is not null
            && (m.Pan is not null || m.Pinch is not null || m.Rotate is not null
                || m.LongPress is not null || m.DragSource is not null || m.DropTarget is not null);

    // Shared empty sentinel so a (theoretically) null newM on a remove transition can be
    // passed to the Apply* handlers (which require a non-null modifier set) without
    // allocating. On the skip path newM is never null when a transition is detected
    // (ModifiersEqual requires both sides non-null or both null), so this is defensive.
    private static readonly ElementModifiers _emptyModifiers = new();

    /// <summary>
    /// #721 — keep the cached gesture/drag dispatch state correct when the reconciler
    /// takes its skip fast-path for an element whose gesture/drag slots changed (those
    /// slots are excluded from the skip predicate, so such an element is skip-eligible).
    /// Two distinct cases, handled differently:
    ///
    /// <para><b>Presence transition</b> (a family is present on exactly one side — an
    /// ADD or a REMOVE-last): route to the full <see cref="ApplyGestureHandlers"/> /
    /// <see cref="ApplyDragDropHandlers"/>. The config-only refresh below is NOT enough
    /// here — an ADD has no <see cref="GestureState"/>/<see cref="DragDropState"/> yet
    /// (so a <c>TryGetValue</c>-only path would no-op and the gesture/drag would never
    /// wire — silently dead), and a REMOVE must clear the platform flags
    /// (<c>ManipulationMode</c> / <c>CanDrag</c> / <c>AllowDrop</c>), which only the full
    /// handler does. A transition means the family was inactive on one side, so the full
    /// Apply is safe: there is no in-flight gesture of a not-yet/​no-longer-present family
    /// to re-arm (and a mid-drag source removal is preserved by the in-flight guard in
    /// <see cref="ApplyDragDropHandlers"/> so its pending DropCompleted still fires).</para>
    ///
    /// <para><b>Identity change</b> (a family is present on BOTH sides, only the captured
    /// closure changed): overwrite just the mutable config fields the stable trampolines
    /// read at dispatch time. This swaps the dispatch target to the latest closure —
    /// mirroring how the Tag / <c>ModifierEventHandlerState</c> refresh keeps routed
    /// handlers current on skip — while leaving the trampolines and the in-flight gesture
    /// cursors (<c>PanBeganDispatched</c>, the LongPress timer, …) untouched, so an
    /// in-flight gesture is never re-armed mid-interaction (no Began/Ended
    /// double-dispatch). <c>TryGetValue</c> (not GetOrCreate) never allocates: an
    /// already-wired family always has its state.</para>
    /// </summary>
    internal static void RefreshGestureDragStateOnSkip(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers? newM)
    {
        // ── Gesture family (Pan/Pinch/Rotate/LongPress) ──
        bool gestureNow = newM is not null
            && (newM.Pan is not null || newM.Pinch is not null || newM.Rotate is not null || newM.LongPress is not null);
        bool gestureBefore = oldM is not null
            && (oldM.Pan is not null || oldM.Pinch is not null || oldM.Rotate is not null || oldM.LongPress is not null);
        if (gestureNow != gestureBefore)
        {
            // ADD or REMOVE-last — wire / unwire through the full handler.
            ApplyGestureHandlers(fe, oldM, newM ?? _emptyModifiers);
        }
        else if (gestureNow && _gestureStates.TryGetValue(fe, out var gs))
        {
            // Already-wired identity change — config-only refresh (mid-gesture-safe).
            gs.Pan = newM!.Pan;
            gs.Pinch = newM.Pinch;
            gs.Rotate = newM.Rotate;
            gs.LongPress = newM.LongPress;
        }

        // ── Drag-drop family (DragSource/DropTarget) ──
        bool dragNow = newM is not null && (newM.DragSource is not null || newM.DropTarget is not null);
        bool dragBefore = oldM is not null && (oldM.DragSource is not null || oldM.DropTarget is not null);
        if (dragNow != dragBefore)
        {
            // ADD or REMOVE-last — wire / unwire (CanDrag/AllowDrop + trampolines + the
            // in-flight-drag guard so a mid-drag source removal still completes cleanly).
            ApplyDragDropHandlers(fe, oldM, newM ?? _emptyModifiers);
        }
        else if (dragNow && _dndStates.TryGetValue(fe, out var ds))
        {
            ds.Source = newM!.DragSource;
            ds.Target = newM.DropTarget;
        }
    }

    /// <summary>
    /// Computes the union of <see cref="ManipulationModes"/> flags required by the
    /// currently-attached gestures. Returns <see cref="ManipulationModes.None"/> when
    /// no gesture is attached — the caller decides whether to clobber the control's
    /// existing mode.
    /// </summary>
    internal static ManipulationModes ComputeManipulationMode(ElementModifiers m)
    {
        var mode = ManipulationModes.None;

        if (m.Pan is { } pan)
        {
            switch (pan.Axis)
            {
                case PanAxis.Both:
                    mode |= ManipulationModes.TranslateX | ManipulationModes.TranslateY;
                    if (pan.WithInertia)
                        mode |= ManipulationModes.TranslateInertia;
                    break;
                case PanAxis.Horizontal:
                    mode |= ManipulationModes.TranslateX;
                    if (pan.WithInertia)
                        mode |= ManipulationModes.TranslateInertia;
                    break;
                case PanAxis.Vertical:
                    mode |= ManipulationModes.TranslateY;
                    if (pan.WithInertia)
                        mode |= ManipulationModes.TranslateInertia;
                    break;
            }
        }

        if (m.Pinch is { } pinch)
        {
            mode |= ManipulationModes.Scale;
            if (pinch.WithInertia)
                mode |= ManipulationModes.ScaleInertia;
        }

        if (m.Rotate is { } rotate)
        {
            mode |= ManipulationModes.Rotate;
            if (rotate.WithInertia)
                mode |= ManipulationModes.RotateInertia;
        }

        return mode;
    }

    private static void ApplyGestureHandlers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // Fast path
        if (m.Pan is null && m.Pinch is null && m.Rotate is null && m.LongPress is null
            && oldM?.Pan is null && oldM?.Pinch is null && oldM?.Rotate is null && oldM?.LongPress is null)
            return;

        var state = GetOrCreateGestureState(fe);
        state.Pan = m.Pan;
        state.Pinch = m.Pinch;
        state.Rotate = m.Rotate;
        state.LongPress = m.LongPress;

        // Recompute ManipulationMode only when the set of gestures is non-empty —
        // respects a user-set .Set(r => r.ManipulationMode = ...) otherwise.
        var mode = ComputeManipulationMode(m);
        if (mode != ManipulationModes.None)
            fe.ManipulationMode = mode;

        // Lazy-attach manipulation trampolines (one-time). Skip when only LongPress is wired.
        bool needsManipulation = m.Pan is not null || m.Pinch is not null || m.Rotate is not null
            || oldM?.Pan is not null || oldM?.Pinch is not null || oldM?.Rotate is not null;
        if (needsManipulation)
        {
            if (state.StartedTrampoline is null)
            {
                state.StartedTrampoline = (s, e) => OnManipulationStarted(state, e);
                fe.ManipulationStarted += state.StartedTrampoline;
            }
            if (state.DeltaTrampoline is null)
            {
                state.DeltaTrampoline = (s, e) => OnManipulationDelta(fe, state, e);
                fe.ManipulationDelta += state.DeltaTrampoline;
            }
            if (state.CompletedTrampoline is null)
            {
                state.CompletedTrampoline = (s, e) => OnManipulationCompleted(state, e);
                fe.ManipulationCompleted += state.CompletedTrampoline;
            }
            if (state.InertiaStartingTrampoline is null)
            {
                state.InertiaStartingTrampoline = (s, e) => { state.InertiaActive = true; };
                fe.ManipulationInertiaStarting += state.InertiaStartingTrampoline;
            }
        }

        // LongPress trampolines — touch/pen via Holding; mouse via pointer timer.
        if (m.LongPress is not null)
        {
            fe.IsHoldingEnabled = true;

            if (state.LongPressHoldingTrampoline is null)
            {
                state.LongPressHoldingTrampoline = (s, e) => OnLongPressHolding(fe, state, e);
                fe.Holding += state.LongPressHoldingTrampoline;
            }

            // Use AddHandler with handledEventsToo:true so mouse emulation still arms when
            // the target is a Control like Button that marks PointerPressed as handled
            // to drive its own Click logic. Without this, .OnLongPress on a Button would
            // silently no-op under mouse input.
            if (state.LongPressPointerPressedTrampoline is null)
            {
                state.LongPressPointerPressedTrampoline = (s, e) => OnLongPressPointerPressed(fe, state, e);
                fe.AddHandler(UIElement.PointerPressedEvent, state.LongPressPointerPressedTrampoline, handledEventsToo: true);
            }
            if (state.LongPressPointerReleasedTrampoline is null)
            {
                state.LongPressPointerReleasedTrampoline = (s, e) => OnLongPressPointerEnded(fe, state, e, cancelled: false);
                fe.AddHandler(UIElement.PointerReleasedEvent, state.LongPressPointerReleasedTrampoline, handledEventsToo: true);
            }
            if (state.LongPressPointerCaptureLostTrampoline is null)
            {
                state.LongPressPointerCaptureLostTrampoline = (s, e) => OnLongPressPointerEnded(fe, state, e, cancelled: true);
                fe.AddHandler(UIElement.PointerCaptureLostEvent, state.LongPressPointerCaptureLostTrampoline, handledEventsToo: true);
            }
            if (state.LongPressPointerMovedTrampoline is null)
            {
                state.LongPressPointerMovedTrampoline = (s, e) => OnLongPressPointerMoved(fe, state, e);
                fe.AddHandler(UIElement.PointerMovedEvent, state.LongPressPointerMovedTrampoline, handledEventsToo: true);
            }
        }
    }

    // ── LongPress dispatch ──────────────────────────────────────────────

    private static void OnLongPressHolding(FrameworkElement fe, GestureState state, HoldingRoutedEventArgs e)
    {
        if (state.LongPress is not { } cfg) return;

        var phase = e.HoldingState switch
        {
            Microsoft.UI.Input.HoldingState.Started => GesturePhase.Began,
            Microsoft.UI.Input.HoldingState.Completed => GesturePhase.Ended,
            Microsoft.UI.Input.HoldingState.Canceled => GesturePhase.Cancelled,
            _ => GesturePhase.Began,
        };

        var pos = e.GetPosition(fe);
        cfg.OnTriggered(new LongPressGesture(
            Position: pos,
            Duration: cfg.MinimumDuration,
            Phase: phase));
    }

    private static void OnLongPressPointerPressed(FrameworkElement fe, GestureState state, PointerRoutedEventArgs e)
    {
        if (state.LongPress is not { } cfg) return;
        if (!cfg.EnableMouseEmulation) return;
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;

        state.LongPressActivePointerId = e.Pointer.PointerId;
        state.LongPressPressedPosition = e.GetCurrentPoint(fe).Position;
        state.LongPressPressedTime = DateTime.UtcNow;
        state.LongPressTriggeredForCurrentPress = false;
        state.LongPressMouseArmed = true;

        // Arm timer (lazily created, reused).
        var timer = state.LongPressMouseTimer;
        if (timer is null)
        {
            timer = new DispatcherTimer();
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!state.LongPressMouseArmed) return;
                if (state.LongPress is not { } liveCfg) return;
                state.LongPressMouseArmed = false;
                state.LongPressTriggeredForCurrentPress = true;
                liveCfg.OnTriggered(new LongPressGesture(
                    Position: state.LongPressPressedPosition,
                    Duration: liveCfg.MinimumDuration,
                    Phase: GesturePhase.Began));
            };
            state.LongPressMouseTimer = timer;
        }
        timer.Interval = cfg.MinimumDuration;
        timer.Start();
    }

    private static void OnLongPressPointerEnded(FrameworkElement fe, GestureState state, PointerRoutedEventArgs e, bool cancelled)
    {
        if (state.LongPress is not { } cfg) return;
        if (!cfg.EnableMouseEmulation) return;
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        if (e.Pointer.PointerId != state.LongPressActivePointerId && state.LongPressActivePointerId != 0) return;

        var wasArmed = state.LongPressMouseArmed;
        var didTrigger = state.LongPressTriggeredForCurrentPress;
        state.LongPressMouseTimer?.Stop();
        state.LongPressMouseArmed = false;

        if (didTrigger)
        {
            var pos = e.GetCurrentPoint(fe).Position;
            cfg.OnTriggered(new LongPressGesture(
                Position: pos,
                Duration: DateTime.UtcNow - state.LongPressPressedTime,
                Phase: cancelled ? GesturePhase.Cancelled : GesturePhase.Ended));
        }
        else if (wasArmed && cancelled)
        {
            // Capture lost before trigger — report cancellation.
            var pos = e.GetCurrentPoint(fe).Position;
            cfg.OnTriggered(new LongPressGesture(
                Position: pos,
                Duration: DateTime.UtcNow - state.LongPressPressedTime,
                Phase: GesturePhase.Cancelled));
        }

        state.LongPressTriggeredForCurrentPress = false;
        state.LongPressActivePointerId = 0;
    }

    private static void OnLongPressPointerMoved(FrameworkElement fe, GestureState state, PointerRoutedEventArgs e)
    {
        if (state.LongPress is not { } cfg) return;
        if (!cfg.EnableMouseEmulation) return;
        if (!state.LongPressMouseArmed) return;
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        if (e.Pointer.PointerId != state.LongPressActivePointerId) return;

        var pos = e.GetCurrentPoint(fe).Position;
        var dx = pos.X - state.LongPressPressedPosition.X;
        var dy = pos.Y - state.LongPressPressedPosition.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > cfg.CancelDistance)
        {
            state.LongPressMouseTimer?.Stop();
            state.LongPressMouseArmed = false;
            // Never fired Began, so no Cancelled callback per spec contract.
        }
    }

    private static void OnManipulationStarted(GestureState state, ManipulationStartedRoutedEventArgs e)
    {
        state.PanBeganDispatched = false;
        state.PinchBeganDispatched = false;
        state.RotateBeganDispatched = false;
        state.PanStart = e.Position;
        state.PanLastTranslation = new Point(0, 0);
        state.InertiaActive = false;

        // For pinch/rotate, WinUI dispatches the first Delta right away with meaningful values;
        // we defer Began to the first delta so the gesture args carry real scale/angle data.

        // Mark handled so an outer ScrollViewer doesn't grab the same manipulation as
        // a scroll and drag the viewport along with the child.
        if (state.Pan is not null || state.Pinch is not null || state.Rotate is not null)
            e.Handled = true;
    }

    private static void OnManipulationDelta(FrameworkElement fe, GestureState state, ManipulationDeltaRoutedEventArgs e)
    {
        var inertial = state.InertiaActive || e.IsInertial;

        // Mark handled so the delta doesn't bubble to an ancestor ScrollViewer (which
        // would then treat our drag as a scroll and move the viewport underneath us).
        if (state.Pan is not null || state.Pinch is not null || state.Rotate is not null)
            e.Handled = true;

        // ── Pan ──
        if (state.Pan is { } pan)
        {
            var translation = new Point(
                e.Cumulative.Translation.X,
                e.Cumulative.Translation.Y);
            var delta = new Point(
                translation.X - state.PanLastTranslation.X,
                translation.Y - state.PanLastTranslation.Y);
            state.PanLastTranslation = translation;

            var magnitude = Math.Sqrt(translation.X * translation.X + translation.Y * translation.Y);
            bool threshold = magnitude >= pan.MinimumDistance;

            if (threshold)
            {
                if (!state.PanBeganDispatched)
                {
                    state.PanBeganDispatched = true;
                    pan.OnBegan?.Invoke(BuildPan(state, translation, delta, e, GesturePhase.Began, inertial));
                }
                pan.OnChanged(BuildPan(state, translation, delta, e, GesturePhase.Changed, inertial));
            }
        }

        // ── Pinch ──
        if (state.Pinch is { } pinch)
        {
            var g = new PinchGesture(
                Scale: e.Cumulative.Scale,
                ScaleDelta: e.Delta.Scale,
                Center: e.Position,
                Phase: state.PinchBeganDispatched ? GesturePhase.Changed : GesturePhase.Began,
                IsInertial: inertial);

            if (!state.PinchBeganDispatched)
            {
                state.PinchBeganDispatched = true;
                pinch.OnBegan?.Invoke(g);
                pinch.OnChanged(g with { Phase = GesturePhase.Changed });
            }
            else
            {
                pinch.OnChanged(g);
            }
        }

        // ── Rotate ──
        if (state.Rotate is { } rotate)
        {
            var g = new RotateGesture(
                Angle: e.Cumulative.Rotation,
                AngleDelta: e.Delta.Rotation,
                Center: e.Position,
                Phase: state.RotateBeganDispatched ? GesturePhase.Changed : GesturePhase.Began,
                IsInertial: inertial);

            if (!state.RotateBeganDispatched)
            {
                state.RotateBeganDispatched = true;
                rotate.OnBegan?.Invoke(g);
                rotate.OnChanged(g with { Phase = GesturePhase.Changed });
            }
            else
            {
                rotate.OnChanged(g);
            }
        }
    }

    private static PanGesture BuildPan(GestureState state, Point translation, Point delta,
        ManipulationDeltaRoutedEventArgs e, GesturePhase phase, bool inertial) =>
        new(
            Translation: translation,
            Delta: delta,
            Velocity: new Point(e.Velocities.Linear.X, e.Velocities.Linear.Y),
            Position: e.Position,
            StartPosition: state.PanStart,
            Phase: phase,
            IsInertial: inertial);

    private static void OnManipulationCompleted(GestureState state, ManipulationCompletedRoutedEventArgs e)
    {
        // Mark handled so the completion (and any post-release inertia frames) don't
        // bubble to an ancestor ScrollViewer.
        if (state.Pan is not null || state.Pinch is not null || state.Rotate is not null)
            e.Handled = true;

        // Pan — only fire Ended if Began fired (honor the minimum-distance contract).
        if (state.Pan is { } pan && state.PanBeganDispatched)
        {
            var translation = state.PanLastTranslation;
            pan.OnEnded?.Invoke(new PanGesture(
                Translation: translation,
                Delta: new Point(0, 0),
                Velocity: new Point(e.Velocities.Linear.X, e.Velocities.Linear.Y),
                Position: e.Position,
                StartPosition: state.PanStart,
                Phase: GesturePhase.Ended,
                IsInertial: state.InertiaActive));
        }

        if (state.Pinch is { } pinch && state.PinchBeganDispatched)
        {
            pinch.OnEnded?.Invoke(new PinchGesture(
                Scale: 1.0,
                ScaleDelta: 1.0,
                Center: e.Position,
                Phase: GesturePhase.Ended,
                IsInertial: state.InertiaActive));
        }

        if (state.Rotate is { } rotate && state.RotateBeganDispatched)
        {
            rotate.OnEnded?.Invoke(new RotateGesture(
                Angle: 0,
                AngleDelta: 0,
                Center: e.Position,
                Phase: GesturePhase.Ended,
                IsInertial: state.InertiaActive));
        }

        // Reset for next manipulation.
        state.PanBeganDispatched = false;
        state.PinchBeganDispatched = false;
        state.RotateBeganDispatched = false;
        state.PanLastTranslation = new Point(0, 0);
        state.InertiaActive = false;
    }
}
