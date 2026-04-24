using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium.Appium;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for the spec 028 in-app devtools UX.
///
/// Exercises the full loop: the lightning-bolt DevtoolsMenu trigger renders when
/// <see cref="ReactorApp.DevtoolsEnabled"/> is on, clicking it opens a flyout,
/// selecting the "Debug UI" ToggleMenuItem writes back to the backing
/// <c>Observable&lt;bool&gt;</c>, and a subscribed TextBlock reflects the new
/// value through the Reactor reconciliation pipeline.
/// </summary>
[TestClass]
public class DevtoolsUxTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context)
    {
        TestSession.AssemblyInit(context);
    }

    [ClassCleanup]
    public static void StopAppSession()
    {
        TestSession.AssemblyCleanup();
    }

    /// <summary>
    /// Trigger visibility: when the fixture mounts (which sets DevtoolsEnabled=true),
    /// the lightning-bolt Button should be findable by AutomationId.
    /// </summary>
    [TestMethod]
    public void Devtools_Trigger_Is_Visible_When_Enabled()
    {
        NavigateToFixture("DevtoolsUx_MenuAndToggle");

        // Trigger button present with the lightning glyph as its Name.
        var trigger = WaitForElement("DevtoolsTrigger");
        Assert.IsNotNull(trigger);

        // Initial flag state — mirrored TextBlock reads the Observable<bool>.
        WaitForText("DebugUiState", "debug-off");
    }

    /// <summary>
    /// Clicking the trigger + toggling the "Debug UI" MenuFlyoutItem writes through
    /// to the backing Observable&lt;bool&gt;, which re-renders the subscribed
    /// TextBlock and makes the conditional DebugOverlay element appear.
    /// </summary>
    [TestMethod]
    public void Devtools_Menu_Toggle_Flows_Through_To_Subscribers()
    {
        // Fresh navigation re-mounts the fixture component, which runs the
        // one-shot DebugUI reset inside DevtoolsUxTestComponent.Render. A plain
        // NavigateToFixture is a no-op when the fixture is already loaded,
        // leaving prior-test toggle state in place.
        NavigateToFixtureFresh("DevtoolsUx_MenuAndToggle");
        WaitForText("DebugUiState", "debug-off");

        // Open the flyout.
        FindById("DevtoolsTrigger").Click();

        // Toggle the "Debug UI" item. Menu flyout items aren't in the root visual
        // tree until the flyout is open — find by Name since WinUI's
        // ToggleMenuFlyoutItem exposes its Text as the UIA Name.
        ClickButton("Debug UI");

        // Mirrored state updates, conditional overlay appears.
        WaitForText("DebugUiState", "debug-on");
        var overlay = WaitForElement("DebugOverlay");
        Assert.AreEqual("debug-overlay-visible", overlay.Text);
    }

    /// <summary>
    /// Second toggle round-trips the flag back to false. Confirms that the
    /// Observable notification path works in both directions, not just on first
    /// activation.
    /// </summary>
    [TestMethod]
    public void Devtools_Menu_Toggle_Is_Reversible()
    {
        // See Flows_Through_To_Subscribers for why Fresh is required.
        NavigateToFixtureFresh("DevtoolsUx_MenuAndToggle");
        WaitForText("DebugUiState", "debug-off");

        // Toggle on.
        FindById("DevtoolsTrigger").Click();
        ClickButton("Debug UI");
        WaitForText("DebugUiState", "debug-on");

        // Toggle off.
        FindById("DevtoolsTrigger").Click();
        ClickButton("Debug UI");
        WaitForText("DebugUiState", "debug-off");
    }
}
