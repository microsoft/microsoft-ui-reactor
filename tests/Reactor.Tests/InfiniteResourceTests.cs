using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for InfiniteResource — the pull-model paginated collection that
/// drives virtualized infinite-scroll scenarios.
/// </summary>
public class InfiniteResourceTests
{
    [Fact]
    public void Constructor_InitialState()
    {
        var resource = new InfiniteResource<string>(InfiniteResourceOptions.Default);
        Assert.Null(resource.TotalCount);
        Assert.IsType<LoadState.Loading>(resource.LoadState);
        Assert.True(resource.HasMore);
        Assert.Empty(resource.Items);
    }

    [Fact]
    public void ItemAt_NegativeIndex_ReturnsDefault()
    {
        var resource = new InfiniteResource<string>(InfiniteResourceOptions.Default);
        Assert.Null(resource.ItemAt(-1));
    }

    [Fact]
    public void ItemAt_UnloadedPage_TriggersCallback()
    {
        int? requestedPage = null;
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 10));
        resource.BindCallbacks(p => requestedPage = p, () => { });

        resource.ItemAt(5); // page 0
        Assert.Equal(0, requestedPage);
    }

    [Fact]
    public void ApplyPageResult_LoadsItems()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a", "b", "c" }, "cursor1", TotalCount: 6));

        Assert.Equal(6, resource.TotalCount);
        Assert.Equal("a", resource.ItemAt(0));
        Assert.Equal("b", resource.ItemAt(1));
        Assert.Equal("c", resource.ItemAt(2));
        Assert.IsType<LoadState.Idle>(resource.LoadState);
    }

    [Fact]
    public void ApplyPageResult_NullNextCursor_SetsEndOfList()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult<string>(0, new Page<string, string>(
            new[] { "a" }, null, TotalCount: 1));

        Assert.IsType<LoadState.EndOfList>(resource.LoadState);
        Assert.False(resource.HasMore);
    }

    [Fact]
    public void ApplyPageError_SetsErrorState()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageError(0, new InvalidOperationException("fail"));

        Assert.IsType<LoadState.Error>(resource.LoadState);
    }

    [Fact]
    public void EnsureRange_RequestsMultiplePages()
    {
        var requested = new List<int>();
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 5));
        resource.BindCallbacks(p => requested.Add(p), () => { });

        resource.EnsureRange(0, 14); // pages 0, 1, 2

        Assert.Contains(0, requested);
        Assert.Contains(1, requested);
        Assert.Contains(2, requested);
    }

    [Fact]
    public void EnsureRange_DoesNotDuplicateInflight()
    {
        var requested = new List<int>();
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 5));
        resource.BindCallbacks(p => requested.Add(p), () => { });

        resource.MarkPageInFlight(0);
        resource.EnsureRange(0, 4); // page 0 already in-flight

        Assert.DoesNotContain(0, requested);
    }

    [Fact]
    public void FetchNext_OnlyWorksInIdleState()
    {
        int? requestedPage = null;
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(p => requestedPage = p, () => { });

        // Initial state is Loading — FetchNext should be no-op
        resource.FetchNext();
        Assert.Null(requestedPage);

        // Apply a page to move to Idle
        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a" }, "cursor1"));
        Assert.IsType<LoadState.Idle>(resource.LoadState);

        resource.FetchNext();
        Assert.NotNull(requestedPage);
    }

    [Fact]
    public void Retry_OnlyWorksInErrorState()
    {
        int? requestedPage = null;
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(p => requestedPage = p, () => { });

        // Not in error state — Retry should be no-op
        resource.Retry();
        Assert.Null(requestedPage);

        // Move to error state
        resource.MarkPageInFlight(0);
        resource.ApplyPageError(0, new Exception("test"));

        resource.Retry();
        Assert.Equal(0, requestedPage);
    }

    [Fact]
    public void Refresh_InvokesCallback()
    {
        bool refreshCalled = false;
        var resource = new InfiniteResource<string>(InfiniteResourceOptions.Default);
        resource.BindCallbacks(_ => { }, () => refreshCalled = true);

        resource.Refresh();
        Assert.True(refreshCalled);
    }

    [Fact]
    public void ClearAllPages_ResetsState()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a", "b" }, "cursor1", TotalCount: 10));

        resource.ClearAllPages();

        Assert.IsType<LoadState.Loading>(resource.LoadState);
        // After clearing, Items is rebuilt to length 0 since no pages exist
    }

    [Fact]
    public void LRU_Eviction_MaxLoadedPages()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 2, MaxLoadedPages: 2));
        resource.BindCallbacks(_ => { }, () => { });

        // Load 3 pages — oldest should be evicted
        for (int i = 0; i < 3; i++)
        {
            resource.MarkPageInFlight(i);
            resource.ApplyPageResult(i, new Page<string, string>(
                new[] { $"p{i}a", $"p{i}b" }, $"cursor{i + 1}"));
        }

        // Page 0 should have been evicted; pages 1 and 2 remain
        Assert.Null(resource.ItemAt(0)); // evicted
    }

    [Fact]
    public void NextPageIndex_ReturnsCorrectValue()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        Assert.Equal(0, resource.NextPageIndex());

        resource.MarkPageInFlight(0);
        Assert.Equal(1, resource.NextPageIndex());

        resource.MarkPageInFlight(2);
        Assert.Equal(3, resource.NextPageIndex());
    }

    [Fact]
    public void GetCursor_ReturnsLastAppliedCursor()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a" }, "cursor_value"));

        Assert.Equal("cursor_value", resource.GetCursor<string>());
    }

    [Fact]
    public void ClearInflightSlot_RemovesUnstartedFetch()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(5);
        Assert.True(resource.HasInFlightFetch);

        resource.ClearInflightSlot(5);
        Assert.False(resource.HasInFlightFetch);
    }

    [Fact]
    public void ClearInflightSlot_DoesNotRemoveLoadedPage()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a" }, "cursor1"));

        resource.ClearInflightSlot(0); // should be no-op (loaded)
        Assert.Equal("a", resource.ItemAt(0));
    }

    [Fact]
    public void HasInFlightFetch_TracksCorrectly()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        Assert.False(resource.HasInFlightFetch);

        resource.MarkPageInFlight(0);
        Assert.True(resource.HasInFlightFetch);

        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a" }, "cursor1"));
        Assert.False(resource.HasInFlightFetch);
    }

    [Fact]
    public void EstimatedRemaining_WithTotalCount()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a", "b", "c" }, "cursor1", TotalCount: 10));

        Assert.Equal(7, resource.EstimatedRemaining);
    }

    [Fact]
    public void EstimatedRemaining_WithoutTotalCount_ReturnsPageSize()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 5));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a" }, "cursor1"));

        Assert.Equal(5, resource.EstimatedRemaining);
    }

    [Fact]
    public void EstimatedRemaining_AtEndOfList_ReturnsZero()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult<string>(0, new Page<string, string>(
            new[] { "a" }, null)); // null cursor = end

        Assert.Equal(0, resource.EstimatedRemaining);
    }

    [Fact]
    public void Items_ReflectsMultiplePages()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 2));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a", "b" }, "c1", TotalCount: 4));

        resource.MarkPageInFlight(1);
        resource.ApplyPageResult(1, new Page<string, string>(
            new[] { "c", "d" }, "c2"));

        Assert.Equal(4, resource.Items.Count);
        Assert.Equal("a", resource.Items[0]);
        Assert.Equal("d", resource.Items[3]);
    }

    [Fact]
    public void ItemAt_PastTotalCount_ReturnsDefault()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a" }, "c1", TotalCount: 1));

        Assert.Null(resource.ItemAt(5));
    }

    [Fact]
    public void MarkPageInFlight_AlreadyLoaded_IsIgnored()
    {
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 3));
        resource.BindCallbacks(_ => { }, () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a" }, "c1"));

        // Marking an already-loaded page in-flight should be a no-op
        resource.MarkPageInFlight(0);
        Assert.Equal("a", resource.ItemAt(0));
    }

    [Fact]
    public void InfiniteResourceOptions_Defaults()
    {
        var opts = InfiniteResourceOptions.Default;
        Assert.Equal(50, opts.PageSize);
        Assert.Null(opts.MaxLoadedPages);
        Assert.Equal(TimeSpan.Zero, opts.EffectiveStaleTime);
        Assert.Equal(TimeSpan.FromMinutes(5), opts.EffectiveCacheTime);
    }

    [Fact]
    public void InfiniteResourceOptions_CustomValues()
    {
        var opts = new InfiniteResourceOptions(
            PageSize: 10,
            MaxLoadedPages: 5,
            StaleTime: TimeSpan.FromSeconds(30),
            CacheTime: TimeSpan.FromMinutes(10),
            CacheKeyPrefix: "test");

        Assert.Equal(10, opts.PageSize);
        Assert.Equal(5, opts.MaxLoadedPages);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.EffectiveStaleTime);
        Assert.Equal(TimeSpan.FromMinutes(10), opts.EffectiveCacheTime);
    }

    [Fact]
    public void LoadState_Types()
    {
        Assert.IsType<LoadState.Loading>(LoadState.Loading.Instance);
        Assert.IsType<LoadState.Idle>(LoadState.Idle.Instance);
        Assert.IsType<LoadState.EndOfList>(LoadState.EndOfList.Instance);

        var error = new LoadState.Error(new Exception("test"));
        Assert.Equal("test", error.Exception.Message);
    }

    [Fact]
    public void EnsureRange_ClampsToTotalCount()
    {
        var requested = new List<int>();
        var resource = new InfiniteResource<string>(new InfiniteResourceOptions(PageSize: 5));
        resource.BindCallbacks(p => requested.Add(p), () => { });

        resource.MarkPageInFlight(0);
        resource.ApplyPageResult(0, new Page<string, string>(
            new[] { "a", "b", "c", "d", "e" }, "c1", TotalCount: 7));

        resource.EnsureRange(0, 100); // should clamp to page 0 and 1
        Assert.Contains(1, requested);
        Assert.DoesNotContain(20, requested);
    }
}
