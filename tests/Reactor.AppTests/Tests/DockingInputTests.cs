using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Interactions;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Spec 045 — E2E keyboard-input + focus tests across docking layout
/// mutations. The bug class this guards against: a parent component's
/// setState (here, a controlled TextField's <c>OnChanged</c> handler)
/// causes the docking host to re-render, and some unconditional
/// property write deep inside the reconciler's tab-header update steals
/// focus from the focused TextField on every keystroke. Symptom:
/// "I type one character and have to click back in the textbox to type
/// another." The fix lives in
/// <c>Reconciler.Update.UpdateTabView</c> +
/// <c>Reconciler.Mount.TryUpdatePinHeaderInPlace</c>.
/// </summary>
[TestClass]
public class DockingInputTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// Type a multi-character string into the left pane's TextField,
    /// then Tab to the right pane and type another string. Both panes
    /// are pinnable ToolWindows in separate tab groups — the
    /// configuration that previously triggered the
    /// <see cref="WinUI.TabView"/> pin-header rebuild on every
    /// keystroke. Every character must land; focus must not bounce.
    /// </summary>
    [TestMethod]
    public void DockingInput_TypeAndTabAcrossPanes()
    {
        NavigateToFixtureFresh("DockingInput_TwoPaneTextFields");

        // Baseline: both states empty (the state TextBlocks read
        // "Left state: " / "Right state: " with a trailing space).
        WaitForText("DockEditor_Left_State", "Left state: ");
        WaitForText("DockEditor_Right_State", "Right state: ");

        // Click into the left TextField and type. A single SendKeys
        // delivers all characters; if focus were stolen between
        // keystrokes, fewer characters would land and the assertion
        // would observe a partial string like "Left state: he".
        var leftField = FindById("DockEditor_Left");
        leftField.Click();
        leftField.SendKeys("hello");

        WaitForText("DockEditor_Left_State", "Left state: hello", timeoutMs: 5000);

        // Tab from the focused left field. WinUI's tab traversal should
        // hop out of the left pane (past any tab strip / splitter
        // chrome) and land on the right pane's TextField.
        leftField.SendKeys(Keys.Tab);

        var rightField = FindById("DockEditor_Right");
        rightField.SendKeys("world");

        WaitForText("DockEditor_Right_State", "Right state: world", timeoutMs: 5000);
        // Left state must be preserved across the Tab traversal +
        // right pane edits.
        WaitForText("DockEditor_Left_State", "Left state: hello");
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
    [TestMethod]
    public void DockingInput_DragToTab_PreservesFocusAndState()
    {
        NavigateToFixtureFresh("DockingInput_TwoPaneTextFields");

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
        // motion (matches the DragDrop test convention — a single
        // MoveToElement is too abrupt for WinUI's drag-detection
        // threshold under synthesized Appium events).
        //
        // We locate the tab headers by Name (the tab caption maps to
        // UIA Name on the TabViewItem header). If WinAppDriver can't
        // resolve them by Name, fall back to a tab-strip walk by
        // class — but in practice the Name lookup is reliable.
        var rightTab = Session.FindElement(MobileBy.Name("Right"));
        var leftTab = Session.FindElement(MobileBy.Name("Left"));

        new Actions(Session)
            .MoveToElement(rightTab)
            .ClickAndHold()
            .MoveByOffset(-20, 0).MoveByOffset(-20, 0)
            .MoveToElement(leftTab)
            .Release()
            .Perform();

        // After the drag, both panes are tabs in the same group. Both
        // states must be intact — the §2.30 resolve step substitutes
        // back the app-supplied Content by Key, so the Memo state
        // slots inside each pane survive the layout mutation.
        WaitForText("DockEditor_Left_State", "Left state: alpha", timeoutMs: 5000);
        WaitForText("DockEditor_Right_State", "Right state: beta", timeoutMs: 5000);

        // The active tab post-drop is implementation-defined (could be
        // either the dragged source or the original target). Whichever
        // is active, typing into it must work — we resolve focus via
        // the active element, then send characters.
        var active = Session.SwitchTo().ActiveElement();
        active.SendKeys("X");

        // One of the two state TextBlocks must reflect the appended X.
        // We poll both — the active-tab pane is the one that grew.
        WaitForOneOfTexts(
            ("DockEditor_Left_State", "Left state: alphaX"),
            ("DockEditor_Right_State", "Right state: betaX"),
            timeoutMs: 5000);
    }

    /// <summary>
    /// Helper: wait until at least one of the given (id, expectedText)
    /// pairs matches. Used when an action's outcome can land in either
    /// of two automation slots (e.g. after a drop, the active tab is
    /// implementation-defined).
    /// </summary>
    private void WaitForOneOfTexts(
        (string Id, string Expected) a,
        (string Id, string Expected) b,
        int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string lastA = "<not found>", lastB = "<not found>";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                lastA = FindById(a.Id).Text ?? "<null>";
                if (lastA == a.Expected) return;
            }
            catch (WebDriverException) { /* keep polling */ }
            try
            {
                lastB = FindById(b.Id).Text ?? "<null>";
                if (lastB == b.Expected) return;
            }
            catch (WebDriverException) { /* keep polling */ }
            Thread.Sleep(100);
        }
        Assert.Fail(
            $"Neither '{a.Id}' nor '{b.Id}' reached expected text within {timeoutMs}ms. " +
            $"Last seen: '{a.Id}'='{lastA}', '{b.Id}'='{lastB}'.");
    }
}
