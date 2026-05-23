using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium.Appium;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for declarative event handler modifiers.
/// These use Appium/WinAppDriver to simulate real user input against a running WinUI3 app,
/// verifying that .OnTapped(), .OnSizeChanged(), .OnPointerPressed(), and .OnKeyDown()
/// fire correctly and update state through the Reactor reconciliation pipeline.
/// </summary>
[TestClass]
public class EventHandlerTests : AppTestBase
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
    /// OnTapped: tap a Button element via WinAppDriver, verify the tap count increments
    /// through the full UI pipeline (event -> state update -> re-render -> UIA text change).
    /// </summary>
    [TestMethod]
    public void Interactive_OnTapped_Updates_State()
    {
        NavigateToFixture("EventHandler_Tapped");

        WaitForText("TapCount", "Tap count: 0");

        FindById("TapBtn").Click();
        WaitForText("TapCount", "Tap count: 1");

        FindById("TapBtn").Click();
        WaitForText("TapCount", "Tap count: 2");
    }

    /// <summary>
    /// OnSizeChanged: click a toggle button that changes a Border's width,
    /// verify the SizeChanged handler fires and reports the new dimensions.
    /// </summary>
    [TestMethod]
    public void Interactive_OnSizeChanged_Reports_Dimensions()
    {
        NavigateToFixture("EventHandler_SizeChanged");

        // Wait for initial size to be reported (format: "Size: 200xNNN")
        WaitForTextContaining("SizeDisplay", "200x");

        // Expand to 400
        ClickButton("SizeToggleBtn");
        WaitForTextContaining("SizeDisplay", "400x");

        // Shrink back to 200
        ClickButton("SizeToggleBtn");
        WaitForTextContaining("SizeDisplay", "200x");
    }

    /// <summary>
    /// OnPointerPressed: click a Button element, verify press is detected.
    /// Note: WinUI Button consumes PointerPressed internally for its press states,
    /// so this E2E test uses Button.OnClick as a proxy. The declarative OnPointerPressed
    /// API is verified by unit tests; this tests the Appium plumbing end-to-end.
    /// </summary>
    [TestMethod]
    public void Interactive_OnPointerPressed_Detects_Click()
    {
        NavigateToFixture("EventHandler_PointerPressed");

        WaitForText("PressCount", "Press count: 0");

        FindById("PointerBtn").Click();
        WaitForText("PressCount", "Press count: 1");
    }

    /// <summary>
    /// OnKeyDown: focus a TextBox and send a key, verify the key is captured
    /// by the declarative .OnKeyDown() handler.
    /// </summary>
    [TestMethod]
    public void Interactive_OnKeyDown_Captures_Keys()
    {
        NavigateToFixture("EventHandler_KeyDown");

        WaitForText("KeyDisplay", "Last key: none");

        // Click the text field to focus it, then type a key
        var target = FindById("KeyInput");
        target.Click();
        target.SendKeys("a");
        WaitForText("KeyDisplay", "Last key: A");
    }

    /// <summary>
    /// UseReducer (action-based): verify adding and clearing items through dispatched actions.
    /// </summary>
    [TestMethod]
    public void Interactive_UseReducer_Dispatches_Actions()
    {
        NavigateToFixture("EventHandler_UseReducer");

        // Initial state: 1 item
        WaitForText("TodoCount", "Count: 1");

        // Add items
        ClickButton("AddBtn");
        WaitForText("TodoCount", "Count: 2");
        ClickButton("AddBtn");
        WaitForText("TodoCount", "Count: 3");

        // Clear all
        ClickButton("ClearBtn");
        WaitForText("TodoCount", "Count: 0");
    }
}
