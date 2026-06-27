using System.Globalization;

namespace StressPerf.KeyedList;

/// <summary>
/// One keyed row. <see cref="Key"/> is the stable reconciler identity
/// (<c>.WithKey(...)</c>); <see cref="Label"/> is the display text. Both strings
/// are computed once at construction so the per-render hot path only pays for the
/// element-record allocation under measurement — never per-render key/label
/// formatting (mirrors how StockDataSource pre-bakes its cell symbols).
/// </summary>
public readonly record struct KeyedRow(int Id, string Key, string Label);

/// <summary>
/// Deterministic keyed-list workload data source for the /perf macro benchmark.
///
/// Unlike <see cref="StressPerf.Shared.StockDataSource"/> — whose cells are
/// POSITIONAL (mutated in place by index, hitting
/// <c>ChildReconciler.ReconcilePositional</c>) — this source maintains an ORDERED
/// list of stably-keyed rows and, each tick, REORDERS / INSERTS / REMOVES them.
/// When the rendered children carry those keys, the child reconciler takes the
/// keyed arm (<c>ReconcileKeyed</c> → <c>ReconcileKeyedMiddle</c>, the LIS-based
/// minimal-move machinery) instead of the positional arm. That is the hot path
/// the StocksGrid workload can never exercise.
///
/// Deterministic (fixed RNG seed) so main-vs-PR /perf runs compare identical
/// edit sequences; the list size is held constant (insertions paired with
/// removals) so working-set and render-count stay stable across the run.
/// </summary>
public sealed class KeyedListSource
{
    /// <summary>Default row count — ~500 to mirror StocksGrid's cell magnitude.</summary>
    public const int DefaultCount = 500;

    private readonly List<KeyedRow> _items;
    private readonly Random _rng = new(42); // deterministic seed (matches StockDataSource)
    private int _nextId;

    public KeyedListSource(int count = DefaultCount)
    {
        if (count < 1) count = 1;
        _items = new List<KeyedRow>(count);
        for (int i = 0; i < count; i++)
            _items.Add(MakeRow(i));
        _nextId = count;
    }

    /// <summary>Current row count (held constant across ticks).</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Reorder / insert / remove a percentage of the keyed rows for one tick.
    ///
    /// <paramref name="percent"/> sets the structural-edit budget
    /// <c>k = round(N * percent / 100)</c> (clamped to <c>[0, N]</c>; <c>percent == 0</c>
    /// leaves the order untouched — the keyed ALL-MATCH FLOOR). Of that
    /// budget a quarter is spent on insert+remove CHURN — each removal paired with
    /// a fresh-key insertion so the row SET changes over time (not just a fixed
    /// permutation) while N stays constant — and the remainder on MOVES (pull a row
    /// from one position and reinsert it at another, key preserved). Both kinds keep
    /// matched keys in the post-prefix/suffix "middle", which is what drives
    /// <c>ReconcileKeyedMiddle</c>'s LIS reorder pass.
    /// </summary>
    /// <returns>The structural-edit budget actually applied (for logging parity).</returns>
    public int Update(double percent)
    {
        var rng = _rng;
        int n = _items.Count;
        int k = (int)Math.Round(n * percent / 100.0, MidpointRounding.AwayFromZero);
        if (k < 0) k = 0;
        if (k > n) k = n;

        // percent == 0 → k == 0: leave the order untouched so every key matches in
        // place. That is the keyed ALL-MATCH FLOOR (LIS == whole list, zero moves) —
        // the case the keyed structural-skip optimization targets. Snapshot() still
        // allocates a fresh array each tick, so a render (and a keyed diff) still runs.
        if (k == 0)
            return 0;

        // Insert/remove churn — net-zero so the list size is invariant. Removing
        // before inserting keeps every index access in range.
        int churn = k / 4;
        for (int i = 0; i < churn; i++)
        {
            _items.RemoveAt(rng.Next(_items.Count));
            _items.Insert(rng.Next(_items.Count + 1), MakeRow(_nextId++));
        }

        // Moves — reorder existing rows, preserving their keys, so the keyed diff
        // sees a permutation in the middle region and computes an LIS.
        int moves = k - churn;
        for (int i = 0; i < moves; i++)
        {
            int from = rng.Next(_items.Count);
            var row = _items[from];
            _items.RemoveAt(from);
            _items.Insert(rng.Next(_items.Count + 1), row);
        }

        return k;
    }

    /// <summary>
    /// Immutable snapshot of the current row order. The Reactor variant rebuilds its
    /// full keyed child array from this each render (no positional memo fast-path),
    /// so the reconciler runs a real keyed diff every tick.
    /// </summary>
    public KeyedRow[] Snapshot() => _items.ToArray();

    private static KeyedRow MakeRow(int id)
    {
        string key = id.ToString(CultureInfo.InvariantCulture);
        // Deterministic, content-stable label derived from the row's identity, so a
        // moved row's text never changes — isolating the STRUCTURAL (keyed-diff)
        // signal from per-cell property updates.
        string label = string.Create(CultureInfo.InvariantCulture, $"Row {id} · item-{id % 97:000}");
        return new KeyedRow(id, key, label);
    }
}
