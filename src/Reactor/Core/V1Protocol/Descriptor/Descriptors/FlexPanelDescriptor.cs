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
/// <para><b>§14 Phase 3-final Batch E:</b> per-child
/// <see cref="FlexAttached"/> (Grow / Shrink / Basis / MinWidth / MinHeight
/// / AlignSelf / Position / Left / Top / Right / Bottom) is now applied via
/// <see cref="Panel{TElement,TControl}.PerChildAttached"/> with the same
/// "always apply — reset to defaults when no hint" semantics as the legacy
/// <c>MountFlex</c> arm (the reset is required for pool-rented controls that
/// could otherwise inherit stale Yoga config).</para>
/// </summary>
internal static class FlexPanelDescriptor
{
    private static readonly Panel<FlexElement, FlexPanel> ChildrenStrategy =
        new Panel<FlexElement, FlexPanel>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children)
        {
            PerChildAttached = static (panel, ui, childEl) => ApplyFlexAttached(childEl, ui),
        };

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

    private static void ApplyFlexAttached(Element child, Microsoft.UI.Xaml.UIElement ctrl)
    {
        var fa = child.GetAttached<FlexAttached>();
        // Always apply — reset to defaults when no FlexAttached, so stale values
        // from pool-rented or reconciler-reused controls are cleared.
        FlexPanel.SetGrow(ctrl, fa?.Grow ?? 0);
        FlexPanel.SetShrink(ctrl, fa?.Shrink ?? 1);
        if (fa is { Basis: { } basis }) FlexPanel.SetBasis(ctrl, basis);
        else ctrl.ClearValue(FlexPanel.BasisProperty);
        if (fa is { MinWidth: { } minWidth }) FlexPanel.SetMinWidth(ctrl, minWidth);
        else ctrl.ClearValue(FlexPanel.FlexMinWidthProperty);
        if (fa is { MinHeight: { } minHeight }) FlexPanel.SetMinHeight(ctrl, minHeight);
        else ctrl.ClearValue(FlexPanel.FlexMinHeightProperty);
        if (fa is { AlignSelf: { } alignSelf }) FlexPanel.SetAlignSelf(ctrl, alignSelf);
        else ctrl.ClearValue(FlexPanel.AlignSelfProperty);
        FlexPanel.SetPosition(ctrl, fa?.Position ?? FlexPositionType.Relative);
        if (fa is { Left: { } left }) FlexPanel.SetLeft(ctrl, left);
        else ctrl.ClearValue(FlexPanel.LeftProperty);
        if (fa is { Top: { } top }) FlexPanel.SetTop(ctrl, top);
        else ctrl.ClearValue(FlexPanel.TopProperty);
        if (fa is { Right: { } right }) FlexPanel.SetRight(ctrl, right);
        else ctrl.ClearValue(FlexPanel.RightProperty);
        if (fa is { Bottom: { } bottom }) FlexPanel.SetBottom(ctrl, bottom);
        else ctrl.ClearValue(FlexPanel.BottomProperty);
    }
}

/// <summary>
/// Spec 048 §7 — thin <see cref="DescriptorHandler{TElement,TControl}"/>
/// subclass so the Reactor.Factories DSL can reach this descriptor via
/// the <c>Reg&lt;&gt;</c> registration touch without leaking
/// <c>DescriptorHandler&lt;,&gt;</c> as a public surface.
/// </summary>
internal sealed class FlexPanelDescriptorHandler() : DescriptorHandler<FlexElement, FlexPanel>(FlexPanelDescriptor.Descriptor);
