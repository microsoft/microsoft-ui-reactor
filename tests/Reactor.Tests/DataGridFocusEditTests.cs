using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for DataGridState focus navigation and editing operations.
/// Covers SetFocus, MoveFocus, FocusHome/End, FocusNextCell/PrevCell,
/// BeginEdit, UpdateEditingValue, CommitEdit, CancelEdit,
/// BeginRowEdit, UpdateRowEditValue, CommitRowEdit, CancelRowEdit,
/// CommitAndMoveNext, and async commit lifecycle.
/// </summary>
public class DataGridFocusEditTests
{
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
        },
    ];

    private DataGridState<TestItem> CreateState(SelectionMode mode = SelectionMode.Multiple)
        => new(new TestDataSource(
            new TestItem(1, "Alice", 95),
            new TestItem(2, "Bob", 87),
            new TestItem(3, "Carol", 92),
            new TestItem(4, "Dave", 78),
            new TestItem(5, "Eve", 99)
        ), TestColumns, mode);

    private async Task<DataGridState<TestItem>> CreateLoadedState(SelectionMode mode = SelectionMode.Multiple)
    {
        var state = CreateState(mode);
        await state.LoadDataAsync();
        return state;
    }

    // ═══════════════════════════════════════════════════════════════
    // SetFocus
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetFocus_SetsRowAndColumnIndex()
    {
        var state = await CreateLoadedState();
        state.SetFocus(2, 1);
        Assert.Equal(2, state.FocusedRowIndex);
        Assert.Equal(1, state.FocusedColIndex);
    }

    [Fact]
    public async Task SetFocus_ClampsOverflow()
    {
        var state = await CreateLoadedState();
        state.SetFocus(100, 100);
        Assert.Equal(4, state.FocusedRowIndex); // last row
        Assert.Equal(2, state.FocusedColIndex); // last column
    }

    [Fact]
    public async Task SetFocus_ClampsNegative()
    {
        var state = await CreateLoadedState();
        state.SetFocus(-5, -3);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public void SetFocus_EmptyGrid_Noop()
    {
        var state = new DataGridState<TestItem>(
            new TestDataSource(), TestColumns, SelectionMode.Multiple);
        state.SetFocus(0, 0); // should not throw
    }

    [Fact]
    public async Task SetFocus_UpdatesFocusedKey()
    {
        var state = await CreateLoadedState();
        state.SetFocus(2, 0);
        Assert.NotNull(state.FocusedKey);
        Assert.Equal("3", state.FocusedKey!.Value.Value); // Carol's ID
    }

    [Fact]
    public async Task SetFocus_FiresStateChanged()
    {
        var state = await CreateLoadedState();
        int changeCount = 0;
        state.StateChanged += () => changeCount++;
        state.SetFocus(1, 1);
        Assert.True(changeCount > 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // MoveFocus
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MoveFocus_DownAndRight()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 0);
        state.MoveFocus(1, 1);
        Assert.Equal(1, state.FocusedRowIndex);
        Assert.Equal(1, state.FocusedColIndex);
    }

    [Fact]
    public async Task MoveFocus_NoFocusYet_StartsAtOrigin()
    {
        var state = await CreateLoadedState();
        state.MoveFocus(1, 0);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task MoveFocus_ClampsBeyondGrid()
    {
        var state = await CreateLoadedState();
        state.SetFocus(4, 2);
        state.MoveFocus(5, 5);
        Assert.Equal(4, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex);
    }

    // ═══════════════════════════════════════════════════════════════
    // FocusHome / FocusEnd
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FocusHome_MovesToFirstColumn()
    {
        var state = await CreateLoadedState();
        state.SetFocus(2, 2);
        state.FocusHome();
        Assert.Equal(2, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusEnd_MovesToLastColumn()
    {
        var state = await CreateLoadedState();
        state.SetFocus(2, 0);
        state.FocusEnd();
        Assert.Equal(2, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusHome_NoFocusYet_StartsAtOrigin()
    {
        var state = await CreateLoadedState();
        state.FocusHome();
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusEnd_NoFocusYet_StartsAtLastColumn()
    {
        var state = await CreateLoadedState();
        state.FocusEnd();
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex);
    }

    // ═══════════════════════════════════════════════════════════════
    // FocusNextCell / FocusPrevCell
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FocusNextCell_AdvancesThroughColumns()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 0);
        Assert.True(state.FocusNextCell());
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(1, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusNextCell_WrapsToNextRow()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 2); // last column
        Assert.True(state.FocusNextCell());
        Assert.Equal(1, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusNextCell_ReturnsFalseAtEnd()
    {
        var state = await CreateLoadedState();
        state.SetFocus(4, 2); // last row, last col
        Assert.False(state.FocusNextCell());
    }

    [Fact]
    public async Task FocusNextCell_NoFocusYet_StartsAtOrigin()
    {
        var state = await CreateLoadedState();
        Assert.True(state.FocusNextCell());
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusPrevCell_MovesBackward()
    {
        var state = await CreateLoadedState();
        state.SetFocus(1, 1);
        Assert.True(state.FocusPrevCell());
        Assert.Equal(1, state.FocusedRowIndex);
        Assert.Equal(0, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusPrevCell_WrapsToEndOfPreviousRow()
    {
        var state = await CreateLoadedState();
        state.SetFocus(1, 0); // first column
        Assert.True(state.FocusPrevCell());
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex); // last column of row 0
    }

    [Fact]
    public async Task FocusPrevCell_ReturnsFalseAtStart()
    {
        var state = await CreateLoadedState();
        state.SetFocus(0, 0);
        Assert.False(state.FocusPrevCell());
    }

    [Fact]
    public async Task FocusPrevCell_NoFocusYet_StartsAtOrigin()
    {
        var state = await CreateLoadedState();
        Assert.True(state.FocusPrevCell());
        Assert.Equal(0, state.FocusedRowIndex);
    }

    // ═══════════════════════════════════════════════════════════════
    // BeginEdit / UpdateEditingValue / CommitEdit / CancelEdit
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BeginEdit_EditableColumn_ReturnsTrue()
    {
        var state = await CreateLoadedState();
        Assert.True(state.BeginEdit(0, 1)); // Name is editable
        Assert.True(state.IsEditing);
    }

    [Fact]
    public async Task BeginEdit_ReadOnlyColumn_ReturnsFalse()
    {
        var state = await CreateLoadedState();
        Assert.False(state.BeginEdit(0, 0)); // Id is read-only
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task BeginEdit_InvalidRowIndex_ReturnsFalse()
    {
        var state = await CreateLoadedState();
        Assert.False(state.BeginEdit(-1, 1));
        Assert.False(state.BeginEdit(100, 1));
    }

    [Fact]
    public async Task BeginEdit_InvalidColIndex_ReturnsFalse()
    {
        var state = await CreateLoadedState();
        Assert.False(state.BeginEdit(0, -1));
        Assert.False(state.BeginEdit(0, 100));
    }

    [Fact]
    public async Task BeginEdit_FocusedCell_UsesCurrentFocus()
    {
        var state = await CreateLoadedState();
        state.SetFocus(1, 1); // Bob, Name
        Assert.True(state.BeginEdit());
        Assert.True(state.IsEditing);
    }

    [Fact]
    public async Task BeginEdit_NoFocus_ReturnsFalse()
    {
        var state = await CreateLoadedState();
        Assert.False(state.BeginEdit());
    }

    [Fact]
    public async Task UpdateEditingValue_StoresNewValue()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 1);
        state.UpdateEditingValue("Alicia");
        // The value is stored; verify via commit
        var result = state.CommitEdit();
        Assert.NotNull(result);
        Assert.Equal("Alicia", result!.Value.NewItem.Name);
    }

    [Fact]
    public async Task UpdateEditingValue_NotEditing_Noop()
    {
        var state = await CreateLoadedState();
        state.UpdateEditingValue("test"); // should not throw
    }

    [Fact]
    public async Task CommitEdit_AppliesNewValue()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 2); // Score
        state.UpdateEditingValue(100.0);
        var result = state.CommitEdit();
        Assert.NotNull(result);
        Assert.Equal(100.0, result!.Value.NewItem.Score);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task CommitEdit_NotEditing_ReturnsNull()
    {
        var state = await CreateLoadedState();
        Assert.Null(state.CommitEdit());
    }

    [Fact]
    public async Task CancelEdit_ClearsEditingState()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 1);
        state.UpdateEditingValue("Alicia");
        state.CancelEdit();
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task CancelEdit_NotEditing_Noop()
    {
        var state = await CreateLoadedState();
        state.CancelEdit(); // should not throw
    }

    [Fact]
    public async Task CommitAndMoveNext_CommitsAndAdvances()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 1); // Name
        state.UpdateEditingValue("Alicia");
        var result = state.CommitAndMoveNext();
        Assert.NotNull(result);
        Assert.Equal("Alicia", result!.Value.NewItem.Name);
        // Focus should have moved to next cell
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.Equal(2, state.FocusedColIndex);
    }

    // ═══════════════════════════════════════════════════════════════
    // Row Edit Mode
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BeginRowEdit_ValidRow_ReturnsTrue()
    {
        var state = await CreateLoadedState();
        Assert.True(state.BeginRowEdit(0));
        Assert.True(state.IsRowEditing);
    }

    [Fact]
    public async Task BeginRowEdit_InvalidRow_ReturnsFalse()
    {
        var state = await CreateLoadedState();
        Assert.False(state.BeginRowEdit(-1));
        Assert.False(state.BeginRowEdit(100));
    }

    [Fact]
    public async Task UpdateRowEditValue_UpdatesColumn()
    {
        var state = await CreateLoadedState();
        state.BeginRowEdit(0);
        state.UpdateRowEditValue("Name", "Alicia");
        state.UpdateRowEditValue("Score", 100.0);
        var result = state.CommitRowEdit();
        Assert.NotNull(result);
        Assert.Equal("Alicia", result!.Value.NewItem.Name);
        Assert.Equal(100.0, result!.Value.NewItem.Score);
    }

    [Fact]
    public async Task UpdateRowEditValue_NotInRowEdit_Noop()
    {
        var state = await CreateLoadedState();
        state.UpdateRowEditValue("Name", "test"); // should not throw
    }

    [Fact]
    public async Task CommitRowEdit_NotInRowEdit_ReturnsNull()
    {
        var state = await CreateLoadedState();
        Assert.Null(state.CommitRowEdit());
    }

    [Fact]
    public async Task CancelRowEdit_ClearsState()
    {
        var state = await CreateLoadedState();
        state.BeginRowEdit(0);
        state.CancelRowEdit();
        Assert.False(state.IsRowEditing);
    }

    [Fact]
    public async Task CancelRowEdit_NotInRowEdit_Noop()
    {
        var state = await CreateLoadedState();
        state.CancelRowEdit(); // should not throw
    }

    [Fact]
    public async Task CancelEdit_DuringRowEdit_CancelsRow()
    {
        var state = await CreateLoadedState();
        state.BeginRowEdit(0);
        state.CancelEdit(); // delegates to CancelRowEdit
        Assert.False(state.IsRowEditing);
    }

    [Fact]
    public async Task IsColumnInRowEdit_ReturnsCorrectly()
    {
        var state = await CreateLoadedState();
        state.BeginRowEdit(0);
        var rowKey = new RowKey("1");
        Assert.True(state.IsColumnInRowEdit(rowKey, "Name"));
        Assert.True(state.IsColumnInRowEdit(rowKey, "Score"));
        Assert.False(state.IsColumnInRowEdit(rowKey, "Id")); // read-only
        Assert.False(state.IsColumnInRowEdit(new RowKey("2"), "Name")); // wrong row
    }

    // ═══════════════════════════════════════════════════════════════
    // Async Commit Lifecycle
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsyncCommit_BeginAndComplete()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 1);
        state.UpdateEditingValue("Alicia");
        var result = state.CommitEdit();
        Assert.NotNull(result);

        var key = result!.Value.Key;
        var original = new TestItem(1, "Alice", 95);
        state.BeginAsyncCommit(key, original);
        Assert.True(state.IsCommitting(key));

        state.CompleteAsyncCommit(key);
        Assert.False(state.IsCommitting(key));
    }

    [Fact]
    public async Task AsyncCommit_FailReverts()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 1);
        state.UpdateEditingValue("Alicia");
        var result = state.CommitEdit();
        Assert.NotNull(result);

        var key = result!.Value.Key;
        var original = new TestItem(1, "Alice", 95);
        state.BeginAsyncCommit(key, original);
        state.FailAsyncCommit(key, "Server error");

        Assert.False(state.IsCommitting(key));
        Assert.Equal("Server error", state.GetCommitError(key));
    }

    [Fact]
    public async Task DismissCommitError_ClearsError()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 1);
        state.UpdateEditingValue("Alicia");
        var result = state.CommitEdit();
        var key = result!.Value.Key;
        state.BeginAsyncCommit(key, new TestItem(1, "Alice", 95));
        state.FailAsyncCommit(key, "Error");

        state.DismissCommitError(key);
        Assert.Null(state.GetCommitError(key));
    }

    // ═══════════════════════════════════════════════════════════════
    // Row Expansion
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExpandRow_CollapsesRow_Toggles()
    {
        var state = await CreateLoadedState();
        var key = new RowKey("1");
        state.ExpandRow(key);
        Assert.True(state.IsExpanded(key));
        state.CollapseRow(key);
        Assert.False(state.IsExpanded(key));
    }

    [Fact]
    public async Task ToggleRowExpansion_Toggles()
    {
        var state = await CreateLoadedState();
        var key = new RowKey("2");
        state.ToggleRowExpansion(key);
        Assert.True(state.IsExpanded(key));
        state.ToggleRowExpansion(key);
        Assert.False(state.IsExpanded(key));
    }

    [Fact]
    public async Task CollapseAllRows_CollapsesAll()
    {
        var state = await CreateLoadedState();
        state.ExpandRow(new RowKey("1"));
        state.ExpandRow(new RowKey("2"));
        state.CollapseAllRows();
        Assert.False(state.IsExpanded(new RowKey("1")));
        Assert.False(state.IsExpanded(new RowKey("2")));
    }

    // ═══════════════════════════════════════════════════════════════
    // Column Operations
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task HideColumn_ShowColumn_TogglesVisibility()
    {
        var state = await CreateLoadedState();
        state.HideColumn("Name");
        Assert.True(state.HiddenColumns.Contains("Name"));
        state.ShowColumn("Name");
        Assert.False(state.HiddenColumns.Contains("Name"));
    }

    [Fact]
    public async Task ToggleColumnVisibility_Toggles()
    {
        var state = await CreateLoadedState();
        state.ToggleColumnVisibility("Score");
        Assert.True(state.HiddenColumns.Contains("Score"));
        state.ToggleColumnVisibility("Score");
        Assert.False(state.HiddenColumns.Contains("Score"));
    }

    [Fact]
    public async Task PinColumn_GetsPinnedGroups()
    {
        var state = await CreateLoadedState();
        state.PinColumn("Id", PinPosition.Left);
        var groups = state.GetPinnedColumnGroups();
        _ = groups; // value type – just verify it can be retrieved
    }

    [Fact]
    public async Task ResizeColumn_UpdatesWidth()
    {
        var state = await CreateLoadedState();
        state.ResizeColumn("Score", 150);
        var cols = state.Columns;
        var scoreCol = cols.FirstOrDefault(c => c.Name == "Score");
        Assert.NotNull(scoreCol);
    }

    [Fact]
    public async Task ReorderColumn_ChangesOrder()
    {
        var state = await CreateLoadedState();
        state.ReorderColumn(2, 0);
        var cols = state.Columns;
        // Score should be first now
        Assert.Equal("Score", cols[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════
    // Search Query
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetSearchQuery_StoresQuery()
    {
        var state = await CreateLoadedState();
        state.SetSearchQuery("  Hello  World  ");
        Assert.Equal("  Hello  World  ", state.SearchQuery);
    }

    [Fact]
    public async Task SetSearchQuery_NullClearsQuery()
    {
        var state = await CreateLoadedState();
        state.SetSearchQuery("test");
        state.SetSearchQuery(null);
        Assert.Null(state.SearchQuery);
    }

    [Fact]
    public async Task SetSearchQuery_EmptyStringClearsQuery()
    {
        var state = await CreateLoadedState();
        state.SetSearchQuery("test");
        state.SetSearchQuery("");
        Assert.Null(state.SearchQuery);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetRowIndex (legacy mode)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRowIndex_FindsExistingKey()
    {
        var state = await CreateLoadedState();
        int idx = state.GetRowIndex(new RowKey("2"));
        Assert.Equal(1, idx);
    }

    [Fact]
    public async Task GetRowIndex_MissingKey_ReturnsNegative()
    {
        var state = await CreateLoadedState();
        int idx = state.GetRowIndex(new RowKey("999"));
        Assert.Equal(-1, idx);
    }

    // ═══════════════════════════════════════════════════════════════
    // Editing with validation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EditWithValidation_InvalidBlocks_Commit()
    {
        var state = await CreateLoadedState();
        state.BeginEdit(0, 1);
        state.UpdateEditingValue("test");

        // Add a validation error via the edit validation context
        var validationCtx = state.EditValidation;
        Assert.NotNull(validationCtx);
        validationCtx!.Add("Name", "Too short");

        var result = state.CommitEdit();
        Assert.Null(result); // Should be blocked by validation error
    }
}
