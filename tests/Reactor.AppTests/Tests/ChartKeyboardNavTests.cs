using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for <c>ChartKeyboardNavigator.HandleKeyDown</c> (and the
/// <c>BuildFocusIndicator</c> overlay it triggers). Focuses an interactive chart's plot area
/// through the real Windows UIA pipeline, then injects the full keyboard-navigation vocabulary
/// with the native <c>winapp ui send-keys</c> verb (<c>--via send-input</c>, so the navigator's
/// modifier-state reads see real key state). This reaches every top-level arm of the navigator's
/// key switch (plus
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
    // [E2eRetry] mops up the rare unattended-desktop input-injection flake (the native winapp
    // send-keys verb is SendInput under the hood and is occasionally dropped before the Host window
    // foregrounds, or UIA SetFocus loses the race with the focus-overlay re-render). A real regression
    // still fails every attempt; retained pending a few stable CI runs on the native verbs (#652).
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
    /// Tries UIA SetFocus (primary), then a real pointer click on the plot area. Both strategies
    /// first LOCATE the chart, so we never inject the Enter probe blindly at an arbitrary focused
    /// element; a strategy is "confirmed" only when a subsequent Enter actually invokes a point.
    /// </summary>
    private bool EnsureChartFocusedAndKeyboardLive()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            // Strategy 1: UIA SetFocus on the plot area by its AutomationName.
            if (UiaFocusChart() && PressEnterAndCheckInvoked())
                return true;

            // Strategy 2: real pointer click inside the located plot area. Only probe with Enter when
            // the chart was actually located and clicked — never inject Enter blindly at whatever
            // holds focus, which could activate an unrelated control (nav/reset) and add noise/flake.
            if (ClickChartToFocus() && PressEnterAndCheckInvoked())
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

        try
        {
            App.Focus(match.Selector);
        }
        catch (WinAppException)
        {
            return false; // some builds reject SetFocus on a Canvas; caller falls back to a click
        }

        // The Canvas remains the window's focused element; the subsequent send-keys verb foregrounds
        // the Host itself before injecting, so keys route to it even if winapp's focus process briefly
        // took foreground.
        return true;
    }

    /// <summary>
    /// Click inside the plot area to focus the Canvas via a real pointer. Returns whether the chart
    /// was located and clicked — a <c>false</c> return means the caller must NOT treat focus as
    /// acquired (and must not then inject keys blindly). Pass a pre-resolved <paramref name="match"/>
    /// to click it directly and avoid a second, possibly-null, lookup.
    /// </summary>
    private bool ClickChartToFocus(UiMatch? match = null)
    {
        match ??= FindChartMatch();
        if (match is null)
            return false;

        // Offset in from the left edge, vertically centered, so the click lands on the focusable
        // plot Canvas rather than its outer axis margin. The native click verb is center-only, so
        // reproduce this exact off-center point with a zero-distance drag (a press + release in place
        // is a click, and with no movement it never crosses the drag threshold).
        int x = match.X + Math.Max(12, match.Width / 4);
        int y = match.Y + match.Height / 2;
        App.Drag($"{x},{y}", $"{x},{y}");
        return true;
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
        App.SendKeys(VkChord(VkEnter), viaSendInput: true);
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
        PressOnChart(VkRight);
        PressOnChart(VkLeft);
        PressOnChart(VkDown);
        PressOnChart(VkUp);
        Thread.Sleep(40);

        // Home / End, plus Ctrl+Home / Ctrl+End (jump to first/last series+point).
        PressOnChart(VkHome);
        PressOnChart(VkEnd);
        PressOnChart(VkHome, ctrl: true);
        PressOnChart(VkEnd, ctrl: true);
        Thread.Sleep(40);

        // Zoom in / out (numpad +/-, Ctrl+= , Ctrl+-) and reset zoom (Ctrl+0).
        PressOnChart(VkAdd);
        PressOnChart(VkSubtract);
        PressOnChart(VkOemPlus, ctrl: true);
        PressOnChart(VkOemMinus, ctrl: true);
        PressOnChart(Vk0, ctrl: true);
        Thread.Sleep(40);

        // Legend focus (L), speak summary (S), alternate-view toggle (T, bubbles), help (F1), and
        // the Shift+? help chord (VK_OEM_2 191 in the shift branch → OnShowHelp).
        PressOnChart(VkL);
        PressOnChart(VkS);
        PressOnChart(VkT);
        PressOnChart(VkF1);
        PressOnChart(VkOem2, shift: true);
        Thread.Sleep(40);

        // Shift+← / Shift+→ : brush selection.
        PressOnChart(VkRight, shift: true);
        PressOnChart(VkLeft, shift: true);
        Thread.Sleep(40);

        // Alt+arrows : pan.
        PressOnChart(VkLeft, alt: true);
        PressOnChart(VkRight, alt: true);
        PressOnChart(VkUp, alt: true);
        PressOnChart(VkDown, alt: true);
        Thread.Sleep(40);

        // Space : invoke the focused point again. Escape : deactivate the focus indicator.
        PressOnChart(VkSpace);
        PressOnChart(VkEscape);
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
        PressOnChart(VkHome);
        InvokeAndAssertFreshIndex(VkEnter, 0);

        // Right x3 -> point 3: proves Right advances the focused point.
        PressOnChart(VkRight);
        PressOnChart(VkRight);
        PressOnChart(VkRight);
        InvokeAndAssertFreshIndex(VkEnter, 3);

        // Left -> point 2: proves Left retreats the focused point.
        PressOnChart(VkLeft);
        InvokeAndAssertFreshIndex(VkEnter, 2);

        // End -> point 9 (last): proves End.
        PressOnChart(VkEnd);
        InvokeAndAssertFreshIndex(VkEnter, 9);

        // Ctrl+Home -> point 0 (first series, first point): proves Ctrl+Home.
        PressOnChart(VkHome, ctrl: true);
        InvokeAndAssertFreshIndex(VkEnter, 0);

        // Ctrl+End -> point 9 (last series, last point): proves Ctrl+End.
        PressOnChart(VkEnd, ctrl: true);
        InvokeAndAssertFreshIndex(VkEnter, 9);

        // Home -> 0, then Space invokes: proves the Space invoke key (distinct from Enter).
        PressOnChart(VkHome);
        InvokeAndAssertFreshIndex(VkSpace, 0);

        // Shift+Right moves PointIndex to brushEnd (0 -> 1); Space invokes: proves the Shift+Right
        // brush arm actually advances the focused point.
        PressOnChart(VkRight, shift: true);
        InvokeAndAssertFreshIndex(VkSpace, 1);

        // Shift+Left moves PointIndex back (1 -> 0); Space invokes: proves the Shift+Left brush arm.
        PressOnChart(VkLeft, shift: true);
        InvokeAndAssertFreshIndex(VkSpace, 0);
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
        // Re-focus before each key. UIA SetFocus is primary; if it's rejected on this build
        // (UiaFocusChart returns false) fall back to a real pointer click on the plot area — the
        // same strategy EnsureChartFocusedAndKeyboardLive uses — so the key actually lands on the
        // chart rather than on whatever else currently holds focus. Locate the chart ONCE and click
        // that exact match (no TOCTOU re-find that could return null and become a silent no-op); if
        // it can't be located at all, fail fast instead of injecting the key blindly at the current
        // focus — a silent miss would otherwise surface as a confusing downstream status-wait timeout.
        if (!UiaFocusChart())
        {
            var match = FindChartMatch()
                ?? throw new WinAppException(
                    $"Chart plot area '{ChartName}' could not be located to receive keyboard input; " +
                    "the fixture may have failed to render or lost its AutomationName.");
            ClickChartToFocus(match);
        }
        App.SendKeys(VkChord(virtualKey, ctrl: ctrl, shift: shift, alt: alt), viaSendInput: true);
        Thread.Sleep(45);
    }

    // Virtual-key codes for the navigator vocabulary (migrated from the retired InputInjector, whose
    // only consumer was this fixture). Each is emitted as a layout-independent `vk=0xNN` send-keys
    // token so the chord is expressible regardless of the keyboard layout or friendly-name grammar.
    private const ushort VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;
    private const ushort VkHome = 0x24, VkEnd = 0x23, VkEnter = 0x0D, VkSpace = 0x20, VkEscape = 0x1B;
    private const ushort VkAdd = 0x6B, VkSubtract = 0x6D, VkOemPlus = 0xBB, VkOemMinus = 0xBD;
    private const ushort VkF1 = 0x70, Vk0 = 0x30, VkL = 0x4C, VkS = 0x53, VkT = 0x54, VkOem2 = 0xBF;

    // Build a native send-keys chord for a virtual key with optional modifiers, e.g.
    // VkChord(VkHome, ctrl: true) -> "ctrl+vk=0x24". The winapp key grammar accepts vk= as the main
    // key of a modifier combo, so this expresses every navigator chord without needing friendly names.
    private static string VkChord(ushort vk, bool ctrl = false, bool shift = false, bool alt = false)
    {
        var sb = new System.Text.StringBuilder();
        if (ctrl) sb.Append("ctrl+");
        if (shift) sb.Append("shift+");
        if (alt) sb.Append("alt+");
        sb.Append("vk=0x").Append(vk.ToString("X2"));
        return sb.ToString();
    }

    // ─── Polling ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Wait (up to <paramref name="timeoutMs"/>) for the status text to contain
    /// <paramref name="substring"/>, returning whether it did. Delegates to winapp's own
    /// single-process internal polling (<see cref="WinAppUi.WaitForValue"/>) rather than looping
    /// <c>App.GetValue</c>, which would spawn a winapp.exe per tick.
    /// </summary>
    private bool PollStatusContains(string substring, int timeoutMs)
        => App.WaitForValue(StatusId, substring, contains: true, timeoutMs: timeoutMs);
}
