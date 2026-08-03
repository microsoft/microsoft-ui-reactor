using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Xunit;
using VirtualKey = global::Windows.System.VirtualKey;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #987 regression cover: the DataGrid's KeyDown pipeline used to capture only the raw
/// <see cref="VirtualKey"/>, so <c>Shift+Tab</c> was indistinguishable from <c>Tab</c> and moved
/// focus FORWARD in navigation, cell-edit and row-edit mode alike.
///
/// <para>Two things are pinned here. First, that <c>KeyChord.Capture</c> asks about the aggregate
/// modifier keys and lands each answer in the matching slot — the mapping is the whole reason the
/// chord survives the handler's <c>DispatcherQueue.TryEnqueue</c> deferral.
/// Second, that each of the three Tab sites actually branches on <see cref="KeyChord.Shift"/>.</para>
///
/// <para><b>Why every direction test below is a differential.</b> With three columns and a ring
/// that wraps, forward and backward traversal land on the SAME column from most start positions —
/// a test that starts in the wrong place passes against a wrong-direction implementation. Each
/// direction assertion therefore starts where the two directions provably diverge (the read-only
/// column, a row boundary, or no focus at all) and is paired with the forward chord run against an
/// identically-prepared state. The two arms catch different wrong implementations: a Shift-blind one
/// fails the backward arm's <c>Assert.Equal</c>, because it lands on the forward column; a
/// direction-insensitive one that always lands on the backward expectation fails the forward arm's.
/// The cross-arm <c>NotEqual</c> that follows compares the two measured values. In a healthy tree
/// it is entailed by the per-arm assertions and never fires. It earns its place in one state only,
/// and that state is reachable by the most reflexive repair there is: if the start position ever
/// stops discriminating, both directions land on the same column, the forward arm fails on its
/// constant, and updating that constant to the observed value makes both per-arm assertions pass.
/// Measured, not assumed — under that exact edit the <c>NotEqual</c> is the sole surviving failure
/// (<c>Expected: Not 2, Actual: 2</c>). It constrains the relationship the two constants must hold,
/// which is a property of the test; the per-arm assertions constrain the run.</para>
/// </summary>
public class DataGridKeyChordTests
{
    private record TestItem(int Id, string Name, double Score, string Note);

    private sealed class TestDataSource : IDataSource<TestItem>
    {
        private readonly List<TestItem> _items;
        public TestDataSource()
            => _items =
            [
                new TestItem(1, "Alice", 95, "a"),
                new TestItem(2, "Bob", 87, "b"),
                new TestItem(3, "Carol", 92, "c"),
            ];

        public Task<DataPage<TestItem>> GetPageAsync(DataRequest request, CancellationToken ct = default)
            => Task.FromResult(new DataPage<TestItem>(_items, TotalCount: _items.Count));

        public RowKey GetRowKey(TestItem item) => new(item.Id.ToString());
        public DataSourceCapabilities Capabilities => DataSourceCapabilities.None;
    }

    // Column 0 is READ-ONLY, and the editable ring is {Name, Score, Note} — size THREE, which is
    // what makes every row-edit direction test below able to fail from ANY origin.
    //
    // It was size two (Name, Score) when #987 shipped, and at that width backward and forward are
    // the same cell from every position inside the ring: from Name both land on Score, from Score
    // both land on Name. Only two origins discriminated — the -1 no-prior-focus sentinel and the
    // read-only IdCol — and #976 then made BOTH unreachable, because BeginRowEdit now parks the
    // cursor on the first editable column it gives real XAML focus to. A read-only column is never
    // in the row-edit pending set, so no preserve-the-cursor rule can rescue that origin either.
    //
    // Widening the ring is the repair the #987 comment prescribed for exactly this case, and it is
    // strictly stronger than the origins it replaces: with an ODD ring every origin discriminates,
    // so a direction test can no longer be silently defused by moving where the edit begins.
    //
    //     origin 1 (NameCol, where BeginRowEdit parks)   back Note  / fwd Score
    //     origin 2 (ScoreCol)                            back Name  / fwd Note
    //     origin 3 (NoteCol)                             back Score / fwd Name
    //
    // An even ring cannot be rescued by any choice of constants; an odd one needs no rescuing.
    private const int IdCol = 0;
    private const int NameCol = 1;
    private const int ScoreCol = 2;
    private const int NoteCol = 3;

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
            Name = "Note",
            FieldType = typeof(string),
            GetValue = obj => ((TestItem)obj).Note,
            SetValue = (obj, val) => ((TestItem)obj) with { Note = (string)(val ?? "") },
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

    private static void Tab(DataGridState<TestItem> state, DataGridElement<TestItem> el, bool shift)
        => DataGridComponent<TestItem>.HandleKeyDownForTests(
            state, el, new KeyChord(VirtualKey.Tab, Shift: shift, Ctrl: false));

    private static (int Row, int Col) Focus(DataGridState<TestItem> state)
        => (state.FocusedRowIndex, state.FocusedColIndex);

    // ── KeyChord.Capture mapping ─────────────────────────────────────

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Capture_RoutesEachAggregateModifierProbeToItsOwnSlot(bool shiftDown, bool ctrlDown)
    {
        var probed = new List<VirtualKey>();
        var chord = KeyChord.Capture(VirtualKey.Tab, k =>
        {
            probed.Add(k);
            if (k == VirtualKey.Shift) return shiftDown;
            if (k == VirtualKey.Control) return ctrlDown;
            return false;
        });

        Assert.Equal(VirtualKey.Tab, chord.Key);

        // The asymmetric rows are what catch a swapped mapping: with (Shift down, Ctrl up) a
        // Shift/Ctrl transposition reports Shift=false, and with a hard-coded flag one of the four
        // rows always disagrees.
        Assert.Equal(shiftDown, chord.Shift);
        Assert.Equal(ctrlDown, chord.Ctrl);

        // ...and these catch a probe of the side-specific keys. VirtualKey.LeftShift alone would
        // satisfy every assertion above (the lambda would answer false and the "modifier up" rows
        // would still pass) while silently ignoring a right-hand Shift+Tab on a real keyboard.
        Assert.Contains(VirtualKey.Shift, probed);
        Assert.Contains(VirtualKey.Control, probed);
    }

    [Fact]
    public void Capture_NullProbe_Throws()
        => Assert.Throws<ArgumentNullException>(() => KeyChord.Capture(VirtualKey.Tab, null!));

    // ── Navigation mode ──────────────────────────────────────────────

    [Fact]
    public async Task NavigationShiftTab_WalksBackwardWhilePlainTabWalksForward()
    {
        var el = Grid(EditMode.Cell);

        // Row 1, column 0 is a row BOUNDARY: backward wraps up into row 0's last column while
        // forward stays in row 1. From any interior cell both directions stay on the same row and
        // a wrong-direction bug is only one column away — this start makes them diverge by a row.
        var back = await LoadedState();
        back.SetFocus(1, IdCol);
        Tab(back, el, shift: true);

        var forward = await LoadedState();
        forward.SetFocus(1, IdCol);
        Tab(forward, el, shift: false);

        // NoteCol is the last column of the fixture, so it is where the backward wrap lands.
        Assert.Equal((0, NoteCol), Focus(back));
        Assert.Equal((1, NameCol), Focus(forward));
        Assert.NotEqual(Focus(forward), Focus(back));
    }

    [Fact]
    public async Task NavigationShiftTab_StopsAtTheVeryFirstCellWithoutWrapping()
    {
        var el = Grid(EditMode.Cell);
        var state = await LoadedState();
        state.SetFocus(0, NameCol);

        // Walk INTO the boundary first. This leg is what keeps the boundary assertion below from
        // being vacuous: an arm that silently did nothing for Shift+Tab would leave focus on Name
        // and fail here, whereas starting at (0,0) and asserting "still at (0,0)" would pass.
        Tab(state, el, shift: true);
        Assert.Equal((0, IdCol), Focus(state));

        // Now at the very first cell, FocusPrevCell() reports false and focus stays put — it must
        // not wrap around to the last cell. Forward from here would move on to Name, so this
        // still fails if the arm ignores Shift.
        Tab(state, el, shift: true);
        Assert.Equal((0, IdCol), Focus(state));
    }

    // ── Cell-edit mode ───────────────────────────────────────────────

    [Fact]
    public async Task CellEditShiftTab_CommitsTheSameEditAndReopensOnThePreviousCell()
    {
        var el = Grid(EditMode.Cell);

        // The LAST column is the divergent start: backward stays in row 0 on the previous editable
        // column, forward wraps to row 1's read-only Id — different row AND different editability.
        // That is Note now that the fixture's editable ring was widened to three; starting on Score
        // would leave both directions inside row 0 and cost this test its row-divergence, which is
        // the property that lets it fail. The start moved with the fixture, not with an expectation.
        var back = await LoadedState();
        back.SetFocus(0, NoteCol);
        Assert.True(back.BeginEdit(0, NoteCol));
        back.UpdateEditingValue("edited");
        Tab(back, el, shift: true);

        var forward = await LoadedState();
        forward.SetFocus(0, NoteCol);
        Assert.True(forward.BeginEdit(0, NoteCol));
        forward.UpdateEditingValue("edited");
        Tab(forward, el, shift: false);

        // Shift+Tab must commit exactly what Tab commits — the direction changes only where the
        // cursor lands. Asserted on the persisted item, which CommitEdit writes synchronously.
        Assert.Equal("edited", back.GetItemAt(0)!.Note);
        Assert.Equal("edited", forward.GetItemAt(0)!.Note);

        Assert.Equal((0, ScoreCol), Focus(back));
        Assert.Equal((1, IdCol), Focus(forward));
        Assert.NotEqual(Focus(forward), Focus(back));

        // Backward landed on an editable column, so the editor reopens there; forward landed on
        // read-only Id, where BeginEdit() no-ops. Pins the reopen, not just the cursor move.
        Assert.True(back.IsEditing);
        Assert.Equal("Score", back.EditingColumnName);
        Assert.False(forward.IsEditing);
    }

    // ── Row-edit mode ────────────────────────────────────────────────

    [Fact]
    public async Task RowEditShiftTab_FromWhereTheRowEditParks_WalksBackwardWithoutCommitting()
    {
        var el = Grid(EditMode.Row);

        // RENAMED, and the premise below rewritten, per the instruction the #987 version of this
        // comment left for exactly this change: it used to start on read-only Id, because with a
        // two-editor ring that was one of only two origins where the directions differed. #976 made
        // that origin unreachable — BeginRowEdit now parks the cursor on the first editable column
        // it gives real XAML focus to, and a read-only column is never in the row-edit pending set,
        // so no preserve-the-cursor rule could have kept it. A test still NAMED for the read-only
        // origin would be found by someone grepping for that case, seen green, and believed.
        //
        // The repair is the structural one that comment prescribed and called correct: the editable
        // ring is now {Name, Score, Note}, size THREE. Moving the destination constants is therefore
        // legitimate here — the discrimination is RESTORED rather than absorbed. With an odd ring
        // every origin discriminates, so this test no longer depends on starting anywhere special,
        // which is strictly stronger than what it replaced.
        //
        // The origin is still asserted rather than assumed, and still fails HERE naming the origin
        // rather than downstream on a destination constant: a failure on the constant invites the
        // reader to update it, which is the edit that yields a green, direction-blind test.
        var back = await LoadedState();
        Assert.True(back.BeginRowEdit(0));
        Assert.Equal(NameCol, back.FocusedColIndex);
        back.UpdateRowEditValue("Name", "Edited");
        Tab(back, el, shift: true);

        var forward = await LoadedState();
        Assert.True(forward.BeginRowEdit(0));
        Assert.Equal(NameCol, forward.FocusedColIndex);
        forward.UpdateRowEditValue("Name", "Edited");
        Tab(forward, el, shift: false);

        // Backward off the FIRST editable column wraps to the LAST; forward steps to the next.
        // These are different columns only because the ring is odd — that is the whole point.
        Assert.Equal(NoteCol, back.FocusedColIndex);
        Assert.Equal(ScoreCol, forward.FocusedColIndex);
        Assert.NotEqual(forward.FocusedColIndex, back.FocusedColIndex);
        Assert.Equal(0, back.FocusedRowIndex);

        // Row-mode Tab navigates and never commits, in either direction (spec 017 §6.7): the row
        // stays open, the pending value survives, and nothing reached the item.
        Assert.True(back.IsRowEditing);
        Assert.Equal("Edited", back.GetRowEditValue("Name"));
        Assert.Equal("Alice", back.GetItemAt(0)!.Name);
    }

    [Fact]
    public async Task RowEditShiftTab_FromTheNoFocusSentinel_WrapsToTheLastEditableColumn()
    {
        var el = Grid(EditMode.Row);

        // RENAMED and re-routed, per the instruction the #987 version of this comment left. It used
        // to reach the -1 sentinel by simply beginning a row edit, which #976 made impossible:
        // BeginRowEdit now parks the cursor on the first VISIBLE editable column, so "no prior cell
        // focus" no longer describes anything this test could set up. Repairing the constant alone
        // would have left a test named for a state it never enters.
        //
        // The sentinel is still reachable, by the route DataGridState.MoveRowEditFocus documents as
        // deliberate: begin the row edit with every editable column HIDDEN — the pending values
        // exist, so the edit begins, but there is nothing visible to park on and the cursor keeps
        // its initial -1 — then reveal the columns, which HideColumn/ShowColumn permit mid-edit
        // because they do not guard on _isRowEditing. That makes the traversal meaningful again with
        // the cursor genuinely at -1, which is the state under test here.
        //
        // What the -1 case pins that no other origin does: backward must treat "no focus" as one
        // position PAST the end and land on the LAST editable column. A naive (-1 - 1) walk starts
        // at Name and never reaches Note.
        var back = await LoadedState();
        HideEditableColumns(back);
        Assert.True(back.BeginRowEdit(0));
        Assert.Equal(-1, back.FocusedColIndex);
        ShowEditableColumns(back);
        Assert.Equal(-1, back.FocusedColIndex); // revealing columns must not itself move the cursor
        Tab(back, el, shift: true);

        var forward = await LoadedState();
        HideEditableColumns(forward);
        Assert.True(forward.BeginRowEdit(0));
        Assert.Equal(-1, forward.FocusedColIndex);
        ShowEditableColumns(forward);
        Assert.Equal(-1, forward.FocusedColIndex);
        Tab(forward, el, shift: false);

        Assert.Equal(NoteCol, back.FocusedColIndex);
        Assert.Equal(NameCol, forward.FocusedColIndex);
        Assert.NotEqual(forward.FocusedColIndex, back.FocusedColIndex);
        Assert.True(back.IsRowEditing);
    }

    private static void HideEditableColumns(DataGridState<TestItem> state)
    {
        state.HideColumn("Name");
        state.HideColumn("Score");
        state.HideColumn("Note");
    }

    private static void ShowEditableColumns(DataGridState<TestItem> state)
    {
        state.ShowColumn("Name");
        state.ShowColumn("Score");
        state.ShowColumn("Note");
    }

    // ── The claim gate ───────────────────────────────────────────────

    [Fact]
    public async Task ShouldHandleKey_ClaimsShiftTabInEveryMode()
    {
        // The claim must stay modifier-blind. Gating it on !chord.Shift would hand Shift+Tab back
        // to FocusManager, and the grid would never run the backward arms above at all — a failure
        // no direction test can see, because HandleKeyDown would simply not be called.
        var navigation = await LoadedState();
        var cellEdit = await LoadedState();
        var rowEdit = await LoadedState();

        Assert.True(cellEdit.BeginEdit(0, NameCol));
        Assert.True(rowEdit.BeginRowEdit(0));

        foreach (var (name, state, el) in new (string, DataGridState<TestItem>, DataGridElement<TestItem>)[]
        {
            ("navigation", navigation, Grid(EditMode.Cell)),
            ("cell edit", cellEdit, Grid(EditMode.Cell)),
            ("row edit", rowEdit, Grid(EditMode.Row)),
        })
        {
            Assert.True(
                DataGridComponent<TestItem>.ShouldHandleKeyForTests(
                    state, el, new KeyChord(VirtualKey.Tab, Shift: true, Ctrl: false)),
                $"Shift+Tab must be claimed in {name} mode");
            Assert.True(
                DataGridComponent<TestItem>.ShouldHandleKeyForTests(
                    state, el, new KeyChord(VirtualKey.Tab, Shift: false, Ctrl: false)),
                $"Tab must be claimed in {name} mode");
        }
    }

    [Fact]
    public async Task ShouldHandleKey_IsModifierBlind_SoTheCaptureGateCannotDropChords()
    {
        // DataGridComponent's KeyDown handler settles the claim from KeyChord.Unmodified(e.Key) and
        // probes the real keyboard only once the key is claimed, so the grid never touches modifier
        // state for keys it does not own. That gate is sound ONLY while the claim ignores modifiers.
        //
        // If a future arm makes ShouldHandleKey modifier-dependent — Ctrl+Home / Ctrl+End, spec 017
        // §6.8 — this test fails, and the fix is to move the KeyChord.Capture() call back ABOVE the
        // ShouldHandleKey() gate in OnMount. Without this test that change would compile, pass every
        // direction test, and silently reintroduce #987 for the new chord: the claim would be
        // decided against modifiers that were never read, so the grid would decline the chord and
        // hand it back to FocusManager.
        var navigation = await LoadedState();
        var cellEdit = await LoadedState();
        var rowEdit = await LoadedState();

        Assert.True(cellEdit.BeginEdit(0, NameCol));
        Assert.True(rowEdit.BeginRowEdit(0));

        VirtualKey[] keys =
        [
            VirtualKey.Tab, VirtualKey.Enter, VirtualKey.Escape, VirtualKey.Home, VirtualKey.End,
            VirtualKey.Up, VirtualKey.Down, VirtualKey.Left, VirtualKey.Right, VirtualKey.Space,
            VirtualKey.F2, VirtualKey.A,
        ];

        var claimedAny = false;

        foreach (var (name, state, el) in new (string, DataGridState<TestItem>, DataGridElement<TestItem>)[]
        {
            ("navigation", navigation, Grid(EditMode.Cell)),
            ("cell edit", cellEdit, Grid(EditMode.Cell)),
            ("row edit", rowEdit, Grid(EditMode.Row)),
        })
        {
            foreach (var key in keys)
            {
                var bare = DataGridComponent<TestItem>.ShouldHandleKeyForTests(
                    state, el, KeyChord.Unmodified(key));
                claimedAny |= bare;

                foreach (var (shift, ctrl) in new[] { (true, false), (false, true), (true, true) })
                {
                    Assert.Equal(
                        bare,
                        DataGridComponent<TestItem>.ShouldHandleKeyForTests(
                            state, el, new KeyChord(key, Shift: shift, Ctrl: ctrl)));
                }
            }
        }

        // Guards the guard: if ShouldHandleKey ever returned false for everything, every comparison
        // above would trivially agree and this test would pass while proving nothing.
        Assert.True(claimedAny, "ShouldHandleKey claimed no key at all — the equality checks above are vacuous.");
    }
}
