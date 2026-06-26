using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Spec 045 §2.6 — E2E coverage of the VS-style immediate tab tear-off
/// pipeline under real pointer input. The selftest fixtures in
/// <c>tests/Reactor.AppTests.Host/SelfTest/Fixtures/NativeDockingTearOffFixture.cs</c>
/// validate the state machine via synthetic Simulate*ForTest calls; this
/// class drives the same pipeline with a REAL mouse drag (so the
/// regressions a synthetic call can't see — WS_EX_TRANSPARENT hit-test
/// routing, WinUI pointer capture, Z-order ordering between source +
/// preview windows — are caught here).
///
/// Real mouse drags are synthesized with the Win32 <see cref="InputInjector"/>
/// fallback (winapp ui has no drag verb). Cross-window reads (e.g. a pane's
/// state mirror after it has been torn off into a floating window) enumerate
/// every Host-process window via <see cref="WinAppUi.ListWindows"/> and query
/// each by HWND — replacing the former Appium Desktop-rooted session.
/// </summary>
/// <remarks>
/// <para>Layout under test: <see cref="DockingTearOffE2EFixtures.TearOffFlowComponent"/>
/// renders a 3-tab host (EditorA / EditorB / EditorC), each pane has its
/// own controlled <c>TextBox</c> + state mirror, and a
/// <c>TearOff_Layout_Summary</c> TextBlock exposes the live host /
/// floating distribution as
/// <c>"host:A,B,C  float:  windows:0"</c>.</para>
/// <para>Not covered here (see #419): float→host dock-back, float→host
/// split, and Esc-mid-drag cancel — all have synthetic-event coverage in
/// the selftest fixtures.</para>
/// </remarks>
[TestClass]
public class DockingTearOffE2ETests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context)
    {
        // Process-wide DPI awareness must be set before screen-coordinate
        // math runs, so drag coordinates match actual pixel positions on
        // >100% scaled displays.
        EnsureDpiAware();
        TestSession.AssemblyInit(context);
    }

    [ClassCleanup]
    public static void StopAppSession()
    {
        TestSession.AssemblyCleanup();
    }

    // ───────────────────────────────────────────────────────────────────
    // P/Invoke + DPI awareness
    // ───────────────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetProcessDpiAwarenessContext(IntPtr dpiContext);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

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
    // Cross-window helpers (replace the former Appium Desktop session)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Locate a TabViewItem header by caption across the host + any floating windows and
    /// return its screen-pixel center. The tear-off press hook only fires when the pointer
    /// originates inside a TabViewItem's visual subtree, so the drag must start on the small
    /// tab header (not the wrapping content group of the same Name).
    /// </summary>
    private (int X, int Y) FindTabItemCenter(string title)
    {
        foreach (var w in App.ListWindows())
        {
            var matches = App.Search(title, w.Hwnd);
            // Prefer an exact-name TabItem; the tab header reports ControlType Tab(View)Item.
            var tab = matches.FirstOrDefault(m =>
                m.Name == title && (m.Type?.Contains("Tab", StringComparison.OrdinalIgnoreCase) ?? false));
            // Fall back to the smallest exact-name match (the header is far smaller than the
            // pane content group that shares the caption).
            tab ??= matches.Where(m => m.Name == title)
                           .OrderBy(m => (long)m.Width * m.Height)
                           .FirstOrDefault();
            if (tab is not null)
                return (tab.X + tab.Width / 2, tab.Y + tab.Height / 2);
        }
        throw new WinAppException($"No TabItem with Name '{title}' found in any Host window.");
    }

    /// <summary>Center of an element addressed by AutomationId in the host window.</summary>
    private (int X, int Y) HostElementCenter(string automationId)
    {
        var b = App.GetBounds(automationId)
            ?? throw new WinAppException($"Element '{automationId}' not found in host window.");
        return (b.CenterX, b.CenterY);
    }

    /// <summary>
    /// Cross-window WaitForText — for UIA elements that live inside a floating window's pane
    /// content (e.g. EditorA_State after A is torn off). Polls every Host-process window.
    /// </summary>
    private void WaitForTextAcrossWindows(string automationId, string expectedText, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string lastSeen = "<not found>";
        while (DateTime.UtcNow < deadline)
        {
            foreach (var w in App.ListWindows())
            {
                var text = App.GetValue(automationId, w.Hwnd);
                if (text is not null)
                {
                    lastSeen = text;
                    if (text == expectedText) return;
                }
            }
            Thread.Sleep(100);
        }
        throw new WinAppTimeoutException(
            $"Timed out after {timeoutMs}ms waiting for AutomationId='{automationId}' " +
            $"to have text '{expectedText}' (any window). Last-seen: '{lastSeen}'.");
    }

    /// <summary>Dump the fixture's UIA-visible diagnostic surface to the test log.</summary>
    private void DumpDiagnostics(string label)
    {
        string Read(string id) =>
            App.ListWindows()
               .Select(w => App.GetValue(id, w.Hwnd))
               .FirstOrDefault(t => t is not null) ?? "<not found>";
        Console.WriteLine($"[{label}] summary='{Read("TearOff_Layout_Summary")}'");
        Console.WriteLine($"[{label}] counters='{Read("TearOff_Event_Counters")}'");
        Console.WriteLine($"[{label}] trace='{Read("TearOff_Trace")}'");
    }

    /// <summary>
    /// Drive a real mouse drag from a tab header center to a target center. The cursor follows
    /// a multi-step path so WinUI's 4-DIP drag threshold fires (a single teleport can jump the
    /// cursor past the strip before the threshold check runs) and the overlay hover handlers
    /// see continuous motion. All scenarios here are "source-stays-visible" dock→float drops.
    /// </summary>
    private void DragFromTo((int X, int Y) from, (int X, int Y) to)
    {
        InputInjector.Foreground(HostHwnd);
        InputInjector.Drag(new[]
        {
            from, (from.X + 8, from.Y), (from.X + 16, from.Y),
            ((from.X + to.X) / 2, (from.Y + to.Y) / 2), to,
        });
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
    // [Retry] mops up the rare unattended-desktop input-injection flake: Win32 SendInput is
    // occasionally dropped before the Host window foregrounds on CI. A real regression still
    // fails every attempt. Removable once winappCli #562 (send-keys)/#498 (drag) ship native verbs.
    [Retry(3)]
    [TestMethod]
    public void TearOff_E01_DragTabOutOfHost_OpensFloatingWindow()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        DragFromTo(FindTabItemCenter("EditorA"), HostElementCenter("TearOff_DropOutsideZone"));

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
    /// floating windows.
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void TearOff_E02_MultipleSequentialTearOffs()
    {
        NavigateToFixtureFresh("DockingTearOff_Flow");
        WaitForText("TearOff_Layout_Summary",
            "host:A,B,C  float:  windows:0", timeoutMs: 5000);

        var dropZone = HostElementCenter("TearOff_DropOutsideZone");

        // First tear-off: A.
        DragFromTo(FindTabItemCenter("EditorA"), dropZone);
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
        DragFromTo(FindTabItemCenter("EditorB"), dropZone);
        try
        {
            // Order in float:... is alphabetical by key. After tearing
            // both A and B, host has only C and the floating list is A,B.
            WaitForText("TearOff_Layout_Summary",
                "host:C  float:A,B  windows:2", timeoutMs: 5000);
        }
        catch
        {
            DumpDiagnostics("E02 after-B");
            throw;
        }

        // Event-counter sanity: exactly 2 OnContentFloating + 2 OnContentFloated.
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
    [Retry(3)]
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
        DragFromTo(FindTabItemCenter("EditorA"), HostElementCenter("TearOff_DropOutsideZone"));
        WaitForText("TearOff_Layout_Summary",
            "host:B,C  float:A  windows:1", timeoutMs: 5000);

        // EditorA_State now lives inside the floating window's UIA tree.
        // The value must still be "preserved".
        WaitForTextAcrossWindows("EditorA_State",
            "EditorA state: preserved", timeoutMs: 5000);
    }

    // ─── E04 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Tear-off reliability under repeated invocations. Tears off A,
    /// resets the fixture, tears off A again. All iterations must produce
    /// identical post-state.
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void TearOff_E04_RepeatedTearOffsAreReliable()
    {
        for (int iter = 1; iter <= 3; iter++)
        {
            NavigateToFixtureFresh("DockingTearOff_Flow");
            WaitForText("TearOff_Layout_Summary",
                "host:A,B,C  float:  windows:0", timeoutMs: 5000);

            DragFromTo(FindTabItemCenter("EditorA"), HostElementCenter("TearOff_DropOutsideZone"));

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
