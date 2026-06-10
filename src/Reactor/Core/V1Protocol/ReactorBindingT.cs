using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §9 / §14 Phase 1 (1.6) — per-binding event helper.
///
/// One <c>On&lt;Event&gt;</c> method per WinUI true-routed input family
/// (pointer, key, tap, focus). Plain CLR events (Toggled, Click,
/// ValueChanged, TextChanged) go through
/// <see cref="OnCustomEvent{TArgs}"/>; that path stores its delegate in
/// the per-control event payload box (<see cref="ControlEventStateBox"/>)
/// so pool/recycle correctly clears it.
///
/// <para><b>Trampoline-refresh pattern</b> (§3): the handler closure
/// captures <see cref="_control"/> but NOT the element. On each event
/// fire, the live element is re-read via
/// <see cref="Reconciler.GetElementTag(FrameworkElement)"/>; the binding
/// survives re-renders without resubscribing.</para>
///
/// <para><b>WriteSuppressed</b> wraps the 1.4 primitive so authors can
/// suppress the next change-event echo on the bound control.</para>
/// </summary>
public readonly struct ReactorBinding<TElement> where TElement : Element
{
    private const int ReferenceSlotBase = 100_000;

    [ThreadStatic]
    private static int s_referenceSlotCount;

    private readonly Reconciler _reconciler;
    private readonly FrameworkElement _control;
    private readonly TElement _element;

    internal ReactorBinding(Reconciler reconciler, FrameworkElement control, TElement element)
    {
        _reconciler = reconciler;
        _control = control;
        _element = element;
        s_referenceSlotCount = 0;
    }

    /// <summary>The control the binding is anchored to.</summary>
    public FrameworkElement Control => _control;

    // ── Pointer family ────────────────────────────────────────────────

    public void OnPointerPressed(Action<TElement, PointerRoutedEventArgs> handler) =>
        Reconciler.BindOnPointerPressed(_control, WrapPointer(handler));
    public void OnPointerMoved(Action<TElement, PointerRoutedEventArgs> handler) =>
        Reconciler.BindOnPointerMoved(_control, WrapPointer(handler));
    public void OnPointerReleased(Action<TElement, PointerRoutedEventArgs> handler) =>
        Reconciler.BindOnPointerReleased(_control, WrapPointer(handler));
    public void OnPointerEntered(Action<TElement, PointerRoutedEventArgs> handler) =>
        Reconciler.BindOnPointerEntered(_control, WrapPointer(handler));
    public void OnPointerExited(Action<TElement, PointerRoutedEventArgs> handler) =>
        Reconciler.BindOnPointerExited(_control, WrapPointer(handler));
    public void OnPointerCaptureLost(Action<TElement, PointerRoutedEventArgs> handler) =>
        Reconciler.BindOnPointerCaptureLost(_control, WrapPointer(handler));
    public void OnPointerWheelChanged(Action<TElement, PointerRoutedEventArgs> handler) =>
        Reconciler.BindOnPointerWheelChanged(_control, WrapPointer(handler));

    // ── Tap family ────────────────────────────────────────────────────

    public void OnTapped(Action<TElement, TappedRoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnTapped(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    public void OnDoubleTapped(Action<TElement, DoubleTappedRoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnDoubleTapped(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    public void OnRightTapped(Action<TElement, RightTappedRoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnRightTapped(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    public void OnHolding(Action<TElement, HoldingRoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnHolding(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    // ── Key family ────────────────────────────────────────────────────

    public void OnKeyDown(Action<TElement, KeyRoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnKeyDown(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    public void OnKeyUp(Action<TElement, KeyRoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnKeyUp(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    // ── Focus family ──────────────────────────────────────────────────

    public void OnGotFocus(Action<TElement, RoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnGotFocus(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    public void OnLostFocus(Action<TElement, RoutedEventArgs> handler)
    {
        var fe = _control;
        Reconciler.BindOnLostFocus(fe, (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        });
    }

    /// <summary>
    /// Spec 047 §9.2 — subscribe to a plain CLR event (Toggled, Click,
    /// ValueChanged, TextChanged …). The handler closure re-fetches the
    /// live element via <see cref="Reconciler.GetElementTag(FrameworkElement)"/>
    /// on each fire so it survives re-renders.
    ///
    /// <para>The subscription is anchored to the control's lifetime. The
    /// engine's pool reset contract clears the per-control event payload
    /// on return, so ported handlers are safe to call this once per Mount.
    /// Handlers should NOT call it inside <see cref="IElementHandler{T,U}.Update"/>
    /// (subscriptions would multiply across renders).</para>
    /// </summary>
    public void OnCustomEvent<TArgs>(
        Action<FrameworkElement, EventHandler<TArgs>> subscribe,
        Action<FrameworkElement, EventHandler<TArgs>> unsubscribe,
        Action<TElement, TArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);
        ArgumentNullException.ThrowIfNull(handler);

        var fe = _control;
        EventHandler<TArgs> trampoline = (sender, args) =>
        {
            // Spec 047 §14 Phase 1 — drain the echo-suppress counter on the
            // control before invoking the user's change handler. Mirrors the
            // legacy per-control trampoline contract documented on
            // ChangeEchoSuppressor.ShouldSuppress (Reconciler.Mount.cs's
            // EnsureToggleSwitchWiring etc.). For non-value-bearing events
            // (Button.Click etc.) the counter is never incremented, so this is
            // a free no-op. See §8 / Phase 4 followup: when the echo
            // suppressor is replaced by per-control tolerance / coercion
            // metadata, this drain migrates with it.
            if (ChangeEchoSuppressor.ShouldSuppress(fe)) return;
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, args);
        };
        subscribe(fe, trampoline);
        // The trampoline must be rooted for the control's lifetime so the
        // GC doesn't collect the captured closure while WinUI still holds
        // the subscription on the native event source. Stash it in the
        // per-control event state box.
        Reconciler.AnchorCustomEventTrampoline(fe, trampoline);
    }

    /// <summary>
    /// Registers a reference-property edge for hand-coded handlers. Slot indices
    /// are assigned by call order for each <c>BindFor</c> instance and offset away
    /// from descriptor slots; repeated reconciles of the same handler call order
    /// therefore hit the same per-control reference slot.
    /// </summary>
    public void Reference<TTarget>(
        Func<TElement, Microsoft.UI.Reactor.Input.ElementRef<TTarget>?> get,
        Action<FrameworkElement, TTarget?> set)
        where TTarget : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        var slot = ReferenceSlotBase + s_referenceSlotCount++;
        var cell = get(_element)?.Inner;
        Reconciler.WireReferenceEdge(
            _control,
            slot,
            cell,
            (c, target) => set(c, target as TTarget));
    }

    /// <summary>
    /// Registers a list-valued reference-property edge for hand-coded handlers.
    /// The target list is rebuilt from resolved cells in author declaration order.
    /// </summary>
    public void ReferenceList<TTarget>(
        Func<TElement, IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef<TTarget>>?> get,
        Action<FrameworkElement, IReadOnlyList<TTarget>> apply)
        where TTarget : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(apply);

        var slot = ReferenceSlotBase + s_referenceSlotCount++;
        var element = _element;
        var refs = get(element);
        List<Microsoft.UI.Reactor.Input.ElementRef>? cells = null;
        if (refs is not null)
        {
            cells = new(refs.Count);
            foreach (var r in refs)
                if (r is not null)
                    cells.Add(r.Inner);
        }

        Reconciler.WireReferenceListEdge(
            _control,
            slot,
            cells,
            c =>
            {
                var liveRefs = get(element);
                if (liveRefs is null || liveRefs.Count == 0)
                {
                    apply(c, Array.Empty<TTarget>());
                    return;
                }

                var resolved = new List<TTarget>(liveRefs.Count);
                foreach (var r in liveRefs)
                {
                    if (r?.Inner.Current is TTarget target)
                        resolved.Add(target);
                }

                apply(c, resolved);
            },
            clearTarget: c => apply(c, Array.Empty<TTarget>()));
    }

    /// <summary>Per-binding wrapper around the 1.4 primitive
    /// (<see cref="ReactorBinding.WriteSuppressed(UIElement, Action)"/>).</summary>
    public void WriteSuppressed(Action mutate) => ReactorBinding.WriteSuppressed(_control, mutate);

    // ── Helpers ───────────────────────────────────────────────────────

    private Action<object, PointerRoutedEventArgs> WrapPointer(Action<TElement, PointerRoutedEventArgs> handler)
    {
        var fe = _control;
        return (s, e) =>
        {
            if (Reconciler.GetElementTag(fe) is TElement el)
                handler(el, e);
        };
    }
}
