using System;
using Microsoft.UI.Reactor.Core.V1Protocol;

namespace Reactor.External.TestControl;

/// <summary>
/// Spec 047 §14 Phase 1 (1.16) — external V1 handler authored against the
/// public Reactor surface only. The very fact that this file compiles
/// (with no <c>InternalsVisibleTo</c> from Reactor.dll into this assembly)
/// is the Phase 1 exit gate item 2 proof.
///
/// <para>The handler exercises every author-facing primitive an external
/// author needs:
/// <list type="bullet">
///   <item><see cref="MountContext.RentControl"/> — pool/allocate the control.</item>
///   <item><see cref="MountContext.BindFor"/> + <see cref="ReactorBinding{TElement}.WriteSuppressed"/>
///         — programmatic write echo-suppression.</item>
///   <item><see cref="ReactorBinding{TElement}.OnCustomEvent"/> — custom-event
///         subscription with trampoline-refresh.</item>
///   <item><see cref="MountContext.ApplySetters"/> — setter chain through
///         the public helper.</item>
///   <item><see cref="ChildrenStrategy{TElement,TControl}"/> — leaf
///         strategy (MarqueeControl is a value-bearing leaf with no
///         child slot).</item>
/// </list></para>
///
/// <para>If this handler ever needed a Reactor internal to do its job,
/// that would be a Phase 1 exit gate violation — the public surface
/// promotion in §1.3 would be incomplete.</para>
/// </summary>
public sealed class MarqueeHandler : IElementHandler<MarqueeElement, MarqueeControl>
{
    public MarqueeControl Mount(MountContext ctx, MarqueeElement el)
    {
        // RentControl exercises the public pool API. MarqueeControl is not
        // in Reactor's internal PoolableTypes set, so this is effectively
        // `new MarqueeControl()` every time — and that is the legitimate,
        // documented behavior for non-poolable types. The point of calling
        // RentControl anyway is that an external author's handler doesn't
        // know (and shouldn't have to know) which types are poolable. The
        // API is stable; the behavior is sound either way.
        var ctrl = ctx.RentControl<MarqueeControl>();

        var bind = ctx.BindFor(ctrl, el);

        // Bare Caption write at mount — the CaptionChanged subscription
        // below is wired *after* this write, so the synchronous event
        // raised by the setter has no trampoline to fire. Suppression at
        // mount is unnecessary (and harmful — it would leak a token that
        // drains the next real event). Update is where WriteSuppressed
        // matters, because by then the subscription is live.
        if (ctrl.Caption != el.Caption)
            ctrl.Caption = el.Caption;

        // OnCustomEvent — wires CaptionChanged via the public trampoline
        // helper. The handler closure does NOT capture `el` — it reads
        // the live element back via the bind helper's GetElementTag refresh
        // (so the subscription survives element re-renders).
        bind.OnCustomEvent<EventArgs>(
            subscribe:   (c, h) => ((MarqueeControl)c).CaptionChanged += new EventHandler(h),
            unsubscribe: (c, h) => ((MarqueeControl)c).CaptionChanged -= new EventHandler(h),
            handler:     (cur, _) => cur.OnCaptionChanged?.Invoke(ctrl.Caption));

        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, MarqueeElement oldEl, MarqueeElement newEl, MarqueeControl ctrl)
    {
        if (oldEl.Caption != newEl.Caption && ctrl.Caption != newEl.Caption)
            ctx.BindFor(ctrl, newEl).WriteSuppressed(() => ctrl.Caption = newEl.Caption);
        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    /// <summary>Leaf — no children. (Children-strategy is <c>null</c>;
    /// the engine treats a null strategy as "leaf" per the
    /// <see cref="IElementHandler{T,U}.Children"/> default.)</summary>
    public ChildrenStrategy<MarqueeElement, MarqueeControl>? Children => null;
}
