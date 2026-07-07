using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Spec 045 — E2E keyboard-input + focus tests across docking layout
/// mutations. The bug class this guards against: a parent component's
/// setState (here, a controlled TextBox's <c>OnChanged</c> handler)
/// causes the docking host to re-render, and some unconditional
/// property write deep inside the reconciler's tab-header update steals
/// focus from the focused TextBox on every keystroke. Symptom:
/// "I type one character and have to click back in the textbox to type
/// another." The fix lives in
/// <c>Reconciler.Update.UpdateTabView</c> +
/// <c>Reconciler.Mount.TryUpdatePinHeaderInPlace</c>.
///
/// Keyboard input + tab-header drags are synthesized via the Win32
/// <see cref="InputInjector"/> fallback (winapp ui has no typing or drag verb).
/// </summary>
[TestClass]
public class DockingInputTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    private static (int X, int Y) Center(Rectangle r) => (r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>
    /// Drag one tab header onto another pane to merge the two panes into one tab group.
    /// <para>
    /// The dragged tab is grabbed by its HEADER caption (a user picks up a tab by its header). The
    /// host TabViews are <c>CanDragTabs=false</c>, so the merge runs through the IMMEDIATE tear-off
    /// pipeline (spec 045 §2.6): crossing the drag threshold tears the "from" pane off into a float,
    /// and settling the cursor on the "to" pane's merge zone latches the overlay's "Add as tab"
    /// target so the float merges back in.
    /// </para>
    /// <para>
    /// The drop target is computed PRE-DRAG and is stable — this custom tear-off drag does NOT
    /// reflow the surviving pane, so the merge zone stays at the "to" pane's original position. It
    /// is the X of the "to" tab caption (<see cref="AppTestBase.FindByName"/>, over the tab header)
    /// combined with the vertical CENTRE of the "to" pane body (<c>pane:dock-input:&lt;to&gt;</c>
    /// bounds) — the header X alone aims too high, at the caption row.
    /// </para>
    /// </summary>
    private void DragTabOnto(string fromName, string toName)
    {
        var grab = Center(FindByName(fromName).Rect);

        var headerRect = FindByName(toName).Rect;
        var paneId = $"pane:dock-input:{toName.ToLowerInvariant()}";
        var paneRect = App.GetBounds(paneId)
            ?? throw new WinAppException($"Target pane '{paneId}' not found pre-drag for drag-merge.");
        var drop = (X: headerRect.X + headerRect.Width / 2, Y: paneRect.Y + paneRect.Height / 2);

        InputInjector.Foreground(HostHwnd);
        InputInjector.DragTearOffMerge(grab, drop);
    }

    /// <summary>
    /// Type a multi-character string into the left pane's TextBox,
    /// then Tab to the right pane and type another string. Both panes
    /// are pinnable ToolWindows in separate tab groups — the
    /// configuration that previously triggered the
    /// <see cref="WinUI.TabView"/> pin-header rebuild on every
    /// keystroke. Every character must land; focus must not bounce.
    /// </summary>
    // [Retry] mops up the rare unattended-desktop input-injection flake: Win32 SendInput is
    // occasionally dropped before the Host window foregrounds on CI. A real regression still
    // fails every attempt. Removable once winappCli #562 (send-keys)/#498 (drag) ship native verbs.
    [E2eRetry(3)]
    [TestMethod]
    public void DockingInput_TypeAndTabAcrossPanes()
    {
        NavigateToFixtureFresh("DockingInput_TwoPaneTextBoxes");

        // Baseline: both states empty (the state TextBlocks read
        // "Left state: " / "Right state: " with a trailing space).
        WaitForText("DockEditor_Left_State", "Left state: ");
        WaitForText("DockEditor_Right_State", "Right state: ");

        // Click into the left TextBox and type. The Thread.Sleep
        // gives WinUI time to settle focus into the inner Edit
        // control before SendKeys delivers the first character.
        var leftField = FindById("DockEditor_Left");
        leftField.Click();
        Thread.Sleep(250);
        leftField.SendKeys("hello");

        WaitForText("DockEditor_Left_State", "Left state: hello", timeoutMs: 5000);

        // Tab from the focused left field. WinUI's tab traversal should
        // hop out of the left pane (past any tab strip / splitter
        // chrome) and land on the right pane's TextBox.
        leftField.SendKeys(Keys.Tab);

        var rightField = FindById("DockEditor_Right");
        rightField.SendKeys("world");

        WaitForText("DockEditor_Right_State", "Right state: world", timeoutMs: 5000);
        // Left state must be preserved across the Tab traversal +
        // right pane edits.
        WaitForText("DockEditor_Left_State", "Left state: hello");
    }

    /// <summary>
    /// Control variant of <see cref="DockingInput_TypeAndTabAcrossPanes"/>:
    /// identical scenario but the fixture uses bare DockableContent with
    /// <c>CanPin: false</c> on both panes. If this test PASSES while the
    /// pinned-pane variant fails, the bug is gated by the pin-affordance
    /// reconcile path in <c>UpdateTabView</c>. If both fail, the bug is
    /// more general and lives in the docking host's render itself.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void DockingInput_NoPin_TypeAndTabAcrossPanes()
    {
        NavigateToFixtureFresh("DockingInput_TwoPaneTextBoxesNoPin");

        WaitForText("DockEditorNoPin_Left_State", "Left state: ");
        WaitForText("DockEditorNoPin_Right_State", "Right state: ");

        var leftField = FindById("DockEditorNoPin_Left");
        leftField.Click();
        Thread.Sleep(250);
        leftField.SendKeys("hello");

        WaitForText("DockEditorNoPin_Left_State", "Left state: hello", timeoutMs: 5000);

        leftField.SendKeys(Keys.Tab);
        var rightField = FindById("DockEditorNoPin_Right");
        rightField.SendKeys("world");
        WaitForText("DockEditorNoPin_Right_State", "Right state: world", timeoutMs: 5000);
        WaitForText("DockEditorNoPin_Left_State", "Left state: hello");
    }

    /// <summary>
    /// Drag the right pane's tab into the left pane's tab group
    /// (Center drop = tabbed siblings). After the layout mutation the
    /// shape-only override stores just the pane Keys; the §2.30
    /// resolve step substitutes back the app-supplied Content (which
    /// holds the Memo state, which holds the typed text). Typing into
    /// the newly-tabbed pane must still work, and the pre-existing
    /// values from both editors must survive the layout change.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void DockingInput_DragToTab_PreservesFocusAndState()
    {
        NavigateToFixtureFresh("DockingInput_TwoPaneTextBoxes");

        // Seed both editors before the layout change so we can verify
        // post-mutation state survival.
        var leftField = FindById("DockEditor_Left");
        leftField.Click();
        leftField.SendKeys("alpha");
        WaitForText("DockEditor_Left_State", "Left state: alpha");

        leftField.SendKeys(Keys.Tab);
        var rightField = FindById("DockEditor_Right");
        rightField.SendKeys("beta");
        WaitForText("DockEditor_Right_State", "Right state: beta");

        // Drag the right tab's header onto the left tab's header. The
        // mid-travel offsets force WinUI to observe continuous pointer
        // motion (matches the DragDrop test convention).
        DragTabOnto("Right", "Left");

        // After the drag, both panes are tabs in the same group. The drag ended on the tab
        // HEADER, so keyboard focus sits on the tab, not an editable field — click the active
        // pane's input box first (what a user would do), then type.
        //
        // WinUI TabView surfaces only the SELECTED tab's content to UIA, so only the active
        // pane's DockEditor_* elements are in the tree; address whichever is realized.
        var activeInputId = App.Exists("DockEditor_Left") ? "DockEditor_Left" : "DockEditor_Right";
        var activeInput = FindById(activeInputId);
        activeInput.Click();
        activeInput.SendKeys("X");
        Thread.Sleep(250);

        var activeState = App.GetValue(activeInputId + "_State") ?? "";
        Assert.IsTrue(
            activeState.EndsWith("X"),
            $"Typing into the post-merge active tab should append 'X' to its " +
            $"state label. {activeInputId}_State='{activeState}'.");
    }

    /// <summary>
    /// Companion to <see cref="DockingInput_DragToTab_PreservesFocusAndState"/>:
    /// after dragging the right pane's tab into the left pane's group,
    /// both pre-drag state values ("alpha" / "beta") must survive.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void DockingInput_DragToTab_PreservesPreDragState()
    {
        NavigateToFixtureFresh("DockingInput_TwoPaneTextBoxes");

        var leftField = FindById("DockEditor_Left");
        leftField.Click();
        Thread.Sleep(250);
        leftField.SendKeys("alpha");
        WaitForText("DockEditor_Left_State", "Left state: alpha");

        leftField.SendKeys(Keys.Tab);
        var rightField = FindById("DockEditor_Right");
        rightField.SendKeys("beta");
        WaitForText("DockEditor_Right_State", "Right state: beta");

        DragTabOnto("Right", "Left");
        Thread.Sleep(500);

        // After the merge both panes are tabs in one TabView. WinUI surfaces only the
        // SELECTED tab's content to UIA, so the other pane's state TextBlock is not in the
        // tree until its tab is activated — select each tab before asserting its (preserved)
        // pre-drag state value.
        SelectTab("Left");
        WaitForText("DockEditor_Left_State", "Left state: alpha", timeoutMs: 5000);
        SelectTab("Right");
        WaitForText("DockEditor_Right_State", "Right state: beta", timeoutMs: 5000);
    }
}
