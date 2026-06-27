using System.Collections.Specialized;
using Microsoft.UI.Reactor.Core.Internal;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Internal;

/// <summary>
/// Regression coverage for the per-diff allocation-elimination work
/// (issue #653): the keyed list diff now rents its working buffers from
/// <see cref="System.Buffers.ArrayPool{T}"/> and reuses <see cref="ReactorListState.Scratch"/>
/// for the duplicate scan, and the no-op / empty fast paths now run ABOVE the
/// duplicate check. These tests assert that pooling never corrupts results —
/// across sequential diffs that share the pool, across interleaved diffs on
/// independent states, and in the rented-buffer-larger-than-count case — and
/// that correctness (which row moves/inserts/removes) is preserved exactly.
/// </summary>
public class KeyedListDiffPoolingTests
{
    private sealed record Item(string Id);

    private static string Key(Item i, int _) => i.Id;

    private static ReactorListState Seed(params string[] keys)
    {
        var s = new ReactorListState();
        var seed = new (int Index, string Key)[keys.Length];
        for (int i = 0; i < keys.Length; i++) seed[i] = (i, keys[i]);
        s.Reset(seed);
        return s;
    }

    private static Item[] Items(params string[] keys)
    {
        var arr = new Item[keys.Length];
        for (int i = 0; i < keys.Length; i++) arr[i] = new Item(keys[i]);
        return arr;
    }

    private static void AssertKeysMatch(ReactorListState s, params string[] expected)
    {
        Assert.Equal(expected.Length, s.Source.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], s.Source[i].Key);
            Assert.Equal(i, s.Source[i].Index);
        }
        Assert.Equal(expected, s.LastKeys);
        Assert.Equal(expected.Length, s.ByKey.Count);
    }

    // ────────────────────────────────────────────────────────────────────
    //  No-op fast path now runs above the duplicate scan (#1)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoOp_FastPath_With_Many_Unique_Keys_Emits_Nothing()
    {
        // 8 unique keys: previously this allocated/scanned a duplicate HashSet
        // BEFORE the no-op check. The reorder (#1) must keep it a pure no-op.
        var keys = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };
        var s = Seed(keys);
        var events = new List<NotifyCollectionChangedEventArgs>();
        s.Source.CollectionChanged += (_, e) => events.Add(e);

        var stats = KeyedListDiff.Apply(s, Items(keys), Key);

        Assert.False(stats.AnyOps);
        Assert.False(stats.Bailout);
        Assert.Empty(events);
        AssertKeysMatch(s, keys);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Rented newKeys buffer may be larger than newCount (#2)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Large_Then_Small_Diff_Preserves_Correctness_Across_Buffer_Sizes()
    {
        // Run diffs whose newCount swings widely so the rented newKeys buffer
        // is reused at very different sizes. If any code path read
        // newKeys.Length instead of the threaded newCount, the stale/cleared
        // tail of a reused (larger) buffer would corrupt the result. The
        // 64→3 collapse legitimately trips the churn bailout; correctness
        // (final key order) must hold either way, which is what we assert.
        var s = Seed();

        var big = new string[64];
        for (int i = 0; i < big.Length; i++) big[i] = $"k{i}";
        KeyedListDiff.Apply(s, Items(big), Key);
        AssertKeysMatch(s, big);

        // Collapse to 3 items — small newCount, large previously-rented buffer.
        KeyedListDiff.Apply(s, Items("k0", "k1", "k2"), Key);
        AssertKeysMatch(s, "k0", "k1", "k2");

        // Grow again (general path) to confirm LastKeys sync stayed correct.
        var stats = KeyedListDiff.Apply(s, Items("k0", "z", "k1", "k2", "w"), Key);
        Assert.False(stats.Bailout);
        AssertKeysMatch(s, "k0", "z", "k1", "k2", "w");
    }

    // ────────────────────────────────────────────────────────────────────
    //  Pooled `doomed` buffer for removals (#3) — non-contiguous removes
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Multiple_NonContiguous_Removes_From_Middle_Are_Correct()
    {
        // [a,b,c,d,e] → [a,c,e]: prefix 'a', suffix 'e', remove b and d (non
        // contiguous indices 1 and 3). Exercises the rented doomed[] sorted
        // descending so RemoveAt keeps earlier indices stable.
        var s = Seed("a", "b", "c", "d", "e");
        var rowA = s.ByKey["a"];
        var rowC = s.ByKey["c"];
        var rowE = s.ByKey["e"];

        var stats = KeyedListDiff.Apply(s, Items("a", "c", "e"), Key);

        Assert.False(stats.Bailout);
        Assert.Equal(0, stats.Inserts);
        Assert.Equal(2, stats.Removes);
        AssertKeysMatch(s, "a", "c", "e");
        // Survivors keep identity (proves we removed the right rows).
        Assert.Same(rowA, s.Source[0]);
        Assert.Same(rowC, s.Source[1]);
        Assert.Same(rowE, s.Source[2]);
        Assert.False(s.ByKey.ContainsKey("b"));
        Assert.False(s.ByKey.ContainsKey("d"));
    }

    [Fact]
    public void Many_Removes_Below_Floor_Use_Pooled_Doomed_Buffer()
    {
        // [a,b,c,d,e,f,g] → [a,g]: prefix 'a', suffix 'g', 5 middle removes.
        // churn = 5 < floor(8) so the general path runs (no bailout), driving
        // the rented doomed buffer with 5 entries.
        var s = Seed("a", "b", "c", "d", "e", "f", "g");
        var stats = KeyedListDiff.Apply(s, Items("a", "g"), Key);

        Assert.False(stats.Bailout);
        Assert.Equal(5, stats.Removes);
        AssertKeysMatch(s, "a", "g");
    }

    // ────────────────────────────────────────────────────────────────────
    //  Churn bailout (#34) still correct with a non-empty prefix AND suffix
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Churn_Bailout_Counts_FullRange_Even_With_Shared_Prefix_And_Suffix()
    {
        // Stable 5-key prefix + stable 5-key suffix, middle 10 fully replaced.
        // Diff-range churn (10 add + 10 remove = 20) must equal full-range
        // churn so the >25% + floor(8) bailout still triggers.
        var old = new List<string>();
        var @new = new List<string>();
        for (int i = 0; i < 5; i++) { old.Add($"p{i}"); @new.Add($"p{i}"); }      // prefix
        for (int i = 0; i < 10; i++) { old.Add($"m{i}"); @new.Add($"n{i}"); }     // churned middle
        for (int i = 0; i < 5; i++) { old.Add($"s{i}"); @new.Add($"s{i}"); }      // suffix

        var s = Seed(old.ToArray());
        var stats = KeyedListDiff.Apply(s, Items(@new.ToArray()), Key);

        Assert.True(stats.Bailout);
        // Reset still produced a coherent collection in the new order.
        AssertKeysMatch(s, @new.ToArray());
    }

    [Fact]
    public void Stable_Prefix_Suffix_With_Small_Middle_Churn_Does_Not_Bail()
    {
        // Same shape but only a 1-key middle change — well under the floor, so
        // the general diff runs and the merged churn calc must NOT over-count.
        var old = new List<string>();
        var @new = new List<string>();
        for (int i = 0; i < 5; i++) { old.Add($"p{i}"); @new.Add($"p{i}"); }
        old.Add("mid"); @new.Add("MID");
        for (int i = 0; i < 5; i++) { old.Add($"s{i}"); @new.Add($"s{i}"); }

        var s = Seed(old.ToArray());
        var stats = KeyedListDiff.Apply(s, Items(@new.ToArray()), Key);

        Assert.False(stats.Bailout);
        AssertKeysMatch(s, @new.ToArray());
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scratch reuse for the duplicate scan (#11) must not leak (clean diff
    //  after a duplicate bailout on the SAME state)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_Bailout_Then_Reset_Through_Empty_Recovers_Cleanly()
    {
        // A duplicate-key diff bails out via Reset, which (by design — see
        // ReactorListState.Reset) tolerates the duplicate and leaves LastKeys
        // holding it. The supported recovery is a structural reset; here we go
        // through the empty list (Fast path 4) and then re-mount a clean unique
        // list. This proves the duplicate scan's reuse of state.Scratch (#11)
        // left no residue that survives a bailout + rebuild.
        var s = Seed("a", "b", "c");
        var dup = KeyedListDiff.Apply(s, Items("a", "b", "c", "d", "a"), Key);
        Assert.True(dup.Bailout);

        KeyedListDiff.Apply(s, Items(), Key); // clear to empty (Fast path 4)

        var clean = KeyedListDiff.Apply(s, Items("a", "b", "c", "d", "e"), Key);
        Assert.False(clean.Bailout);
        AssertKeysMatch(s, "a", "b", "c", "d", "e");
    }

    [Fact]
    public void Duplicate_In_FourPlus_Keys_Still_Detected_Via_Scratch()
    {
        // 4+ keys takes the Scratch-backed dup path (not the <=3 inline scan).
        var s = Seed("a", "b", "c", "d");
        var stats = KeyedListDiff.Apply(s, Items("a", "b", "c", "d", "b"), Key);
        Assert.True(stats.Bailout);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Pool sharing: interleaved diffs on independent states
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Interleaved_Diffs_On_Independent_States_Do_Not_Cross_Contaminate()
    {
        var s1 = Seed("a", "b", "c", "d", "e");
        var s2 = Seed("v", "w", "x", "y", "z");

        for (int round = 0; round < 25; round++)
        {
            // s1: rotate left by one each round.
            var k1 = new[] { "b", "c", "d", "e", "a" };
            KeyedListDiff.Apply(s1, Items(k1), Key);
            AssertKeysMatch(s1, k1);

            // s2: reverse each round (toggles back and forth).
            var k2 = new[] { "z", "y", "x", "w", "v" };
            KeyedListDiff.Apply(s2, Items(k2), Key);
            AssertKeysMatch(s2, k2);

            // restore both so the next round starts from the seed order.
            KeyedListDiff.Apply(s1, Items("a", "b", "c", "d", "e"), Key);
            KeyedListDiff.Apply(s2, Items("v", "w", "x", "y", "z"), Key);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Randomized stress — the pooled diff must equal a trivial oracle
    //  (Source order == applied key list) on every step, with survivor
    //  identity preserved for keys that persist across a step.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Randomized_Sequence_Of_Diffs_Always_Matches_Oracle()
    {
        var rng = new Random(20240613);
        var universe = new string[40];
        for (int i = 0; i < universe.Length; i++) universe[i] = $"u{i}";

        var s = Seed();
        var current = new List<string>();

        for (int step = 0; step < 400; step++)
        {
            // Build a random subset of the universe in a random order. Always
            // unique keys (no dup/null) so we stay on the structural path.
            int take = rng.Next(0, universe.Length + 1);
            var pool = new List<string>(universe);
            var next = new List<string>(take);
            for (int i = 0; i < take; i++)
            {
                int idx = rng.Next(pool.Count);
                next.Add(pool[idx]);
                pool.RemoveAt(idx);
            }

            // Capture survivor rows (keys present before AND after) to assert
            // identity stability through the diff.
            var survivorsBefore = new Dictionary<string, ReactorRow>();
            foreach (var k in next)
                if (s.ByKey.TryGetValue(k, out var row)) survivorsBefore[k] = row;

            var stats = KeyedListDiff.Apply(s, Items(next.ToArray()), Key);

            // Oracle: the Source must always equal exactly the applied list.
            AssertKeysMatch(s, next.ToArray());

            // When the diff did NOT bail, survivor ReactorRow identity must be
            // preserved (WinUI relies on this). A bailout (Reset) legitimately
            // rebuilds rows, so only check identity on the non-bailout path.
            if (!stats.Bailout)
            {
                foreach (var (k, beforeRow) in survivorsBefore)
                    Assert.Same(beforeRow, s.ByKey[k]);
            }

            current = next;
        }

        // Final state is internally consistent.
        AssertKeysMatch(s, current.ToArray());
    }

    // ────────────────────────────────────────────────────────────────────
    //  Allocation budgets (#1/#2/#11 pooling regression guards — M2)
    //
    //  Mirrors the GC.GetAllocatedBytesForCurrentThread warm-up + measured
    //  pattern used by the other perf-budget tests (#665/#692, DockPerfBudget).
    //  These FAIL if the per-diff pooling is reverted: the no-op path re-grows a
    //  `new string[newCount]` (#2) and a HasDuplicates HashSet that now runs
    //  ABOVE the fast path (#1); the general path re-grows the same, scaled by N.
    // ────────────────────────────────────────────────────────────────────

    private static readonly Func<Item, int, string> KeyFn = Key;

    [Fact]
    public void NoOp_SteadyState_Diff_Allocates_Nothing()
    {
        // The steady-state grid frame: keys never change. With the no-op fast
        // path ABOVE the duplicate scan (#1) and the rented newKeys buffer (#2),
        // an unchanged diff allocates ZERO heap bytes. The Item[] and key
        // delegate are built ONCE so the loop measures only the diff itself.
        var keys = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };
        var s = Seed(keys);
        var items = Items(keys);

        for (int i = 0; i < 200; i++) // warm: JIT + pool fill + Scratch growth
        {
            var st = KeyedListDiff.Apply(s, items, KeyFn);
            Assert.False(st.AnyOps);
        }

        const int iterations = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            _ = KeyedListDiff.Apply(s, items, KeyFn);
        var delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // Pooled no-op is 0 B/iter. Allow a tiny constant for CI/JIT noise; a
        // single reverted `new string[8]` (~88 B) or HasDuplicates HashSet
        // (hundreds of B) per call would blow far past this.
        Assert.True(delta < 8L * iterations,
            $"steady-state no-op diff allocated {delta} B over {iterations} iters " +
            $"({(double)delta / iterations:F2} B/iter); expected ~0 — pooling (#1/#2) likely reverted");
    }

    [Fact]
    public void General_Keyed_Reorder_Diff_Stays_Within_Pooled_Budget()
    {
        // A real general-path diff over a large list (128 keys) reordered by a
        // single adjacent swap each way. The pooled newKeys[128] (#2) and
        // Scratch-backed duplicate scan (#11) keep per-diff allocation tiny
        // (just the inherent ObservableCollection move event + movedRows list).
        // Reverting pooling re-adds a `new string[128]` + a 128-entry HashSet
        // per diff (several KB), which this budget catches.
        const int n = 128;
        var a = new string[n];
        for (int i = 0; i < n; i++) a[i] = $"k{i}";
        var b = (string[])a.Clone();
        (b[0], b[1]) = (b[1], b[0]); // adjacent swap → one move

        var s = Seed(a);
        var itemsA = Items(a);
        var itemsB = Items(b);

        for (int i = 0; i < 100; i++) // warm both directions
        {
            KeyedListDiff.Apply(s, itemsB, KeyFn);
            KeyedListDiff.Apply(s, itemsA, KeyFn);
        }

        const int pairs = 2_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < pairs; i++)
        {
            KeyedListDiff.Apply(s, itemsB, KeyFn);
            KeyedListDiff.Apply(s, itemsA, KeyFn);
        }
        var delta = GC.GetAllocatedBytesForCurrentThread() - before;
        long perDiff = delta / (2L * pairs);

        // Generous ceiling vs. the multi-KB-per-diff cost of reverted pooling,
        // but far below it, so an un-pooled regression at N=128 fails loudly.
        // Measured steady state is ~72 B/diff (a single ObservableCollection
        // move event). A 256 B ceiling keeps ~3.5x headroom for runtime variance
        // while a reverted `new string[128]` (~1 KB) + 128-entry HashSet (several
        // KB) fails by a wide margin.
        Assert.True(perDiff < 256,
            $"general keyed reorder allocated {perDiff} B/diff (N={n}); pooled buffers " +
            $"(#2 newKeys, #11 Scratch dup-scan) keep this small — likely reverted");
    }
}
