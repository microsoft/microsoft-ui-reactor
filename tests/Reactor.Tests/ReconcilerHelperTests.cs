using Microsoft.UI.Reactor.Core;
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
        Assert.Equal("Hello World", Reconciler.ExtractElementCaption(element));
    }

    [Fact]
    public void ExtractElementCaption_Null_Returns_Null()
    {
        Assert.Null(Reconciler.ExtractElementCaption(null));
    }

    [Fact]
    public void ExtractElementCaption_NonTextBlock_Returns_Null()
    {
        var element = new BorderElement(null!);
        Assert.Null(Reconciler.ExtractElementCaption(element));
    }

    // ════════════════════════════════════════════════════════════════
    //  ResolveCaptionForElement — control-specific caption resolution
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCaptionForElement_Button_With_Label()
    {
        var button = new ButtonElement("Click Me");
        Assert.Equal("Click Me", Reconciler.ResolveCaptionForElement(button));
    }

    [Fact]
    public void ResolveCaptionForElement_Button_Without_Label_Falls_Through_To_ContentElement()
    {
        var button = new ButtonElement(null!) { ContentElement = new TextBlockElement("Content Text") };
        Assert.Equal("Content Text", Reconciler.ResolveCaptionForElement(button));
    }

    [Fact]
    public void ResolveCaptionForElement_Button_Without_Label_Or_Content_Returns_Null()
    {
        var button = new ButtonElement(null!) { ContentElement = null };
        Assert.Null(Reconciler.ResolveCaptionForElement(button));
    }

    [Fact]
    public void ResolveCaptionForElement_CheckBox_Returns_Label()
    {
        var cb = new CheckBoxElement(false, Label: "Accept terms");
        Assert.Equal("Accept terms", Reconciler.ResolveCaptionForElement(cb));
    }

    [Fact]
    public void ResolveCaptionForElement_RadioButton_Returns_Label()
    {
        var rb = new RadioButtonElement("Option A");
        Assert.Equal("Option A", Reconciler.ResolveCaptionForElement(rb));
    }

    [Fact]
    public void ResolveCaptionForElement_HyperlinkButton_Returns_Content()
    {
        var hl = new HyperlinkButtonElement("Learn more");
        Assert.Equal("Learn more", Reconciler.ResolveCaptionForElement(hl));
    }

    [Fact]
    public void ResolveCaptionForElement_RepeatButton_Returns_Label()
    {
        var rb = new RepeatButtonElement("Repeat");
        Assert.Equal("Repeat", Reconciler.ResolveCaptionForElement(rb));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleButton_Returns_Label()
    {
        var tb = new ToggleButtonElement("Toggle");
        Assert.Equal("Toggle", Reconciler.ResolveCaptionForElement(tb));
    }

    [Fact]
    public void ResolveCaptionForElement_DropDownButton_Returns_Label()
    {
        var ddb = new DropDownButtonElement("Choose");
        Assert.Equal("Choose", Reconciler.ResolveCaptionForElement(ddb));
    }

    [Fact]
    public void ResolveCaptionForElement_SplitButton_Returns_Label()
    {
        var sb = new SplitButtonElement("Split");
        Assert.Equal("Split", Reconciler.ResolveCaptionForElement(sb));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSplitButton_Returns_Label()
    {
        var tsb = new ToggleSplitButtonElement("ToggleSplit");
        Assert.Equal("ToggleSplit", Reconciler.ResolveCaptionForElement(tsb));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSwitch_Prefers_Header()
    {
        var ts = new ToggleSwitchElement(false, OnContent: "On", OffContent: "Off") { Header = "Wifi" };
        Assert.Equal("Wifi", Reconciler.ResolveCaptionForElement(ts));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSwitch_Falls_Back_To_OnContent()
    {
        var ts = new ToggleSwitchElement(false, OnContent: "Enabled", OffContent: "Disabled") { Header = null };
        Assert.Equal("Enabled", Reconciler.ResolveCaptionForElement(ts));
    }

    [Fact]
    public void ResolveCaptionForElement_ToggleSwitch_Falls_Back_To_OffContent()
    {
        var ts = new ToggleSwitchElement(false, OnContent: null, OffContent: "Disabled") { Header = null };
        Assert.Equal("Disabled", Reconciler.ResolveCaptionForElement(ts));
    }

    [Fact]
    public void ResolveCaptionForElement_TextField_Prefers_Header()
    {
        var tf = new TextBoxElement("", Placeholder: "Enter name") { Header = "Name" };
        Assert.Equal("Name", Reconciler.ResolveCaptionForElement(tf));
    }

    [Fact]
    public void ResolveCaptionForElement_TextField_Falls_Back_To_Placeholder()
    {
        var tf = new TextBoxElement("", Placeholder: "Enter name") { Header = null };
        Assert.Equal("Enter name", Reconciler.ResolveCaptionForElement(tf));
    }

    [Fact]
    public void ResolveCaptionForElement_TextBlock_Returns_Content()
    {
        var tb = new TextBlockElement("Label text");
        Assert.Equal("Label text", Reconciler.ResolveCaptionForElement(tb));
    }

    [Fact]
    public void ResolveCaptionForElement_Unknown_Element_Returns_Null()
    {
        var border = new BorderElement(null!);
        Assert.Null(Reconciler.ResolveCaptionForElement(border));
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
        Assert.Equal(expected, Reconciler.ParseSymbol(name));
    }

    [Fact]
    public void ParseSymbol_CaseInsensitive()
    {
        Assert.Equal(Symbol.Home, Reconciler.ParseSymbol("home"));
        Assert.Equal(Symbol.Home, Reconciler.ParseSymbol("HOME"));
    }

    [Fact]
    public void ParseSymbol_Unknown_Returns_Placeholder()
    {
        Assert.Equal(Symbol.Placeholder, Reconciler.ParseSymbol("NonExistentSymbol"));
    }

    // Note: ParseColumnDef/ParseRowDef create WinUI ColumnDefinition/RowDefinition
    // objects which require a WinUI thread. These are tested in selftest fixtures.
}
