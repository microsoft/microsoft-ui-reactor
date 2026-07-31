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
    /// Regression for issue #987: the grid's KeyDown pipeline captured only the raw
    /// <c>VirtualKey</c> and deferred dispatch through <c>DispatcherQueue.TryEnqueue</c>, so the
    /// modifier state never reached the handler and <c>Shift+Tab</c> was indistinguishable from
    /// <c>Tab</c> — an editing Shift+Tab moved FORWARD. This is the only tier that exercises the
    /// real synchronous modifier read and the mounted handler wiring; the headless tests construct
    /// the chord themselves and cannot see either.
    ///
    /// <para>The oracle is the direction, not the commit: both directions commit the LastName edit
    /// identically, so the EditLog right after the chord cannot tell them apart. What differs is
    /// where the editor reopens — backward lands on FirstName (editable, editor reopens), forward
    /// lands on Salary (read-only, NO editor at all). Typing into the reopened editor and
    /// committing therefore produces <c>[1:Alicia,Smythe]</c> only if the move went backward; the
    /// forward bug cannot get that far, because there is no editor to type into.</para>
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_EditingShiftTab_ReopensEditorOnPreviousCell()
    {
        NavigateToFixtureFresh("DataGrid_EditableGrid");
        WaitForText("EditLog", "Edits:");
        Assert.IsNotNull(WaitForName("Alice"), "'Alice' (row 1 FirstName, the PREVIOUS cell) should be visible");
        Assert.IsNotNull(WaitForName("Smith"), "'Smith' (row 1 LastName) should be visible");

        // Edit row-1 LastName (Smith -> Smythe); type a new value but do NOT commit.
        TapCell("Smith");
        TypeIntoFocusedEditor("Smythe");

        // Editing Shift+Tab: commits LastName and must reopen the editor on the PREVIOUS cell
        // (FirstName). Forward would land on read-only Salary and reopen nothing.
        App.SendKeys("shift+tab", viaSendInput: true);

        // Fast-fail diagnostic, NOT an oracle: this can legitimately observe the OUTGOING LastName
        // editor before it is torn down, so its success proves nothing about direction. It is here
        // so the buggy case (no editor anywhere, because the chord moved forward onto the read-only
        // Salary column) reports that cause instead of timing out on the commit wait below.
        try { WaitForEditor(timeoutMs: 4000); }
        catch (WinAppException ex)
        {
            Assert.Fail("Inline editor did not reopen after the editing Shift+Tab. The chord most likely moved " +
                        "FORWARD onto the read-only Salary column — i.e. the modifier was dropped between the " +
                        "routed handler and the deferred dispatch (#987). " + ex.Message);
        }

        // The LastName commit from the chord must have landed, with FirstName untouched.
        WaitForTextContaining("EditLog", "[1:Alice,Smythe]", timeoutMs: 5000);

        // Unconditional proof the editor reopened on FirstName and not somewhere else: type into it
        // and commit. Editing FirstName commits row 1 as [1:Alicia,Smythe], preserving the LastName
        // just committed above. Had the editor reopened on LastName, this would read
        // [1:Alice,Alicia] and the wait would time out.
        TypeIntoFocusedEditor("Alicia", commitWithEnter: true);
        WaitForTextContaining("EditLog", "[1:Alicia,Smythe]", timeoutMs: 5000);
        Assert.IsNotNull(WaitForName("Alicia"), "Edited FirstName 'Alicia' should be visible after commit.");
        Assert.IsNotNull(WaitForName("Smythe"), "'Smythe' should still be visible after the second commit.");
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
    /// The backward twin of <see cref="Interactive_DataGrid_EditingTabToReadOnly_DoesNotSuppressNextCommit"/>.
    /// The <c>SuppressNextLostFocusCommit</c> guard is armed on the Tab KEY, deliberately without
    /// looking at the direction (#987), so Shift+Tab arms it exactly like Tab — and must therefore
    /// consume it exactly like Tab when the cell it lands on has no editor. Here: edit row-1
    /// FirstName (its PREVIOUS tab-order cell, Id, is read-only), press Shift+Tab, then edit a
    /// different row's cell and move focus off the grid. That second edit must commit — if the
    /// guard leaked, it silently swallows the focus-out commit and 'Bobby' is lost.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_DataGrid_EditingShiftTabToReadOnly_DoesNotSuppressNextCommit()
    {
        NavigateToFixtureFresh("DataGrid_EditableGrid");
        WaitForText("EditLog", "Edits:");
        Assert.IsNotNull(WaitForName("Alice"), "'Alice' (row 1 FirstName) should be visible");

        // Edit row-1 FirstName (Alice -> Alicia); Shift+Tab moves BACKWARD to Id (read-only), so no
        // editor reopens. The commit itself still has to land.
        TapCell("Alice");
        TypeIntoFocusedEditor("Alicia");
        App.SendKeys("shift+tab", viaSendInput: true);
        WaitForTextContaining("EditLog", "[1:Alicia,Smith]", timeoutMs: 5000);

        // Now edit a different row's cell and move focus OUT of the grid by clicking the anchor
        // button, firing the grid's "focus left the grid" LostFocus commit — the exact path the
        // guard suppresses. If the guard leaked from the Shift+Tab-into-read-only above, this
        // second edit is discarded and the wait below times out.
        Assert.IsNotNull(WaitForName("Bob"), "'Bob' (row 2 FirstName) should be visible");
        TapCell("Bob");
        TypeIntoFocusedEditor("Bobby");
        Element("BlurAnchor").Click(); // focus leaves the grid -> blur-commit through LostFocus

        WaitForTextContaining("EditLog", "[2:Bobby,Jones]", timeoutMs: 5000);
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
