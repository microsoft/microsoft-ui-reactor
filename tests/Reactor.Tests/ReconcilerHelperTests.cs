using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Reconciler's pure helper functions: caption extraction, caption resolution,
/// grid/row definition parsing, and symbol parsing. These are the building blocks for
/// accessibility, layout, and icon rendering.
/// </summary>
public class ReconcilerHelperTests
{
    // ════════════════════════════════════════════════════════════════
    //  ExtractElementCaption — pull visible text from element records
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractElementCaption_TextBlock_Returns_Content()
    {
        var element = new TextBlockElement("Hello World");
        Assert.Equal("Hello World", AccessibilityApplier.ExtractElementCaption(element));
    }

    [Fact]
    public void ExtractElementCaption_Null_Returns_Null()
    {
        Assert.Null(AccessibilityApplier.ExtractElementCaption(null));
    }

    [Fact]
    public void ExtractElementCaption_NonTextBlock_Returns_Null()
    {
        var element = new BorderElement(null!);
        Assert.Null(AccessibilityApplier.ExtractElementCaption(element));
    }

    // ════════════════════════════════════════════════════════════════
    //  ResolveCaptionForElement — control-specific caption resolution
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCaptionForElement_Button_With_Label()
    {
        var button = new ButtonElement("Click Me");
        Assert.Equal("Click Me", AccessibilityApplier.ResolveCaptionForElement(button));
    }

    [Fact]
    public void ResolveCaptionForElement_Button_Without_Label_Falls_Through_To_ContentElement()
    {
        var button = new ButtonElement(null!) { ContentElement = new TextBlockElement("Content Text") };
        Assert.Equal("Content Text", AccessibilityApplier.ResolveCaptionForElement(button));
    }

    [Fact]
    public void ResolveCaptionForElement_Button_Without_Label_Or_Content_Returns_Null()
    {
        var button = new ButtonElement(null!) { ContentElement = null };
        Assert.Null(AccessibilityApplier.ResolveCaptionForElement(button));
    }

    [Fact]
    public void ResolveCaptionForElement_CheckBox_Returns_Label()
    {
        var cb = new CheckBoxElement(false, Label: "Accept terms");
        Assert.Equal("Accept terms", AccessibilityApplier.ResolveCaptionForElement(cb));
    }

    [Fact]
    public void ResolveCaptionForElement_RadioButton_Returns_Label()
    {
        var rb = new RadioButtonElement("Option A");
        Assert.Equal("Option A", AccessibilityApplier.ResolveCaptionForElement(rb));
    }

    [Fact]
    public void ResolveCaptionForElement_HyperlinkButton_Returns_Content()
    {
        var hl = new HyperlinkButtonElement("Learn more");
        Assert.Equal("Learn more", AccessibilityApplier.ResolveCaptionForElement(hl));
    }

    [Fact]
    public void ResolveCaptionForElement_RepeatButton_Returns_Label()
    {
        var rb = new RepeatButtonElement("Repeat");
        Assert.Equal("Repeat", AccessibilityApplier.ResolveCaptionForElement(rb));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleButton_Returns_Label()
    {
        var tb = new ToggleButtonElement("Toggle");
        Assert.Equal("Toggle", AccessibilityApplier.ResolveCaptionForElement(tb));
    }

    [Fact]
    public void ResolveCaptionForElement_DropDownButton_Returns_Label()
    {
        var ddb = new DropDownButtonElement("Choose");
        Assert.Equal("Choose", AccessibilityApplier.ResolveCaptionForElement(ddb));
    }

    [Fact]
    public void ResolveCaptionForElement_SplitButton_Returns_Label()
    {
        var sb = new SplitButtonElement("Split");
        Assert.Equal("Split", AccessibilityApplier.ResolveCaptionForElement(sb));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSplitButton_Returns_Label()
    {
        var tsb = new ToggleSplitButtonElement("ToggleSplit");
        Assert.Equal("ToggleSplit", AccessibilityApplier.ResolveCaptionForElement(tsb));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSwitch_Prefers_Header()
    {
        var ts = new ToggleSwitchElement(false, OnContent: "On", OffContent: "Off") { Header = "Wifi" };
        Assert.Equal("Wifi", AccessibilityApplier.ResolveCaptionForElement(ts));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSwitch_Falls_Back_To_OnContent()
    {
        var ts = new ToggleSwitchElement(false, OnContent: "Enabled", OffContent: "Disabled") { Header = null };
        Assert.Equal("Enabled", AccessibilityApplier.ResolveCaptionForElement(ts));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSwitch_Falls_Back_To_OffContent()
    {
        var ts = new ToggleSwitchElement(false, OnContent: null, OffContent: "Disabled") { Header = null };
        Assert.Equal("Disabled", AccessibilityApplier.ResolveCaptionForElement(ts));
    }

    [Fact]
    public void ResolveCaptionForElement_TextBox_Prefers_Header()
    {
        var tf = new TextBoxElement("", PlaceholderText: "Enter name") { Header = "Name" };
        Assert.Equal("Name", AccessibilityApplier.ResolveCaptionForElement(tf));
    }

    [Fact]
    public void ResolveCaptionForElement_TextBox_Falls_Back_To_Placeholder()
    {
        var tf = new TextBoxElement("", PlaceholderText: "Enter name") { Header = null };
        Assert.Equal("Enter name", AccessibilityApplier.ResolveCaptionForElement(tf));
    }

    [Fact]
    public void ResolveCaptionForElement_TextBlock_Returns_Content()
    {
        var tb = new TextBlockElement("Label text");
        Assert.Equal("Label text", AccessibilityApplier.ResolveCaptionForElement(tb));
    }

    [Fact]
    public void ResolveCaptionForElement_Unknown_Element_Returns_Null()
    {
        var border = new BorderElement(null!);
        Assert.Null(AccessibilityApplier.ResolveCaptionForElement(border));
    }

    // ════════════════════════════════════════════════════════════════
    //  ParseSymbol — icon name → WinUI Symbol enum
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Home", Symbol.Home)]
    [InlineData("Setting", Symbol.Setting)]
    [InlineData("Find", Symbol.Find)]
    [InlineData("Add", Symbol.Add)]
    public void ParseSymbol_Valid_Name_Returns_Symbol(string name, Symbol expected)
    {
        Assert.Equal(expected, IconResolver.ParseSymbol(name));
    }

    [Fact]
    public void ParseSymbol_CaseInsensitive()
    {
        Assert.Equal(Symbol.Home, IconResolver.ParseSymbol("home"));
        Assert.Equal(Symbol.Home, IconResolver.ParseSymbol("HOME"));
    }

    [Fact]
    public void ParseSymbol_Unknown_Returns_Placeholder()
    {
        Assert.Equal(Symbol.Placeholder, IconResolver.ParseSymbol("NonExistentSymbol"));
    }

    // Note: ParseColumnDef/ParseRowDef create WinUI ColumnDefinition/RowDefinition
    // objects which require a WinUI thread. These are tested in selftest fixtures.
}
