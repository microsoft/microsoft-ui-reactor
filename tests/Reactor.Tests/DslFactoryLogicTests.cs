using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Dsl.cs factory methods that have non-trivial logic
/// (setting multiple properties, mapping from Command, etc.)
/// </summary>
public class DslFactoryLogicTests
{
    // ════════════════════════════════════════════════════════════════
    //  Text factory methods with semantic properties
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Heading_Sets_FontSize_Weight_And_HeadingLevel()
    {
        var el = Heading("Main Title");
        Assert.Equal("Main Title", el.Content);
        Assert.Equal(28, el.FontSize);
        Assert.Equal((ushort)700, el.Weight!.Value.Weight);
        Assert.Equal(AutomationHeadingLevel.Level1, el.Modifiers!.HeadingLevel);
    }

    [Fact]
    public void SubHeading_Sets_FontSize_Weight_And_HeadingLevel()
    {
        var el = SubHeading("Section");
        Assert.Equal("Section", el.Content);
        Assert.Equal(20, el.FontSize);
        Assert.Equal((ushort)600, el.Weight!.Value.Weight);
        Assert.Equal(AutomationHeadingLevel.Level2, el.Modifiers!.HeadingLevel);
    }

    [Fact]
    public void Caption_Sets_FontSize()
    {
        var el = Caption("Small text");
        Assert.Equal("Small text", el.Content);
        Assert.Equal(12, el.FontSize);
    }

    // ════════════════════════════════════════════════════════════════
    //  Button from Command
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Button_From_Command_Maps_Label_And_IsEnabled()
    {
        bool executed = false;
        var cmd = new Command { Label = "Save", Execute = () => executed = true };
        var el = Button(cmd);
        Assert.Equal("Save", el.Label);
        Assert.True(el.IsEnabled);
        el.OnClick!.Invoke();
        Assert.True(executed);
    }

    [Fact]
    public void Button_From_Command_Disabled()
    {
        var cmd = new Command { Label = "Delete", Execute = () => { }, CanExecute = false };
        var el = Button(cmd);
        Assert.False(el.IsEnabled);
    }

    [Fact]
    public void Button_With_Element_Content()
    {
        var icon = TextBlock("★");
        var el = Button(icon, () => { });
        Assert.Same(icon, el.ContentElement);
    }

    // ════════════════════════════════════════════════════════════════
    //  RichEditBox factory
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RichEditBox_Stores_OnTextChanged()
    {
        Action<string>? handler = s => { };
        var el = RichEditBox("initial", handler);
        Assert.Equal("initial", el.Text);
        Assert.Same(handler, el.OnTextChanged);
    }

    // ════════════════════════════════════════════════════════════════
    //  Input factories
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TextField_With_All_Parameters()
    {
        var el = TextField("val", s => { }, "hint", "Header");
        Assert.Equal("val", el.Value);
        Assert.Equal("hint", el.Placeholder);
        Assert.Equal("Header", el.Header);
        Assert.NotNull(el.OnChanged);
    }

    [Fact]
    public void PasswordBox_Creates_With_Parameters()
    {
        var el = PasswordBox("secret", null, "Enter password");
        Assert.Equal("secret", el.Password);
        Assert.Equal("Enter password", el.PlaceholderText);
    }

    [Fact]
    public void NumberBox_Creates_With_Value()
    {
        var el = NumberBox(42.5, null, "Count");
        Assert.Equal(42.5, el.Value);
        Assert.Equal("Count", el.Header);
    }

    [Fact]
    public void AutoSuggestBox_Creates_With_Handlers()
    {
        var el = AutoSuggestBox("query", s => { }, s => { });
        Assert.Equal("query", el.Text);
        Assert.NotNull(el.OnTextChanged);
        Assert.NotNull(el.OnQuerySubmitted);
    }

    [Fact]
    public void ThreeStateCheckBox_Creates_With_NullState()
    {
        var el = ThreeStateCheckBox(null, null, "Maybe");
        Assert.Null(el.CheckedState);
        Assert.Equal("Maybe", el.Label);
    }

    [Fact]
    public void Slider_Creates_With_Range()
    {
        var el = Slider(50, 0, 100, null);
        Assert.Equal(50, el.Value);
        Assert.Equal(0, el.Min);
        Assert.Equal(100, el.Max);
    }

    [Fact]
    public void ToggleSwitch_Creates_With_Content()
    {
        var el = ToggleSwitch(true, null, "Yes", "No", "Power");
        Assert.True(el.IsOn);
        Assert.Equal("Yes", el.OnContent);
        Assert.Equal("No", el.OffContent);
        Assert.Equal("Power", el.Header);
    }

    [Fact]
    public void RatingControl_Creates_With_Value()
    {
        var el = RatingControl(3.5, null);
        Assert.Equal(3.5, el.Value);
    }

    // ════════════════════════════════════════════════════════════════
    //  Layout factories
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void VStack_With_Spacing()
    {
        var el = VStack(8, TextBlock("a"), TextBlock("b"));
        Assert.Equal(8, el.Spacing);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Vertical, el.Orientation);
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void HStack_With_Spacing()
    {
        var el = HStack(4, TextBlock("a"));
        Assert.Equal(4, el.Spacing);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Horizontal, el.Orientation);
    }

    [Fact]
    public void VStack_Filters_Null_Children()
    {
        var el = VStack(TextBlock("a"), null, TextBlock("b"));
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void Border_Creates_With_Child()
    {
        var child = TextBlock("inside");
        var el = Border(child);
        Assert.Same(child, el.Child);
    }

    [Fact]
    public void ScrollView_Creates_With_Child()
    {
        var child = TextBlock("scroll me");
        var el = ScrollView(child);
        Assert.Same(child, el.Child);
    }

    [Fact]
    public void Viewbox_Creates_With_Child()
    {
        var child = TextBlock("scale me");
        var el = Viewbox(child);
        Assert.Same(child, el.Child);
    }

    // ════════════════════════════════════════════════════════════════
    //  Progress factories
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Progress_Determinate()
    {
        var el = Progress(75);
        Assert.Equal(75, el.Value);
    }

    [Fact]
    public void ProgressIndeterminate_HasNullValue()
    {
        var el = ProgressIndeterminate();
        Assert.Null(el.Value);
    }

    [Fact]
    public void ProgressRing_Determinate_And_Indeterminate()
    {
        var det = ProgressRing(50);
        Assert.Equal(50, det.Value);

        var indet = ProgressRing();
        Assert.Null(indet.Value);
    }

    // ════════════════════════════════════════════════════════════════
    //  Info / Status factories
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void InfoBar_With_Title_And_Message()
    {
        var el = InfoBar("Error", "Something went wrong");
        Assert.Equal("Error", el.Title);
        Assert.Equal("Something went wrong", el.Message);
    }

    [Fact]
    public void InfoBadge_With_Value()
    {
        var el = InfoBadge(5);
        Assert.Equal(5, el.Value);
    }

    // ════════════════════════════════════════════════════════════════
    //  Dialog / Overlay factories
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ContentDialog_Creates_With_Title_Content_Button()
    {
        var content = TextBlock("Are you sure?");
        var el = ContentDialog("Confirm", content, "Yes");
        Assert.Equal("Confirm", el.Title);
        Assert.Same(content, el.Content);
        Assert.Equal("Yes", el.PrimaryButtonText);
    }

    [Fact]
    public void TeachingTip_Creates_With_Title_Subtitle()
    {
        var el = TeachingTip("Did you know?", "This is a tip");
        Assert.Equal("Did you know?", el.Title);
        Assert.Equal("This is a tip", el.Subtitle);
    }

    // ════════════════════════════════════════════════════════════════
    //  Media factories
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Image_Creates_With_Source()
    {
        var el = Image("ms-appx:///Assets/logo.png");
        Assert.Equal("ms-appx:///Assets/logo.png", el.Source);
    }

    [Fact]
    public void PersonPicture_Creates()
    {
        var el = PersonPicture();
        Assert.NotNull(el);
    }

    // ════════════════════════════════════════════════════════════════
    //  Navigation factories
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TitleBar_Creates_With_Title()
    {
        var el = TitleBar("My App");
        Assert.Equal("My App", el.Title);
    }

    [Fact]
    public void Expander_Creates_With_Properties()
    {
        var content = TextBlock("details");
        var el = Expander("Show more", content, true, null);
        Assert.Equal("Show more", el.Header);
        Assert.Same(content, el.Content);
        Assert.True(el.IsExpanded);
    }
}
