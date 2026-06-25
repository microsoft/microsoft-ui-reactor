using System.Buffers;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for <see cref="ChildReconciler.ComputeLISInto"/> — the
/// allocation-free LIS variant introduced by issue #653 that writes a
/// membership mask into a caller-supplied (pooled) <c>bool[]</c> instead of
/// allocating a <see cref="HashSet{T}"/>. These assert it produces exactly the
/// same membership as the legacy <see cref="ChildReconciler.ComputeLIS"/>
/// wrapper, clears any stale markers in the supplied mask, and never reads
/// past <c>length</c> when the mask buffer is larger (the pooled case).
/// </summary>
public class ChildReconcilerLisIntoTests
{
    private static bool[] MaskFor(int[] arr)
    {
        var mask = new bool[arr.Length];
        ChildReconciler.ComputeLISInto(arr, arr.Length, mask);
        return mask;
    }

    private static HashSet<int> SetFromMask(bool[] mask, int length)
    {
        var set = new HashSet<int>();
        for (int i = 0; i < length; i++)
            if (mask[i]) set.Add(i);
        return set;
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 5 })]
    [InlineData(new[] { 1, 2, 3, 4, 5 })]
    [InlineData(new[] { 5, 4, 3, 2, 1 })]
    [InlineData(new[] { -1, 2, -1, 4, -1 })]
    [InlineData(new[] { -1, -1, -1 })]
    [InlineData(new[] { 3, 1, 2, 4 })]
    [InlineData(new[] { 2, 0, 1, 3 })]
    [InlineData(new[] { 1, 3, 2, 3 })]
    [InlineData(new[] { 5, 1, 4, 2, 3 })]
    public void ComputeLISInto_Matches_HashSet_Wrapper(int[] arr)
    {
        var expected = ChildReconciler.ComputeLIS(arr);
        var mask = MaskFor(arr);
        var actual = SetFromMask(mask, arr.Length);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeLISInto_Clears_Stale_Markers_In_Supplied_Mask()
    {
        // A pooled mask can arrive with stale `true` entries. ComputeLISInto
        // must clear [0,length) so only genuine LIS members remain true.
        var arr = new[] { 5, 4, 3, 2, 1 }; // LIS is a single element
        var mask = new bool[arr.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = true; // pre-dirty

        ChildReconciler.ComputeLISInto(arr, arr.Length, mask);

        int trues = 0;
        for (int i = 0; i < arr.Length; i++) if (mask[i]) trues++;
        Assert.Equal(1, trues); // reverse-sorted ⇒ LIS length 1
    }

    [Fact]
    public void ComputeLISInto_Honors_Length_When_Mask_Is_Larger()
    {
        // Simulate the hot-path contract: the bool[] comes from ArrayPool and
        // is larger than `length`. Only [0,length) may be touched/read.
        int[] arr = { 2, 0, 1, 3 }; // LIS = indices {1,2,3}
        int length = arr.Length;

        var pool = ArrayPool<bool>.Shared;
        bool[] mask = pool.Rent(length);
        try
        {
            // Dirty the whole rented buffer including the tail beyond length.
            for (int i = 0; i < mask.Length; i++) mask[i] = true;

            ChildReconciler.ComputeLISInto(arr, length, mask);

            var actual = SetFromMask(mask, length);
            Assert.Equal(new HashSet<int> { 1, 2, 3 }, actual);
        }
        finally
        {
            pool.Return(mask, clearArray: true);
        }
    }

    [Fact]
    public void ComputeLISInto_LargeSequence_With_One_Swap()
    {
        var arr = Enumerable.Range(0, 100).ToArray();
        (arr[50], arr[51]) = (arr[51], arr[50]);

        var mask = MaskFor(arr);
        int trues = 0;
        for (int i = 0; i < arr.Length; i++) if (mask[i]) trues++;

        Assert.True(trues >= 99);
        // Values at LIS positions must be strictly increasing.
        int prev = int.MinValue;
        for (int i = 0; i < arr.Length; i++)
        {
            if (!mask[i]) continue;
            Assert.True(arr[i] > prev);
            prev = arr[i];
        }
    }
}
