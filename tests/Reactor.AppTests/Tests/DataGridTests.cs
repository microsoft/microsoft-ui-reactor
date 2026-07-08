using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for DataGrid inline editing. Exercises click-to-edit, real keyboard input,
/// cross-row commit, and same-row cell switching through the full WinUI accessibility
/// pipeline. Cells are located + clicked via winapp ui; the inline editor TextBox has no
/// stable AutomationId, so text is typed into the focused control with the Win32
/// <see cref="InputInjector"/> fallback (winapp ui has no keyboard typing).
/// </summary>
[TestClass]
public class DataGridTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// Click a cell to enter edit mode, type a new value, click a different
    /// row to commit, then click the second column to edit it, type, and
    /// press Enter to commit. Verifies the full editing pipeline through
    /// real mouse and keyboard input.
    /// </summary>
    // [Retry] mops up the rare unattended-desktop input-injection flake: Win32 SendInput is
    // occasionally dropped before the Host window foregrounds on CI. A real regression still
    // fails every attempt. Removable once winappCli #562 (send-keys)/#498 (drag) ship native verbs.
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_ClickEditTabCommit()
    {
        NavigateToFixtureFresh("DataGrid_EditableGrid");

        // 1. Wait for grid data
        WaitForText("EditLog", "Edits:");
        Assert.IsNotNull(WaitForName("Alice"), "'Alice' should be visible");
        Assert.IsNotNull(FindByName("Smith"), "'Smith' should be visible");

        // 2. Click "Alice" to start editing FirstName in row 1
        TapCell("Alice");

        // 3. Clear and type new value into the now-focused inline editor
        TypeIntoFocusedEditor("Alicia");

        // 4. Click "Bob" (different row) to commit the FirstName edit
        Assert.IsNotNull(FindByName("Bob"), "'Bob' should be visible while editing");
        TapCell("Bob");

        // 5. Verify the first edit committed. The fixture logs every onRowChanged callback into an
        // append-only EditLog, so we can deterministically assert the row-1 commit callback fired
        // with the right key + values — even when the cross-row commit-tap also fires a spurious
        // unchanged row-2 commit (an overwrite-style status would race and settle on '2:Bob,Jones').
        // Also confirm the persisted grid value, so both the callback contract and the data commit
        // are covered. See the E2E flake investigation.
        WaitForTextContaining("EditLog", "[1:Alicia,Smith]", timeoutMs: 5000);
        Assert.IsNotNull(WaitForName("Alicia"), "'Alicia' should be visible after the cross-row commit-tap");

        // 6. Click "Smith" to edit LastName in row 1
        Assert.IsNotNull(WaitForName("Smith"), "'Smith' should be visible");
        TapCell("Smith");

        // 7. Clear and type, 8. press Enter to commit
        TypeIntoFocusedEditor("Johnson", commitWithEnter: true);

        // 9. Verify second edit committed
        WaitForTextContaining("EditLog", "[1:Alicia,Johnson]", timeoutMs: 5000);
        Assert.IsNotNull(WaitForName("Alicia"), "'Alicia' should still be visible");
        Assert.IsNotNull(WaitForName("Johnson"), "'Johnson' should be visible after commit");
    }

    private const string AnchorId = "KbdNav_FocusAnchor";
    private const string StatusId = "KbdNav_Status";
    private const string EditLogId = "KbdNav_EditLog";

    /// <summary>
    /// Drives the DataGrid's REAL keyboard-navigation pipeline end to end: focuses the grid
    /// (a tab stop) via UIA, then injects the full non-editing + editing key vocabulary with the
    /// Win32 <see cref="InputInjector"/> fallback (winapp ui has no arrow/keystroke input). This is
    /// coverage the in-process <c>DataGrid_KeyReflect_*</c> selftest cannot reach: that selftest
    /// reflectively invokes the private <c>HandleKeyDown</c> directly, so it never runs the real
    /// <c>OnMount</c> AddHandler lambda + <c>ShouldHandleKey</c> gate, and never exercises the Up /
    /// Left arrow arms or <c>ShouldHandleKey(Space)</c> — those are only reachable through injected
    /// keys landing on a focused grid.
    ///
    /// Every navigation assertion is deterministic: the grid's three columns hold globally-unique
    /// cell values, so the inline editor that <c>BeginEdit</c> opens (Enter / F2) reveals exactly
    /// which cell the focus moved to — proving the arrow / Home / End / Tab handler bodies actually
    /// moved cell focus. Row selection (Space) and edit commits (editing Enter / Tab) are asserted
    /// through the fixture's status + edit-log TextBlocks.
    /// </summary>
    // [E2eRetry] mops up the rare unattended-desktop input-injection flake (Win32 SendInput is
    // occasionally dropped before the Host window foregrounds, or UIA SetFocus loses the race with
    // the grid's focus/edit re-render). A real regression still fails every attempt. Removable once
    // winappCli #562 (send-keys) ships native keyboard verbs.
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_KeyboardNavigation()
    {
        NavigateToFixtureFresh("DataGrid_KeyboardNav");
        WaitForText(StatusId, "Sel:none");
        Assert.IsNotNull(WaitForName("Alice"), "'Alice' should be visible");

        // 0. Acquire keyboard focus on the grid and prove the cross-process key pipeline is live
        //    (a focused grid + Enter opens the inline editor). Confirmed before any assertion so a
        //    non-interactive/mis-focused run fails here with a clear message instead of mid-way.
        if (!EnsureGridFocusedAndKeyboardLive())
        {
            Assert.Fail(
                "Could not focus the DataGrid and drive its keyboard pipeline (an Enter on the " +
                "focused grid never opened the inline editor). The grid is a tab stop; injected " +
                "arrow/Enter keys should route to its KeyDown handler.");
        }
        // After liveness the internal focus rests on cell (0,0) and no edit is active.

        // 1. Right + Down move cell focus; Enter begins editing the landed cell. The editor value
        //    (unique per cell) proves focus reached row 1 / col 1 (Last = "Jones"). Covers the
        //    Right + Down arms, the real AddHandler lambda, and BeginEdit via Enter.
        PressNavKey(InputInjector.VkRight); // (0,0) -> (0,1)
        PressNavKey(InputInjector.VkDown);  // (0,1) -> (1,1)
        PressNavKey(InputInjector.VkEnter); // BeginEdit(1,1)
        AssertEditorValue("Jones", "Right+Down should focus row 1 / col 1 (Last='Jones')");

        // Escape cancels the edit (editing-branch ShouldHandleKey + CancelEdit). Focus stays (1,1).
        PressEditingKey(InputInjector.VkEscape);
        AssertNoEditor("Escape should cancel the inline edit");

        // 2. Left + Up — the two arrow arms NO existing test drives — then F2 begins editing.
        //    Landing on (0,0) => First = "Alice" proves both moved focus.
        PressNavKey(InputInjector.VkLeft); // (1,1) -> (1,0)
        PressNavKey(InputInjector.VkUp);   // (1,0) -> (0,0)
        PressNavKey(InputInjector.VkF2);   // BeginEdit(0,0)
        AssertEditorValue("Alice", "Left+Up should focus row 0 / col 0 (First='Alice')");
        PressEditingKey(InputInjector.VkEscape);
        AssertNoEditor("Escape should cancel after F2 edit");

        // 3. End jumps to the last column (City='Reno'); Home returns to the first (First='Alice').
        PressNavKey(InputInjector.VkEnd);   // (0,0) -> (0,2)
        PressNavKey(InputInjector.VkEnter); // BeginEdit(0,2)
        AssertEditorValue("Reno", "End should focus the last column (City='Reno')");
        PressEditingKey(InputInjector.VkEscape);
        AssertNoEditor("Escape should cancel after End edit");

        PressNavKey(InputInjector.VkHome);  // (0,2) -> (0,0)
        PressNavKey(InputInjector.VkEnter); // BeginEdit(0,0)
        AssertEditorValue("Alice", "Home should focus the first column (First='Alice')");
        PressEditingKey(InputInjector.VkEscape);
        AssertNoEditor("Escape should cancel after Home edit");

        // 4. Tab (not editing) advances to the next cell (FocusNextCell): (0,0) -> (0,1) Last='Smith'.
        PressNavKey(InputInjector.VkTab);   // FocusNextCell -> (0,1)
        PressNavKey(InputInjector.VkEnter); // BeginEdit(0,1)
        AssertEditorValue("Smith", "Tab should advance focus to the next cell (Last='Smith')");

        // 5. Tab WHILE EDITING commits the current cell (onRowChanged fires) and reopens the editor
        //    on the next cell — the editing-branch Tab arm (CommitAndMoveNext + BeginEdit). The
        //    grid registers its handler with handledEventsToo so Tab reaches it even after WinUI's
        //    FocusManager consumes it. Editor now at (0,2) City='Reno'; the commit logs row 1.
        PressEditingKey(InputInjector.VkTab);
        WaitForTextContaining(EditLogId, "[1:", timeoutMs: 5000);
        AssertEditorValue("Reno", "Editing Tab should commit and advance the editor to City='Reno'");

        // 6. Enter WHILE EDITING commits and closes the editor (editing-branch Enter arm + CommitEdit).
        PressEditingKey(InputInjector.VkEnter);
        AssertNoEditor("Editing Enter should commit and close the editor");

        // 7. Space selects the focused row (Space arm: ShouldHandleKey(Space) + HandleRowClick).
        //    The fixture surfaces the selection through the status TextBlock -> "Sel:1".
        PressNavKey(InputInjector.VkSpace);
        WaitForTextContaining(StatusId, "Sel:1", timeoutMs: 5000);

        Assert.IsTrue(App.Exists(AnchorId), "Keyboard-nav fixture should still be present (no crash).");
    }

    // ─── Keyboard-nav helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Move keyboard focus onto the grid and confirm the injected-key pipeline reaches its KeyDown
    /// handler. The grid's own Grid container is a tab stop but is not UIA-addressable, so we focus
    /// the adjacent anchor Button and Tab onto the grid (see <see cref="FocusGrid"/>). Confirmation
    /// is by editor VALUE: from the fixture's fresh state the first arrow lands on cell (0,0), so
    /// Enter must open an editor reading "Alice". This is self-consistent across retries — a failed
    /// attempt whose keys never reached the grid leaves the internal focus untouched (still fresh),
    /// so the next attempt's first arrow again lands on (0,0). Leaves a clean, non-editing (0,0).
    /// </summary>
    private bool EnsureGridFocusedAndKeyboardLive()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            FocusGrid();
            InputInjector.PressKey(InputInjector.VkDown); // from fresh state -> cell (0,0)
            Thread.Sleep(50);
            InputInjector.PressKey(InputInjector.VkEnter); // BeginEdit(0,0) if the grid is focused

            if (WaitForEditorValue("Alice", 1500)?.Trim() == "Alice")
            {
                // Grid is focusable and the pipeline is live; return to a known non-editing (0,0).
                PressEditingKey(InputInjector.VkEscape);
                WaitForNoEditor(2000);
                return true;
            }

            // Clear any stray editor before retrying so the next attempt starts clean.
            if (App.FindFirstEditableSelector() is not null)
            {
                PressEditingKey(InputInjector.VkEscape);
                WaitForNoEditor(1000);
            }
            Thread.Sleep(120);
        }
        return false;
    }

    /// <summary>
    /// Put keyboard focus on the grid: UIA-focus the adjacent anchor Button (the grid's Grid
    /// container isn't UIA-addressable), then inject a single Tab to move focus onto the grid — the
    /// next tab stop. Focus is on the anchor (not the grid) when this Tab fires, so it does NOT
    /// trigger the grid's own Tab handler; the grid's internal cell focus is left untouched.
    /// </summary>
    private void FocusGrid()
    {
        InputInjector.Foreground(HostHwnd);
        try { App.Focus(AnchorId); }
        catch (WinAppException) { /* anchor should always be focusable; foreground focus still applies */ }
        InputInjector.Foreground(HostHwnd);
        InputInjector.Tab(); // anchor -> grid (next tab stop)
        Thread.Sleep(40);
    }

    /// <summary>Re-focus the grid, then inject one navigation key (used when NOT editing).</summary>
    private void PressNavKey(ushort virtualKey)
    {
        FocusGrid();
        InputInjector.PressKey(virtualKey);
        Thread.Sleep(60);
    }

    /// <summary>
    /// Focus the open inline editor, then inject an editing key (Enter / Escape / Tab). Keeping
    /// focus inside the grid subtree guarantees the key bubbles to the grid's KeyDown handler even
    /// though the editor, not the grid, holds WinUI focus while editing.
    /// </summary>
    private void PressEditingKey(ushort virtualKey)
    {
        var editor = App.FindFirstEditableSelector();
        InputInjector.Foreground(HostHwnd);
        if (editor is not null)
        {
            try { App.Focus(editor); }
            catch (WinAppException) { /* slug may have staled on a re-render; foreground focus still targets it */ }
            InputInjector.Foreground(HostHwnd);
        }
        InputInjector.PressKey(virtualKey);
        Thread.Sleep(80);
    }

    private void AssertEditorValue(string expected, string because)
    {
        var last = WaitForEditorValue(expected, timeoutMs: 4000);
        Assert.AreEqual(expected, last?.Trim(),
            $"{because}. Inline editor value should be '{expected}' but last-seen was '{last ?? "<no editor>"}'.");
    }

    private void AssertNoEditor(string because)
        => Assert.IsTrue(WaitForNoEditor(4000), $"{because}. An inline editor was still present.");

    /// <summary>Poll the inline editor's value until it equals <paramref name="expected"/>; returns the last read.</summary>
    private string? WaitForEditorValue(string expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? last = null;
        do
        {
            var sel = App.FindFirstEditableSelector();
            last = sel is null ? null : App.GetValue(sel);
            if (last?.Trim() == expected)
                return last;
            Thread.Sleep(120);
        }
        while (DateTime.UtcNow < deadline);
        return last;
    }

    private bool WaitForNoEditor(int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            if (App.FindFirstEditableSelector() is null)
                return true;
            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);
        return false;
    }

    /// <summary>
    /// Tap a DataGrid cell by its visible text to enter/commit cell edit. The cells are
    /// display-only TextBlocks (no InvokePattern), and a WinUI <c>Tapped</c> only fires on an
    /// ACTIVE window, so winapp's UIA invoke/click can't drive them. We foreground the host and
    /// inject a real pointer click at the cell centre — the same proven path the gesture tests use.
    /// </summary>
    private void TapCell(string name)
    {
        var r = FindByName(name).Rect;
        InputInjector.Foreground(HostHwnd);
        InputInjector.Click(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    /// <summary>
    /// Replace the contents of the inline editor that appears after a cell tap. The editor is a
    /// TextBox with no AutomationId, so we locate it by UIA control type, then clear + type through
    /// <see cref="UiElement"/> (which foregrounds the host, focuses the editor via UIA SetFocus,
    /// and injects real keystrokes — winapp ui has no keyboard typing).
    /// </summary>
    private void TypeIntoFocusedEditor(string value, bool commitWithEnter = false)
    {
        var editor = WaitForEditor();

        // The inline editor's UIA SetFocus does not reliably select-all, so a Clear that races the
        // editor's focus/realization can leave the old text in place — the new value then
        // interleaves with it (observed 'aAlicialice') or is dropped. Clear+type, then confirm the
        // editor holds exactly the new value before the caller commits. Retry only when we can
        // positively read a wrong value; never blind-retry (that would double-type when the
        // editor value can't be read).
        string? lastSeen = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ClearEditor(editor);

            if (commitWithEnter)
            {
                editor.SendKeys(value + Keys.Enter); // Enter commits + closes the editor; can't re-read it
                return;
            }

            editor.SendKeys(value);
            lastSeen = ReadEditorValueSettled(editor, value, timeoutMs: 1500);
            if (lastSeen is null || lastSeen == value)
                return; // confirmed correct, or unreadable (don't risk double-typing)
        }

        // Every attempt positively read a wrong value — fail loudly here (with the last value seen)
        // rather than letting the caller's downstream assertion time out with a confusing message.
        throw new WinAppException(
            $"Inline editor did not accept '{value}' after 3 clear+type attempts; last-seen value was '{lastSeen}'.");
    }

    /// <summary>Clear the inline editor, confirming it reads empty when the value is readable.</summary>
    private static void ClearEditor(UiElement editor, int attempts = 3)
    {
        for (int i = 0; i < attempts; i++)
        {
            editor.Clear();
            var v = ReadEditorValueSettled(editor, string.Empty, timeoutMs: 500);
            if (v is null || v.Length == 0)
                return; // empty, or unreadable — stop (avoid over-clearing)
        }
    }

    /// <summary>
    /// Poll the editor's value until it equals <paramref name="expected"/> or the timeout elapses,
    /// returning the last value read (null when winapp cannot read the editor's value).
    /// </summary>
    private static string? ReadEditorValueSettled(UiElement editor, string expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? last;
        do
        {
            last = editor.Text;
            if (last == expected)
                return last;
            Thread.Sleep(150);
        }
        while (DateTime.UtcNow < deadline);
        return last;
    }

    /// <summary>Wait for the DataGrid inline editor (a UIA <c>Edit</c> control) to mount.</summary>
    private UiElement WaitForEditor(int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var selector = App.FindFirstEditableSelector();
            if (selector is not null)
                return Element(selector);
            Thread.Sleep(100);
        }
        throw new WinAppException("DataGrid inline editor (Edit control) did not appear after the cell tap.");
    }

    private UiElement? WaitForName(string name, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var matches = App.Search(name);
            var exact = matches.FirstOrDefault(m => m.Name == name) ?? matches.FirstOrDefault();
            if (exact is not null)
            {
                var selector = !string.IsNullOrEmpty(exact.AutomationId) ? exact.AutomationId! : exact.Selector;
                return Element(selector, exact.AutomationId);
            }
            Thread.Sleep(100);
        }
        return null;
    }
}
