using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Comprehensive tests for DataGridState — the pure state machine behind the DataGrid.
/// Tests sorting, filtering, selection, column management, focus navigation,
/// editing, and row expansion — all without WinUI dependencies.
/// </summary>
public class DataGridStateAdditionalTests
{
    // ── Test helpers ─────────────────────────────────────────────

    private record TestItem(int Id, string Name, double Score);

    private class TestDataSource : IDataSource<TestItem>
    {
        private readonly List<TestItem> _items;
        public TestDataSource(params TestItem[] items) => _items = new(items);

        public Task<DataPage<TestItem>> GetPageAsync(DataRequest request, CancellationToken ct = default)
            => Task.FromResult(new DataPage<TestItem>(_items, TotalCount: _items.Count));

        public RowKey GetRowKey(TestItem item) => new(item.Id.ToString());
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

    private DataGridState<TestItem> CreateState(SelectionMode mode = SelectionMode.Multiple)
        => new(new TestDataSource(
            new TestItem(1, "Alice", 95),
            new TestItem(2, "Bob", 87),
            new TestItem(3, "Carol", 92)
        ), TestColumns, mode);

    private async Task<DataGridState<TestItem>> CreateLoadedState(SelectionMode mode = SelectionMode.Multiple)
    {
        var state = CreateState(mode);
        await state.LoadDataAsync();
        return state;
    }

    // ════════════════════════════════════════════════════════════════
    //  Sort operations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ToggleSort_None_To_Ascending()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        Assert.Single(state.Sorts);
        Assert.Equal(SortDirection.Ascending, state.GetSortDirection("Name"));
    }

    [Fact]
    public void ToggleSort_Ascending_To_Descending()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        state.ToggleSort("Name");
        Assert.Single(state.Sorts);
        Assert.Equal(SortDirection.Descending, state.GetSortDirection("Name"));
    }

    [Fact]
    public void ToggleSort_Descending_To_None()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        state.ToggleSort("Name");
        state.ToggleSort("Name");
        Assert.Empty(state.Sorts);
        Assert.Null(state.GetSortDirection("Name"));
    }

    [Fact]
    public void ToggleSort_Additive_MultiSort()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        state.ToggleSort("Score", additive: true);
        Assert.Equal(2, state.Sorts.Count);
        Assert.Equal(SortDirection.Ascending, state.GetSortDirection("Name"));
        Assert.Equal(SortDirection.Ascending, state.GetSortDirection("Score"));
    }

    [Fact]
    public void ToggleSort_Additive_Toggle_Existing()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        state.ToggleSort("Name", additive: true); // Asc -> Desc
        Assert.Equal(SortDirection.Descending, state.GetSortDirection("Name"));

        state.ToggleSort("Name", additive: true); // Desc -> remove
        Assert.Null(state.GetSortDirection("Name"));
    }

    [Fact]
    public void ToggleSort_NonAdditive_Replaces_Existing()
    {
        var state = CreateState();
        state.ToggleSort("Name");
        state.ToggleSort("Score"); // Not additive — clears Name, adds Score
        Assert.Single(state.Sorts);
        Assert.Null(state.GetSortDirection("Name"));
        Assert.Equal(SortDirection.Ascending, state.GetSortDirection("Score"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Filter operations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SetFilter_And_GetFilter()
    {
        var state = CreateState();
        var filter = new FilterDescriptor("Name", FilterOperator.Contains, "Al");
        state.SetFilter(filter);
        Assert.Equal(filter, state.GetFilter("Name"));
    }

    [Fact]
    public void SetFilter_Replaces_Existing()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "Al"));
        var newFilter = new FilterDescriptor("Name", FilterOperator.Contains, "Bo");
        state.SetFilter(newFilter);
        Assert.Single(state.Filters);
        Assert.Equal("Bo", state.GetFilter("Name")!.Value);
    }

    [Fact]
    public void ClearFilter_Removes_Specific()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "Al"));
        state.SetFilter(new FilterDescriptor("Score", FilterOperator.GreaterThan, 90));
        state.ClearFilter("Name");
        Assert.Single(state.Filters);
        Assert.Null(state.GetFilter("Name"));
        Assert.NotNull(state.GetFilter("Score"));
    }

    [Fact]
    public void ClearAllFilters()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "Al"));
        state.SetFilter(new FilterDescriptor("Score", FilterOperator.GreaterThan, 90));
        state.ClearAllFilters();
        Assert.Empty(state.Filters);
    }

    [Fact]
    public void ClearFilter_NoOp_When_Not_Found()
    {
        var state = CreateState();
        int changes = 0;
        state.StateChanged += () => changes++;
        state.ClearFilter("NonExistent");
        Assert.Equal(0, changes);
    }

    // ════════════════════════════════════════════════════════════════
    //  Search
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SetSearchQuery()
    {
        var state = CreateState();
        state.SetSearchQuery("test");
        Assert.Equal("test", state.SearchQuery);
    }

    [Fact]
    public void SetSearchQuery_Whitespace_Becomes_Null()
    {
        var state = CreateState();
        state.SetSearchQuery("   ");
        Assert.Null(state.SearchQuery);
    }

    // ════════════════════════════════════════════════════════════════
    //  Selection — Single mode
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleSelection_Click_Selects_One()
    {
        var state = CreateState(SelectionMode.Single);
        state.HandleRowClick(new RowKey("1"));
        Assert.Single(state.SelectedKeys);
        Assert.True(state.IsSelected(new RowKey("1")));
    }

    [Fact]
    public void SingleSelection_Click_Another_Replaces()
    {
        var state = CreateState(SelectionMode.Single);
        state.HandleRowClick(new RowKey("1"));
        state.HandleRowClick(new RowKey("2"));
        Assert.Single(state.SelectedKeys);
        Assert.False(state.IsSelected(new RowKey("1")));
        Assert.True(state.IsSelected(new RowKey("2")));
    }

    // ════════════════════════════════════════════════════════════════
    //  Selection — Multiple mode
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleSelection_Ctrl_Click_Toggles()
    {
        var state = CreateState(SelectionMode.Multiple);
        state.HandleRowClick(new RowKey("1"));
        state.HandleRowClick(new RowKey("2"), ctrlKey: true);
        Assert.Equal(2, state.SelectedKeys.Count);

        state.HandleRowClick(new RowKey("1"), ctrlKey: true); // deselect
        Assert.Single(state.SelectedKeys);
        Assert.False(state.IsSelected(new RowKey("1")));
    }

    [Fact]
    public void MultipleSelection_Shift_Click_Range()
    {
        var state = CreateState(SelectionMode.Multiple);
        var order = new[] { new RowKey("1"), new RowKey("2"), new RowKey("3") };
        state.HandleRowClick(new RowKey("1"));
        state.HandleRowClick(new RowKey("3"), shiftKey: true, visibleOrder: order);
        Assert.Equal(3, state.SelectedKeys.Count);
    }

    [Fact]
    public void SelectAll_And_Clear()
    {
        var state = CreateState(SelectionMode.Multiple);
        var allKeys = new[] { new RowKey("1"), new RowKey("2"), new RowKey("3") };
        state.SelectAll(allKeys);
        Assert.Equal(3, state.SelectedKeys.Count);

        state.ClearSelection();
        Assert.Empty(state.SelectedKeys);
        Assert.Null(state.AnchorKey);
    }

    [Fact]
    public void Selection_None_Mode_NoOp()
    {
        var state = CreateState(SelectionMode.None);
        state.HandleRowClick(new RowKey("1"));
        Assert.Empty(state.SelectedKeys);
    }

    [Fact]
    public void SelectAll_NoOp_In_Single_Mode()
    {
        var state = CreateState(SelectionMode.Single);
        state.SelectAll(new[] { new RowKey("1"), new RowKey("2") });
        Assert.Empty(state.SelectedKeys);
    }

    [Fact]
    public void SelectRange_Reverse_Order()
    {
        var state = CreateState();
        var order = new[] { new RowKey("1"), new RowKey("2"), new RowKey("3") };
        state.SelectRange(new RowKey("3"), new RowKey("1"), order);
        Assert.Equal(3, state.SelectedKeys.Count);
    }

    [Fact]
    public void SelectRange_Invalid_Keys_NoOp()
    {
        var state = CreateState();
        var order = new[] { new RowKey("1"), new RowKey("2") };
        state.SelectRange(new RowKey("X"), new RowKey("1"), order);
        Assert.Empty(state.SelectedKeys);
    }

    // ════════════════════════════════════════════════════════════════
    //  Column operations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetColumnWidth_Default_And_Custom()
    {
        var state = CreateState();
        Assert.Equal(80, state.GetColumnWidth("Score")); // specified
        Assert.Equal(120, state.GetColumnWidth("Name")); // default
    }

    [Fact]
    public void ResizeColumn_Clamps_To_Min_Max()
    {
        var state = CreateState();
        state.ResizeColumn("Score", 10); // below min of 50
        Assert.Equal(50, state.GetColumnWidth("Score"));

        state.ResizeColumn("Score", 300); // above max of 200
        Assert.Equal(200, state.GetColumnWidth("Score"));
    }

    [Fact]
    public void ReorderColumn()
    {
        var state = CreateState();
        state.ReorderColumn(0, 2);
        Assert.Equal("Name", state.AllColumns[0].Name);
        Assert.Equal("Id", state.AllColumns[2].Name);
    }

    [Fact]
    public void ReorderColumn_Invalid_NoOp()
    {
        var state = CreateState();
        state.ReorderColumn(-1, 0);
        state.ReorderColumn(0, 99);
        Assert.Equal("Id", state.AllColumns[0].Name); // unchanged
    }

    [Fact]
    public void HideAndShow_Column()
    {
        var state = CreateState();
        state.HideColumn("Score");
        Assert.False(state.IsColumnVisible("Score"));
        Assert.Equal(2, state.Columns.Count);

        state.ShowColumn("Score");
        Assert.True(state.IsColumnVisible("Score"));
        Assert.Equal(3, state.Columns.Count);
    }

    [Fact]
    public void ToggleColumnVisibility()
    {
        var state = CreateState();
        state.ToggleColumnVisibility("Name");
        Assert.False(state.IsColumnVisible("Name"));

        state.ToggleColumnVisibility("Name");
        Assert.True(state.IsColumnVisible("Name"));
    }

    [Fact]
    public void PinnedColumnGroups()
    {
        var state = CreateState();
        state.PinColumn("Id", PinPosition.Left);
        state.PinColumn("Score", PinPosition.Right);

        var (left, center, right) = state.GetPinnedColumnGroups();
        Assert.Single(left);
        Assert.Equal("Id", left[0].Name);
        Assert.Single(center);
        Assert.Equal("Name", center[0].Name);
        Assert.Single(right);
        Assert.Equal("Score", right[0].Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  Row expansion
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ToggleRowExpansion()
    {
        var state = CreateState();
        var key = new RowKey("1");
        Assert.False(state.IsExpanded(key));

        state.ToggleRowExpansion(key);
        Assert.True(state.IsExpanded(key));

        state.ToggleRowExpansion(key);
        Assert.False(state.IsExpanded(key));
    }

    [Fact]
    public void ExpandAndCollapse_Row()
    {
        var state = CreateState();
        var key = new RowKey("1");
        state.ExpandRow(key);
        Assert.True(state.IsExpanded(key));

        state.CollapseRow(key);
        Assert.False(state.IsExpanded(key));
    }

    [Fact]
    public void CollapseAllRows()
    {
        var state = CreateState();
        state.ExpandRow(new RowKey("1"));
        state.ExpandRow(new RowKey("2"));
        state.CollapseAllRows();
        Assert.Empty(state.ExpandedRows);
    }

    // ════════════════════════════════════════════════════════════════
    //  StateChanged event
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void StateChanged_Fires_On_Operations()
    {
        var state = CreateState();
        int changes = 0;
        state.StateChanged += () => changes++;

        state.ToggleSort("Name");
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "x"));
        state.HandleRowClick(new RowKey("1"));
        state.ResizeColumn("Score", 100);
        state.HideColumn("Id");

        Assert.Equal(5, changes);
    }

    // ════════════════════════════════════════════════════════════════
    //  Editing state
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Initial_State_Not_Editing()
    {
        var state = CreateState();
        Assert.False(state.IsEditing);
        Assert.False(state.IsRowEditing);
        Assert.Null(state.EditingRowKey);
        Assert.Null(state.EditingColumnName);
        Assert.False(state.HasValidationErrors);
    }

    [Fact]
    public void GetRowEditValue_Returns_Null_When_Not_Editing()
    {
        var state = CreateState();
        Assert.Null(state.GetRowEditValue("Name"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Focus navigation (requires loaded data)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetFocus_Clamps_To_Bounds()
    {
        var state = await CreateLoadedState();
        state.SetFocus(100, 100); // beyond bounds
        Assert.Equal(2, state.FocusedRowIndex); // clamped to last row (0-indexed, 3 items)
        Assert.Equal(2, state.FocusedColIndex); // clamped to last col
    }

    [Fact]
    public async Task MoveFocus_Delta()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 0);
        state.MoveFocus(1, 1);
        Assert.Equal(1, state.FocusedRowIndex);
        Assert.Equal(1, state.FocusedColIndex);
    }

    [Fact]
    public async Task MoveFocus_NoFocus_Starts_AtZero()
    {
        var state = await CreateLoadedState();
        state.MoveFocus(0, 0);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusHome_And_End()
    {
        var state = await CreateLoadedState();
        state.SetFocus(1, 1);
        state.FocusHome();
        Assert.Equal(0, state.FocusedColIndex);
        Assert.Equal(1, state.FocusedRowIndex);

        state.FocusEnd();
        Assert.Equal(2, state.FocusedColIndex); // last column
        Assert.Equal(1, state.FocusedRowIndex);
    }

    [Fact]
    public async Task FocusNextCell_Wraps_To_Next_Row()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 2); // last column
        var moved = state.FocusNextCell();
        Assert.True(moved);
        Assert.Equal(1, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusNextCell_Returns_False_At_End()
    {
        var state = await CreateLoadedState();
        state.SetFocus(2, 2); // last cell
        var moved = state.FocusNextCell();
        Assert.False(moved);
    }

    [Fact]
    public async Task FocusPrevCell_Wraps_To_Previous_Row()
    {
        var state = await CreateLoadedState();
        state.SetFocus(1, 0); // first column of row 1
        var moved = state.FocusPrevCell();
        Assert.True(moved);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusPrevCell_Returns_False_At_Start()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 0); // first cell
        var moved = state.FocusPrevCell();
        Assert.False(moved);
    }

    [Fact]
    public async Task FocusHome_NoFocus_Starts_AtZero()
    {
        var state = await CreateLoadedState();
        state.FocusHome();
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusEnd_NoFocus_Starts_AtZero()
    {
        var state = await CreateLoadedState();
        state.FocusEnd();
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex);
    }

    // ════════════════════════════════════════════════════════════════
    //  Cell editing (requires loaded data with SetValue)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BeginEdit_ReadOnly_Column_Returns_False()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 0); // "Id" column — IsReadOnly = true
        Assert.False(state.BeginEdit());
    }

    [Fact]
    public async Task BeginEdit_Editable_Column()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 1); // "Name" column — editable
        Assert.True(state.BeginEdit());
        Assert.True(state.IsEditing);
        Assert.Equal("Name", state.EditingColumnName);
        Assert.Equal("Alice", state.EditingValue);
    }

    [Fact]
    public async Task UpdateEditingValue_And_CommitEdit()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 1);
        state.BeginEdit();

        state.UpdateEditingValue("Alicia");
        Assert.Equal("Alicia", state.EditingValue);

        var result = state.CommitEdit();
        Assert.NotNull(result);
        Assert.Equal("Alicia", result.Value.NewItem.Name);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task CancelEdit_Discards_Changes()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Changed");
        state.CancelEdit();

        Assert.False(state.IsEditing);
        Assert.Equal("Alice", state.GetItemAt(0)!.Name); // unchanged
    }

    [Fact]
    public async Task CommitAndMoveNext()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Alicia");
        var result = state.CommitAndMoveNext();

        Assert.NotNull(result);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex); // moved to next column
    }

    [Fact]
    public async Task BeginEdit_Invalid_Row_Returns_False()
    {
        var state = await CreateLoadedState();
        Assert.False(state.BeginEdit(-1, 1));
        Assert.False(state.BeginEdit(100, 1));
    }

    [Fact]
    public async Task CommitEdit_Returns_Null_When_Not_Editing()
    {
        var state = await CreateLoadedState();
        Assert.Null(state.CommitEdit());
    }

    // ════════════════════════════════════════════════════════════════
    //  Row-mode editing
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BeginRowEdit_And_CommitRowEdit()
    {
        var state = await CreateLoadedState();
        Assert.True(state.BeginRowEdit(0));
        Assert.True(state.IsRowEditing);
        Assert.True(state.IsEditing);

        Assert.Equal("Alice", state.GetRowEditValue("Name"));
        Assert.Equal(95.0, state.GetRowEditValue("Score"));

        state.UpdateRowEditValue("Name", "Alicia");
        state.UpdateRowEditValue("Score", 99.0);

        var result = state.CommitRowEdit();
        Assert.NotNull(result);
        Assert.Equal("Alicia", result.Value.NewItem.Name);
        Assert.Equal(99.0, result.Value.NewItem.Score);
        Assert.False(state.IsRowEditing);
    }

    [Fact]
    public async Task CancelRowEdit_Discards()
    {
        var state = await CreateLoadedState();
        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", "Changed");
        state.CancelRowEdit();
        Assert.False(state.IsRowEditing);
        Assert.Equal("Alice", state.GetItemAt(0)!.Name);
    }

    [Fact]
    public async Task CancelEdit_Redirects_To_CancelRowEdit()
    {
        var state = await CreateLoadedState();
        state.BeginRowEdit(0);
        state.CancelEdit(); // should redirect to CancelRowEdit
        Assert.False(state.IsRowEditing);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task IsColumnInRowEdit()
    {
        var state = await CreateLoadedState();
        state.BeginRowEdit(0);
        var key = state.EditingRowKey!.Value;
        Assert.True(state.IsColumnInRowEdit(key, "Name"));
        Assert.True(state.IsColumnInRowEdit(key, "Score"));
        Assert.False(state.IsColumnInRowEdit(key, "Id")); // read-only, not in edit
    }

    [Fact]
    public async Task BeginRowEdit_Invalid_Row()
    {
        var state = await CreateLoadedState();
        Assert.False(state.BeginRowEdit(-1));
        Assert.False(state.BeginRowEdit(100));
    }

    [Fact]
    public async Task CommitRowEdit_NotInRowEdit_Returns_Null()
    {
        var state = await CreateLoadedState();
        Assert.Null(state.CommitRowEdit());
    }

    // ════════════════════════════════════════════════════════════════
    //  Async commit lifecycle
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsyncCommit_Complete_Lifecycle()
    {
        var state = await CreateLoadedState();
        var key = new RowKey("1");
        var original = state.GetItemAt(0)!;

        state.BeginAsyncCommit(key, original);
        Assert.True(state.IsCommitting(key));
        Assert.True(state.HasPendingCommits);

        state.CompleteAsyncCommit(key);
        Assert.False(state.IsCommitting(key));
        Assert.False(state.HasPendingCommits);
    }

    [Fact]
    public async Task AsyncCommit_Fail_Reverts()
    {
        var state = await CreateLoadedState();
        var key = new RowKey("1");
        var original = state.GetItemAt(0)!;

        // Edit the cell first
        state.SetFocus(0, 1);
        state.BeginEdit();
        state.UpdateEditingValue("Changed");
        state.CommitEdit();

        // Begin async commit
        state.BeginAsyncCommit(key, original);
        state.FailAsyncCommit(key, "Server error");

        Assert.False(state.IsCommitting(key));
        Assert.Equal("Server error", state.GetCommitError(key));
        // Should have reverted to original
        Assert.Equal("Alice", state.GetItemAt(0)!.Name);
    }

    [Fact]
    public async Task DismissCommitError()
    {
        var state = await CreateLoadedState();
        var key = new RowKey("1");
        state.BeginAsyncCommit(key, state.GetItemAt(0)!);
        state.FailAsyncCommit(key, "Error");
        state.DismissCommitError(key);
        Assert.Null(state.GetCommitError(key));
    }

    // ════════════════════════════════════════════════════════════════
    //  Data loading and client sort/filter
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LoadDataAsync_Loads_Items()
    {
        var state = CreateState();
        Assert.Equal(0, state.ItemCount);
        await state.LoadDataAsync();
        Assert.Equal(3, state.ItemCount);
    }

    [Fact]
    public async Task LoadDataAsync_Client_Sort()
    {
        var state = CreateState();
        state.ToggleSort("Name"); // ascending
        await state.LoadDataAsync(); // client-side sort (no ServerSort capability)
        Assert.Equal("Alice", state.GetItemAt(0)!.Name);
        Assert.Equal("Bob", state.GetItemAt(1)!.Name);
        Assert.Equal("Carol", state.GetItemAt(2)!.Name);
    }

    [Fact]
    public async Task LoadDataAsync_Client_Sort_Descending()
    {
        var state = CreateState();
        state.ToggleSort("Name"); // asc
        state.ToggleSort("Name"); // desc
        await state.LoadDataAsync();
        Assert.Equal("Carol", state.GetItemAt(0)!.Name);
        Assert.Equal("Alice", state.GetItemAt(2)!.Name);
    }

    [Fact]
    public async Task LoadDataAsync_Client_Filter_Contains()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "o"));
        await state.LoadDataAsync();
        Assert.Equal(2, state.ItemCount); // Bob and Carol
    }

    [Fact]
    public async Task LoadDataAsync_Client_Filter_Equals()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Equals, "Bob"));
        await state.LoadDataAsync();
        Assert.Equal(1, state.ItemCount);
        Assert.Equal("Bob", state.GetItemAt(0)!.Name);
    }

    [Fact]
    public async Task LoadDataAsync_Client_Filter_NotEquals()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.NotEquals, "Bob"));
        await state.LoadDataAsync();
        Assert.Equal(2, state.ItemCount);
    }

    [Fact]
    public async Task LoadDataAsync_Client_Filter_GreaterThan()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Score", FilterOperator.GreaterThan, 90.0));
        await state.LoadDataAsync();
        Assert.Equal(2, state.ItemCount); // Alice(95) and Carol(92)
    }

    [Fact]
    public async Task LoadDataAsync_Client_Filter_LessThan()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Score", FilterOperator.LessThan, 90.0));
        await state.LoadDataAsync();
        Assert.Equal(1, state.ItemCount); // Bob(87)
    }

    [Fact]
    public async Task LoadDataAsync_Client_Filter_IsNull()
    {
        var source = new TestDataSource(
            new TestItem(1, "Alice", 95),
            new TestItem(2, "Bob", 87)
        );
        var state = new DataGridState<TestItem>(source, TestColumns, SelectionMode.Multiple);
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.IsNull));
        await state.LoadDataAsync();
        Assert.Equal(0, state.ItemCount); // none are null
    }

    [Fact]
    public async Task LoadDataAsync_Client_Filter_IsNotNull()
    {
        var state = CreateState();
        state.SetFilter(new FilterDescriptor("Name", FilterOperator.IsNotNull));
        await state.LoadDataAsync();
        Assert.Equal(3, state.ItemCount); // all non-null
    }

    [Fact]
    public async Task LoadDataAsync_MultiSort()
    {
        var source = new TestDataSource(
            new TestItem(1, "Alice", 95),
            new TestItem(2, "Bob", 95),
            new TestItem(3, "Carol", 87)
        );
        var state = new DataGridState<TestItem>(source, TestColumns, SelectionMode.Multiple);
        state.ToggleSort("Score"); // ascending
        state.ToggleSort("Name", additive: true); // then ascending name
        await state.LoadDataAsync();
        Assert.Equal("Carol", state.GetItemAt(0)!.Name); // 87
        Assert.Equal("Alice", state.GetItemAt(1)!.Name); // 95, A
        Assert.Equal("Bob", state.GetItemAt(2)!.Name);   // 95, B
    }

    [Fact]
    public async Task GetRowIndex_After_Load()
    {
        var state = await CreateLoadedState();
        Assert.Equal(0, state.GetRowIndex(new RowKey("1")));
        Assert.Equal(1, state.GetRowIndex(new RowKey("2")));
        Assert.Equal(-1, state.GetRowIndex(new RowKey("99")));
    }

    [Fact]
    public async Task GetRowKeyAt_After_Load()
    {
        var state = await CreateLoadedState();
        Assert.Equal("1", state.GetRowKeyAt(0));
        Assert.Equal("2", state.GetRowKeyAt(1));
        Assert.Null(state.GetRowKeyAt(100));
    }

    [Fact]
    public async Task IsItemLoaded_After_Load()
    {
        var state = await CreateLoadedState();
        Assert.True(state.IsItemLoaded(0));
        Assert.True(state.IsItemLoaded(2));
        Assert.False(state.IsItemLoaded(100));
    }

    [Fact]
    public async Task LoadedItems_Returns_All_After_Load()
    {
        var state = await CreateLoadedState();
        Assert.Equal(3, state.LoadedItems.Count);
    }

    [Fact]
    public async Task GetItemAt_OutOfBounds()
    {
        var state = await CreateLoadedState();
        Assert.Null(state.GetItemAt(100));
    }

    // ════════════════════════════════════════════════════════════════
    //  Server-capable data source (paged mode)
    // ════════════════════════════════════════════════════════════════

    private class ServerSortSource : IDataSource<TestItem>
    {
        private readonly List<TestItem> _items;
        public ServerSortSource(params TestItem[] items) => _items = new(items);

        public Task<DataPage<TestItem>> GetPageAsync(DataRequest request, CancellationToken ct = default)
            => Task.FromResult(new DataPage<TestItem>(_items, TotalCount: _items.Count));

        public RowKey GetRowKey(TestItem item) => new(item.Id.ToString());
        public DataSourceCapabilities Capabilities => DataSourceCapabilities.ServerSort | DataSourceCapabilities.ServerFilter;
    }

    [Fact]
    public async Task LoadDataAsync_Paged_Mode()
    {
        var source = new ServerSortSource(
            new TestItem(1, "Alice", 95),
            new TestItem(2, "Bob", 87)
        );
        var state = new DataGridState<TestItem>(source, TestColumns, SelectionMode.Multiple);
        await state.LoadDataAsync();
        Assert.NotNull(state.PageCache);
        Assert.Equal(2, state.ItemCount);
    }

    [Fact]
    public async Task Paged_Mode_GetItemAt()
    {
        var source = new ServerSortSource(new TestItem(1, "Alice", 95));
        var state = new DataGridState<TestItem>(source, TestColumns, SelectionMode.Multiple);
        await state.LoadDataAsync();
        Assert.Equal("Alice", state.GetItemAt(0)!.Name);
    }

    // ════════════════════════════════════════════════════════════════
    //  Validation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetValidationMessages_Empty_When_Not_Editing()
    {
        var state = await CreateLoadedState();
        Assert.Empty(state.GetValidationMessages("Name"));
        Assert.Empty(state.GetAllValidationMessages());
    }

    // ════════════════════════════════════════════════════════════════
    //  Selection version tracking
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SelectionVersion_Increments()
    {
        var state = CreateState();
        var v0 = state.SelectionVersion;
        state.HandleRowClick(new RowKey("1"));
        Assert.True(state.SelectionVersion > v0);
    }

    [Fact]
    public void EditingVersion_Increments()
    {
        var state = CreateState();
        var v0 = state.EditingVersion;
        // EditingVersion doesn't change without actual editing
        Assert.Equal(v0, state.EditingVersion);
    }

    // ════════════════════════════════════════════════════════════════
    //  SetHookResource
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SetHookResource_Null_NoOp()
    {
        var state = CreateState();
        state.SetHookResource(null);
        Assert.Null(state.HookResource);
    }

    // ════════════════════════════════════════════════════════════════
    //  ClearAllFilters no-op when empty
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ClearAllFilters_NoOp_When_Empty()
    {
        var state = CreateState();
        int changes = 0;
        state.StateChanged += () => changes++;
        state.ClearAllFilters();
        Assert.Equal(0, changes);
    }

    // ════════════════════════════════════════════════════════════════
    //  Column group — all hidden
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Columns_ExcludesHidden()
    {
        var state = CreateState();
        state.HideColumn("Id");
        state.HideColumn("Name");
        Assert.Single(state.Columns); // only Score visible
        Assert.Equal(3, state.AllColumns.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  Expand/Collapse idempotency
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ExpandRow_Idempotent()
    {
        var state = CreateState();
        int changes = 0;
        state.StateChanged += () => changes++;
        state.ExpandRow(new RowKey("1"));
        state.ExpandRow(new RowKey("1")); // no-op
        Assert.Equal(1, changes);
    }

    [Fact]
    public void CollapseRow_Idempotent()
    {
        var state = CreateState();
        state.ExpandRow(new RowKey("1"));
        int changes = 0;
        state.StateChanged += () => changes++;
        state.CollapseRow(new RowKey("1"));
        state.CollapseRow(new RowKey("1")); // no-op
        Assert.Equal(1, changes);
    }

    [Fact]
    public void CollapseAllRows_NoOp_When_Empty()
    {
        var state = CreateState();
        int changes = 0;
        state.StateChanged += () => changes++;
        state.CollapseAllRows();
        Assert.Equal(0, changes);
    }
}
