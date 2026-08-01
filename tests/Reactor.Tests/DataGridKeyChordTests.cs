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
/// The cross-arm <c>NotEqual</c> that follows compares the two measured values, so it survives an
/// edit that collapses the two expected constants onto one another — but it is entailed by the
/// per-arm assertions and is never what fails first.</para>
/// </summary>
public class DataGridKeyChordTests
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

    // Column 0 is READ-ONLY, which is what makes the row-edit direction tests below meaningful:
    // it is the one start position from which forward and backward land on different columns.
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

        Assert.Equal((0, ScoreCol), Focus(back));
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

        // Score (last column) is the divergent start: backward stays in row 0 on Name, forward
        // wraps to row 1's read-only Id — different row AND different editability.
        var back = await LoadedState();
        back.SetFocus(0, ScoreCol);
        Assert.True(back.BeginEdit(0, ScoreCol));
        back.UpdateEditingValue(41.0);
        Tab(back, el, shift: true);

        var forward = await LoadedState();
        forward.SetFocus(0, ScoreCol);
        Assert.True(forward.BeginEdit(0, ScoreCol));
        forward.UpdateEditingValue(41.0);
        Tab(forward, el, shift: false);

        // Shift+Tab must commit exactly what Tab commits — the direction changes only where the
        // cursor lands. Asserted on the persisted item, which CommitEdit writes synchronously.
        Assert.Equal(41.0, back.GetItemAt(0)!.Score);
        Assert.Equal(41.0, forward.GetItemAt(0)!.Score);

        Assert.Equal((0, NameCol), Focus(back));
        Assert.Equal((1, IdCol), Focus(forward));
        Assert.NotEqual(Focus(forward), Focus(back));

        // Backward landed on an editable column, so the editor reopens there; forward landed on
        // read-only Id, where BeginEdit() no-ops. Pins the reopen, not just the cursor move.
        Assert.True(back.IsEditing);
        Assert.Equal("Name", back.EditingColumnName);
        Assert.False(forward.IsEditing);
    }

    // ── Row-edit mode ────────────────────────────────────────────────

    [Fact]
    public async Task RowEditShiftTab_FromTheReadOnlyColumn_WalksBackwardWithoutCommitting()
    {
        var el = Grid(EditMode.Row);

        // Starting on read-only Id is the ONLY interior start where the two directions differ:
        // backward skips it to Score, forward to Name. From Name or Score the two-editor ring
        // wraps to the same place either way, and a Shift-blind implementation would pass.
        var back = await LoadedState();
        back.SetFocus(0, IdCol);
        Assert.True(back.BeginRowEdit(0));
        back.UpdateRowEditValue("Name", "Edited");
        Tab(back, el, shift: true);

        var forward = await LoadedState();
        forward.SetFocus(0, IdCol);
        Assert.True(forward.BeginRowEdit(0));
        forward.UpdateRowEditValue("Name", "Edited");
        Tab(forward, el, shift: false);

        Assert.Equal(ScoreCol, back.FocusedColIndex);
        Assert.Equal(NameCol, forward.FocusedColIndex);
        Assert.NotEqual(forward.FocusedColIndex, back.FocusedColIndex);
        Assert.Equal(0, back.FocusedRowIndex);

        // Row-mode Tab navigates and never commits, in either direction (spec 017 §6.7): the row
        // stays open, the pending value survives, and nothing reached the item.
        Assert.True(back.IsRowEditing);
        Assert.Equal("Edited", back.GetRowEditValue("Name"));
        Assert.Equal("Alice", back.GetItemAt(0)!.Name);
    }

    [Fact]
    public async Task RowEditShiftTab_WithNoPriorCellFocus_WrapsToTheLastEditableColumn()
    {
        var el = Grid(EditMode.Row);

        // A row edit started from the Edit button leaves FocusedColIndex at -1. Backward has to
        // treat that as one PAST the end and land on the last editable column; a naive
        // (-1 - 1) walk would start at Name and never reach Score.
        var back = await LoadedState();
        Assert.True(back.BeginRowEdit(0));
        Assert.Equal(-1, back.FocusedColIndex);
        Tab(back, el, shift: true);

        var forward = await LoadedState();
        Assert.True(forward.BeginRowEdit(0));
        Assert.Equal(-1, forward.FocusedColIndex);
        Tab(forward, el, shift: false);

        Assert.Equal(ScoreCol, back.FocusedColIndex);
        Assert.Equal(NameCol, forward.FocusedColIndex);
        Assert.NotEqual(forward.FocusedColIndex, back.FocusedColIndex);
        Assert.True(back.IsRowEditing);
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
