using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// SIMD-equivalence + allocation guard for the keyed-middle bookkeeping.
///
/// <see cref="ChildReconciler.RunKeyedMiddleCore{TSink}"/> keeps its
/// <c>oldRelToPanel</c> survivor-position model exact by shifting a band of
/// entries on every Insert/Move. Those three shift loops were vectorized with
/// <c>System.Numerics.Vector</c> (recovering the keyed-reconcile time the
/// correct right-to-left rewrite cost — see #657 follow-up). Vectorized hot
/// paths are exactly where tail/remainder and overlapping-range bugs hide, so
/// this test pins the production (now SIMD) core to a hand-rolled SCALAR
/// REFERENCE core: it runs both over thousands of random + steady-state keyed
/// edit sequences and asserts the emitted Mount/Move/Patch op stream and the
/// resulting cell model are BIT-FOR-BIT identical. It also asserts the core
/// allocates zero bytes per call (the shifts stay on pooled buffers; Vector is
/// a stack value type).
/// </summary>
public class ChildReconcilerKeyedMiddleSimdTests
{
    private const int NewBase = 1000;
    private const int PrefixBase = 2000;
    private const int SuffixBase = 3000;
    private const int Unpatched = -1;

    private struct Cell
    {
        public int Control;
        public int Content;
    }

    // One recorded sink op: Kind 0 = MountInsert, 1 = MoveExisting, 2 = Patch.
    private readonly record struct Op(int Kind, int A, int B, int C);

    /// <summary>Records the op stream AND maintains the cell model (so both the
    /// emitted sequence and the final order/identity/content can be compared).
    /// Mirrors the real sink's structural mutations exactly.</summary>
    private struct DiffSink : ChildReconciler.IKeyedMiddleSink
    {
        public List<Cell> Cells;
        public List<Op> Ops;

        public bool MountInsert(int newIdx, int panelIdx)
        {
            Cells.Insert(panelIdx, new Cell { Control = NewBase + newIdx, Content = newIdx });
            Ops.Add(new Op(0, newIdx, panelIdx, 0));
            return true;
        }

        public void MoveExisting(int fromIdx, int toIdx)
        {
            var cell = Cells[fromIdx];
            Cells.RemoveAt(fromIdx);
            Cells.Insert(toIdx, cell);
            Ops.Add(new Op(1, fromIdx, toIdx, 0));
        }

        public void Patch(int oldRelIdx, int newIdx, int panelIdx)
        {
            Ops.Add(new Op(2, oldRelIdx, newIdx, panelIdx));
            if (panelIdx >= 0 && panelIdx < Cells.Count)
            {
                var cell = Cells[panelIdx];
                cell.Content = newIdx;
                Cells[panelIdx] = cell;
            }
        }
    }

    /// <summary>Allocation-probe sink: counts only, touches no heap.</summary>
    private struct NoOpSink : ChildReconciler.IKeyedMiddleSink
    {
        public int Mounts;
        public int Moves;
        public int Patches;
        public bool MountInsert(int newIdx, int panelIdx) { Mounts++; return true; }
        public void MoveExisting(int fromIdx, int toIdx) { Moves++; }
        public void Patch(int oldRelIdx, int newIdx, int panelIdx) { Patches++; }
    }

    /// <summary>
    /// Hand-rolled SCALAR reference — a faithful copy of the pre-SIMD
    /// <see cref="ChildReconciler.RunKeyedMiddleCore{TSink}"/> shift loops. The
    /// production core must match this exactly.
    /// </summary>
    private static void RunScalarReference<TSink>(
        ref TSink sink, int[] newToOld, bool[] inLis,
        int newMidLen, int oldMidLen, int initialAnchor, int[] oldRelToPanel)
        where TSink : struct, ChildReconciler.IKeyedMiddleSink
    {
        int anchor = initialAnchor;
        for (int i = newMidLen - 1; i >= 0; i--)
        {
            int oldRel = newToOld[i];
            if (oldRel < 0)
            {
                if (sink.MountInsert(i, anchor))
                    for (int r = 0; r < oldMidLen; r++)
                        if (oldRelToPanel[r] >= anchor) oldRelToPanel[r]++;
                continue;
            }

            int cur = oldRelToPanel[oldRel];
            if (cur < 0) continue;

            if (!inLis[i])
            {
                int to = cur < anchor ? anchor - 1 : anchor;
                if (cur != to)
                {
                    sink.MoveExisting(cur, to);
                    if (cur < to)
                    {
                        for (int r = 0; r < oldMidLen; r++)
                        {
                            int p = oldRelToPanel[r];
                            if (p > cur && p <= to) oldRelToPanel[r] = p - 1;
                        }
                    }
                    else
                    {
                        for (int r = 0; r < oldMidLen; r++)
                        {
                            int p = oldRelToPanel[r];
                            if (p >= to && p < cur) oldRelToPanel[r] = p + 1;
                        }
                    }
                    oldRelToPanel[oldRel] = to;
                    cur = to;
                }
            }

            sink.Patch(oldRel, i, cur);
            anchor = cur;
        }
    }

    private sealed class Model
    {
        public required int[] NewToOld;
        public required bool[] InLis;
        public required int NewMidLen;
        public required int OldMidLen;
        public required int InitialAnchor;
        public required int[] OldRelToPanelBase; // immutable; cloned per run
        public required Cell[] CellsBase;         // immutable; cloned per run
        public required int PrefixLen;
        public required int SuffixLen;
    }

    /// <summary>
    /// Build the post-Step-1 model exactly as production
    /// <c>ReconcileKeyedMiddle</c> does: prefix/suffix strip, last-wins key map,
    /// matched survivors compacted in OLD order to live slots, LIS via the real
    /// <see cref="ChildReconciler.ComputeLISInto"/>.
    /// </summary>
    private static Model BuildModel(int[] oldKeys, int[] newKeys)
    {
        int oldLen = oldKeys.Length, newLen = newKeys.Length;
        int prefix = 0;
        while (prefix < oldLen && prefix < newLen && oldKeys[prefix] == newKeys[prefix]) prefix++;
        int suffix = 0;
        while (suffix < oldLen - prefix && suffix < newLen - prefix &&
               oldKeys[oldLen - 1 - suffix] == newKeys[newLen - 1 - suffix]) suffix++;

        int oldMidLen = oldLen - prefix - suffix;
        int newMidLen = newLen - prefix - suffix;

        var oldKeyMap = new Dictionary<int, int>(oldMidLen);
        for (int r = 0; r < oldMidLen; r++) oldKeyMap[oldKeys[prefix + r]] = r;

        var newToOld = new int[newMidLen];
        var matched = new bool[oldMidLen];
        for (int j = 0; j < newMidLen; j++)
        {
            if (oldKeyMap.TryGetValue(newKeys[prefix + j], out int r)) { newToOld[j] = r; matched[r] = true; }
            else newToOld[j] = -1;
        }

        var inLis = new bool[newMidLen];
        ChildReconciler.ComputeLISInto(newToOld, newMidLen, inLis);

        var oldRelToPanel = new int[oldMidLen];
        for (int r = 0; r < oldMidLen; r++) oldRelToPanel[r] = -1;

        var cells = new List<Cell>();
        for (int k = 0; k < prefix; k++) cells.Add(new Cell { Control = PrefixBase + k, Content = -2 });
        int compact = 0;
        for (int r = 0; r < oldMidLen; r++)
        {
            if (matched[r])
            {
                oldRelToPanel[r] = prefix + compact;
                cells.Add(new Cell { Control = r, Content = Unpatched });
                compact++;
            }
        }
        int initialAnchor = prefix + compact;
        for (int k = 0; k < suffix; k++) cells.Add(new Cell { Control = SuffixBase + k, Content = -2 });

        return new Model
        {
            NewToOld = newToOld,
            InLis = inLis,
            NewMidLen = newMidLen,
            OldMidLen = oldMidLen,
            InitialAnchor = initialAnchor,
            OldRelToPanelBase = oldRelToPanel,
            CellsBase = cells.ToArray(),
            PrefixLen = prefix,
            SuffixLen = suffix,
        };
    }

    /// <summary>Run the production (SIMD) core and the scalar reference over the
    /// same model and assert the op stream + cell model are identical. Returns
    /// the production cells/ops for invariant checks.</summary>
    private static (List<Cell> Cells, List<Op> Ops) RunBothAndCompare(Model m, int caseId)
    {
        // Production (SIMD) core.
        var prodPanel = (int[])m.OldRelToPanelBase.Clone();
        var prod = new DiffSink { Cells = new List<Cell>(m.CellsBase), Ops = new List<Op>() };
        ChildReconciler.RunKeyedMiddleCore(ref prod, m.NewToOld, m.InLis, m.NewMidLen, m.OldMidLen, m.InitialAnchor, prodPanel);

        // Scalar reference core.
        var refPanel = (int[])m.OldRelToPanelBase.Clone();
        var refk = new DiffSink { Cells = new List<Cell>(m.CellsBase), Ops = new List<Op>() };
        RunScalarReference(ref refk, m.NewToOld, m.InLis, m.NewMidLen, m.OldMidLen, m.InitialAnchor, refPanel);

        Assert.True(prod.Ops.Count == refk.Ops.Count,
            $"case {caseId}: op-count {prod.Ops.Count} (simd) vs {refk.Ops.Count} (scalar)");
        for (int o = 0; o < prod.Ops.Count; o++)
            Assert.True(prod.Ops[o] == refk.Ops[o],
                $"case {caseId}: op[{o}] {prod.Ops[o]} (simd) vs {refk.Ops[o]} (scalar)");

        Assert.True(prod.Cells.Count == refk.Cells.Count, $"case {caseId}: cell-count mismatch");
        for (int c = 0; c < prod.Cells.Count; c++)
            Assert.True(prod.Cells[c].Control == refk.Cells[c].Control && prod.Cells[c].Content == refk.Cells[c].Content,
                $"case {caseId}: cell[{c}] mismatch");

        return (prod.Cells, prod.Ops);
    }

    /// <summary>Assert the four keyed-reorder invariants on the production
    /// output (independent ground truth, not just SIMD==scalar).</summary>
    private static void AssertInvariants(Model m, List<Cell> cells)
    {
        Assert.Equal(m.PrefixLen + m.NewMidLen + m.SuffixLen, cells.Count);
        for (int k = 0; k < m.PrefixLen; k++) Assert.Equal(PrefixBase + k, cells[k].Control);
        for (int k = 0; k < m.SuffixLen; k++) Assert.Equal(SuffixBase + k, cells[m.PrefixLen + m.NewMidLen + k].Control);
        for (int j = 0; j < m.NewMidLen; j++)
        {
            var cell = cells[m.PrefixLen + j];
            int expected = m.NewToOld[j] >= 0 ? m.NewToOld[j] : NewBase + j;
            Assert.Equal(expected, cell.Control);
            Assert.Equal(j, cell.Content);
        }
    }

    // Deterministic port of StressPerf.KeyedList.KeyedListSource (the regressing
    // workload): N stable keys, each tick churns (k/4 insert+remove) + moves.
    private sealed class KeyedListSim
    {
        private readonly List<int> _items = new();
        private readonly Random _rng;
        private int _nextId;
        public KeyedListSim(int count, int seed)
        {
            _rng = new Random(seed);
            for (int i = 0; i < count; i++) _items.Add(i);
            _nextId = count;
        }
        public int[] Snapshot() => _items.ToArray();
        public void Update(double percent)
        {
            int n = _items.Count;
            int k = (int)Math.Round(n * percent / 100.0, MidpointRounding.AwayFromZero);
            if (k < 0) k = 0; if (k > n) k = n;
            if (k == 0) return;
            int churn = k / 4;
            for (int i = 0; i < churn; i++)
            {
                _items.RemoveAt(_rng.Next(_items.Count));
                _items.Insert(_rng.Next(_items.Count + 1), _nextId++);
            }
            int moves = k - churn;
            for (int i = 0; i < moves; i++)
            {
                int from = _rng.Next(_items.Count);
                int row = _items[from];
                _items.RemoveAt(from);
                _items.Insert(_rng.Next(_items.Count + 1), row);
            }
        }
    }

    [Fact]
    public void Simd_Matches_Scalar_Over_Random_Workloads()
    {
        int cases = 0;
        for (int seed = 1; seed <= 5000; seed++)
        {
            var rng = new Random(seed);
            int n = 1 + rng.Next(80);
            double pct = rng.Next(0, 60);
            var sim = new KeyedListSim(n, seed);
            int[] oldKeys = sim.Snapshot();
            sim.Update(pct);
            int[] newKeys = sim.Snapshot();

            var model = BuildModel(oldKeys, newKeys);
            var (cells, _) = RunBothAndCompare(model, seed);
            AssertInvariants(model, cells);
            cases++;
        }
        Assert.Equal(5000, cases);
    }

    [Theory]
    [InlineData(500, 10.0, 200)]
    [InlineData(500, 25.0, 150)]
    [InlineData(500, 5.0, 200)]
    [InlineData(2000, 10.0, 60)]
    public void Simd_Matches_Scalar_Over_SteadyState_Ticks(int n, double pct, int ticks)
    {
        var sim = new KeyedListSim(n, 42);
        int[] prev = sim.Snapshot();
        for (int t = 0; t < ticks; t++)
        {
            sim.Update(pct);
            int[] cur = sim.Snapshot();
            var model = BuildModel(prev, cur);
            var (cells, _) = RunBothAndCompare(model, t);
            AssertInvariants(model, cells);
            prev = cur;
        }
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(4096)]   // exercises wide vector spans + remainder tails
    [InlineData(4099)]
    public void LargeN_Churn_Sequence_Stays_Exact(int n)
    {
        // A churn sequence at large N — the live-index oracle must stay exact
        // through many overlapping shifts (the guard the buggy O(1)-update
        // original, and any SIMD tail/range bug, would fail).
        var sim = new KeyedListSim(n, 7);
        int[] prev = sim.Snapshot();
        for (int t = 0; t < 40; t++)
        {
            sim.Update(12.0);
            int[] cur = sim.Snapshot();
            var model = BuildModel(prev, cur);
            var (cells, _) = RunBothAndCompare(model, t);
            AssertInvariants(model, cells);
            prev = cur;
        }
    }

    [Theory]
    [InlineData("a,b,c,d", "b,c,d,a")]   // rotation (the C1 repro)
    [InlineData("a,b,c,d,e", "e,d,c,b,a")] // full reverse
    [InlineData("a,b,c,d,e,f,g,h,i,j", "j,a,i,b,h,c,g,d,f,e")] // interleave
    public void Simd_Matches_Scalar_Explicit(string oldCsv, string newCsv)
    {
        int[] Parse(string s)
        {
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = parts[i][0]; // char code as key
            return r;
        }
        var model = BuildModel(Parse(oldCsv), Parse(newCsv));
        var (cells, _) = RunBothAndCompare(model, 0);
        AssertInvariants(model, cells);
    }

    [Fact]
    public void KeyedMiddleCore_Is_Allocation_Free()
    {
        // A representative N=500/pct=10 steady-state tick.
        var sim = new KeyedListSim(500, 42);
        int[] prev = sim.Snapshot();
        for (int i = 0; i < 6; i++) { sim.Update(10.0); prev = sim.Snapshot(); }
        sim.Update(10.0);
        var model = BuildModel(prev, sim.Snapshot());

        var panel = new int[model.OldMidLen];
        long sink = 0;

        // Warm up JIT/tiering so the measured window is steady-state.
        for (int i = 0; i < 64; i++)
        {
            Array.Copy(model.OldRelToPanelBase, panel, model.OldMidLen);
            var s = new NoOpSink();
            ChildReconciler.RunKeyedMiddleCore(ref s, model.NewToOld, model.InLis, model.NewMidLen, model.OldMidLen, model.InitialAnchor, panel);
            sink += s.Moves + s.Mounts + s.Patches; // long += int: no boxing
        }

        const int iters = 500;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iters; i++)
        {
            Array.Copy(model.OldRelToPanelBase, panel, model.OldMidLen);
            var s = new NoOpSink();
            ChildReconciler.RunKeyedMiddleCore(ref s, model.NewToOld, model.InLis, model.NewMidLen, model.OldMidLen, model.InitialAnchor, panel);
            sink += s.Moves + s.Mounts + s.Patches;
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(sink >= 0); // observe `sink` so the loop body can't be elided
        Assert.True(after - before == 0,
            $"keyed-middle core allocated {after - before} bytes over {iters} iters (expected 0 — Vector<int> is a stack value type, shifts run on the caller's pooled buffer).");
    }
}
