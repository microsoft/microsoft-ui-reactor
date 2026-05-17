using System.Collections.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Reactor.Core.Internal;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Internal;

/// <summary>
/// Exhaustive coverage of <see cref="KeyedListDiff.Apply"/> per spec 042
/// §4.3 — single insert/remove/move, prefix/suffix lockstep, reverse,
/// shuffle, duplicate-key + null-key bailout, churn-threshold bailout,
/// idempotency, and the survivor-identity invariant WinUI relies on.
/// </summary>
public class KeyedListDiffTests
{
    // ────────────────────────────────────────────────────────────────────
    //  Helpers — capture the OC's CollectionChanged stream and prepare
    //  state from a string array so test cases stay readable.
    // ────────────────────────────────────────────────────────────────────

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

    private sealed class Recorder
    {
        public readonly List<NotifyCollectionChangedEventArgs> Events = new();
        public Recorder(ReactorListState state) =>
            state.Source.CollectionChanged += (_, e) => Events.Add(e);
        public int Count(NotifyCollectionChangedAction action) =>
            Events.Count(e => e.Action == action);
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
        // ByKey count never exceeds Source count (duplicates would have
        // bailed out before this method is called).
        Assert.Equal(expected.Length, s.ByKey.Count);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Empty / no-op shapes
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Empty_To_Empty_NoOp()
    {
        var s = Seed();
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items(), Key);

        Assert.False(stats.AnyOps);
        Assert.False(stats.Bailout);
        Assert.Empty(rec.Events);
    }

    [Fact]
    public void Apply_Identical_NoOp_No_CollectionChanged_Events()
    {
        var s = Seed("a", "b", "c");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("a", "b", "c"), Key);

        Assert.False(stats.AnyOps);
        Assert.Empty(rec.Events);
        AssertKeysMatch(s, "a", "b", "c");
    }

    [Fact]
    public void Apply_Idempotency_Second_Apply_Is_NoOp()
    {
        var s = Seed("a", "b");
        KeyedListDiff.Apply(s, Items("x", "a", "b", "y"), Key); // arbitrary diff

        var rec = new Recorder(s);
        var stats = KeyedListDiff.Apply(s, Items("x", "a", "b", "y"), Key);

        Assert.False(stats.AnyOps);
        Assert.Empty(rec.Events);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Empty ↔ non-empty
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Empty_To_NonEmpty_Emits_Inserts()
    {
        var s = Seed();
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("a", "b", "c"), Key);

        Assert.Equal(3, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.Equal(0, stats.Moves);
        Assert.Equal(3, rec.Count(NotifyCollectionChangedAction.Add));
        AssertKeysMatch(s, "a", "b", "c");
    }

    [Fact]
    public void Apply_NonEmpty_To_Empty_Emits_Removes()
    {
        var s = Seed("a", "b", "c");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items(), Key);

        Assert.Equal(0, stats.Inserts);
        Assert.Equal(3, stats.Removes);
        Assert.Equal(3, rec.Count(NotifyCollectionChangedAction.Remove));
        Assert.Empty(s.Source);
        Assert.Empty(s.ByKey);
        Assert.Empty(s.LastKeys);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Single-op fast paths
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Single_Append_Emits_One_Add()
    {
        var s = Seed("a", "b", "c");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("a", "b", "c", "d"), Key);

        Assert.Equal(1, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.Equal(0, stats.Moves);
        Assert.Equal(1, rec.Count(NotifyCollectionChangedAction.Add));
        AssertKeysMatch(s, "a", "b", "c", "d");
    }

    [Fact]
    public void Apply_Single_Prepend_Emits_One_Add()
    {
        var s = Seed("a", "b", "c");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("z", "a", "b", "c"), Key);

        Assert.Equal(1, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.Equal(0, stats.Moves);
        Assert.Equal(1, rec.Count(NotifyCollectionChangedAction.Add));
        AssertKeysMatch(s, "z", "a", "b", "c");
    }

    [Fact]
    public void Apply_Insert_In_Middle()
    {
        var s = Seed("a", "b", "d", "e");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("a", "b", "c", "d", "e"), Key);

        Assert.Equal(1, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.Equal(0, stats.Moves);
        Assert.Equal(1, rec.Count(NotifyCollectionChangedAction.Add));
        AssertKeysMatch(s, "a", "b", "c", "d", "e");
    }

    [Fact]
    public void Apply_Remove_From_Start()
    {
        var s = Seed("a", "b", "c");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("b", "c"), Key);

        Assert.Equal(0, stats.Inserts);
        Assert.Equal(1, stats.Removes);
        Assert.Equal(0, stats.Moves);
        Assert.Equal(1, rec.Count(NotifyCollectionChangedAction.Remove));
        AssertKeysMatch(s, "b", "c");
    }

    [Fact]
    public void Apply_Remove_From_Middle()
    {
        var s = Seed("a", "b", "c", "d");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("a", "b", "d"), Key);

        Assert.Equal(0, stats.Inserts);
        Assert.Equal(1, stats.Removes);
        Assert.Equal(0, stats.Moves);
        Assert.Equal(1, rec.Count(NotifyCollectionChangedAction.Remove));
        AssertKeysMatch(s, "a", "b", "d");
    }

    [Fact]
    public void Apply_Remove_From_End()
    {
        var s = Seed("a", "b", "c");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("a", "b"), Key);

        Assert.Equal(0, stats.Inserts);
        Assert.Equal(1, stats.Removes);
        Assert.Equal(0, stats.Moves);
        Assert.Equal(1, rec.Count(NotifyCollectionChangedAction.Remove));
        AssertKeysMatch(s, "a", "b");
    }

    // ────────────────────────────────────────────────────────────────────
    //  Moves
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Single_Move_Up_By_One()
    {
        // [a,b,c,d] → [a,c,b,d] — move c from index 2 to index 1
        var s = Seed("a", "b", "c", "d");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("a", "c", "b", "d"), Key);

        Assert.Equal(0, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.Equal(1, stats.Moves);
        Assert.Equal(1, rec.Count(NotifyCollectionChangedAction.Move));
        AssertKeysMatch(s, "a", "c", "b", "d");
    }

    [Fact]
    public void Apply_Move_Long_Distance()
    {
        // [a,b,c,d,e] → [e,a,b,c,d] — move e from index 4 to index 0
        var s = Seed("a", "b", "c", "d", "e");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("e", "a", "b", "c", "d"), Key);

        // The diff might emit this as a single Move or multiple — the
        // important contract is that no inserts/removes happen and the
        // final order is correct.
        Assert.Equal(0, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.True(stats.Moves >= 1);
        AssertKeysMatch(s, "e", "a", "b", "c", "d");
    }

    [Fact]
    public void Apply_Reverse_Five_Items_Yields_Multiple_Moves_Not_Removes()
    {
        // [a,b,c,d,e] → [e,d,c,b,a]
        var s = Seed("a", "b", "c", "d", "e");
        var rec = new Recorder(s);

        var stats = KeyedListDiff.Apply(s, Items("e", "d", "c", "b", "a"), Key);

        // Reverse fails the >25% bulk-replace check (churn is 0 since all
        // keys are retained — they just changed order), so the general
        // algorithm runs and emits Moves, not Inserts/Removes.
        Assert.Equal(0, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.True(stats.Moves >= 1);
        Assert.Equal(0, rec.Count(NotifyCollectionChangedAction.Add));
        Assert.Equal(0, rec.Count(NotifyCollectionChangedAction.Remove));
        AssertKeysMatch(s, "e", "d", "c", "b", "a");
    }

    [Fact]
    public void Apply_Shuffle_Preserves_All_Survivors_And_Order_Is_Correct()
    {
        var s = Seed("a", "b", "c", "d", "e", "f", "g", "h");
        var stats = KeyedListDiff.Apply(s, Items("c", "f", "a", "h", "b", "g", "d", "e"), Key);

        Assert.Equal(0, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.Equal(8, stats.Survivors);
        AssertKeysMatch(s, "c", "f", "a", "h", "b", "g", "d", "e");
    }

    // ────────────────────────────────────────────────────────────────────
    //  Survivor identity — WinUI relies on object-identity stability
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Survivor_ReactorRow_Identity_Is_Preserved_Across_Diffs()
    {
        var s = Seed("a", "b", "c");
        var rowA = s.ByKey["a"];
        var rowB = s.ByKey["b"];
        var rowC = s.ByKey["c"];

        KeyedListDiff.Apply(s, Items("z", "b", "a", "c"), Key);

        // a, b, c all survived — their ReactorRow refs must be the same
        // instances. This is the contract WinUI uses to recognize "this
        // item moved" from the CollectionChanged stream.
        Assert.Same(rowB, s.Source[1]);
        Assert.Same(rowA, s.Source[2]);
        Assert.Same(rowC, s.Source[3]);
    }

    [Fact]
    public void Survivor_ReactorRow_Identity_Survives_Insert_Plus_Remove()
    {
        var s = Seed("a", "b", "c", "d");
        var rowB = s.ByKey["b"];
        var rowC = s.ByKey["c"];

        KeyedListDiff.Apply(s, Items("a", "b", "c", "e"), Key); // remove d, add e

        Assert.Same(rowB, s.ByKey["b"]);
        Assert.Same(rowC, s.ByKey["c"]);
        Assert.True(s.ByKey.ContainsKey("e"));
        Assert.False(s.ByKey.ContainsKey("d"));
    }

    // ────────────────────────────────────────────────────────────────────
    //  Bailout — duplicate keys, null keys, >25% churn
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Duplicate_Keys_Triggers_Bailout()
    {
        var s = Seed("a", "b", "c");
        var logger = new TestLogger();

        var stats = KeyedListDiff.Apply(
            s,
            Items("a", "b", "c", "a"), // "a" duplicated
            Key,
            logger,
            "TestList");

        Assert.True(stats.Bailout);
        Assert.Contains("duplicate", string.Join('\n', logger.Messages));
        // Reset still produced a coherent Source — Source.Count matches
        // the user input (4 rows, even with the duplicate).
        Assert.Equal(4, s.Source.Count);
    }

    [Fact]
    public void Apply_Null_Key_Triggers_Bailout()
    {
        var s = Seed("a", "b");
        var logger = new TestLogger();

        var stats = KeyedListDiff.Apply(
            s,
            Items("a", "b"),
            (item, _) => item.Id == "b" ? null! : item.Id, // forced null
            logger,
            "TestList");

        Assert.True(stats.Bailout);
        Assert.Contains("null key", string.Join('\n', logger.Messages));
    }

    [Fact]
    public void Apply_Churn_Over_25_Percent_Triggers_Bailout_When_Above_Floor()
    {
        // 20 items, replace 6 → churn = 6 inserts + 6 removes = 12.
        // oldCount = 20, 12 * 4 = 48 > 20. 12 ≥ floor (8). Bailout.
        var oldKeys = new string[20];
        var newKeys = new string[20];
        for (int i = 0; i < 20; i++)
        {
            oldKeys[i] = $"k{i}";
            newKeys[i] = i < 6 ? $"new{i}" : $"k{i}";
        }
        var s = Seed(oldKeys);
        var logger = new TestLogger();

        var stats = KeyedListDiff.Apply(s, Items(newKeys), Key, logger, "TestList");

        Assert.True(stats.Bailout);
    }

    [Fact]
    public void Apply_Small_List_With_Heavy_Churn_Does_Not_Bail()
    {
        // 4-item list, 3 of 4 replaced. Percentage-wise that's 75% churn,
        // but the absolute number of churned ops (6) is below the 8-op
        // floor — for a list this small, running the general diff is just
        // as cheap as a Reset and preserves animation on the one survivor.
        var s = Seed("a", "b", "c", "d");
        var stats = KeyedListDiff.Apply(s, Items("a", "x", "y", "z"), Key);

        Assert.False(stats.Bailout);
        AssertKeysMatch(s, "a", "x", "y", "z");
    }

    [Fact]
    public void Apply_Bulk_Replace_Over_Floor_Triggers_Bailout()
    {
        // 100 items, replace 30 → churn = 60. 60 ≥ 8 floor and 60*4 > 100.
        var oldKeys = new string[100];
        var newKeys = new string[100];
        for (int i = 0; i < 100; i++)
        {
            oldKeys[i] = $"k{i}";
            newKeys[i] = i < 30 ? $"new{i}" : $"k{i}";
        }
        var s = Seed(oldKeys);
        var stats = KeyedListDiff.Apply(s, Items(newKeys), Key);

        Assert.True(stats.Bailout);
    }

    [Fact]
    public void Apply_Twenty_Percent_Change_Does_Not_Bail()
    {
        // 100 items, change 10 → 10% churn (10 inserts, 10 removes = 20 total).
        // additions=10, removals=10, churn=20, 20*4 = 80 ≤ 100. No bail.
        var oldKeys = new string[100];
        var newKeys = new string[100];
        for (int i = 0; i < 100; i++)
        {
            oldKeys[i] = $"k{i}";
            newKeys[i] = i < 10 ? $"new{i}" : $"k{i}";
        }
        var s = Seed(oldKeys);
        var stats = KeyedListDiff.Apply(s, Items(newKeys), Key);

        Assert.False(stats.Bailout);
    }

    // ────────────────────────────────────────────────────────────────────
    //  LastKeys / ByKey invariants stay in sync after every shape
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { "a", "b", "c" }, new[] { "c", "b", "a" })]
    [InlineData(new[] { "x" }, new[] { "x", "y", "z" })]
    [InlineData(new[] { "a", "b", "c", "d", "e" }, new[] { "a", "c", "e" })]
    [InlineData(new string[] { }, new[] { "a", "b" })]
    [InlineData(new[] { "a", "b" }, new string[] { })]
    public void Apply_Maintains_LastKeys_Equals_Source_Equals_ByKey(string[] oldKeys, string[] newKeys)
    {
        var s = Seed(oldKeys);
        KeyedListDiff.Apply(s, Items(newKeys), Key);

        // LastKeys exactly mirrors Source in visible order.
        Assert.Equal(s.Source.Count, s.LastKeys.Count);
        for (int i = 0; i < s.Source.Count; i++)
            Assert.Equal(s.Source[i].Key, s.LastKeys[i]);

        // ByKey count never exceeds Source count.
        Assert.Equal(s.Source.Count, s.ByKey.Count);

        // ReactorRow.Index agrees with position.
        for (int i = 0; i < s.Source.Count; i++)
            Assert.Equal(i, s.Source[i].Index);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Test logger for diagnostic surfacing
    // ────────────────────────────────────────────────────────────────────

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NullScope();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, global::System.Exception? exception, Func<TState, global::System.Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
        private sealed class NullScope : IDisposable { public void Dispose() { } }
    }
}
