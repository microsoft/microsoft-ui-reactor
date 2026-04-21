using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Interactions;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for spec 027 Tier 3 gesture modifiers. Drives real user input
/// (mouse drag, right-click, double-click, mouse hold) against the host
/// fixtures declared in <c>GestureE2EFixtures.cs</c>.
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
    [TestMethod]
    public void Interactive_OnPan_Drag_ReportsTranslationAndPhase()
    {
        NavigateToFixtureFresh("Gesture_Pan");

        WaitForText("PanPhase", "phase=idle");

        var target = FindById("PanTarget");
        new Actions(Session)
            .MoveToElement(target)
            .ClickAndHold()
            .MoveByOffset(60, 40)
            .Release()
            .Perform();

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
    [TestMethod]
    public void Interactive_OnDoubleTap_FiresOnDoubleClick()
    {
        NavigateToFixtureFresh("Gesture_DoubleTap");

        WaitForText("DoubleTapCount", "Doubletap count: 0");

        new Actions(Session)
            .DoubleClick(FindById("DoubleTapTarget"))
            .Perform();

        WaitForText("DoubleTapCount", "Doubletap count: 1");
    }

    /// <summary>
    /// .OnRightTapped: right-click a Button and verify the count increments.
    /// </summary>
    [TestMethod]
    public void Interactive_OnRightTapped_FiresOnContextClick()
    {
        NavigateToFixtureFresh("Gesture_RightTap");

        WaitForText("RightTapCount", "Righttap count: 0");

        new Actions(Session)
            .ContextClick(FindById("RightTapTarget"))
            .Perform();

        WaitForText("RightTapCount", "Righttap count: 1");
    }

    /// <summary>
    /// .OnLongPress: press-and-hold and verify the long-press callback fires.
    /// Uses mouse emulation (default-off in production; opted in by the fixture).
    /// </summary>
    [TestMethod]
    public void Interactive_OnLongPress_FiresAfterHold()
    {
        NavigateToFixtureFresh("Gesture_LongPress");

        WaitForText("LongPressCount", "Longpress count: 0");

        var target = FindById("LongPressTarget");
        new Actions(Session)
            .MoveToElement(target)
            .ClickAndHold()
            .Perform();
        Thread.Sleep(TimeSpan.FromMilliseconds(600));
        new Actions(Session).Release().Perform();

        WaitForText("LongPressCount", "Longpress count: 1", timeoutMs: 6000);
    }
}
