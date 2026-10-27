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
    private static int S(int index) => ChildSlots.ForIndex(index);
    private static int G(int index, int inGroup) => ChildSlots.ForGroupChild(index, inGroup);

    [Fact]
    public void Factory_DroppedEmpty_RecordsArgumentSlots()
    {
        var a = TextBlock("a");
        var b = TextBlock("b");
        var stack = VStack(a, Empty(), b);

        Assert.Equal([a, b], stack.Children);
        Assert.Equal([S(0), S(2)], ChildSlots.Get(stack.Children));
    }

    [Fact]
    public void Factory_DroppedNull_RecordsArgumentSlots()
    {
        var a = TextBlock("a");
        var b = TextBlock("b");
        var stack = HStack(null, a, null, b);

        Assert.Equal([a, b], stack.Children);
        Assert.Equal([S(1), S(3)], ChildSlots.Get(stack.Children));
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
        Assert.Equal([S(0), G(1, 0), G(1, 2), S(2)], ChildSlots.Get(stack.Children));
    }

    [Fact]
    public void Factory_GroupResize_KeepsLaterSiblingSlot()
    {
        var e = TextBlock("e");
        var two = VStack(Group(TextBlock("x"), TextBlock("y")), e);
        var one = VStack(Group(TextBlock("x")), e);

        Assert.Equal(S(1), ChildSlots.Get(two.Children)![^1]);
        Assert.Equal(S(1), ChildSlots.Get(one.Children)![^1]);
    }

    [Fact]
    public void Slots_IncreaseWithPosition()
    {
        var stack = VStack(Empty(), TextBlock("a"), Group(TextBlock("b"), null, TextBlock("c")), null, TextBlock("d"));
        var slots = ChildSlots.Get(stack.Children)!;

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
    public void Factory_NestedGroup_FallsBackToGroupPositions()
    {
        // The outer group's own map mixes an argument id (A) with ids flattened out of the
        // inner group (B, C); reading only some of them as arguments would give A and B the
        // same slot.
        var stack = VStack(Group(null, TextBlock("A"), Group(TextBlock("B"), TextBlock("C"))));

        Assert.Equal([G(0, 0), G(0, 1), G(0, 2)], ChildSlots.Get(stack.Children));
    }

    [Fact]
    public void UniformGrid_CarriesSlotsToItsPositionedCells()
    {
        var grid = UniformGrid(Microsoft.UI.Xaml.Controls.Orientation.Horizontal, TextBlock("a"), Empty(), TextBlock("c"));

        Assert.Equal(2, grid.Children.Length);
        Assert.Equal([S(0), S(2)], ChildSlots.Get(grid.Children));
    }

    [Fact]
    public void Filter_HandBuiltArray_UsesRawIndices()
    {
        var a = TextBlock("a");
        var b = TextBlock("b");
        var filtered = ChildReconciler.Filter([a, EmptyElement.Instance, null!, b], out var slots);

        Assert.Equal([a, b], filtered);
        Assert.Equal([S(0), S(3)], slots);
    }

    [Fact]
    public void Filter_FactoryArray_ReturnsItsRegisteredSlots()
    {
        var stack = VStack(Empty(), TextBlock("a"));
        var filtered = ChildReconciler.Filter(stack.Children, out var slots);

        Assert.Same(stack.Children, filtered);
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
