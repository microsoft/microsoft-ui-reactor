using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// End-to-end tests for WinForms ↔ XAML Island interop.
///
/// These tests launch a real WinForms app with a XAML Island (Reactor.WinFormsTests.Host)
/// and drive it through Appium/WinAppDriver to validate:
///   - Tab navigation across the WinForms ↔ WinUI boundary
///   - Rendering of Reactor/WinUI controls inside the island
///   - Accessibility properties exposed through the UIA pipeline
///
/// Test host layout:
///   Top bar (WinForms):     WF_TextBox3 (TabIndex=4, after island)
///   Left panel (WinForms):  WF_TextBox1, WF_Button1, WF_TextBox2
///   Right panel (Island):   Reactor_TextBox1, Reactor_Button1, Reactor_TextBox2
/// </summary>
[TestClass]
public class WinFormsInteropTests : WinFormsTestBase
{
    [ClassInitialize]
    public static void StartSession(TestContext context)
    {
        WinFormsTestSession.Init(context);
    }

    [ClassCleanup]
    public static void StopSession()
    {
        WinFormsTestSession.Cleanup();
    }

    // ════════════════════════════════════════════════════════════════════
    //  RENDERING — WinUI controls render correctly inside the island
    // ════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Interop_Rendering_IslandContentVisible()
    {
        // The Reactor component's title text should be visible through UIA
        var title = WaitForElement("Reactor_Title", timeoutMs: 10000);
        Assert.IsNotNull(title, "Reactor title element should be visible in UIA tree");
        Assert.AreEqual("Reactor Island Content", title.Text,
            "Title text should render correctly inside the XAML Island");
    }

    [TestMethod]
    public void Interop_Rendering_RenderProofVisible()
    {
        // Static text that proves the Reactor component mounted and rendered
        var proof = WaitForElement("Reactor_RenderProof", timeoutMs: 10000);
        Assert.IsNotNull(proof, "Render proof element should be visible");
        Assert.AreEqual("Island rendered successfully", proof.Text);
    }

    [TestMethod]
    public void Interop_Rendering_CounterInitialState()
    {
        var countText = WaitForElement("Reactor_CountDisplay");
        Assert.AreEqual("Count: 0", countText.Text,
            "Counter should initialize to 0");
    }

    [TestMethod]
    public void Interop_Rendering_ButtonClickUpdatesState()
    {
        // Click the Reactor button inside the island
        var button = FindById("Reactor_Button1");
        button.Click();

        // Counter should increment
        WaitForText("Reactor_CountDisplay", "Count: 1");

        // Click again
        button.Click();
        WaitForText("Reactor_CountDisplay", "Count: 2");
    }

    [TestMethod]
    public void Interop_Rendering_TextInputWorks()
    {
        // Type into the Reactor TextBox inside the island
        var textBox = FindById("Reactor_TextBox1");
        textBox.Click();
        textBox.Clear();
        textBox.SendKeys("hello island");

        // Verify the display updates
        WaitForText("Reactor_TextDisplay", "Text: hello island");
    }

    [TestMethod]
    public void Interop_Rendering_WinFormsControlsVisible()
    {
        // Verify WinForms controls are also visible through UIA
        // UIA Name comes from AccessibleName when set (lowercase)
        var wfButton = FindByName("WinForms button");
        Assert.IsNotNull(wfButton, "WinForms button should be visible in UIA tree");
    }

    // ════════════════════════════════════════════════════════════════════
    //  TAB NAVIGATION — Full cycle validates exact order in both directions
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The expected forward Tab order through the entire form.
    /// This is the single source of truth — both cycle tests validate against it.
    ///
    /// WinForms controls use Control.Name as AutomationId.
    /// Reactor controls use the .AutomationId() modifier.
    /// </summary>
    private static readonly string[] ExpectedTabOrder =
    [
        "WF_TextBox1",       // leftPanel child 0
        "WF_Button1",        // leftPanel child 1
        "WF_TextBox2",       // leftPanel child 2
        "Reactor_TextBox1",     // island - first WinUI control
        "Reactor_Button1",      // island — second WinUI control
        "Reactor_TextBox2",     // island - third WinUI control
        "WF_TextBox3",       // bottomBar child 0
    ];

    [TestMethod]
    public void Interop_Tab_ForwardCycle_TwoFullLoops()
    {
        // Start at the first control
        FindById("WF_TextBox1").Click();
        AssertFocused("WF_TextBox1", "Start");

        // Tab forward through every control, twice
        for (int loop = 0; loop < 2; loop++)
        {
            for (int i = 0; i < ExpectedTabOrder.Length; i++)
            {
                var current = ExpectedTabOrder[i];
                var next = ExpectedTabOrder[(i + 1) % ExpectedTabOrder.Length];

                SendTab();
                AssertFocused(next, $"Loop {loop + 1}, Tab from {current} → expected {next}");
            }
        }
    }

    [TestMethod]
    public void Interop_Tab_BackwardCycle_TwoFullLoops()
    {
        // Start at the last control before wrapping
        FindById("WF_TextBox3").Click();
        AssertFocused("WF_TextBox3", "Start");

        // Shift+Tab backward through every control, twice
        for (int loop = 0; loop < 2; loop++)
        {
            for (int i = ExpectedTabOrder.Length - 1; i >= 0; i--)
            {
                var current = ExpectedTabOrder[i];
                var prev = ExpectedTabOrder[(i - 1 + ExpectedTabOrder.Length) % ExpectedTabOrder.Length];

                SendShiftTab();
                AssertFocused(prev,
                    $"Loop {loop + 1}, Shift+Tab from {current} → expected {prev}");
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ACCESSIBILITY — UIA properties exposed through the island
    // ════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Interop_A11y_IslandControlsHaveAutomationIds()
    {
        // Verify Reactor controls inside the island are discoverable by AutomationId
        var title = FindById("Reactor_Title");
        Assert.IsNotNull(title, "Reactor_Title should be findable by AutomationId through UIA");

        var textBox = FindById("Reactor_TextBox1");
        Assert.IsNotNull(textBox, "Reactor_TextBox1 should be findable by AutomationId");

        var button = FindById("Reactor_Button1");
        Assert.IsNotNull(button, "Reactor_Button1 should be findable by AutomationId");
    }

    [TestMethod]
    public void Interop_A11y_IslandControlsHaveAccessibleNames()
    {
        // Verify accessible names are exposed through UIA
        var textBox = FindById("Reactor_TextBox1");
        var name = textBox.GetAttribute("Name");
        Assert.AreEqual("Island TextBox", name,
            "Reactor TextBox should expose its AutomationName through UIA");

        var button = FindById("Reactor_Button1");
        var buttonName = button.GetAttribute("Name");
        Assert.AreEqual("Island button", buttonName,
            "Reactor Button should expose its AutomationName through UIA");
    }

    [TestMethod]
    public void Interop_A11y_LiveRegionExposed()
    {
        // Verify live region annotation is exposed through the island
        var liveRegion = FindById("Reactor_LiveRegion");
        var liveSetting = liveRegion.GetAttribute("LiveSetting");
        Assert.IsNotNull(liveSetting,
            "LiveSetting should be exposed for screen reader status messages through the island");
        // LiveSetting: Off=0, Polite=1, Assertive=2
        Assert.IsTrue(liveSetting.Contains("1") || liveSetting.ToLower().Contains("polite"),
            $"Expected Polite live region, got: {liveSetting}");
    }

    [TestMethod]
    public void Interop_A11y_WinFormsAndIslandControlsBothVisible()
    {
        // Both WinForms and WinUI controls should appear in the same UIA tree
        var wfButton = FindByName("WinForms button");
        Assert.IsNotNull(wfButton, "WinForms button should be in UIA tree");

        var reactorButton = FindById("Reactor_Button1");
        Assert.IsNotNull(reactorButton, "Reactor button should be in UIA tree");

        // Both should have a ControlType — verifies they're real UIA elements
        var wfType = wfButton.GetAttribute("LocalizedControlType");
        Assert.IsNotNull(wfType, "WinForms button should have a LocalizedControlType");

        var reactorType = reactorButton.GetAttribute("LocalizedControlType");
        Assert.IsNotNull(reactorType, "Reactor button should have a LocalizedControlType");
    }

    [TestMethod]
    public void Interop_A11y_TextBoxIsEditable()
    {
        // Verify the TextBox inside the island is recognized as editable
        var textBox = FindById("Reactor_TextBox1");
        var controlType = textBox.GetAttribute("LocalizedControlType");

        // WinUI TextBox reports as "edit" in UIA
        Assert.IsTrue(
            controlType != null && controlType.Contains("edit", StringComparison.OrdinalIgnoreCase),
            $"Island TextBox should be an editable control type, got: {controlType}");
    }
}
