using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Text;
using Windows.UI.Text;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Element records — creation, immutability, defaults, and the DSL factory methods.
/// These are pure C# record tests, no WinUI thread needed.
/// </summary>
public class ElementTests
{
    // ════════════════════════════════════════════════════════════════
    //  Text elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Text_Creates_TextBlockElement_With_Content()
    {
        var el = TextBlock("Hello");
        Assert.IsType<TextBlockElement>(el);
        Assert.Equal("Hello", el.Content);
        Assert.Null(el.FontSize);
        Assert.Null(el.Weight);
    }

    [Fact]
    public void Heading_Creates_TextBlockElement_With_Bold_And_Size28()
    {
        var el = Heading("Title");
        Assert.Equal("Title", el.Content);
        Assert.Equal(28, el.FontSize);
        Assert.NotNull(el.Weight);
        Assert.Equal(700, el.Weight!.Value.Weight);
    }

    [Fact]
    public void SubHeading_Creates_TextBlockElement_With_SemiBold_And_Size20()
    {
        var el = SubHeading("Sub");
        Assert.Equal(20, el.FontSize);
        Assert.NotNull(el.Weight);
        Assert.Equal(600, el.Weight!.Value.Weight);
    }

    [Fact]
    public void Caption_Creates_TextBlockElement_With_Size12()
    {
        var el = Caption("cap");
        Assert.Equal(12, el.FontSize);
    }

    [Fact]
    public void String_Implicit_Conversion_To_Element()
    {
        Element el = "hello";
        Assert.IsType<TextBlockElement>(el);
        Assert.Equal("hello", ((TextBlockElement)el).Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  Button elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Button_Defaults_To_Enabled_With_No_Click()
    {
        var el = Button("Click");
        Assert.Equal("Click", el.Label);
        Assert.True(el.IsEnabled);
        Assert.Null(el.OnClick);
    }

    [Fact]
    public void Button_With_Click_Handler()
    {
        bool clicked = false;
        var el = Button("Go", () => clicked = true);
        el.OnClick!.Invoke();
        Assert.True(clicked);
    }

    [Fact]
    public void Button_Disabled_Extension_Toggles_IsEnabled()
    {
        var el = Button("Go").IsEnabled(false);
        Assert.False(el.Modifiers!.IsEnabled);

        var el2 = Button("Go").IsEnabled(true);
        Assert.True(el2.Modifiers!.IsEnabled);
    }

    [Fact]
    public void HyperlinkButton_Creates_With_Uri()
    {
        var uri = new Uri("https://example.com");
        var el = HyperlinkButton("Link", uri);
        Assert.Equal("Link", el.Content);
        Assert.Equal(uri, el.NavigateUri);
    }

    [Fact]
    public void ToggleButton_Creates_With_Defaults()
    {
        var el = ToggleButton("Toggle");
        Assert.Equal("Toggle", el.Label);
        Assert.False(el.IsChecked);
        Assert.Null(el.OnIsCheckedChanged);
    }

    [Fact]
    public void RepeatButton_Has_Default_Delay_And_Interval()
    {
        var el = RepeatButton("Rep");
        Assert.Equal(250, el.Delay);
        Assert.Equal(50, el.Interval);
    }

    // ════════════════════════════════════════════════════════════════
    //  Input elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_Creates_With_Value_And_Placeholder()
    {
        var el = TextBox("val", placeholderText: "hint");
        Assert.Equal("val", el.Value);
        Assert.Equal("hint", el.PlaceholderText);
    }

    [Fact]
    public void PasswordBox_Creates_With_Password()
    {
        var el = PasswordBox("secret", placeholderText: "Enter password");
        Assert.Equal("secret", el.Password);
        Assert.Equal("Enter password", el.PlaceholderText);
    }

    [Fact]
    public void NumberBox_Creates_With_Defaults()
    {
        var el = NumberBox(42.0);
        Assert.Equal(42.0, el.Value);
        Assert.Equal(double.MinValue, el.Minimum);
        Assert.Equal(double.MaxValue, el.Maximum);
        Assert.Equal(NumberBoxSpinButtonPlacementMode.Hidden, el.SpinButtonPlacement);
    }

    [Fact]
    public void NumberBox_Range_Extension_Sets_MinMax()
    {
        var el = NumberBox(5.0).Range(0, 10);
        Assert.Equal(0, el.Minimum);
        Assert.Equal(10, el.Maximum);
    }

    [Fact]
    public void CheckBox_Creates_With_State()
    {
        var el = CheckBox(true, label: "Agree");
        Assert.True(el.IsChecked);
        Assert.Equal("Agree", el.Label);
    }

    [Fact]
    public void RadioButton_Creates_With_GroupName()
    {
        var el = RadioButton("Option A", groupName: "grp1");
        Assert.Equal("Option A", el.Label);
        Assert.Equal("grp1", el.GroupName);
    }

    [Fact]
    public void ComboBox_Creates_With_Items()
    {
        var el = ComboBox(["A", "B", "C"], selectedIndex: 1);
        Assert.Equal(3, el.Items.Length);
        Assert.Equal(1, el.SelectedIndex);
    }

    [Fact]
    public void ComboBox_Placeholder_Extension()
    {
        var el = ComboBox(["A"]).PlaceholderText("Pick one");
        Assert.Equal("Pick one", el.PlaceholderText);
    }

    [Fact]
    public void Slider_Creates_With_Range()
    {
        var el = Slider(50, 0, 100);
        Assert.Equal(50, el.Value);
        Assert.Equal(0, el.Min);
        Assert.Equal(100, el.Max);
    }

    [Fact]
    public void ToggleSwitch_Creates_With_Content()
    {
        var el = ToggleSwitch(true, onContent: "On", offContent: "Off");
        Assert.True(el.IsOn);
        Assert.Equal("On", el.OnContent);
        Assert.Equal("Off", el.OffContent);
    }

    [Fact]
    public void RatingControl_Creates_With_Defaults()
    {
        var el = RatingControl(3.5);
        Assert.Equal(3.5, el.Value);
        Assert.Equal(5, el.MaxRating);
        Assert.False(el.IsReadOnly);
    }

    // ════════════════════════════════════════════════════════════════
    //  Date / Time elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DatePicker_Creates_With_Date()
    {
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var el = DatePicker(date);
        Assert.Equal(date, el.Date);
        Assert.True(el.DayVisible);
    }

    [Fact]
    public void TimePicker_Creates_With_Time()
    {
        var time = new TimeSpan(14, 30, 0);
        var el = TimePicker(time);
        Assert.Equal(time, el.Time);
        Assert.Equal(1, el.MinuteIncrement);
    }

    // ════════════════════════════════════════════════════════════════
    //  Progress elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Progress_Determinate()
    {
        var el = Progress(75);
        Assert.Equal(75.0, el.Value);
        Assert.False(el.IsIndeterminate);
    }

    [Fact]
    public void Progress_Indeterminate()
    {
        var el = ProgressIndeterminate();
        Assert.Null(el.Value);
        Assert.True(el.IsIndeterminate);
    }

    [Fact]
    public void ProgressRing_Defaults()
    {
        var el = ProgressRing();
        Assert.True(el.IsIndeterminate);
        Assert.True(el.IsActive);
    }

    // ════════════════════════════════════════════════════════════════
    //  Layout elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void VStack_Creates_Vertical_Stack_Filtering_Nulls()
    {
        var el = VStack(TextBlock("A"), null, TextBlock("B"));
        Assert.Equal(Orientation.Vertical, el.Orientation);
        Assert.Equal(2, el.Children.Length);
        Assert.Equal(8, el.Spacing); // default
    }

    [Fact]
    public void VStack_With_Custom_Spacing()
    {
        var el = VStack(16, TextBlock("A"), TextBlock("B"));
        Assert.Equal(16, el.Spacing);
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void HStack_Creates_Horizontal_Stack()
    {
        var el = HStack(TextBlock("A"), TextBlock("B"));
        Assert.Equal(Orientation.Horizontal, el.Orientation);
    }

    [Fact]
    public void Border_Creates_With_Child()
    {
        var child = TextBlock("inner");
        var el = Border(child);
        Assert.Same(child, el.Child);
        Assert.Null(el.CornerRadius);
        Assert.Null(el.Background);
    }

    [Fact]
    public void Border_Fluent_Extensions_CornerRadius_And_Thickness()
    {
        // CornerRadius is now stored on ElementModifiers (works on Control and Border)
        var el = Border(TextBlock("x"))
            .CornerRadius(8);
        Assert.Equal(new Microsoft.UI.Xaml.CornerRadius(8), el.Modifiers!.CornerRadius);
    }

    // Border_Fluent_Extensions_Brush moved to selfhost fixtures (WinUIActivationFixtures).

    [Fact]
    public void ScrollView_Creates_With_Defaults()
    {
        var el = ScrollViewer(TextBlock("scrollable"));
        Assert.Equal(ScrollBarVisibility.Auto, el.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, el.VerticalScrollBarVisibility);
    }

    [Fact]
    public void Grid_Creates_With_Definition_And_Children()
    {
        var el = Grid(
            [GridSize.Star(), GridSize.Auto], [GridSize.Star()],
            TextBlock("A").Grid(row: 0, column: 0),
            TextBlock("B").Grid(row: 0, column: 1)
        );
        Assert.Equal(2, el.Definition.Columns.Length);
        Assert.Single(el.Definition.Rows);
        Assert.Equal(2, el.Children.Length);
    }

    [Fact]
    public void Expander_Creates_With_Header_And_Content()
    {
        var el = Expander("Header", TextBlock("Content"), isExpanded: true);
        Assert.Equal("Header", el.Header);
        Assert.True(el.IsExpanded);
        Assert.Equal(ExpandDirection.Down, el.ExpandDirection);
    }

    // ════════════════════════════════════════════════════════════════
    //  Navigation elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NavigationView_Creates_With_Items()
    {
        var el = NavigationView([NavItem("Home", tag: "home"), NavItem("Settings")]);
        Assert.Equal(2, el.MenuItems.Length);
        Assert.Equal("home", el.MenuItems[0].Tag);
        Assert.True(el.IsSettingsVisible);
    }

    [Fact]
    public void TabView_Creates_With_Tabs()
    {
        var el = TabView(Tab("Tab1", TextBlock("Content1")), Tab("Tab2", TextBlock("Content2")));
        Assert.Equal(2, el.Tabs.Length);
        Assert.Equal(0, el.SelectedIndex);
    }

    [Fact]
    public void BreadcrumbBar_Creates_With_Items()
    {
        var el = BreadcrumbBar([Breadcrumb("Home"), Breadcrumb("Products")]);
        Assert.Equal(2, el.Items.Length);
    }

    // ════════════════════════════════════════════════════════════════
    //  Collections
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ListView_Creates_With_Items()
    {
        var el = ListView(TextBlock("A"), TextBlock("B"), TextBlock("C"));
        Assert.Equal(3, el.Items.Length);
        Assert.Equal(-1, el.SelectedIndex);
        Assert.Equal(ListViewSelectionMode.Single, el.SelectionMode);
    }

    [Fact]
    public void TreeView_Creates_With_Nodes()
    {
        var el = TreeView(
            TreeNode("Root",
                TreeNode("Child1"),
                TreeNode("Child2")
            )
        );
        Assert.Single(el.Nodes);
        Assert.Equal(2, el.Nodes[0].Children!.Length);
    }

    // ════════════════════════════════════════════════════════════════
    //  Dialog / Overlay elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ContentDialog_Creates_With_Defaults()
    {
        var el = ContentDialog("Title", TextBlock("Body"));
        Assert.Equal("Title", el.Title);
        Assert.Equal("OK", el.PrimaryButtonText);
        Assert.False(el.IsOpen);
    }

    [Fact]
    public void InfoBar_Creates_With_Severity()
    {
        var el = InfoBar("Warning", "Something happened")
            .Severity(InfoBarSeverity.Warning);
        Assert.Equal("Warning", el.Title);
        Assert.Equal(InfoBarSeverity.Warning, el.Severity);
        Assert.True(el.IsOpen);
    }

    // ════════════════════════════════════════════════════════════════
    //  Menu elements
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MenuBar_Creates_With_Items()
    {
        var el = MenuBar(
            Menu("File", MenuItem("New"), MenuItem("Open"), MenuSeparator(), MenuItem("Exit")),
            Menu("Edit", MenuItem("Copy"))
        );
        Assert.Equal(2, el.Items.Length);
        Assert.Equal(4, el.Items[0].Items.Length);
    }

    [Fact]
    public void CommandBar_Creates_With_Commands()
    {
        var el = CommandBar(
            primaryCommands: [AppBarButton("Save"), AppBarButton("Delete")]
        );
        Assert.Equal(2, el.PrimaryCommands!.Length);
    }

    // ════════════════════════════════════════════════════════════════
    //  Modifier extensions
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Margin_Stores_Inline_On_Element()
    {
        var el = TextBlock("Hi").Margin(10);
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers);
        Assert.Equal(new Thickness(10), el.Modifiers!.Margin);
    }

    [Fact]
    public void Margin_Accepts_Single_Named_Side()
    {
        // 525-run corpus showed ~200 build failures from agents writing
        // `.Margin(top: 12)` etc. against the prior all-required signature.
        // The per-side overload now defaults missing sides to 0, matching
        // the WPF Thickness convention and the agent's intuition.
        var top = TextBlock("Hi").Margin(top: 12);
        Assert.Equal(new Thickness(0, 12, 0, 0), top.Modifiers!.Margin);

        var left = TextBlock("Hi").Margin(left: 5);
        Assert.Equal(new Thickness(5, 0, 0, 0), left.Modifiers!.Margin);

        var pair = TextBlock("Hi").Margin(left: 8, right: 8);
        Assert.Equal(new Thickness(8, 0, 8, 0), pair.Modifiers!.Margin);
    }

    [Fact]
    public void Margin_Positional_Calls_Bind_To_More_Specific_Overloads()
    {
        // Regression guard for the per-side overload's default-value addition.
        // C# overload resolution prefers fewer-parameter overloads when they
        // match — so .Margin(10) must still bind to the uniform overload
        // (Thickness(10,10,10,10)), not the per-side one (Thickness(10,0,0,0)).
        Assert.Equal(new Thickness(10), TextBlock("x").Margin(10).Modifiers!.Margin);
        Assert.Equal(new Thickness(8, 4, 8, 4),
            TextBlock("x").Margin(horizontal: 8, vertical: 4).Modifiers!.Margin);
        Assert.Equal(new Thickness(1, 2, 3, 4),
            TextBlock("x").Margin(1, 2, 3, 4).Modifiers!.Margin);
    }

    [Fact]
    public void Margin_Two_Arg_Positional_Follows_CSS_Vertical_First()
    {
        var el = TextBlock("x").Margin(16, 14);
        Assert.Equal(new Thickness(16, 14, 16, 14), el.Modifiers!.Margin);

        var pad = TextBlock("x").Padding(16, 14);
        Assert.Equal(new Thickness(16, 14, 16, 14), pad.Modifiers!.Padding);
    }

    [Fact]
    public void Padding_Accepts_Single_Named_Side()
    {
        // Same defaulting story as Margin — the symmetry test exists so a
        // future edit that drops defaults from one of the two stays caught.
        var top = TextBlock("Hi").Padding(top: 12);
        Assert.Equal(new Thickness(0, 12, 0, 0), top.Modifiers!.Padding);

        Assert.Equal(new Thickness(16), TextBlock("x").Padding(16).Modifiers!.Padding);
        Assert.Equal(new Thickness(8, 4, 8, 4),
            TextBlock("x").Padding(horizontal: 8, vertical: 4).Modifiers!.Padding);
    }

    [Fact]
    public void Multiple_Modifiers_Merge()
    {
        var el = TextBlock("Hi").Margin(10).Width(200).Opacity(0.5);
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers);
        Assert.Equal(new Thickness(10), el.Modifiers!.Margin);
        Assert.Equal(200, el.Modifiers.Width);
        Assert.Equal(0.5, el.Modifiers.Opacity);
    }

    [Fact]
    public void Width_And_Height_Via_Size()
    {
        var el = TextBlock("Hi").Size(100, 50);
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers);
        Assert.Equal(100, el.Modifiers!.Width);
        Assert.Equal(50, el.Modifiers.Height);
    }

    [Fact]
    public void Center_Sets_Both_Alignments()
    {
        var el = TextBlock("Hi").Center();
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers);
        Assert.Equal(HorizontalAlignment.Center, el.Modifiers!.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, el.Modifiers.VerticalAlignment);
    }

    [Fact]
    public void Visible_False_Sets_IsVisible()
    {
        var el = TextBlock("Hi").IsVisible(false);
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers);
        Assert.False(el.Modifiers!.IsVisible);
    }

    [Fact]
    public void ToolTip_Sets_ToolTip()
    {
        var el = TextBlock("Hi").ToolTip("Help text");
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers);
        Assert.Equal("Help text", el.Modifiers!.ToolTip);
    }

    [Fact]
    public void WithKey_Sets_Key_On_Element()
    {
        var el = TextBlock("Hi").WithKey("item-1");
        Assert.Equal("item-1", el.Key);
    }

    // ════════════════════════════════════════════════════════════════
    //  Set() extension — compile-time type safety
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Set_Adds_Setter_To_ButtonElement()
    {
        var el = Button("Go").Set(b => b.FlowDirection = Microsoft.UI.Xaml.FlowDirection.RightToLeft);
        // Setters is internal, so we verify via the Set extension returning a new element
        var el2 = Button("Go");
        Assert.NotEqual(el, el2); // Set creates a new record with setters
    }

    [Fact]
    public void Set_Chains_Multiple_Setters()
    {
        var el = TextBlock("Hi")
            .IsTextSelectionEnabled()
            .TextWrapping();
        // Fluent methods create new record instances with the properties set
        Assert.NotEqual(TextBlock("Hi"), el);
    }

    [Fact]
    public void Set_On_Slider_Is_Strongly_Typed()
    {
        var el = Slider(50, 0, 100)
            .Set(s => s.TickFrequency = 10);
        Assert.NotEqual(Slider(50, 0, 100), el);
    }

    // ════════════════════════════════════════════════════════════════
    //  Conditional helpers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void When_True_Returns_Element()
    {
        var el = When(true, () => TextBlock("yes"));
        Assert.IsType<TextBlockElement>(el);
    }

    [Fact]
    public void When_False_Returns_Empty()
    {
        var el = When(false, () => TextBlock("no"));
        Assert.IsType<EmptyElement>(el);
    }

    [Fact]
    public void If_True_Returns_Then()
    {
        var el = If(true, () => TextBlock("then"), () => TextBlock("else"));
        Assert.Equal("then", ((TextBlockElement)el).Content);
    }

    [Fact]
    public void If_False_Returns_Otherwise()
    {
        var el = If(false, () => TextBlock("then"), () => TextBlock("else"));
        Assert.Equal("else", ((TextBlockElement)el).Content);
    }

    [Fact]
    public void If_False_No_Otherwise_Returns_Empty()
    {
        var el = If(false, () => TextBlock("then"));
        Assert.IsType<EmptyElement>(el);
    }

    [Fact]
    public void ForEach_Maps_Items_To_Group()
    {
        var el = ForEach(new[] { "A", "B", "C" }, item => TextBlock(item));
        Assert.IsType<GroupElement>(el);
        var group = (GroupElement)el;
        Assert.Equal(3, group.Children.Length);
    }

    [Fact]
    public void ForEach_With_Index()
    {
        var el = ForEach(new[] { "A", "B" }, (item, i) => TextBlock($"{i}:{item}"));
        var group = (GroupElement)el;
        Assert.Equal(2, group.Children.Length);
        Assert.Equal("0:A", ((TextBlockElement)group.Children[0]).Content);
    }

    [Fact]
    public void ForEach_Group_Flattened_In_Parent()
    {
        var el = HStack(
            TextBlock("before"),
            ForEach(new[] { "A", "B" }, item => TextBlock(item)),
            TextBlock("after")
        );
        // GroupElement should be flattened: before, A, B, after
        Assert.Equal(4, el.Children.Length);
        Assert.Equal("before", ((TextBlockElement)el.Children[0]).Content);
        Assert.Equal("A", ((TextBlockElement)el.Children[1]).Content);
        Assert.Equal("B", ((TextBlockElement)el.Children[2]).Content);
        Assert.Equal("after", ((TextBlockElement)el.Children[3]).Content);
    }

    [Fact]
    public void Empty_Returns_Singleton()
    {
        var el1 = Empty();
        var el2 = Empty();
        Assert.Same(el1, el2);
    }

    // ════════════════════════════════════════════════════════════════
    //  ErrorBoundary element
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ErrorBoundary_Creates_Element_With_Child_And_Fallback()
    {
        var child = TextBlock("Hello");
        var eb = ErrorBoundary(child, ex => TextBlock($"Error: {ex.Message}"));
        Assert.IsType<ErrorBoundaryElement>(eb);
        Assert.Same(child, eb.Child);
    }

    [Fact]
    public void ErrorBoundary_Static_Fallback_Overload()
    {
        var child = TextBlock("Hello");
        var fallback = TextBlock("Something went wrong");
        var eb = ErrorBoundary(child, fallback);
        Assert.IsType<ErrorBoundaryElement>(eb);

        // The static overload wraps in a lambda — verify it returns the fallback
        var result = eb.Fallback(new Exception("test"));
        Assert.IsType<TextBlockElement>(result);
        Assert.Equal("Something went wrong", ((TextBlockElement)result).Content);
    }

    [Fact]
    public void ErrorBoundary_ShallowEquals_Always_Returns_False()
    {
        var child = TextBlock("Hello");
        Func<Exception, Element> fallback = ex => TextBlock("err");
        var a = new ErrorBoundaryElement(child, fallback);
        var b = new ErrorBoundaryElement(child, fallback);
        // Delegates can't be reliably compared, so ShallowEquals returns false
        Assert.False(Element.ShallowEquals(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  Setters fast-path: ShallowEquals uses ReferenceEquals on Setters
    // ════════════════════════════════════════════════════════════════
    //
    // Setters arrays are reference-compared rather than length-checked. The
    // C# 12 collection-expression `[]` default lowers to Array.Empty<T>(),
    // which is a singleton — so two elements with the default empty Setters
    // remain reference-equal. Non-empty arrays are *not* deep-compared:
    // a stable Setters reference held across renders fast-paths; a freshly
    // allocated array (the typical case) does not. This contract is what
    // lets future call-sites that cache their Setters opt into skip.

    [Fact]
    public void ShallowEquals_TextBlock_Default_Empty_Setters_Are_Equal()
    {
        // Both use the empty-default Setters → same Array.Empty<T>() singleton.
        var a = TextBlock("Hello");
        var b = TextBlock("Hello");
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_TextBlock_Different_NonEmpty_Setters_Are_Not_Equal()
    {
        // .Set allocates a fresh Setters array each call — distinct refs → not equal.
        var a = TextBlock("Hello").Set(tb => tb.FontSize = 14);
        var b = TextBlock("Hello").Set(tb => tb.FontSize = 14);
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_TextBlock_Same_NonEmpty_Setters_Reference_Are_Equal()
    {
        // Authors that cache the Setters array across renders get the fast-path:
        // identical underlying record + reference-equal Setters → ShallowEquals true.
        var setters = new Action<TextBlock>[] { tb => tb.FontSize = 14 };
        var a = TextBlock("Hello") with { Setters = setters };
        var b = TextBlock("Hello") with { Setters = setters };
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_TextBlock_Empty_Vs_NonEmpty_Setters_Are_Not_Equal()
    {
        // Empty default vs non-empty: refs differ → fast-path declines.
        var a = TextBlock("Hello");
        var b = TextBlock("Hello").Set(tb => tb.FontSize = 14);
        Assert.False(Element.ShallowEquals(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  Record immutability
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Element_With_Expression_Creates_New_Instance()
    {
        // Use record with-expression directly to avoid FontWeights.Bold COMException
        var original = TextBlock("Hello");
        var modified = original with { Weight = new global::Windows.UI.Text.FontWeight { Weight = 700 } };
        Assert.NotSame(original, modified);
        Assert.Null(original.Weight);
        Assert.Equal(700, modified.Weight!.Value.Weight);
    }

    [Fact]
    public void Slider_StepFrequency_Extension()
    {
        var el = Slider(50).StepFrequency(5);
        Assert.Equal(5, el.StepFrequency);
    }

    // ════════════════════════════════════════════════════════════════
    //  CanUpdate logic
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Same_Type_Verified_By_Record_Type()
    {
        // CanUpdate is internal — we verify the underlying logic:
        // same element type → can update (same .GetType())
        var a = TextBlock("a");
        var b = TextBlock("b");
        Assert.Equal(a.GetType(), b.GetType());
    }

    [Fact]
    public void CanUpdate_Different_Type_Verified_By_Record_Type()
    {
        var a = (Element)TextBlock("a");
        var b = (Element)Button("b");
        Assert.NotEqual(a.GetType(), b.GetType());
    }

    [Fact]
    public void CanUpdate_Same_Component_Type_Has_Same_ComponentType()
    {
        var a = new ComponentElement(typeof(TestComponent));
        var b = new ComponentElement(typeof(TestComponent));
        Assert.Equal(a.ComponentType, b.ComponentType);
    }

    [Fact]
    public void CanUpdate_Different_Component_Type_Has_Different_ComponentType()
    {
        var a = new ComponentElement(typeof(TestComponent));
        var b = new ComponentElement(typeof(TestComponent2));
        Assert.NotEqual(a.ComponentType, b.ComponentType);
    }

    // ════════════════════════════════════════════════════════════════
    //  Thickness
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Thickness_Uniform()
    {
        var t = new Thickness(10);
        Assert.Equal(10, t.Left);
        Assert.Equal(10, t.Top);
        Assert.Equal(10, t.Right);
        Assert.Equal(10, t.Bottom);
    }

    [Fact]
    public void Thickness_Horizontal_Vertical()
    {
        var t = new Thickness(5, 10, 5, 10);
        Assert.Equal(5, t.Left);
        Assert.Equal(10, t.Top);
        Assert.Equal(5, t.Right);
        Assert.Equal(10, t.Bottom);
    }

    [Fact]
    public void Thickness_Full()
    {
        var t = new Thickness(1, 2, 3, 4);
        Assert.Equal(1, t.Left);
        Assert.Equal(2, t.Top);
        Assert.Equal(3, t.Right);
        Assert.Equal(4, t.Bottom);
    }

    // ════════════════════════════════════════════════════════════════
    //  ElementModifiers.Merge
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Modifiers_Merge_Overwrites_Non_Null()
    {
        var a = new ElementModifiers { Width = 100, Height = 50 };
        var b = new ElementModifiers { Width = 200, Opacity = 0.5 };
        var merged = a.Merge(b);

        Assert.Equal(200, merged.Width);   // overwritten
        Assert.Equal(50, merged.Height);   // kept from a
        Assert.Equal(0.5, merged.Opacity); // new from b
    }

    [Fact]
    public void Modifiers_Merge_Preserves_When_Other_Is_Null()
    {
        var a = new ElementModifiers { Margin = new Thickness(10) };
        var b = new ElementModifiers { };
        var merged = a.Merge(b);

        Assert.Equal(new Thickness(10), merged.Margin);
    }

    // ── Test component stubs ────────────────────────────────────────

    private class TestComponent : Component
    {
        public override Element Render() => TextBlock("test");
    }

    private class TestComponent2 : Component
    {
        public override Element Render() => TextBlock("test2");
    }
}
