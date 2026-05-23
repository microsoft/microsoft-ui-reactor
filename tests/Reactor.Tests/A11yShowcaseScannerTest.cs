// ════════════════════════════════════════════════════════════════════════════
//  Scanner test — proves the A11y Showcase triggers the expected diagnostics.
//
//  This is NOT a test to "fix" — it asserts that the broken accessibility
//  in the A11y Showcase sample is correctly detected by the runtime
//  AccessibilityScanner. Run with:  dotnet test tests/Reactor.Tests --filter A11yShowcase
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.UI.Reactor.Core;
using Windows.UI.Text;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Constructs the same element tree as the A11y Showcase sample app
/// and verifies the AccessibilityScanner catches every intentional issue.
/// </summary>
public class A11yShowcaseScannerTest
{
    // Helper matching App.cs — icon buttons use Image elements as content
    static Element IconBtn(string icon, Action? onClick = null) =>
        Button(Image($"ms-appx:///Assets/{icon}.png").Size(16, 16), onClick);

    /// <summary>
    /// Build a representative element tree matching the A11y Showcase App.cs
    /// structure. We can't call App.Render() directly (it needs hooks), so
    /// we replicate the tree shape with the same a11y gaps.
    /// </summary>
    private static Element BuildShowcaseTree()
    {
        return VStack(0,
            // Header
            Border(
                HStack(12,
                    // A11Y_002: Image without alt text
                    Image("ms-appx:///Assets/logo.png").Size(28, 28),

                    // A11Y_004: Large bold text styled as heading, no HeadingLevel
                    // Use FontWeight directly (not .Bold()) to avoid WinUI COM activation in unit tests
                    new TextBlockElement("Task Tracker") { FontSize = 24, Weight = new FontWeight(700) },

                    HStack(4,
                        // A11Y_001: Icon-only buttons without AutomationName (x3)
                        IconBtn("settings"),
                        IconBtn("people"),
                        IconBtn("refresh")
                    )
                )
            ),

            // Toolbar
            // NOTE: A11Y_005 (concrete brush on interactive control) is demonstrated
            // in the real App.cs via .Background("#0078d4"), but .Background(string)
            // creates a SolidColorBrush which requires WinUI COM — can't test headless.
            HStack(12,
                Button("All", null),
                Button("Active", null),

                // A11Y_003: TextField without header, AutomationName, or LabeledBy
                TextBox("", null, placeholder: "New task..."),

                // A11Y_001: Another icon-only button
                IconBtn("add")
            ),

            // Task rows — icon buttons without names
            VStack(4,
                Border(
                    HStack(12,
                        CheckBox(false, null),
                        new TextBlockElement("Set up CI pipeline") { Weight = new FontWeight(600) },
                        HStack(4,
                            // A11Y_002: Decorative image not hidden
                            Image("ms-appx:///Assets/Important.png").Size(14, 14),
                            // A11Y_001: Icon-only edit/delete buttons
                            IconBtn("edit"),
                            IconBtn("delete")
                        )
                    )
                )
            ),

            // Footer
            HStack(12,
                TextBlock("Quick note:"),
                // A11Y_003: TextField with mistyped LabeledBy → A11Y_008
                TextBox("", null).LabeledBy("FooterNoteLabel_TYPO")
            )
        );
        // NOTE: No .Landmark(Main) on any element → A11Y_006
    }

    [Fact]
    public void A11yShowcase_Triggers_All_Expected_Diagnostics()
    {
        var tree = BuildShowcaseTree();
        var findings = AccessibilityScanner.Scan(tree);

        // A11Y_001: Icon-only buttons (settings, people, refresh, add, edit, delete = 6)
        var a001 = findings.Where(f => f.Id == "A11Y_001").ToList();
        Assert.True(a001.Count >= 6, $"Expected >= 6 icon-button findings, got {a001.Count}");

        // A11Y_002: Images without alt text (logo + priority badge icon = 2)
        var a002 = findings.Where(f => f.Id == "A11Y_002").ToList();
        Assert.True(a002.Count >= 2, $"Expected >= 2 image findings, got {a002.Count}");

        // A11Y_003: Form fields without labels (toolbar "New task" field = 1;
        // footer field has .LabeledBy() so it's not flagged here — caught by A11Y_008 instead)
        var a003 = findings.Where(f => f.Id == "A11Y_003").ToList();
        Assert.True(a003.Count >= 1, $"Expected >= 1 form-field finding, got {a003.Count}");

        // A11Y_004: Heading-styled text without HeadingLevel (1)
        var a004 = findings.Where(f => f.Id == "A11Y_004").ToList();
        Assert.True(a004.Count >= 1, $"Expected >= 1 heading finding, got {a004.Count}");

        // A11Y_005: Concrete brush — tested in the real app, not here (requires WinUI COM)

        // A11Y_006: No Main landmark
        var a006 = findings.Where(f => f.Id == "A11Y_006").ToList();
        Assert.Single(a006);

        // A11Y_008: Unresolved .LabeledBy() reference
        var a008 = findings.Where(f => f.Id == "A11Y_008").ToList();
        Assert.Single(a008);
        Assert.Contains("FooterNoteLabel_TYPO", a008[0].Message);
    }

    [Fact]
    public void A11yShowcase_Diagnostics_Include_Fix_Suggestions()
    {
        var tree = BuildShowcaseTree();
        var findings = AccessibilityScanner.Scan(tree);

        // Every finding should have a fix suggestion with a modifier name
        foreach (var finding in findings)
        {
            Assert.NotNull(finding.Fix);
            Assert.False(string.IsNullOrEmpty(finding.Fix.Modifier),
                $"{finding.Id}: Fix.Modifier should not be empty");
        }

        // Icon-button fixes should suggest AutomationName
        var iconBtnFix = findings.First(f => f.Id == "A11Y_001").Fix;
        Assert.Equal("AutomationName", iconBtnFix.Modifier);
        Assert.Contains(".AutomationName(", iconBtnFix.CodeSnippet);
    }

    [Fact]
    public void A11yShowcase_ExportJson_Contains_Structured_Report()
    {
        var tree = BuildShowcaseTree();
        var findings = AccessibilityScanner.Scan(tree);

        var tempPath = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(), $"a11y-showcase-{Guid.NewGuid()}.json");

        try
        {
            AccessibilityScanner.ExportJson(findings, tempPath);
            Assert.True(global::System.IO.File.Exists(tempPath));

            var json = global::System.IO.File.ReadAllText(tempPath);

            // Verify structured JSON fields
            Assert.Contains("\"diagnosticCount\"", json);
            Assert.Contains("\"wcagCriterion\"", json);
            Assert.Contains("\"fix\"", json);
            Assert.Contains("\"modifier\"", json);
            Assert.Contains("\"context\"", json);

            // Verify all expected diagnostic IDs appear
            Assert.Contains("A11Y_001", json);
            Assert.Contains("A11Y_002", json);
            Assert.Contains("A11Y_003", json);
            Assert.Contains("A11Y_006", json);
            Assert.Contains("A11Y_008", json);
        }
        finally
        {
            if (global::System.IO.File.Exists(tempPath)) global::System.IO.File.Delete(tempPath);
        }
    }
}
