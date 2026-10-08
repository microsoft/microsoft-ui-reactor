using System.Collections;
using Microsoft.UI.Reactor.Advanced.Tabular;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Advanced;

// TableView (Microsoft.UI.Xaml.Controls.Tabular) factories. The element, its column
// records and the fluent modifiers live in Microsoft.UI.Reactor.Advanced.Tabular.
public static partial class Factories
{
    /// <summary>
    /// Creates a WinUI <c>TableView</c>: a virtualized table over <paramref name="itemsSource"/>
    /// with the given columns.
    /// </summary>
    /// <param name="itemsSource">
    /// The rows — usually an immutable array or list of records from component state; replace
    /// it to change the rows. A user-managed observable collection
    /// is bound directly.
    /// </param>
    /// <param name="columns">The columns, in display order (see <see cref="TextColumn{T}"/>, <see cref="TemplateColumn{T}"/> and <see cref="BoundColumn"/>).</param>
    /// <remarks>Requires the Windows App SDK experimental channel; the API may change.</remarks>
    public static TableViewElement TableView(IEnumerable? itemsSource, params TableColumn[] columns) =>
        new() { ItemsSource = itemsSource, Columns = columns };

    /// <summary>
    /// Creates a text column that reads its text from each row of type <typeparamref name="T"/>
    /// in C# — no binding, no <c>INotifyPropertyChanged</c>, NativeAOT-safe. It sorts by that
    /// text, and is editable when <paramref name="onEdit"/> is set: a committed edit calls it
    /// with the row and the new text, so you can update your state immutably.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="header">Header content (usually a string).</param>
    /// <param name="text">Reads the cell text from a row.</param>
    /// <param name="onEdit">Commits an edit; <c>null</c> keeps the column read-only.</param>
    /// <param name="width">Optional width in pixels.</param>
    public static TableTextColumn TextColumn<T>(object header, Func<T, string?> text, Action<T, string>? onEdit = null, double? width = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new()
        {
            Header = header,
            Text = item => item is T row ? text(row) : null,
            OnEdit = onEdit is null ? null : (item, value) => { if (item is T row) onEdit(row, value); },
            Width = width is double w ? new GridLength(w) : null,
        };
    }

    /// <summary>
    /// Creates a column bound to the row property at <paramref name="path"/> through a classic
    /// WinUI <c>{Binding}</c>; edits are written into the row object. Suits a mutable,
    /// <c>INotifyPropertyChanged</c> model — prefer <see cref="TextColumn{T}"/> for immutable rows.
    /// </summary>
    /// <param name="header">Header content (usually a string).</param>
    /// <param name="path">Property path of the bound value, for example <c>"Name"</c>.</param>
    /// <param name="width">Optional width in pixels.</param>
    public static TableBoundColumn BoundColumn(object header, string path, double? width = null) =>
        new() { Header = header, Path = path, Width = width is double w ? new GridLength(w) : null };

    /// <summary>
    /// Creates a column whose cells are Reactor elements rendered from each row of type
    /// <typeparamref name="T"/>. Rows of another type render an empty cell.
    /// </summary>
    /// <typeparam name="T">The row item type.</typeparam>
    /// <param name="header">Header content (usually a string).</param>
    /// <param name="cell">Renders the cell for a row.</param>
    /// <param name="width">Optional width in pixels.</param>
    public static TableTemplateColumn TemplateColumn<T>(object header, Func<T, Element?> cell, double? width = null)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return new()
        {
            Header = header,
            Cell = item => item is T row ? cell(row) : null,
            Width = width is double w ? new GridLength(w) : null,
        };
    }
}
