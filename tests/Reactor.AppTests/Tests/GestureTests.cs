using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for spec 027 Tier 3 gesture modifiers. Drives real user input
/// (mouse drag, right-click, double-click, mouse hold) against the host
/// fixtures declared in <c>GestureE2EFixtures.cs</c>.
///
/// The pan gesture uses the native <c>winapp ui drag</c> verb; the long-press uses the same
/// verb with <c>--hold-ms</c> (press-and-hold in place); double/right click use winapp's
/// native click verbs.
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
    // [E2eRetry] mops up the rare unattended-desktop input-injection flake: the native winapp
    // send-keys/drag verbs are SendInput under the hood and are occasionally dropped before the Host
    // foregrounds on CI. A real regression still fails every attempt; retained pending a few stable CI runs (#652).
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_OnPan_Drag_ReportsTranslationAndPhase()
    {
        NavigateToFixtureFresh("Gesture_Pan");

        WaitForText("PanPhase", "phase=idle");

        var r = FindById("PanTarget").Rect;
        var fromX = r.X + r.Width / 2;
        var fromY = r.Y + r.Height / 2;
        // Native drag from the target's center to an offset destination; the CLI interpolates the
        // motion so WinUI observes continuous pointer movement across its manipulation threshold.
        App.Drag("PanTarget", $"{fromX + 60},{fromY + 40}");

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
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_OnDoubleTap_FiresOnDoubleClick()
    {
        NavigateToFixtureFresh("Gesture_DoubleTap");

        WaitForText("DoubleTapCount", "Doubletap count: 0");

        App.Click("DoubleTapTarget", doubleClick: true);

        WaitForText("DoubleTapCount", "Doubletap count: 1");
    }

    /// <summary>
    /// .OnRightTapped: right-click a Button and verify the count increments.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_OnRightTapped_FiresOnContextClick()
    {
        NavigateToFixtureFresh("Gesture_RightTap");

        WaitForText("RightTapCount", "Righttap count: 0");

        App.Click("RightTapTarget", rightClick: true);

        WaitForText("RightTapCount", "Righttap count: 1");
    }

    /// <summary>
    /// .OnLongPress: press-and-hold and verify the long-press callback fires.
    /// Uses mouse emulation (default-off in production; opted in by the fixture).
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void Interactive_OnLongPress_FiresAfterHold()
    {
        NavigateToFixtureFresh("Gesture_LongPress");

        WaitForText("LongPressCount", "Longpress count: 0");

        // from == to (no movement) with --hold-ms presses and holds the button in place, then
        // releases — a press-and-hold / long-press at the target's center.
        App.Drag("LongPressTarget", "LongPressTarget", holdMs: 600);

        WaitForText("LongPressCount", "Longpress count: 1", timeoutMs: 6000);
    }
}

