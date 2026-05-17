using System.Collections.ObjectModel;

namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// Stable identity carrier inside the internally-owned
/// <see cref="ObservableCollection{T}"/> backing a templated items control.
/// Reference type by design: WinUI's <c>INotifyCollectionChanged</c>
/// consumers use object identity to track items across the event stream,
/// so reusing the same <see cref="ReactorRow"/> instance for surviving keys
/// is what lets WinUI distinguish "this item moved" from
/// "removed + inserted." See spec 042 §4.
/// </summary>
internal sealed class ReactorRow
{
    /// <summary>
    /// Current zero-based position in the visible source. Kept in sync with
    /// the <see cref="ReactorListState.Source"/> index after every diff op.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Key produced by the user's <c>KeySelector</c> for this row's data.
    /// Stable across reorder; never null after construction.
    /// </summary>
    public string Key { get; set; } = "";

    public override string ToString() => $"Row[{Index}]={Key}";
}

/// <summary>
/// Per-mounted-control state that backs the internal
/// <see cref="ObservableCollection{T}"/> delta channel for ListView /
/// GridView / ItemsRepeater. Owned by the reconciler; attached to the
/// host control via the <c>Reconciler.GetListState</c> /
/// <c>Reconciler.SetListState</c> helpers (added in task 1.2).
/// See spec 042 §4 for the model.
/// </summary>
internal sealed class ReactorListState
{
    /// <summary>
    /// The collection WinUI binds to. Mutations on this collection raise
    /// <c>CollectionChanged</c>, which is what drives incremental container
    /// add / remove / move animations.
    /// </summary>
    public ObservableCollection<ReactorRow> Source { get; } = new();

    /// <summary>
    /// Key → row lookup. Rebuilt incrementally during each diff so the
    /// next diff can find survivors in O(1).
    /// </summary>
    public Dictionary<string, ReactorRow> ByKey { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Snapshot of the keys present after the last successful diff (in
    /// visual order). Used to drive the lockstep prefix walk in
    /// <see cref="KeyedListDiff"/>.
    /// </summary>
    public List<string> LastKeys { get; } = new();

    /// <summary>
    /// Reusable working dictionary for the diff's "remaining old rows"
    /// step. Cleared at end of each diff so allocations don't accumulate.
    /// </summary>
    internal Dictionary<string, ReactorRow> Scratch { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Reset the state to a fresh set of (index, key) pairs. Used during
    /// mount and during bulk-replace bailout when the entire list is being
    /// rebuilt from scratch.
    /// </summary>
    /// <param name="rows">
    /// Sequence of (index, key) pairs in visual order. Indices are not
    /// trusted — this method assigns them positionally so the input only
    /// needs to be in the desired output order.
    /// </param>
    public void Reset(IEnumerable<(int Index, string Key)> rows)
    {
        Source.Clear();
        ByKey.Clear();
        LastKeys.Clear();

        int i = 0;
        foreach (var (_, key) in rows)
        {
            var row = new ReactorRow { Index = i, Key = key };
            Source.Add(row);
            // Preserve the first-occurrence row when the caller passes
            // duplicate keys; the diff's bailout path handles that case
            // by rebuilding state via this method, so we must remain
            // tolerant. ByKey then points at the canonical (first) row.
            if (!ByKey.ContainsKey(key))
                ByKey.Add(key, row);
            LastKeys.Add(key);
            i++;
        }
    }
}
