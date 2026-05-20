using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Drive every fluent modifier on ElementExtensions so the simple
/// <c>el with { ... }</c> bodies get covered. Each test invokes a previously
/// uncovered method and asserts the result has the expected modifier set,
/// proving the chain executed.
/// </summary>
public class ElementExtensionsCoverageTests
{
    // ────────────────────────────────────────────────────────────────
    //  Margin / Padding overloads
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Margin_HorizontalVertical()
    {
        var el = TextBlock("x").Margin(horizontal: 8.0, vertical: 4.0);
        Assert.Equal(new Thickness(8, 4, 8, 4), el.Modifiers!.Margin);
    }

    [Fact]
    public void Margin_FourSides()
    {
        var el = TextBlock("x").Margin(1, 2, 3, 4);
        Assert.Equal(new Thickness(1, 2, 3, 4), el.Modifiers!.Margin);
    }

    [Fact]
    public void Padding_Uniform_HorizontalVertical_FourSides()
    {
        var a = TextBlock("x").Padding(5);
        var b = TextBlock("x").Padding(horizontal: 8.0, vertical: 4.0);
        var c = TextBlock("x").Padding(1, 2, 3, 4);
        Assert.Equal(new Thickness(5), a.Modifiers!.Padding);
        Assert.Equal(new Thickness(8, 4, 8, 4), b.Modifiers!.Padding);
        Assert.Equal(new Thickness(1, 2, 3, 4), c.Modifiers!.Padding);
    }

    [Fact]
    public void Logical_BiDi_Margin_Padding_Border()
    {
        var el = TextBlock("x")
            .MarginInlineStart(1)
            .MarginInlineEnd(2)
            .PaddingInlineStart(3)
            .PaddingInlineEnd(4)
            .BorderInlineStart(new Thickness(5));
        Assert.Equal(1, el.Modifiers!.MarginInlineStart);
        Assert.Equal(2, el.Modifiers.MarginInlineEnd);
        Assert.Equal(3, el.Modifiers.PaddingInlineStart);
        Assert.Equal(4, el.Modifiers.PaddingInlineEnd);
        Assert.Equal(new Thickness(5), el.Modifiers.BorderInlineStart);
    }

    // ────────────────────────────────────────────────────────────────
    //  Size / Min / Max / Center
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Sets_Width_And_Height()
    {
        var el = TextBlock("x").Size(120, 40);
        Assert.Equal(120, el.Modifiers!.Width);
        Assert.Equal(40, el.Modifiers.Height);
    }

    [Fact]
    public void MinMax_Width_Height()
    {
        var el = TextBlock("x").MinWidth(10).MinHeight(20).MaxWidth(100).MaxHeight(200);
        Assert.Equal(10, el.Modifiers!.MinWidth);
        Assert.Equal(20, el.Modifiers.MinHeight);
        Assert.Equal(100, el.Modifiers.MaxWidth);
        Assert.Equal(200, el.Modifiers.MaxHeight);
    }

    [Fact]
    public void Center_Sets_Both_Alignments()
    {
        var el = TextBlock("x").Center();
        Assert.Equal(HorizontalAlignment.Center, el.Modifiers!.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, el.Modifiers.VerticalAlignment);
    }

    [Fact]
    public void RequestedTheme_Sets_Modifier()
    {
        var el = TextBlock("x").RequestedTheme(ElementTheme.Dark);
        Assert.Equal(ElementTheme.Dark, el.Modifiers!.RequestedTheme);
    }

    // ────────────────────────────────────────────────────────────────
    //  Visibility / Visual / Transforms
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Visibility_Opacity_Scale_Rotation_Center()
    {
        var v3 = new global::System.Numerics.Vector3(2, 3, 4);
        var el = TextBlock("x")
            .IsVisible(false)
            .Opacity(0.5)
            .Scale(2.0f)
            .Scale(v3)
            .Rotation(45f)
            .CenterPoint(v3);
        Assert.False(el.Modifiers!.IsVisible);
        Assert.Equal(0.5, el.Modifiers.Opacity);
        Assert.Equal(v3, el.Modifiers.Scale);
        Assert.Equal(45f, el.Modifiers.Rotation);
        Assert.Equal(v3, el.Modifiers.CenterPoint);
    }

    [Fact]
    public void Translation_Sets_Vector()
    {
        var el = TextBlock("x").Translation(1, 2, 3);
        Assert.Equal(new global::System.Numerics.Vector3(1, 2, 3), el.Modifiers!.Translation);
    }

    // ────────────────────────────────────────────────────────────────
    //  Typography
    // ────────────────────────────────────────────────────────────────

    // FontFamily / FontWeight overloads require constructing WinUI types
    // (FontFamily, FontWeight) and live in the SelfHost coverage suite.

    // ────────────────────────────────────────────────────────────────
    //  Decoration (tooltip, flyouts, contextflyout)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ToolTip_And_Flyout_Attachments()
    {
        var fly = (Element)Button("Y");
        var ctx = (Element)Button("Z");
        var rich = (Element)TextBlock("rich");

        var el = TextBlock("x")
            .ToolTip("hello")
            .WithFlyout(fly)
            .WithContextFlyout(ctx)
            .WithToolTip(rich);

        Assert.Equal("hello", el.Modifiers!.ToolTip);
        Assert.Same(fly, el.Modifiers.AttachedFlyout);
        Assert.Same(ctx, el.Modifiers.ContextFlyout);
        Assert.Same(rich, el.Modifiers.RichToolTip);
    }

    // ────────────────────────────────────────────────────────────────
    //  Theme bindings (Background, Foreground, Border)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Background_Foreground_Border_Theme_Bindings()
    {
        // Brush-creating overloads require WinUI activation; the theme overload
        // doesn't and exercises the ModifyTheme code path.
        var bg = TextBlock("x").Background(Theme.Accent);
        Assert.True(bg.ThemeBindings!.ContainsKey("Background"));

        var fg = TextBlock("x").Foreground(Theme.PrimaryText);
        Assert.True(fg.ThemeBindings!.ContainsKey("Foreground"));

        var bd = TextBlock("x").WithBorder(Theme.CardStroke, 4.0);
        Assert.True(bd.ThemeBindings!.ContainsKey("BorderBrush"));
        Assert.Equal(new Thickness(4.0), bd.Modifiers!.BorderThickness);
    }

    // ────────────────────────────────────────────────────────────────
    //  CornerRadius / IsEnabled
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CornerRadius_Uniform_And_FourSides()
    {
        var a = TextBlock("x").CornerRadius(6);
        var b = TextBlock("x").CornerRadius(1, 2, 3, 4);
        Assert.Equal(new CornerRadius(6), a.Modifiers!.CornerRadius);
        Assert.Equal(new CornerRadius(1, 2, 3, 4), b.Modifiers!.CornerRadius);
    }

    [Fact]
    public void IsEnabled_Sets_Modifier()
    {
        var el = Button("Y").IsEnabled(false);
        Assert.False(el.Modifiers!.IsEnabled);
        var enabled = Button("Y").IsEnabled();
        Assert.True(enabled.Modifiers!.IsEnabled);
    }

    [Fact]
    [Obsolete("Tests the deprecated Disabled shim")]
    public void Disabled_Shim_Inverts_To_IsEnabled()
    {
        var el = Button("Y").Disabled();
        Assert.False(el.Modifiers!.IsEnabled);
        var enabled = Button("Y").Disabled(false);
        Assert.True(enabled.Modifiers!.IsEnabled);
    }

    // ────────────────────────────────────────────────────────────────
    //  Sugar — text / textfield / shapes / popup
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void TextBlockElement_Sugar_NonWinUI_Modifiers()
    {
        // Skip Bold/SemiBold/FontFamily — those allocate WinRT FontWeight/FontFamily
        // and only succeed inside an Application host (selftest fixtures).
        var el = TextBlock("x")
            .FontSize(14)
            .TextWrapping()
            .TextAlignment(TextAlignment.Center)
            .TextTrimming(TextTrimming.CharacterEllipsis)
            .IsTextSelectionEnabled();
        Assert.Equal(14.0, el.FontSize);
        Assert.Equal(global::Microsoft.UI.Xaml.TextWrapping.Wrap, el.TextWrapping);
        Assert.Equal(TextAlignment.Center, el.TextAlignment);
        Assert.Equal(TextTrimming.CharacterEllipsis, el.TextTrimming);
        Assert.True(el.IsTextSelectionEnabled);
    }

    [Fact]
    [Obsolete("Tests the deprecated Selectable shim")]
    public void TextBlock_Selectable_Shim()
    {
        var el = TextBlock("x").Selectable();
        Assert.True(el.IsTextSelectionEnabled);
    }

    [Fact]
    public void TextField_Sugar()
    {
        var el = TextField("x", _ => { })
            .IsReadOnly()
            .AcceptsReturn()
            .TextWrapping()
            .Header("Label");
        Assert.True(el.IsReadOnly);
        Assert.True(el.AcceptsReturn);
        Assert.Equal(global::Microsoft.UI.Xaml.TextWrapping.Wrap, el.TextWrapping);
        Assert.Equal("Label", el.Header);
    }

    // Path.StrokeDashArray uses DoubleCollection (WinUI), tested in selftest.

    [Fact]
    [Obsolete("Tests the deprecated ReadOnly shim for TextField")]
    public void TextField_ReadOnly_Shim()
    {
        var el = TextField("x", _ => { }).ReadOnly();
        Assert.True(el.IsReadOnly);
    }

    [Fact]
    public void Popup_Sugar_IsLightDismissEnabled_And_Offset()
    {
        var el = Popup(TextBlock("x")).IsLightDismissEnabled().Offset(10, 20);
        Assert.True(el.IsLightDismissEnabled);
        Assert.Equal(10, el.HorizontalOffset);
        Assert.Equal(20, el.VerticalOffset);
    }

    [Fact]
    public void Shape_StrokeThickness_NoBrush_Modifiers()
    {
        // Brush-typed Fill/Stroke require WinUI activation; the StrokeThickness
        // modifier is independent and exercises the `el with` body.
        var pa = Path2D().StrokeThickness(3);
        Assert.Equal(3.0, pa.StrokeThickness);
        var li = Line(0, 0, 1, 1).StrokeThickness(2);
        Assert.Equal(2.0, li.StrokeThickness);
    }

    // ────────────────────────────────────────────────────────────────
    //  Stack / Combo / NumberBox / Slider / Toggle / Rating sugar
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Stack_Spacing_Sugar()
    {
        var el = VStack().Spacing(8);
        Assert.Equal(8.0, el.Spacing);
    }

    [Fact]
    public void ComboBox_Sugar()
    {
        var el = ComboBox(new string[] { "a", "b" }, 0, _ => { })
            .Placeholder("Pick…")
            .IsEditable()
            .Header("Lbl");
        Assert.Equal("Pick…", el.PlaceholderText);
        Assert.True(el.IsEditable);
        Assert.Equal("Lbl", el.Header);
    }

    [Fact]
    [Obsolete("Tests the deprecated Editable shim")]
    public void ComboBox_Editable_Shim()
    {
        var el = ComboBox(new string[] { "a" }, 0, _ => { }).Editable();
        Assert.True(el.IsEditable);
    }

    [Fact]
    public void NumberBox_Range_And_SpinButtons()
    {
        var el = NumberBox(5, _ => { }).Range(0, 10).SpinButtons();
        Assert.Equal(0, el.Minimum);
        Assert.Equal(10, el.Maximum);
        Assert.Equal(NumberBoxSpinButtonPlacementMode.Inline, el.SpinButtonPlacement);
    }

    [Fact]
    public void Slider_Sugar()
    {
        var el = Slider(0, onValueChanged: _ => { }).StepFrequency(0.5).Header("Vol");
        Assert.Equal(0.5, el.StepFrequency);
        Assert.Equal("Vol", el.Header);
    }

    [Fact]
    public void ToggleSwitch_Header_Sugar()
    {
        var el = ToggleSwitch(false, _ => { }).Header("On?");
        Assert.Equal("On?", el.Header);
    }

    [Fact]
    public void RatingControl_MaxRating_And_IsReadOnly()
    {
        var el = RatingControl(0, _ => { }).MaxRating(10).IsReadOnly();
        Assert.Equal(10, el.MaxRating);
        Assert.True(el.IsReadOnly);
    }

    [Fact]
    [Obsolete("Tests the deprecated ReadOnly shim for RatingControl")]
    public void RatingControl_ReadOnly_Shim()
    {
        var el = RatingControl(0, _ => { }).ReadOnly();
        Assert.True(el.IsReadOnly);
    }

    [Fact]
    public void InfoBar_Severity_And_IsClosable()
    {
        var el = InfoBar("Title", "Msg").Severity(InfoBarSeverity.Warning).IsClosable(false);
        Assert.Equal(InfoBarSeverity.Warning, el.Severity);
        Assert.False(el.IsClosable);
    }

    [Fact]
    [Obsolete("Tests the deprecated Closable shim")]
    public void InfoBar_Closable_Shim()
    {
        var el = InfoBar("Title", "Msg").Closable(false);
        Assert.False(el.IsClosable);
    }

    [Fact]
    public void NavigationView_PaneDisplayMode_PaneTitle()
    {
        var el = NavigationView(new NavigationViewItemData[0]).PaneDisplayMode(NavigationViewPaneDisplayMode.LeftCompact).PaneTitle("App");
        Assert.Equal(NavigationViewPaneDisplayMode.LeftCompact, el.PaneDisplayMode);
        Assert.Equal("App", el.PaneTitle);
    }

    [Fact]
    public void TitleBar_Subtitle_Sugar()
    {
        var el = TitleBar("Title").Subtitle("Sub");
        Assert.Equal("Sub", el.Subtitle);
    }

    [Fact]
    public void Expander_Direction_Sugar()
    {
        var el = Expander("Hd", TextBlock("x")).Direction(ExpandDirection.Up);
        Assert.Equal(ExpandDirection.Up, el.ExpandDirection);
    }

    [Fact]
    public void RepeatButton_Delay_Interval()
    {
        var el = RepeatButton("X", () => { }).Delay(200).Interval(50);
        Assert.Equal(200, el.Delay);
        Assert.Equal(50, el.Interval);
    }

    [Fact]
    public void ProgressRing_IsActive_Sugar()
    {
        var el = ProgressRing().IsActive(false);
        Assert.False(el.IsActive);
    }

    [Fact]
    [Obsolete("Tests the deprecated Active shim")]
    public void ProgressRing_Active_Shim()
    {
        var el = ProgressRing().Active(false);
        Assert.False(el.IsActive);
    }

    [Fact]
    public void PersonPicture_DisplayName_Initials()
    {
        var el = PersonPicture().DisplayName("Jane Doe").Initials("JD");
        Assert.Equal("Jane Doe", el.DisplayName);
        Assert.Equal("JD", el.Initials);
    }

    [Fact]
    public void ListView_GridView_SelectionMode()
    {
        var lv = ListView().SelectionMode(ListViewSelectionMode.Multiple);
        Assert.Equal(ListViewSelectionMode.Multiple, lv.SelectionMode);
        var gv = GridView().SelectionMode(ListViewSelectionMode.Single);
        Assert.Equal(ListViewSelectionMode.Single, gv.SelectionMode);
    }

    [Fact]
    public void TabView_IsAddTabButtonVisible_Sugar()
    {
        var el = TabView([]).IsAddTabButtonVisible(false);
        Assert.False(el.IsAddTabButtonVisible);
    }

    [Fact]
    [Obsolete("Tests the deprecated IsAddButtonVisible shim")]
    public void TabView_IsAddButtonVisible_Shim()
    {
        var el = TabView([]).IsAddButtonVisible(false);
        Assert.False(el.IsAddTabButtonVisible);
    }

    [Fact]
    [Obsolete("Tests the deprecated ShowAddButton shim")]
    public void TabView_ShowAddButton_Shim()
    {
        var el = TabView([]).ShowAddButton(false);
        Assert.False(el.IsAddTabButtonVisible);
    }

    [Fact]
    public void WithKey_Sets_Key_On_Element()
    {
        var el = TextBlock("x").WithKey("k1");
        Assert.Equal("k1", el.Key);
    }

    // ────────────────────────────────────────────────────────────────
    //  FlexElement sugar (FlexPadding overloads)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Flex_Padding_Overloads()
    {
        var u = Flex(TextBlock("x")).FlexPadding(4);
        var hv = Flex(TextBlock("x")).FlexPadding(8.0, 2.0);
        var fs = Flex(TextBlock("x")).FlexPadding(1, 2, 3, 4);
        Assert.Equal(new Thickness(4), u.FlexPadding);
        Assert.Equal(new Thickness(8, 2, 8, 2), hv.FlexPadding);
        Assert.Equal(new Thickness(1, 2, 3, 4), fs.FlexPadding);
    }

    // ────────────────────────────────────────────────────────────────
    //  Set() — typed setter on a sampling of element types
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Set_On_Various_Elements_Adds_To_Setters()
    {
        var t = TextBlock("x").Set(tb => tb.Opacity = 0.9);
        Assert.Single(t.Setters);

        var b = Button("Y").Set(btn => btn.Width = 50);
        Assert.Single(b.Setters);

        var tf = TextField("x", _ => { }).Set(tb => tb.MaxLength = 5);
        Assert.Single(tf.Setters);

        var s = VStack().Set(sp => sp.Spacing = 4);
        Assert.Single(s.Setters);

        var bd = Border(TextBlock("x")).Set(b => b.Padding = new Thickness(1));
        Assert.Single(bd.Setters);

        var ex = Expander("h", TextBlock("x")).Set(e => e.IsExpanded = true);
        Assert.Single(ex.Setters);

        var cb = CheckBox(false, _ => { }).Set(c => c.Content = "Y");
        Assert.Single(cb.Setters);

        var rec = Rectangle().Set(r => r.Width = 5);
        Assert.Single(rec.Setters);

        var lin = Line(0, 0, 1, 1).Set(l => l.X1 = 0);
        Assert.Single(lin.Setters);

        var fl = Flex(TextBlock("x")).Set(_ => { });
        Assert.Single(fl.Setters);
    }

    // ────────────────────────────────────────────────────────────────
    //  Layout / Spring layout / Connected animations
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void LayoutAnimation_Default_Duration_Custom_Spring_AndFullConfig()
    {
        var dft = TextBlock("x").LayoutAnimation();
        Assert.NotNull(dft.LayoutAnimation);

        var dur = TextBlock("x").LayoutAnimation(TimeSpan.FromMilliseconds(150));
        Assert.Equal(TimeSpan.FromMilliseconds(150), dur.LayoutAnimation!.Duration);

        var spring = TextBlock("x").SpringLayoutAnimation(0.7f, 0.05f);
        Assert.True(spring.LayoutAnimation!.UseSpring);
        Assert.Equal(0.7f, spring.LayoutAnimation.DampingRatio);

        var custom = TextBlock("x").LayoutAnimation(new LayoutAnimationConfig { Duration = TimeSpan.FromSeconds(1) });
        Assert.Equal(TimeSpan.FromSeconds(1), custom.LayoutAnimation!.Duration);
    }

    [Fact]
    public void ConnectedAnimation_Sets_Key()
    {
        var el = TextBlock("x").ConnectedAnimation("hero");
        Assert.Equal("hero", el.ConnectedAnimationKey);
    }

    // ────────────────────────────────────────────────────────────────
    //  Implicit transitions
    // ────────────────────────────────────────────────────────────────

    // Implicit transitions and ThemeTransitions allocate WinUI Composition /
    // Theme transition objects — covered in the selftest fixture.

    // ────────────────────────────────────────────────────────────────
    //  ScrollView modifiers
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ScrollView_Modifiers()
    {
        var sv = ScrollViewer(TextBlock("x"))
            .ZoomMode(ZoomMode.Enabled)
            .HorizontalScrollMode(ScrollMode.Enabled)
            .VerticalScrollMode(ScrollMode.Auto);
        Assert.Equal(ZoomMode.Enabled, sv.ZoomMode);
        Assert.Equal(ScrollMode.Enabled, sv.HorizontalScrollMode);
        Assert.Equal(ScrollMode.Auto, sv.VerticalScrollMode);
    }

    // ────────────────────────────────────────────────────────────────
    //  Resources / lightweight styling
    // ────────────────────────────────────────────────────────────────

    // Resources(builder.Set("...", "color")) eagerly parses brushes and needs
    // WinUI activation — covered in the selftest fixture.

    // ────────────────────────────────────────────────────────────────
    //  Automation / Sound / OnMount
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Automation_And_Sound_And_OnMount_Modifiers()
    {
        var el = TextBlock("x")
            .AutomationName("a")
            .AutomationId("id1")
            .SoundMode(ElementSoundMode.Off)
            .OnMount(_ => { });
        Assert.Equal("a", el.Modifiers!.AutomationName);
        Assert.Equal("id1", el.Modifiers.AutomationId);
        Assert.Equal(ElementSoundMode.Off, el.Modifiers.ElementSoundMode);
        Assert.NotNull(el.Modifiers.OnMountAction);
    }

    // ────────────────────────────────────────────────────────────────
    //  Accessibility (Tier 1 + Tier 2/3)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void A11y_Tier1_Modifiers()
    {
        var el = Button("Y")
            .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2)
            .IsTabStop(false)
            .TabIndex(3)
            .AccessKey("F");
        Assert.Equal(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2, el.Modifiers!.HeadingLevel);
        Assert.False(el.Modifiers.IsTabStop);
        Assert.Equal(3, el.Modifiers.TabIndex);
        Assert.Equal("F", el.Modifiers.AccessKey);
    }

    [Fact]
    public void A11y_Tier2_Modifiers()
    {
        var el = TextBlock("x")
            .HelpText("hint")
            .FullDescription("desc")
            .Landmark(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Main)
            .AccessibilityView(Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw)
            .AccessibilityHidden()
            .Required()
            .LiveRegion(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive)
            .PositionInSet(2, 5)
            .HierarchyLevel(1)
            .ItemStatus("3 unread")
            .LabeledBy("Lbl")
            .TabNavigation(Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once);

        var a = el.Modifiers!.Accessibility!;
        Assert.Equal("hint", a.HelpText);
        Assert.Equal("desc", a.FullDescription);
        Assert.Equal(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Main, a.LandmarkType);
        Assert.Equal(Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw, a.AccessibilityView);
        Assert.True(a.IsRequiredForForm);
        Assert.Equal(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive, a.LiveSetting);
        Assert.Equal(2, a.PositionInSet);
        Assert.Equal(5, a.SizeOfSet);
        Assert.Equal(1, a.Level);
        Assert.Equal("3 unread", a.ItemStatus);
        Assert.Equal("Lbl", a.LabeledBy);
        Assert.Equal(Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once, a.TabFocusNavigation);
    }

    [Fact]
    public void Semantics_Wraps_Element_With_SemanticElement()
    {
        var inner = (Element)TextBlock("x");
        var s = inner.Semantics(role: "slider", value: "3", rangeMin: 0, rangeMax: 5, rangeValue: 3);
        Assert.IsType<SemanticElement>(s);
        Assert.Same(inner, s.Child);
        Assert.Equal("slider", s.Semantics.Role);
        Assert.Equal("3", s.Semantics.Value);
        Assert.Equal(0, s.Semantics.RangeMin);
        Assert.Equal(5, s.Semantics.RangeMax);
        Assert.Equal(3, s.Semantics.RangeValue);
    }

    // ────────────────────────────────────────────────────────────────
    //  Animate / Stagger / Keyframes
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Animate_Sets_AnimationConfig()
    {
        var el = TextBlock("x").Animate(Curve.Linear(200));
        Assert.NotNull(el.AnimationConfig);
    }

    [Fact]
    public void Stagger_Sets_StaggerConfig()
    {
        var el = VStack().Stagger(TimeSpan.FromMilliseconds(50));
        Assert.NotNull(el.StaggerConfig);
        Assert.Equal(TimeSpan.FromMilliseconds(50), el.StaggerConfig!.Delay);
    }

    [Fact]
    public void Keyframes_Adds_Entry()
    {
        var el = TextBlock("x").Keyframes("test", trigger: 1, kf => kf.At(1f, opacity: 1f));
        Assert.NotNull(el.KeyframeAnimations);
        Assert.Single(el.KeyframeAnimations!);
        Assert.Equal("test", el.KeyframeAnimations![0].Name);
    }
}
