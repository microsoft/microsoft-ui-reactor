using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core.V1Protocol;

namespace Reactor.External.TestControl;

/// <summary>
/// Issue #206 — external V1 handler for a Slider-shaped value-bearing control,
/// authored against the public Reactor surface ONLY (no
/// <c>InternalsVisibleTo</c> from Microsoft.UI.Reactor.dll). It is the worked proof that a
/// third-party custom value control can suppress the echo from its own
/// programmatic write with nothing but the public primitives:
/// <list type="bullet">
///   <item><see cref="MountContext.RentControl"/> — pool/allocate.</item>
///   <item><see cref="MountContext.BindFor"/> +
///         <see cref="ReactorBinding{TElement}.OnCustomEvent"/> — subscribe the
///         change event with trampoline-refresh.</item>
///   <item><see cref="ReactorBinding{TElement}.WriteSuppressed"/> — suppress the
///         echo of the handler's own programmatic <c>Value</c> write.</item>
/// </list>
///
/// <para><b>Controlled-control contract.</b> The programmatic write in
/// <see cref="Update"/> is (1) gated on a readback-vs-target equality check
/// (<c>ctrl.Value</c> vs <c>newEl.Value</c>) so it only runs when the live
/// control actually differs from the rendered value — which also snaps back
/// native drift — and (2) wrapped in <c>WriteSuppressed</c> 1:1 — so the
/// engine-synthesized <see cref="GaugeControl.ValueChanged"/> consumes
/// exactly one suppression token and never reaches the user's
/// <see cref="GaugeElement.OnValueChanged"/>. A genuine user edit (outside the
/// suppression scope) leaves no token and flows through to the callback.</para>
/// </summary>
public sealed class GaugeHandler : IElementHandler<GaugeElement, GaugeControl>
{
    public GaugeControl Mount(MountContext ctx, GaugeElement el)
    {
        var ctrl = ctx.RentControl<GaugeControl>();
        var bind = ctx.BindFor(ctrl, el);

        // Bare initial write — the ValueChanged subscription is wired below,
        // so the synchronous setter event has no trampoline yet. Suppression
        // at mount would leak a token that drains the next real event.
        if (!EqualityComparer<double>.Default.Equals(ctrl.Value, el.Value))
            ctrl.Value = el.Value;

        bind.OnCustomEvent<EventArgs>(
            subscribe:   (c, h) => ((GaugeControl)c).ValueChanged += new EventHandler(h),
            unsubscribe: (c, h) => ((GaugeControl)c).ValueChanged -= new EventHandler(h),
            handler:     (cur, _) => cur.OnValueChanged?.Invoke(ctrl.Value));

        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, GaugeElement oldEl, GaugeElement newEl, GaugeControl ctrl)
    {
        // Controlled-value write: gated on the live control readback (NOT the
        // old element) so the handler also snaps back any native-control drift
        // when the rendered value is unchanged — mirroring the descriptor
        // .Controlled path. The write is echo-suppressed 1:1. This is the entire
        // public-surface contract issue #206 asks custom-control authors to
        // follow.
        if (!EqualityComparer<double>.Default.Equals(ctrl.Value, newEl.Value))
            ctx.BindFor(ctrl, newEl).WriteSuppressed(() => ctrl.Value = newEl.Value);
        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    /// <summary>Leaf — value-bearing control with no child slot.</summary>
    public ChildrenStrategy<GaugeElement, GaugeControl>? Children => null;
}
