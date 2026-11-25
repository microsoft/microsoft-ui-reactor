using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: Control and modifier event subscription: V1 binding shims, control event payloads, Ensure*Subscribed trampolines.
public sealed partial class Reconciler
{
    // ════════════════════════════════════════════════════════════════════
    //  Internal binding shims for V1 ReactorBinding<TElement> (1.6)
    // ════════════════════════════════════════════════════════════════════
    //
    // Each shim corresponds to an Ensure*Subscribed helper in this file.
    // They exist so ReactorBinding<TElement>.On<Event>(Action<TElement,…>)
    // can update the per-control trampoline without exposing the private
    // ModifierEventHandlerState type. Strongly-typed delegates only — no reflection.

    internal static void BindOnPointerPressed(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerPressedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerMoved(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerMovedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerReleased(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerReleasedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerEntered(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerEnteredSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerExited(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerExitedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerCaptureLost(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerCaptureLostSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerWheelChanged(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerWheelChangedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnTapped(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? handler)
        => EnsureTappedSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnDoubleTapped(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? handler)
        => EnsureDoubleTappedSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnRightTapped(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? handler)
        => EnsureRightTappedSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnHolding(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? handler)
        => EnsureHoldingSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnKeyDown(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
        => EnsureKeyDownSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnKeyUp(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
        => EnsureKeyUpSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnGotFocus(FrameworkElement fe, Action<object, RoutedEventArgs>? handler)
        => EnsureGotFocusSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnLostFocus(FrameworkElement fe, Action<object, RoutedEventArgs>? handler)
        => EnsureLostFocusSubscribed(fe, GetOrCreateModifierState(fe), handler);

    /// <summary>
    /// Spec 047 §9.2 / §14 Phase 1 (1.7) — anchor a strongly-typed
    /// trampoline delegate to the control's per-control event payload box
    /// so the GC keeps the captured closure alive while WinUI's
    /// subscription list holds the native side. Pool reset clears the
    /// box on return.
    /// </summary>
    internal static void AnchorCustomEventTrampoline(FrameworkElement fe, Delegate trampoline)
    {
        var state = GetOrCreateReactorState(fe);
        if (state.ControlEventState is not ControlEventStateBox box
            || box.HandlerType != typeof(V1Protocol.CustomEventAnchorPayload))
        {
            box = new ControlEventStateBox
            {
                HandlerType = typeof(V1Protocol.CustomEventAnchorPayload),
                Payload = new V1Protocol.CustomEventAnchorPayload()
            };
            state.ControlEventState = box;
        }
        ((V1Protocol.CustomEventAnchorPayload)box.Payload).Trampolines.Add(trampoline);
    }

    /// <summary>
    /// Spec 047 §9.2 / §14 Phase 1 — get-or-create a typed per-control event
    /// payload (e.g. <c>ToggleSwitchEventPayload</c>). The payload survives
    /// pool rent/return cycles so trampolines wired once per control lifetime
    /// stay alive (mirrors the legacy <c>ModifierEventHandlerState</c> "stable
    /// trampoline + mutable Current* slot" pattern documented in
    /// <c>Reconciler.cs:3266-3289</c>). Authors of built-in ports use this
    /// instead of <see cref="V1Protocol.ReactorBinding{TElement}.OnCustomEvent"/>
    /// for the seven audited control-intrinsic events — see
    /// <c>ControlEventPayloads.cs</c>.
    /// </summary>
    internal static T GetOrCreateControlEventPayload<T>(FrameworkElement fe) where T : class, new()
    {
        var state = GetOrCreateReactorState(fe);
        if (state.ControlEventState is ControlEventStateBox existing
            && existing.HandlerType == typeof(T))
        {
            return (T)existing.Payload;
        }
        var payload = new T();
        state.ControlEventState = new ControlEventStateBox
        {
            HandlerType = typeof(T),
            Payload = payload,
        };
        return payload;
    }

    /// <summary>
    /// Spec 047 §8 — non-allocating sibling of
    /// <see cref="GetOrCreateControlEventPayload{T}"/>. Returns the existing
    /// per-control payload of type <typeparamref name="T"/>, or <c>null</c> if
    /// the control has no <c>ReactorState</c>, no event-state box, or a box of a
    /// different handler type. Unlike the get-or-create form this never creates
    /// state, so callers on a hot/read path (e.g. the controlled-prop value-diff
    /// echo check) can probe without forcing allocation on callback-less or
    /// unsubscribed controls.
    /// </summary>
    internal static T? TryGetControlEventPayload<T>(FrameworkElement fe) where T : class
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state
            && state.ControlEventState is ControlEventStateBox box
            && box.HandlerType == typeof(T))
        {
            return (T)box.Payload;
        }
        return null;
    }

    /// <summary>
    /// Issue #207 — payload accessor for trampolines that already hold the
    /// control's <see cref="ReactorState"/> (read once via
    /// <see cref="TryGetReactorState"/>), avoiding a second attached-DP read.
    /// </summary>
    internal static T? TryGetControlEventPayload<T>(ReactorState state) where T : class
    {
        if (state.ControlEventState is ControlEventStateBox box
            && box.HandlerType == typeof(T))
        {
            return (T)box.Payload;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks the currently-attached event handlers on a FrameworkElement so they
    /// can be detached before new ones are attached. Stored as the element's Tag
    /// (or alongside it in a wrapper if Tag is already used for pool identity).
    /// </summary>
    /// <summary>
    /// Per-element handler state. Holds the <b>current</b> user delegate for each event
    /// plus a bit tracking whether the stable trampoline has been attached yet. The
    /// trampoline reads from the mutable <c>Current*</c> field when it fires, so updating
    /// a handler just swaps the field — no WinUI subscribe/unsubscribe churn.
    /// </summary>
    internal sealed class ModifierEventHandlerState
    {
        // Current user handlers (mutable; null means "no-op")
        public Action<object, SizeChangedEventArgs>? CurrentSizeChanged;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerPressed;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerMoved;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerReleased;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerEntered;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerExited;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerCanceled;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerCaptureLost;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerWheelChanged;
        public Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? CurrentTapped;
        public Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? CurrentDoubleTapped;
        public Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? CurrentRightTapped;
        public Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? CurrentHolding;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentKeyDown;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentKeyUp;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentPreviewKeyDown;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentPreviewKeyUp;
        public Action<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? CurrentCharacterReceived;
        public Action<object, RoutedEventArgs>? CurrentGotFocus;
        public Action<object, RoutedEventArgs>? CurrentLostFocus;
        public Action<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? CurrentAccessKeyDisplayRequested;

        // Stable trampoline delegates — captured for reference-equality detach (never used)
        // and to prevent GC collection of the compiler-generated closure.
        public SizeChangedEventHandler? SizeChangedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerPressedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerMovedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerReleasedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerEnteredTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerExitedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerCanceledTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerCaptureLostTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerWheelChangedTrampoline;
        public Microsoft.UI.Xaml.Input.TappedEventHandler? TappedTrampoline;
        public Microsoft.UI.Xaml.Input.DoubleTappedEventHandler? DoubleTappedTrampoline;
        public Microsoft.UI.Xaml.Input.RightTappedEventHandler? RightTappedTrampoline;
        public Microsoft.UI.Xaml.Input.HoldingEventHandler? HoldingTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? KeyDownTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? KeyUpTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? PreviewKeyDownTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? PreviewKeyUpTrampoline;
        public global::Windows.Foundation.TypedEventHandler<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? CharacterReceivedTrampoline;
        public RoutedEventHandler? GotFocusTrampoline;
        public RoutedEventHandler? LostFocusTrampoline;
        public global::Windows.Foundation.TypedEventHandler<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? AccessKeyDisplayRequestedTrampoline;

        /// <summary>
        /// Null out every Current* user delegate so trampolines already attached
        /// on the native event source become no-ops. Trampoline delegate fields
        /// are left intact — they're rooted by WinUI's subscription list and
        /// can't be detached here without access to the native element.
        /// </summary>
        public void ClearCurrentHandlers()
        {
            CurrentSizeChanged = null;
            CurrentPointerPressed = null;
            CurrentPointerMoved = null;
            CurrentPointerReleased = null;
            CurrentPointerEntered = null;
            CurrentPointerExited = null;
            CurrentPointerCanceled = null;
            CurrentPointerCaptureLost = null;
            CurrentPointerWheelChanged = null;
            CurrentTapped = null;
            CurrentDoubleTapped = null;
            CurrentRightTapped = null;
            CurrentHolding = null;
            CurrentKeyDown = null;
            CurrentKeyUp = null;
            CurrentPreviewKeyDown = null;
            CurrentPreviewKeyUp = null;
            CurrentCharacterReceived = null;
            CurrentGotFocus = null;
            CurrentLostFocus = null;
            CurrentAccessKeyDisplayRequested = null;
        }
    }

    // ModifierEventHandlerState lives on the ReactorState wrapper attached to the
    // native DependencyObject via ReactorAttached.StateProperty. That keys
    // on native identity, so two RCWs pointing at the same element share one
    // ModifierEventHandlerState and one set of trampolines — fixing issue #86.
    private static ModifierEventHandlerState GetOrCreateModifierState(FrameworkElement fe)
    {
        var state = GetOrCreateReactorState(fe);
        return state.Modifiers ??= new ModifierEventHandlerState();
    }

    private static bool HasAnyEventHandler(ElementModifiers? m)
    {
        if (m is null) return false;
        return m.OnSizeChanged is not null
            || m.OnPointerPressed is not null || m.OnPointerMoved is not null || m.OnPointerReleased is not null
            || m.OnPointerEntered is not null || m.OnPointerExited is not null || m.OnPointerCanceled is not null
            || m.OnPointerCaptureLost is not null || m.OnPointerWheelChanged is not null
            || m.OnTapped is not null || m.OnDoubleTapped is not null || m.OnRightTapped is not null || m.OnHolding is not null
            || m.OnKeyDown is not null || m.OnKeyUp is not null
            || m.OnPreviewKeyDown is not null || m.OnPreviewKeyUp is not null
            || m.OnCharacterReceived is not null
            || m.OnGotFocus is not null || m.OnLostFocus is not null
            || m.OnAccessKeyDisplayRequested is not null;
    }

    private static bool HasAnyPointerHandler(ElementModifiers m)
    {
        return m.OnPointerPressed is not null || m.OnPointerMoved is not null || m.OnPointerReleased is not null
            || m.OnPointerEntered is not null || m.OnPointerExited is not null || m.OnPointerCanceled is not null
            || m.OnPointerCaptureLost is not null || m.OnPointerWheelChanged is not null
            || m.OnTapped is not null || m.OnDoubleTapped is not null || m.OnRightTapped is not null || m.OnHolding is not null;
    }

    private static void ApplyEventHandlers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // Fast path: nothing to do
        if (!HasAnyEventHandler(m) && !HasAnyEventHandler(oldM)) return;

        var state = GetOrCreateModifierState(fe);

        // Trampoline pattern: each Ensure* helper points the current-handler field at
        // the new delegate and, if the trampoline isn't attached yet, attaches it once.
        // Subsequent renders that just hand us a fresh closure touch only the field —
        // no add_/remove_ COM traffic on the underlying WinUI event.
        EnsureSizeChangedSubscribed(fe, state, m.OnSizeChanged);
        EnsurePointerPressedSubscribed(fe, state, m.OnPointerPressed);
        EnsurePointerMovedSubscribed(fe, state, m.OnPointerMoved);
        EnsurePointerReleasedSubscribed(fe, state, m.OnPointerReleased);
        EnsurePointerEnteredSubscribed(fe, state, m.OnPointerEntered);
        EnsurePointerExitedSubscribed(fe, state, m.OnPointerExited);
        EnsurePointerCanceledSubscribed(fe, state, m.OnPointerCanceled);
        EnsurePointerCaptureLostSubscribed(fe, state, m.OnPointerCaptureLost);
        EnsurePointerWheelChangedSubscribed(fe, state, m.OnPointerWheelChanged);
        EnsureTappedSubscribed(fe, state, m.OnTapped, oldM?.OnTapped);
        EnsureDoubleTappedSubscribed(fe, state, m.OnDoubleTapped, oldM?.OnDoubleTapped);
        EnsureRightTappedSubscribed(fe, state, m.OnRightTapped, oldM?.OnRightTapped);
        EnsureHoldingSubscribed(fe, state, m.OnHolding, oldM?.OnHolding);
        EnsureKeyDownSubscribed(fe, state, m.OnKeyDown);
        EnsureKeyUpSubscribed(fe, state, m.OnKeyUp);
        EnsurePreviewKeyDownSubscribed(fe, state, m.OnPreviewKeyDown);
        EnsurePreviewKeyUpSubscribed(fe, state, m.OnPreviewKeyUp);
        EnsureCharacterReceivedSubscribed(fe, state, m.OnCharacterReceived);
        EnsureGotFocusSubscribed(fe, state, m.OnGotFocus);
        EnsureLostFocusSubscribed(fe, state, m.OnLostFocus);
        EnsureAccessKeyDisplayRequestedSubscribed(fe, state, m.OnAccessKeyDisplayRequested);

        // Shape auto-fill: Shape subclasses need a non-null Fill to hit-test pointer events.
        // If any pointer-family handler is attached and Fill is null, set transparent brush.
        if (fe is Microsoft.UI.Xaml.Shapes.Shape shape && shape.Fill is null && HasAnyPointerHandler(m))
        {
            shape.Fill = new SolidColorBrush(global::Microsoft.UI.Colors.Transparent);
        }
    }

    // ── Trampoline Ensure* helpers ──────────────────────────────────────
    // Each helper:
    //   1. Updates state.Current<Event> to the new user handler (may be null).
    //   2. On first non-null handler, allocates the stable trampoline, attaches
    //      it to the WinUI event, emits reactor:event.reattach once.
    //   3. Never detaches — the trampoline stays bound for the element's lifetime.
    //      When the user handler becomes null again, the trampoline dispatches no-op.

    // <snippet:event-trampoline>
    private static void EnsureSizeChangedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, SizeChangedEventArgs>? handler)
    {
        state.CurrentSizeChanged = handler;
        if (state.SizeChangedTrampoline is null && handler is not null)
        {
            state.SizeChangedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("SizeChanged");
                state.CurrentSizeChanged?.Invoke(s!, e);
            };
            fe.SizeChanged += state.SizeChangedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("SizeChanged", fe.GetType().Name);
        }
    }
    // </snippet:event-trampoline>

    private static void EnsurePointerPressedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerPressed = handler;
        if (state.PointerPressedTrampoline is null && handler is not null)
        {
            state.PointerPressedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerPressed");
                state.CurrentPointerPressed?.Invoke(s!, e);
            };
            fe.PointerPressed += state.PointerPressedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerPressed", fe.GetType().Name);
        }
    }

    private static void EnsurePointerMovedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerMoved = handler;
        if (state.PointerMovedTrampoline is null && handler is not null)
        {
            state.PointerMovedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerMoved");
                state.CurrentPointerMoved?.Invoke(s!, e);
            };
            fe.PointerMoved += state.PointerMovedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerMoved", fe.GetType().Name);
        }
    }

    private static void EnsurePointerReleasedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerReleased = handler;
        if (state.PointerReleasedTrampoline is null && handler is not null)
        {
            state.PointerReleasedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerReleased");
                state.CurrentPointerReleased?.Invoke(s!, e);
            };
            fe.PointerReleased += state.PointerReleasedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerReleased", fe.GetType().Name);
        }
    }

    private static void EnsurePointerEnteredSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerEntered = handler;
        if (state.PointerEnteredTrampoline is null && handler is not null)
        {
            state.PointerEnteredTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerEntered");
                state.CurrentPointerEntered?.Invoke(s!, e);
            };
            fe.PointerEntered += state.PointerEnteredTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerEntered", fe.GetType().Name);
        }
    }

    private static void EnsurePointerExitedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerExited = handler;
        if (state.PointerExitedTrampoline is null && handler is not null)
        {
            state.PointerExitedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerExited");
                state.CurrentPointerExited?.Invoke(s!, e);
            };
            fe.PointerExited += state.PointerExitedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerExited", fe.GetType().Name);
        }
    }

    private static void EnsurePointerCanceledSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerCanceled = handler;
        if (state.PointerCanceledTrampoline is null && handler is not null)
        {
            state.PointerCanceledTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerCanceled");
                state.CurrentPointerCanceled?.Invoke(s!, e);
            };
            fe.PointerCanceled += state.PointerCanceledTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerCanceled", fe.GetType().Name);
        }
    }

    private static void EnsurePointerCaptureLostSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerCaptureLost = handler;
        if (state.PointerCaptureLostTrampoline is null && handler is not null)
        {
            state.PointerCaptureLostTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerCaptureLost");
                state.CurrentPointerCaptureLost?.Invoke(s!, e);
            };
            fe.PointerCaptureLost += state.PointerCaptureLostTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerCaptureLost", fe.GetType().Name);
        }
    }

    private static void EnsurePointerWheelChangedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerWheelChanged = handler;
        if (state.PointerWheelChangedTrampoline is null && handler is not null)
        {
            state.PointerWheelChangedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerWheelChanged");
                state.CurrentPointerWheelChanged?.Invoke(s!, e);
            };
            fe.PointerWheelChanged += state.PointerWheelChangedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerWheelChanged", fe.GetType().Name);
        }
    }

    private static void EnsureTappedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? oldHandler)
    {
        state.CurrentTapped = handler;
        if (state.TappedTrampoline is null && handler is not null)
        {
            state.TappedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("Tapped");
                state.CurrentTapped?.Invoke(s!, e);
            };
            fe.Tapped += state.TappedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("Tapped", fe.GetType().Name);
        }
        if (handler is not null)
        {
            fe.IsTapEnabled = true;
            // Make tappable elements keyboard-accessible and visible in UIA tree
            if (fe is Microsoft.UI.Xaml.Controls.Control ctrl && !ctrl.IsTabStop)
                ctrl.IsTabStop = true;
        }
        else if (oldHandler is not null)
        {
            fe.IsTapEnabled = false;
        }
    }

    private static void EnsureDoubleTappedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? oldHandler)
    {
        state.CurrentDoubleTapped = handler;
        if (state.DoubleTappedTrampoline is null && handler is not null)
        {
            state.DoubleTappedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("DoubleTapped");
                state.CurrentDoubleTapped?.Invoke(s!, e);
            };
            fe.DoubleTapped += state.DoubleTappedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("DoubleTapped", fe.GetType().Name);
        }
        if (handler is not null) fe.IsDoubleTapEnabled = true;
        else if (oldHandler is not null) fe.IsDoubleTapEnabled = false;
    }

    private static void EnsureRightTappedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? oldHandler)
    {
        state.CurrentRightTapped = handler;
        if (state.RightTappedTrampoline is null && handler is not null)
        {
            state.RightTappedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("RightTapped");
                state.CurrentRightTapped?.Invoke(s!, e);
            };
            fe.RightTapped += state.RightTappedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("RightTapped", fe.GetType().Name);
        }
        if (handler is not null) fe.IsRightTapEnabled = true;
        else if (oldHandler is not null) fe.IsRightTapEnabled = false;
    }

    private static void EnsureHoldingSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? oldHandler)
    {
        state.CurrentHolding = handler;
        if (state.HoldingTrampoline is null && handler is not null)
        {
            state.HoldingTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("Holding");
                state.CurrentHolding?.Invoke(s!, e);
            };
            fe.Holding += state.HoldingTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("Holding", fe.GetType().Name);
        }
        if (handler is not null) fe.IsHoldingEnabled = true;
        else if (oldHandler is not null) fe.IsHoldingEnabled = false;
    }

    private static void EnsureKeyDownSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentKeyDown = handler;
        if (state.KeyDownTrampoline is null && handler is not null)
        {
            state.KeyDownTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("KeyDown");
                state.CurrentKeyDown?.Invoke(s!, e);
            };
            fe.KeyDown += state.KeyDownTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("KeyDown", fe.GetType().Name);
        }
    }

    private static void EnsureKeyUpSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentKeyUp = handler;
        if (state.KeyUpTrampoline is null && handler is not null)
        {
            state.KeyUpTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("KeyUp");
                state.CurrentKeyUp?.Invoke(s!, e);
            };
            fe.KeyUp += state.KeyUpTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("KeyUp", fe.GetType().Name);
        }
    }

    private static void EnsurePreviewKeyDownSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentPreviewKeyDown = handler;
        if (state.PreviewKeyDownTrampoline is null && handler is not null)
        {
            state.PreviewKeyDownTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PreviewKeyDown");
                state.CurrentPreviewKeyDown?.Invoke(s!, e);
            };
            fe.PreviewKeyDown += state.PreviewKeyDownTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PreviewKeyDown", fe.GetType().Name);
        }
    }

    private static void EnsurePreviewKeyUpSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentPreviewKeyUp = handler;
        if (state.PreviewKeyUpTrampoline is null && handler is not null)
        {
            state.PreviewKeyUpTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PreviewKeyUp");
                state.CurrentPreviewKeyUp?.Invoke(s!, e);
            };
            fe.PreviewKeyUp += state.PreviewKeyUpTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PreviewKeyUp", fe.GetType().Name);
        }
    }

    private static void EnsureCharacterReceivedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? handler)
    {
        state.CurrentCharacterReceived = handler;
        if (state.CharacterReceivedTrampoline is null && handler is not null)
        {
            state.CharacterReceivedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("CharacterReceived");
                state.CurrentCharacterReceived?.Invoke(s, e);
            };
            fe.CharacterReceived += state.CharacterReceivedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("CharacterReceived", fe.GetType().Name);
        }
    }

    private static void EnsureGotFocusSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, RoutedEventArgs>? handler)
    {
        state.CurrentGotFocus = handler;
        if (state.GotFocusTrampoline is null && handler is not null)
        {
            state.GotFocusTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("GotFocus");
                state.CurrentGotFocus?.Invoke(s!, e);
            };
            fe.GotFocus += state.GotFocusTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("GotFocus", fe.GetType().Name);
        }
    }

    private static void EnsureLostFocusSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, RoutedEventArgs>? handler)
    {
        state.CurrentLostFocus = handler;
        if (state.LostFocusTrampoline is null && handler is not null)
        {
            state.LostFocusTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("LostFocus");
                state.CurrentLostFocus?.Invoke(s!, e);
            };
            fe.LostFocus += state.LostFocusTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("LostFocus", fe.GetType().Name);
        }
    }

    private static void EnsureAccessKeyDisplayRequestedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? handler)
    {
        state.CurrentAccessKeyDisplayRequested = handler;
        if (state.AccessKeyDisplayRequestedTrampoline is null && handler is not null)
        {
            state.AccessKeyDisplayRequestedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("AccessKeyDisplayRequested");
                state.CurrentAccessKeyDisplayRequested?.Invoke(s!, e);
            };
            fe.AccessKeyDisplayRequested += state.AccessKeyDisplayRequestedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("AccessKeyDisplayRequested", fe.GetType().Name);
        }
    }
}
