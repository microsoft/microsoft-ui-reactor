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
    public async Task RowEditTab_WithNoPriorColumnFocus_LandsOnTheColumnAfterTheFirstEditable()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);

        // Started from the row's Edit button — no prior column focus. Since #976 BeginRowEdit puts
        // REAL focus on the row's first visible editor, so it parks the logical cursor there too;
        // leaving it at -1 would make this first Tab move the cursor onto the column focus is
        // already on and the keystroke would visibly do nothing.
        Assert.True(state.BeginRowEdit(1));
        Assert.Equal(NameCol, state.FocusedColIndex);

        Key(state, el, VirtualKey.Tab);

        Assert.Equal(ScoreCol, state.FocusedColIndex);
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
        Assert.True(state.BeginRowEdit(0));
        Assert.True(state.GetRowEditValue("Name") is not null);

        // Park the cursor on the read-only Id column AFTER BeginRowEdit: since #976 BeginRowEdit
        // moves the cursor to whichever column it gives real focus to (here Score, the only visible
        // editor), and starting the traversal there would make "landed on Score" vacuously true.
        state.SetFocus(0, IdCol);

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

        // Start on the read-only Id column. With only Name and Score editable the ring has size 2,
        // so exactly TWO origins make forward and backward disagree: IdCol, and the -1 no-prior-focus
        // sentinel used by the test further down this file. From either editable column both
        // directions land on the same place, so a test started there passes even if this method
        // walks forward.
        //
        // ORDERING IS LOAD-BEARING, and it changed with #976. BeginRowEdit now parks the cursor on
        // the first editable column it gives real focus to, so a SetFocus(IdCol) placed BEFORE it is
        // silently rewritten from Id(0) to Name(1) — collapsing the one origin that can tell the two
        // directions apart. #987 measured what that costs, in order:
        //   1. only the FORWARD destination constant fails (Expected 1 / Actual 2), which invites the
        //      reader to update that one number;
        //   2. after that repair the file is green with backward == forward == ScoreCol — direction-
        //      blind;
        //   3. inverting MoveRowEditFocus then leaves this test PASSING (5 killers become 4).
        // Parking AFTER BeginRowEdit keeps the discriminating origin. The origin assertion below is
        // kept from #987, but note it is weaker in this position: it now checks that SetFocus honours
        // a read-only column rather than that BeginRowEdit left the cursor alone. The direction claim
        // itself is carried by the step-matched cross-arm NotEqual at the end of this test.
        Assert.True(state.BeginRowEdit(0));
        state.SetFocus(0, IdCol);
        Assert.Equal(IdCol, state.FocusedColIndex);

        // Backward off column 0 wraps to the LAST editable column, not the first. This is the
        // step that carries the direction claim — one step from a shared origin, so it is the
        // half of the differential below that can actually disagree with forward traversal.
        Assert.True(state.FocusPrevRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        var backAfterOneStep = state.FocusedColIndex;

        // Landing on Name rather than Id is itself the proof that the read-only column is skipped
        // going backward, just as it was going forward.
        Assert.True(state.FocusPrevRowEditColumn());
        Assert.Equal(NameCol, state.FocusedColIndex);
        Assert.Equal(0, state.FocusedRowIndex);
        Assert.True(state.IsRowEditing);

        // Differential isolation — same grid, same origin, only the direction differs. The pairing
        // has to be step-matched to mean anything: backward's SECOND step also lands on NameCol, so
        // comparing the two arms' end states (NameCol both) discriminates nothing — with a 2-editable
        // ring the directions converge once the step counts diverge. The oracle is one step against
        // one step, i.e. backAfterOneStep (Score) against forward's first step (Name). The origin is
        // parked and asserted on this arm too, so a future change that moves the start column fails
        // both arms rather than whichever the runner reaches first.
        var forward = await LoadedState();
        Assert.True(forward.BeginRowEdit(0));
        forward.SetFocus(0, IdCol);
        Assert.Equal(IdCol, forward.FocusedColIndex);
        Assert.True(forward.FocusNextRowEditColumn());
        Assert.Equal(NameCol, forward.FocusedColIndex);

        // Cross-arm, and deliberately kept even though the two Equal()s above already entail it
        // while they hold distinct literals. Its job is not logical independence but EDIT
        // robustness: it is the only line here that still fails when a future edit "repairs" both
        // Equal()s to whatever a regressed tree reports, which is precisely how a direction test
        // goes permanently green. The two NotEqual()s this replaces were single-arm — each
        // compared against a distinct literal already pinned one line above, so no edit that
        // broke the Equal could ever trip them, and no value of the field could satisfy one and
        // fail the other (#1022).
        Assert.NotEqual(backAfterOneStep, forward.FocusedColIndex);
    }

    /// <summary>
    /// Issue #976. A row with only ONE visible editable column wraps Tab straight back to the
    /// column it started on, so there is no logical cursor move — but the render is still
    /// mandatory. By the time this runs, native Tab has already pushed real keyboard focus off the
    /// editor onto Save/Cancel, and only a render runs the hook that pulls it back. Skipping the
    /// notification as a "redundant re-render" optimisation makes Tab silently escape the row.
    /// </summary>
    [Fact]
    public async Task RowEditTab_OnSingleEditableColumn_StillArmsAndNotifiesSoFocusIsReclaimed()
    {
        var state = await LoadedState();

        // Leave exactly one visible editable column, so the wrap lands back on the start.
        state.HideColumn("Score");
        state.SetFocus(0, NameCol);
        Assert.True(state.BeginRowEdit(0));

        var renders = 0;
        state.StateChanged += () => renders++;
        var versionBefore = state.FocusRequestVersion;

        Assert.True(state.FocusNextRowEditColumn());

        // The cursor genuinely did NOT move — this really is the no-move path, so the render below
        // cannot be attributed to SetFocus.
        Assert.Equal(NameCol, state.FocusedColIndex);

        // A fresh request was armed for the column Tab landed on...
        Assert.True(state.FocusRequestVersion > versionBefore);
        Assert.True(state.TryConsumeEditorFocusRequest(new RowKey("1"), "Name"));

        // ...and a render was raised to consume it.
        Assert.Equal(1, renders);
    }

    /// <summary>
    /// Companion to the test above: when the cursor DOES move, the notification must come from
    /// <c>SetFocus</c> exactly once — not once from SetFocus plus a second explicit one.
    /// </summary>
    [Fact]
    public async Task RowEditTab_AcrossTwoEditableColumns_NotifiesExactlyOnce()
    {
        var state = await LoadedState();

        state.SetFocus(0, NameCol);
        Assert.True(state.BeginRowEdit(0));

        var renders = 0;
        state.StateChanged += () => renders++;

        Assert.True(state.FocusNextRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.Equal(1, renders);
        Assert.True(state.TryConsumeEditorFocusRequest(new RowKey("1"), "Score"));
    }

    [Fact]
    public async Task FocusPrevRowEditColumn_FromTheFirstEditableColumn_WrapsToTheLastEditableColumn()
    {
        var state = await LoadedState();

        // Started from the Edit button — no prior column focus. Since #976 BeginRowEdit focuses the
        // row's first visible editor and parks the cursor there, so this is backward-off-the-first
        // -editable-column. It must wrap to the LAST editable column rather than stalling on Name
        // or walking onto the read-only Id column.
        Assert.True(state.BeginRowEdit(0));
        Assert.Equal(NameCol, state.FocusedColIndex);

        Assert.True(state.FocusPrevRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.NotEqual(NameCol, state.FocusedColIndex);
        Assert.NotEqual(IdCol, state.FocusedColIndex);
        Assert.Equal(0, state.FocusedRowIndex);
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
