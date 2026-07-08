using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for <c>ChartKeyboardNavigator.HandleKeyDown</c> (and the
/// <c>BuildFocusIndicator</c> overlay it triggers). Focuses an interactive chart's plot area
/// through the real Windows UIA pipeline, then injects the full keyboard-navigation vocabulary
/// with the Win32 <see cref="InputInjector"/> fallback (winapp ui has no keyboard typing / arrow
/// keys). This reaches every top-level arm of the navigator's key switch (plus
/// <c>BuildFocusIndicator</c>) cross-process — though not the legend-focused Enter/Space/Esc
/// sub-branches, which need a legend this fixture doesn't enable — something the in-process
/// selftests cannot do, because the modifier-state reads
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
    /// point invokes it), drive the full key vocabulary for line coverage — point/series
    /// navigation, Home/End (+Ctrl), zoom (+/- , Ctrl+= , Ctrl+- , Ctrl+0), legend (L), summary (S),
    /// alternate view (T), help (F1), brush (Shift+←/→), pan (Alt+arrows), invoke (Space) and
    /// deactivate (Esc) — then behaviorally assert every arm whose effect the fixture can observe
    /// (see <see cref="AssertObservablePointArms"/>). This reaches every top-level HandleKeyDown
    /// arm; the legend-focused
    /// Enter/Space/Esc sub-branches need a legend the fixture doesn't enable.
    /// </summary>
    // [E2eRetry] mops up the rare unattended-desktop input-injection flake (Win32 SendInput is
    // occasionally dropped before the Host window foregrounds, or UIA SetFocus loses the race with
    // the focus-overlay re-render). A real regression still fails every attempt. Removable once
    // winappCli #562 (send-keys) ships native keyboard verbs.
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_Chart_KeyboardVocabulary_DrivesHandleKeyDown()
    {
        NavigateToFixtureFresh("D3_ChartKeyboardNav");
        WaitForTextContaining(StatusId, "none");

        // 1. Focus the plot area and prove the cross-process keyboard pipeline is live: Enter on
        //    the focused point invokes it, so the status flips to "invoked:<index>".
        if (!EnsureChartFocusedAndKeyboardLive())
        {
            Assert.Fail(
                "Could not focus the interactive chart and drive its keyboard pipeline " +
                $"(status never became 'invoked:'; last-seen '{App.GetValue(StatusId)}').");
        }

        // 2. Line-coverage pass: drive EVERY top-level HandleKeyDown arm once — including the arms
        //    whose option handlers the DSL leaves null (zoom / pan / brush / legend / summary /
        //    help), the series-switch arms, and Escape, whose effects this fixture's
        //    point-index-only status cannot observe. This is a COVERAGE drive, not a behavioral
        //    assertion; the per-arm behavioral proof is step 3. Re-focus before every key so each
        //    arm reaches the handler even when the focus-overlay re-render (bare Canvas ->
        //    Canvas+overlay Grid) drops keyboard focus.
        DriveFullKeyboardVocabulary();

        // 3. Non-vacuous behavioral proof for every arm whose effect the fixture CAN observe: each
        //    key that moves the focused point index (Home, Right, Left, End, Ctrl+Home, Ctrl+End,
        //    and the Shift+arrow brush keys, which also move PointIndex) plus the two invoke keys
        //    (Enter, Space). Each assertion there fails if the arm it targets is deleted or no-op'd.
        //    (Series-switch Up/Down, the null-handler zoom/pan/brush-callback/legend/summary/help
        //    arms, and Escape's focus-deactivate produce no point-index change this fixture
        //    surfaces, so they stay covered-only by step 2 — not behaviorally asserted.)
        AssertObservablePointArms();

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
    /// Line-coverage drive of the whole keyboard vocabulary understood by
    /// <c>ChartKeyboardNavigator.HandleKeyDown</c> — every top-level switch arm, including the ones
    /// this fixture cannot observe (null-handler zoom / pan / brush-callback / legend / summary /
    /// help, series-switch Up/Down, and Escape). This asserts nothing on its own; behavioral
    /// validation of the observable arms lives in <see cref="AssertObservablePointArms"/>. Each key
    /// is preceded by a re-focus so it reaches the handler regardless of the overlay re-render
    /// dropping focus.
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

    /// <summary>
    /// Behavioral proof for every HandleKeyDown arm whose effect this fixture can observe: each key
    /// that moves the focused point index — Home, Right, Left, End, Ctrl+Home, Ctrl+End, and the
    /// Shift+arrow brush keys (which also move <c>PointIndex</c>) — plus the two invoke keys (Enter,
    /// Space). Each step navigates from a known position and invokes, asserting the EXACT resulting
    /// point index, so the assertion fails if that arm is deleted or made a no-op (the anti-vacuous
    /// contract). Consecutive target indices are always distinct, and <see
    /// cref="InvokeAndAssertFreshIndex"/> additionally guards that the status doesn't already show
    /// the target before each invoke — so every assertion must observe a real state transition and
    /// cannot pass on a stale status. SampleLine has 10 points, the FocusState persists in the
    /// fixture Component, and a non-shift move resets any prior brush selection, so every index below
    /// is deterministic.
    /// </summary>
    private void AssertObservablePointArms()
    {
        // Home -> point 0, then Enter invokes: proves the Home and Enter arms.
        PressOnChart(InputInjector.VkHome);
        InvokeAndAssertFreshIndex(InputInjector.VkEnter, 0);

        // Right x3 -> point 3: proves Right advances the focused point.
        PressOnChart(InputInjector.VkRight);
        PressOnChart(InputInjector.VkRight);
        PressOnChart(InputInjector.VkRight);
        InvokeAndAssertFreshIndex(InputInjector.VkEnter, 3);

        // Left -> point 2: proves Left retreats the focused point.
        PressOnChart(InputInjector.VkLeft);
        InvokeAndAssertFreshIndex(InputInjector.VkEnter, 2);

        // End -> point 9 (last): proves End.
        PressOnChart(InputInjector.VkEnd);
        InvokeAndAssertFreshIndex(InputInjector.VkEnter, 9);

        // Ctrl+Home -> point 0 (first series, first point): proves Ctrl+Home.
        PressOnChart(InputInjector.VkHome, ctrl: true);
        InvokeAndAssertFreshIndex(InputInjector.VkEnter, 0);

        // Ctrl+End -> point 9 (last series, last point): proves Ctrl+End.
        PressOnChart(InputInjector.VkEnd, ctrl: true);
        InvokeAndAssertFreshIndex(InputInjector.VkEnter, 9);

        // Home -> 0, then Space invokes: proves the Space invoke key (distinct from Enter).
        PressOnChart(InputInjector.VkHome);
        InvokeAndAssertFreshIndex(InputInjector.VkSpace, 0);

        // Shift+Right moves PointIndex to brushEnd (0 -> 1); Space invokes: proves the Shift+Right
        // brush arm actually advances the focused point.
        PressOnChart(InputInjector.VkRight, shift: true);
        InvokeAndAssertFreshIndex(InputInjector.VkSpace, 1);

        // Shift+Left moves PointIndex back (1 -> 0); Space invokes: proves the Shift+Left brush arm.
        PressOnChart(InputInjector.VkLeft, shift: true);
        InvokeAndAssertFreshIndex(InputInjector.VkSpace, 0);
    }

    /// <summary>
    /// Press an invoke key (Enter / Space) on the focused chart and wait until the status reports
    /// exactly <paramref name="expectedIndex"/>. Guards against a vacuous pass: the status must NOT
    /// already show that index before the invoke — <see cref="WaitForTextContaining"/> returns
    /// immediately on an already-matching value, so without this guard an assertion whose target
    /// happened to equal the current status would pass without the just-navigated arm proving
    /// anything. The caller therefore must navigate to a point index different from the last-invoked
    /// one before each call (the sequence in <see cref="AssertObservablePointArms"/> does), and this
    /// guard fails loudly if that ever stops holding.
    /// </summary>
    private void InvokeAndAssertFreshIndex(ushort invokeKey, int expectedIndex)
    {
        var target = $"invoked:{expectedIndex}";
        var before = App.GetValue(StatusId) ?? "";
        Assert.IsFalse(before.Contains(target),
            $"Vacuous-assertion guard: status already reads '{before}' before invoking for '{target}'. " +
            "The preceding navigation must land on a point index different from the last invoked one.");
        PressOnChart(invokeKey);
        WaitForTextContaining(StatusId, target);
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
