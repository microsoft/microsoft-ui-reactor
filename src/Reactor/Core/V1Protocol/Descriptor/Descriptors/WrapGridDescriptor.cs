using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3-final Batch E — descriptor variant of the
/// hand-coded <c>MountWrapGrid</c> / <c>UpdateWrapGrid</c> arms in
/// <see cref="Reconciler"/>. Closes the Phase 3 batch-8 escape-hatch
/// ("WrapGrid escape-hatched (needs per-child attached-prop hook)") — the
/// Batch A additive <see cref="Panel{TElement,TControl}.PerChildAttached"/>
/// surface makes the per-child <see cref="WrapGridAttached"/> writes
/// expressible declaratively.
///
/// <para><b>Coverage:</b> a zero-event panel container backed by
/// <see cref="WinUI.VariableSizedWrapGrid"/>. Four conditional one-way
/// props (<c>MaximumRowsOrColumns</c> ≥ 0, <c>ItemWidth</c> /
/// <c>ItemHeight</c> non-NaN, <c>Orientation</c> always written). Children
/// are dispatched through the <see cref="Panel{TElement,TControl}"/>
/// strategy with a <see cref="Panel{TElement,TControl}.PerChildAttached"/>
/// callback that mirrors the legacy <c>MountWrapGrid</c> arm —
/// <c>VariableSizedWrapGrid.SetRowSpan</c> /
/// <c>VariableSizedWrapGrid.SetColumnSpan</c> only when the hint is
/// &gt; 1 (default).</para>
/// </summary>
internal static class WrapGridDescriptor
{
    private static readonly Panel<WrapGridElement, WinUI.VariableSizedWrapGrid> ChildrenStrategy =
        new Panel<WrapGridElement, WinUI.VariableSizedWrapGrid>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children)
        {
            PerChildAttached = static (grid, ui, childEl) =>
            {
                if (ui is not FrameworkElement fe) return;
                var wga = childEl.GetAttached<WrapGridAttached>();
                if (wga is null)
                {
                    fe.ClearValue(WinUI.VariableSizedWrapGrid.RowSpanProperty);
                    fe.ClearValue(WinUI.VariableSizedWrapGrid.ColumnSpanProperty);
                    return;
                }
                if (wga.RowSpan > 1) WinUI.VariableSizedWrapGrid.SetRowSpan(fe, wga.RowSpan);
                else fe.ClearValue(WinUI.VariableSizedWrapGrid.RowSpanProperty);
                if (wga.ColumnSpan > 1) WinUI.VariableSizedWrapGrid.SetColumnSpan(fe, wga.ColumnSpan);
                else fe.ClearValue(WinUI.VariableSizedWrapGrid.ColumnSpanProperty);
            },
        };

    public static readonly ControlDescriptor<WrapGridElement, WinUI.VariableSizedWrapGrid> Descriptor =
        new ControlDescriptor<WrapGridElement, WinUI.VariableSizedWrapGrid>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Orientation,
            set: static (c, v) => c.Orientation = v)
        .OneWayConditional(
            get:         static e => e.MaximumRowsOrColumns,
            set:         static (c, v) => c.MaximumRowsOrColumns = v,
            shouldWrite: static e => e.MaximumRowsOrColumns >= 0)
        .OneWayConditional(
            get:         static e => e.ItemWidth,
            set:         static (c, v) => c.ItemWidth = v,
            shouldWrite: static e => !double.IsNaN(e.ItemWidth))
        .OneWayConditional(
            get:         static e => e.ItemHeight,
            set:         static (c, v) => c.ItemHeight = v,
            shouldWrite: static e => !double.IsNaN(e.ItemHeight));
}
