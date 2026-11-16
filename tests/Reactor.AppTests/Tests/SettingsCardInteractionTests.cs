using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for spec 065 — the native SettingsCard and SettingsExpander under real input
/// and an out-of-process UI Automation client.
///
/// The <c>SettingsCard_E2E</c> host fixture renders a clickable card, a card with a toggle
/// switch, and a controlled expander. Selftests already cover the automation peers
/// in-process; these tests prove what only real input can: a pointer click and the Enter /
/// Space keys reaching a clickable card's <c>onClick</c> exactly once through
/// <c>ButtonBase</c>, Tab focus landing on the card, a real click on the expander header
/// raising <c>onIsExpandedChanged</c>, and the roles and names a screen reader sees
/// (spec 065 §Accessibility: a clickable card is a named Button; other cards are unnamed
/// Groups whose content is named after the header; the expander is a Group named after the
/// header).
/// </summary>
[TestClass]
public class SettingsCardInteractionTests : AppTestBase
{
    private const string Fixture = "SettingsCard_E2E";

    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    [TestMethod]
    public void Cards_ExposeToolkitRolesAndNames()
    {
        NavigateToFixtureFresh(Fixture);

        var clickable = FindById("ScClickable");
        Assert.AreEqual("button", clickable.GetAttribute("LocalizedControlType"));
        Assert.AreEqual("Storage", clickable.GetAttribute("Name"));

        var group = FindById("ScGroup");
        Assert.AreEqual("group", group.GetAttribute("LocalizedControlType"));
        Assert.IsTrue(string.IsNullOrEmpty(group.GetAttribute("Name")),
            "A non-clickable card never takes focus, so (like the Toolkit) it stays unnamed.");
        Assert.AreEqual("Notifications", FindById("ScToggle").GetAttribute("Name"),
            "Content without its own name takes the card header as its name.");

        var expander = FindById("ScExpander");
        Assert.AreEqual("group", expander.GetAttribute("LocalizedControlType"));
        Assert.AreEqual("Display", expander.GetAttribute("Name"));
    }

    // [E2eRetry] mops up the rare unattended-desktop input-injection flake (SendInput dropped
    // before the Host foregrounds). A real regression still fails every attempt.
    [E2eRetry(3)]
    [TestMethod]
    public void ClickableCard_RealClick_RunsOnClickOnce()
    {
        NavigateToFixtureFresh(Fixture);
        WaitForText("ScClicks", "Clicks: 0");

        FindById("ScClickable").Click();

        WaitForText("ScClicks", "Clicks: 1");
    }

    [E2eRetry(3)]
    [TestMethod]
    public void ClickableCard_TabFocus_EnterAndSpaceRunOnClick()
    {
        NavigateToFixtureFresh(Fixture);
        WaitForText("ScClicks", "Clicks: 0");

        FindById("ScBefore").SendKeys(Keys.Tab);
        Assert.AreEqual("True", FindById("ScClickable").GetAttribute("HasKeyboardFocus"),
            "Tab from the preceding button should land on the clickable card.");

        FindById("ScClickable").SendKeys(Keys.Enter);
        WaitForText("ScClicks", "Clicks: 1");

        FindById("ScClickable").SendKeys(Keys.Space);
        WaitForText("ScClicks", "Clicks: 2");
    }

    [E2eRetry(3)]
    [TestMethod]
    public void Expander_RealClickAndSpace_ToggleIsExpanded()
    {
        NavigateToFixtureFresh(Fixture);
        WaitForText("ScExpandedState", "Expanded: False");

        // Collapsed, the expander is all header, so its center is the expand button.
        FindById("ScExpander").Click();
        WaitForText("ScExpandedState", "Expanded: True");
        WaitForElement("ScSlider");

        // The click left keyboard focus on the header; Space collapses it again.
        FindById("ScExpander").SendKeys(Keys.Space);
        WaitForText("ScExpandedState", "Expanded: False");
    }
}
