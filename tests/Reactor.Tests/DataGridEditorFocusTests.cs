using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Xunit;
using VirtualKey = global::Windows.System.VirtualKey;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #976 regression cover: the editor-focus REQUEST state machine on
/// <see cref="DataGridState{T}"/>.
///
/// <para>Before the fix every focus API on the state moved a purely logical cursor
/// (<c>_focusedRowIndex</c> / <c>_focusedColIndex</c>) and nothing ever asked XAML for real
/// keyboard focus, so opening a cell or row editor left the caret wherever it already was and
/// row-mode Tab walked native focus out of the grid entirely.</para>
///
/// <para>Headless tests can't construct a WinUI control, so the real <c>Focus()</c> call is covered
/// one tier up by the selftest fixtures. What IS testable here — and what these assert — is that
/// the arming/consuming contract the renderer depends on is exactly right: armed at the correct
/// (row, column), one-shot, cell-scoped, and cleared on every edit exit. Each assertion is written
/// differentially (before-vs-after, or against the state's own idea of where the cursor landed) so
/// it fails if the arming call it targets is deleted.</para>
/// </summary>
public class DataGridEditorFocusTests
{
    private record TestItem(int Id, string Name, double Score, string Notes);

    private sealed class TestDataSource : IDataSource<TestItem>
    {
        private readonly List<TestItem> _items =
        [
            new TestItem(1, "Alice", 95, "first"),
            new TestItem(2, "Bob", 87, "second"),
            new TestItem(3, "Carol", 92, "third"),
        ];

        public Task<DataPage<TestItem>> GetPageAsync(DataRequest request, CancellationToken ct = default)
        {
            // Honour the sort so a test can actually make the rows change places. The direction
            // doesn't matter — only that index N stops naming the same row.
            var items = request.Sort is { Count: > 0 }
                ? Enumerable.Reverse(_items).ToList()
                : _items;
            return Task.FromResult(new DataPage<TestItem>(items, TotalCount: items.Count));
        }

        public RowKey GetRowKey(TestItem item) => new(item.Id.ToString());
        public DataSourceCapabilities Capabilities => DataSourceCapabilities.None;
    }

    // "Id" is READ-ONLY and "Name" is HIDDEN in the fixture below, so the first column that
    // BeginRowEdit may legally focus is "Score" at index 2. A naive "column 0", "first column with
    // a SetValue", or "first key in the pending-values dictionary" implementation all pick the
    // wrong one and fail.
    private const int IdCol = 0;
    private const int NameCol = 1;
    private const int ScoreCol = 2;
    private const int NotesCol = 3;

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
        new FieldDescriptor
        {
            Name = "Notes",
            FieldType = typeof(string),
            GetValue = obj => ((TestItem)obj).Notes,
            SetValue = (obj, val) => ((TestItem)obj) with { Notes = (string)(val ?? "") },
        },
    ];

    private static async Task<DataGridState<TestItem>> LoadedState()
    {
        var state = new DataGridState<TestItem>(new TestDataSource(), Columns, SelectionMode.None);
        await state.LoadDataAsync();
        return state;
    }

    /// <summary>A state whose only editable VISIBLE column is "Score" — see the comment above.</summary>
    private static async Task<DataGridState<TestItem>> LoadedStateWithHiddenName()
    {
        var state = await LoadedState();
        state.HideColumn("Name");
        state.HideColumn("Notes");
        return state;
    }

    private static DataGridElement<TestItem> Grid(EditMode mode) => new()
    {
        Source = new TestDataSource(),
        Columns = Columns,
        Editable = true,
        EditMode = mode,
        SelectionMode = SelectionMode.None,
        OnRowChanged = null,
    };

    private static RowKey Row(int index) => new((index + 1).ToString());

    // ── Cell mode ───────────────────────────────────────────────────

    [Fact]
    public async Task BeginEdit_ArmsFocusRequestForTheCellBeingOpened()
    {
        var state = await LoadedState();

        // Differential: nothing is armed until BeginEdit arms it. A "returns true for any cell"
        // implementation fails right here.
        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Score"));

        Assert.True(state.BeginEdit(1, ScoreCol));

        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task BeginEdit_DoesNotArmAnyOtherCell()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));

        // Wrong column on the right row, and the right column on the wrong row, must both miss —
        // and must leave the real request intact rather than consuming it.
        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Notes"));
        Assert.False(state.TryConsumeEditorFocusRequest(Row(0), "Score"));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task ConsumingTheFocusRequestIsOneShot()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));

        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));

        // A re-render of the still-editing row must NOT yank focus back into the editor after the
        // user has moved on.
        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task HasEditorFocusRequest_DoesNotConsume()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));

        // The renderer uses this as a cheap gate on every cell; if it consumed, the first cell
        // rendered would eat the request meant for a later one.
        Assert.True(state.HasEditorFocusRequest(Row(1), "Score"));
        Assert.True(state.HasEditorFocusRequest(Row(1), "Score"));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task CommitEdit_ClearsPendingFocusRequest()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));
        state.UpdateEditingValue(42.0);

        Assert.NotNull(state.CommitEdit());

        // A request that outlives its edit would steal focus from wherever the user went next.
        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task CancelEdit_ClearsPendingFocusRequest()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));

        state.CancelEdit();

        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    // ── Row mode ────────────────────────────────────────────────────

    [Fact]
    public async Task BeginRowEdit_ArmsTheFirstVisibleEditableColumn()
    {
        var state = await LoadedStateWithHiddenName();

        Assert.True(state.BeginRowEdit(1));

        // NOT "Id" (read-only ⇒ no editor) and NOT "Name" (hidden ⇒ no rendered editor to focus).
        Assert.False(state.HasEditorFocusRequest(Row(1), "Id"));
        Assert.False(state.HasEditorFocusRequest(Row(1), "Name"));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task BeginRowEdit_MovesTheColumnCursorOntoTheFocusedEditor()
    {
        var state = await LoadedStateWithHiddenName();

        Assert.True(state.BeginRowEdit(1));

        // Real focus lands on the first VISIBLE editable column, so the logical cursor has to
        // agree with it — otherwise the first Tab would move the cursor onto the column focus is
        // already on and the keystroke would visibly do nothing (#976).
        //
        // Asserted against the armed column rather than a literal index, and the fixture hides
        // "Name" so a "cursor = first non-read-only column" implementation lands on 1 and fails.
        Assert.True(state.FocusedColIndex >= 0);
        Assert.Equal("Score", Columns[state.FocusedColIndex].Name);
        Assert.True(state.HasEditorFocusRequest(Row(1), Columns[state.FocusedColIndex].Name));
    }

    [Fact]
    public async Task BeginRowEdit_KeepsTheCaretInTheCellTheCursorWasAlreadyOn()
    {
        var state = await LoadedState();

        // Roving focus already on "Notes" (index 3), which is editable and visible. Starting a row
        // edit from there should keep the caret in Notes rather than yanking it back to the first
        // editable column, and must not move the cursor.
        state.SetFocus(1, 3);
        Assert.True(state.BeginRowEdit(1));

        Assert.Equal(3, state.FocusedColIndex);
        Assert.True(state.HasEditorFocusRequest(Row(1), "Notes"));
        Assert.False(state.HasEditorFocusRequest(Row(1), "Name"));
    }

    [Fact]
    public async Task BeginRowEdit_DoesNotParkOnTheCursorColumn_WhenThatColumnIsHidden()
    {
        var state = await LoadedState();

        // "Notes" is editable but HIDDEN. Preserving the cursor there would arm a focus request for
        // a column RenderRow never walks — it projects the VISIBLE columns — so nothing would ever
        // consume it: no editor takes focus, and the cursor names a cell the user cannot see. That
        // is the dead-keystroke bug #976 exists to remove, which is why the preserve guard tests
        // visibility as well as editability. Dropping the IsColumnVisible conjunct passes every
        // other test in this file.
        state.HideColumn("Notes");
        state.SetFocus(1, NotesCol);

        Assert.True(state.BeginRowEdit(1));

        // Falls through to the first VISIBLE editable column...
        Assert.Equal(NameCol, state.FocusedColIndex);
        // ...and arms the request there rather than on the hidden column. Both are asserted because
        // the cursor and the armed request are separate observables: a change that moved one without
        // the other would reintroduce the cursor/focus disagreement in a new place.
        Assert.True(state.HasEditorFocusRequest(Row(1), "Name"));
        Assert.False(state.HasEditorFocusRequest(Row(1), "Notes"));
    }

    [Fact]
    public async Task BeginRowEdit_MovesTheCursorToTheFallbackEditor_WhenTheCursorColumnHasNone()
    {
        var state = await LoadedState();

        // "Id" is read-only, so it renders no editor. Focus has to fall back to the first column
        // that does — and the cursor must follow it there.
        //
        // Leaving the cursor on Id (as the first cut of #976 did) reintroduces the dead-keystroke
        // bug in a subtler form: real focus is on Name(1) while the cursor still says Id(0), so the
        // first Tab traverses 0 → 1 and lands on the column focus is ALREADY on. The invariant is
        // that after BeginRowEdit the cursor names exactly the column that got real focus.
        state.SetFocus(1, 0);
        Assert.True(state.BeginRowEdit(1));

        Assert.Equal(1, state.FocusedColIndex);
        Assert.NotEqual(0, state.FocusedColIndex);
        Assert.True(state.HasEditorFocusRequest(Row(1), Columns[state.FocusedColIndex].Name));
        Assert.False(state.HasEditorFocusRequest(Row(1), "Id"));

        // ...and the first Tab therefore actually moves.
        Assert.True(state.FocusNextRowEditColumn());
        Assert.Equal(2, state.FocusedColIndex);
    }

    [Fact]
    public async Task BeginRowEdit_LeavesTheColumnCursorAlone_WhenNoVisibleEditorExists()
    {
        // Every editable column hidden ⇒ nothing to focus ⇒ the cursor must stay at the "no prior
        // focus" origin MoveRowEditFocus keys its wrap-around on.
        var state = await LoadedState();
        state.HideColumn("Name");
        state.HideColumn("Score");
        state.HideColumn("Notes");
        var before = state.FocusedColIndex;

        Assert.True(state.BeginRowEdit(1));

        Assert.Equal(before, state.FocusedColIndex);
        Assert.False(state.HasRowEditFocusTarget());
    }

    [Fact]
    public async Task CommitRowEdit_ClearsPendingFocusRequest()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));
        state.UpdateRowEditValue("Score", 55.0);

        Assert.NotNull(state.CommitRowEdit());

        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Name"));
        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task CancelRowEdit_ClearsPendingFocusRequest()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));

        state.CancelRowEdit();

        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Name"));
        Assert.False(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    // ── Row-mode traversal (#977's FocusNext/PrevRowEditColumn) ──────

    [Fact]
    public async Task FocusNextRowEditColumn_ReArmsAtTheColumnItActuallyLandsOn()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));
        // Drain BeginRowEdit's own request so what we observe below is the traversal's.
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Name"));

        Assert.True(state.FocusNextRowEditColumn());

        // Asserted against where the traversal says it landed, not a literal — a hardcoded
        // "always arm column N" implementation fails as soon as the fixture's columns change.
        var landed = Columns[state.FocusedColIndex].Name;
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), landed));
    }

    [Fact]
    public async Task FocusPrevRowEditColumn_ReArmsAtTheColumnItActuallyLandsOn()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Name"));

        // Direction-agnostic seam: #987's Shift+Tab plumbing gets real focus movement for free.
        Assert.True(state.FocusPrevRowEditColumn());

        var landed = Columns[state.FocusedColIndex].Name;
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), landed));
    }

    [Fact]
    public async Task FocusNextRowEditColumn_ArmsEvenWhenTheWrapLandsBackOnTheSameColumn()
    {
        // Single visible editable column: the traversal wraps onto itself and deliberately skips
        // SetFocus (nothing moved). Native Tab has still pushed keyboard focus off the editor, so
        // the request must be armed anyway or Tab silently escapes the grid.
        var state = await LoadedStateWithHiddenName();
        Assert.True(state.BeginRowEdit(1));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));

        // BeginRowEdit parks the cursor on the editor it focused, so with a single visible editable
        // column the cursor is already on Score and every Tab from here is the wrap-onto-itself
        // case. Asserting that first is what makes the wrap assertion below meaningful rather than
        // a coincidence of where the cursor happened to start.
        Assert.Equal(ScoreCol, state.FocusedColIndex);

        var versionBefore = state.FocusRequestVersion;
        Assert.True(state.FocusNextRowEditColumn());

        Assert.Equal(ScoreCol, state.FocusedColIndex); // proves nothing moved
        Assert.True(state.FocusRequestVersion > versionBefore);
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task RowEditTabThroughTheKeyHandlerArmsAFocusRequest()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);
        Assert.True(state.BeginRowEdit(1));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Name"));

        var versionBefore = state.FocusRequestVersion;
        DataGridComponent<TestItem>.HandleKeyDownForTests(state, el, KeyChord.Unmodified(VirtualKey.Tab));

        // The whole point of #976: the key path — not just the public API — has to arm.
        Assert.True(state.FocusRequestVersion > versionBefore);

        // Assert the DESTINATION, not merely that something moved. Reading the landing column back
        // out of the state (Columns[state.FocusedColIndex].Name) would consume whatever the state
        // decided and pass just as happily if the traversal inverted — the editable ring here is
        // {Name, Score, Notes}, so forward from Name is Score and backward is Notes, and only a
        // literal can tell those apart.
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));

        // …and it must not have committed or torn down the row on the way.
        Assert.True(state.IsRowEditing);
    }

    [Fact]
    public async Task RowEditShiftTabThroughTheKeyHandlerArmsABackwardFocusRequest()
    {
        var state = await LoadedState();
        var el = Grid(EditMode.Row);
        Assert.True(state.BeginRowEdit(1));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Name"));

        var versionBefore = state.FocusRequestVersion;
        // The backward twin, and the reason it exists: #987 carries the modifier through the
        // deferred dispatch, #976 turns the landing into a real focus request. Either half can
        // regress on its own — dropping Shift lands on Score with a request still armed, so a test
        // that only checked "a request was armed" would stay green through exactly the bug the
        // chord plumbing exists to prevent.
        DataGridComponent<TestItem>.HandleKeyDownForTests(state, el, new KeyChord(VirtualKey.Tab, Shift: true, Ctrl: false));

        Assert.True(state.FocusRequestVersion > versionBefore);

        // Backward off the FIRST editable column wraps to the LAST one. Notes is reachable only by
        // going backward from Name: forward would need two steps, and this test takes one.
        Assert.Equal(NotesCol, state.FocusedColIndex);
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Notes"));
        Assert.True(state.IsRowEditing);
    }

    [Fact]
    public async Task RowEditTabDirectionsDisagreeThroughTheKeyHandler()
    {
        // Cross-arm differential, step-matched: one Tab against one Shift+Tab from the SAME origin.
        // Its job is edit robustness rather than logical independence — it is the line that still
        // fails if someone "repairs" both destination literals above to whatever a regressed tree
        // reports, which is how a direction test goes permanently green.
        var forward = await LoadedState();
        var back = await LoadedState();
        var el = Grid(EditMode.Row);

        Assert.True(forward.BeginRowEdit(1));
        Assert.True(back.BeginRowEdit(1));
        Assert.Equal(back.FocusedColIndex, forward.FocusedColIndex); // same origin, or nothing below means anything

        DataGridComponent<TestItem>.HandleKeyDownForTests(forward, el, KeyChord.Unmodified(VirtualKey.Tab));
        DataGridComponent<TestItem>.HandleKeyDownForTests(back, el, new KeyChord(VirtualKey.Tab, Shift: true, Ctrl: false));

        Assert.NotEqual(back.FocusedColIndex, forward.FocusedColIndex);
    }

    /// <summary>
    /// The "no cursor yet" arm of <c>MoveRowEditFocus</c> — backward from -1 has to start one PAST
    /// the end so it settles on the LAST candidate, not the second-to-last.
    ///
    /// <para>#976 parks the cursor on whatever editor <c>BeginRowEdit</c> focuses, which deletes the
    /// scenario that arm was originally written for (row edit opened from the Edit button with no
    /// prior cell focus). It does NOT delete the arm: <c>BeginRowEdit</c> still returns true when the
    /// row has pending values but no VISIBLE editor, and leaves the cursor at -1. Column visibility
    /// is mutable during an edit — <c>ShowColumn</c> is public and does not guard on
    /// <c>_isRowEditing</c> — so an editor can appear afterwards and make the traversal live.</para>
    ///
    /// <para>The assertions name the two DESTINATIONS rather than "focus moved": the offset does not
    /// change which columns get swept (both forms visit all of them), only where the sweep starts,
    /// so an implementation missing it still returns true and still lands somewhere. Dropping the
    /// <c>colCount</c> origin makes the backward arm settle on Score instead of Notes, which is the
    /// exact value asserted below. The forward arm is the control — it does not use the offset and
    /// must stay on Score either way.</para>
    /// </summary>
    [Fact]
    public async Task RowEditTraversal_FromAnUnsetCursor_WalksBackwardToTheLastVisibleEditor()
    {
        var state = await LoadedState();

        // No VISIBLE editable column: Id is read-only, the other three are hidden. BeginRowEdit
        // still succeeds — the pending values exist — but has nothing to park the cursor on.
        state.HideColumn("Name");
        state.HideColumn("Score");
        state.HideColumn("Notes");
        Assert.True(state.BeginRowEdit(1));

        // Precondition, not decoration: if #976's park ever reaches this state the cursor is >= 0,
        // the arm under test is skipped, and both assertions below would hold for the wrong reason.
        Assert.Equal(-1, state.FocusedColIndex);

        // Two editors reappear mid-edit. Two, not one: with a single candidate every start position
        // converges on it and the arm becomes unobservable.
        state.ShowColumn("Score");
        state.ShowColumn("Notes");

        Assert.True(state.FocusPrevRowEditColumn());
        var backward = state.FocusedColIndex;

        var forward = await LoadedState();
        forward.HideColumn("Name");
        forward.HideColumn("Score");
        forward.HideColumn("Notes");
        Assert.True(forward.BeginRowEdit(1));
        Assert.Equal(-1, forward.FocusedColIndex);
        forward.ShowColumn("Score");
        forward.ShowColumn("Notes");
        Assert.True(forward.FocusNextRowEditColumn());

        Assert.Equal(NotesCol, backward);                 // last visible editable
        Assert.Equal(ScoreCol, forward.FocusedColIndex);  // first visible editable — control
        Assert.NotEqual(backward, forward.FocusedColIndex);
    }

    // ── HasRowEditFocusTarget (the KeyDown suppression gate) ─────────

    [Fact]
    public async Task HasRowEditFocusTarget_IsFalseOutsideARowEdit()
    {
        var state = await LoadedState();
        Assert.False(state.HasRowEditFocusTarget());

        // Cell edit is not row edit: IsEditing is true here, so a gate written against IsEditing
        // instead of IsRowEditing fails.
        Assert.True(state.BeginEdit(1, ScoreCol));
        Assert.False(state.HasRowEditFocusTarget());
    }

    [Fact]
    public async Task HasRowEditFocusTarget_IsTrueDuringARowEditWithAVisibleEditor()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));

        Assert.True(state.HasRowEditFocusTarget());
    }

    [Fact]
    public async Task HasRowEditFocusTarget_IsFalseWhenEveryEditableColumnIsHidden()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));

        state.HideColumn("Name");
        state.HideColumn("Score");
        state.HideColumn("Notes");

        // Nothing left to focus, so the KeyDown handler must NOT claim the next LostFocus —
        // arming SuppressNextLostFocusCommit here would swallow a later legitimate blur-commit.
        Assert.False(state.HasRowEditFocusTarget());
    }

    [Fact]
    public async Task HasRowEditFocusTarget_IsNonMutating()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));

        var colBefore = state.FocusedColIndex;
        var versionBefore = state.FocusRequestVersion;

        Assert.True(state.HasRowEditFocusTarget());
        Assert.True(state.HasRowEditFocusTarget());

        // The KeyDown handler calls this synchronously BEFORE the deferred HandleKeyDown; if it
        // moved the cursor or armed a request, the traversal that follows would start from the
        // wrong place.
        Assert.Equal(colBefore, state.FocusedColIndex);
        Assert.Equal(versionBefore, state.FocusRequestVersion);
        Assert.True(state.HasEditorFocusRequest(Row(1), "Name"));
    }

    // ── The KeyDown LostFocus-claim gate ────────────────────────────

    [Fact]
    public async Task ShouldClaimNextLostFocus_OnlyForTab()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));

        Assert.True(DataGridComponent<TestItem>.ShouldClaimNextLostFocus(state, VirtualKey.Tab));
        Assert.False(DataGridComponent<TestItem>.ShouldClaimNextLostFocus(state, VirtualKey.Enter));
        Assert.False(DataGridComponent<TestItem>.ShouldClaimNextLostFocus(state, VirtualKey.Escape));
        Assert.False(DataGridComponent<TestItem>.ShouldClaimNextLostFocus(state, VirtualKey.Down));
    }

    [Fact]
    public async Task ShouldClaimNextLostFocus_IsFalseWhenNotEditing()
    {
        var state = await LoadedState();

        // Navigation Tab doesn't move focus off an editor, so there is no blur-commit to claim —
        // claiming one would leave the flag armed to swallow a later real commit.
        Assert.False(DataGridComponent<TestItem>.ShouldClaimNextLostFocus(state, VirtualKey.Tab));
    }

    [Fact]
    public async Task ShouldClaimNextLostFocus_IsTrueForRowEditTab()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));

        // This is the #976 case: native Tab has already walked focus onto Save/Cancel, and the
        // deferred LostFocus check would commit the whole row before our focus request runs.
        Assert.True(DataGridComponent<TestItem>.ShouldClaimNextLostFocus(state, VirtualKey.Tab));
    }

    [Fact]
    public async Task ShouldClaimNextLostFocus_IsFalseForRowEditTabWithNothingToFocus()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));
        state.HideColumn("Name");
        state.HideColumn("Score");
        state.HideColumn("Notes");

        // No editor will be focused, so Tab really does leave the grid — the row SHOULD
        // blur-commit, and the one-shot flag must not be left armed for the next commit.
        Assert.False(DataGridComponent<TestItem>.ShouldClaimNextLostFocus(state, VirtualKey.Tab));
    }

    // ── Version counter ─────────────────────────────────────────────

    [Fact]
    public async Task FocusRequestVersion_AdvancesOnEveryArmIncludingARepeatOfTheSameCell()
    {
        var state = await LoadedState();

        var v0 = state.FocusRequestVersion;
        Assert.True(state.BeginEdit(1, ScoreCol));
        var v1 = state.FocusRequestVersion;
        Assert.True(v1 > v0);

        state.CancelEdit();
        Assert.True(state.BeginEdit(1, ScoreCol));

        // Re-opening the SAME cell must be distinguishable from "nothing happened", or a
        // version-keyed effect would never re-fire for a repeat edit of one cell.
        Assert.True(state.FocusRequestVersion > v1);
    }

    [Fact]
    public async Task FocusRequestVersion_AdvancesWhenTheSameCellIsReArmedWithNoInterveningConsume()
    {
        // Row 1's only visible editable column is "Score", so the Tab traversal wraps onto the
        // column BeginRowEdit already armed — a re-arm with the request still pending. An
        // implementation that short-circuits when the request already matches would stall the
        // version here, and the "did anything happen?" oracle would start lying.
        var state = await LoadedStateWithHiddenName();
        Assert.True(state.BeginRowEdit(1));
        Assert.True(state.HasEditorFocusRequest(Row(1), "Score"));

        var armed = state.FocusRequestVersion;
        Assert.True(state.FocusNextRowEditColumn());

        Assert.True(state.FocusRequestVersion > armed);
        Assert.True(state.HasEditorFocusRequest(Row(1), "Score"));
    }

    [Fact]
    public async Task FocusRequestVersion_IsNotRewoundByConsumeOrClear()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));
        var armed = state.FocusRequestVersion;

        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
        Assert.Equal(armed, state.FocusRequestVersion);

        state.CancelEdit();
        Assert.Equal(armed, state.FocusRequestVersion);
    }

    // ── The deferred-focus staleness guard ──────────────────────────
    //
    // A claimed request is applied on a LATER dispatcher tick, so the edit that armed it can end,
    // or a newer request can supersede it, before the Focus() call lands. IsFocusRequestStillCurrent
    // is the gate; it is a pure state predicate, so it is testable here rather than one tier up.

    [Fact]
    public async Task IsFocusRequestStillCurrent_IsTrueForTheLiveRequest()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));

        // Consuming deliberately does NOT advance the version, so the claim a hook took stays
        // valid across the deferral.
        Assert.True(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Score", state.FocusRequestVersion));
    }

    [Fact]
    public async Task IsFocusRequestStillCurrent_IsFalseOnceANewerRequestSupersedesIt()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));
        var claimed = state.FocusRequestVersion;
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));

        // Row-mode Tab pressed again before the first tick ran. Honouring the older claim now
        // would fight the newer one and land on whichever dispatcher tick happens to run last.
        state.CancelEdit();
        Assert.True(state.BeginEdit(1, NotesCol));

        Assert.False(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Score", claimed));

        // Differential: the NEW claim, taken at the new version, is still current.
        Assert.True(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Notes", state.FocusRequestVersion));
    }

    [Fact]
    public async Task IsFocusRequestStillCurrent_IsFalseWhenRowModeReArmsBeforeTheTickRuns()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));
        var claimed = state.FocusRequestVersion;

        // Establishes the differential: nothing about the row, the mode, or the column changes
        // across the FocusNextRowEditColumn below, so the flip from true to false can ONLY be the
        // version check talking.
        Assert.True(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Name", claimed));

        // Tab pressed a second time before the first claim's dispatcher tick ran.
        Assert.True(state.FocusNextRowEditColumn());

        // Honouring the older claim now would drag focus back to the cell the user just left, and
        // which of the two ticks wins would come down to dispatcher ordering.
        Assert.False(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Name", claimed));

        // The newer claim, taken at the newer version, is the one that must land.
        Assert.True(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Name", state.FocusRequestVersion));
    }

    [Theory]
    [InlineData(true)]  // commit
    [InlineData(false)] // cancel
    public async Task IsFocusRequestStillCurrent_IsFalseOnceTheCellEditEnded(bool commit)
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));
        var claimed = state.FocusRequestVersion;
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));
        Assert.True(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Score", claimed));

        // The edit ends between the claim and the deferred tick — Enter, Esc, or a blur-commit.
        // Focusing now would yank the caret back onto a control the user has left, and the pool
        // may already have recycled that control into a different row.
        if (commit) state.CommitEdit(); else state.CancelEdit();

        Assert.False(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Score", claimed));
    }

    [Fact]
    public async Task IsFocusRequestStillCurrent_IsFalseForADifferentRowOrColumn()
    {
        var state = await LoadedState();
        Assert.True(state.BeginEdit(1, ScoreCol));
        var claimed = state.FocusRequestVersion;

        Assert.False(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(2), "Score", claimed));
        Assert.False(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Notes", claimed));
    }

    [Fact]
    public async Task IsFocusRequestStillCurrent_IgnoresTheColumnInRowMode()
    {
        var state = await LoadedState();
        Assert.True(state.BeginRowEdit(1));
        var claimed = state.FocusRequestVersion;

        // Row mode keeps EVERY editable cell of the row open at once, so the row key is the whole
        // identity — a cell-mode-style column match would reject the Tab target and silently drop
        // every focus move after the first.
        Assert.True(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Notes", claimed));
        Assert.False(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(2), "Notes", claimed));

        state.CancelRowEdit();
        Assert.False(DataGridComponent<TestItem>.IsFocusRequestStillCurrent(
            state, Row(1), "Notes", claimed));
    }

    // ── Row-key keying survives reordering ──────────────────────────

    [Fact]
    public async Task FocusRequest_TracksTheRowKey_NotTheRowIndex()
    {
        var state = await LoadedState();

        var armedKey = new RowKey("3"); // Carol
        const int armedIndex = 2;

        Assert.Equal(armedIndex, state.GetRowIndex(armedKey)); // premise
        Assert.True(state.BeginEdit(armedIndex, ScoreCol));
        Assert.True(state.HasEditorFocusRequest(armedKey, "Score"));

        // Reorder the rows.
        state.ToggleSort("Score");
        await state.LoadDataAsync(TestContext.Current.CancellationToken);

        // The armed row must genuinely have moved, or everything below is vacuously true.
        Assert.NotEqual(armedIndex, state.GetRowIndex(armedKey));

        // Whoever slid into the armed INDEX is exactly who an index-keyed request would now be
        // pointing at — the wrong row's editor. Keyed on the row KEY, the request still names Carol.
        var occupant = state.GetItemAt(armedIndex);
        Assert.NotNull(occupant);
        var occupantKey = new RowKey(occupant!.Id.ToString());
        Assert.NotEqual(armedKey, occupantKey);

        Assert.True(state.HasEditorFocusRequest(armedKey, "Score"));
        Assert.False(state.HasEditorFocusRequest(occupantKey, "Score"));
    }

    /// <summary>
    /// The grid's KeyDown wiring guard must keep its <c>ConditionalWeakTable</c> on a
    /// <b>non-generic</b> holder, so that every closed <c>DataGridComponent&lt;T&gt;</c> shares one
    /// table.
    ///
    /// <para><b>Why this is a real defect and not a style preference.</b> A <c>static</c> field on a
    /// generic type gets one instance <i>per closed construction</i> — a language rule, not a design
    /// choice. <c>ElementPool</c> recycles by CLR type and <c>Microsoft.UI.Xaml.Controls.Grid</c> is
    /// on its poolable list (<c>ElementPool.cs:50</c>), so the very same pooled grid root can be
    /// returned by <c>DataGridComponent&lt;Person&gt;</c> and then rented by
    /// <c>DataGridComponent&lt;Order&gt;</c>. Were the table per-instantiation, the second
    /// component's table would hold no entry for that element, the unwire path would have nothing to
    /// remove, and KeyDown handlers would accumulate on the recycled root while a stale closure kept
    /// driving the previous grid's state — precisely the defect the wiring guard exists to prevent.
    /// The repo states this contract in <c>AGENTS.md</c> as one table; a <c>static</c> on a generic
    /// silently makes it N.</para>
    ///
    /// <para><b>Relationship to the selftest.</b> The behaviour already has a twin one tier up —
    /// <c>EditorFocus_GridKeyDownWiring_IsSharedAcrossClosedGenerics</c> in
    /// <c>DataGridEditFixtures.cs</c> wires the same control through <c>DataGridComponent</c> closed
    /// over two different row types and asserts the second call displaces the first. That is the
    /// authoritative check, because it exercises the real wiring path. This one is deliberately
    /// narrower and cheaper: it fails in the headless tier, without a WinUI window, and it fails on
    /// the <i>cause</i> (the table moved onto a per-closed-T holder) rather than on the downstream
    /// symptom, which is the difference between a one-line diagnosis and an investigation.</para>
    ///
    /// <para><b>Anti-vacuity.</b> The invariant is a negative, so it would pass for free against a
    /// field this test failed to find — the anchor is therefore pinned before it runs. The predicate
    /// is then exercised in both directions by two positive controls, including the case that looks
    /// harmless: a non-generic class <i>nested inside</i> a generic one is still per-closed-T, so
    /// merely moving the holder inside <c>DataGridComponent&lt;T&gt;</c> would reintroduce the
    /// bug.</para>
    /// </summary>
    [Fact]
    public void KeyDownWiringTable_IsSharedAcrossClosedGenerics_NotOnePerInstantiation()
    {
        var field = typeof(DataGridKeyDownWiring).GetField(
            "s_entries",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);

        Assert.True(
            field is not null,
            "Could not find the static ConditionalWeakTable backing DataGridKeyDownWiring. The "
            + "assertion below is a negative and would pass vacuously against a field this test "
            + "never located, so resolve the rename/removal before trusting any result from here.");

        Assert.False(
            IsPerClosedGeneric(field!.DeclaringType!),
            $"The KeyDown wiring table now lives on '{field.DeclaringType}', which is generic or is "
            + "nested inside a generic type. A static field on a generic gets one instance per closed "
            + "construction, so DataGridComponent<Person> and DataGridComponent<Order> would each get "
            + "their own table. ElementPool recycles by CLR type and Grid is poolable, so the same "
            + "grid root can move between them: the second instantiation would not see the first's "
            + "handler, handlers would accumulate, and a stale closure would keep driving the "
            + "previous grid's state. Keep the table on a non-generic holder.");

        // Both directions of the predicate, so a green above means the check looked and agreed
        // rather than that it cannot say no. The second control is the one that matters: it is what
        // "just move the helper inside DataGridComponent<T>" would produce, and it reads as
        // non-generic at a glance.
        Assert.True(IsPerClosedGeneric(typeof(DirectlyGenericHolder<int>)));
        Assert.True(IsPerClosedGeneric(typeof(GenericOuter<int>.NonGenericInner)));
    }

    /// <summary>
    /// Whether a static field declared on <paramref name="type"/> would be duplicated per closed
    /// generic construction — true when the type itself is generic, or when any type it is nested
    /// inside is.
    /// </summary>
    private static bool IsPerClosedGeneric(global::System.Type type)
    {
        for (global::System.Type? t = type; t is not null; t = t.DeclaringType)
        {
            if (t.IsGenericType || t.IsGenericTypeDefinition)
                return true;
        }

        return false;
    }

    private static class DirectlyGenericHolder<T>
    {
        internal static readonly object Marker = new();
    }

    private static class GenericOuter<T>
    {
        internal static class NonGenericInner
        {
            internal static readonly object Marker = new();
        }
    }
}
