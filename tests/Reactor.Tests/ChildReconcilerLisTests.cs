using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for ChildReconciler.ComputeLIS — the longest increasing subsequence
/// algorithm used for minimal-move keyed reconciliation.
/// </summary>
public class ChildReconcilerLisTests
{
    [Fact]
    public void ComputeLIS_Empty()
    {
        var result = ChildReconciler.ComputeLIS(Array.Empty<int>());
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeLIS_SingleElement()
    {
        var result = ChildReconciler.ComputeLIS(new[] { 5 });
        Assert.Single(result);
        Assert.Contains(0, result);
    }

    [Fact]
    public void ComputeLIS_AlreadySorted()
    {
        var result = ChildReconciler.ComputeLIS(new[] { 1, 2, 3, 4, 5 });
        Assert.Equal(5, result.Count); // entire array is the LIS
    }

    [Fact]
    public void ComputeLIS_ReverseSorted()
    {
        var result = ChildReconciler.ComputeLIS(new[] { 5, 4, 3, 2, 1 });
        Assert.Single(result); // only one element in LIS
    }

    [Fact]
    public void ComputeLIS_WithUnmapped()
    {
        // -1 entries are skipped (unmapped items)
        var result = ChildReconciler.ComputeLIS(new[] { -1, 2, -1, 4, -1 });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ComputeLIS_AllUnmapped()
    {
        var result = ChildReconciler.ComputeLIS(new[] { -1, -1, -1 });
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeLIS_MixedSequence()
    {
        // Array: [3, 1, 2, 4] → LIS is [1, 2, 4] (length 3)
        var result = ChildReconciler.ComputeLIS(new[] { 3, 1, 2, 4 });
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ComputeLIS_RealisticReconcile()
    {
        // Simulates old→new index mapping: items 0,1,2,3 reordered to 2,0,1,3
        // newToOld = [2, 0, 1, 3] → LIS = [0, 1, 3] (indices 1,2,3)
        var result = ChildReconciler.ComputeLIS(new[] { 2, 0, 1, 3 });
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ComputeLIS_DuplicateValues()
    {
        var result = ChildReconciler.ComputeLIS(new[] { 1, 3, 2, 3 });
        Assert.True(result.Count >= 2);
    }

    [Fact]
    public void ComputeLIS_LargeSequence()
    {
        // Sequential with one swap — LIS should be n-1
        var arr = Enumerable.Range(0, 100).ToArray();
        (arr[50], arr[51]) = (arr[51], arr[50]); // swap two
        var result = ChildReconciler.ComputeLIS(arr);
        Assert.True(result.Count >= 99);
    }

    [Fact]
    public void ComputeLIS_ReturnedIndicesAreValid()
    {
        var arr = new[] { 5, 1, 4, 2, 3 };
        var result = ChildReconciler.ComputeLIS(arr);
        foreach (var idx in result)
        {
            Assert.InRange(idx, 0, arr.Length - 1);
        }
        // Values at returned indices should be increasing
        var values = result.OrderBy(i => i).Select(i => arr[i]).ToList();
        for (int i = 1; i < values.Count; i++)
        {
            Assert.True(values[i] > values[i - 1]);
        }
    }
}
