using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

/// <summary>
/// Spec 047 §6 / §14 Phase 2 (Q1 spike) — interpreter that drives a
/// <see cref="ControlDescriptor{TElement,TControl}"/> against the v1
/// protocol surface.
///
/// <para>Implemented as a regular
/// <see cref="IElementHandler{TElement,TControl}"/> so the
/// <see cref="V1HandlerAdapter{TElement,TControl}"/> dispatch shell is
/// identical between the descriptor and hand-coded handler paths — the only
/// thing that differs between the Q1 A|B variants is the body of Mount /
/// Update. Any measured delta is the interpreter's tax, not a different
/// dispatch shape.</para>
///
/// <para><b>Mount sequence</b> (matches the hand-coded handlers' KD-1b
/// ordering):
/// <list type="number">
///   <item>Rent the control via <see cref="MountContext.RentControl{T}"/>.</item>
///   <item>Iterate <see cref="ControlDescriptor{TElement,TControl}.Properties"/>
///   and invoke <see cref="PropEntry{TElement,TControl}.Mount"/> on each —
///   all bare initial writes happen first.</item>
///   <item>Iterate again and invoke
///   <see cref="PropEntry{TElement,TControl}.EnsureSubscribed"/> — controlled
///   entries wire their change-event trampolines now (no echo on the just-
///   written values because nothing is listening yet).</item>
///   <item>Apply setters.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed class DescriptorHandler<TElement, TControl> : IElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement, new()
{
    private readonly ControlDescriptor<TElement, TControl> _descriptor;

    public DescriptorHandler(ControlDescriptor<TElement, TControl> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptor = descriptor;
    }

    /// <summary>The descriptor this handler interprets. Exposed for tests
    /// and bench harnesses — not part of the steady-state author API.</summary>
    public ControlDescriptor<TElement, TControl> Descriptor => _descriptor;

    public ChildrenStrategy<TElement, TControl>? Children => _descriptor.Children;

    public TControl Mount(MountContext ctx, TElement el)
    {
        var ctrl = ctx.RentControl(_descriptor.PoolPolicy, _descriptor.Factory);

        // Phase 1: all bare initial writes (no echo possible — subscriptions
        // not yet live).
        var props = _descriptor.Properties;
        for (int i = 0; i < props.Count; i++)
            props[i].Mount(ctrl, el);

        // Phase 2: subscribe controlled entries.
        var binding = ctx.BindFor(ctrl, el);
        for (int i = 0; i < props.Count; i++)
            props[i].EnsureSubscribed(binding, ctrl, el);

        var getSetters = _descriptor.GetSetters;
        if (getSetters is not null)
            ctx.ApplySetters(getSetters(el), ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl ctrl)
    {
        var props = _descriptor.Properties;
        for (int i = 0; i < props.Count; i++)
            props[i].Update(ctrl, oldEl, newEl);

        // Late-wire on null→non-null callback transition — if the element
        // gained a callback since Mount, subscribe now. The per-entry CWT
        // gate makes the no-op case cheap.
        var binding = ctx.BindFor(ctrl, newEl);
        for (int i = 0; i < props.Count; i++)
            props[i].EnsureSubscribed(binding, ctrl, newEl);

        var getSetters = _descriptor.GetSetters;
        if (getSetters is not null)
            ctx.ApplySetters(getSetters(newEl), ctrl);
    }
}
