using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 1 (1.12) — value-bearing + coercing control port.
/// <see cref="WinUI.Slider"/>'s <c>Value</c> is clamped against
/// <c>Minimum</c>/<c>Maximum</c>, so range writes can themselves coerce
/// the existing Value (raising <c>ValueChanged</c>).
///
/// <para><b>Coercion-tolerance rule</b> (per spec §13 Q3 / §8 audit):
/// Slider is one of the eight coercion sites identified in the audit, not
/// a §8.1 round-trip. The handler encodes the tolerance by emitting an
/// additional <see cref="ReactorBinding{TElement}.WriteSuppressed"/> token
/// for each Min/Max write that would coerce the current Value:</para>
/// <code>
/// if (ctrl.Minimum != n.Min)
/// {
///     if (ctrl.Value &lt; n.Min) bind.WriteSuppressed(() => { });
///     ctrl.Minimum = n.Min;
/// }
/// </code>
/// <para>The <c>WriteSuppressed</c> call without a mutate would be wasteful;
/// instead we suppress and write together via <see cref="ReactorBinding.WriteSuppressed{T}"/>.</para>
///
/// <para>TODO(Phase 4): the declarative <c>coercedBy: [Minimum, Maximum]</c>
/// descriptor metadata mentioned in §8 lands with the descriptor model
/// spike; for Phase 1 hand-coded handlers, the rule is encoded directly
/// in the Update body.</para>
///
/// <para><b>Pool:</b> WinUI.Slider is not in <c>PoolableTypes</c>, so
/// <see cref="MountContext.RentControl{T}"/> falls through to
/// <c>new T()</c>. We still go through <c>RentControl</c> for protocol
/// symmetry — external authors should not have to special-case non-poolable
/// types at the call site.</para>
/// </summary>
internal sealed class SliderHandler : IElementHandler<SliderElement, WinUI.Slider>
{
    // Static trampoline — captures nothing, reads the live element via
    // GetElementTag. Per-control lifetime; pool reuse preserves it.
    private static readonly RangeBaseValueChangedEventHandler ValueChangedTrampoline = (s, args) =>
    {
        var sl = (WinUI.Slider)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(sl)) return;
        (Reconciler.GetElementTag(sl) as SliderElement)?.OnValueChanged?.Invoke(args.NewValue);
    };

    public WinUI.Slider Mount(MountContext ctx, SliderElement el)
    {
        var ctrl = ctx.RentControl<WinUI.Slider>();

        // Min/Max BEFORE Value — a stale range would coerce a fresh in-range
        // Value. Matches legacy MountSlider's invariant.
        ctrl.Minimum = el.Min;
        ctrl.Maximum = el.Max;
        ctrl.Value = el.Value;
        ctrl.StepFrequency = el.StepFrequency;
        ctrl.Orientation = el.Orientation;
        ctrl.TickFrequency = el.TickFrequency;
        ctrl.TickPlacement = el.TickPlacement;
        ctrl.SnapsTo = el.SnapsTo;
        ctrl.IsThumbToolTipEnabled = el.IsThumbToolTipEnabled;
        if (el.Header is not null) ctrl.Header = el.Header;

        EnsureValueChangedWiring(ctrl, el);
        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, SliderElement oldEl, SliderElement newEl, WinUI.Slider ctrl)
    {
        var bind = ctx.BindFor(ctrl, newEl);

        // Coercion-tolerance: Min/Max writes can themselves move Value within
        // the new range, raising ValueChanged. Suppress those echoes too —
        // one token per write that would coerce.
        if (ctrl.Minimum != newEl.Min)
        {
            if (ctrl.Value < newEl.Min)
                bind.WriteSuppressed(() => ctrl.Minimum = newEl.Min);
            else
                ctrl.Minimum = newEl.Min;
        }
        if (ctrl.Maximum != newEl.Max)
        {
            if (ctrl.Value > newEl.Max)
                bind.WriteSuppressed(() => ctrl.Maximum = newEl.Max);
            else
                ctrl.Maximum = newEl.Max;
        }
        if (ctrl.Value != newEl.Value)
            bind.WriteSuppressed(() => ctrl.Value = newEl.Value);

        if (ctrl.StepFrequency != newEl.StepFrequency) ctrl.StepFrequency = newEl.StepFrequency;
        if (ctrl.Orientation != newEl.Orientation) ctrl.Orientation = newEl.Orientation;
        if (ctrl.TickFrequency != newEl.TickFrequency) ctrl.TickFrequency = newEl.TickFrequency;
        if (ctrl.TickPlacement != newEl.TickPlacement) ctrl.TickPlacement = newEl.TickPlacement;
        if (ctrl.SnapsTo != newEl.SnapsTo) ctrl.SnapsTo = newEl.SnapsTo;
        if (ctrl.IsThumbToolTipEnabled != newEl.IsThumbToolTipEnabled)
            ctrl.IsThumbToolTipEnabled = newEl.IsThumbToolTipEnabled;
        if (newEl.Header is not null && !ReferenceEquals(oldEl.Header, newEl.Header))
            ctrl.Header = newEl.Header;

        // Lazy-wire on null→non-null callback transition.
        EnsureValueChangedWiring(ctrl, newEl);

        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    // Gate per-control event wiring on the user actually having wired a
    // callback — matches legacy MountSlider behavior. Without this gate
    // every callback-less Slider pays subscription cost legacy skips (M4 / M5).
    private static void EnsureValueChangedWiring(WinUI.Slider ctrl, SliderElement el)
    {
        if (el.OnValueChanged is null) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<SliderEventPayload>(ctrl);
        if (payload.ValueChangedTrampoline is null)
        {
            payload.ValueChangedTrampoline = ValueChangedTrampoline;
            ctrl.ValueChanged += ValueChangedTrampoline;
        }
    }

    public ChildrenStrategy<SliderElement, WinUI.Slider>? Children { get; } =
        new None<SliderElement, WinUI.Slider>();
}
