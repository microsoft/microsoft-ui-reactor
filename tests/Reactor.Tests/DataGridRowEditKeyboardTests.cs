using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Data;
using Xunit;
using VirtualKey = global::Windows.System.VirtualKey;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #853 regression cover: keyboard handling while a row is in <see cref="EditMode.Row"/> edit.
///
/// <para><see cref="DataGridState{T}.IsEditing"/> is <c>_editingRowKey is not null</c>, and
/// <c>BeginRowEdit</c> sets that key — so <c>IsEditing</c> is TRUE during a row edit while
/// <c>EditingColumnName</c> stays null (the "row mode" signal). Before the fix,
/// <c>HandleKeyDown</c>'s single-CELL block therefore swallowed Enter and Tab and routed them into
/// <c>CommitEdit()</c>, which commits with a null column name: it drops the row's pending
/// <c>_rowEditValues</c>, clears <c>_editingRowKey</c> while leaving <c>_isRowEditing</c> set, and
/// (for Tab) starts a stray cell edit.</para>
///
/// <para>These tests drive the real private handlers through the component's test seams and assert
/// on the resulting DATA and state, so each one fails if its target branch is removed.</para>
/// </summary>
public class DataGridRowEditKeyboardTests
{
    private record TestItem(int Id, string Name, double Score);

    private sealed class TestDataSource : IDataSource<TestItem>
    {
        private readonly List<TestItem> _items;
        public TestDataSource()
            => _items =
            [
                new TestItem(1, "Alice", 95),
                new TestItem(2, "Bob", 87),
                new TestItem(3, "Carol", 92),
            ];

        public Task<DataPage<TestItem>> GetPageAsync(DataRequest request, CancellationToken ct = default)
            => Task.FromResult(new DataPage<TestItem>(_items, TotalCount: _items.Count));

        public RowKey GetRowKey(TestItem item) => new(item.Id.ToString());
        public DataSourceCapabilities Capabilities => DataSourceCapabilities.None;
    }

    // Column 0 is READ-ONLY on purpose: row-mode Tab must skip it, because BeginRowEdit never
    // gives it an editor.
    private const int IdCol = 0;
    private const int NameCol = 1;
    private const int ScoreCol = 2;

    private static readonly FieldDescriptor[] Columns =
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

    private static async Task<DataGridState<TestItem>> LoadedState()
    {
        var state = new DataGridState<TestItem>(new TestDataSource(), Columns, SelectionMode.None);
        await state.LoadDataAsync();
        return state;
    }

    // OnRowChanged stays null so HandleAsyncCommit (and its DispatcherQueue.GetForCurrentThread())
    // is never reached in a headless run — commits are asserted against the state's own items.
    private static DataGridElement<TestItem> Grid(EditMode mode) => new()
    {
        Source = new TestDataSource(),
        Columns = Columns,
        Editable = true,
        EditMode = mode,
        SelectionMode = SelectionMode.None,
        OnRowChanged = null,
    };

    private static void Key(DataGridState<TestItem> state, DataGridElement<TestItem> el, VirtualKey key)
        => DataGridComponent<TestItem>.HandleKeyDownForTests(
            state, el, new KeyChord(key, Shift: false, Ctrl: false));

    private static bool Should(DataGridState<TestItem> state, DataGridElement<TestItem> el, VirtualKey key)
        => DataGridComponent<TestItem>.ShouldHandleKeyForTests(
            state, el, new KeyChord(key, Shift: false, Ctrl: false));

    // Same shape as Columns, except Name carries a Required validator so a blank pending value
    // makes CommitRowEdit() bail out and leave the row open.
    private static readonly FieldDescriptor[] ValidatedColumns =
    [
        Columns[IdCol],
        Columns[NameCol] with { Validators = [Validate.Required()] },
        Columns[ScoreCol],
    ];

    private static async Task<DataGridState<TestItem>> ValidatedState()
    {
        var state = new DataGridState<TestItem>(new TestDataSource(), ValidatedColumns, SelectionMode.None);
        await state.LoadDataAsync();
        return state;
    }

    private static DataGridElement<TestItem> ValidatedGrid() => new()
    {
        Source = new TestDataSource(),
        Columns = ValidatedColumns,
        Editable = true,
        EditMode = EditMode.Row,
        SelectionMode = SelectionMode.None,
        OnRowChanged = null,
    };

    private static TestItem Row(DataGridState<TestItem> state, int index)
        => state.GetItemAt(index)!;

    // ── Tab must not run the single-cell commit path ─────────────────

    [Fact]
    public async Task RowEditTab_DoesNotCommit_AndKeepsRowEditStateIntact()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);

        Assert.True(state.BeginRowEdit(0));
        state.UpdateRowEditValue("Name", "Alicia");
        state.UpdateRowEditValue("Score", 100.0);

        Key(state, el, VirtualKey.Tab);

        // The row is still being edited, with its pending values untouched...
        Assert.True(state.IsRowEditing);
        Assert.NotNull(state.EditingRowKey);
        Assert.Null(state.EditingColumnName);
        Assert.Equal("Alicia", state.GetRowEditValue("Name"));
        Assert.Equal(100.0, state.GetRowEditValue("Score"));

        // ...and nothing was written through to the item. Under the bug this either threw
        // (null column name into the name→index dictionary) or cleared _editingRowKey while
        // leaving _isRowEditing true.
        Assert.Equal("Alice", Row(state, 0).Name);
        Assert.Equal(95.0, Row(state, 0).Score);
    }

    [Fact]
    public async Task RowEditTab_MovesToNextEditableColumn_SkippingReadOnly()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);

        state.SetFocus(0, NameCol);
        Assert.True(state.BeginRowEdit(0));

        Key(state, el, VirtualKey.Tab);
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.Equal(0, state.FocusedRowIndex);

        // Past the last column it wraps back INSIDE the row: to Name, never to the read-only Id
        // column, and never down to the next row the way the cell-mode FocusNextCell would.
        Key(state, el, VirtualKey.Tab);
        Assert.Equal(NameCol, state.FocusedColIndex);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.True(state.IsRowEditing);
    }

    [Fact]
    public async Task RowEditTab_WithNoPriorColumnFocus_LandsOnFirstEditableColumn()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);

        // BeginRowEdit sets only the focused ROW, so the column index is still -1 here — the
        // shape you get when the row edit starts from the row's Edit button.
        Assert.True(state.BeginRowEdit(1));
        Assert.Equal(-1, state.FocusedColIndex);

        Key(state, el, VirtualKey.Tab);

        Assert.Equal(NameCol, state.FocusedColIndex);
        Assert.Equal(1, state.FocusedRowIndex);
    }

    [Fact]
    public async Task FocusNextRowEditColumn_ReturnsFalse_WhenNotRowEditing()
    {
        var state = await LoadedState();
        state.SetFocus(0, NameCol);

        Assert.False(state.FocusNextRowEditColumn());
        Assert.Equal(NameCol, state.FocusedColIndex);
    }

    [Fact]
    public async Task RowEditTab_SkipsHiddenColumns()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);

        // BeginRowEdit snapshots pending values from the FULL column list, so a hidden editable
        // column lands in _rowEditValues even though the row renders no editor for it. Tab must
        // not park focus on a cell the user cannot see.
        state.HideColumn("Name");
        state.SetFocus(0, IdCol);
        Assert.True(state.BeginRowEdit(0));
        Assert.True(state.GetRowEditValue("Name") is not null);

        Key(state, el, VirtualKey.Tab);

        // Name is the very next column and IS in _rowEditValues, so a traversal that ignored
        // visibility would stop there. Landing on Score instead proves both that the hidden column
        // was stepped over and that the traversal ran at all — Score is neither Name nor the IdCol
        // it started on.
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.True(state.IsRowEditing);
    }

    [Fact]
    public async Task FocusNextRowEditColumn_ReturnsFalse_WhenEveryEditableColumnIsHidden()
    {
        var state = await LoadedState();

        state.HideColumn("Name");
        state.HideColumn("Score");
        state.SetFocus(0, IdCol);
        Assert.True(state.BeginRowEdit(0));

        // Nowhere visible to go — report that rather than landing on the read-only Id column or
        // spinning on a hidden one.
        Assert.False(state.FocusNextRowEditColumn());
        Assert.Equal(IdCol, state.FocusedColIndex);
        Assert.True(state.IsRowEditing);

        // Differential isolation: the ONLY thing that changes here is Name's visibility, so the
        // false above has to be the visibility check talking and not a method that never moves.
        state.ShowColumn("Name");
        Assert.True(state.FocusNextRowEditColumn());
        Assert.Equal(NameCol, state.FocusedColIndex);
    }

    [Fact]
    public async Task FocusPrevRowEditColumn_WalksBackwardAndWraps()
    {
        var state = await LoadedState();

        // Start on the read-only Id column. With only Name and Score editable, that is the one
        // position where forward and backward traversal disagree — from anywhere else the two
        // directions ping-pong between the same two columns, so a test that started elsewhere
        // would pass even if this method walked forward.
        state.SetFocus(0, IdCol);
        Assert.True(state.BeginRowEdit(0));

        // Backward off column 0 wraps to the LAST editable column, not the first.
        Assert.True(state.FocusPrevRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);

        // Landing on Name rather than Id is itself the proof that the read-only column is skipped
        // going backward, just as it was going forward.
        Assert.True(state.FocusPrevRowEditColumn());
        Assert.Equal(NameCol, state.FocusedColIndex);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.True(state.IsRowEditing);

        // Differential isolation: same grid, same start, only the direction differs.
        var forward = await LoadedState();
        forward.SetFocus(0, IdCol);
        Assert.True(forward.BeginRowEdit(0));
        Assert.True(forward.FocusNextRowEditColumn());
        Assert.Equal(NameCol, forward.FocusedColIndex);
    }

    [Fact]
    public async Task RowEditTab_OnSingleEditableColumn_DoesNotRaiseARedundantStateChanged()
    {
        var state = await LoadedState();

        // Leave exactly one visible editable column, so the wrap lands back on the start.
        state.HideColumn("Score");
        state.SetFocus(0, NameCol);
        Assert.True(state.BeginRowEdit(0));

        var renders = 0;
        state.StateChanged += () => renders++;

        // Tab has a valid target (Name), it just happens to be the current one — report success
        // without asking the grid to re-render for a move that didn't move.
        Assert.True(state.FocusNextRowEditColumn());
        Assert.Equal(NameCol, state.FocusedColIndex);
        Assert.Equal(0, renders);

        // Differential isolation: give it somewhere to go and the notification comes back.
        state.ShowColumn("Score");
        renders = 0;
        Assert.True(state.FocusNextRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.Equal(1, renders);
    }

    [Fact]
    public async Task FocusPrevRowEditColumn_WithNoPriorColumnFocus_LandsOnLastEditableColumn()
    {
        var state = await LoadedState();

        // BeginRowEdit from the Edit button leaves FocusedColIndex at -1. Forward from there lands
        // on the FIRST editable column, so backward has to land on the LAST one. A traversal that
        // just plugs -1 into the modulo starts at colCount - 2 and can never reach the last column.
        Assert.True(state.BeginRowEdit(0));
        Assert.Equal(-1, state.FocusedColIndex);

        Assert.True(state.FocusPrevRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);

        // Differential isolation against the forward direction from the same -1 start.
        var forward = await LoadedState();
        Assert.True(forward.BeginRowEdit(0));
        Assert.Equal(-1, forward.FocusedColIndex);
        Assert.True(forward.FocusNextRowEditColumn());
        Assert.Equal(NameCol, forward.FocusedColIndex);
    }

    [Fact]
    public async Task FocusPrevRowEditColumn_ReturnsFalse_WhenNotRowEditing()
    {
        var state = await LoadedState();
        state.SetFocus(0, ScoreCol);

        Assert.False(state.FocusPrevRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);
    }

    // ── Enter / Escape go to the ROW api, not the cell api ───────────

    [Fact]
    public async Task RowEditEnter_CommitsTheWholeRow()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);

        Assert.True(state.BeginRowEdit(0));
        state.UpdateRowEditValue("Name", "Alicia");
        state.UpdateRowEditValue("Score", 100.0);

        Key(state, el, VirtualKey.Enter);

        // BOTH columns landed — a single-cell commit can only ever apply one, so this is what
        // separates CommitRowEdit() from the CommitEdit() the cell branch used to run.
        Assert.Equal("Alicia", Row(state, 0).Name);
        Assert.Equal(100.0, Row(state, 0).Score);

        // ...and every piece of edit state is cleared together.
        Assert.False(state.IsRowEditing);
        Assert.False(state.IsEditing);
        Assert.Null(state.EditingRowKey);
        Assert.Null(state.GetRowEditValue("Name"));
    }

    [Fact]
    public async Task RowEditEnter_WithValidationError_KeepsTheRowOpenAndCommitsNothing()
    {
        var state = await ValidatedState();
        var el = ValidatedGrid();

        Assert.True(state.BeginRowEdit(0));
        state.UpdateRowEditValue("Name", "");        // fails Validate.Required()
        state.UpdateRowEditValue("Score", 100.0);    // valid, but must not sneak through

        var validation = state.EditValidation;
        Assert.NotNull(validation);
        Assert.False(validation!.IsValid());

        Key(state, el, VirtualKey.Enter);

        // CommitRowEdit() returns null on a validation failure and leaves the row in edit mode, so
        // the Enter branch must not clear state or half-apply the row behind it.
        Assert.True(state.IsRowEditing);
        Assert.NotNull(state.EditingRowKey);
        Assert.Equal("Alice", Row(state, 0).Name);
        Assert.Equal(95.0, Row(state, 0).Score);

        // The pending edits are still there for the user to fix.
        Assert.Equal("", state.GetRowEditValue("Name"));
        Assert.Equal(100.0, state.GetRowEditValue("Score"));

        // Fixing the error and pressing Enter again commits — proving the block above was the
        // validator talking, not an Enter branch that never commits.
        state.UpdateRowEditValue("Name", "Alicia");
        Key(state, el, VirtualKey.Enter);

        Assert.False(state.IsRowEditing);
        Assert.Equal("Alicia", Row(state, 0).Name);
        Assert.Equal(100.0, Row(state, 0).Score);
    }

    [Fact]
    public async Task RowEditEscape_CancelsTheWholeRow()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);

        Assert.True(state.BeginRowEdit(0));
        state.UpdateRowEditValue("Name", "Discarded");
        state.UpdateRowEditValue("Score", 0.0);

        Key(state, el, VirtualKey.Escape);

        Assert.False(state.IsRowEditing);
        Assert.False(state.IsEditing);
        Assert.Equal("Alice", Row(state, 0).Name);
        Assert.Equal(95.0, Row(state, 0).Score);
    }

    // ── State-level guard: every CommitEdit caller is safe ───────────

    [Fact]
    public async Task CommitEdit_DuringRowEdit_DelegatesToCommitRowEdit()
    {
        var state = await LoadedState();

        Assert.True(state.BeginRowEdit(0));
        state.UpdateRowEditValue("Name", "Alicia");
        state.UpdateRowEditValue("Score", 100.0);

        var result = state.CommitEdit();

        Assert.NotNull(result);
        Assert.Equal("Alicia", result!.Value.NewItem.Name);
        Assert.Equal(100.0, result!.Value.NewItem.Score);
        Assert.Equal("Alicia", Row(state, 0).Name);
        Assert.Equal(100.0, Row(state, 0).Score);
        Assert.False(state.IsRowEditing);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public async Task RowPointerClickOnAnotherRow_DuringRowEdit_CommitsTheRow()
    {
        var state = await LoadedState();

        Assert.True(state.BeginRowEdit(0));
        state.UpdateRowEditValue("Name", "Alicia");
        state.UpdateRowEditValue("Score", 100.0);

        // Clicking a DIFFERENT row routes through CommitInFlightEditThroughDispatcher(), which is
        // guarded only by IsEditing — the second doorway into the null-column cell commit.
        var otherRow = new RowKey(state.GetRowKeyAt(2)!);
        state.InvokeRowPointerClick(otherRow, ctrlKey: false, shiftKey: false);

        Assert.Equal("Alicia", Row(state, 0).Name);
        Assert.Equal(100.0, Row(state, 0).Score);
        Assert.False(state.IsRowEditing);
        Assert.Equal(2, state.FocusedRowIndex);
    }

    // ── Cell mode is untouched ───────────────────────────────────────

    [Fact]
    public async Task CellEditTab_StillCommitsTheCellAndReopensOnTheNextOne()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Cell);

        state.SetFocus(0, NameCol);
        Assert.True(state.BeginEdit(0, NameCol));
        state.UpdateEditingValue("Alicia");

        Key(state, el, VirtualKey.Tab);

        // The edited cell committed, focus advanced, and the editor reopened on the next cell.
        Assert.Equal("Alicia", Row(state, 0).Name);
        Assert.Equal(95.0, Row(state, 0).Score); // untouched by a cell commit
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.True(state.IsEditing);
        Assert.False(state.IsRowEditing);
        Assert.Equal("Score", state.EditingColumnName);
    }

    [Fact]
    public async Task CellEditEnter_StillCommitsOnlyTheEditedCell()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Cell);

        state.SetFocus(0, NameCol);
        Assert.True(state.BeginEdit(0, NameCol));
        state.UpdateEditingValue("Alicia");

        Key(state, el, VirtualKey.Enter);

        Assert.Equal("Alicia", Row(state, 0).Name);
        Assert.False(state.IsEditing);
        Assert.Null(state.EditingColumnName);
    }

    // ── Key filter ───────────────────────────────────────────────────

    [Fact]
    public async Task ShouldHandleKey_DuringRowEdit_ClaimsOnlyEnterEscapeAndTab()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);
        Assert.True(state.BeginRowEdit(0));

        Assert.True(Should(state, el, VirtualKey.Enter));
        Assert.True(Should(state, el, VirtualKey.Escape));
        Assert.True(Should(state, el, VirtualKey.Tab));

        // Arrows/Home/End must reach the focused editor for in-text caret movement.
        Assert.False(Should(state, el, VirtualKey.Down));
        Assert.False(Should(state, el, VirtualKey.Left));
        Assert.False(Should(state, el, VirtualKey.Home));
        Assert.False(Should(state, el, VirtualKey.F2));
    }
}
