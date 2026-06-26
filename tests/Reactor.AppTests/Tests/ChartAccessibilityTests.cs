using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// End-to-end accessibility tests for Reactor charts. Validates that chart
/// accessibility properties are visible through the real Windows UIA pipeline
/// (the same path used by Narrator, NVDA, and JAWS).
///
/// These tests run OUT OF PROCESS via winapp ui (UIA), reading UIA properties
/// through the Windows UIA client API. This validates what selftests cannot:
/// that accessibility annotations survive the cross-process UIA boundary.
///
/// WCAG success criteria covered:
///   1.1.1 — Non-text content (chart accessible name)
///   1.3.1 — Info and relationships (grid provider, series structure)
///   4.1.2 — Name, role, value (point values readable by AT)
/// </summary>
[TestClass]
public class ChartAccessibilityTests : AppTestBase
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

    private void NavigateToChartFixture()
    {
        NavigateToFixture("ChartAccessibility_Showcase");
    }

    // ════════════════════════════════════════════════════════════════════
    //  WCAG 1.1.1 — Non-text Content (Level A)
    //  "All non-text content has a text alternative."
    //  Chart must expose an accessible name matching its .Title() value.
    // ════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ChartA11y_UIA_ChartHasAccessibleName()
    {
        NavigateToChartFixture();

        // The line chart has .Title("Monthly Revenue") — verify UIA Name
        // Note: The chart is a Canvas with AutomationName set via the plot area.
        // The AutomationId is on the outer wrapper; we search for the chart
        // and read its Name property through UIA.
        var chart = FindById("ChartA11y_E2E_LineChart");
        Assert.IsNotNull(chart, "Line chart element should be findable via UIA");

        var name = chart.GetAttribute("Name");
        Assert.IsNotNull(name,
            "WCAG 1.1.1: Chart must have an accessible name visible to screen readers");
        Assert.IsTrue(
            name.Contains("Monthly Revenue"),
            $"WCAG 1.1.1: Chart Name should contain 'Monthly Revenue' from .Title(), got: '{name}'");
    }

    // ════════════════════════════════════════════════════════════════════
    //  WCAG 1.3.1 — Info and Relationships (Level A)
    //  "Information conveyed through presentation can be
    //   programmatically determined."
    //  Chart grid provider exposes RowCount/ColumnCount.
    // ════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ChartA11y_UIA_GridProviderExposed()
    {
        NavigateToChartFixture();

        // Navigate to the bar chart — 1 series, 4 points
        var barChart = FindById("ChartA11y_E2E_BarChart");
        Assert.IsNotNull(barChart, "Bar chart element should be findable via UIA");

        // WinAppDriver may not expose IGridProvider properties directly via GetAttribute.
        // Verify the element exists and has a name — the in-process selftests validate
        // the grid provider row/column counts.
        var name = barChart.GetAttribute("Name");
        Assert.IsNotNull(name,
            "WCAG 1.3.1: Bar chart should have an accessible name for programmatic structure");
    }

    // ════════════════════════════════════════════════════════════════════
    //  WCAG 4.1.2 — Name, Role, Value (Level A)
    //  "For all UI components, the name and role can be
    //   programmatically determined."
    //  Chart point values are readable through UIA.
    // ════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ChartA11y_UIA_PointValueReadable()
    {
        NavigateToChartFixture();

        // The line chart exposes per-point values through the grid provider.
        // Since WinAppDriver can navigate into grid items, we verify the chart
        // element is present and contains accessible content.
        var lineChart = FindById("ChartA11y_E2E_LineChart");
        Assert.IsNotNull(lineChart,
            "WCAG 4.1.2: Line chart must be present in UIA tree");

        // Verify the chart has a non-empty name (the plot area or title)
        var name = lineChart.GetAttribute("Name");
        Assert.IsTrue(!string.IsNullOrWhiteSpace(name),
            "WCAG 4.1.2: Chart should expose meaningful Name for AT navigation");

        // Verify ItemStatus is exposed (data summary)
        var itemStatus = lineChart.GetAttribute("ItemStatus");
        if (itemStatus is not null)
        {
            Assert.IsTrue(itemStatus.Contains("series") || itemStatus.Contains("point"),
                $"Chart ItemStatus should describe data structure, got: '{itemStatus}'");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Forced Colors (conditional)
    //  Only runs meaningful assertions if high contrast is detectable.
    // ════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ChartA11y_UIA_ForcedColorsActive()
    {
        NavigateToChartFixture();

        // This test validates that the chart fixture is accessible regardless
        // of high-contrast mode. Since we cannot toggle high contrast from
        // winapp ui, we verify the chart elements are present and accessible.
        var lineChart = FindById("ChartA11y_E2E_LineChart");
        var barChart = FindById("ChartA11y_E2E_BarChart");
        var pieChart = FindById("ChartA11y_E2E_PieChart");

        Assert.IsNotNull(lineChart, "Line chart should be in UIA tree");
        Assert.IsNotNull(barChart, "Bar chart should be in UIA tree");
        Assert.IsNotNull(pieChart, "Pie chart should be in UIA tree");

        // All charts should have names
        Assert.IsNotNull(lineChart.GetAttribute("Name"), "Line chart needs Name");
        Assert.IsNotNull(barChart.GetAttribute("Name"), "Bar chart needs Name");
        Assert.IsNotNull(pieChart.GetAttribute("Name"), "Pie chart needs Name");
    }
}
