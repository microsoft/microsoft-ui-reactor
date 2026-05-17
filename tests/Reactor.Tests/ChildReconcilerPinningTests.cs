using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pinning tests for ChildReconciler — locked-in invariants that Phase 1 of
/// spec 042 (keyed-list reconciliation) must not change. The Phase 1 work
/// touches the templated-list (ListView/GridView/LazyVStack) reconciliation
/// path; <see cref="ChildReconciler"/> drives the hand-built keyed-children
/// path (e.g. <c>FlexColumn(items.Select(... .WithKey(item.Id)))</c>) and
/// must keep behaving identically. End-to-end animation behavior on real
/// WinUI panels is verified in <c>Reactor.AppTests/Tests/
/// KeyedListReconciliationTests.cs</c>; this file pins the algorithmic
/// contracts that are reachable without a XAML host.
/// </summary>
public class ChildReconcilerPinningTests
{
    // ════════════════════════════════════════════════════════════════════
    //  HasAnyKeys — single-key, mixed-key, and edge-case coverage
    // ════════════════════════════════════════════════════════════════════

    private static bool InvokeHasAnyKeys(Element[] elements)
    {
        var method = typeof(ChildReconciler).GetMethod(
            "HasAnyKeys",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [elements])!;
    }

    private static string? InvokeGetKey(Element element, int positionalIndex)
    {
        var method = typeof(ChildReconciler).GetMethod(
            "GetKey",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (string?)method!.Invoke(null, [element, positionalIndex]);
    }

    private static bool InvokeKeyMatch(Element a, Element b)
    {
        var method = typeof(ChildReconciler).GetMethod(
            "KeyMatch",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [a, b])!;
    }

    [Fact]
    public void HasAnyKeys_Empty_Returns_False()
    {
        Assert.False(InvokeHasAnyKeys([]));
    }

    [Fact]
    public void HasAnyKeys_Mixed_Keyed_And_Unkeyed_Returns_True()
    {
        var elements = new Element[]
        {
            new TextBlockElement("a"),                       // unkeyed
            new TextBlockElement("b") { Key = "k" },         // keyed
            new TextBlockElement("c"),                       // unkeyed
        };
        Assert.True(InvokeHasAnyKeys(elements));
    }

    [Fact]
    public void HasAnyKeys_First_Element_Keyed_Returns_True()
    {
        var elements = new Element[]
        {
            new TextBlockElement("a") { Key = "k1" },
            new TextBlockElement("b"),
        };
        Assert.True(InvokeHasAnyKeys(elements));
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetKey — explicit key vs positional fallback
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetKey_Explicit_Returns_Provided_Key()
    {
        var el = new TextBlockElement("hi") { Key = "user-42" };
        Assert.Equal("user-42", InvokeGetKey(el, 0));
    }

    [Fact]
    public void GetKey_Unkeyed_Returns_Positional_Synthetic()
    {
        var el = new TextBlockElement("hi");
        var key = InvokeGetKey(el, 3);
        // Synthetic key must include both the position and the element type
        // name so that two unkeyed elements of different types at the same
        // position do not collide and accidentally update each other.
        Assert.NotNull(key);
        Assert.Contains("3", key!);
        Assert.Contains(nameof(TextBlockElement), key);
    }

    [Fact]
    public void GetKey_Unkeyed_Different_Types_Yield_Different_Synthetic_Keys()
    {
        var a = InvokeGetKey(new TextBlockElement("a"), 5);
        var b = InvokeGetKey(new ButtonElement("a"), 5);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GetKey_Unkeyed_Same_Type_Different_Positions_Yield_Different_Synthetic_Keys()
    {
        var a = InvokeGetKey(new TextBlockElement("x"), 0);
        var b = InvokeGetKey(new TextBlockElement("x"), 1);
        Assert.NotEqual(a, b);
    }

    // ════════════════════════════════════════════════════════════════════
    //  KeyMatch — both-null is a match, type-mismatch never matches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void KeyMatch_Both_Null_Keys_Same_Type_Returns_True()
    {
        var a = new TextBlockElement("a");
        var b = new TextBlockElement("b");
        Assert.True(InvokeKeyMatch(a, b));
    }

    [Fact]
    public void KeyMatch_Both_Same_NonNull_Key_Same_Type_Returns_True()
    {
        var a = new TextBlockElement("a") { Key = "k" };
        var b = new TextBlockElement("b") { Key = "k" };
        Assert.True(InvokeKeyMatch(a, b));
    }

    [Fact]
    public void KeyMatch_Different_Types_Returns_False_Even_When_Keys_Match()
    {
        var a = new TextBlockElement("a") { Key = "k" };
        var b = new ButtonElement("a")    { Key = "k" };
        Assert.False(InvokeKeyMatch(a, b));
    }

    [Fact]
    public void KeyMatch_One_Null_Other_Set_Returns_False()
    {
        var a = new TextBlockElement("a");                       // null key
        var b = new TextBlockElement("a") { Key = "k" };         // explicit key
        Assert.False(InvokeKeyMatch(a, b));
    }

    // ════════════════════════════════════════════════════════════════════
    //  LIS edge cases that Phase 1 must not regress
    //
    //  These complement ChildReconcilerLisTests / ChildReconcilerTests /
    //  ChildReconcilerIntegrationTests by covering shapes that are most
    //  load-bearing for keyed list animations: pure insert/remove (no LIS
    //  movement), pure reverse, partial reverse, and shuffles with stable
    //  prefix/suffix.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Lis_Pure_Insert_Pattern_All_Old_Are_Increasing()
    {
        // newToOld for "append two new at end": [0, 1, 2, -1, -1]
        // (old indices 0,1,2 are in order; the two -1 are new items not in LIS)
        var lis = ChildReconciler.ComputeLIS([0, 1, 2, -1, -1]);
        // All three mapped positions form the LIS — no moves emitted.
        Assert.Equal(3, lis.Count);
        Assert.Contains(0, lis);
        Assert.Contains(1, lis);
        Assert.Contains(2, lis);
    }

    [Fact]
    public void Lis_Prepend_All_Existing_Are_In_LIS()
    {
        // newToOld for "prepend two new at start": [-1, -1, 0, 1, 2]
        var lis = ChildReconciler.ComputeLIS([-1, -1, 0, 1, 2]);
        Assert.Equal(3, lis.Count);
        Assert.Contains(2, lis);
        Assert.Contains(3, lis);
        Assert.Contains(4, lis);
    }

    [Fact]
    public void Lis_Single_Move_Floats_Item_By_One()
    {
        // [B, A, C, D] from [A, B, C, D]: newToOld = [1, 0, 2, 3]
        // LIS is either [0, 2, 3] (length 3 — keep B,C,D; move A) or
        // [1, 2, 3] (also length 3 — keep A,C,D; move B). Both are valid
        // minimal-move solutions; we only pin that exactly N-1 children
        // stay put.
        var lis = ChildReconciler.ComputeLIS([1, 0, 2, 3]);
        Assert.Equal(3, lis.Count);
    }

    [Fact]
    public void Lis_Reverse_Five_Items_Yields_Single_Pin()
    {
        // newToOld = [4, 3, 2, 1, 0]
        var lis = ChildReconciler.ComputeLIS([4, 3, 2, 1, 0]);
        Assert.Single(lis); // exactly one child stays put; others move
    }

    [Fact]
    public void Lis_Stable_Prefix_And_Suffix_With_Shuffled_Middle()
    {
        // newToOld = [0, 1, 5, 4, 3, 2, 6, 7] — stable [0,1] prefix and
        // stable [6,7] suffix with a reversed middle [5,4,3,2].
        // LIS must contain all four stable entries plus at most one from
        // the reversed middle, so length is 5.
        var lis = ChildReconciler.ComputeLIS([0, 1, 5, 4, 3, 2, 6, 7]);
        Assert.Equal(5, lis.Count);
        Assert.Contains(0, lis);
        Assert.Contains(1, lis);
        Assert.Contains(6, lis);
        Assert.Contains(7, lis);
    }

    [Fact]
    public void Lis_All_New_Returns_Empty()
    {
        // Mounting fresh: every position is -1 (no old match).
        var lis = ChildReconciler.ComputeLIS([-1, -1, -1, -1]);
        Assert.Empty(lis);
    }

    [Fact]
    public void Lis_All_Removed_Single_Survivor()
    {
        // newToOld = [3] — only one survivor, three were removed.
        var lis = ChildReconciler.ComputeLIS([3]);
        Assert.Single(lis);
        Assert.Contains(0, lis);
    }
}
