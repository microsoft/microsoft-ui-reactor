using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for <c>ChartKeyboardNavigator.HandleKeyDown</c> (and the
/// <c>BuildFocusIndicator</c> overlay it triggers). Focuses an interactive chart's plot area
/// through the real Windows UIA pipeline, then injects the full keyboard-navigation vocabulary
/// with the Win32 <see cref="InputInjector"/> fallback (winapp ui has no keyboard typing / arrow
/// keys). This drives every arm of the navigator's key switch cross-process — something the
/// in-process selftests cannot, because the modifier-state reads
/// (<c>InputKeyboardSource.GetKeyStateForCurrentThread</c>) require real injected key state.
///
/// The interactive chart wraps itself in a <c>FuncElement</c>, so an AutomationId on it does not
/// surface; the focusable plot-area Canvas instead exposes the chart <c>.Title()</c> as its
/// AutomationName ("Keyboard Nav Chart"), which is how we locate + focus it.
/// </summary>
[TestClass]
public class ChartKeyboardNavTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    private const string StatusId = "ChartKbd_E2E_Status";
    private const string ChartName = "Keyboard Nav Chart";

    /// <summary>
    /// Focus the interactive chart, confirm the keyboard pipeline is live (Enter on the focused
    /// point invokes it), then drive the entire HandleKeyDown vocabulary: point/series navigation,
    /// Home/End (+Ctrl), zoom (+/- , Ctrl+= , Ctrl+- , Ctrl+0), legend (L), summary (S),
    /// alternate view (T), help (F1), brush (Shift+←/→), pan (Alt+arrows), invoke (Space) and
    /// deactivate (Esc).
    /// </summary>
    // [E2eRetry] mops up the rare unattended-desktop input-injection flake (Win32 SendInput is
    // occasionally dropped before the Host window foregrounds, or UIA SetFocus loses the race with
    // the focus-overlay re-render). A real regression still fails every attempt. Removable once
    // winappCli #562 (send-keys) ships native keyboard verbs.
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_Chart_KeyboardVocabulary_DrivesHandleKeyDown()
    {
        NavigateToFixtureFresh("Chart_KeyboardNav");
        WaitForTextContaining(StatusId, "none");

        // 1. Focus the plot area and prove the cross-process keyboard pipeline is live: Enter on
        //    the focused point invokes it, so the status flips to "invoked:<index>".
        if (!EnsureChartFocusedAndKeyboardLive())
        {
            Assert.Fail(
                "Could not focus the interactive chart and drive its keyboard pipeline " +
                $"(status never became 'invoked:'; last-seen '{App.GetValue(StatusId)}').");
        }

        // 2. Drive the FULL HandleKeyDown switch. Re-focus before every key so each arm reaches
        //    the handler even when the focus-overlay re-render (bare Canvas -> Canvas+overlay Grid)
        //    drops keyboard focus.
        DriveFullKeyboardVocabulary();

        // 3. Non-vacuous behavioral proof that navigation + invoke actually reach the handler
        //    AFTER the whole vocabulary — a check the earlier liveness invoke ("invoked:0")
        //    cannot satisfy on its own. Home resets PointIndex to 0, three Rights advance it to
        //    3, and Enter invokes the current point, so the status must become exactly
        //    "invoked:3". SampleLine has 10 points and the FocusState persists in the fixture's
        //    Component state, so the index is deterministic. If focus were lost during the
        //    vocabulary (leaving a stale status), this fresh, distinct signal would not appear
        //    and the wait would fail — which is the point.
        PressOnChart(InputInjector.VkHome);
        PressOnChart(InputInjector.VkRight);
        PressOnChart(InputInjector.VkRight);
        PressOnChart(InputInjector.VkRight);
        PressOnChart(InputInjector.VkEnter);
        WaitForTextContaining(StatusId, "invoked:3");

        Assert.IsTrue(App.Exists(StatusId), "Chart keyboard fixture should still be present (no crash).");
    }

    // ─── Focus acquisition ────────────────────────────────────────────────────

    /// <summary>
    /// Acquire keyboard focus on the chart plot area and confirm the keyboard pipeline responds.
    /// Tries UIA SetFocus (primary), then a real pointer click on the plot area, then Tab-in — any
    /// strategy is "confirmed" only when a subsequent Enter actually invokes a point.
    /// </summary>
    private bool EnsureChartFocusedAndKeyboardLive()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            // Strategy 1: UIA SetFocus on the plot area by its AutomationName.
            if (UiaFocusChart() && PressEnterAndCheckInvoked())
                return true;

            // Strategy 2: real pointer click inside the focusable plot area.
            ClickChartToFocus();
            if (PressEnterAndCheckInvoked())
                return true;

            // Strategy 3: Tab in from the status label until the plot area takes focus.
            InputInjector.Foreground(HostHwnd);
            for (int t = 0; t < 4; t++)
                InputInjector.Tab();
            if (PressEnterAndCheckInvoked())
                return true;

            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>Move UIA keyboard focus to the plot-area Canvas (AutomationName == chart title).</summary>
    private bool UiaFocusChart()
    {
        var match = FindChartMatch();
        if (match is null)
            return false;

        InputInjector.Foreground(HostHwnd);
        try
        {
            App.Focus(match.Selector);
        }
        catch (WinAppException)
        {
            return false; // some builds reject SetFocus on a Canvas; caller falls back to click/Tab
        }

        // winapp's focus process may have briefly taken foreground; restore the Host so injected
        // keys route to it (the Canvas remains the window's focused element across the restore).
        InputInjector.Foreground(HostHwnd);
        return true;
    }

    /// <summary>Click inside the plot area to focus the Canvas via a real pointer.</summary>
    private void ClickChartToFocus()
    {
        var match = FindChartMatch();
        if (match is null)
            return;

        InputInjector.Foreground(HostHwnd);
        // Offset in from the left edge, vertically centered, so the click lands on the focusable
        // plot Canvas rather than its outer axis margin.
        int x = match.X + Math.Max(12, match.Width / 4);
        int y = match.Y + match.Height / 2;
        InputInjector.Click(x, y);
    }

    /// <summary>The plot-area match whose AutomationName carries the chart title.</summary>
    private UiMatch? FindChartMatch()
    {
        var matches = App.Search(ChartName);
        return matches.FirstOrDefault(m => m.Name != null && m.Name.Contains(ChartName))
            ?? matches.FirstOrDefault();
    }

    // ─── Key driving ──────────────────────────────────────────────────────────

    /// <summary>Press Enter on the (hopefully) focused chart and report whether a point invoked.</summary>
    private bool PressEnterAndCheckInvoked()
    {
        InputInjector.Foreground(HostHwnd);
        InputInjector.PressKey(InputInjector.VkEnter);
        return PollStatusContains("invoked:", timeoutMs: 900);
    }

    /// <summary>
    /// Inject the whole keyboard vocabulary understood by <c>ChartKeyboardNavigator.HandleKeyDown</c>.
    /// Each key is preceded by a re-focus so it reaches the handler regardless of the overlay
    /// re-render dropping focus.
    /// </summary>
    private void DriveFullKeyboardVocabulary()
    {
        // ← / → / ↓ / ↑ : point + series navigation. The first arrow flips the navigator into its
        // focused state, so the following render builds the double-ring focus overlay
        // (BuildFocusIndicator).
        PressOnChart(InputInjector.VkRight);
        PressOnChart(InputInjector.VkLeft);
        PressOnChart(InputInjector.VkDown);
        PressOnChart(InputInjector.VkUp);
        Thread.Sleep(40);

        // Home / End, plus Ctrl+Home / Ctrl+End (jump to first/last series+point).
        PressOnChart(InputInjector.VkHome);
        PressOnChart(InputInjector.VkEnd);
        PressOnChart(InputInjector.VkHome, ctrl: true);
        PressOnChart(InputInjector.VkEnd, ctrl: true);
        Thread.Sleep(40);

        // Zoom in / out (numpad +/-, Ctrl+= , Ctrl+-) and reset zoom (Ctrl+0).
        PressOnChart(InputInjector.VkAdd);
        PressOnChart(InputInjector.VkSubtract);
        PressOnChart(InputInjector.VkOemPlus, ctrl: true);
        PressOnChart(InputInjector.VkOemMinus, ctrl: true);
        PressOnChart(InputInjector.Vk0, ctrl: true);
        Thread.Sleep(40);

        // Legend focus (L), speak summary (S), alternate-view toggle (T, bubbles), help (F1).
        PressOnChart(InputInjector.VkL);
        PressOnChart(InputInjector.VkS);
        PressOnChart(InputInjector.VkT);
        PressOnChart(InputInjector.VkF1);
        Thread.Sleep(40);

        // Shift+← / Shift+→ : brush selection.
        PressOnChart(InputInjector.VkRight, shift: true);
        PressOnChart(InputInjector.VkLeft, shift: true);
        Thread.Sleep(40);

        // Alt+arrows : pan.
        PressOnChart(InputInjector.VkLeft, alt: true);
        PressOnChart(InputInjector.VkRight, alt: true);
        PressOnChart(InputInjector.VkUp, alt: true);
        PressOnChart(InputInjector.VkDown, alt: true);
        Thread.Sleep(40);

        // Space : invoke the focused point again. Escape : deactivate the focus indicator.
        PressOnChart(InputInjector.VkSpace);
        PressOnChart(InputInjector.VkEscape);
    }

    /// <summary>Re-focus the plot area, then inject one (optionally chorded) key.</summary>
    private void PressOnChart(ushort virtualKey, bool ctrl = false, bool shift = false, bool alt = false)
    {
        // Re-focus before each key. If UIA SetFocus is rejected on this build (UiaFocusChart
        // returns false), fall back to a real pointer click on the plot area — the same focus
        // strategy EnsureChartFocusedAndKeyboardLive uses — so the key actually lands on the chart
        // rather than on whatever else currently holds focus.
        if (!UiaFocusChart())
            ClickChartToFocus();
        InputInjector.PressKeyWith(virtualKey, ctrl: ctrl, shift: shift, alt: alt);
        Thread.Sleep(45);
    }

    // ─── Polling ──────────────────────────────────────────────────────────────

    private bool PollStatusContains(string substring, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            var value = App.GetValue(StatusId);
            if (value != null && value.Contains(substring))
                return true;
            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);
        return false;
    }
}
