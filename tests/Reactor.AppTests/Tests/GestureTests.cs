using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for spec 027 Tier 3 gesture modifiers. Drives real user input
/// (mouse drag, right-click, double-click, mouse hold) against the host
/// fixtures declared in <c>GestureE2EFixtures.cs</c>.
///
/// winapp ui has no drag and no press-hold, so the pan + long-press gestures
/// use the Win32 <see cref="InputInjector"/> fallback; double/right click use
/// winapp's native click verbs.
/// </summary>
[TestClass]
public class GestureTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// .OnPan: mouse-drag a Border and verify the pan callback reports
    /// Began → Changed → Ended and cumulative translation is non-zero.
    /// </summary>
    // [Retry] mops up the rare unattended-desktop input-injection flake: Win32 SendInput is
    // occasionally dropped before the Host window foregrounds on CI. A real regression still
    // fails every attempt. Removable once winappCli #562 (send-keys)/#498 (drag) ship native verbs.
    [Retry(3)]
    [TestMethod]
    public void Interactive_OnPan_Drag_ReportsTranslationAndPhase()
    {
        NavigateToFixtureFresh("Gesture_Pan");

        WaitForText("PanPhase", "phase=idle");

        var r = FindById("PanTarget").Rect;
        InputInjector.Foreground(HostHwnd);
        InputInjector.Drag(InputInjector.DragPath(
            r.X + r.Width / 2, r.Y + r.Height / 2,
            r.X + r.Width / 2 + 60, r.Y + r.Height / 2 + 40));

        // Either Ended (best case) or Changed (if WinUI swallowed the last frame) is acceptable;
        // the important part is that the reconciler wired the manipulation events correctly.
        var phase = WaitForTextContaining("PanPhase", "phase=");
        Assert.IsTrue(phase == "phase=changed" || phase == "phase=ended",
            $"Expected changed|ended, got {phase}");

        // Translation should have moved — tolerance is loose because DPI + WinUI manipulation
        // smoothing mean the reported delta isn't pixel-exact.
        var tx = WaitForTextContaining("PanTranslation", "tx=");
        StringAssert.StartsWith(tx, "tx=", "Pan translation text should report tx=");
        Assert.AreNotEqual("tx=0 ty=0", tx, "Pan should have moved the translation counters");
    }

    /// <summary>
    /// .OnDoubleTap: double-click a Button and verify the count increments.
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void Interactive_OnDoubleTap_FiresOnDoubleClick()
    {
        NavigateToFixtureFresh("Gesture_DoubleTap");

        WaitForText("DoubleTapCount", "Doubletap count: 0");

        InputInjector.Foreground(HostHwnd);
        App.Click("DoubleTapTarget", doubleClick: true);

        WaitForText("DoubleTapCount", "Doubletap count: 1");
    }

    /// <summary>
    /// .OnRightTapped: right-click a Button and verify the count increments.
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void Interactive_OnRightTapped_FiresOnContextClick()
    {
        NavigateToFixtureFresh("Gesture_RightTap");

        WaitForText("RightTapCount", "Righttap count: 0");

        InputInjector.Foreground(HostHwnd);
        App.Click("RightTapTarget", rightClick: true);

        WaitForText("RightTapCount", "Righttap count: 1");
    }

    /// <summary>
    /// .OnLongPress: press-and-hold and verify the long-press callback fires.
    /// Uses mouse emulation (default-off in production; opted in by the fixture).
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void Interactive_OnLongPress_FiresAfterHold()
    {
        NavigateToFixtureFresh("Gesture_LongPress");

        WaitForText("LongPressCount", "Longpress count: 0");

        var r = FindById("LongPressTarget").Rect;
        InputInjector.Foreground(HostHwnd);
        InputInjector.PressHoldRelease(r.X + r.Width / 2, r.Y + r.Height / 2, holdMs: 600);

        WaitForText("LongPressCount", "Longpress count: 1", timeoutMs: 6000);
    }
}

