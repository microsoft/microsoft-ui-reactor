using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls.Validation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the DataGridState headless core — sort state, selection state,
/// column operations, and data loading.
/// </summary>
public class DataGridStateTests
{
    private record TestItem(int Id, string Name, string Category, double Price);

    private static ListDataSource<TestItem> CreateSource(int count = 20)
    {
        var items = Enumerable.Range(0, count).Select(i => new TestItem(
            Id: i,
            Name: $"Item {i}",
            Category: i % 3 == 0 ? "A" : i % 3 == 1 ? "B" : "C",
            Price: 10.0 + i * 1.5
        ));
        return new ListDataSource<TestItem>(items, t => (RowKey)t.Id);
    }

    private static IReadOnlyList<FieldDescriptor> CreateColumns()
    {
        return new FieldDescriptor[]
        {
            new() { Name = "Id", FieldType = typeof(int), GetValue = o => ((TestItem)o).Id, Width = 60 },
            new() { Name = "Name", FieldType = typeof(string), GetValue = o => ((TestItem)o).Name, Width = 200 },
            new() { Name = "Category", FieldType = typeof(string), GetValue = o => ((TestItem)o).Category, Width = 120 },
            new() { Name = "Price", FieldType = typeof(double), GetValue = o => ((TestItem)o).Price, Width = 100 },
        };
    }

    // ── Sort state transitions ────────────────────────────────────

    [Fact]
    public void ToggleSort_None_To_Ascending()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");

        Assert.Single(state.Sorts);
        Assert.Equal("Name", state.Sorts[0].Field);
        Assert.Equal(SortDirection.Ascending, state.Sorts[0].Direction);
    }

    [Fact]
    public void ToggleSort_Ascending_To_Descending()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        state.ToggleSort("Name");

        Assert.Single(state.Sorts);
        Assert.Equal(SortDirection.Descending, state.Sorts[0].Direction);
    }

    [Fact]
    public void ToggleSort_Descending_To_None()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        state.ToggleSort("Name");
        state.ToggleSort("Name");

        Assert.Empty(state.Sorts);
    }

    [Fact]
    public void ToggleSort_NonAdditive_Replaces_Previous()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        state.ToggleSort("Price"); // replaces Name sort

        Assert.Single(state.Sorts);
        Assert.Equal("Price", state.Sorts[0].Field);
    }

    [Fact]
    public void ToggleSort_Additive_Adds_MultiSort()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        state.ToggleSort("Price", additive: true);

        Assert.Equal(2, state.Sorts.Count);
        Assert.Equal("Name", state.Sorts[0].Field);
        Assert.Equal("Price", state.Sorts[1].Field);
    }

    [Fact]
    public void ToggleSort_Additive_Removes_Existing_After_Descending()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        state.ToggleSort("Price", additive: true);
        state.ToggleSort("Name", additive: true); // Name: Asc -> Desc
        state.ToggleSort("Name", additive: true); // Name: Desc -> removed

        Assert.Single(state.Sorts);
        Assert.Equal("Price", state.Sorts[0].Field);
    }

    [Fact]
    public void GetSortDirection_Returns_Null_When_Unsorted()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        Assert.Null(state.GetSortDirection("Name"));
    }

    [Fact]
    public void GetSortDirection_Returns_Direction_When_Sorted()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        Assert.Equal(SortDirection.Ascending, state.GetSortDirection("Name"));
    }

    [Fact]
    public void ToggleSort_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.ToggleSort("Name");
        Assert.Equal(1, changes);
    }

    // ── Selection: Single mode ───────────────────────────────────

    [Fact]
    public void SingleSelection_Click_Selects_Row()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);
        state.HandleRowClick((RowKey)5);

        Assert.Single(state.SelectedKeys);
        Assert.Contains((RowKey)5, state.SelectedKeys);
    }

    [Fact]
    public void SingleSelection_Click_Replaces_Previous()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)10);

        Assert.Single(state.SelectedKeys);
        Assert.Contains((RowKey)10, state.SelectedKeys);
        Assert.DoesNotContain((RowKey)5, state.SelectedKeys);
    }

    [Fact]
    public void SingleSelection_Sets_AnchorKey()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);
        state.HandleRowClick((RowKey)7);

        Assert.Equal((RowKey)7, state.AnchorKey);
    }

    [Fact]
    public void SingleSelection_Sets_FocusedKey()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);
        state.HandleRowClick((RowKey)7);

        Assert.Equal((RowKey)7, state.FocusedKey);
    }

    // ── Selection: Multiple mode ─────────────────────────────────

    [Fact]
    public void MultiSelection_Click_Replaces_All()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)10); // plain click replaces

        Assert.Single(state.SelectedKeys);
        Assert.Contains((RowKey)10, state.SelectedKeys);
    }

    [Fact]
    public void MultiSelection_CtrlClick_Toggles()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)10, ctrlKey: true);

        Assert.Equal(2, state.SelectedKeys.Count);
        Assert.Contains((RowKey)5, state.SelectedKeys);
        Assert.Contains((RowKey)10, state.SelectedKeys);

        // Ctrl+click again deselects
        state.HandleRowClick((RowKey)5, ctrlKey: true);
        Assert.Single(state.SelectedKeys);
        Assert.DoesNotContain((RowKey)5, state.SelectedKeys);
    }

    [Fact]
    public void MultiSelection_ShiftClick_Range()
    {
        var visibleOrder = Enumerable.Range(0, 20).Select(i => (RowKey)i).ToList();
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.HandleRowClick((RowKey)3);
        state.HandleRowClick((RowKey)7, shiftKey: true, visibleOrder: visibleOrder);

        Assert.Equal(5, state.SelectedKeys.Count);
        for (int i = 3; i <= 7; i++)
            Assert.Contains((RowKey)i, state.SelectedKeys);
    }

    [Fact]
    public void MultiSelection_SelectAll()
    {
        var allKeys = Enumerable.Range(0, 20).Select(i => (RowKey)i).ToList();
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.SelectAll(allKeys);

        Assert.Equal(20, state.SelectedKeys.Count);
    }

    [Fact]
    public void MultiSelection_ClearSelection()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        state.ClearSelection();

        Assert.Empty(state.SelectedKeys);
        Assert.Null(state.AnchorKey);
    }

    [Fact]
    public void None_Mode_Ignores_Clicks()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.HandleRowClick((RowKey)5);

        Assert.Empty(state.SelectedKeys);
    }

    [Fact]
    public void IsSelected_Returns_Correct_Value()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);
        state.HandleRowClick((RowKey)5);

        Assert.True(state.IsSelected((RowKey)5));
        Assert.False(state.IsSelected((RowKey)10));
    }

    [Fact]
    public void Selection_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.HandleRowClick((RowKey)5);
        Assert.Equal(1, changes);
    }

    // ── SetSelectionMode (issue #872 — reactive selection mode) ───

    [Fact]
    public void SelectionMode_Getter_Reflects_Constructor_Value()
    {
        Assert.Equal(SelectionMode.None,
            new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None).SelectionMode);
        Assert.Equal(SelectionMode.Single,
            new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single).SelectionMode);
        Assert.Equal(SelectionMode.Multiple,
            new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple).SelectionMode);
    }

    [Fact]
    public void SetSelectionMode_Single_To_Multiple_Enables_CtrlMultiSelect()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);

        // Baseline: in Single mode, ctrl-toggling two rows does NOT accumulate — it replaces.
        // This makes the post-widen accumulation assertion meaningful rather than always-true.
        state.HandleRowClick((RowKey)5, ctrlKey: true);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        Assert.Single(state.SelectedKeys);

        var changes = 0;
        state.StateChanged += () => changes++;
        state.SetSelectionMode(SelectionMode.Multiple);

        Assert.Equal(SelectionMode.Multiple, state.SelectionMode);
        Assert.True(changes > 0); // a mode change raises StateChanged

        // Now ctrl-toggling two distinct rows selects BOTH (multi-select is live).
        state.ClearSelection();
        state.HandleRowClick((RowKey)5, ctrlKey: true);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        Assert.Equal(2, state.SelectedKeys.Count);
        Assert.Contains((RowKey)5, state.SelectedKeys);
        Assert.Contains((RowKey)10, state.SelectedKeys);
    }

    [Fact]
    public void SetSelectionMode_Single_To_Multiple_Preserves_Existing_Selection()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Single);
        state.HandleRowClick((RowKey)5);
        Assert.Single(state.SelectedKeys);

        // Widening must not trim the current selection.
        state.SetSelectionMode(SelectionMode.Multiple);
        Assert.Contains((RowKey)5, state.SelectedKeys);
    }

    [Fact]
    public void SetSelectionMode_Multiple_To_Single_Trims_To_Anchor()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)10, ctrlKey: true); // anchor becomes 10
        Assert.Equal(2, state.SelectedKeys.Count);
        var versionBefore = state.SelectionVersion;

        state.SetSelectionMode(SelectionMode.Single);

        Assert.Equal(SelectionMode.Single, state.SelectionMode);
        Assert.Single(state.SelectedKeys);
        Assert.Contains((RowKey)10, state.SelectedKeys); // the anchor is kept
        Assert.True(state.SelectionVersion > versionBefore);
    }

    [Fact]
    public void SetSelectionMode_Multiple_To_None_Clears_Selection_And_Anchor()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        var versionBefore = state.SelectionVersion;

        state.SetSelectionMode(SelectionMode.None);

        Assert.Equal(SelectionMode.None, state.SelectionMode);
        Assert.Empty(state.SelectedKeys);
        Assert.Null(state.AnchorKey);
        Assert.True(state.SelectionVersion > versionBefore);

        // None mode now ignores further clicks.
        state.HandleRowClick((RowKey)7, ctrlKey: true);
        Assert.Empty(state.SelectedKeys);
    }

    [Fact]
    public void SetSelectionMode_Same_Mode_Is_NoOp()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        var versionBefore = state.SelectionVersion;
        var changes = 0;
        state.StateChanged += () => changes++;

        state.SetSelectionMode(SelectionMode.Multiple);

        Assert.Equal(0, changes);
        Assert.Equal(versionBefore, state.SelectionVersion);
        Assert.Equal(2, state.SelectedKeys.Count);
    }

    [Fact]
    public void SetSelectionMode_Multiple_To_Single_Fallback_Keeps_One_When_Anchor_Not_Selected()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.Multiple);
        // Build a 2-key selection whose AnchorKey/FocusedKey are NOT in the set: click 5,
        // ctrl-add 8, ctrl-add 10, then ctrl-remove 10 (anchor+focus stay on the now-deselected 10).
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)8, ctrlKey: true);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        Assert.Equal(2, state.SelectedKeys.Count);
        Assert.DoesNotContain((RowKey)10, state.SelectedKeys);

        state.SetSelectionMode(SelectionMode.Single);

        // Neither anchor nor focus is a valid selected key, so the fallback keeps exactly one
        // of the still-selected keys (and nothing outside the set).
        Assert.Single(state.SelectedKeys);
        Assert.True(state.IsSelected((RowKey)5) || state.IsSelected((RowKey)8));
    }

    [Fact]
    public async Task SetSelectionMode_Multiple_To_Single_Prefers_FocusedKey_When_Anchor_Not_Selected()
    {
        var state = new DataGridState<TestItem>(CreateSource(20), CreateColumns(), SelectionMode.Multiple);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // sel = {5, 8}; AnchorKey ends on the deselected row 10, then move keyboard focus onto the
        // still-selected row 5 so the anchor and focused keys diverge.
        state.HandleRowClick((RowKey)5);
        state.HandleRowClick((RowKey)8, ctrlKey: true);
        state.HandleRowClick((RowKey)10, ctrlKey: true);
        state.HandleRowClick((RowKey)10, ctrlKey: true); // deselect 10; anchor/focus remain on 10
        state.SetFocus(5, 0);                             // FocusedKey -> row 5 (selected); anchor stays 10
        Assert.Equal(2, state.SelectedKeys.Count);

        state.SetSelectionMode(SelectionMode.Single);

        // Anchor (10) is not selected, so the focused-and-selected key (5) is kept over the other.
        Assert.Single(state.SelectedKeys);
        Assert.True(state.IsSelected((RowKey)5));
        Assert.False(state.IsSelected((RowKey)8));
    }

    // ── Column operations ────────────────────────────────────────

    [Fact]
    public void GetColumnWidth_Returns_Descriptor_Width()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        Assert.Equal(60, state.GetColumnWidth("Id"));
        Assert.Equal(200, state.GetColumnWidth("Name"));
    }

    [Fact]
    public void GetColumnWidth_Returns_Default_For_Unknown()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        Assert.Equal(120, state.GetColumnWidth("NonExistent"));
    }

    [Fact]
    public void ResizeColumn_Updates_Width()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.ResizeColumn("Name", 300);
        Assert.Equal(300, state.GetColumnWidth("Name"));
    }

    [Fact]
    public void ResizeColumn_Respects_MinWidth()
    {
        var columns = new FieldDescriptor[]
        {
            new() { Name = "Name", FieldType = typeof(string), GetValue = o => "", MinWidth = 80 },
        };
        var state = new DataGridState<TestItem>(CreateSource(), columns, SelectionMode.None);
        state.ResizeColumn("Name", 20); // below min
        Assert.Equal(80, state.GetColumnWidth("Name"));
    }

    [Fact]
    public void ReorderColumn_Moves_Column()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        // Original: Id, Name, Category, Price
        state.ReorderColumn(0, 2);
        // Expected: Name, Category, Id, Price
        Assert.Equal("Name", state.Columns[0].Name);
        Assert.Equal("Category", state.Columns[1].Name);
        Assert.Equal("Id", state.Columns[2].Name);
    }

    [Fact]
    public void ResizeColumn_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.ResizeColumn("Name", 250);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ResizeColumn_Respects_MaxWidth()
    {
        var columns = new FieldDescriptor[]
        {
            new() { Name = "Name", FieldType = typeof(string), GetValue = o => "", MaxWidth = 400 },
        };
        var state = new DataGridState<TestItem>(CreateSource(), columns, SelectionMode.None);
        state.ResizeColumn("Name", 500); // above max
        Assert.Equal(400, state.GetColumnWidth("Name"));
    }

    [Fact]
    public void ResizeColumn_Multiple_Columns_Independent()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        // Resize two columns independently
        state.ResizeColumn("Id", 100);
        state.ResizeColumn("Name", 350);

        // Each column retains its own width
        Assert.Equal(100, state.GetColumnWidth("Id"));
        Assert.Equal(350, state.GetColumnWidth("Name"));

        // Unresized columns keep their descriptor default
        Assert.Equal(120, state.GetColumnWidth("Category"));
    }

    [Fact]
    public void ResizeColumn_Successive_Resizes_Overwrite()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        state.ResizeColumn("Name", 100);
        Assert.Equal(100, state.GetColumnWidth("Name"));

        state.ResizeColumn("Name", 250);
        Assert.Equal(250, state.GetColumnWidth("Name"));

        state.ResizeColumn("Name", 175);
        Assert.Equal(175, state.GetColumnWidth("Name"));
    }

    // ── Page-tracking data source ──────────────────────────────

    /// <summary>
    /// Wraps a ListDataSource and tracks every GetPageAsync call — recording
    /// the requested PageSize and cumulative items returned. Used to prove
    /// whether loading is eager (one giant page) or incremental (small blocks).
    /// </summary>
    private class PageTrackingSource<TItem> : IDataSource<TItem>
    {
        private readonly IDataSource<TItem> _inner;
        public int CallCount { get; private set; }
        public int TotalItemsFetched { get; private set; }
        public int LargestPageRequested { get; private set; }
        public List<int> RequestedPageSizes { get; } = new();

        public PageTrackingSource(IDataSource<TItem> inner) => _inner = inner;

        public DataSourceCapabilities Capabilities => _inner.Capabilities;
        public RowKey GetRowKey(TItem item) => _inner.GetRowKey(item);

        public async Task<DataPage<TItem>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedPageSizes.Add(request.PageSize);
            if (request.PageSize > LargestPageRequested)
                LargestPageRequested = request.PageSize;

            var page = await _inner.GetPageAsync(request, cancellationToken);
            TotalItemsFetched += page.Items.Count;
            return page;
        }
    }

    // ── Data loading ─────────────────────────────────────────────

    [Fact]
    public async Task LoadDataAsync_Populates_Items()
    {
        var state = new DataGridState<TestItem>(CreateSource(50), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        Assert.Equal(50, state.ItemCount);
        Assert.Equal(50, state.TotalCount);
        Assert.False(state.IsLoading);
    }

    [Fact]
    public async Task LoadDataAsync_Uses_Incremental_Paging()
    {
        // Given a data source with 10,000 items
        var inner = CreateSource(10_000);
        var tracking = new PageTrackingSource<TestItem>(inner);
        var state = new DataGridState<TestItem>(tracking, CreateColumns(), SelectionMode.None);

        // When we load data
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // ItemCount and TotalCount both reflect the full dataset
        Assert.Equal(10_000, state.TotalCount);
        Assert.Equal(10_000, state.ItemCount);

        // Only a small page was fetched — NOT all 10,000 items
        Assert.True(tracking.LargestPageRequested <= 100,
            $"Expected incremental page size <= 100, but largest request was {tracking.LargestPageRequested}");
        Assert.True(tracking.TotalItemsFetched <= 100,
            $"Expected <= 100 items fetched initially, but got {tracking.TotalItemsFetched}");
    }

    [Fact]
    public async Task LoadDataAsync_With_Sort_Applies_To_Request()
    {
        var state = new DataGridState<TestItem>(CreateSource(10), CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // Items should be sorted by Name ascending
        for (int i = 1; i < state.LoadedItems.Count; i++)
        {
            Assert.True(
                string.Compare(state.LoadedItems[i - 1].Name, state.LoadedItems[i].Name,
                    StringComparison.Ordinal) <= 0);
        }
    }

    [Fact]
    public async Task LoadDataAsync_Fires_StateChanged_Multiple_Times()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        int changes = 0;
        state.StateChanged += () => changes++;

        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // At least twice: once for IsLoading=true, once for IsLoading=false.
        // In paged mode, BlockLoaded also fires StateChanged.
        Assert.True(changes >= 2, $"Expected >= 2 StateChanged fires, got {changes}");
    }

    // ── Focus navigation ─────────────────────────────────────────

    [Fact]
    public async Task SetFocus_Sets_RowAndCol()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(3, 1);

        Assert.Equal(3, state.FocusedRowIndex);
        Assert.Equal(1, state.FocusedColIndex);
        Assert.Equal((RowKey)3, state.FocusedKey);
    }

    [Fact]
    public async Task SetFocus_Clamps_To_Bounds()
    {
        var state = new DataGridState<TestItem>(CreateSource(5), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(100, 100);

        Assert.Equal(4, state.FocusedRowIndex); // clamped to last row
        Assert.Equal(3, state.FocusedColIndex); // clamped to last col (4 columns)
    }

    [Fact]
    public async Task MoveFocus_Down()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 0);
        state.MoveFocus(1, 0);

        Assert.Equal(1, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task MoveFocus_Right()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 0);
        state.MoveFocus(0, 1);

        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(1, state.FocusedColIndex);
    }

    [Fact]
    public async Task MoveFocus_Initializes_When_No_Focus()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.MoveFocus(1, 0); // no focus yet — should start at (0,0)

        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusHome_Goes_To_First_Column()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(2, 3);
        state.FocusHome();

        Assert.Equal(2, state.FocusedRowIndex); // same row
        Assert.Equal(0, state.FocusedColIndex); // first column
    }

    [Fact]
    public async Task FocusEnd_Goes_To_Last_Column()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(2, 0);
        state.FocusEnd();

        Assert.Equal(2, state.FocusedRowIndex); // same row
        Assert.Equal(3, state.FocusedColIndex); // last column (4 cols -> index 3)
    }

    [Fact]
    public async Task FocusNextCell_Wraps_To_Next_Row()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 3); // last column
        var moved = state.FocusNextCell();

        Assert.True(moved);
        Assert.Equal(1, state.FocusedRowIndex); // wrapped to next row
        Assert.Equal(0, state.FocusedColIndex); // first column
    }

    [Fact]
    public async Task FocusPrevCell_Wraps_To_Previous_Row()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(1, 0); // first column, second row
        var moved = state.FocusPrevCell();

        Assert.True(moved);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(3, state.FocusedColIndex); // last col of prev row
    }

    [Fact]
    public async Task FocusNextCell_Returns_False_At_End()
    {
        var state = new DataGridState<TestItem>(CreateSource(2), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(1, 3); // last cell
        var moved = state.FocusNextCell();

        Assert.False(moved);
    }

    [Fact]
    public async Task FocusPrevCell_Returns_False_At_Start()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 0); // first cell
        var moved = state.FocusPrevCell();

        Assert.False(moved);
    }

    [Fact]
    public async Task SetFocus_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.SetFocus(1, 1);
        Assert.Equal(1, changes);
    }

    // ── Editing operations ───────────────────────────────────────

    private static IReadOnlyList<FieldDescriptor> CreateEditableColumns()
    {
        return new FieldDescriptor[]
        {
            new() { Name = "Id", FieldType = typeof(int), GetValue = o => ((TestItem)o).Id, Width = 60, IsReadOnly = true },
            new()
            {
                Name = "Name", FieldType = typeof(string),
                GetValue = o => ((TestItem)o).Name, Width = 200,
                SetValue = (owner, val) => ((TestItem)owner) with { Name = (string)(val ?? "") },
            },
            new()
            {
                Name = "Category", FieldType = typeof(string),
                GetValue = o => ((TestItem)o).Category, Width = 120,
                SetValue = (owner, val) => ((TestItem)owner) with { Category = (string)(val ?? "") },
            },
            new()
            {
                Name = "Price", FieldType = typeof(double),
                GetValue = o => ((TestItem)o).Price, Width = 100,
                SetValue = (owner, val) => ((TestItem)owner) with { Price = (double)(val ?? 0.0) },
            },
        };
    }

    [Fact]
    public async Task BeginEdit_On_ReadOnly_Column_Returns_False()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 0); // Id column (read-only)
        var result = state.BeginEdit();

        Assert.False(result);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task BeginEdit_On_Editable_Column_Starts_Editing()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1); // Name column (editable)
        var result = state.BeginEdit();

        Assert.True(result);
        Assert.True(state.IsEditing);
        Assert.Equal((RowKey)0, state.EditingRowKey);
        Assert.Equal("Name", state.EditingColumnName);
        Assert.Equal("Item 0", state.EditingValue);
    }

    [Fact]
    public async Task UpdateEditingValue_Changes_Pending_Value()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("New Name");

        Assert.Equal("New Name", state.EditingValue);
    }

    [Fact]
    public async Task CommitEdit_Applies_Change_To_Loaded_Items()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Updated Name");
        var result = state.CommitEdit();

        Assert.NotNull(result);
        Assert.Equal((RowKey)0, result!.Value.Key);
        Assert.Equal("Updated Name", result.Value.NewItem.Name);
        Assert.Equal("Updated Name", state.LoadedItems[0].Name);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task CommitEdit_Returns_New_Record_For_Immutable()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var originalItem = state.LoadedItems[0];
        state.SetFocus(0, 2); // Category column
        state.BeginEdit();
        state.UpdateEditingValue("NewCategory");
        var result = state.CommitEdit();

        Assert.NotNull(result);
        // TestItem is a record — SetValue creates a new instance
        Assert.NotSame(originalItem, result!.Value.NewItem);
        Assert.Equal("NewCategory", result.Value.NewItem.Category);
        // Other properties preserved
        Assert.Equal(originalItem.Id, result.Value.NewItem.Id);
        Assert.Equal(originalItem.Name, result.Value.NewItem.Name);
    }

    [Fact]
    public async Task CancelEdit_Discards_Changes()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var originalName = state.LoadedItems[0].Name;
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Should Be Discarded");
        state.CancelEdit();

        Assert.False(state.IsEditing);
        Assert.Equal(originalName, state.LoadedItems[0].Name); // unchanged
    }

    [Fact]
    public async Task CommitAndMoveNext_Commits_And_Advances_Focus()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1); // Name column
        state.BeginEdit();
        state.UpdateEditingValue("Edited");
        var result = state.CommitAndMoveNext();

        Assert.NotNull(result);
        Assert.Equal("Edited", result!.Value.NewItem.Name);
        Assert.False(state.IsEditing);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex); // moved to next column
    }

    [Fact]
    public async Task BeginEdit_By_RowCol_Index()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var result = state.BeginEdit(2, 1); // row 2, Name column

        Assert.True(result);
        Assert.Equal((RowKey)2, state.EditingRowKey);
        Assert.Equal("Name", state.EditingColumnName);
        Assert.Equal("Item 2", state.EditingValue);
    }

    [Fact]
    public async Task Editing_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.SetFocus(0, 1);
        changes = 0; // reset after focus change
        state.BeginEdit();
        Assert.Equal(1, changes);

        // UpdateEditingValue does NOT fire StateChanged — the editor manages its own display.
        state.UpdateEditingValue("New");
        Assert.Equal(1, changes); // unchanged

        state.CommitEdit();
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task GetRowIndex_Returns_Correct_Index()
    {
        var state = new DataGridState<TestItem>(CreateSource(10), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, state.GetRowIndex((RowKey)5));
        Assert.Equal(-1, state.GetRowIndex((RowKey)999));
    }

    // ── Push-down vs client-side sort ───────────────────────────

    /// <summary>
    /// A data source that does NOT declare ServerSort capability.
    /// Returns items in insertion order regardless of sort requests.
    /// </summary>
    private class NoServerSortSource : IDataSource<TestItem>
    {
        private readonly List<TestItem> _items;
        private readonly Func<TestItem, RowKey> _getRowKey;
        public DataRequest? LastRequest { get; private set; }

        public NoServerSortSource(IEnumerable<TestItem> items, Func<TestItem, RowKey> getRowKey)
        {
            _items = new List<TestItem>(items);
            _getRowKey = getRowKey;
        }

        // Deliberately omit ServerSort — the grid must sort client-side.
        public DataSourceCapabilities Capabilities => DataSourceCapabilities.ServerCount;

        public RowKey GetRowKey(TestItem item) => _getRowKey(item);

        public Task<DataPage<TestItem>> GetPageAsync(DataRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            // Always return items in insertion order — ignores any Sort in request.
            return Task.FromResult(new DataPage<TestItem>(_items, null, _items.Count));
        }
    }

    [Fact]
    public async Task PushDown_Sort_Passed_To_Source_When_ServerSort_Capable()
    {
        // ListDataSource declares ServerSort — the sort descriptors should be in the request.
        var source = CreateSource(10);
        var state = new DataGridState<TestItem>(source, CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // Verify the source applied the sort (data comes back sorted by Name asc)
        for (int i = 1; i < state.LoadedItems.Count; i++)
        {
            Assert.True(
                string.Compare(state.LoadedItems[i - 1].Name, state.LoadedItems[i].Name,
                    StringComparison.Ordinal) <= 0,
                $"Expected server-sorted: '{state.LoadedItems[i - 1].Name}' <= '{state.LoadedItems[i].Name}'");
        }
    }

    [Fact]
    public async Task ClientSide_Sort_Applied_When_Source_Lacks_ServerSort()
    {
        // Items deliberately NOT in Name order: "Item 9", "Item 0", "Item 5", ...
        var items = new[]
        {
            new TestItem(9, "Zebra", "A", 9.0),
            new TestItem(0, "Apple", "B", 1.0),
            new TestItem(5, "Mango", "C", 5.0),
            new TestItem(3, "Banana", "A", 3.0),
        };
        var source = new NoServerSortSource(items, t => (RowKey)t.Id);
        var state = new DataGridState<TestItem>(source, CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name");
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // The source returns unsorted data, but DataGridState should sort client-side.
        Assert.Equal("Apple", state.LoadedItems[0].Name);
        Assert.Equal("Banana", state.LoadedItems[1].Name);
        Assert.Equal("Mango", state.LoadedItems[2].Name);
        Assert.Equal("Zebra", state.LoadedItems[3].Name);

        // The sort should NOT have been pushed to the source (no Sort in request).
        Assert.Null(source.LastRequest!.Sort);
    }

    [Fact]
    public async Task ClientSide_Sort_Descending_When_Source_Lacks_ServerSort()
    {
        var items = new[]
        {
            new TestItem(0, "Apple", "B", 1.0),
            new TestItem(3, "Banana", "A", 3.0),
            new TestItem(5, "Mango", "C", 5.0),
        };
        var source = new NoServerSortSource(items, t => (RowKey)t.Id);
        var state = new DataGridState<TestItem>(source, CreateColumns(), SelectionMode.None);
        state.ToggleSort("Name"); // Ascending
        state.ToggleSort("Name"); // Descending
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Mango", state.LoadedItems[0].Name);
        Assert.Equal("Banana", state.LoadedItems[1].Name);
        Assert.Equal("Apple", state.LoadedItems[2].Name);
    }

    // ── Filter operations ───────────────────────────────────────

    [Fact]
    public void SetFilter_Adds_Filter()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Equals, "Item 5"));

        Assert.Single(state.Filters);
        Assert.Equal("Name", state.Filters[0].Field);
    }

    [Fact]
    public void SetFilter_Replaces_Existing_On_Same_Field()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Equals, "Item 5"));
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "Item"));

        Assert.Single(state.Filters);
        Assert.Equal(FilterOperator.Contains, state.Filters[0].Operator);
    }

    [Fact]
    public void ClearFilter_Removes_Filter()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Equals, "Item 5"));
        state.ClearFilter("Name");

        Assert.Empty(state.Filters);
    }

    [Fact]
    public void ClearAllFilters_Removes_All()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Equals, "a"));
        state.SetFilter(new FilterDescriptor("Category", FilterOperator.Equals, "A"));
        state.ClearAllFilters();

        Assert.Empty(state.Filters);
    }

    [Fact]
    public void GetFilter_Returns_Active_Filter()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "test"));

        var filter = state.GetFilter("Name");
        Assert.NotNull(filter);
        Assert.Equal(FilterOperator.Contains, filter!.Operator);
        Assert.Equal("test", filter.Value);
    }

    [Fact]
    public void GetFilter_Returns_Null_For_No_Filter()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        Assert.Null(state.GetFilter("Name"));
    }

    [Fact]
    public void SetFilter_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Equals, "A"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Filter_Applied_To_LoadData()
    {
        var state = new DataGridState<TestItem>(CreateSource(20), CreateColumns(), SelectionMode.None);
        state.SetFilter(new FilterDescriptor("Category", FilterOperator.Equals, "A"));
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // ListDataSource supports ServerFilter, only Category="A" items returned
        Assert.True(state.LoadedItems.Count < 20);
        Assert.All(state.LoadedItems, item => Assert.Equal("A", item.Category));
    }

    // ── Row-mode editing ────────────────────────────────────────

    [Fact]
    public async Task BeginRowEdit_Activates_All_Editable_Cells()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var result = state.BeginRowEdit(0);

        Assert.True(result);
        Assert.True(state.IsRowEditing);
        Assert.True(state.IsEditing);
        Assert.Equal((RowKey)0, state.EditingRowKey);

        // Should have pending values for all editable columns (Name, Category, Price)
        // but NOT for Id (read-only)
        Assert.Equal("Item 0", state.GetRowEditValue("Name"));
        Assert.NotNull(state.GetRowEditValue("Category"));
        Assert.NotNull(state.GetRowEditValue("Price"));
        Assert.Null(state.GetRowEditValue("Id")); // read-only
    }

    [Fact]
    public async Task BeginRowEdit_Returns_False_On_Invalid_Index()
    {
        var state = new DataGridState<TestItem>(CreateSource(5), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        Assert.False(state.BeginRowEdit(-1));
        Assert.False(state.BeginRowEdit(100));
        Assert.False(state.IsRowEditing);
    }

    [Fact]
    public async Task UpdateRowEditValue_Changes_Pending_Value()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", "New Name");
        state.UpdateRowEditValue("Price", 99.99);

        Assert.Equal("New Name", state.GetRowEditValue("Name"));
        Assert.Equal(99.99, state.GetRowEditValue("Price"));
    }

    [Fact]
    public async Task CommitRowEdit_Applies_All_Changes()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", "Updated Name");
        state.UpdateRowEditValue("Category", "Z");
        state.UpdateRowEditValue("Price", 100.0);
        var result = state.CommitRowEdit();

        Assert.NotNull(result);
        Assert.Equal("Updated Name", result!.Value.NewItem.Name);
        Assert.Equal("Z", result.Value.NewItem.Category);
        Assert.Equal(100.0, result.Value.NewItem.Price);
        Assert.Equal(0, result.Value.NewItem.Id); // unchanged

        // Verify in-memory state updated
        Assert.Equal("Updated Name", state.LoadedItems[0].Name);
        Assert.False(state.IsRowEditing);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task CancelRowEdit_Discards_All_Changes()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var originalName = state.LoadedItems[0].Name;
        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", "Should Be Discarded");
        state.UpdateRowEditValue("Price", 999.0);
        state.CancelRowEdit();

        Assert.False(state.IsRowEditing);
        Assert.Equal(originalName, state.LoadedItems[0].Name);
    }

    [Fact]
    public async Task IsColumnInRowEdit_Identifies_Editable_Columns()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        var rowKey = state.EditingRowKey!.Value;

        // Name, Category, Price are editable
        Assert.True(state.IsColumnInRowEdit(rowKey, "Name"));
        Assert.True(state.IsColumnInRowEdit(rowKey, "Category"));
        Assert.True(state.IsColumnInRowEdit(rowKey, "Price"));
        // Id is read-only
        Assert.False(state.IsColumnInRowEdit(rowKey, "Id"));
        // Different row key
        Assert.False(state.IsColumnInRowEdit((RowKey)999, "Name"));
    }

    [Fact]
    public async Task RowEdit_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.BeginRowEdit(0);
        Assert.Equal(1, changes);

        // UpdateRowEditValue does NOT fire StateChanged
        state.UpdateRowEditValue("Name", "New");
        Assert.Equal(1, changes);

        state.CommitRowEdit();
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task CancelEdit_Delegates_To_CancelRowEdit_When_In_Row_Mode()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        Assert.True(state.IsRowEditing);

        state.CancelEdit(); // Should delegate to CancelRowEdit
        Assert.False(state.IsRowEditing);
        Assert.False(state.IsEditing);
    }

    // ── Cell-level validation ───────────────────────────────────

    private static IReadOnlyList<FieldDescriptor> CreateValidatedColumns()
    {
        return new FieldDescriptor[]
        {
            new() { Name = "Id", FieldType = typeof(int), GetValue = o => ((TestItem)o).Id, Width = 60, IsReadOnly = true },
            new()
            {
                Name = "Name", FieldType = typeof(string),
                GetValue = o => ((TestItem)o).Name, Width = 200,
                SetValue = (owner, val) => ((TestItem)owner) with { Name = (string)(val ?? "") },
                Validators = new IValidator[] { Validate.Required(), Validate.MinLength(2) },
            },
            new()
            {
                Name = "Category", FieldType = typeof(string),
                GetValue = o => ((TestItem)o).Category, Width = 120,
                SetValue = (owner, val) => ((TestItem)owner) with { Category = (string)(val ?? "") },
            },
            new()
            {
                Name = "Price", FieldType = typeof(double),
                GetValue = o => ((TestItem)o).Price, Width = 100,
                SetValue = (owner, val) => ((TestItem)owner) with { Price = (double)(val ?? 0.0) },
                Validators = new IValidator[] { Validate.Range(0, 10000) },
            },
        };
    }

    [Fact]
    public async Task Cell_Validation_Creates_Context_On_BeginEdit()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        Assert.Null(state.EditValidation);
        state.SetFocus(0, 1); // Name column (has validators)
        state.BeginEdit();

        Assert.NotNull(state.EditValidation);
    }

    [Fact]
    public async Task Cell_Validation_Runs_On_UpdateEditingValue()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1); // Name
        state.BeginEdit();

        // Empty string violates Required and MinLength
        state.UpdateEditingValue("");
        Assert.True(state.HasValidationErrors);
        Assert.NotEmpty(state.GetValidationMessages("Name"));
    }

    [Fact]
    public async Task Cell_Validation_Clears_On_Valid_Value()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1); // Name
        state.BeginEdit();

        state.UpdateEditingValue(""); // invalid
        Assert.True(state.HasValidationErrors);

        state.UpdateEditingValue("Valid Name"); // valid
        Assert.False(state.HasValidationErrors);
        Assert.Empty(state.GetValidationMessages("Name"));
    }

    [Fact]
    public async Task Cell_Validation_Blocks_Commit()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1); // Name
        state.BeginEdit();
        state.UpdateEditingValue(""); // Required + MinLength violation

        var result = state.CommitEdit();
        Assert.Null(result); // commit blocked
        Assert.True(state.IsEditing); // still editing
    }

    [Fact]
    public async Task Cell_Validation_Allows_Commit_When_Valid()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1); // Name
        state.BeginEdit();
        state.UpdateEditingValue("OK"); // valid (2+ chars)

        var result = state.CommitEdit();
        Assert.NotNull(result);
        Assert.Equal("OK", result!.Value.NewItem.Name);
    }

    [Fact]
    public async Task Cell_Validation_Cleared_On_CancelEdit()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue(""); // invalid
        Assert.True(state.HasValidationErrors);

        state.CancelEdit();
        Assert.Null(state.EditValidation);
        Assert.False(state.HasValidationErrors);
    }

    // ── Row-level validation ────────────────────────────────────

    [Fact]
    public async Task Row_Validation_Creates_Context_For_All_Editable_Fields()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);

        Assert.NotNull(state.EditValidation);
        // Name and Price have validators
        var registered = state.EditValidation!.RegisteredFields;
        Assert.Contains("Name", registered);
        Assert.Contains("Category", registered);
        Assert.Contains("Price", registered);
    }

    [Fact]
    public async Task Row_Validation_Runs_On_UpdateRowEditValue()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", ""); // invalid
        Assert.True(state.HasValidationErrors);
        Assert.NotEmpty(state.GetValidationMessages("Name"));
    }

    [Fact]
    public async Task Row_Validation_Blocks_CommitRowEdit()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", ""); // invalid

        var result = state.CommitRowEdit();
        Assert.Null(result); // blocked
        Assert.True(state.IsRowEditing); // still editing
    }

    [Fact]
    public async Task Row_Validation_Multiple_Fields()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", ""); // invalid
        state.UpdateRowEditValue("Price", -5.0); // out of range

        var allMessages = state.GetAllValidationMessages();
        Assert.True(allMessages.Count >= 2); // errors on both fields
        Assert.NotEmpty(state.GetValidationMessages("Name"));
        Assert.NotEmpty(state.GetValidationMessages("Price"));
    }

    [Fact]
    public async Task Row_Validation_Allows_Commit_When_All_Valid()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateValidatedColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", "Valid");
        state.UpdateRowEditValue("Price", 50.0);

        var result = state.CommitRowEdit();
        Assert.NotNull(result);
    }

    // ── Async commit lifecycle ──────────────────────────────────

    [Fact]
    public async Task BeginAsyncCommit_Marks_Row_As_Committing()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var original = state.LoadedItems[0];
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Updated");
        var result = state.CommitEdit();
        Assert.NotNull(result);

        state.BeginAsyncCommit(result!.Value.Key, original);

        Assert.True(state.IsCommitting(result.Value.Key));
        Assert.True(state.HasPendingCommits);
    }

    [Fact]
    public async Task CompleteAsyncCommit_Clears_Committing_State()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var original = state.LoadedItems[0];
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Updated");
        var result = state.CommitEdit()!;

        state.BeginAsyncCommit(result.Value.Key, original);
        state.CompleteAsyncCommit(result.Value.Key);

        Assert.False(state.IsCommitting(result.Value.Key));
        Assert.False(state.HasPendingCommits);
        Assert.Null(state.GetCommitError(result.Value.Key));
        // Item remains at updated value
        Assert.Equal("Updated", state.LoadedItems[0].Name);
    }

    [Fact]
    public async Task FailAsyncCommit_Reverts_Item_And_Stores_Error()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var original = state.LoadedItems[0];
        var originalName = original.Name;
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Updated");
        var result = state.CommitEdit()!;

        state.BeginAsyncCommit(result.Value.Key, original);
        state.FailAsyncCommit(result.Value.Key, "Server error: conflict");

        Assert.False(state.IsCommitting(result.Value.Key));
        Assert.Equal("Server error: conflict", state.GetCommitError(result.Value.Key));
        // Item reverted to original
        Assert.Equal(originalName, state.LoadedItems[0].Name);
    }

    [Fact]
    public async Task DismissCommitError_Clears_Error()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var original = state.LoadedItems[0];
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("X");
        var result = state.CommitEdit()!;

        state.BeginAsyncCommit(result.Value.Key, original);
        state.FailAsyncCommit(result.Value.Key, "Error");
        Assert.NotNull(state.GetCommitError(result.Value.Key));

        state.DismissCommitError(result.Value.Key);
        Assert.Null(state.GetCommitError(result.Value.Key));
    }

    [Fact]
    public async Task AsyncCommit_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateEditableColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        var original = state.LoadedItems[0];
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("X");
        var result = state.CommitEdit()!;

        int changes = 0;
        state.StateChanged += () => changes++;

        state.BeginAsyncCommit(result.Value.Key, original);
        Assert.Equal(1, changes);

        state.CompleteAsyncCommit(result.Value.Key);
        Assert.Equal(2, changes);
    }

    // ── Column visibility ───────────────────────────────────────

    [Fact]
    public void HideColumn_Removes_From_Visible_Columns()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        Assert.Equal(4, state.Columns.Count);

        state.HideColumn("Category");

        Assert.Equal(3, state.Columns.Count);
        Assert.DoesNotContain(state.Columns, c => c.Name == "Category");
        Assert.Equal(4, state.AllColumns.Count); // AllColumns still has all
    }

    [Fact]
    public void ShowColumn_Restores_Visibility()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        state.HideColumn("Category");
        Assert.Equal(3, state.Columns.Count);

        state.ShowColumn("Category");
        Assert.Equal(4, state.Columns.Count);
    }

    [Fact]
    public void ToggleColumnVisibility_Toggles()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        state.ToggleColumnVisibility("Price");
        Assert.False(state.IsColumnVisible("Price"));
        Assert.Equal(3, state.Columns.Count);

        state.ToggleColumnVisibility("Price");
        Assert.True(state.IsColumnVisible("Price"));
        Assert.Equal(4, state.Columns.Count);
    }

    [Fact]
    public void IsColumnVisible_Returns_Correct_State()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        Assert.True(state.IsColumnVisible("Name"));
        state.HideColumn("Name");
        Assert.False(state.IsColumnVisible("Name"));
    }

    [Fact]
    public void HideColumn_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.HideColumn("Name");
        Assert.Equal(1, changes);

        // Hiding same column again doesn't fire
        state.HideColumn("Name");
        Assert.Equal(1, changes);
    }

    // ── Search state ────────────────────────────────────────────

    [Fact]
    public async Task Search_Filters_Loaded_Items()
    {
        var state = new DataGridState<TestItem>(CreateSource(20), CreateColumns(), SelectionMode.None);
        await state.LoadDataAsync(TestContext.Current.CancellationToken);
        Assert.Equal(20, state.LoadedItems.Count);

        state.SetSearchQuery("Item 1");
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // ListDataSource supports ServerSearch, so "Item 1" matches "Item 1", "Item 10"-"Item 19"
        Assert.True(state.LoadedItems.Count >= 1);
        Assert.True(state.LoadedItems.Count < 20);
    }

    // ── Row detail expand/collapse ──────────────────────────────

    [Fact]
    public void ToggleRowExpansion_Expands_And_Collapses()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        Assert.False(state.IsExpanded((RowKey)5));

        state.ToggleRowExpansion((RowKey)5);
        Assert.True(state.IsExpanded((RowKey)5));
        Assert.Single(state.ExpandedRows);

        state.ToggleRowExpansion((RowKey)5);
        Assert.False(state.IsExpanded((RowKey)5));
        Assert.Empty(state.ExpandedRows);
    }

    [Fact]
    public void ExpandRow_Adds_To_Expanded()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        state.ExpandRow((RowKey)3);
        state.ExpandRow((RowKey)7);

        Assert.True(state.IsExpanded((RowKey)3));
        Assert.True(state.IsExpanded((RowKey)7));
        Assert.Equal(2, state.ExpandedRows.Count);
    }

    [Fact]
    public void CollapseRow_Removes_From_Expanded()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        state.ExpandRow((RowKey)3);
        state.CollapseRow((RowKey)3);

        Assert.False(state.IsExpanded((RowKey)3));
    }

    [Fact]
    public void CollapseAllRows_Clears_All()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        state.ExpandRow((RowKey)1);
        state.ExpandRow((RowKey)2);
        state.ExpandRow((RowKey)3);
        state.CollapseAllRows();

        Assert.Empty(state.ExpandedRows);
    }

    [Fact]
    public void ToggleRowExpansion_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.ToggleRowExpansion((RowKey)5);
        Assert.Equal(1, changes);
    }

    // ── Column pinning ──────────────────────────────────────────

    [Fact]
    public void GetPinnedColumnGroups_Separates_By_Pin()
    {
        var columns = new FieldDescriptor[]
        {
            new() { Name = "Id", FieldType = typeof(int), GetValue = o => 0, Pin = PinPosition.Left },
            new() { Name = "Name", FieldType = typeof(string), GetValue = o => "" },
            new() { Name = "Category", FieldType = typeof(string), GetValue = o => "" },
            new() { Name = "Actions", FieldType = typeof(string), GetValue = o => "", Pin = PinPosition.Right },
        };
        var state = new DataGridState<TestItem>(CreateSource(), columns, SelectionMode.None);

        var (left, center, right) = state.GetPinnedColumnGroups();

        Assert.Single(left);
        Assert.Equal("Id", left[0].Name);
        Assert.Equal(2, center.Count);
        Assert.Equal("Name", center[0].Name);
        Assert.Single(right);
        Assert.Equal("Actions", right[0].Name);
    }

    [Fact]
    public void PinColumn_Changes_Pin_Position()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);

        state.PinColumn("Id", PinPosition.Left);
        state.PinColumn("Price", PinPosition.Right);

        var (left, center, right) = state.GetPinnedColumnGroups();
        Assert.Single(left);
        Assert.Equal("Id", left[0].Name);
        Assert.Single(right);
        Assert.Equal("Price", right[0].Name);
        Assert.Equal(2, center.Count); // Name, Category
    }

    [Fact]
    public void PinColumn_Fires_StateChanged()
    {
        var state = new DataGridState<TestItem>(CreateSource(), CreateColumns(), SelectionMode.None);
        int changes = 0;
        state.StateChanged += () => changes++;

        state.PinColumn("Id", PinPosition.Left);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Hidden_Columns_Excluded_From_Pinned_Groups()
    {
        var columns = new FieldDescriptor[]
        {
            new() { Name = "Id", FieldType = typeof(int), GetValue = o => 0, Pin = PinPosition.Left },
            new() { Name = "Name", FieldType = typeof(string), GetValue = o => "" },
        };
        var state = new DataGridState<TestItem>(CreateSource(), columns, SelectionMode.None);

        state.HideColumn("Id");
        var (left, center, _) = state.GetPinnedColumnGroups();

        Assert.Empty(left); // hidden column not in any group
        Assert.Single(center);
    }
}
