using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Layout;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 8) — descriptor variant of the hand-coded
/// <c>MountFlex</c> / <c>UpdateFlex</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> zero-event Yoga-driven flex container. Eight
/// unconditional one-way props (<c>Direction</c>, <c>JustifyContent</c>,
/// <c>AlignItems</c>, <c>AlignContent</c>, <c>Wrap</c>, <c>ColumnGap</c>,
/// <c>RowGap</c>, <c>FlexPadding</c>). Children dispatched through the
/// <see cref="Panel{TElement,TControl}"/> strategy
/// (<c>FlexPanel : Panel</c>, so <c>Children</c> is the standard
/// <c>UIElementCollection</c>).</para>
///
/// <para><b>Known gaps:</b> the legacy hand-coded path applies
/// <see cref="FlexAttached"/> per child after children mount (Grow / Shrink /
/// Basis / Order / AlignSelf / margins). The Panel strategy in
/// V1HandlerAdapter doesn't surface a per-child post-mount hook, so flex
/// children stay at default Yoga node config. Authors who need per-child
/// flex tuning stay on V1 OFF (legacy arm). Container-level layout has
/// parity.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class FlexPanelDescriptor
{
    private static readonly Panel<FlexElement, FlexPanel> ChildrenStrategy =
        new Panel<FlexElement, FlexPanel>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children);

    public static readonly ControlDescriptor<FlexElement, FlexPanel> Descriptor =
        new ControlDescriptor<FlexElement, FlexPanel>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Direction,
            set: static (c, v) => c.Direction = v)
        .OneWay(
            get: static e => e.JustifyContent,
            set: static (c, v) => c.JustifyContent = v)
        .OneWay(
            get: static e => e.AlignItems,
            set: static (c, v) => c.AlignItems = v)
        .OneWay(
            get: static e => e.AlignContent,
            set: static (c, v) => c.AlignContent = v)
        .OneWay(
            get: static e => e.Wrap,
            set: static (c, v) => c.Wrap = v)
        .OneWay(
            get: static e => e.ColumnGap,
            set: static (c, v) => c.ColumnGap = v)
        .OneWay(
            get: static e => e.RowGap,
            set: static (c, v) => c.RowGap = v)
        .OneWay(
            get: static e => e.FlexPadding,
            set: static (c, v) => c.FlexPadding = v);
}
