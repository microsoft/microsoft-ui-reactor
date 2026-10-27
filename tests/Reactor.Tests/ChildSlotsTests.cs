using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Slot identity for unkeyed container children: the DSL container factories record which
/// argument each kept child came from, so <see cref="ChildReconciler"/> can match the
/// siblings of a toggled <c>cond ? X : Empty()</c> by slot rather than by shifted index.
/// The slot-matched walk itself runs against live WinUI panels and is covered by the
/// <c>ConditionalChildSlot_*</c> selftests.
/// </summary>
public class ChildSlotsTests
{
    private static long S(int index) => ChildSlots.ForIndex(index);
    private static long G(int index, int inGroup) => ChildSlots.ForGroupChild(index, inGroup);

    private static long N(int index, long inGroup) => ChildSlots.Nest(index, inGroup);

    private static long[] SlotsOf(Element[] children)
    {
        var slots = ChildSlots.Get(children);
        Assert.NotNull(slots);
        return slots;
    }

    [Fact]
    public void Factory_DroppedEmpty_RecordsArgumentSlots()
    {
        var a = TextBlock("a");
        var b = TextBlock("b");
        var stack = VStack(a, Empty(), b);

        Assert.Equal([a, b], stack.Children);
        Assert.Equal([S(0), S(2)], SlotsOf(stack.Children));
    }

    [Fact]
    public void Factory_DroppedNull_RecordsArgumentSlots()
    {
        var a = TextBlock("a");
        var b = TextBlock("b");
        var stack = HStack(null, a, null, b);

        Assert.Equal([a, b], stack.Children);
        Assert.Equal([S(1), S(3)], SlotsOf(stack.Children));
    }

    [Fact]
    public void Factory_NothingDropped_RegistersNoMapAndKeepsTheArray()
    {
        Element[] args = [TextBlock("a"), TextBlock("b")];
        var stack = VStack(args);

        Assert.Same(args, stack.Children);
        Assert.Null(ChildSlots.Get(stack.Children));
    }

    [Fact]
    public void Factory_Group_GivesGroupChildrenSlotsInsideTheGroupArgument()
    {
        var a = TextBlock("a");
        var b = TextBlock("b");
        var d = TextBlock("d");
        var e = TextBlock("e");
        // Group(b, Empty(), d) drops its own middle argument; d keeps its group position 2.
        var stack = VStack(a, Group(b, Empty(), d), e);

        Assert.Equal([a, b, d, e], stack.Children);
        Assert.Equal([S(0), G(1, 0), G(1, 2), S(2)], SlotsOf(stack.Children));
    }

    [Fact]
    public void Factory_GroupResize_KeepsLaterSiblingSlot()
    {
        var e = TextBlock("e");
        var two = VStack(Group(TextBlock("x"), TextBlock("y")), e);
        var one = VStack(Group(TextBlock("x")), e);

        Assert.Equal(S(1), SlotsOf(two.Children)[^1]);
        Assert.Equal(S(1), SlotsOf(one.Children)[^1]);
    }

    [Fact]
    public void Slots_IncreaseWithPosition()
    {
        var stack = VStack(Empty(), TextBlock("a"), Group(TextBlock("b"), null, TextBlock("c")), null, TextBlock("d"));
        var slots = SlotsOf(stack.Children);

        for (int i = 1; i < slots.Length; i++)
            Assert.True(slots[i - 1] < slots[i], $"slot {i} = {slots[i]} does not follow {slots[i - 1]}");
    }

    [Fact]
    public void Factory_EverythingDropped_RegistersNoMap()
    {
        var stack = VStack(Empty(), null);

        Assert.Empty(stack.Children);
        Assert.Null(ChildSlots.Get(stack.Children));
    }

    [Fact]
    public void Factory_TrailingDrop_RegistersNoMap()
    {
        // The kept children keep their indices, which is what an array with no map means.
        var stack = VStack(TextBlock("a"), TextBlock("b"), null);

        Assert.Equal(2, stack.Children.Length);
        Assert.Null(ChildSlots.Get(stack.Children));
    }

    [Fact]
    public void Factory_NestedGroup_NestsTheGroupsOwnPaths()
    {
        // The outer group's map holds A's argument (1) and B / C's paths through the inner
        // group (2, 0) / (2, 1); the stack nests all three under its own argument 0.
        var stack = VStack(Group(null, TextBlock("A"), Group(TextBlock("B"), TextBlock("C"))));

        Assert.Equal([N(0, S(1)), N(0, G(2, 0)), N(0, G(2, 1))], SlotsOf(stack.Children));
        var slots = SlotsOf(stack.Children);
        Assert.True(slots[0] < slots[1] && slots[1] < slots[2]);
    }

    [Fact]
    public void Factory_NestedGroup_ToggleKeepsLaterSiblingsSlots()
    {
        // VStack(Group(show ? x : Empty(), y, Group(z))): hiding x must not hand y x's slot,
        // nor z y's.
        StackElement Tree(bool show) =>
            VStack(Group(show ? TextBlock("x") : Empty(), TextBlock("y"), Group(TextBlock("z"))));

        var shown = SlotsOf(Tree(true).Children);
        var hidden = SlotsOf(Tree(false).Children);

        Assert.Equal(3, shown.Length);
        Assert.Equal(2, hidden.Length);
        Assert.Equal(shown[1], hidden[0]);
        Assert.Equal(shown[2], hidden[1]);
    }

    [Fact]
    public void GroupChild_PastTheSignBit_StaysDistinctAndOrdered()
    {
        // Indices from 32768 set the top bit of a first-level id; nesting must not
        // sign-extend it into the argument above.
        Assert.NotEqual(G(0, 32768), G(1, 32768));
        Assert.True(S(0) < G(0, 32768) && G(0, 32768) < G(0, 40000) && G(0, 40000) < S(1));
        Assert.True(N(0, S(40000)) < S(1));
    }

    [Fact]
    public void Factory_DeepNestedGroup_ToggleKeepsShallowSiblingsSlots()
    {
        // Past four levels only the deepest child's path is shortened; y and z keep their
        // own slots when x toggles.
        StackElement Tree(bool show) =>
            VStack(Group(show ? TextBlock("x") : Empty(), TextBlock("y"),
                Group(Group(Group(Group(TextBlock("z")))))));

        var shown = SlotsOf(Tree(true).Children);
        var hidden = SlotsOf(Tree(false).Children);

        Assert.Equal(shown[1], hidden[0]);
        Assert.Equal(shown[2], hidden[1]);
        Assert.True(shown[0] < shown[1] && shown[1] < shown[2]);
    }

    [Fact]
    public void Slots_NeverDecreaseThroughDeepAndWideGroups()
    {
        var wide = new Element?[70000];
        wide[0] = TextBlock("first");
        wide[69999] = TextBlock("last");
        var stack = VStack(TextBlock("a"), Group(wide), Group(Group(Group(Group(Group(TextBlock("deep")))))), TextBlock("b"));
        var slots = SlotsOf(stack.Children);

        for (int i = 1; i < slots.Length; i++)
            Assert.True(slots[i - 1] <= slots[i], $"slot {i} = {slots[i]} is below {slots[i - 1]}");
    }
    [Fact]
    public void UniformGrid_CarriesSlotsToItsPositionedCells()
    {
        var grid = UniformGrid(Microsoft.UI.Xaml.Controls.Orientation.Horizontal, TextBlock("a"), Empty(), TextBlock("c"));

        Assert.Equal(2, grid.Children.Length);
        Assert.Equal([S(0), S(2)], SlotsOf(grid.Children));
    }

    [Fact]
    public void Filter_HandBuiltArray_UsesRawIndices()
    {
        var a = TextBlock("a");
        var b = TextBlock("b");
        var filtered = ChildReconciler.Filter([a, EmptyElement.Instance, null!, b], out var slots);

        Assert.Equal([a, b], filtered);
        Assert.NotNull(slots);
        Assert.Equal([S(0), S(3)], slots);
    }

    [Fact]
    public void Filter_FactoryArray_ReturnsItsRegisteredSlots()
    {
        var stack = VStack(Empty(), TextBlock("a"));
        var filtered = ChildReconciler.Filter(stack.Children, out var slots);

        Assert.Same(stack.Children, filtered);
        Assert.NotNull(slots);
        Assert.Equal([S(1)], slots);
    }

    [Fact]
    public void Filter_PlainArray_HasNoSlots()
    {
        Element[] plain = [TextBlock("a"), TextBlock("b")];
        var filtered = ChildReconciler.Filter(plain, out var slots);

        Assert.Same(plain, filtered);
        Assert.Null(slots);
    }

    [Fact]
    public void AlignByIndex_NoMaps_IsPositional() =>
        Assert.True(ChildSlots.AlignByIndex(null, 3, null, 2));

    [Fact]
    public void AlignByIndex_HiddenMiddleChild_IsNotPositional()
    {
        // VStack(h, cond ? x : Empty(), a, b): shown [h, x, a, b] → hidden [h, a, b].
        Assert.False(ChildSlots.AlignByIndex(null, 4, [S(0), S(2), S(3)], 3));
        Assert.False(ChildSlots.AlignByIndex([S(0), S(2), S(3)], 3, null, 4));
    }

    [Fact]
    public void AlignByIndex_HiddenTrailingChild_IsPositional() =>
        // VStack(a, b, cond ? x : Empty()): only the tail changes, which positional already handles.
        Assert.True(ChildSlots.AlignByIndex(null, 3, [S(0), S(1)], 2));

    [Fact]
    public void AlignByIndex_SameSlotsBothSides_IsPositional() =>
        Assert.True(ChildSlots.AlignByIndex([S(0), S(2)], 2, [S(0), S(2)], 2));

    [Fact]
    public void AlignByIndex_DifferentSlotsSameLength_IsNotPositional() =>
        // cond ? x : Empty() swapped with a sibling conditional: [x, _, b] ↔ [_, y, b].
        Assert.False(ChildSlots.AlignByIndex([S(0), S(2)], 2, [S(1), S(2)], 2));
}
