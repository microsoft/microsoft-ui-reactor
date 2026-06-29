using System.Globalization;
using System.Linq;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the per-render allocation / LINQ caching added to DataGridState
/// (version counters, O(1) sort/filter/width lookups, the shared column-layout
/// cache, the visible-columns cache, and the index-based range selection).
/// Each test asserts the cached/memoized path produces results identical to the
/// previous LINQ-based behavior and that caches invalidate on the right mutations.
/// </summary>
public class DataGridPerfCacheTests
{
    // ── Test helpers (mirror DataGridStateAdditionalTests) ───────────

    private record TestItem(int Id, string Name, double Score);

    private sealed class TestDataSource : IDataSource<TestItem>
    {
        private readonly List<TestItem> _items;
        public TestDataSource(params TestItem[] items) => _items = new(items);

        public Task<DataPage<TestItem>> GetPageAsync(DataRequest request, CancellationToken ct = default)
            => Task.FromResult(new DataPage<TestItem>(_items, TotalCount: _items.Count));

        public RowKey GetRowKey(TestItem item) => new(item.Id.ToString(CultureInfo.InvariantCulture));
        public DataSourceCapabilities Capabilities => DataSourceCapabilities.None;
    }

    private static readonly FieldDescriptor[] TestColumns =
    [
        new FieldDescriptor
        {
            Name = "Id",
            FieldType = typeof(int),
            GetValue = obj => ((TestItem)obj).Id,
            IsReadOnly = true,
        },
        new FieldDescriptor
        {
            Name = "Name",
            FieldType = typeof(string),
            GetValue = obj => ((TestItem)obj).Name,
            SetValue = (obj, val) => ((TestItem)obj) with { Name = (string)(val ?? "") },
        },
        new FieldDescriptor
        {
            Name = "Score",
            FieldType = typeof(double),
            GetValue = obj => ((TestItem)obj).Score,
            SetValue = (obj, val) => ((TestItem)obj) with { Score = (double)(val ?? 0.0) },
            Width = 80,
            MinWidth = 50,
            MaxWidth = 200,
        },
    ];

    private static DataGridState<TestItem> CreateState(SelectionMode mode = SelectionMode.Multiple)
        => new(new TestDataSource(
            new TestItem(1, "Alice", 95),
            new TestItem(2, "Bob", 87),
            new TestItem(3, "Carol", 92)
        ), TestColumns, mode);

    // Loads via the client-side fallback path (source advertises no server sort/filter, so an
    // active sort forces "load all + sort locally"), which is the path that populates the internal
    // row-key cache scanned by the index-based range selection.
    private static async Task<DataGridState<TestItem>> CreateClientFallbackLoadedState(
        SelectionMode mode = SelectionMode.Multiple)
    {
        var state = CreateState(mode);
        state.ToggleSort("Id"); // ascending -> rows ordered 1,2,3
        await state.LoadDataAsync(TestContext.Current.CancellationToken);
        return state;
    }

    // ════════════════════════════════════════════════════════════════
    //  Version counters (cache keys) bump exactly on the right mutations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SortVersion_Bumps_On_Sort_Mutations_And_Is_Stable_On_Reads()
    {
        var state = CreateState();
        var v0 = state.SortVersion;

        // Pure reads must not bump the version (otherwise the memoized sort key
        // would be rebuilt every render).
        _ = state.GetSortDirection("Name");
        _ = state.Sorts;
        Assert.Equal(v0, state.SortVersion);

        state.ToggleSort("Name");            // None -> Asc
        var v1 = state.SortVersion;
        Assert.True(v1 > v0);

        state.ToggleSort("Name");            // Asc -> Desc
        var v2 = state.SortVersion;
        Assert.True(v2 > v1);

        state.ToggleSort("Name");            // Desc -> removed
        Assert.True(state.SortVersion > v2);
    }

    [Fact]
    public void FilterVersion_Bumps_On_Filter_Mutations_Only()
    {
        var state = CreateState();
        var v0 = state.FilterVersion;

        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "Al"));
        var v1 = state.FilterVersion;
        Assert.True(v1 > v0);

        // No-op clear of a non-existent filter must NOT bump.
        state.ClearFilter("DoesNotExist");
        Assert.Equal(v1, state.FilterVersion);

        state.ClearFilter("Name");
        var v2 = state.FilterVersion;
        Assert.True(v2 > v1);

        // No-op clear-all on an already-empty filter set must NOT bump.
        state.ClearAllFilters();
        Assert.Equal(v2, state.FilterVersion);

        state.SetFilter(new FilterDescriptor("Score", FilterOperator.GreaterThan, 90));
        state.ClearAllFilters();
        Assert.True(state.FilterVersion > v2);
    }

    [Fact]
    public void ColumnVersion_Bumps_On_Resize_Hide_Show_Reorder_Pin()
    {
        var state = CreateState();

        var v = state.ColumnVersion;
        state.ResizeColumn("Score", 100);
        Assert.True(state.ColumnVersion > v);

        v = state.ColumnVersion;
        state.HideColumn("Name");
        Assert.True(state.ColumnVersion > v);

        v = state.ColumnVersion;
        state.ShowColumn("Name");
        Assert.True(state.ColumnVersion > v);

        v = state.ColumnVersion;
        state.ReorderColumn(0, 2);
        Assert.True(state.ColumnVersion > v);

        v = state.ColumnVersion;
        state.PinColumn("Id", PinPosition.Left);
        Assert.True(state.ColumnVersion > v);
    }

    // ════════════════════════════════════════════════════════════════
    //  O(1) lookups produce the same answers as the previous LINQ scans
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetSortDirection_Matches_Linq_Over_Sorts()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        state.ToggleSort("Score", additive: true);
        state.ToggleSort("Score", additive: true); // Score: Asc -> Desc

        foreach (var col in new[] { "Id", "Name", "Score" })
        {
            var expected = state.Sorts
                .Where(s => s.Field == col)
                .Select(s => (SortDirection?)s.Direction)
                .FirstOrDefault();
            Assert.Equal(expected, state.GetSortDirection(col));
        }
    }

    [Fact]
    public void GetFilter_Matches_Linq_Over_Filters()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "Al"));
        state.SetFilter(new FilterDescriptor("Score", FilterOperator.GreaterThan, 90));

        foreach (var col in new[] { "Id", "Name", "Score" })
        {
            var expected = state.Filters.FirstOrDefault(f => f.Field == col);
            Assert.Equal(expected, state.GetFilter(col));
        }
    }

    [Fact]
    public void GetColumnWidth_Matches_Manual_Lookup_After_Resize()
    {
        var state = CreateState();
        state.ResizeColumn("Score", 150);
        state.ResizeColumn("Id", 70);

        // Independent reference mirroring the previous LINQ semantics: an explicit
        // resize override wins, else the descriptor's declared Width, else 120.
        var overrides = new Dictionary<string, double> { ["Score"] = 150, ["Id"] = 70 };
        foreach (var col in state.AllColumns)
        {
            double expected = overrides.TryGetValue(col.Name, out var w)
                ? w
                : (col.Width ?? 120);
            Assert.Equal(expected, state.GetColumnWidth(col.Name));
        }

        Assert.Equal(150, state.GetColumnWidth("Score"));
        Assert.Equal(70, state.GetColumnWidth("Id"));
        Assert.Equal(120, state.GetColumnWidth("Name")); // unset -> default
    }

    // ════════════════════════════════════════════════════════════════
    //  Column-layout cache (#125): shared, stable references; correct content
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetColumnLayout_Returns_Stable_References_Until_Column_Change()
    {
        var state = CreateState();
        var cols = state.Columns;

        var (w1, d1) = state.GetColumnLayout(cols, false, false, false);
        var (w2, d2) = state.GetColumnLayout(cols, false, false, false);

        // Same version + same shape -> identical cached instances (no per-render alloc).
        Assert.Same(w1, w2);
        Assert.Same(d1, d2);

        // A column mutation must invalidate the cache and yield fresh instances.
        state.ResizeColumn("Score", 100);
        var (w3, d3) = state.GetColumnLayout(state.Columns, false, false, false);
        Assert.NotSame(d1, d3);
        Assert.NotSame(w1, w3);

        // ...and the new layout reflects the resize.
        Assert.Equal(100, w3[2]);
        Assert.Equal("100", d3.Columns[2]);
    }

    [Fact]
    public void GetColumnLayout_Content_Matches_Manual_Construction_With_Shape()
    {
        var state = CreateState();
        var cols = state.Columns;

        // No leading/trailing columns.
        var (widths, def) = state.GetColumnLayout(cols, false, false, false);
        Assert.Equal(cols.Count, def.Columns.Length);
        Assert.Equal(new[] { "*" }, def.Rows);
        for (int c = 0; c < cols.Count; c++)
        {
            Assert.Equal(state.GetColumnWidth(cols[c].Name), widths[c]);
            Assert.Equal(
                state.GetColumnWidth(cols[c].Name).ToString(CultureInfo.InvariantCulture),
                def.Columns[c]);
        }

        // Full shape: row-detail (24) + select (40) leading, edit-actions (Auto) trailing.
        var (_, shaped) = state.GetColumnLayout(cols, true, true, true);
        Assert.Equal(cols.Count + 3, shaped.Columns.Length);
        Assert.Equal("24", shaped.Columns[0]);
        Assert.Equal("40", shaped.Columns[1]);
        for (int c = 0; c < cols.Count; c++)
            Assert.Equal(
                state.GetColumnWidth(cols[c].Name).ToString(CultureInfo.InvariantCulture),
                shaped.Columns[2 + c]);
        Assert.Equal("Auto", shaped.Columns[^1]);
    }

    // ════════════════════════════════════════════════════════════════
    //  Visible-columns cache (#127)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Columns_Cache_Reflects_Hide_Show_And_Is_Reused_Within_A_Version()
    {
        var state = CreateState();

        Assert.Equal(3, state.Columns.Count);
        Assert.Same(state.Columns, state.Columns); // repeated access -> no Where+ToList each time

        state.HideColumn("Score");
        var hidden = state.Columns;
        Assert.Equal(new[] { "Id", "Name" }, hidden.Select(c => c.Name).ToArray());
        Assert.DoesNotContain(hidden, c => c.Name == "Score");
        Assert.Same(hidden, state.Columns); // cached across repeated access

        state.ShowColumn("Score");
        Assert.Equal(new[] { "Id", "Name", "Score" }, state.Columns.Select(c => c.Name).ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Index-based range selection (#130)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ShiftClick_Without_VisibleOrder_Matches_Explicit_VisibleOrder()
    {
        // Path A: shift-click with no visibleOrder -> scans the internal row-key cache
        // (SelectRangeByKeyCache) instead of materializing it into a List<RowKey>.
        var a = await CreateClientFallbackLoadedState();
        var order = Enumerable.Range(0, a.ItemCount)
            .Select(i => new RowKey(a.GetRowKeyAt(i)!))
            .ToList();

        a.HandleRowClick(order[0]);
        a.HandleRowClick(order[^1], shiftKey: true); // no visibleOrder

        // Path B: identical clicks but with an explicit visibleOrder equal to the cache order.
        var b = await CreateClientFallbackLoadedState();
        b.HandleRowClick(order[0]);
        b.HandleRowClick(order[^1], shiftKey: true, visibleOrder: order);

        Assert.Equal(
            b.SelectedKeys.Select(k => k.Value).OrderBy(v => v),
            a.SelectedKeys.Select(k => k.Value).OrderBy(v => v));

        // And the full range really was selected (proves the cache was scanned, not ignored).
        Assert.Equal(a.ItemCount, a.SelectedKeys.Count);
    }

    [Fact]
    public async Task ShiftClick_KeyCache_Scan_NoOp_When_Anchor_Absent_From_Cache()
    {
        // Mirrors SelectRange's no-op-when-key-missing behavior on the index-scan path.
        var state = await CreateClientFallbackLoadedState();
        state.HandleRowClick(new RowKey("1")); // anchor = "1"

        var before = state.SelectedKeys.Select(k => k.Value).OrderBy(v => v).ToArray();
        state.HandleRowClick(new RowKey("999"), shiftKey: true); // "999" not in cache
        var after = state.SelectedKeys.Select(k => k.Value).OrderBy(v => v).ToArray();

        // "to" key absent -> range select no-ops; selection is unchanged.
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ShiftClick_Reversed_KeyCache_Scan_Selects_Same_Range_As_Forward()
    {
        // Anchor on the LAST row, shift-click the FIRST -> the index scan runs "backwards"
        // (start = Min, end = Max), so it must still select the whole inclusive range.
        var a = await CreateClientFallbackLoadedState();
        var order = Enumerable.Range(0, a.ItemCount)
            .Select(i => new RowKey(a.GetRowKeyAt(i)!))
            .ToList();

        a.HandleRowClick(order[^1]);
        a.HandleRowClick(order[0], shiftKey: true); // no visibleOrder -> reversed key-cache scan
        Assert.Equal(a.ItemCount, a.SelectedKeys.Count);

        // The same reversed clicks with an explicit visibleOrder select the identical set.
        var b = await CreateClientFallbackLoadedState();
        b.HandleRowClick(order[^1]);
        b.HandleRowClick(order[0], shiftKey: true, visibleOrder: order);

        Assert.Equal(
            b.SelectedKeys.Select(k => k.Value).OrderBy(v => v),
            a.SelectedKeys.Select(k => k.Value).OrderBy(v => v));
    }

    [Fact]
    public void SelectRange_Reversed_Missing_And_Empty_Behave_Like_Old_Loop()
    {
        var state = CreateState();
        var order = new List<RowKey> { new("1"), new("2"), new("3"), new("4") };

        // Reversed (from after to) selects the same inclusive range as forward (Min/Max).
        state.SelectRange(new RowKey("4"), new RowKey("2"), order);
        Assert.Equal(new[] { "2", "3", "4" },
            state.SelectedKeys.Select(k => k.Value).OrderBy(v => v).ToArray());

        // Missing "from" anchor -> no-op (early return before clearing), selection preserved.
        state.SelectRange(new RowKey("nope"), new RowKey("2"), order);
        Assert.Equal(new[] { "2", "3", "4" },
            state.SelectedKeys.Select(k => k.Value).OrderBy(v => v).ToArray());

        // Missing "to" target -> no-op.
        state.SelectRange(new RowKey("1"), new RowKey("nope"), order);
        Assert.Equal(new[] { "2", "3", "4" },
            state.SelectedKeys.Select(k => k.Value).OrderBy(v => v).ToArray());

        // Empty visibleOrder -> no-op.
        state.SelectRange(new RowKey("1"), new RowKey("4"), new List<RowKey>());
        Assert.Equal(new[] { "2", "3", "4" },
            state.SelectedKeys.Select(k => k.Value).OrderBy(v => v).ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  Column-layout cache (#125): keyed on the caller-supplied column list too
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetColumnLayout_Invalidates_When_Columns_Reference_Changes()
    {
        var state = CreateState();
        var colsA = state.Columns; // 3 columns
        var (wA, dA) = state.GetColumnLayout(colsA, false, false, false);
        Assert.Equal(3, wA.Length);

        // A different column-list reference (e.g. the app swapped el.Columns) with a different
        // count must NOT be served colsA's cached layout, even though no internal column mutation
        // bumped ColumnVersion. Returning the stale 3-wide arrays here would mis-size the grid (and
        // index out of range when the row renderer walks the new, shorter column set).
        var colsB = new List<FieldDescriptor> { state.AllColumns[0], state.AllColumns[1] };
        var (wB, dB) = state.GetColumnLayout(colsB, false, false, false);

        Assert.NotSame(wA, wB);
        Assert.NotSame(dA, dB);
        Assert.Equal(2, wB.Length);
        Assert.Equal(2, dB.Columns.Length);

        // Re-passing the original reference rebuilds for it again (cache now holds colsB).
        var (wA2, _) = state.GetColumnLayout(colsA, false, false, false);
        Assert.Equal(3, wA2.Length);
    }

    [Fact]
    public void GetColumnLayout_Rebuilds_When_Same_Reference_Is_Mutated_In_Place()
    {
        var state = CreateState();
        var live = new List<FieldDescriptor>(state.AllColumns); // 3 columns
        var (w1, _) = state.GetColumnLayout(live, false, false, false);
        Assert.Equal(3, w1.Length);

        // Same reference, but its element count changed (pathological in-place edit). The count
        // guard forces a rebuild so we never return an array sized for the old column count.
        live.RemoveAt(2);
        var (w2, _) = state.GetColumnLayout(live, false, false, false);

        Assert.NotSame(w1, w2);
        Assert.Equal(2, w2.Length);
    }

    [Fact]
    public void GetColumnLayout_SameCount_InPlace_Reorder_Serves_Cached_Layout_By_Design()
    {
        // Locks the reference-identity reliance documented in GetColumnLayout's <remarks>:
        // the cache keys on (ColumnVersion, shape, ReferenceEquals(columns), columns.Count).
        // A SAME-reference, SAME-count in-place REORDER of the passed list bumps NONE of those
        // (no internal mutator ran, so ColumnVersion is unchanged), so the cache is served
        // unchanged — by design, because detecting it would require per-render content hashing
        // that defeats the cache. Per the immutable-props convention a caller must instead pass a
        // NEW list reference. This test makes that silent edge an explicit, intentional contract.
        var state = CreateState();
        var live = new List<FieldDescriptor>(state.AllColumns); // 3 columns
        var (w1, d1) = state.GetColumnLayout(live, false, false, false);

        // Same reference, same count, different ORDER — and crucially no ColumnVersion bump.
        (live[0], live[2]) = (live[2], live[0]);
        var (w2, d2) = state.GetColumnLayout(live, false, false, false);

        // Cache hit: the identical array + definition references are returned (stale-by-design).
        Assert.Same(w1, w2);
        Assert.Same(d1, d2);

        // Sanity: passing a genuinely NEW reference (the supported path) DOES rebuild.
        var fresh = new List<FieldDescriptor>(live);
        var (w3, _) = state.GetColumnLayout(fresh, false, false, false);
        Assert.NotSame(w1, w3);
    }

    [Fact]
    public void GetColumnLayout_Invalidates_On_Reorder_Pin_And_ToggleVisibility()
    {
        var state = CreateState();
        var (_, d0) = state.GetColumnLayout(state.Columns, false, false, false);

        state.ReorderColumn(0, 2);
        var (_, dReorder) = state.GetColumnLayout(state.Columns, false, false, false);
        Assert.NotSame(d0, dReorder);

        state.PinColumn("Id", PinPosition.Left);
        var (_, dPin) = state.GetColumnLayout(state.Columns, false, false, false);
        Assert.NotSame(dReorder, dPin);

        state.ToggleColumnVisibility("Name"); // hide Name
        var (_, dToggle) = state.GetColumnLayout(state.Columns, false, false, false);
        Assert.NotSame(dPin, dToggle);
        Assert.Equal(state.Columns.Count, dToggle.Columns.Length);
    }

    // ════════════════════════════════════════════════════════════════
    //  Visible-columns snapshot (#127): a returned list is never mutated later
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Columns_Returns_A_Stable_Snapshot_Not_Mutated_By_Later_Column_Changes()
    {
        var state = CreateState();

        var before = state.Columns; // [Id, Name, Score]
        Assert.Equal(new[] { "Id", "Name", "Score" }, before.Select(c => c.Name).ToArray());

        // Mutations that bump ColumnVersion must not retroactively rewrite a previously returned
        // list (the old Where(...).ToList() handed out an independent snapshot each call). This
        // guards against the cache being a live shared buffer that Hide/Reorder edits in place.
        state.HideColumn("Name");
        state.ReorderColumn(0, 1);

        Assert.Equal(new[] { "Id", "Name", "Score" }, before.Select(c => c.Name).ToArray());

        // A fresh read reflects the new state and is a distinct instance.
        var after = state.Columns;
        Assert.NotSame(before, after);
        Assert.DoesNotContain(after, c => c.Name == "Name"); // hidden
    }

    // ════════════════════════════════════════════════════════════════
    //  O(1) lookups: unknown-column parity with the old LINQ FirstOrDefault
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Lookups_Match_Linq_FirstOrDefault_For_Unknown_Columns()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        state.SetFilter(new FilterDescriptor("Score", FilterOperator.GreaterThan, 90));

        // GetSortDirection / GetFilter on an unknown column return null, matching FirstOrDefault.
        Assert.Equal(
            state.Sorts.Where(s => s.Field == "Nope").Select(s => (SortDirection?)s.Direction).FirstOrDefault(),
            state.GetSortDirection("Nope"));
        Assert.Null(state.GetSortDirection("Nope"));

        Assert.Equal(state.Filters.FirstOrDefault(f => f.Field == "Nope"), state.GetFilter("Nope"));
        Assert.Null(state.GetFilter("Nope"));

        // GetColumnWidth on an unknown column falls back to the 120 default (no resize, no descriptor).
        Assert.Equal(120, state.GetColumnWidth("Nope"));
    }
}
