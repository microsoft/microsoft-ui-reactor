using System.Collections;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Tabular;
using Microsoft.UI.Xaml.Media;
using WinUITableView = Microsoft.UI.Xaml.Controls.Tabular.TableView;

namespace Microsoft.UI.Reactor.Advanced.Tabular;

/// <summary>
/// Reactor element for the WinUI <see cref="WinUITableView"/>
/// (<c>Microsoft.UI.Xaml.Controls.Tabular</c>, Windows App SDK experimental channel):
/// a virtualized, column-oriented table with sorting, grouping, filtering, inline
/// editing and single-row selection.
/// </summary>
/// <remarks>
/// Create it with <c>TableView(items, columns...)</c> from
/// <see cref="Microsoft.UI.Reactor.Advanced.Factories"/>. Every optional property is
/// <c>null</c> by default, which leaves the control's own default in place; a property
/// is written only when it changes between renders, so state the user changes
/// interactively (column widths, the active sort) survives re-renders.
/// </remarks>
public sealed record TableViewElement : Element
{
    /// <summary>
    /// The rows. Pass an immutable snapshot — an array or list of records held in component
    /// state — and replace it to change the rows: the handler diffs each new snapshot into
    /// a collection it owns (see <see cref="RowKey"/>), so only the rows that changed are
    /// touched and selection, scroll position and the active edit survive. A user-owned
    /// <see cref="global::System.Collections.Specialized.INotifyCollectionChanged"/>
    /// collection is bound directly instead; under NativeAOT it must be a named
    /// <c>partial</c> collection type, or CsWinRT cannot project it.
    /// </summary>
    public IEnumerable? ItemsSource { get; init; }

    /// <summary>
    /// Stable identity of a row, used to diff successive <see cref="ItemsSource"/> snapshots:
    /// a row whose key moved is moved, a row whose key is new is inserted, and a row whose key
    /// persists but whose value changed (for example <c>person with { Name = … }</c>) is
    /// replaced in place. When <c>null</c>, rows are matched by position.
    /// </summary>
    public Func<object, object?>? RowKey { get; init; }

    /// <summary>The columns, in display order. See <see cref="TableColumn"/>.</summary>
    public IReadOnlyList<TableColumn> Columns { get; init; } = Array.Empty<TableColumn>();

    /// <summary>
    /// Optional row predicate. When set, <see cref="ItemsSource"/> is wrapped in a
    /// <see cref="TableViewSource"/> and filtered without copying. The predicate is
    /// re-applied whenever the delegate instance changes, so memoize it
    /// (<c>UseMemo</c>/<c>UseCallback</c>) when the table is large.
    /// </summary>
    public Func<object, bool>? Filter { get; init; }

    /// <summary>
    /// Optional grouping key selector. When set, rows are grouped under collapsible
    /// group headers. Re-applied whenever the delegate instance changes.
    /// </summary>
    public Func<object, object?>? GroupBy { get; init; }

    /// <summary>
    /// Optional group header content. Receives the group's
    /// <see cref="TableViewGroupInfo"/> (key, item count, nesting level). When
    /// <c>null</c>, the control's default header is used.
    /// </summary>
    public Func<TableViewGroupInfo, Element?>? GroupHeader { get; init; }

    /// <summary>Content shown in place of the rows when the source is empty.</summary>
    public Element? EmptyContent { get; init; }

    /// <summary>Row selection mode (<c>None</c> or <c>Single</c>).</summary>
    public TableViewSelectionMode? SelectionMode { get; init; }

    /// <summary>
    /// Controlled selected row index (<c>-1</c> clears the selection). When <c>null</c>,
    /// the control owns the selection.
    /// </summary>
    public int? SelectedIndex { get; init; }

    /// <summary>Row height density (<c>Compact</c>, <c>Standard</c>, <c>Comfortable</c>).</summary>
    public TableViewDensity? Density { get; init; }

    /// <summary>Which cell grid lines are drawn.</summary>
    public TableViewGridLinesVisibility? GridLinesVisibility { get; init; }

    /// <summary>Whether the column header row is shown.</summary>
    public TableViewHeadersVisibility? HeadersVisibility { get; init; }

    /// <summary>Whether clicking a column header sorts by that column.</summary>
    public bool? CanUserSortColumns { get; init; }

    /// <summary>Whether the user can drag column header edges to resize columns.</summary>
    public bool? CanUserResizeColumns { get; init; }

    /// <summary>
    /// Whether cells are read-only. When <c>null</c>, a table with an editable column — a
    /// <see cref="TableTextColumn"/> with an <see cref="TableTextColumn.OnEdit"/> callback, or a
    /// column with an <see cref="TableColumn.EditingCell"/> — is editable; otherwise the control
    /// default (read-only) applies, so a <see cref="TableBoundColumn"/> table opts in with
    /// <c>.ReadOnly(false)</c>.
    /// </summary>
    public bool? IsReadOnly { get; init; }

    /// <summary>Background brush for every row.</summary>
    public Brush? RowBackground { get; init; }

    /// <summary>Background brush for alternating rows (row banding).</summary>
    public Brush? AlternatingRowBackground { get; init; }

    /// <summary>Raised when the selected row changes, with the new index (<c>-1</c> when cleared) and item.</summary>
    public Action<int, object?>? OnSelectionChanged { get; init; }

    /// <summary>Raised before a column sort is applied. Set <c>Cancel</c> to veto it.</summary>
    public Action<TableViewSortingEventArgs>? OnSorting { get; init; }

    /// <summary>Raised after a column sort is applied (or cleared: <c>Direction == None</c>).</summary>
    public Action<TableViewSortedEventArgs>? OnSorted { get; init; }

    /// <summary>Raised before a cell enters edit mode. Set <c>Cancel</c> to veto it.</summary>
    public Action<TableViewBeginningEditEventArgs>? OnBeginningEdit { get; init; }

    /// <summary>Raised before a cell edit is committed or cancelled.</summary>
    public Action<TableViewCellEditEndingEventArgs>? OnCellEditEnding { get; init; }

    /// <summary>Raw control setters applied after typed properties (escape hatch).</summary>
    public Action<WinUITableView>[] Setters { get; init; } = Array.Empty<Action<WinUITableView>>();

    internal TableViewElement() { }
}

/// <summary>
/// A <see cref="TableViewElement"/> column. Use <see cref="TableTextColumn"/> for text read
/// from the row in C# (optionally edited through a callback), <see cref="TableTemplateColumn"/>
/// for cells rendered as Reactor elements, and <see cref="TableBoundColumn"/> for a classic
/// WinUI <c>{Binding}</c> over a mutable, INPC row model. Optional properties left <c>null</c>
/// keep the control default.
/// </summary>
public abstract record TableColumn
{
    private protected TableColumn() { }

    /// <summary>Header content (usually a string).</summary>
    public object? Header { get; init; }

    /// <summary>Tooltip shown on the column header.</summary>
    public object? HeaderToolTip { get; init; }

    /// <summary>
    /// Column width. Use <c>new GridLength(120)</c> for pixels,
    /// <c>new GridLength(1, GridUnitType.Star)</c> to share the remaining space, or
    /// <see cref="GridLength.Auto"/>.
    /// </summary>
    public GridLength? Width { get; init; }

    /// <summary>Minimum width, in pixels, when the user resizes the column.</summary>
    public double? MinWidth { get; init; }

    /// <summary>Maximum width, in pixels, when the user resizes the column.</summary>
    public double? MaxWidth { get; init; }

    /// <summary>Whether the user can sort by this column.</summary>
    public bool? CanSort { get; init; }

    /// <summary>Whether the user can resize this column.</summary>
    public bool? CanResize { get; init; }

    /// <summary>Whether this column's cells are read-only.</summary>
    public bool? IsReadOnly { get; init; }

    /// <summary>Pins the column to the leading or trailing edge during horizontal scroll.</summary>
    public TableViewFrozenEdge? FrozenEdge { get; init; }

    /// <summary>The direction sequence a header click steps through.</summary>
    public TableViewSortCycle? SortCycle { get; init; }

    /// <summary>
    /// Property path the column sorts by (resolved by reflection). A
    /// <see cref="TableBoundColumn"/> sorts by its <see cref="TableBoundColumn.Path"/> and a
    /// <see cref="TableTextColumn"/> by its text when this and <see cref="SortComparer"/> are
    /// <c>null</c>.
    /// </summary>
    public string? SortMemberPath { get; init; }

    /// <summary>
    /// Typed comparison used to sort the column instead of <see cref="SortMemberPath"/>.
    /// Receives two row items; return a negative, zero or positive value. Sorting with a
    /// comparer needs no reflection metadata, so prefer it under NativeAOT.
    /// </summary>
    public Comparison<object?>? SortComparer { get; init; }

    /// <summary>
    /// Content shown while a cell of this column is being edited. Receives the row item;
    /// write edits back through your own callbacks (for example a <c>TextBox</c>'s
    /// <c>onChanged</c>). For a <see cref="TableTextColumn"/> this replaces the built-in
    /// text editor; for a <see cref="TableBoundColumn"/>, the bound editor.
    /// </summary>
    public Func<object?, Element?>? EditingCell { get; init; }

    /// <summary>Whether the column is shown.</summary>
    public bool IsVisible { get; init; } = true;
}

/// <summary>
/// A text column read from the row in C#: no property path, no
/// <see cref="global::System.ComponentModel.INotifyPropertyChanged"/>, and nothing for the
/// trimmer to lose, so it works unchanged with immutable records and under NativeAOT.
/// </summary>
/// <remarks>
/// <para>It is the platform's own text column — TextBlock cells, a TextBox editor, the cell's
/// accessible value and UIA value pattern — fed by a pathless binding through a C# converter
/// that calls <see cref="Text"/>. A cell re-reads its text when its row is replaced (for
/// example by a new record in the next snapshot) or re-realized.</para>
/// <para>The column sorts by its text unless <see cref="TableColumn.SortComparer"/> or
/// <see cref="TableColumn.SortMemberPath"/> says otherwise.</para>
/// <para>It is editable when <see cref="OnEdit"/> is set (and the table then defaults to
/// editable): double-click, F2 or typing opens the editor, and committing (Enter, or moving to
/// another cell) calls <see cref="OnEdit"/> with the row and the new text — update your state
/// there, for example <c>setPeople([.. people.Select(x => x == p ? p with { Name = text } : x)])</c>.
/// Nothing is written into the row object, and Escape cancels without a call. Without
/// <see cref="OnEdit"/> (or <see cref="TableColumn.EditingCell"/>) the column is read-only.</para>
/// </remarks>
public sealed record TableTextColumn : TableColumn
{
    /// <summary>Reads the cell text from a row.</summary>
    public required Func<object?, string?> Text { get; init; }

    /// <summary>Commits an edit: receives the row and the new text. <c>null</c> makes the column read-only.</summary>
    public Action<object, string>? OnEdit { get; init; }
}

/// <summary>
/// A column that displays — and, unless read-only, edits in place — a row property through a
/// classic WinUI <c>{Binding}</c> on <see cref="Path"/>. Edits are written straight into the
/// row object, so this suits a mutable model; prefer <see cref="TableTextColumn"/> for
/// immutable rows.
/// </summary>
/// <remarks>
/// The binding resolves <see cref="Path"/> by name at run time. Under NativeAOT/trimming
/// annotate the row type with <c>[WinRT.GeneratedBindableCustomProperty]</c>. Rows
/// implementing <see cref="global::System.ComponentModel.INotifyPropertyChanged"/> update in
/// place; <see cref="global::System.ComponentModel.INotifyDataErrorInfo"/> errors keep the
/// editor open.
/// </remarks>
public sealed record TableBoundColumn : TableColumn
{
    /// <summary>Property path of the bound value (for example <c>"Name"</c> or <c>"Address.City"</c>).</summary>
    public required string Path { get; init; }
}

/// <summary>A column whose cells are rendered as Reactor elements.</summary>
/// <remarks>
/// Cells are mounted on demand as rows are realized, and re-rendered in place when a row
/// is recycled for another item or when the column record changes (every render that
/// builds a new column). Component state inside a cell therefore belongs to the realized
/// row, not to the item. Set <see cref="TableColumn.SortMemberPath"/> or
/// <see cref="TableColumn.SortComparer"/> to make the column sortable.
/// </remarks>
public sealed record TableTemplateColumn : TableColumn
{
    /// <summary>Renders the cell for a row item.</summary>
    public required Func<object?, Element?> Cell { get; init; }
}
