using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Slot identity for the unkeyed children of a container: which argument of the
/// container factory each child came from. The DSL container factories drop
/// <see langword="null"/> / <see cref="EmptyElement"/> arguments and flatten
/// <see cref="GroupElement"/>, so a child's index in <c>Children</c> shifts when a
/// conditional sibling before it toggles. <see cref="ChildReconciler"/> matches
/// unkeyed children by slot instead, so <c>cond ? X : Empty()</c> holds its place
/// the way React's <c>null</c> does and the siblings after it keep their state.
/// </summary>
/// <remarks>
/// A slot id is the path of argument indices that leads to the child: the container's
/// argument, then — for a child flattened out of a <see cref="GroupElement"/> — the
/// group's argument, and so on. Each level takes 16 bits of a <see langword="long"/>,
/// most significant first; levels below the first store the index plus one, so a
/// shorter path sorts before the paths that extend it and ids increase with position.
/// Four levels fit: nesting a path that already uses all four drops its deepest level,
/// and an index past a level's range is clamped to its largest value. Either way ids
/// stay non-decreasing and only the children that share the shortened id match each
/// other by position (the merge walk pairs equal ids in order); their shallower siblings
/// keep their exact identity. An array with no slot map registered uses
/// <c>index &lt;&lt; 48</c>. Only the factories'
/// slow path (an argument was null, empty or a group, so it already allocates)
/// registers a map, and only when it differs from those implicit ids (a dropped
/// trailing argument does not); it is keyed weakly by the array it returns, and the
/// lookup is skipped entirely until a map has ever been registered.
/// </remarks>
internal static class ChildSlots
{
    /// <summary>Largest argument index a slot id can encode at the first level.</summary>
    internal const int MaxIndex = 0x7FFF;

    /// <summary>Largest index a slot id can encode at a nested (group) level.</summary>
    internal const int MaxGroupIndex = 0xFFFE;

    private const int LevelBits = 16;
    private const int FirstLevelShift = 48;

    private static readonly ConditionalWeakTable<Element[], long[]> s_slots = new();
    private static volatile bool s_any;

    internal static long ForIndex(int index) => (long)Math.Min(index, MaxIndex) << FirstLevelShift;

    /// <summary>The id of the child at <paramref name="indexInGroup"/> of a group with no map
    /// of its own, the group being argument <paramref name="index"/>.</summary>
    internal static long ForGroupChild(int index, int indexInGroup) =>
        ((long)Math.Min(index, MaxIndex) << FirstLevelShift)
        | ((long)(Math.Min(indexInGroup, MaxGroupIndex) + 1) << (FirstLevelShift - LevelBits));

    /// <summary>The id of a group's child whose id relative to the group is
    /// <paramref name="groupSlot"/>, the group being argument <paramref name="index"/>.</summary>
    internal static long Nest(int index, long groupSlot) =>
        ((long)Math.Min(index, MaxIndex) << FirstLevelShift)
        | ((groupSlot >>> LevelBits) + (1L << (FirstLevelShift - LevelBits)));

    internal static void Set(Element[] children, long[] slots)
    {
        s_slots.AddOrUpdate(children, slots);
        s_any = true;
    }

    internal static long[]? Get(Element[] children) =>
        s_any && s_slots.TryGetValue(children, out var slots) ? slots : null;

    internal static long At(long[]? slots, int index) => slots is null ? ForIndex(index) : slots[index];

    /// <summary>
    /// Whether matching two child arrays by index pairs the same children as matching
    /// them by slot: every index both arrays have holds the same slot. When it does,
    /// the positional reconcile (and its structural-skip fast path) is used unchanged.
    /// </summary>
    internal static bool AlignByIndex(long[]? oldSlots, int oldLength, long[]? newSlots, int newLength)
    {
        if (oldSlots is null && newSlots is null) return true;
        int common = Math.Min(oldLength, newLength);
        for (int i = 0; i < common; i++)
        {
            if (At(oldSlots, i) != At(newSlots, i)) return false;
        }
        return true;
    }
}