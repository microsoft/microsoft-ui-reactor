using System.Collections;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls;

internal sealed class VirtualListIndices : IReadOnlyList<int>
{
    internal static readonly Func<int, string> DefaultKeySelector = static i => i.ToString();

    public VirtualListIndices(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Count = count;
    }

    public int Count { get; }

    public int this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return index;
        }
    }

    // Both inputs are framework-owned and immutable. A custom delegate, mutable
    // item list, or derived stack with different GetKeyAt behavior cannot opt in.
    internal static bool IsDefaultKeySource(LazyStackElementBase stack) =>
        stack.GetType() == typeof(LazyVStackElement<int>)
        && stack is LazyVStackElement<int> { Items: VirtualListIndices } vertical
        && ReferenceEquals(vertical.KeySelector, DefaultKeySelector);

    public IEnumerator<int> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return i;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
