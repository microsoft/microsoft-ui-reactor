using global::Windows.Foundation;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Reactor.Core;

public sealed partial class Reconciler
{
    /// <summary>
    /// Per-element gesture dispatch state. Mirrors <see cref="EventHandlerState"/>'s
    /// trampoline pattern for the three manipulation events, plus per-gesture cursors
    /// that track distance thresholds, start anchors, and inertia flags so callback
    /// contracts stay clean ("<see cref="GesturePhase.Began"/> fires exactly once").
    /// </summary>
    internal sealed class GestureState
    {
        public PanGestureConfig? Pan;
        public PinchGestureConfig? Pinch;
        public RotateGestureConfig? Rotate;

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
        if (m.Pan is null && m.Pinch is null && m.Rotate is null
            && oldM?.Pan is null && oldM?.Pinch is null && oldM?.Rotate is null)
            return;

        var state = GetOrCreateGestureState(fe);
        state.Pan = m.Pan;
        state.Pinch = m.Pinch;
        state.Rotate = m.Rotate;

        // Recompute ManipulationMode only when the set of gestures is non-empty —
        // respects a user-set .Set(r => r.ManipulationMode = ...) otherwise.
        var mode = ComputeManipulationMode(m);
        if (mode != ManipulationModes.None)
            fe.ManipulationMode = mode;

        // Lazy-attach trampolines (one-time).
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
    }

    private static void OnManipulationDelta(FrameworkElement fe, GestureState state, ManipulationDeltaRoutedEventArgs e)
    {
        var inertial = state.InertiaActive || e.IsInertial;

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
