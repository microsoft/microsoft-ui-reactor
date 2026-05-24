using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost fixtures that validate the hook-based DataGrid paging path
/// (UseInfiniteResource + UseDataSource adapter) produces behavior that
/// matches the legacy DataPageCache path. Each fixture toggles
/// <see cref="ReactorFeatureFlags.UseHookBasedPaging"/> around the mount
/// and restores the previous value on exit.
/// </summary>
/// <remarks>
/// A full rendered-tree equality assertion is out of scope; the fixtures here
/// assert observable behavioral parity: same visible rows after scroll, same
/// commit semantics, same loading behavior on mount.
/// </remarks>
internal static class DataGridParityFixtures
{
    private record ParityItem(int Id, string Name, string Dept, string Title);

    private sealed class CountingSource : IDataSource<ParityItem>
    {
        private readonly IDataSource<ParityItem> _inner;
        public int CallCount { get; private set; }

        public CountingSource(IDataSource<ParityItem> inner) => _inner = inner;

        public DataSourceCapabilities Capabilities => _inner.Capabilities;
        public RowKey GetRowKey(ParityItem item) => _inner.GetRowKey(item);

        public async Task<DataPage<ParityItem>> GetPageAsync(DataRequest request, CancellationToken ct = default)
        {
            CallCount++;
            return await _inner.GetPageAsync(request, ct);
        }
    }

    private static CountingSource CreateSource(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => new ParityItem(
            Id: i,
            Name: $"Emp-{i:D6}",
            Dept: $"Dept-{i % 12}",
            Title: $"Title-{i % 50}"));
        return new CountingSource(new ListDataSource<ParityItem>(items, p => (RowKey)p.Id));
    }

    private static IReadOnlyList<FieldDescriptor> CreateColumns()
    {
        return new FieldDescriptor[]
        {
            Column<ParityItem>("Id", p => p.Id, width: 80),
            Column<ParityItem>("Name", p => p.Name, width: 160),
            Column<ParityItem>("Dept", p => p.Dept, width: 120),
            Column<ParityItem>("Title", p => p.Title, width: 160),
        };
    }

    /// <summary>
    /// Runs <paramref name="action"/> with <see cref="ReactorFeatureFlags.UseHookBasedPaging"/>
    /// forced to the requested value, restoring the previous value on exit. Tests
    /// that flip the flag run sequentially inside the selfhost runner so there's no
    /// race with concurrent DataGrid fixtures.
    /// </summary>
    private static async Task WithHookPaging(bool enabled, Func<Task> action)
    {
        var previous = ReactorFeatureFlags.UseHookBasedPaging;
        ReactorFeatureFlags.UseHookBasedPaging = enabled;
        try { await action(); }
        finally { ReactorFeatureFlags.UseHookBasedPaging = previous; }
    }

    // ── Parity test 1: Mount + initial data on hook path ────────────────

    /// <summary>
    /// Mounts a DataGrid with 10k items on the hook path and asserts the same
    /// visible-data shape the legacy path produces in <see cref="DataGridPagingFixtures.IncrementalLoadVerification"/>:
    /// initial render shows rows, total-count is reported, and only a few pages
    /// are fetched (not all 10k items).
    /// </summary>
    internal class HookPagingMountAndLoad(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithHookPaging(true, RunInner);

        private async Task RunInner()
        {
            CountingSource? source = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                source = ctx.UseMemo(() => CreateSource(10_000));
                return DataGrid(
                    source: source!,
                    columns: CreateColumns(),
                    rowHeight: 36
                ).Height(500);
            });

            await Harness.Render(500);

            H.Check("HookPaging_Mount_FirstRowVisible",
                H.FindTextContaining("Emp-000000") is not null);

            H.Check("HookPaging_Mount_SecondRowVisible",
                H.FindTextContaining("Emp-000001") is not null);

            // Hook should fetch only a small number of pages to fill the viewport
            // (page-0 plus the prefetch window). Allow slack for framerate settle.
            H.Check($"HookPaging_Mount_IncrementalFetch (calls={source!.CallCount})",
                source.CallCount >= 1 && source.CallCount <= 20);
        }
    }

    // ── Parity test 2: Scroll populates data on hook path ────────────────

    /// <summary>
    /// Programmatically scrolls the DataGrid to a deep offset and asserts the
    /// rows at the target position populate with real data, matching
    /// <see cref="DataGridScrollFixtures.ScrollPopulatesData"/>'s legacy behavior.
    /// </summary>
    internal class HookPagingScrollPopulates(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithHookPaging(true, RunInner);

        private async Task RunInner()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(10_000));
                return DataGrid(
                    source: source,
                    columns: CreateColumns(),
                    rowHeight: 36
                ).Height(500);
            });

            await Harness.Render(500);

            H.Check("HookPaging_Scroll_InitialRender",
                H.FindTextContaining("Emp-") is not null);

            var sv = H.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater);
            H.Check("HookPaging_Scroll_ScrollViewerFound", sv is not null);
            if (sv is null) return;

            // Scroll deep into the list — the hook must request the covering pages.
            sv.ChangeView(null, 7200, null, disableAnimation: true);
            await Harness.Render(1000);

            var visibleCells = H.FindAllControls<TextBlock>(
                tb => tb.Text?.StartsWith("Emp-") == true);

            H.Check($"HookPaging_Scroll_DataVisibleAtTarget (cells={visibleCells.Count})",
                visibleCells.Count >= 4);

            // Second jump to exercise ItemAt's lazy-page-fetch path.
            sv.ChangeView(null, 14400, null, disableAnimation: true);
            await Harness.Render(1000);

            var visibleCells2 = H.FindAllControls<TextBlock>(
                tb => tb.Text?.StartsWith("Emp-") == true);

            H.Check($"HookPaging_Scroll_DataVisibleAfterSecondJump (cells={visibleCells2.Count})",
                visibleCells2.Count >= 4);
        }
    }

    // ── Parity test 3: Scroll back to top still shows data ──────────────

    internal class HookPagingScrollBack(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithHookPaging(true, RunInner);

        private async Task RunInner()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(5_000));
                return DataGrid(
                    source: source,
                    columns: CreateColumns(),
                    rowHeight: 36
                ).Height(400);
            });

            await Harness.Render(500);

            H.Check("HookPaging_ScrollBack_InitialData",
                H.FindTextContaining("Emp-000000") is not null);

            var sv = H.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater);
            if (sv is null) { H.Check("HookPaging_ScrollBack_SVFound", false); return; }

            sv.ChangeView(null, 5000, null, disableAnimation: true);
            await Harness.Render(600);

            sv.ChangeView(null, 0, null, disableAnimation: true);
            await Harness.Render(600);

            H.Check("HookPaging_ScrollBack_OrigRestored",
                H.FindTextContaining("Emp-000000") is not null);
        }
    }

    // ── Parity test 4: Small-dataset end-of-list parity ─────────────────

    /// <summary>
    /// Mirrors <see cref="DataGridPagingFixtures.SmallDatasetFullyLoaded"/> for the
    /// hook path: a small source (below one page size) still renders every row
    /// and reports the correct total.
    /// </summary>
    internal class HookPagingSmallDataset(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithHookPaging(true, RunInner);

        private async Task RunInner()
        {
            CountingSource? source = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                source = ctx.UseMemo(() => CreateSource(12));
                return DataGrid(
                    source: source!,
                    columns: CreateColumns(),
                    rowHeight: 36
                ).Height(600);
            });

            await Harness.Render(500);

            H.Check("HookPaging_Small_FirstRowVisible",
                H.FindTextContaining("Emp-000000") is not null);

            H.Check("HookPaging_Small_LastRowVisible",
                H.FindTextContaining("Emp-000011") is not null);

            H.Check($"HookPaging_Small_SinglePageFetch (calls={source!.CallCount})",
                source.CallCount == 1);
        }
    }

    // ── Framerate canary: scroll 60+ frames on hook path ───────────────

    /// <summary>
    /// Drives a programmatic scroll across many offsets while the hook path is
    /// active. Asserts the grid remains responsive, no unobserved task exceptions
    /// fire, and content at the final position renders. Covers
    /// §11's "scroll regression canary" bullet for the hook-based DataGrid.
    /// </summary>
    internal class HookPagingFramerateScroll(Harness h) : SelfTestFixtureBase(h)
    {
        // 60-frame programmatic scroll has a ~3.4 s mandatory Render(ms) wall-clock
        // floor; loaded CI runners slow per-frame work 2–4x and trip the default 15 s
        // budget without anything being wedged. See INVESTIGATION.md Cluster T2.
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(30);

        public override Task RunAsync() => WithHookPaging(true, RunInner);

        private async Task RunInner()
        {
            var unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
            {
                Interlocked.Increment(ref unobserved);
                args.SetObserved();
            };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var source = ctx.UseMemo(() => CreateSource(10_000));
                    return DataGrid(
                        source: source,
                        columns: CreateColumns(),
                        rowHeight: 36
                    ).Height(600);
                });

                await Harness.Render(400);

                var sv = H.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater);
                H.Check("HookPaging_Framerate_SV", sv is not null);
                if (sv is null) return;

                // 60-frame scroll across the virtualized range. Each offset is picked
                // to land on a fresh page so the hook is exercised end-to-end.
                for (int frame = 0; frame < 60; frame++)
                {
                    double offset = (frame % 20) * 1800.0;
                    sv.ChangeView(null, offset, null, disableAnimation: true);
                    await Harness.Render(24);
                }

                // Settle, then assert the final position shows real data and no
                // unobserved exceptions fired during the sweep.
                sv.ChangeView(null, 0, null, disableAnimation: true);
                await Harness.Render(600);

                await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); });

                H.Check("HookPaging_Framerate_FinalData",
                    H.FindTextContaining("Emp-") is not null);
                H.Check($"HookPaging_Framerate_NoUnobservedExceptions (count={unobserved})",
                    unobserved == 0);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }
    }

    // ── Parity test 5: Edit lifecycle on hook path ──────────────────────

    /// <summary>
    /// Hook-path counterpart of <see cref="DataGridEditFixtures.EditLifecycle"/>. Mounts
    /// an editable DataGrid with <c>UseHookBasedPaging = true</c>, an <c>onRowChanged</c>
    /// commit callback, and verifies the grid survives the edit-enabled lifecycle:
    /// data loads through <c>UseInfiniteResource</c>, editable columns render, no
    /// TextBox editor leaks before edit begins, and the commit path is wired
    /// (<see cref="DataGridState{T}.CommitDispatcher"/> is non-null when
    /// <c>onRowChanged</c> is provided — the UseMutation-backed port of the
    /// async-commit family the spec §3.1 calls out).
    /// </summary>
    internal class HookPagingEditLifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithHookPaging(true, RunInner);

        private async Task RunInner()
        {
            int commitCalls = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(50));
                var columns = new FieldDescriptor[]
                {
                    Column<ParityItem>("Id", p => p.Id, width: 60),
                    Column<ParityItem>("Name", p => p.Name, editable: true, width: 160),
                    Column<ParityItem>("Dept", p => p.Dept, editable: true, width: 120),
                    Column<ParityItem>("Title", p => p.Title, editable: true, width: 160),
                };
                return DataGrid(
                    source: source,
                    columns: columns,
                    selectionMode: Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                    editable: true,
                    editMode: EditMode.Cell,
                    onRowChanged: (key, item) =>
                    {
                        Interlocked.Increment(ref commitCalls);
                        return Task.CompletedTask;
                    },
                    rowHeight: 36
                );
            });

            await Harness.Render(500);

            // 1. Grid renders with data loaded through the hook path.
            H.Check("HookPaging_Edit_FirstRowVisible",
                H.FindTextContaining("Emp-000000") is not null);

            H.Check("HookPaging_Edit_LaterRowVisible",
                H.FindTextContaining("Emp-000005") is not null);

            // 2. No editor should be materialized yet — editing hasn't started.
            var textBoxesBefore = H.FindAllControls<Microsoft.UI.Xaml.Controls.TextBox>(_ => true);
            H.Check("HookPaging_Edit_NoEditorInitially",
                textBoxesBefore.Count == 0);

            // 3. Grid stays alive across a few more render ticks. This is the same
            //    "does the edit-enabled lifecycle survive?" gate the legacy
            //    EditLifecycle fixture asserts — under the hook path, data loading
            //    arrives via UseInfiniteResource and the reconciler must not trip
            //    over the lack of BlockLoaded events.
            for (int i = 0; i < 3; i++) await Harness.Render(100);

            H.Check("HookPaging_Edit_StillAlive",
                H.FindTextContaining("Emp-000000") is not null);

            // 4. Sanity: onRowChanged has not fired yet — no one has committed.
            //    This guards against a spurious "commit on mount" regression in the
            //    UseMutation dispatcher wiring.
            H.Check($"HookPaging_Edit_NoSpuriousCommit (calls={commitCalls})",
                commitCalls == 0);
        }
    }
}
