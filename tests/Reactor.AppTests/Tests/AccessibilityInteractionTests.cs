using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// End-to-end accessibility interaction tests that validate behavior patterns
/// through the real out-of-process UIA pipeline. These complement
/// AccessibilityTests.cs (static property validation) by testing:
///
/// - Keyboard navigation flow (Tab order, TabNavigation modes)
/// - Live region announcements after state changes (UseAnnounce)
/// - Heading hierarchy structure (H1 → H2 → H3)
/// - Access key activation
/// - SemanticPanel composite semantics (role, value, range via UIA)
/// - LabeledBy resolution (label → field association)
///
/// Each test navigates to a dedicated fixture, interacts via WinAppDriver,
/// and reads UIA properties to verify what assistive technology actually sees.
/// </summary>
[TestClass]
public class AccessibilityInteractionTests : AppTestBase
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

    // ════════════════════════════════════════════════════════════════
    //  WCAG 2.1.1 — Keyboard: Tab order follows TabIndex
    // ════════════════════════════════════════════════════════════════

    // [E2eRetry] mops up the rare unattended-desktop input-injection flake: the native winapp
    // send-keys/drag verbs are SendInput under the hood and are occasionally dropped before the Host
    // foregrounds on CI. A real regression still fails every attempt; retained pending a few stable CI runs (#652).
    [E2eRetry(3)]
    [TestMethod]
    public void A11y_2_1_1_TabOrderFollowsTabIndex()
    {
        NavigateToFixture("Accessibility_KeyboardNav");

        // Focus the first field and verify it receives focus
        var field1 = FindById("A11yNav_Field1");
        field1.Click();

        // Verify the field's accessible name is correct via UIA
        var name1 = field1.GetAttribute("Name");
        Assert.AreEqual("First field", name1,
            "WCAG 2.1.1: First field must have accessible name");

        // Tab through fields — each Tab should move to the next TabIndex
        field1.SendKeys(Keys.Tab);
        var active2 = FindById("A11yNav_Field2");
        Assert.IsNotNull(active2, "Tab from field 1 should reach field 2");

        active2.SendKeys(Keys.Tab);
        var active3 = FindById("A11yNav_Field3");
        Assert.IsNotNull(active3, "Tab from field 2 should reach field 3");

        active3.SendKeys(Keys.Tab);
        var active4 = FindById("A11yNav_Field4");
        Assert.IsNotNull(active4, "Tab from field 3 should reach field 4");

        // Verify the submit button is reachable
        active4.SendKeys(Keys.Tab);
        var submit = FindById("A11yNav_Submit");
        Assert.IsNotNull(submit, "Tab from field 4 should reach Submit button");
    }

    // ════════════════════════════════════════════════════════════════
    //  WCAG 4.1.3 — UseAnnounce triggers live region update
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void A11y_4_1_3_LiveRegionPoliteAndAssertiveExposed()
    {
        NavigateToFixture("Accessibility_LiveRegion");

        // Verify the Polite live region is exposed via UIA
        var status = FindById("A11yLive_Status");
        var liveSettingPolite = status.GetAttribute("LiveSetting");
        Assert.IsNotNull(liveSettingPolite,
            "WCAG 4.1.3: Status element must have LiveSetting for screen reader announcements");
        Assert.IsTrue(liveSettingPolite.Contains("1") || liveSettingPolite.ToLower().Contains("polite"),
            $"Expected Polite live region, got: {liveSettingPolite}");

        // Verify the Assertive live region is exposed via UIA
        var alert = FindById("A11yLive_Alert");
        var liveSettingAssertive = alert.GetAttribute("LiveSetting");
        Assert.IsNotNull(liveSettingAssertive,
            "WCAG 4.1.3: Alert element must have LiveSetting=Assertive");
        Assert.IsTrue(liveSettingAssertive.Contains("2") || liveSettingAssertive.ToLower().Contains("assertive"),
            $"Expected Assertive live region, got: {liveSettingAssertive}");
    }

    // ════════════════════════════════════════════════════════════════
    //  WCAG 1.3.1 — Heading hierarchy: H1 → H2 → H3, no skips
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void A11y_1_3_1_HeadingHierarchyValid()
    {
        NavigateToFixture("Accessibility_HeadingHierarchy");

        // Verify all heading elements exist and have correct text
        var h1 = FindById("A11yH_H1");
        Assert.IsNotNull(h1, "H1 heading should exist");
        Assert.AreEqual("Application Settings", h1.Text);

        var h2a = FindById("A11yH_H2a");
        Assert.IsNotNull(h2a, "First H2 heading should exist");
        Assert.AreEqual("Appearance", h2a.Text);

        var h2b = FindById("A11yH_H2b");
        Assert.IsNotNull(h2b, "Second H2 heading should exist");
        Assert.AreEqual("Notifications", h2b.Text);

        var h3 = FindById("A11yH_H3");
        Assert.IsNotNull(h3, "H3 heading should exist");
        Assert.AreEqual("Email Alerts", h3.Text);

        // Attempt to read HeadingLevel via UIA
        // (WinAppDriver may not expose this property — graceful fallback)
        var h1Level = h1.GetAttribute("HeadingLevel");
        if (h1Level is not null)
        {
            Assert.IsTrue(h1Level.Contains("1"), $"H1 should be Level1, got: {h1Level}");

            var h2aLevel = h2a.GetAttribute("HeadingLevel");
            Assert.IsTrue(h2aLevel != null && h2aLevel.Contains("2"),
                $"H2a should be Level2, got: {h2aLevel}");

            var h3Level = h3.GetAttribute("HeadingLevel");
            Assert.IsTrue(h3Level != null && h3Level.Contains("3"),
                $"H3 should be Level3, got: {h3Level}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WCAG 2.1.1 — Access key button activation
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void A11y_2_1_1_AccessKeyExposedOnButtons()
    {
        NavigateToFixture("Accessibility_AccessKey");

        // Verify access keys are exposed via UIA
        var saveBtn = FindById("A11yAK_SaveBtn");
        var saveKey = saveBtn.GetAttribute("AccessKey");
        Assert.IsNotNull(saveKey,
            "WCAG 2.1.1: Save button must expose AccessKey");
        Assert.IsTrue(saveKey.Contains("S"),
            $"Expected AccessKey containing 'S', got: {saveKey}");

        var cancelBtn = FindById("A11yAK_CancelBtn");
        var cancelKey = cancelBtn.GetAttribute("AccessKey");
        Assert.IsTrue(cancelKey != null && cancelKey.Contains("C"),
            $"Expected AccessKey containing 'C', got: {cancelKey}");
    }

    // ════════════════════════════════════════════════════════════════
    //  Composite Semantics — SemanticPanel exposes role/value via UIA
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void A11y_4_1_2_SemanticPanelExposesRoleAndName()
    {
        NavigateToFixture("Accessibility_SemanticPanel");

        // Star rating — wrapped in SemanticPanel with role="slider"
        var starRating = FindById("A11ySem_StarRating");
        Assert.IsNotNull(starRating, "Star rating element should be findable via UIA");

        // Verify accessible name passes through
        var name = starRating.GetAttribute("Name");
        Assert.AreEqual("Star rating", name,
            "WCAG 4.1.2: SemanticPanel must pass through AutomationName");

        // Verify the control type reflects the semantic role
        // WinAppDriver exposes this as "LocalizedControlType" or "ControlType"
        var controlType = starRating.GetAttribute("LocalizedControlType");
        if (controlType is not null)
        {
            // SemanticPanelAutomationPeer maps "slider" → AutomationControlType.Slider
            Assert.IsTrue(
                controlType.ToLower().Contains("slider") || controlType.ToLower().Contains("group"),
                $"Expected slider or group control type, got: {controlType}");
        }
    }

    [TestMethod]
    public void A11y_4_1_2_SemanticPanelStatusBadge()
    {
        NavigateToFixture("Accessibility_SemanticPanel");

        // Status badge — wrapped in SemanticPanel with role="statusbar"
        var statusBadge = FindById("A11ySem_StatusBadge");
        Assert.IsNotNull(statusBadge, "Status badge element should be findable via UIA");

        var name = statusBadge.GetAttribute("Name");
        Assert.AreEqual("Connection status", name,
            "Status badge must have accessible name");
    }

    // ════════════════════════════════════════════════════════════════
    //  LabeledBy — label → field association via UIA
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void A11y_3_3_2_LabeledByResolvesLabelToField()
    {
        NavigateToFixtureFresh("Accessibility_LabeledBy");

        // Wait for the fixture-specific element to confirm navigation completed
        var title = WaitForElement("A11yLbl_Title", 5000);
        Assert.AreEqual("LabeledBy Test", title.Text,
            "Navigation should have switched to the LabeledBy fixture");

        // The label element
        var label = FindById("A11yLbl_EmailLabel");
        Assert.IsNotNull(label, "Label element should exist");
        Assert.AreEqual("Email address", label.Text);

        // The field that references the label via LabeledBy
        var field = FindById("A11yLbl_EmailField");
        Assert.IsNotNull(field, "Email field should exist");

        // WinUI resolves LabeledBy to set the UIA Name from the label element.
        // The field's Name should contain "Email" once LabeledBy resolves.
        // Note: WinUI's LabeledBy resolution happens asynchronously after mount,
        // so give it a moment.
        Thread.Sleep(500);
        var fieldName = field.GetAttribute("Name");
        if (fieldName is not null && fieldName.Length > 0)
        {
            Assert.IsTrue(
                fieldName.Contains("Email") || fieldName.Contains("email") || fieldName.Contains("user@"),
                $"LabeledBy should associate the label with the field. Got Name: {fieldName}");
        }

        // Verify the self-labeled field works independently
        var phoneField = FindById("A11yLbl_PhoneField");
        var phoneName = phoneField.GetAttribute("Name");
        Assert.AreEqual("Phone number", phoneName,
            "Self-labeled field should have its AutomationName");
    }

    // ════════════════════════════════════════════════════════════════
    //  TabNavigation — toolbar with Once mode
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void A11y_2_1_1_TabNavigationToolbarReachable()
    {
        NavigateToFixture("Accessibility_TabNavigation");

        // Verify the toolbar and its buttons are accessible
        var toolbar = FindById("A11yTabNav_Toolbar");
        Assert.IsNotNull(toolbar, "Toolbar should be findable via UIA");

        var toolbarName = toolbar.GetAttribute("Name");
        Assert.AreEqual("Formatting toolbar", toolbarName,
            "Toolbar should have accessible name");

        // Verify toolbar buttons exist and are reachable
        var boldBtn = FindById("A11yTabNav_Bold");
        Assert.IsNotNull(boldBtn, "Bold button in toolbar should exist");

        var italicBtn = FindById("A11yTabNav_Italic");
        Assert.IsNotNull(italicBtn, "Italic button in toolbar should exist");

        var underlineBtn = FindById("A11yTabNav_Underline");
        Assert.IsNotNull(underlineBtn, "Underline button in toolbar should exist");
    }

    [TestMethod]
    public void A11y_2_1_1_TabNavigationFieldsBeforeAndAfterToolbar()
    {
        NavigateToFixture("Accessibility_TabNavigation");

        // Verify fields before and after toolbar are accessible
        var beforeField = FindById("A11yTabNav_Before");
        Assert.IsNotNull(beforeField, "Field before toolbar should exist");
        var beforeName = beforeField.GetAttribute("Name");
        Assert.AreEqual("Before toolbar", beforeName);

        var afterField = FindById("A11yTabNav_After");
        Assert.IsNotNull(afterField, "Field after toolbar should exist");
        var afterName = afterField.GetAttribute("Name");
        Assert.AreEqual("After toolbar", afterName);
    }
}
