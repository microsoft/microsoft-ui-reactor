using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 2 of spec 042: identity-on-data convention. Locks down that the
/// <see cref="IReactorKeyed"/>-defaulting factory overloads and the
/// <c>WithKey&lt;T, TKey&gt;</c> extension produce identity-projection
/// behavior identical to the explicit <c>KeySelector</c> / <c>WithKey(string)</c>
/// paths. The Phase 1 templated-list reconciler is fed through both shapes
/// so a regression in defaulting cannot silently change keyed-list diff
/// behavior.
/// </summary>
public class IReactorKeyedTests
{
    // ── Test data ───────────────────────────────────────────────────────

    /// <summary>POCO that owns its identity. Stable Key for the lifetime of the instance.</summary>
    private sealed record KeyedItem(string Key, string Name) : IReactorKeyed;

    private static IReadOnlyList<KeyedItem> Items(params (string Key, string Name)[] rows)
    {
        var arr = new KeyedItem[rows.Length];
        for (int i = 0; i < rows.Length; i++) arr[i] = new KeyedItem(rows[i].Key, rows[i].Name);
        return arr;
    }

    private static Element ViewBuilder(KeyedItem item, int _) => TextBlock(item.Name);

    // ════════════════════════════════════════════════════════════════════
    //  ListView<T> — defaulting overload
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ListView_IReactorKeyed_Defaults_KeySelector_To_DotKey()
    {
        var items = Items(("a", "A"), ("b", "B"), ("c", "C"));

        var defaulted = ListView(items, ViewBuilder);
        var explicitSel = ListView(items, static i => i.Key, ViewBuilder);

        // GetKeyAt is the single internal seam KeyedListDiff uses to project
        // identity for the templated list pipeline — pinning it here means
        // the diff cannot diverge between the two construction shapes.
        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(explicitSel.GetKeyAt_TestAccessor(i), defaulted.GetKeyAt_TestAccessor(i));
            Assert.Equal(items[i].Key, defaulted.GetKeyAt_TestAccessor(i));
        }
    }

    [Fact]
    public void GridView_IReactorKeyed_Defaults_KeySelector_To_DotKey()
    {
        var items = Items(("a", "A"), ("b", "B"));

        var defaulted = GridView(items, ViewBuilder);

        Assert.Equal("a", defaulted.GetKeyAt_TestAccessor(0));
        Assert.Equal("b", defaulted.GetKeyAt_TestAccessor(1));
    }

    [Fact]
    public void FlipView_IReactorKeyed_Defaults_KeySelector_To_DotKey()
    {
        var items = Items(("p1", "Page 1"), ("p2", "Page 2"));

        var defaulted = FlipView(items, ViewBuilder);

        Assert.Equal("p1", defaulted.GetKeyAt_TestAccessor(0));
        Assert.Equal("p2", defaulted.GetKeyAt_TestAccessor(1));
    }

    [Fact]
    public void LazyVStack_IReactorKeyed_Defaults_KeySelector_To_DotKey()
    {
        var items = Items(("x", "X"), ("y", "Y"));

        var defaulted = LazyVStack(items, ViewBuilder);

        Assert.Equal("x", defaulted.GetKeyAt_TestAccessor(0));
        Assert.Equal("y", defaulted.GetKeyAt_TestAccessor(1));
    }

    [Fact]
    public void LazyHStack_IReactorKeyed_Defaults_KeySelector_To_DotKey()
    {
        var items = Items(("x", "X"), ("y", "Y"));

        var defaulted = LazyHStack(items, ViewBuilder);

        Assert.Equal("x", defaulted.GetKeyAt_TestAccessor(0));
        Assert.Equal("y", defaulted.GetKeyAt_TestAccessor(1));
    }

    // ════════════════════════════════════════════════════════════════════
    //  Diff parity — defaulted selector and explicit selector produce
    //  identical op-shape stats through KeyedListDiff.Apply.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void KeyedListDiff_DefaultedSelector_MatchesExplicitSelector_OnInsert()
    {
        AssertDiffParity(
            before: Items(("a", "A"), ("b", "B"), ("c", "C")),
            after: Items(("a", "A"), ("b", "B"), ("c", "C"), ("d", "D")));
    }

    [Fact]
    public void KeyedListDiff_DefaultedSelector_MatchesExplicitSelector_OnRemove()
    {
        AssertDiffParity(
            before: Items(("a", "A"), ("b", "B"), ("c", "C")),
            after: Items(("a", "A"), ("c", "C")));
    }

    [Fact]
    public void KeyedListDiff_DefaultedSelector_MatchesExplicitSelector_OnMove()
    {
        AssertDiffParity(
            before: Items(("a", "A"), ("b", "B"), ("c", "C")),
            after: Items(("c", "C"), ("a", "A"), ("b", "B")));
    }

    [Fact]
    public void KeyedListDiff_DefaultedSelector_MatchesExplicitSelector_OnReverse()
    {
        AssertDiffParity(
            before: Items(("a", "A"), ("b", "B"), ("c", "C"), ("d", "D")),
            after: Items(("d", "D"), ("c", "C"), ("b", "B"), ("a", "A")));
    }

    private static void AssertDiffParity(IReadOnlyList<KeyedItem> before, IReadOnlyList<KeyedItem> after)
    {
        var defaulted = ListView(after, ViewBuilder);
        var explicitSel = ListView(after, static i => i.Key, ViewBuilder);

        // Seed two parallel ReactorListState instances with the "before"
        // shape, then apply each construction shape's selector to the
        // "after" sequence. Both must emit the same op-shape stats.
        var stateA = SeedState(before);
        var stateB = SeedState(before);

        var statsA = KeyedListDiff.Apply(stateA, after, (it, i) => defaulted.GetKeyAt_TestAccessor(i));
        var statsB = KeyedListDiff.Apply(stateB, after, (it, i) => explicitSel.GetKeyAt_TestAccessor(i));

        Assert.Equal(statsB.Inserts, statsA.Inserts);
        Assert.Equal(statsB.Removes, statsA.Removes);
        Assert.Equal(statsB.Moves, statsA.Moves);
        Assert.Equal(statsB.Survivors, statsA.Survivors);
        Assert.Equal(statsB.Bailout, statsA.Bailout);
    }

    private static ReactorListState SeedState(IReadOnlyList<KeyedItem> items)
    {
        var s = new ReactorListState();
        var seed = new (int Index, string Key)[items.Count];
        for (int i = 0; i < items.Count; i++) seed[i] = (i, items[i].Key);
        s.Reset(seed);
        return s;
    }

    // ════════════════════════════════════════════════════════════════════
    //  WithKey<T, TKey>(IReactorKeyed) extension
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithKey_IReactorKeyed_Sets_Element_Key_To_Item_Key()
    {
        var item = new KeyedItem("k-42", "Hello");

        var byItem = TextBlock(item.Name).WithKey(item);
        var byString = TextBlock(item.Name).WithKey(item.Key);

        Assert.Equal("k-42", byItem.Key);
        Assert.Equal(byString.Key, byItem.Key);
    }

    [Fact]
    public void WithKey_IReactorKeyed_Preserves_Element_Type()
    {
        var item = new KeyedItem("k-1", "X");

        // Fluent inference: the overload must return the element type, not
        // some erased base. A compile-time regression here would be caught
        // by this assignment without an explicit cast.
        TextBlockElement keyed = TextBlock(item.Name).WithKey(item);

        Assert.Equal("k-1", keyed.Key);
        Assert.Equal("X", keyed.Content);
    }

    [Fact]
    public void WithKey_IReactorKeyed_Null_Throws_ArgumentNullException()
    {
        var el = TextBlock("X");
        KeyedItem? item = null;

        Assert.Throws<ArgumentNullException>(() => el.WithKey(item!));
    }

    // ════════════════════════════════════════════════════════════════════
    //  Stability requirement — repeated Key reads return the same value.
    //  Documents the IReactorKeyed contract from the interface docs.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void IReactorKeyed_Key_Is_Stable_Across_Reads()
    {
        var item = new KeyedItem("stable-key", "Some Name");

        // The interface explicitly requires stability; a record-property
        // implementation satisfies it trivially, but the test pins the
        // expectation so future implementations (e.g. computed properties)
        // are evaluated against this baseline.
        Assert.Equal(item.Key, item.Key);
        Assert.Equal("stable-key", item.Key);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Test accessor — surfaces the internal GetKeyAt seam to the test
//  assembly without exposing it to user code. The assembly already lists
//  Microsoft.UI.Reactor.Tests in InternalsVisibleTo, so this is purely an
//  ergonomic alias rather than a visibility widening.
// ════════════════════════════════════════════════════════════════════════
internal static class TemplatedListTestAccessor
{
    public static string GetKeyAt_TestAccessor(this TemplatedListElementBase el, int index) =>
        el.GetKeyAt(index);

    public static string GetKeyAt_TestAccessor(this LazyStackElementBase el, int index) =>
        el.GetKeyAt(index);
}
