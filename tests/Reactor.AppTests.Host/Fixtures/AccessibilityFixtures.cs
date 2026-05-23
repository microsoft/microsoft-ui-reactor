using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// Test fixtures that mount controls with accessibility modifiers.
/// Each control has an AutomationId so the out-of-process UIA tests
/// (AccessibilityTests.cs) can find them and read their UIA properties
/// through the real accessibility pipeline (Narrator/NVDA/WinAppDriver).
/// </summary>
internal static class AccessibilityFixtures
{
    /// <summary>
    /// Comprehensive accessibility fixture exercising every Reactor a11y modifier.
    /// Maps to WCAG 2.1 success criteria. UIA properties are validated from
    /// the out-of-process Appium tests via WinAppDriver's GetAttribute() API.
    /// </summary>
    internal static Element AccessibilityShowcase(RenderContext ctx)
    {
        return VStack(8,
            // ── WCAG 1.1.1: Non-text Content ──────────────────
            // Icon-only button needs accessible name
            Button("🔍")
                .AutomationName("Search documents")
                .AutomationId("A11y_SearchBtn"),

            // Decorative image hidden from screen readers
            Image("ms-appx:///Assets/StoreLogo.png")
                .AccessibilityHidden()
                .AutomationId("A11y_DecorativeImg")
                .Width(16).Height(16),

            // ── WCAG 1.3.1: Info and Relationships ────────────
            // Heading levels for document structure
            TextBlock("Account Settings")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11y_H1"),

            TextBlock("Personal Information")
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .AutomationId("A11y_H2"),

            // Navigation landmark
            HStack(
                Button("Home").AutomationId("A11y_NavHome"),
                Button("Profile").AutomationId("A11y_NavProfile")
            ).Landmark(AutomationLandmarkType.Navigation)
             .AutomationId("A11y_NavBar"),

            // Main content landmark
            VStack(
                // ── WCAG 3.3.2: Labels and Instructions ───────
                // Form field with full accessibility annotations
                TextBox("user@example.com")
                    .AutomationName("Email address")
                    .Required()
                    .HelpText("Enter your primary contact email")
                    .FullDescription("This email will be used for account recovery and notifications. Must be a valid email format.")
                    .AutomationId("A11y_EmailField"),

                // ── WCAG 4.1.2: Name, Role, Value ────────────
                // Control with item status
                CheckBox(true, label: "Notifications")
                    .AutomationName("Enable notifications")
                    .ItemStatus("Currently enabled")
                    .AutomationId("A11y_NotifCB"),

                // Control with position in set
                TextBlock("Step 2 of 5")
                    .PositionInSet(2, 5)
                    .AutomationId("A11y_StepIndicator"),

                // Hierarchy level
                TextBlock("Category")
                    .HierarchyLevel(1)
                    .AutomationId("A11y_Level1"),

                TextBlock("Sub-category")
                    .HierarchyLevel(2)
                    .AutomationId("A11y_Level2")

            ).Landmark(AutomationLandmarkType.Main)
             .AutomationId("A11y_MainContent"),

            // ── WCAG 2.1.1: Keyboard ─────────────────────────
            Button("File")
                .AccessKey("F")
                .TabIndex(1)
                .AutomationId("A11y_FileBtn"),

            Button("Edit")
                .AccessKey("E")
                .TabIndex(2)
                .AutomationId("A11y_EditBtn"),

            // Toolbar with contained tab navigation
            HStack(
                Button("Bold").AutomationId("A11y_BoldBtn"),
                Button("Italic").AutomationId("A11y_ItalicBtn")
            ).TabNavigation(Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once)
             .AutomationId("A11y_Toolbar"),

            // ── WCAG 4.1.3: Status Messages ──────────────────
            // Live regions for dynamic announcements
            TextBlock("Status: Ready")
                .LiveRegion(AutomationLiveSetting.Polite)
                .AutomationId("A11y_StatusPolite"),

            TextBlock("Alert: None")
                .LiveRegion(AutomationLiveSetting.Assertive)
                .AutomationId("A11y_AlertAssertive"),

            // ── AccessibilityView variants ────────────────────
            TextBlock("Visible to AT")
                .AccessibilityView(AccessibilityView.Content)
                .AutomationId("A11y_ViewContent"),

            TextBlock("Hidden from AT")
                .AccessibilityView(AccessibilityView.Raw)
                .AutomationId("A11y_ViewRaw")
        );
    }

    /// <summary>
    /// Chart accessibility fixture for E2E UIA validation via Appium/WinAppDriver.
    /// Mounts several chart types with accessibility configured so cross-process
    /// tests can validate the full UIA tree.
    /// </summary>
    internal static Element ChartAccessibilityShowcase(RenderContext ctx)
    {
        // Line chart — 2 series, 5 points each
        var lineData1 = new[] { (0.0, 10.0), (1, 25), (2, 18), (3, 35), (4, 42) }
            .Select(p => new { X = p.Item1, Y = p.Item2 }).ToArray();
        var lineData2 = new[] { (0.0, 15.0), (1, 12), (2, 28), (3, 22), (4, 38) }
            .Select(p => new { X = p.Item1, Y = p.Item2 }).ToArray();

        // Bar chart — single series, 4 points
        var barData = new[] { (0.0, 30.0), (1, 70), (2, 45), (3, 90) }
            .Select(p => new { X = p.Item1, Y = p.Item2 }).ToArray();

        // Pie chart — 4 slices
        var pieData = new[] { ("Chrome", 60.0), ("Safari", 20), ("Firefox", 12), ("Edge", 8) }
            .Select(p => new { Name = p.Item1, Value = p.Item2 }).ToArray();

        return VStack(12,
            TextBlock("Chart Accessibility Showcase")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("ChartA11y_E2E_Heading"),

            // Line chart with title, series names, units
            Charting.Charts.LineChart(lineData1, d => d.X, d => d.Y)
                .Title("Monthly Revenue")
                .SeriesName("Region A")
                .Units("months", "USD")
                .Width(400).Height(250)
                .ToElement()
                .AutomationId("ChartA11y_E2E_LineChart"),

            // Bar chart with title and default labels
            Charting.Charts.BarChart(barData, d => d.X, d => d.Y)
                .Title("Quarterly Sales")
                .SeriesName("Product A")
                .Width(400).Height(250)
                .ToElement()
                .AutomationId("ChartA11y_E2E_BarChart"),

            // Pie chart with title and slice labels
            Charting.Charts.PieChart(pieData, d => d.Value, d => d.Name)
                .Title("Browser Market Share")
                .Width(300).Height(250)
                .ToElement()
                .AutomationId("ChartA11y_E2E_PieChart"),

            // Interactive chart with keyboard nav enabled
            Charting.Charts.LineChart(lineData1, d => d.X, d => d.Y)
                .Title("Interactive Revenue Chart")
                .SeriesName("Revenue")
                .Interactive()
                .Width(400).Height(250)
                .ToElement()
                .AutomationId("ChartA11y_E2E_InteractiveChart")
        );
    }
}
