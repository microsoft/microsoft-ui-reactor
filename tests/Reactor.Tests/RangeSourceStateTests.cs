using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for <see cref="RangeSourceState"/> (#98/#99) — the ping-pong
/// <c>[0..N-1]</c> backing store that replaced per-render
/// <c>Enumerable.Range(...).ToList()</c> in the ListView / GridView handlers.
///
/// The load-bearing correctness property is that consecutive calls return
/// <em>different</em> List references (so WinUI's <c>ItemsSource</c> setter sees
/// a reference change and recycles/re-realizes its containers — Issue #495/#464)
/// while the content stays <c>[0, 1, …, count-1]</c>.
/// </summary>
public class RangeSourceStateTests
{
    private static int[] Range(int n)
    {
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = i;
        return r;
    }

    [Fact]
    public void Next_ReturnsZeroToNMinusOne()
    {
        var state = new RangeSourceState();
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, state.Next(5));
    }

    [Fact]
    public void Next_ZeroCount_ReturnsEmptyList()
    {
        var state = new RangeSourceState();
        Assert.Empty(state.Next(0));
    }

    [Fact]
    public void Next_ConsecutiveCalls_ReturnAlternatingReferences()
    {
        var state = new RangeSourceState();
        var a = state.Next(3);
        var b = state.Next(3);
        var c = state.Next(3);

        // Ping-pong: each call hands back the buffer the previous call didn't,
        // so neighbours always differ but it alternates back after two calls.
        Assert.NotSame(a, b);
        Assert.NotSame(b, c);
        Assert.Same(a, c);

        // Content is identical [0,1,2] regardless of which buffer is returned.
        Assert.Equal(new[] { 0, 1, 2 }, a);
        Assert.Equal(new[] { 0, 1, 2 }, b);
        Assert.Equal(new[] { 0, 1, 2 }, c);
    }

    [Fact]
    public void Next_CountChange_RebuildsContent_AndKeepsAlternatingReferences()
    {
        var state = new RangeSourceState();

        // Buffers are reused in place, so a later count change mutates the
        // buffer an earlier call returned (by design — that's the allocation
        // win). Assert content immediately after each call, and compare each
        // reference only against the immediately-prior one.
        var a = state.Next(2);
        Assert.Equal(new[] { 0, 1 }, a);

        var b = state.Next(4);
        Assert.Equal(new[] { 0, 1, 2, 3 }, b);
        Assert.NotSame(a, b);

        var c = state.Next(4);
        Assert.Equal(new[] { 0, 1, 2, 3 }, c);
        Assert.NotSame(b, c);

        var d = state.Next(1);
        Assert.Equal(new[] { 0 }, d);
        Assert.NotSame(c, d);
    }

    [Fact]
    public void Next_ManyMixedCalls_AlwaysDifferFromImmediatelyPriorReference()
    {
        var state = new RangeSourceState();
        List<int>? prev = null;

        // A mix of repeated-count and count-change calls — the same shape the
        // grid stress workload produces frame to frame.
        foreach (var count in new[] { 1, 1, 1, 3, 3, 2, 2, 2, 5, 5, 0, 4 })
        {
            var current = state.Next(count);

            // Reference must differ from the immediately-prior result; otherwise
            // WinUI short-circuits the ItemsSource set and skips the refresh.
            if (prev is not null)
                Assert.NotSame(prev, current);

            // Content is always exactly the range for the requested count.
            Assert.Equal(Range(count), current);

            prev = current;
        }
    }
}
