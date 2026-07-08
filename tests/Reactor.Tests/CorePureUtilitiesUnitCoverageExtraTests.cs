using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Targeted unit coverage for small pure-managed gaps in the Core utilities:
/// <see cref="QueryCache"/> miss metadata, <see cref="Optional{T}"/> boxed equality,
/// <see cref="KeyedMemoCache"/> diagnostics, and <see cref="InfiniteResource{TItem}"/>
/// in-flight bookkeeping / atomic items view.
/// </summary>
public class CorePureUtilitiesUnitCoverageExtraTests
{
    // ── QueryCache.TryGetFetchedAt (miss arms) ──────────────────────────────

    [Fact]
    public void QueryCache_TryGetFetchedAt_MissingKey_ReturnsFalseWithDefaults()
    {
        using var cache = new QueryCache();

        Assert.False(cache.TryGetFetchedAt("nope", out var fetchedAt, out var staleTime));
        Assert.Equal(default(DateTime), fetchedAt);
        Assert.Equal(default(TimeSpan), staleTime);
    }

    [Fact]
    public void QueryCache_TryGetFetchedAt_SlotWithoutEntry_MissesUntilSet()
    {
        using var cache = new QueryCache();

        // Subscribe creates a slot (Count == 1) but leaves Entry null, so the metadata
        // lookup still misses — Count distinguishes this arm from the missing-key case.
        cache.Subscribe("k");
        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGetFetchedAt("k", out _, out _));

        // After a Set the same slot now reports its fetched/stale metadata.
        cache.Set("k", 42, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(1));
        Assert.True(cache.TryGetFetchedAt("k", out _, out var staleTime));
        Assert.Equal(TimeSpan.FromSeconds(3), staleTime);
    }

    // ── Optional<T>.Equals(object) ──────────────────────────────────────────

    [Fact]
    public void Optional_ObjectEquals_IsDeclaredOverride_AndMatchesOnlyEqualBoxedOptional()
    {
        // The Equals(object) override is declared on Optional<T> itself, not inherited
        // from ValueType. This is the real oracle: deleting the override would make the
        // boxed calls below fall back to ValueType.Equals (which returns the same
        // results for these int cases), so only the DeclaringType check distinguishes it.
        var equalsObj = typeof(Optional<int>).GetMethod(
            nameof(object.Equals), new[] { typeof(object) });
        Assert.NotNull(equalsObj);
        Assert.Equal(typeof(Optional<int>), equalsObj!.DeclaringType);

        var seven = Optional<int>.Of(7);
        Assert.True(seven.Equals((object)Optional<int>.Of(7)));   // equal boxed Optional
        Assert.False(seven.Equals((object)Optional<int>.Of(8)));  // different value
        Assert.False(seven.Equals((object)"7"));                  // wrong runtime type
        Assert.False(seven.Equals((object?)null));                // null
    }

    // ── KeyedMemoCache diagnostics ──────────────────────────────────────────

    [Fact]
    public void KeyedMemoCache_ExposesCapacityAndCount()
    {
        var cache = new KeyedMemoCache(64);
        Assert.Equal(64, cache.Capacity);
        Assert.Equal(0, cache.Count);

        // Capacity is clamped to a floor of 1; default matches DefaultCapacity.
        Assert.Equal(1, new KeyedMemoCache(0).Capacity);
        Assert.Equal(KeyedMemoCache.DefaultCapacity, new KeyedMemoCache().Capacity);
    }

    // ── InfiniteResource in-flight bookkeeping ──────────────────────────────

    [Fact]
    public void InfiniteResource_ClearInflightSlot_RecomputesHighestInflightEnd()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 4));

        resource.MarkPageInFlight(1); // covers indices [4, 8)
        resource.MarkPageInFlight(3); // covers indices [12, 16) -> virtual length 16
        Assert.Equal(16, resource.Items.Count);

        // Clearing the higher page forces a conservative recompute across the
        // remaining in-flight slot (page 1), shrinking the virtual length to 8.
        resource.ClearInflightSlot(3);
        Assert.Equal(8, resource.Items.Count);
        Assert.True(resource.HasInFlightFetch);
    }

    [Fact]
    public void InfiniteResource_ItemsView_NonGenericEnumeratorYieldsLoadedItems()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 2));
        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(new[] { "a", "b" }, NextCursor: null));

        var nonGeneric = (global::System.Collections.IEnumerable)resource.Items;
        var enumerator = nonGeneric.GetEnumerator();

        var seen = new global::System.Collections.Generic.List<object?>();
        while (enumerator.MoveNext()) seen.Add(enumerator.Current);

        Assert.Equal(2, seen.Count);
        Assert.Equal("a", seen[0]);
        Assert.Equal("b", seen[1]);
    }
}
