using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 8) — descriptor variant of the hand-coded
/// <c>MountGrid</c> / <c>UpdateGrid</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event panel container with two simple
/// one-way numeric props (<c>RowSpacing</c>, <c>ColumnSpacing</c>) plus a
/// reference-keyed <c>OneWay&lt;GridDefinition&gt;</c> entry whose set
/// lambda clears + rebuilds the WinUI <c>RowDefinitions</c> /
/// <c>ColumnDefinitions</c> collections through
/// <see cref="Reconciler.ParseRowDef"/> / <see cref="Reconciler.ParseColumnDef"/>.
/// The comparer is <see cref="ReferenceEqualityComparer.Instance"/> so the
/// rebuild only fires when the element's <c>Definition</c> instance changes
/// (mirrors the legacy <c>!ReferenceEquals(o.Definition, n.Definition)</c>
/// gate). Children are dispatched through the
/// <see cref="Panel{TElement,TControl}"/> strategy.</para>
///
/// <para><b>Known gaps:</b> the legacy hand-coded path applies
/// <see cref="GridAttached"/> (Row / Column / RowSpan / ColumnSpan) per
/// child after children mount. The Panel strategy in V1HandlerAdapter
/// doesn't surface a per-child post-mount hook yet, so descriptor-mounted
/// children stack at row 0 / column 0. Authors who need
/// <c>Grid.SetRow</c> / <c>Grid.SetColumn</c> stay on V1 OFF (legacy arm).
/// Container-level spacing and definitions have parity.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class GridDescriptor
{
    private static readonly Panel<GridElement, WinUI.Grid> ChildrenStrategy =
        new Panel<GridElement, WinUI.Grid>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children);

    public static readonly ControlDescriptor<GridElement, WinUI.Grid> Descriptor =
        new ControlDescriptor<GridElement, WinUI.Grid>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.RowSpacing,
            set: static (c, v) => c.RowSpacing = v)
        .OneWay(
            get: static e => e.ColumnSpacing,
            set: static (c, v) => c.ColumnSpacing = v)
        .OneWay<GridDefinition>(
            get: static e => e.Definition,
            set: static (c, v) =>
            {
                c.ColumnDefinitions.Clear();
                c.RowDefinitions.Clear();
                if (v is null) return;
                foreach (var col in v.Columns)
                    c.ColumnDefinitions.Add(Reconciler.ParseColumnDef(col));
                foreach (var row in v.Rows)
                    c.RowDefinitions.Add(Reconciler.ParseRowDef(row));
            },
            comparer: GridDefinitionReferenceComparer.Instance);

    private sealed class GridDefinitionReferenceComparer : IEqualityComparer<GridDefinition>
    {
        public static readonly GridDefinitionReferenceComparer Instance = new();
        public bool Equals(GridDefinition? x, GridDefinition? y) => ReferenceEquals(x, y);
        public int GetHashCode(GridDefinition obj) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
