using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Tabular;
using WinUITableView = Microsoft.UI.Xaml.Controls.Tabular.TableView;

namespace Microsoft.UI.Reactor.Advanced.Tabular;

/// <summary>Fluent modifiers for <see cref="TableViewElement"/> and <see cref="TableColumn"/>.</summary>
public static class TableViewModifiers
{
    /// <summary>Sets the row selection mode.</summary>
    public static TableViewElement SelectionMode(this TableViewElement el, TableViewSelectionMode mode) =>
        el with { SelectionMode = mode };

    /// <summary>Controls the selected row (<c>-1</c> clears it).</summary>
    public static TableViewElement SelectedIndex(this TableViewElement el, int index) =>
        el with { SelectedIndex = index };

    /// <summary>Sets the row height density.</summary>
    public static TableViewElement Density(this TableViewElement el, TableViewDensity density) =>
        el with { Density = density };

    /// <summary>Sets which grid lines are drawn.</summary>
    public static TableViewElement GridLines(this TableViewElement el, TableViewGridLinesVisibility visibility) =>
        el with { GridLinesVisibility = visibility };

    /// <summary>Makes every cell read-only (or editable again).</summary>
    public static TableViewElement ReadOnly(this TableViewElement el, bool isReadOnly = true) =>
        el with { IsReadOnly = isReadOnly };

    /// <summary>
    /// Gives rows a stable identity so successive snapshots are diffed by key (moves, inserts,
    /// removals, in-place replacements) instead of by position. See <see cref="TableViewElement.RowKey"/>.
    /// </summary>
    public static TableViewElement KeyRows<T>(this TableViewElement el, Func<T, object?> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return el with { RowKey = item => item is T row ? key(row) : item };
    }

    /// <summary>Filters the rows (see <see cref="TableViewElement.Filter"/>).</summary>
    public static TableViewElement FilterRows<T>(this TableViewElement el, Func<T, bool>? predicate) =>
        el with { Filter = predicate is null ? null : item => item is T row && predicate(row) };

    /// <summary>Groups the rows by a key (see <see cref="TableViewElement.GroupBy"/>).</summary>
    public static TableViewElement GroupRows<T>(this TableViewElement el, Func<T, object?>? keySelector) =>
        el with { GroupBy = keySelector is null ? null : item => item is T row ? keySelector(row) : null };

    /// <summary>Raised when the selected row changes, with the new index (<c>-1</c> when cleared) and item.</summary>
    public static TableViewElement SelectionChanged(this TableViewElement el, Action<int, object?>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Raised after a column sort is applied or cleared.</summary>
    public static TableViewElement Sorted(this TableViewElement el, Action<TableViewSortedEventArgs>? handler) =>
        el with { OnSorted = handler };

    /// <summary>Raised before a cell edit is committed or cancelled.</summary>
    public static TableViewElement CellEditEnding(this TableViewElement el, Action<TableViewCellEditEndingEventArgs>? handler) =>
        el with { OnCellEditEnding = handler };

    /// <summary>Adds a raw <see cref="WinUITableView"/> setter (escape hatch).</summary>
    public static TableViewElement Set(this TableViewElement el, Action<WinUITableView> setter) =>
        el with { Setters = [.. el.Setters, setter] };

    /// <summary>Pins the column to the leading (default) or trailing edge.</summary>
    public static TableColumn Frozen(this TableColumn column, TableViewFrozenEdge edge = TableViewFrozenEdge.Leading) =>
        column with { FrozenEdge = edge };

    /// <summary>Gives the column a proportional share of the remaining width.</summary>
    public static TableColumn Star(this TableColumn column, double weight = 1) =>
        column with { Width = new GridLength(weight, GridUnitType.Star) };

    /// <summary>Makes the column's cells read-only (or editable again).</summary>
    public static TableColumn ReadOnly(this TableColumn column, bool isReadOnly = true) =>
        column with { IsReadOnly = isReadOnly };

    /// <summary>Sorts the column by the row property at <paramref name="path"/>.</summary>
    public static TableColumn SortBy(this TableColumn column, string path) =>
        column with { SortMemberPath = path };

    /// <summary>Sorts the column with a typed comparison (no reflection metadata needed).</summary>
    public static TableColumn SortBy<T>(this TableColumn column, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return column with
        {
            SortComparer = (a, b) => (a, b) switch
            {
                (T x, T y) => comparison(x, y),
                (null, null) => 0,
                (null, _) => -1,
                (_, null) => 1,
                _ => 0,
            },
        };
    }

    /// <summary>Renders an editor while a cell of this column is being edited.</summary>
    public static TableColumn Editing<T>(this TableColumn column, Func<T, Core.Element?> editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return column with { EditingCell = item => item is T row ? editor(row) : null };
    }
}
