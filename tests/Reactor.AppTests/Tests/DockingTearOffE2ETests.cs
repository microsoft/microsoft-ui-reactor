using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Interactions;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Spec 045 §2.6 — E2E coverage of the VS-style immediate tab tear-off
/// pipeline under real WinAppDriver / Appium pointer input. The
/// selftest fixtures in
/// <c>tests/Reactor.AppTests.Host/SelfTest/Fixtures/NativeDockingTearOffFixture.cs</c>
/// validate the state machine via synthetic Simulate*ForTest calls; this
/// class drives the same pipeline with a REAL mouse drag (so the
/// regressions a synthetic call can't see — WS_EX_TRANSPARENT hit-test
/// routing, WinUI pointer capture, Z-order ordering between source +
/// preview windows — are caught here).
/// </summary>
/// <remarks>
/// <para>Layout under test: <see cref="DockingTearOffE2EFixtures.TearOffFlowComponent"/>
/// renders a 3-tab host (EditorA / EditorB / EditorC), each pane has its
/// own controlled <c>TextBox</c> + state mirror, and a
/// <c>TearOff_Layout_Summary</c> TextBlock exposes the live host /
/// floating distribution as
/// <c>"host:A,B,C  float:  windows:0"</c> so the test asserts against
/// a single string match.</para>
/// <para>A large <c>TearOff_DropOutsideZone</c> Border sits below the
/// dock host so a drag <c>MoveToElement</c> on it lands the cursor
/// outside every Dock-edge button — the canonical drop-outside path.</para>
/// </remarks>
[TestClass]
public class DockingTearOffE2ETests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession()
    {
        // Tear down the optional cross-window driver first — must happen
        // before WinAppDriver shuts down via TestSession cleanup.
        if (_desktopSession is not null)
        {
            try { _desktopSession.Quit(); } catch { /* best-effort */ }
            _desktopSession = null;
        }
        TestSession.AssemblyCleanup();
    }

    // ───────────────────────────────────────────────────────────────────
    // Drag helpers
    //
    // The Actions chain here is patterned after DockingInputTests'
    // drag-to-tab test: multi-step MoveByOffset so WinUI's pointer
    // pipeline observes continuous motion (a single MoveToElement is too
    // abrupt for the tear-off's 4-DIP threshold detection to fire reliably
    // under synthesized Appium events on slow CI VMs).
    //
    // After the threshold crosses, the tear-off opens a floating preview
    // window that tracks the cursor in real-time via the cursor-poll
    // tracker. Each subsequent MoveByOffset / MoveToElement re-positions
    // it. On Release, the pipeline finalizes against whatever overlay
    // (if any) had a latched hover at release time.
    // ───────────────────────────────────────────────────────────────────

    // Desktop-rooted driver for cross-window queries. The shared Session is
    // attached to the host-app top-level window via appTopLevelWindow, so it
    // can't see floating-window UIA content. After a tear-off, the dragged
    // pane (and its TabItem header) lives in a separate top-level window;
    // we need a Desktop-rooted driver to find it. The session is opened
    // lazily on first use and torn down in [ClassCleanup].
    private const string WinAppDriverUrl = "http://127.0.0.1:4723";
    private static WindowsDriver<WindowsElement>? _desktopSession;
    private static WindowsDriver<WindowsElement> DesktopSession
    {
        get
        {
            if (_desktopSession is not null) return _desktopSession;
            var opts = new AppiumOptions();
            opts.AddAdditionalCapability("app", "Root");
            opts.AddAdditionalCapability("deviceName", "WindowsPC");
            _desktopSession = new WindowsDriver<WindowsElement>(new Uri(WinAppDriverUrl), opts);
            _desktopSession.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(2);
            return _desktopSession;
        }
    }

    // FindByName(title) matches the broadest UIA element with that Name —
    // for a docked pane that's the wrapping ControlType.Group (header +
    // content), so MoveToElement lands in the content body. The tear-off
    // press hook is on the TabView and only fires when the pointer
    // originates inside a TabViewItem's visual subtree, so the drag must
    // start ON the tab header — the smaller ControlType.TabItem element.
    //
    // Search via the Desktop session so we find TabItems in both the host
    // window and any floating preview windows (the host-bound session sees
    // only the host's UIA tree). MoveToElement uses screen coordinates so
    // the resulting element works fine when passed to Actions(Session).
    private WindowsElement FindTabItem(string title)
    {
        return (WindowsElement)DesktopSession.FindElement(
            MobileBy.XPath($"//TabItem[@Name='{title}']"));
    }

    // Cross-window WaitForText — for UIA elements that live inside a
    // floating-window's pane content (e.g. EditorA_State after A is torn
    // off). The host session sees only the host window's tree.
    private void WaitForTextAcrossWindows(string automationId, string expectedText, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string lastSeen = "<not found>";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var el = DesktopSession.FindElement(MobileBy.AccessibilityId(automationId));
                lastSeen = el.Text ?? "<null>";
                if (lastSeen == expectedText) return;
            }
            catch (WebDriverException) { /* element may not exist yet */ }
            Thread.Sleep(100);
        }
        throw new WebDriverTimeoutException(
            $"Timed out after {timeoutMs}ms waiting for AutomationId='{automationId}' " +
            $"to have text '{expectedText}' (Desktop session). Last-seen: '{lastSeen}'.");
    }

    // Selenium Actions ties the drag to a specific driver's element ID
    // resolution. The host Session is bound to the host window; the
    // tear-off tests cross between host and floating windows. Running
    // Actions through the Desktop-rooted driver lets it resolve elements
    // from either window — and on WinAppDriver, Actions just translate
    // to global Win32 cursor / button events anyway, so the resulting
    // input reaches whichever app window the cursor is over.
    private void DragFromTo(WindowsElement source, WindowsElement target)
        => DragFromToOffset(source, target, targetXOffset: 0, targetYOffset: 0);

    private void DragFromToOffset(WindowsElement source, WindowsElement target, int targetXOffset, int targetYOffset)
    {
        new Actions(DesktopSession)
            .MoveToElement(source)
            .ClickAndHold()
            // Two small offsets first — gives the press-down + threshold
            // detection a couple of pointer-move events before the big
            // MoveToElement jump (matches the dock-input test's proven
            // pattern; a single MoveToElement is too abrupt for WinUI's
            // drag-detection under synthesized Appium events).
            .MoveByOffset(10, 0)
            .MoveByOffset(10, 0)
            .MoveToElement(target, targetXOffset, targetYOffset)
            .Release()
            .Perform();
        // Cursor-poll tick is 16 ms and the host re-render is async after
        // session.End() — 500 ms is plenty of settle time.
        Thread.Sleep(500);
    }

    // ─── E01 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Dock → float: drag EditorA's tab into the drop-outside zone below
    /// the host. After release the pane must be in a floating window, the
    /// host must retain B + C, and the floating-window-count event surface
    /// must show 1 window open.
    /// </summary>
    [TestMethod]
    public void TearOff_E01_DragTabOutOfHost_OpensFloatingWindow()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        var tabA = FindTabItem("EditorA");
        // Resolve via DesktopSession too — the drag runs through Actions
        // on the desktop driver, which needs both elements from that
        // driver to resolve their IDs.
        var dropZone = (WindowsElement)DesktopSession.FindElement(
            MobileBy.AccessibilityId("TearOff_DropOutsideZone"));
        DragFromTo(tabA, dropZone);

        // Diagnostic: if the post-drag wait fails, surface the fixture's
        // event counters + tear-off trace so the failure message tells us
        // which pipeline stage broke (gate? mutation? re-dock?). Anything
        // 0 narrows the stage that didn't fire.
        try
        {
            WaitForText("TearOff_Layout_Summary",
                "host:B,C  float:A  windows:1", timeoutMs: 5000);
        }
        catch
        {
            var counters = FindById("TearOff_Event_Counters").Text;
            var summary = FindById("TearOff_Layout_Summary").Text;
            var trace = FindById("TearOff_Trace").Text;
            Console.WriteLine($"[diagnostic] summary='{summary}'");
            Console.WriteLine($"[diagnostic] counters='{counters}'");
            Console.WriteLine($"[diagnostic] trace='{trace}'");
            throw;
        }
    }

    // ─── E02 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Float → host roundtrip: tear off EditorA, then drag it back over
    /// EditorB's tab header. The float-to-host pipeline (BeginFloatingTearOff
    /// → AppWindow.Hide() of the source → cursor-poll preview → per-group
    /// Center overlay confirm) must re-insert A into the host's group.
    /// Final layout: floating count back to 0, A docked alongside B + C.
    /// </summary>
    [TestMethod]
    [Ignore("Cross-window drag — see #419. Selenium Actions(driver) rejects elements from a different driver, and the floating-window tab requires the Desktop-rooted driver. Pending Win32-mouse drag rewrite.")]
    public void TearOff_E02_FloatingTabDocksBackToHost()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        // Phase 1 — tear off A.
        var tabA = FindTabItem("EditorA");
        var dropZone = FindById("TearOff_DropOutsideZone");
        DragFromTo(tabA, dropZone);
        WaitForText("TearOff_Layout_Summary",
            "host:B,C  float:A  windows:1", timeoutMs: 5000);

        // Phase 2 — drag A's tab from the floating window back over
        // EditorB's tab header (Center drop = tabs in the same group).
        // FindTabItem resolves across all process windows; after Phase 1
        // there's exactly one TabItem named "EditorA" — in the floating
        // window.
        var floatingTabA = FindTabItem("EditorA");
        var tabB = FindTabItem("EditorB");
        DragFromTo(floatingTabA, tabB);

        // After Center confirm, A is back in the host's group with B + C.
        // The floating window closes (its only pane just moved out), so
        // windows:0 again.
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 8000);
    }

    // ─── E03 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Tear-off state preservation: type into EditorA's TextBox first,
    /// then tear off the tab. The pane's controlled state is held in the
    /// host component's <c>useState</c>, NOT inside the TabView's runtime
    /// state, so the §2.30 shape-only override must resolve back to the
    /// app-supplied content (carrying the typed value) when the floating
    /// window re-mounts the pane. Symptom of a regression: typed text
    /// vanishes when the pane moves between host and floating window.
    /// </summary>
    [TestMethod]
    [Ignore("Cross-window drag — see #419. The tear-off step works (E01 covers it) but verifying state in the floating window still hits the Selenium cross-driver Actions limitation when re-using the drag helper. Pending Win32-mouse drag rewrite.")]
    public void TearOff_E03_TearOff_PreservesPaneState()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        // Type into A's input. The state mirror should reflect it.
        var inputA = FindById("EditorA_Input");
        inputA.Click();
        Thread.Sleep(250);
        inputA.SendKeys("preserved");
        WaitForText("EditorA_State", "EditorA state: preserved", timeoutMs: 5000);

        // Tear A off into a floating window.
        var tabA = FindTabItem("EditorA");
        var dropZone = FindById("TearOff_DropOutsideZone");
        DragFromTo(tabA, dropZone);
        WaitForText("TearOff_Layout_Summary",
            "host:B,C  float:A  windows:1", timeoutMs: 5000);

        // EditorA_State now lives inside the floating window's UIA tree.
        // The host-bound Session can't see it; use the Desktop-rooted
        // helper instead. The value must still be "preserved" — the state
        // Component's UseState slot survives the host's RemovePane because
        // the pane reference is the same and the controlled-input loop
        // reattaches in the floating mount.
        WaitForTextAcrossWindows("EditorA_State", "EditorA state: preserved", timeoutMs: 5000);
    }

    // ─── E04 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Float → host into a SPLIT slot: tear off A, then drop it on the
    /// SplitRight area of the host (drag to the right edge of EditorB's
    /// tab area). The host's root overlay's <c>DockRight</c> edge button
    /// receives the confirm, MovePaneToTarget creates a horizontal split
    /// with B + C on the left and A on the right. The layout summary
    /// only tracks pane-to-window membership (not the split shape), so
    /// the observable outcome is windows:0 + A back in host.
    /// </summary>
    [TestMethod]
    [Ignore("Cross-window drag — see #419. Same root cause as E02 — the second drag step originates in the floating window. Pending Win32-mouse drag rewrite.")]
    public void TearOff_E04_FloatingDocksToHostEdge()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        var tabA = FindTabItem("EditorA");
        var dropZone = FindById("TearOff_DropOutsideZone");
        DragFromTo(tabA, dropZone);
        WaitForText("TearOff_Layout_Summary",
            "host:B,C  float:A  windows:1", timeoutMs: 5000);

        // Drag A's floating tab to the right edge of EditorB's tab — the
        // SplitRight target on B's per-group overlay (or DockRight on the
        // root overlay; either way A ends up back in host).
        var floatingTabA = FindTabItem("EditorA");
        var tabB = FindTabItem("EditorB");
        DragFromToOffset(floatingTabA, tabB, targetXOffset: 300, targetYOffset: 0);

        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 8000);
    }
}
