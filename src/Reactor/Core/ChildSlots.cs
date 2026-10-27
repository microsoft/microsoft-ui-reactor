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
/// A slot id is <c>argumentIndex &lt;&lt; 16</c> for a direct child, or
/// <c>(argumentIndex &lt;&lt; 16) | (indexInGroup + 1)</c> for a child flattened out of
/// a <see cref="GroupElement"/>; ids increase with position. An array with no slot
/// map registered uses <c>index &lt;&lt; 16</c>. Only the factories' slow path (an
/// argument was null, empty or a group, so it already allocates) registers a map,
/// keyed weakly by the array it returns; the lookup is skipped entirely until a map
/// has ever been registered.
/// </remarks>
internal static class ChildSlots
{
    /// <summary>Largest argument index a slot id can encode.</summary>
    internal const int MaxIndex = 0x7FFF;

    /// <summary>Largest index inside a group a slot id can encode.</summary>
    internal const int MaxGroupIndex = 0xFFFE;

    private static readonly ConditionalWeakTable<Element[], int[]> s_slots = new();
    private static volatile bool s_any;

    internal static int ForIndex(int index) => index << 16;

    internal static int ForGroupChild(int index, int indexInGroup) => (index << 16) | (indexInGroup + 1);

    internal static void Set(Element[] children, int[] slots)
    {
        s_slots.AddOrUpdate(children, slots);
        s_any = true;
    }

    internal static int[]? Get(Element[] children) =>
        s_any && s_slots.TryGetValue(children, out var slots) ? slots : null;

    internal static int At(int[]? slots, int index) => slots is null ? index << 16 : slots[index];

    /// <summary>
    /// Whether matching two child arrays by index pairs the same children as matching
    /// them by slot: every index both arrays have holds the same slot. When it does,
    /// the positional reconcile (and its structural-skip fast path) is used unchanged.
    /// </summary>
    internal static bool AlignByIndex(int[]? oldSlots, int oldLength, int[]? newSlots, int newLength)
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
