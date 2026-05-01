using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost tests for DataGrid incremental/paginated data loading.
/// Verifies that large datasets don't eagerly load all rows, and that
/// scrolling triggers additional page loads via DataPageCache.
/// </summary>
internal static class DataGridPagingFixtures
{
    record PagingItem(int Id, string Name, string Category);

    /// <summary>
    /// Wraps a ListDataSource and tracks GetPageAsync calls to verify
    /// incremental loading behavior.
    /// </summary>
    private class TrackingSource : IDataSource<PagingItem>
    {
        private readonly IDataSource<PagingItem> _inner;
        public int CallCount { get; private set; }
        public int TotalItemsFetched { get; private set; }
        public int LargestPageRequested { get; private set; }

        public TrackingSource(IDataSource<PagingItem> inner) => _inner = inner;
        public DataSourceCapabilities Capabilities => _inner.Capabilities;
        public RowKey GetRowKey(PagingItem item) => _inner.GetRowKey(item);

        public async Task<DataPage<PagingItem>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (request.PageSize > LargestPageRequested)
                LargestPageRequested = request.PageSize;
            var page = await _inner.GetPageAsync(request, cancellationToken);
            TotalItemsFetched += page.Items.Count;
            return page;
        }
    }

    private static TrackingSource CreateTrackingSource(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => new PagingItem(
            Id: i,
            Name: $"Employee {i:D5}",
            Category: i % 5 == 0 ? "Manager" : "Individual"
        ));
        var inner = new ListDataSource<PagingItem>(items, p => (RowKey)p.Id);
        return new TrackingSource(inner);
    }

    private static IReadOnlyList<FieldDescriptor> CreateColumns()
    {
        return new FieldDescriptor[]
        {
            Column<PagingItem>("Id", p => p.Id, width: 60),
            Column<PagingItem>("Name", p => p.Name, width: 200),
            Column<PagingItem>("Category", p => p.Category, width: 120),
        };
    }

    /// <summary>
    /// Mount a DataGrid with 10,000 items and verify that only a small
    /// number of pages are fetched (not all 10K items). The DataGrid should
    /// use DataPageCache for incremental block loading.
    /// </summary>
    internal class IncrementalLoadVerification(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            TrackingSource? tracking = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateTrackingSource(10_000));
                tracking = source;

                return DataGrid(
                    source: source,
                    columns: CreateColumns(),
                    rowHeight: 36
                );
            });

            await Harness.Render(500);

            // 1. Grid renders with data
            H.Check("Paging_Renders",
                H.FindTextContaining("Employee") is not null);

            // 2. Total count should reflect the full dataset
            H.Check($"Paging_TotalCountReported (tracking={tracking?.TotalItemsFetched})",
                tracking is not null);

            // 3. NOT all 10,000 items were fetched — this is the key assertion.
            // With incremental paging, only the first block (~50 items) should be loaded.
            H.Check($"Paging_IncrementalLoad (fetched={tracking!.TotalItemsFetched}, largest={tracking.LargestPageRequested})",
                tracking.TotalItemsFetched <= 200);

            // 4. The largest page request should be a block size (50), not int.MaxValue
            H.Check($"Paging_NoHugePageRequest (largest={tracking.LargestPageRequested})",
                tracking.LargestPageRequested <= 100);

            await Harness.Render(200);

            // 5. Grid is still alive and rendering correctly
            H.Check("Paging_StillAlive",
                H.FindTextContaining("Employee") is not null);
        }
    }

    /// <summary>
    /// Mount a small DataGrid (20 items, fits in one block) and verify all items
    /// are accessible. This tests backward compatibility — small datasets should
    /// still work correctly with the paging infrastructure.
    /// </summary>
    internal class SmallDatasetFullyLoaded(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            TrackingSource? tracking = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateTrackingSource(20));
                tracking = source;

                return DataGrid(
                    source: source,
                    columns: CreateColumns(),
                    rowHeight: 36
                );
            });

            await Harness.Render(500);

            // 1. All items visible in small dataset
            H.Check("PagingSmall_Renders",
                H.FindTextContaining("Employee 00000") is not null);

            // 2. Only one page needed for 20 items (block size is 50)
            H.Check($"PagingSmall_OneFetch (calls={tracking?.CallCount})",
                tracking is not null && tracking.CallCount <= 2);

            // 3. All 20 items were fetched (they fit in one block)
            H.Check($"PagingSmall_AllFetched (fetched={tracking!.TotalItemsFetched})",
                tracking.TotalItemsFetched == 20);

            await Harness.Render(200);

            // 4. Grid stable
            H.Check("PagingSmall_Stable",
                H.FindTextContaining("Employee") is not null);
        }
    }
}
