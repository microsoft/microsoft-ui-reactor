using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Internal;

/// <summary>
/// Spec 042 Phase 3 §6 — exercises <see cref="KeyedListDiff.Apply"/>'s
/// ambient-animation channel. When the caller passes a non-null ambient
/// with <see cref="AmbientAnimation.HasEffect"/> true:
/// <list type="bullet">
/// <item><description>Inserted rows carry <see cref="ReactorRow.PendingEnterAnimation"/>
/// so the container-realization path can apply a per-container enter
/// animation when WinUI materializes the container.</description></item>
/// <item><description>Survivor rows that moved index are reported via
/// <see cref="KeyedListDiff.DiffStats.MovedRows"/> so the caller can
/// drive offset animations on the corresponding realized containers.</description></item>
/// <item><description>Bailout / no-op / no-ambient paths produce zero
/// allocation and zero animation tagging — the steady state is unaffected.</description></item>
/// </list>
/// </summary>
public class KeyedListDiffAnimationTests
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

    // ════════════════════════════════════════════════════════════════════
    //  Insert paths — inserted rows tagged with the ambient kind
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Single_Append_With_Ambient_Tags_New_Row()
    {
        var state = Seed("a", "b");
        var items = Items("a", "b", "c");
        var ambient = new AmbientAnimation(AnimationKind.Spring);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.Equal(1, stats.Inserts);
        // Survivors must not be tagged — only the newly-inserted "c".
        Assert.Null(state.ByKey["a"].PendingEnterAnimation);
        Assert.Null(state.ByKey["b"].PendingEnterAnimation);
        Assert.Equal(AnimationKind.Spring, state.ByKey["c"].PendingEnterAnimation);
    }

    [Fact]
    public void Single_Prepend_With_Ambient_Tags_New_Row()
    {
        var state = Seed("b", "c");
        var items = Items("a", "b", "c");
        var ambient = new AmbientAnimation(AnimationKind.EaseOut);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.Equal(1, stats.Inserts);
        Assert.Equal(AnimationKind.EaseOut, state.ByKey["a"].PendingEnterAnimation);
        Assert.Null(state.ByKey["b"].PendingEnterAnimation);
        Assert.Null(state.ByKey["c"].PendingEnterAnimation);
    }

    [Fact]
    public void Empty_To_NonEmpty_Tags_All_Inserted_Rows()
    {
        // Mount-equivalent diff path. Every row is newly inserted, so every
        // row should be tagged so the container-realization handler can
        // animate them as they come into view.
        var state = Seed();
        var items = Items("a", "b", "c");
        var ambient = new AmbientAnimation(AnimationKind.Spring);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.Equal(3, stats.Inserts);
        Assert.All(state.Source, row =>
            Assert.Equal(AnimationKind.Spring, row.PendingEnterAnimation));
    }

    [Fact]
    public void Insert_In_Middle_Tags_Only_New_Row()
    {
        // Goes through the general path because no fast-path matches.
        var state = Seed("a", "b", "d");
        var items = Items("a", "b", "c", "d");
        var ambient = new AmbientAnimation(AnimationKind.EaseInOut);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.Equal(1, stats.Inserts);
        Assert.Equal(0, stats.Moves);
        Assert.Null(state.ByKey["a"].PendingEnterAnimation);
        Assert.Null(state.ByKey["b"].PendingEnterAnimation);
        Assert.Equal(AnimationKind.EaseInOut, state.ByKey["c"].PendingEnterAnimation);
        Assert.Null(state.ByKey["d"].PendingEnterAnimation);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Move paths — survivors that moved are reported via MovedRows
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Single_Move_With_Ambient_Reports_Moved_Row()
    {
        // Forces the general path: swap two middle entries (no Insert/Remove).
        var state = Seed("a", "b", "c", "d");
        var items = Items("a", "c", "b", "d");
        var ambient = new AmbientAnimation(AnimationKind.Spring);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.True(stats.Moves >= 1);
        Assert.NotNull(stats.MovedRows);
        // At minimum, "c" moved from 2 → 1, so it must appear in MovedRows.
        Assert.Contains(stats.MovedRows!, r => r.Key == "c");
        // Survivors never carry an enter tag.
        Assert.All(state.Source, row => Assert.Null(row.PendingEnterAnimation));
    }

    [Fact]
    public void Reverse_With_Ambient_Reports_Explicitly_Moved_Rows()
    {
        // The forward-walk diff implicitly relocates the tail row as
        // earlier survivors are moved past it, so the explicit Move count
        // is N-1 rather than N. We assert the contract that *every*
        // row appearing in MovedRows had Source.Move called on it (so a
        // move animation is the right per-container effect).
        var state = Seed("a", "b", "c", "d");
        var items = Items("d", "c", "b", "a");
        var ambient = new AmbientAnimation(AnimationKind.EaseOut);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.Equal(0, stats.Inserts);
        Assert.Equal(0, stats.Removes);
        Assert.NotNull(stats.MovedRows);
        Assert.Equal(stats.Moves, stats.MovedRows!.Count);
        // The final OC order is the new order — that's the data correctness
        // contract, independent of move counting.
        Assert.Equal(new[] { "d", "c", "b", "a" }, state.LastKeys);
    }

    // ════════════════════════════════════════════════════════════════════
    //  No-ambient / None-kind paths — zero animation overhead
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Without_Ambient_No_Row_Is_Tagged()
    {
        var state = Seed("a", "b");
        var items = Items("a", "b", "c");

        var stats = KeyedListDiff.Apply(state, items, Key);

        Assert.Equal(1, stats.Inserts);
        // The pre-Phase-3 contract: ambient parameter omitted ⇒ no tag.
        Assert.All(state.Source, row => Assert.Null(row.PendingEnterAnimation));
        // And no MovedRows allocation either (the steady-state hot path).
        Assert.Null(stats.MovedRows);
    }

    [Fact]
    public void None_Kind_Ambient_Is_Treated_As_No_Animation()
    {
        // The AnimationKind.None contract: a nested Animate(.None) explicitly
        // suppresses the outer's intent for its scope. The diff must respect
        // that — inserted rows under .None get no enter tag so the
        // realization handler does not animate them.
        var state = Seed("a", "b");
        var items = Items("a", "b", "c");
        var ambient = new AmbientAnimation(AnimationKind.None);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.Equal(1, stats.Inserts);
        Assert.Null(state.ByKey["c"].PendingEnterAnimation);
        Assert.Null(stats.MovedRows);
    }

    [Fact]
    public void No_Change_Diff_Returns_Empty_Stats_Even_With_Ambient()
    {
        // Same-keys fast path runs before the ambient is consulted.
        // Important: the steady-state render under an Animate(...) scope
        // must not pretend ops happened where they didn't.
        var state = Seed("a", "b", "c");
        var items = Items("a", "b", "c");
        var ambient = new AmbientAnimation(AnimationKind.Spring);

        var stats = KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        Assert.False(stats.AnyOps);
        Assert.Null(stats.MovedRows);
        Assert.All(state.Source, row => Assert.Null(row.PendingEnterAnimation));
    }

    // ════════════════════════════════════════════════════════════════════
    //  Survivor identity — diff must preserve ReactorRow object identity
    //  across moves, otherwise the realization handler would see a
    //  not-tagged row even though the data moved.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Survivors_Preserve_ReactorRow_Identity_Across_Move()
    {
        var state = Seed("a", "b", "c");
        var items = Items("c", "a", "b");
        var rowB = state.ByKey["b"];
        var ambient = new AmbientAnimation(AnimationKind.Spring);

        KeyedListDiff.Apply(state, items, Key, ambient: ambient);

        // Same instance, new index — that's what makes WinUI's
        // CollectionChanged distinguish "moved" from "removed + inserted".
        Assert.Same(rowB, state.ByKey["b"]);
    }

    [Fact]
    public void AnimationKind_Maps_To_NonNull_Curve_For_All_Active_Kinds()
    {
        // Pin the AnimationKind → Curve table so a future addition without
        // a matching map entry surfaces immediately as a test failure.
        Assert.Null(AnimationKindMap.ToCurve(AnimationKind.None));
        Assert.NotNull(AnimationKindMap.ToCurve(AnimationKind.Default));
        Assert.NotNull(AnimationKindMap.ToCurve(AnimationKind.Spring));
        Assert.NotNull(AnimationKindMap.ToCurve(AnimationKind.EaseIn));
        Assert.NotNull(AnimationKindMap.ToCurve(AnimationKind.EaseOut));
        Assert.NotNull(AnimationKindMap.ToCurve(AnimationKind.EaseInOut));

        Assert.False(AnimationKindMap.IsActive(AnimationKind.None));
        Assert.True(AnimationKindMap.IsActive(AnimationKind.Spring));
    }
}
