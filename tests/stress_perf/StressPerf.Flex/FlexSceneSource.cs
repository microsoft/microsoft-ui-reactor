using System.Globalization;

namespace StressPerf.Flex;

/// <summary>
/// One leaf cell of the deep flex tree. <see cref="Grow"/>, <see cref="Basis"/> and
/// <see cref="Width"/> are the per-child flex inputs pushed onto the Yoga node every
/// render (via <c>.Flex(grow, basis, ...)</c> + <c>.Width(...)</c>); mutating a
/// fraction of them each tick forces a real Yoga relayout. <see cref="Label"/> is the
/// display text, computed once at construction so the per-render hot path only pays
/// for the element-record allocation + the layout pass under measurement — never
/// per-render string formatting (mirrors how KeyedRow pre-bakes its label).
/// </summary>
public readonly record struct FlexLeaf(int Id, string Label, double Grow, double Basis, double Width);

/// <summary>
/// Deterministic deep-flex-tree workload data source for the /perf macro benchmark.
///
/// Where <see cref="StressPerf.Shared.StockDataSource"/> mutates a flat positional
/// grid and the keyed-list source reorders keyed rows, this source backs a DEEP
/// NESTED flex tree (sections → rows → leaf cells) whose leaves' flex inputs
/// (grow / basis / width) are mutated each tick. When the rendered tree pushes those
/// inputs onto the Yoga nodes, a real Yoga measure/layout pass runs every frame — the
/// FlexPanel/Yoga LAYOUT engine the positional StocksGrid and keyed-list workloads
/// never exercise.
///
/// The tree SHAPE (section / row / column counts) is fixed for the whole run, so the
/// child reconciler takes its cheap positional arm and the cost under measurement is
/// the layout-engine work, not child diffing. Deterministic (fixed RNG seed) so
/// main-vs-PR /perf runs replay identical mutation sequences; leaf count is held
/// constant so working-set and render-count stay stable across the run.
/// </summary>
public sealed class FlexSceneSource
{
    // Tree shape. The single knob below — DefaultLeafTarget — drives the whole scale;
    // bump it (and nothing else) if a smoke run shows the alloc-bytes/render or the
    // memory delta is too small to clear harness noise. The inner shape (rows × cols per
    // section) is fixed so the tree stays a deep three-level nest (root-column →
    // section-column → row → leaf); the number of SECTIONS is derived to reach the leaf
    // target. ~2000 leaves at meaningful depth so per-node inline-array memory
    // (#142/#143) and per-frame list/line pooling (#141/#144) are at a scale that
    // survives the noisy Avg-Memory-MB metric — node count is exactly what makes the
    // inline-per-node-storage win visible.
    public const int DefaultLeafTarget = 2000;
    public const int RowsPerSection = 10;
    public const int ColsPerRow = 10;

    public int Sections { get; }
    public int Rows { get; }
    public int Cols { get; }

    private readonly FlexLeaf[] _leaves;
    private readonly Random _rng = new(42); // deterministic seed (matches StockDataSource / KeyedListSource)

    public FlexSceneSource(int leafTarget = DefaultLeafTarget)
    {
        if (leafTarget < 1) leafTarget = 1;
        Rows = RowsPerSection;
        Cols = ColsPerRow;
        int perSection = Rows * Cols;
        // Round the section count UP so the realized leaf count is >= the requested
        // target (e.g. target 2000 → 20 sections → exactly 2000 leaves).
        Sections = Math.Max(1, (leafTarget + perSection - 1) / perSection);

        _leaves = new FlexLeaf[Sections * Rows * Cols];
        for (int i = 0; i < _leaves.Length; i++)
            _leaves[i] = MakeLeaf(i);
    }

    /// <summary>Total leaf-cell count (held constant across ticks).</summary>
    public int Count => _leaves.Length;

    /// <summary>
    /// Re-roll the flex inputs (grow / basis / width) on a percentage of the leaf cells
    /// for one tick.
    ///
    /// <paramref name="percent"/> sets the layout-churn budget
    /// <c>k = round(N * percent / 100)</c> (clamped to <c>[0, N]</c>). Those <c>k</c>
    /// leaves get NEW grow/basis/width values, so their Yoga nodes are re-dirtied and a
    /// real measure/layout pass runs. The remaining <c>N - k</c> leaves keep their
    /// EXACT current values — when the tree re-pushes those unchanged inputs each render,
    /// that is precisely the path #670's YogaNode setter equality guards optimize
    /// (unchanged setter → no re-dirty → the frame-level layout cache HITS). So
    /// <c>percent == 0</c> is the ALL-UNCHANGED FLOOR (every node cache-eligible) and the
    /// win grows visible as the churn fraction varies. <see cref="Snapshot"/> still
    /// allocates a fresh array each tick, so a full render (and layout pass) runs
    /// regardless.
    /// </summary>
    /// <returns>The layout-churn budget actually applied (for logging parity).</returns>
    public int Update(double percent)
    {
        var rng = _rng;
        int n = _leaves.Length;
        int k = (int)Math.Round(n * percent / 100.0, MidpointRounding.AwayFromZero);
        if (k < 0) k = 0;
        if (k > n) k = n;

        // Re-roll k distinct-ish leaves. We don't require strict distinctness — a
        // repeated index just re-rolls the same leaf twice, which is harmless and keeps
        // the per-tick work O(k) with no allocation.
        for (int i = 0; i < k; i++)
        {
            int idx = rng.Next(n);
            var leaf = _leaves[idx];
            _leaves[idx] = leaf with
            {
                Grow = rng.Next(0, 3),                 // 0..2
                Basis = 40 + rng.Next(0, 80),          // 40..119
                Width = 40 + rng.Next(0, 80),          // 40..119
            };
        }

        return k;
    }

    /// <summary>
    /// Immutable snapshot of the current leaf inputs. The harness rebuilds its full
    /// nested flex child tree from this each render (no positional memo fast-path), so a
    /// real Yoga layout pass runs every tick.
    /// </summary>
    public FlexLeaf[] Snapshot() => (FlexLeaf[])_leaves.Clone();

    private FlexLeaf MakeLeaf(int id)
    {
        // Deterministic, content-stable label derived from identity, so a leaf's text
        // never changes — isolating the LAYOUT-engine signal from per-cell text updates.
        string label = string.Create(CultureInfo.InvariantCulture, $"L{id} · {id % 97:000}");
        // Deterministic initial flex inputs from the seeded RNG so main and PR start from
        // the identical scene before any mutation.
        double grow = _rng.Next(0, 3);
        double basis = 40 + _rng.Next(0, 80);
        double width = 40 + _rng.Next(0, 80);
        return new FlexLeaf(id, label, grow, basis, width);
    }
}
