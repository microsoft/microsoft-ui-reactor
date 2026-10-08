using System.Collections;
using System.Collections.ObjectModel;

namespace Microsoft.UI.Reactor.Advanced.Tabular;

/// <summary>
/// The collection a <see cref="TableViewHandler"/> binds for a row snapshot. A named
/// <c>partial</c> type, so the CsWinRT AOT generator emits its WinRT collection interfaces
/// (<c>IBindableVector</c>, <c>INotifyCollectionChanged</c>): an anonymous
/// <c>ObservableCollection&lt;object?&gt;</c> assigned to an <c>object</c>-typed property is
/// rejected under NativeAOT ("items must implement a supported collection interface").
/// </summary>
internal sealed partial class TableRows : ObservableCollection<object?>;

/// <summary>
/// Applies an immutable row snapshot to the collection a <see cref="TableViewHandler"/> binds,
/// as the smallest run of collection edits, so the native table re-realizes only the rows that
/// changed and keeps its selection, scroll position and active edit.
/// </summary>
internal static class RowDiff
{
    /// <summary>
    /// Makes <paramref name="rows"/> equal, element for element (by reference), to
    /// <paramref name="next"/>.
    /// </summary>
    /// <param name="rows">The bound collection; mutated in place.</param>
    /// <param name="next">The new snapshot.</param>
    /// <param name="key">
    /// Row identity. When set, a row whose key persists is moved to its new position and
    /// replaced only if its instance changed; rows with new keys are inserted and rows with
    /// vanished keys removed. When <c>null</c>, rows are matched by position.
    /// </param>
    internal static void Apply(ObservableCollection<object?> rows, IEnumerable next, Func<object, object?>? key)
    {
        var items = next as IList<object?> ?? next.Cast<object?>().ToList();

        if (key is null)
        {
            var common = Math.Min(rows.Count, items.Count);
            for (var i = 0; i < common; i++)
            {
                if (!ReferenceEquals(rows[i], items[i])) rows[i] = items[i];
            }
            while (rows.Count > items.Count) rows.RemoveAt(rows.Count - 1);
            for (var i = rows.Count; i < items.Count; i++) rows.Add(items[i]);
            return;
        }

        object? KeyOf(object? row) => row is null ? null : key(row);

        var wanted = new HashSet<object?>();
        foreach (var item in items) wanted.Add(KeyOf(item));
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(KeyOf(rows[i]))) rows.RemoveAt(i);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var k = KeyOf(item);
            if (i < rows.Count && Equals(KeyOf(rows[i]), k))
            {
                if (!ReferenceEquals(rows[i], item)) rows[i] = item;
                continue;
            }

            var found = -1;
            for (var j = i + 1; j < rows.Count; j++)
            {
                if (Equals(KeyOf(rows[j]), k)) { found = j; break; }
            }

            if (found >= 0)
            {
                rows.Move(found, i);
                if (!ReferenceEquals(rows[i], item)) rows[i] = item;
            }
            else
            {
                rows.Insert(i, item);
            }
        }

        // Duplicate keys can leave surplus rows behind.
        while (rows.Count > items.Count) rows.RemoveAt(rows.Count - 1);
    }
}

/// <summary>The commit rule for a <see cref="TableTextColumn"/> edit.</summary>
internal static class TextColumnEdit
{
    /// <summary>
    /// Whether a committed edit should reach <see cref="TableTextColumn.OnEdit"/>: only for a
    /// known row, when the column has a callback and the text actually changed.
    /// </summary>
    internal static bool ShouldCommit(TableTextColumn column, object? item, string? text) =>
        column.OnEdit is not null
        && item is not null
        && text is not null
        && !string.Equals(text, column.Text(item) ?? string.Empty, StringComparison.Ordinal);
}