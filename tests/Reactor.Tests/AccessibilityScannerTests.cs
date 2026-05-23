using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.UI.Text;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Unit tests for the AccessibilityScanner. Creates element trees programmatically
/// and verifies the scanner produces the correct diagnostics without running a
/// WinUI application or requiring the UI thread.
/// </summary>
public class AccessibilityScannerTests
{
    // ════════════════════════════════════════════════════════════════
    //  A11Y_001: Icon-only Button without accessible name
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A11Y_001_IconButton_Without_AutomationName()
    {
        var tree = VStack(
            Button(TextBlock("🔍"), null) // icon content, no AutomationName
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_001");
    }

    [Fact]
    public void A11Y_001_IconButton_With_AutomationName_Passes()
    {
        var tree = VStack(
            Button(TextBlock("🔍"), null).AutomationName("Search")
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_001");
    }

    [Fact]
    public void A11Y_001_TextButton_Not_Flagged()
    {
        var tree = VStack(
            Button("Search", null)
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_001");
    }

    // ════════════════════════════════════════════════════════════════
    //  A11Y_002: Image without alt text
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A11Y_002_Image_Without_AutomationName()
    {
        var tree = VStack(
            Image("ms-appx:///Assets/photo.png")
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_002");
    }

    [Fact]
    public void A11Y_002_Image_With_AutomationName_Passes()
    {
        var tree = VStack(
            Image("ms-appx:///Assets/photo.png").AutomationName("Team photo")
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_002");
    }

    [Fact]
    public void A11Y_002_Image_AccessibilityHidden_Passes()
    {
        var tree = VStack(
            Image("ms-appx:///Assets/divider.png").AccessibilityHidden()
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_002");
    }

    // ════════════════════════════════════════════════════════════════
    //  A11Y_003: Form field without label
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A11Y_003_TextField_Without_Label()
    {
        var tree = VStack(
            TextBox("", null, placeholder: "Enter email")
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_003");
    }

    [Fact]
    public void A11Y_003_TextField_With_Header_Passes()
    {
        var tree = VStack(
            TextBox("", null, header: "Email address")
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_003");
    }

    [Fact]
    public void A11Y_003_TextField_With_AutomationName_Passes()
    {
        var tree = VStack(
            TextBox("", null).AutomationName("Email address")
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_003");
    }

    [Fact]
    public void A11Y_003_TextField_With_LabeledBy_Passes()
    {
        var tree = VStack(
            TextBlock("Email").AutomationId("EmailLabel"),
            TextBox("", null).LabeledBy("EmailLabel")
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_003");
    }

    // ════════════════════════════════════════════════════════════════
    //  A11Y_004: Heading-styled text without HeadingLevel
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A11Y_004_LargeBoldText_Without_HeadingLevel()
    {
        // Use FontWeight directly (not .Bold()) to avoid WinUI COM activation in unit tests
        var tree = VStack(
            new TextBlockElement("Settings") { FontSize = 24, Weight = new global::Windows.UI.Text.FontWeight(700) }
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_004");
    }

    [Fact]
    public void A11Y_004_LargeBoldText_With_HeadingLevel_Passes()
    {
        var tree = VStack(
            (new TextBlockElement("Settings") { FontSize = 24, Weight = new global::Windows.UI.Text.FontWeight(700) })
                .HeadingLevel(AutomationHeadingLevel.Level1)
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_004");
    }

    [Fact]
    public void A11Y_004_SmallText_Not_Flagged()
    {
        var tree = VStack(
            TextBlock("Normal text").FontSize(14)
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_004");
    }

    // ════════════════════════════════════════════════════════════════
    //  A11Y_006: No Main landmark
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A11Y_006_No_Main_Landmark()
    {
        var tree = VStack(
            TextBlock("Hello"),
            Button("Go", null)
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_006");
    }

    [Fact]
    public void A11Y_006_Has_Main_Landmark_Passes()
    {
        var tree = VStack(
            TextBlock("Hello"),
            Button("Go", null)
        ).Landmark(AutomationLandmarkType.Main);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_006");
    }

    // ════════════════════════════════════════════════════════════════
    //  A11Y_008: Unresolved LabeledBy
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A11Y_008_LabeledBy_Missing_AutomationId()
    {
        var tree = VStack(
            TextBox("", null).LabeledBy("NonExistentLabel")
                .AutomationName("Email") // prevent A11Y_003
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_008");
    }

    [Fact]
    public void A11Y_008_LabeledBy_Valid_Reference_Passes()
    {
        var tree = VStack(
            TextBlock("Email").AutomationId("EmailLabel"),
            TextBox("", null).LabeledBy("EmailLabel")
                .AutomationName("Email") // prevent A11Y_003
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_008");
    }

    // ════════════════════════════════════════════════════════════════
    //  Clean tree: zero findings
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Clean_Tree_Zero_Findings()
    {
        var tree = VStack(
            Heading("Settings"),
            TextBox("", null, header: "Name"),
            Button("Save", null)
        ).Landmark(AutomationLandmarkType.Main);

        var findings = AccessibilityScanner.Scan(tree);
        Assert.Empty(findings);
    }

    // ════════════════════════════════════════════════════════════════
    //  JSON export
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ExportJson_Produces_Valid_File()
    {
        var tree = VStack(
            Image("test.png"), // triggers A11Y_002
            TextBox("", null) // triggers A11Y_003
        );

        var findings = AccessibilityScanner.Scan(tree);
        Assert.True(findings.Count >= 2);

        var tempPath = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), $"a11y-test-{Guid.NewGuid()}.json");
        try
        {
            AccessibilityScanner.ExportJson(findings, tempPath);
            Assert.True(global::System.IO.File.Exists(tempPath));

            var json = global::System.IO.File.ReadAllText(tempPath);
            Assert.Contains("\"diagnosticCount\"", json);
            Assert.Contains("A11Y_002", json);
            Assert.Contains("A11Y_003", json);
        }
        finally
        {
            if (global::System.IO.File.Exists(tempPath)) global::System.IO.File.Delete(tempPath);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Context enrichment
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Scanner_Provides_Child_Context_For_IconButton()
    {
        var tree = VStack(
            Button("Reply", null),
            Button("Forward", null),
            Button(TextBlock("🗑"), null) // triggers A11Y_001
        );

        var findings = AccessibilityScanner.Scan(tree);
        var iconBtnFinding = findings.FirstOrDefault(f => f.Id == "A11Y_001");
        Assert.NotNull(iconBtnFinding);
        // The scanner should report the child content type
        Assert.NotNull(iconBtnFinding!.Context.ChildTypes);
        Assert.Contains("TextBlockElement", iconBtnFinding.Context.ChildTypes!);
    }

    // ════════════════════════════════════════════════════════════════
    //  Heading() DSL default
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Heading_Sets_HeadingLevel_By_Default()
    {
        var el = Heading("Test");
        Assert.NotNull(el.Modifiers);
        Assert.Equal(AutomationHeadingLevel.Level1, el.Modifiers!.HeadingLevel);
    }

    [Fact]
    public void SubHeading_Sets_HeadingLevel_By_Default()
    {
        var el = SubHeading("Test");
        Assert.NotNull(el.Modifiers);
        Assert.Equal(AutomationHeadingLevel.Level2, el.Modifiers!.HeadingLevel);
    }
}
