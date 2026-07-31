using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for DataGrid inline editing. Exercises click-to-edit, real keyboard input,
/// cross-row commit, and same-row cell switching through the full WinUI accessibility
/// pipeline. Cells are located + clicked via winapp ui; the inline editor TextBox has no
/// stable AutomationId, so text is typed into the focused control through the native
/// <c>winapp ui send-keys</c> verb.
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
    // [E2eRetry] mops up the rare unattended-desktop input-injection flake: the native winapp
    // send-keys/drag verbs are SendInput under the hood and are occasionally dropped before the Host
    // foregrounds on CI. A real regression still fails every attempt; retained pending a few stable CI runs (#652).
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

    /// <summary>
    /// Regression: pressing Tab WHILE EDITING must commit the current cell AND leave the inline
    /// editor reopened on the next cell — it must not be torn down. The grid's deferred LostFocus
    /// commit fires because Tab moves real focus out of the single-tab-stop grid; previously it ran
    /// after the editing-Tab flow had already committed + reopened the editor and committed a second
    /// time, destroying it (the editor never reappeared). Guarded by
    /// <c>DataGridState&lt;T&gt;.SuppressNextLostFocusCommit</c>.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_EditingTab_ReopensEditorOnNextCell()
    {
        NavigateToFixtureFresh("DataGrid_EditableGrid");
        WaitForText("EditLog", "Edits:");
        Assert.IsNotNull(WaitForName("Alice"), "'Alice' should be visible");
        Assert.IsNotNull(WaitForName("Smith"), "'Smith' (the next cell) should be visible");

        // Edit row-1 FirstName (Alice); type a new value but do NOT commit.
        TapCell("Alice");
        TypeIntoFocusedEditor("Alicia");

        // Editing-Tab: commits FirstName and should reopen the editor on the LastName cell.
        App.SendKeys("tab", viaSendInput: true);

        // The inline editor must still be present (reopened on the next cell), not torn down by the
        // LostFocus safety-net.
        UiElement? editor = null;
        try { editor = WaitForEditor(timeoutMs: 4000); }
        catch (WinAppException ex)
        {
            Assert.Fail("Inline editor did not REOPEN on the next cell after the editing-Tab — the reopen " +
                        "regressed (the LostFocus safety-net likely committed a second time and tore it down). " + ex.Message);
        }

        Assert.IsNotNull(editor, "Inline editor should have reopened on the next cell after the editing-Tab.");

        // The FirstName commit from the Tab must have landed (LastName unchanged, still "Smith").
        WaitForTextContaining("EditLog", "[1:Alicia,Smith]", timeoutMs: 5000);

        // Authoritative, unconditional proof the editor reopened on the LastName cell (not still on
        // FirstName): type a new value into the reopened editor and commit. Editing LastName commits
        // row 1 as [1:Alicia,Smythe] — FirstName preserved as the just-committed "Alicia"; had the
        // editor wrongly reopened on FirstName, that commit would read [1:Smythe,Smith] and this
        // assertion would time out. Unlike a direct editor-value read (winapp can't reliably read the
        // inline TextBox), this behavioral oracle is unconditional.
        TypeIntoFocusedEditor("Smythe", commitWithEnter: true);
        WaitForTextContaining("EditLog", "[1:Alicia,Smythe]", timeoutMs: 5000);
        Assert.IsNotNull(WaitForName("Smythe"), "Edited LastName 'Smythe' should be visible after commit.");
    }

    /// <summary>
    /// Regression for the SuppressNextLostFocusCommit guard's one-shot lifetime: an editing-Tab into a
    /// NON-editable next cell reopens no editor (IsEditing ends false), so the guard must still be
    /// consumed — otherwise it lingers on the persistent state and silently suppresses the NEXT
    /// legitimate focus-out commit, losing that edit. Here: edit row-1 LastName (its next tab-order
    /// cell, Salary, is read-only), press Tab, then edit a different row's cell and move focus off the
    /// grid (click the anchor button). That second edit must commit.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_EditingTabToReadOnly_DoesNotSuppressNextCommit()
    {
        NavigateToFixtureFresh("DataGrid_EditableGrid");
        WaitForText("EditLog", "Edits:");
        Assert.IsNotNull(WaitForName("Smith"), "'Smith' (row 1 LastName) should be visible");

        // Edit row-1 LastName (Smith -> Brown); Tab moves to Salary (read-only) so no editor reopens.
        TapCell("Smith");
        TypeIntoFocusedEditor("Brown");
        App.SendKeys("tab", viaSendInput: true);
        WaitForTextContaining("EditLog", "[1:Alice,Brown]", timeoutMs: 5000);

        // Now edit a different row's cell, then move focus OUT of the grid by clicking the anchor
        // button. That fires the grid's "focus left the grid" LostFocus commit — the exact path the
        // guard suppresses. If the guard leaked from the Tab-into-read-only above, 'Bobby' is lost.
        Assert.IsNotNull(WaitForName("Bob"), "'Bob' (row 2 FirstName) should be visible");
        TapCell("Bob");
        TypeIntoFocusedEditor("Bobby");
        Element("BlurAnchor").Click(); // focus leaves the grid -> blur-commit through LostFocus

        WaitForTextContaining("EditLog", "[2:Bobby,Jones]", timeoutMs: 5000);
    }

    /// <summary>
    /// Issue #976: in ROW edit mode, Tab must cycle real keyboard focus among the row's editors —
    /// wrapping from the last one back to the first — instead of walking out of the grid and
    /// tripping the LostFocus blur-commit.
    ///
    /// <para>Behavioral oracle, because winapp cannot tell us which of the row's several open
    /// editors has focus: type into the editor focus lands in and read back which COLUMN changed.
    /// Start a row edit on row 1 (focus lands in FirstName), Tab once (→ LastName) and type
    /// "Smythe", Tab again — the wrap — and type "Alicia", then Enter to commit the row. Only a
    /// correct wrap produces <c>[1:Alicia,Smythe]</c>: if Tab left the grid, the blur-commit lands
    /// <c>[1:Alice,Smythe]</c> first and "Alicia" goes nowhere; if focus never moved, "Alicia"
    /// overwrites LastName and the log reads <c>[1:Alice,Alicia]</c>.</para>
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_RowEditTab_WrapsToFirstEditorWithoutCommitting()
    {
        NavigateToFixtureFresh("DataGrid_RowEditGrid");
        WaitForText("RowEditLog", "Edits:");
        Assert.IsNotNull(WaitForName("Alice"), "'Alice' (row 1 FirstName) should be visible");

        BeginRowEditOnFirstRow();

        // Focus is in the FirstName editor. Tab → LastName, replace "Smith" with "Smythe".
        WaitForFocus("RowEdit_FirstName", "row edit start");
        App.SendKeys(Keys.Tab, viaSendInput: true);
        WaitForFocus("RowEdit_LastName", "Tab from FirstName");
        ReplaceFocusedEditorText("Smythe");

        // Tab again. LastName is the LAST editable column, so this is the wrap: focus must return
        // to the FirstName editor rather than continuing on to Save/Cancel and out of the grid.
        App.SendKeys(Keys.Tab, viaSendInput: true);
        WaitForFocus("RowEdit_FirstName", "Tab from LastName (the wrap)");
        ReplaceFocusedEditorText("Alicia");

        // Nothing may have committed yet — the wrap must not have tripped a blur-commit.
        var logSoFar = FindById("RowEditLog").Text ?? "";
        Assert.IsFalse(logSoFar.Contains('['),
            $"Row-mode Tab must not commit the row; RowEditLog was '{logSoFar}'.");

        App.SendKeys(Keys.Enter, viaSendInput: true);

        WaitForTextContaining("RowEditLog", "[1:Alicia,Smythe]", timeoutMs: 5000);
    }

    /// <summary>
    /// Issue #976, companion to the wrap test: the row-mode Tab arms
    /// <c>SuppressNextLostFocusCommit</c> so the wrap's focus-out doesn't blur-commit the row. That
    /// flag is one-shot, so it must be consumed by the Tab's own LostFocus — if it leaks it
    /// silently swallows the NEXT legitimate blur-commit and the edit is lost. Same bug class as
    /// <see cref="Interactive_DataGrid_EditingTabToReadOnly_DoesNotSuppressNextCommit"/>.
    ///
    /// <para>The oracle has to have teeth in BOTH directions, which is why the value typed after
    /// the Tab matters. Asserting only on the FirstName edit would pass with suppression removed
    /// entirely: that Tab would blur-commit <c>[1:Alicia,Smith]</c> on the spot and the final
    /// assertion would happily match it. Editing LastName after the Tab can only happen if the row
    /// is STILL in edit mode, so <c>[1:Alicia,Smythe]</c> proves the Tab did not commit — and its
    /// arrival at all proves the flag did not leak and swallow the anchor's blur-commit.</para>
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_RowEditTab_DoesNotSuppressNextCommit()
    {
        NavigateToFixtureFresh("DataGrid_RowEditGrid");
        WaitForText("RowEditLog", "Edits:");
        Assert.IsNotNull(WaitForName("Alice"), "'Alice' (row 1 FirstName) should be visible");

        BeginRowEditOnFirstRow();

        // Type into FirstName, Tab (arms the guard), then type into LastName — only reachable if
        // the Tab moved focus without committing.
        WaitForFocus("RowEdit_FirstName", "row edit start");
        ReplaceFocusedEditorText("Alicia");
        App.SendKeys(Keys.Tab, viaSendInput: true);
        WaitForFocus("RowEdit_LastName", "Tab from FirstName");
        ReplaceFocusedEditorText("Smythe");

        // Nothing may have committed yet.
        var logSoFar = FindById("RowEditLog").Text ?? "";
        Assert.IsFalse(logSoFar.Contains('['),
            $"Row-mode Tab must not commit the row; RowEditLog was '{logSoFar}'.");

        // Now leave the grid entirely. The row must still commit through the LostFocus path — the
        // one-shot flag was consumed by the Tab's own LostFocus and must not still be armed.
        Element("RowEditBlurAnchor").Click();

        WaitForTextContaining("RowEditLog", "[1:Alicia,Smythe]", timeoutMs: 5000);

        // Exactly one commit: a leaked-and-then-recovered flag, or a Tab that committed and then
        // re-committed on blur, both show up as a second entry.
        var log = FindById("RowEditLog").Text ?? "";
        Assert.AreEqual(1, log.Count(c => c == '['),
            $"Expected exactly one row commit; RowEditLog was '{log}'.");
    }

    /// <summary>
    /// Block until real keyboard focus is on the editor with <paramref name="expectedAutomationId"/>.
    /// </summary>
    /// <remarks>
    /// <para>Two jobs, and both matter.</para>
    ///
    /// <para><b>Synchronization.</b> The grid's focus move is deliberately deferred through
    /// <c>DispatcherQueue.TryEnqueue</c> (it has to land after WinUI's own Tab navigation), so it is
    /// NOT complete when <c>SendKeys(Tab)</c> returns. Typing straight after the Tab races that
    /// dispatcher tick and lands the text in whichever editor still had focus — which is exactly how
    /// this test previously "failed": it typed both values into FirstName and reported the product
    /// broken. A real user's Tab-then-type has orders of magnitude more slack than a test harness.</para>
    ///
    /// <para><b>Oracle.</b> It asserts the DESTINATION of the focus move, not merely that focus
    /// moved. "Focus left FirstName" is satisfied by landing on Save, on Cancel, or outside the grid
    /// entirely, so a test phrased that way cannot fail when the direction inverts — the exact
    /// vacuity trap called out in AGENTS.md § "Checks that actually prove something".</para>
    ///
    /// <para>Times out loudly with the observed AutomationId. <see cref="IUiaPropertyReader.GetFocusedAutomationId"/>
    /// returns <c>""</c> when focus is unreadable or on an element with no id, so a broken instrument
    /// surfaces as a failure naming what it saw, never as a silent pass.</para>
    /// </remarks>
    private static void WaitForFocus(string expectedAutomationId, string step, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string actual;
        do
        {
            actual = Uia.GetFocusedAutomationId();
            if (actual == expectedAutomationId) return;
            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail(
            $"After '{step}', keyboard focus should be on '{expectedAutomationId}' but was " +
            $"'{(actual.Length == 0 ? "<none/unreadable>" : actual)}' after {timeoutMs}ms.");
    }

    /// <summary>
    /// Click the first row's "Edit" button to enter row-edit mode and wait until its editors are
    /// realized. Row mode has one Edit button per row and they share a name, so take the topmost.
    /// </summary>
    private void BeginRowEditOnFirstRow()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(5000);
        while (DateTime.UtcNow < deadline)
        {
            var buttons = App.Search("Edit").Where(m => m.Name == "Edit").ToList();
            if (buttons.Count > 0)
            {
                var first = buttons[0];
                // Normalize a missing AutomationId to null rather than "": UiElement.GetAttribute
                // branches on `AutomationId != null`, so an empty-but-non-null id would send it
                // down the read-by-automation-id path with an empty id.
                var id = string.IsNullOrEmpty(first.AutomationId) ? null : first.AutomationId;
                Element(id ?? first.Selector, id).Click();
                // Save/Cancel only exist while the row is being edited, so their arrival is proof
                // the row edit actually started before we start pressing Tab.
                Assert.IsNotNull(WaitForName("Save"), "Row edit did not start — no 'Save' button appeared.");
                _ = WaitForEditor();
                return;
            }
            Thread.Sleep(100);
        }

        Assert.Fail("Row-mode 'Edit' button never appeared.");
    }

    /// <summary>
    /// Replace the contents of whatever editor currently holds real keyboard focus, WITHOUT
    /// locating it first. Row mode keeps every editable cell open at once, so
    /// <see cref="WaitForEditor"/>'s "first Edit control" would always resolve to the same editor
    /// and silently defeat the point of the test — the whole question is where focus is. Select-all
    /// + delete then typing goes to the focused control, whichever it is.
    ///
    /// <para>Both payloads are in winapp's send-keys token grammar (<c>ctrl+a</c>, <c>delete</c>,
    /// <c>text=</c>), NOT the Win32 <c>SendKeys.SendWait</c> <c>^a</c> shorthand — the CLI rejects
    /// the latter. <c>ctrl+a delete</c> mirrors <see cref="UiElement.Clear"/>, and the literal is
    /// escaped through <see cref="UiElement.ToSendKeysTokens"/> so the tokenizer keeps it intact.</para>
    /// </summary>
    private void ReplaceFocusedEditorText(string value)
    {
        App.SendKeys("ctrl+a delete", viaSendInput: true);
        App.SendKeys(UiElement.ToSendKeysTokens(value), viaSendInput: true);
        Thread.Sleep(150); // let TextChanged propagate into the pending row values before the next key
    }

    /// <summary>
    /// Tap a DataGrid cell by its visible text to enter/commit cell edit. The cells are
    /// display-only TextBlocks (no InvokePattern), and a WinUI <c>Tapped</c> only fires on an
    /// ACTIVE window, so winapp's UIA invoke/click can't drive them. We foreground the host and
    /// inject a real pointer click at the cell centre — the same proven path the gesture tests use.
    /// </summary>
    private void TapCell(string name)
    {
        FindByName(name).Click();
    }

    /// <summary>
    /// Replace the contents of the inline editor that appears after a cell tap. The editor is a
    /// TextBox with no AutomationId, so we locate it by UIA control type, then clear + type through
    /// <see cref="UiElement"/> (which focuses the editor via UIA SetFocus and injects real keystrokes
    /// through the native <c>winapp ui send-keys</c> verb).
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

            editor.SendKeys(value);
            lastSeen = ReadEditorValueSettled(editor, value, timeoutMs: 1500);
            if (lastSeen is null || lastSeen == value)
            {
                // Commit only AFTER the typed value has settled into the editor. The native send-keys
                // verb injects the text and any trailing key back-to-back, so a combined "value + Enter"
                // can fire the commit before the TextBox's TextChanged/binding has captured the text —
                // committing an empty value (observed as '[1:Alicia,]' on CI). Typing, settling, then
                // pressing Enter as a separate send mirrors the old per-keystroke injector, which only
                // pressed Enter after every character had propagated. (Once Enter commits, the editor
                // closes and can no longer be read, so the settle read must happen before it.)
                if (commitWithEnter)
                    editor.SendKeys(Keys.Enter);
                return; // confirmed correct, or unreadable (don't risk double-typing)
            }
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
