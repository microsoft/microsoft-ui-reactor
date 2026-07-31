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
            => Task.FromResult(new DataPage<TestItem>(_items, TotalCount: _items.Count));

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
    public async Task BeginRowEdit_FallsBackToTheFirstEditor_WhenTheCursorColumnHasNone()
    {
        var state = await LoadedState();

        // "Id" is read-only, so it renders no editor. Focus has to fall back to the first column
        // that does — but the cursor was set deliberately by the caller, so it is left alone.
        state.SetFocus(1, 0);
        Assert.True(state.BeginRowEdit(1));

        Assert.Equal(0, state.FocusedColIndex);
        Assert.True(state.HasEditorFocusRequest(Row(1), "Name"));
        Assert.False(state.HasEditorFocusRequest(Row(1), "Id"));
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

        // BeginRowEdit leaves the column cursor at -1, so the FIRST Tab still moves it. Take that
        // step so the second Tab is genuinely the wrap-onto-itself case this test is about.
        Assert.True(state.FocusNextRowEditColumn());
        Assert.Equal(ScoreCol, state.FocusedColIndex);
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), "Score"));

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
        DataGridComponent<TestItem>.HandleKeyDownForTests(state, el, VirtualKey.Tab);

        // The whole point of #976: the key path — not just the public API — has to arm.
        Assert.True(state.FocusRequestVersion > versionBefore);
        Assert.True(state.TryConsumeEditorFocusRequest(Row(1), Columns[state.FocusedColIndex].Name));

        // …and it must not have committed or torn down the row on the way.
        Assert.True(state.IsRowEditing);
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
}
