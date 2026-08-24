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
        Assert.Equal(new[] { "*" }, def.Rows.Select(rs => rs.ToString()));
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

    // ════════════════════════════════════════════════════════════════
    //  #671 — stabilized row/cell/expand modifier handlers
    //  (reference stability for the skip path + live-index resolution so a
    //   cached handler never fires against a stale row after a sort/mutation)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void StabilizedHandlers_Are_Reference_Stable_Per_Key_And_Distinct_Across_Keys()
    {
        // The whole point of #671: a given (row[, column]) slot must return the SAME delegate
        // instance across renders, so post-#665 ModifiersEqual (per-slot ReferenceEquals) is true
        // and the unchanged cell/row skips Update. Distinct keys must get distinct instances.
        var state = CreateState();
        var k1 = new RowKey("1");
        var k2 = new RowKey("2");

        Assert.Same(state.GetRowPointerHandler(k1), state.GetRowPointerHandler(k1));
        Assert.Same(state.GetExpandHandler(k1), state.GetExpandHandler(k1));
        Assert.Same(state.GetCellEditHandler(k1, "Name"), state.GetCellEditHandler(k1, "Name"));

        Assert.NotSame(state.GetRowPointerHandler(k1), state.GetRowPointerHandler(k2));
        Assert.NotSame(state.GetExpandHandler(k1), state.GetExpandHandler(k2));
        Assert.NotSame(state.GetCellEditHandler(k1, "Name"), state.GetCellEditHandler(k1, "Score"));
    }

    [Fact]
    public void ClearStabilizedHandlerCaches_Forces_Fresh_Instances()
    {
        var state = CreateState();
        var k1 = new RowKey("1");
        var rp = state.GetRowPointerHandler(k1);
        var ex = state.GetExpandHandler(k1);
        var ce = state.GetCellEditHandler(k1, "Name");

        state.ClearStabilizedHandlerCaches();

        Assert.NotSame(rp, state.GetRowPointerHandler(k1));
        Assert.NotSame(ex, state.GetExpandHandler(k1));
        Assert.NotSame(ce, state.GetCellEditHandler(k1, "Name"));
    }

    [Fact]
    public async Task RowPointerClick_Resolves_The_Live_Row_Index_After_A_Sort_Reorders_Rows()
    {
        // Stale-closure regression (the #721 bug class): a handler captured while row "3" sat at
        // index 2 must, after a re-sort moves "3" to index 0, still act on "3" — i.e. it resolves
        // the CURRENT index, never a captured one.
        var state = await CreateClientFallbackLoadedState(); // sorted by Id asc: 1,2,3
        var key3 = new RowKey("3");
        Assert.Equal(2, state.GetRowIndex(key3));

        // Grab the stabilized handler BEFORE the reorder (simulating a delegate cached on an
        // earlier render), then reorder so "3" lands at index 0.
        var handlerBefore = state.GetRowPointerHandler(key3);
        state.ToggleSort("Id"); // asc -> desc
        await state.LoadDataAsync(TestContext.Current.CancellationToken); // 3,2,1
        Assert.Equal(0, state.GetRowIndex(key3));

        // The cached delegate is the same instance (skip-stable) ...
        Assert.Same(handlerBefore, state.GetRowPointerHandler(key3));
        // ... and invoking its resolved action lands on "3"'s CURRENT position, not the stale 2.
        state.InvokeRowPointerClick(key3, ctrlKey: false, shiftKey: false);

        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal("3", state.FocusedKey?.Value);
        Assert.Contains(key3, state.SelectedKeys);
    }

    [Fact]
    public async Task CellEditClick_Begins_Edit_On_The_Correct_Cell_After_A_Sort_Reorders_Rows()
    {
        var state = await CreateClientFallbackLoadedState(); // 1,2,3
        var key1 = new RowKey("1");
        Assert.Equal(0, state.GetRowIndex(key1));

        var handlerBefore = state.GetCellEditHandler(key1, "Name");
        state.ToggleSort("Id"); // -> desc: 3,2,1
        await state.LoadDataAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, state.GetRowIndex(key1)); // "1" now last

        Assert.Same(handlerBefore, state.GetCellEditHandler(key1, "Name"));
        state.InvokeCellEditClick(key1, "Name");

        // Editing the right logical row ("1") + column ("Name"), resolved at the new index.
        Assert.True(state.IsEditing);
        Assert.Equal("1", state.EditingRowKey?.Value);
        Assert.Equal("Name", state.EditingColumnName);
        Assert.Equal(2, state.FocusedRowIndex);
    }

    [Fact]
    public async Task StabilizedHandlers_NoOp_For_A_Key_No_Longer_In_The_Set()
    {
        var state = await CreateClientFallbackLoadedState(); // 1,2,3
        var ghost = new RowKey("999"); // never present
        Assert.Equal(-1, state.GetRowIndex(ghost));

        // A handler for a departed/absent key resolves to index -1 and must no-op (no throw, no
        // focus/selection/edit side effects) — so a stale cached handler is harmless.
        state.InvokeRowPointerClick(ghost, ctrlKey: false, shiftKey: false);
        state.InvokeCellEditClick(ghost, "Name");

        Assert.False(state.IsEditing);
        Assert.Empty(state.SelectedKeys);
    }

    [Fact]
    public void ExpandHandler_Toggles_The_Keyed_Rows_Detail_State()
    {
        // The expand handler is index-free (keys on the row key), so it is inherently stable and
        // correct regardless of row movement. Verify the underlying toggle it routes to.
        var state = CreateState();
        var k2 = new RowKey("2");
        Assert.False(state.IsExpanded(k2));

        state.ToggleRowExpansion(k2); // the action the cached handler dispatches
        Assert.True(state.IsExpanded(k2));
        state.ToggleRowExpansion(k2);
        Assert.False(state.IsExpanded(k2));
    }

    // ── #759 dual-review required coverage: commit-routing + column dimension ──
    // The commit-in-flight path moved into CommitInFlightEditThroughDispatcher (routed via
    // CommitDispatcher) and the column-index resolution are the riskiest relocated logic; these
    // lock them. CommitDispatcher is null headless, so the existing suites never exercised it.

    [Fact]
    public async Task RowPointerClick_On_A_Different_Row_Commits_The_InFlight_Edit_Through_The_Dispatcher_Once()
    {
        var state = await CreateClientFallbackLoadedState(); // rows 1,2,3 (Id asc)
        var commits = new List<(RowKey Key, TestItem NewItem, TestItem? Orig)>();
        state.CommitDispatcher = (k, n, o) => commits.Add((k, n, o));

        // Begin editing row "1" (index 0), column "Name", with a pending new value.
        Assert.True(state.BeginEdit(0, 1));
        state.UpdateEditingValue("Alice2");

        // Click a DIFFERENT row ("2") — must commit row "1"'s in-flight edit exactly ONCE, routed
        // through CommitDispatcher, carrying the PRE-commit original-item snapshot for revert.
        state.InvokeRowPointerClick(new RowKey("2"), ctrlKey: false, shiftKey: false);

        Assert.Single(commits);
        Assert.Equal("1", commits[0].Key.Value);
        Assert.Equal("Alice", commits[0].Orig?.Name);   // pre-edit snapshot, not the new value
        Assert.Equal("Alice2", commits[0].NewItem.Name); // committed value
        Assert.False(state.IsEditing);                   // edit committed, not left dangling
    }

    [Fact]
    public async Task CellEditClick_On_A_New_Cell_Commits_The_InFlight_Edit_Through_The_Dispatcher_Once_Then_Begins()
    {
        var state = await CreateClientFallbackLoadedState(); // rows 1,2,3
        var commits = new List<(RowKey Key, TestItem NewItem, TestItem? Orig)>();
        state.CommitDispatcher = (k, n, o) => commits.Add((k, n, o));

        Assert.True(state.BeginEdit(0, 1)); // row "1", column "Name"
        state.UpdateEditingValue("Alice2");

        // Tap a different cell — commits the in-flight edit (once, via the dispatcher) BEFORE the new
        // edit begins.
        state.InvokeCellEditClick(new RowKey("2"), "Score");

        Assert.Single(commits);
        Assert.Equal("1", commits[0].Key.Value);
        Assert.Equal("Alice", commits[0].Orig?.Name);
        Assert.Equal("Alice2", commits[0].NewItem.Name);

        // ...and a NEW edit began on the tapped cell (row "2", column "Score").
        Assert.True(state.IsEditing);
        Assert.Equal("2", state.EditingRowKey?.Value);
        Assert.Equal("Score", state.EditingColumnName);
    }

    [Fact]
    public async Task CellEditClick_NoOps_For_An_Absent_Column_And_Resolves_The_Right_Column_By_Name_After_Reorder()
    {
        var state = await CreateClientFallbackLoadedState(); // rows 1,2,3; columns Id,Name,Score
        var key1 = new RowKey("1");

        // Absent column name -> the name->index lookup misses -> no-op (no edit begins).
        state.InvokeCellEditClick(key1, "Nope");
        Assert.False(state.IsEditing);

        // A cached cell handler resolves its column BY NAME at invocation, so after a column reorder
        // it still edits the same logical column ("Score"), not whatever now sits at Score's old
        // index. The handler instance also stays reference-stable across the reorder.
        var handlerBefore = state.GetCellEditHandler(key1, "Score");
        state.ReorderColumn(2, 0); // move Score (idx 2) to the front -> columns become Score,Id,Name
        Assert.Same(handlerBefore, state.GetCellEditHandler(key1, "Score"));

        state.InvokeCellEditClick(key1, "Score");
        Assert.True(state.IsEditing);
        Assert.Equal("1", state.EditingRowKey?.Value);
        Assert.Equal("Score", state.EditingColumnName); // correct column by name despite the reorder
    }
}
