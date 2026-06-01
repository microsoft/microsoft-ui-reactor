using System.Runtime.InteropServices;
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
/// <para><b>What this suite covers via real mouse input:</b></para>
/// <list type="bullet">
///   <item>E01: single-tab tear-off → floating window (drop-outside).</item>
///   <item>E02: multiple sequential tear-offs (A then B → two floating windows).</item>
///   <item>E03: pane content state survives the tear-off layout mutation.</item>
///   <item>E04: tear-off pipeline reliable across repeated invocations.</item>
/// </list>
/// <para><b>Not covered here — see #419 for details and selftest fixtures
/// for synthetic-event coverage:</b></para>
/// <list type="bullet">
///   <item>Float → host dock-back via real mouse drag. When the source
///   floating window has a single tab, <c>BeginFloatingTearOff</c>
///   hides it once the 4-DIP threshold crosses; WinAppDriver's Actions
///   pipeline freezes on the now-vanished session-bound HWND and
///   subsequent moves / wobbles don't deliver to the host. Selftest
///   fixtures <c>T04_FloatToHostCenter</c> /
///   <c>T05_FloatToHostSplit</c> exercise the same code paths via
///   synthetic <c>Simulate*ForTest</c> calls.</item>
///   <item>Float → host split. Same root cause.</item>
///   <item>Esc-mid-drag cancel. Selenium 3 + WinAppDriver can't reliably
///   hold Esc down across the tracker's 16 ms poll cycle. Synthetic
///   coverage in <c>T13_EscCancelDuringDrag</c>.</item>
/// </list>
/// </remarks>
[TestClass]
public class DockingTearOffE2ETests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context)
    {
        // Process-wide DPI awareness must be set before any WinAppDriver
        // session opens, so that screen-coordinate math in Actions chains
        // matches actual pixel positions on >100% scaled displays.
        EnsureDpiAware();
        TestSession.AssemblyInit(context);
    }

    [ClassCleanup]
    public static void StopAppSession()
    {
        // Tear down the optional cross-window driver first — must happen
        // before WinAppDriver shuts down via TestSession cleanup.
        if (_desktopSession is not null)
        {
            try { _desktopSession.Quit(); }
            catch (WebDriverException) { /* driver already dead — best-effort */ }
            _desktopSession = null;
        }
        TestSession.AssemblyCleanup();
    }

    // ───────────────────────────────────────────────────────────────────
    // P/Invoke + DPI awareness
    // ───────────────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetProcessDpiAwarenessContext(IntPtr dpiContext);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    // Process-wide DPI context is set once from ClassInitialize, before any
    // WinAppDriver session opens. Failure modes: missing API on older
    // Windows (EntryPointNotFoundException / DllNotFoundException) or the
    // context already being set by the host process — both are harmless
    // for these tests' Actions-based screen-coordinate math, so we log
    // and continue rather than fail the whole suite.
    private static void EnsureDpiAware()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) == 0)
            {
                Console.WriteLine(
                    $"[DPI] SetProcessDpiAwarenessContext returned 0 (Win32 error " +
                    $"{Marshal.GetLastWin32Error()}); continuing with process default.");
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Console.WriteLine($"[DPI] API unavailable on this Windows build ({ex.GetType().Name}); continuing.");
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Cross-window driver
    //
    // The shared Session attached via TestSession.AssemblyInit binds to
    // the host-app top-level window. After a tear-off, the dragged pane
    // lives in a separate top-level window; we need a Desktop-rooted
    // driver to find it. Lazily opened, torn down in [ClassCleanup].
    // ───────────────────────────────────────────────────────────────────

    private const string WinAppDriverUrl = "http://127.0.0.1:4723";
    private static readonly TimeSpan DesktopSessionImplicitWait = TimeSpan.FromSeconds(2);
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
            _desktopSession.Manage().Timeouts().ImplicitWait = DesktopSessionImplicitWait;
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
    // Search via DesktopSession so we find TabItems in both the host
    // window and any floating preview windows (the host-bound session
    // sees only the host's UIA tree).
    private WindowsElement FindTabItem(string title)
    {
        return (WindowsElement)DesktopSession.FindElement(
            MobileBy.XPath($"//TabItem[@Name='{title}']"));
    }

    // Cross-window WaitForText — for UIA elements that live inside a
    // floating window's pane content (e.g. EditorA_State after A is
    // torn off). The host-bound Session can't see floating-window UIA.
    //
    // Implicit wait is suppressed for the duration of this poll so each
    // FindElement returns/throws immediately and the 100 ms cadence
    // actually fires; otherwise the driver's 2 s implicit wait dominates
    // and a 5 s timeout would only get ~2 polls.
    private void WaitForTextAcrossWindows(string automationId, string expectedText, int timeoutMs = 5000)
    {
        // WinAppDriver doesn't expose GET /timeouts (W3C-only), so we
        // can't read the current value back. Restore to the constant we
        // initialized DesktopSession with.
        var timeouts = DesktopSession.Manage().Timeouts();
        timeouts.ImplicitWait = TimeSpan.Zero;
        try
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
        finally
        {
            timeouts.ImplicitWait = DesktopSessionImplicitWait;
        }
    }

    /// <summary>Dump the fixture's UIA-visible diagnostic surface
    /// (layout summary, event counters, tear-off pipeline trace) to
    /// the test log. Use in catch blocks so the failure message points
    /// at WHICH stage of the pipeline didn't fire.</summary>
    private void DumpDiagnostics(string label)
    {
        string Read(string id)
        {
            try { return DesktopSession.FindElement(MobileBy.AccessibilityId(id)).Text ?? "<null>"; }
            catch (WebDriverException) { return "<not found>"; }
        }
        Console.WriteLine($"[{label}] summary='{Read("TearOff_Layout_Summary")}'");
        Console.WriteLine($"[{label}] counters='{Read("TearOff_Event_Counters")}'");
        Console.WriteLine($"[{label}] trace='{Read("TearOff_Trace")}'");
    }

    // ───────────────────────────────────────────────────────────────────
    // Drag helpers
    //
    // All drag scenarios in this suite are "source-stays-visible" —
    // they're dock→float drops on a drop-outside zone (E01, E02, E03,
    // E04). The source tab's host window stays put throughout the drag,
    // so WinAppDriver's session-bound input pipeline keeps tracking the
    // cursor. Actions runs via DesktopSession so it can resolve elements
    // from any top-level window.
    //
    // Float→host drags (the original E02/E04 scenarios from issue #419)
    // are blocked by a WinAppDriver limitation: the source floating
    // window gets hidden mid-drag once threshold crosses, and the
    // session-bound input pipeline can't continue routing moves to the
    // target. Those scenarios are covered by selftest fixtures
    // (NativeDockingTearOffFixture T04/T05) using synthetic events.
    // ───────────────────────────────────────────────────────────────────

    /// <summary>Drive a real mouse drag from <paramref name="source"/>'s
    /// center to <paramref name="target"/>'s center via a single
    /// WinAppDriver Actions chain. Both elements MUST be resolved via
    /// <see cref="DesktopSession"/> (Actions rejects elements from a
    /// different driver — see #419). The cursor follows a multi-step
    /// path so WinUI's 4-DIP drag threshold fires and the overlay
    /// hover handlers see continuous motion.</summary>
    private void DragFromTo(WindowsElement source, WindowsElement target)
        => DragFromToOffset(source, target, targetXOffset: 0, targetYOffset: 0);

    private void DragFromToOffset(WindowsElement source, WindowsElement target, int targetXOffset, int targetYOffset)
    {
        new Actions(DesktopSession)
            .MoveToElement(source)
            .ClickAndHold()
            // Two small offsets clear the 4-DIP drag threshold so the
            // tear-off pipeline's BeginTearOff callback fires before
            // the big MoveToElement teleport. A single MoveToElement
            // to a target hundreds of pixels away can be emitted as
            // one input event that "jumps" the cursor past the strip
            // before the threshold check fires.
            .MoveByOffset(8, 0)
            .MoveByOffset(8, 0)
            .MoveToElement(target, targetXOffset, targetYOffset)
            .Release()
            .Perform();
        // Cursor-poll Finalize + host re-render are async — settle.
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
        var dropZone = (WindowsElement)DesktopSession.FindElement(
            MobileBy.AccessibilityId("TearOff_DropOutsideZone"));
        DragFromTo(tabA, dropZone);

        try
        {
            WaitForText("TearOff_Layout_Summary",
                "host:B,C  float:A  windows:1", timeoutMs: 5000);
        }
        catch
        {
            DumpDiagnostics("E01 post-drag");
            throw;
        }
    }

    // ─── E02 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Multiple sequential tear-offs: tear off A, then tear off B. After
    /// both drags the host retains only C and there are two distinct
    /// floating windows. Exercises the press-hook reset path (a leaked
    /// candidate from the first drag would break the second), the
    /// per-pane DockManager event surface (each tear-off must fire
    /// OnContentFloating / OnContentFloated independently), and the
    /// floating-window lifecycle counter (must increment exactly twice).
    /// </summary>
    [TestMethod]
    public void TearOff_E02_MultipleSequentialTearOffs()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        var dropZone = (WindowsElement)DesktopSession.FindElement(
            MobileBy.AccessibilityId("TearOff_DropOutsideZone"));

        // First tear-off: A.
        DragFromTo(FindTabItem("EditorA"), dropZone);
        try
        {
            WaitForText("TearOff_Layout_Summary",
                "host:B,C  float:A  windows:1", timeoutMs: 5000);
        }
        catch
        {
            DumpDiagnostics("E02 after-A");
            throw;
        }

        // Second tear-off: B.
        DragFromTo(FindTabItem("EditorB"), dropZone);
        try
        {
            // Order in float:... is alphabetical by key (see
            // DockingTearOffE2EFixtures.OrderBy(s)). After tearing
            // both A and B, host has only C and the floating list
            // is A,B.
            WaitForText("TearOff_Layout_Summary",
                "host:C  float:A,B  windows:2", timeoutMs: 5000);
        }
        catch
        {
            DumpDiagnostics("E02 after-B");
            throw;
        }

        // Event-counter sanity: exactly 2 OnContentFloating + 2
        // OnContentFloated, 0 OnContentDocked.
        var counters = FindById("TearOff_Event_Counters").Text;
        Assert.IsTrue(counters?.StartsWith("floating:2  floated:2") == true,
            $"Expected 2 floating / 2 floated events, got: '{counters}'");
    }

    // ─── E03 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Tear-off state preservation: type into EditorA's TextBox first,
    /// then tear off the tab. The pane's controlled state is held in
    /// the host component's <c>useState</c>, NOT inside the TabView's
    /// runtime state — so the §2.30 shape-only override must resolve
    /// back to the app-supplied content (carrying the typed value)
    /// when the floating window re-mounts the pane.
    /// </summary>
    [TestMethod]
    public void TearOff_E03_TearOff_PreservesPaneState()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        // Type into A's input. The state mirror reflects it.
        var inputA = FindById("EditorA_Input");
        inputA.Click();
        Thread.Sleep(250);
        inputA.SendKeys("preserved");
        WaitForText("EditorA_State", "EditorA state: preserved", timeoutMs: 5000);

        // Tear A off into a floating window.
        var tabA = FindTabItem("EditorA");
        var dropZone = (WindowsElement)DesktopSession.FindElement(
            MobileBy.AccessibilityId("TearOff_DropOutsideZone"));
        DragFromTo(tabA, dropZone);
        WaitForText("TearOff_Layout_Summary",
            "host:B,C  float:A  windows:1", timeoutMs: 5000);

        // EditorA_State now lives inside the floating window's UIA
        // tree. Cross-window WaitForText reaches it via the Desktop
        // session. The value must still be "preserved" — the
        // Component's UseState slot survives RemovePane because the
        // pane reference is the same and the controlled-input loop
        // reattaches in the floating mount.
        WaitForTextAcrossWindows("EditorA_State",
            "EditorA state: preserved", timeoutMs: 5000);
    }

    // ─── E04 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Tear-off reliability under repeated invocations. Tears off A,
    /// resets the fixture, tears off A again. If the first drag leaked
    /// a candidate, a stuck tracker, or a Dock drag session, the second
    /// drag would fail (no Press fires, or BeginTearOff is refused).
    /// All iterations must produce identical post-state.
    /// </summary>
    /// <remarks>
    /// Esc-mid-drag cancel was attempted as an additional E04 (verifies
    /// the <c>commit=False</c> path inside <c>Tracker.OnTick</c>'s
    /// VK_ESCAPE poll) but is too timing-sensitive under WinAppDriver:
    /// Selenium 3's <c>SendKeys(Keys.Escape)</c> emits a sub-millisecond
    /// key-down/up pair that the tracker's 16 ms poll regularly misses,
    /// and <c>Actions.KeyDown</c> only accepts modifier keys. The Esc
    /// cancel path has synthetic-event coverage in
    /// <c>NativeDockingTearOffFixture.T13_EscCancelDuringDrag</c>.
    /// </remarks>
    [TestMethod]
    public void TearOff_E04_RepeatedTearOffsAreReliable()
    {
        for (int iter = 1; iter <= 3; iter++)
        {
            NavigateToFixtureFresh("DockingTearOff_Flow");
            WaitForText("TearOff_Layout_Summary",
                "host:A,B,C  float:  windows:0", timeoutMs: 5000);

            var tabA = FindTabItem("EditorA");
            var dropZone = (WindowsElement)DesktopSession.FindElement(
                MobileBy.AccessibilityId("TearOff_DropOutsideZone"));
            DragFromTo(tabA, dropZone);

            try
            {
                WaitForText("TearOff_Layout_Summary",
                    "host:B,C  float:A  windows:1", timeoutMs: 5000);
            }
            catch
            {
                DumpDiagnostics($"E04 iter#{iter}");
                throw;
            }
        }
    }
}
