using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Issue #308 — every factory method should be usable with a record
/// <c>with</c>-expression on the return value, so callers can mutate
/// properties without an unchecked cast and without the perf cost of
/// fluent extension methods.
///
/// Each fact: (1) calls a factory, (2) uses <c>with { ... }</c> to set
/// an init-only property on the returned record, (3) asserts that the
/// concrete type was preserved and the property was updated.
///
/// Compile failure of any test below is the signal that a factory has
/// regressed to a base/erased return type.
/// </summary>
public class FactoryWithExpressionTests
{
    // ════════════════════════════════════════════════════════════════
    //  Text
    // ════════════════════════════════════════════════════════════════

    [Fact] public void TextBlock_WithExpr_Sets_Property()
        => Assert.Equal(22, (TextBlock("hi") with { FontSize = 22 }).FontSize);

    [Fact] public void Heading_WithExpr_Sets_Property()
        => Assert.Equal(36, (Heading("hi") with { FontSize = 36 }).FontSize);

    [Fact] public void SubHeading_WithExpr_Sets_Property()
        => Assert.Equal(24, (SubHeading("hi") with { FontSize = 24 }).FontSize);

    [Fact] public void Caption_WithExpr_Sets_Property()
        => Assert.Equal(14, (Caption("hi") with { FontSize = 14 }).FontSize);

    [Fact] public void RichTextBlock_String_WithExpr_Sets_Key()
        => Assert.Equal("k", (RichTextBlock("hi") with { Key = "k" }).Key);

    [Fact] public void RichTextBlock_Paragraphs_WithExpr_Sets_Key()
        => Assert.Equal("k", (RichTextBlock(new[] { Paragraph(Run("a")) }) with { Key = "k" }).Key);

    [Fact] public void RichEditBox_WithExpr_Sets_Key()
        => Assert.Equal("k", (RichEditBox("hi") with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Buttons
    // ════════════════════════════════════════════════════════════════

    [Fact] public void Button_WithExpr_Sets_Property()
        => Assert.False((Button("Go") with { IsEnabled = false }).IsEnabled);

    [Fact] public void Button_Content_WithExpr_Sets_Key()
        => Assert.Equal("k", (Button(TextBlock("Go")) with { Key = "k" }).Key);

    [Fact] public void HyperlinkButton_WithExpr_Sets_Key()
        => Assert.Equal("k", (HyperlinkButton("hi") with { Key = "k" }).Key);

    [Fact] public void RepeatButton_WithExpr_Sets_Key()
        => Assert.Equal("k", (RepeatButton("hi") with { Key = "k" }).Key);

    [Fact] public void ToggleButton_WithExpr_Sets_Property()
        => Assert.True((ToggleButton("hi") with { IsChecked = true }).IsChecked);

    [Fact] public void ThreeStateToggleButton_WithExpr_Sets_Key()
        => Assert.Equal("k", (ThreeStateToggleButton("hi") with { Key = "k" }).Key);

    [Fact] public void DropDownButton_WithExpr_Sets_Key()
        => Assert.Equal("k", (DropDownButton("hi") with { Key = "k" }).Key);

    [Fact] public void SplitButton_WithExpr_Sets_Key()
        => Assert.Equal("k", (SplitButton("hi") with { Key = "k" }).Key);

    [Fact] public void ToggleSplitButton_WithExpr_Sets_Key()
        => Assert.Equal("k", (ToggleSplitButton("hi") with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Input controls
    // ════════════════════════════════════════════════════════════════

    [Fact] public void TextField_WithExpr_Sets_Property()
        => Assert.Equal("hint", (TextField("v") with { Placeholder = "hint" }).Placeholder);

    [Fact] public void PasswordBox_WithExpr_Sets_Key()
        => Assert.Equal("k", (PasswordBox("p") with { Key = "k" }).Key);

    [Fact] public void NumberBox_WithExpr_Sets_Key()
        => Assert.Equal("k", (NumberBox(1.0) with { Key = "k" }).Key);

    [Fact] public void AutoSuggestBox_WithExpr_Sets_Key()
        => Assert.Equal("k", (AutoSuggestBox("v") with { Key = "k" }).Key);

    [Fact] public void CheckBox_WithExpr_Sets_Property()
        => Assert.Equal("hi", (CheckBox(false) with { Label = "hi" }).Label);

    [Fact] public void ThreeStateCheckBox_WithExpr_Sets_Key()
        => Assert.Equal("k", (ThreeStateCheckBox(null) with { Key = "k" }).Key);

    [Fact] public void RadioButton_WithExpr_Sets_Property()
        => Assert.True((RadioButton("hi") with { IsChecked = true }).IsChecked);

    [Fact] public void RadioButtons_WithExpr_Sets_Property()
        => Assert.Equal(1, (RadioButtons(new[] { "a", "b" }) with { SelectedIndex = 1 }).SelectedIndex);

    [Fact] public void ComboBox_Strings_WithExpr_Sets_Property()
        => Assert.Equal(1, (ComboBox(new[] { "a", "b" }) with { SelectedIndex = 1 }).SelectedIndex);

    [Fact] public void ComboBox_Elements_WithExpr_Sets_Key()
        => Assert.Equal("k", (ComboBox(new Element[] { TextBlock("a") }, 0, null) with { Key = "k" }).Key);

    [Fact] public void Slider_WithExpr_Sets_Property()
        => Assert.Equal(7.0, (Slider(0) with { TickFrequency = 7.0 }).TickFrequency);

    [Fact] public void ToggleSwitch_WithExpr_Sets_Property()
        => Assert.Equal("ON", (ToggleSwitch(false) with { OnContent = "ON" }).OnContent);

    [Fact] public void RatingControl_WithExpr_Sets_Key()
        => Assert.Equal("k", (RatingControl(2) with { Key = "k" }).Key);

    [Fact] public void ColorPicker_WithExpr_Sets_Key()
        => Assert.Equal("k", (ColorPicker(global::Windows.UI.Color.FromArgb(255,0,0,0)) with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Date / time
    // ════════════════════════════════════════════════════════════════

    [Fact] public void CalendarDatePicker_WithExpr_Sets_Key()
        => Assert.Equal("k", (CalendarDatePicker(DateTimeOffset.Now) with { Key = "k" }).Key);

    [Fact] public void DatePicker_WithExpr_Sets_Key()
        => Assert.Equal("k", (DatePicker(DateTimeOffset.Now) with { Key = "k" }).Key);

    [Fact] public void TimePicker_WithExpr_Sets_Key()
        => Assert.Equal("k", (TimePicker(TimeSpan.Zero) with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Progress / status
    // ════════════════════════════════════════════════════════════════

    [Fact] public void Progress_WithExpr_Sets_Key()
        => Assert.Equal("k", (Progress(0.5) with { Key = "k" }).Key);

    [Fact] public void ProgressIndeterminate_WithExpr_Sets_Key()
        => Assert.Equal("k", (ProgressIndeterminate() with { Key = "k" }).Key);

    [Fact] public void ProgressRing_WithExpr_Sets_Key()
        => Assert.Equal("k", (ProgressRing() with { Key = "k" }).Key);

    [Fact] public void ProgressRing_Value_WithExpr_Sets_Key()
        => Assert.Equal("k", (ProgressRing(0.5) with { Key = "k" }).Key);

    [Fact] public void InfoBar_WithExpr_Sets_Key()
        => Assert.Equal("k", (InfoBar("title") with { Key = "k" }).Key);

    [Fact] public void InfoBadge_WithExpr_Sets_Property()
        => Assert.Equal(7, (InfoBadge() with { Value = 7 }).Value);

    [Fact] public void InfoBadge_Value_WithExpr_Sets_Property()
        => Assert.Equal(8, (InfoBadge(1) with { Value = 8 }).Value);

    // ════════════════════════════════════════════════════════════════
    //  Layout
    // ════════════════════════════════════════════════════════════════

    [Fact] public void VStack_WithExpr_Sets_Property()
        => Assert.Equal(16.0, (VStack() with { Spacing = 16.0 }).Spacing);

    [Fact] public void VStack_Spacing_WithExpr_Sets_Key()
        => Assert.Equal("k", (VStack(4) with { Key = "k" }).Key);

    [Fact] public void HStack_WithExpr_Sets_Property()
        => Assert.Equal(16.0, (HStack() with { Spacing = 16.0 }).Spacing);

    [Fact] public void HStack_Spacing_WithExpr_Sets_Key()
        => Assert.Equal("k", (HStack(4) with { Key = "k" }).Key);

    [Fact] public void WrapGrid_WithExpr_Sets_Property()
        => Assert.Equal(5, (WrapGrid() with { MaximumRowsOrColumns = 5 }).MaximumRowsOrColumns);

    [Fact] public void WrapGrid_Max_WithExpr_Sets_Property()
        => Assert.Equal(6, (WrapGrid(3) with { MaximumRowsOrColumns = 6 }).MaximumRowsOrColumns);

    [Fact] public void ScrollView_WithExpr_Sets_Key()
        => Assert.Equal("k", (ScrollViewer(TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void Border_WithExpr_Sets_Key()
        => Assert.Equal("k", (Border(TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void Expander_WithExpr_Sets_Key()
        => Assert.Equal("k", (Expander("h", TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void SplitView_WithExpr_Sets_Key()
        => Assert.Equal("k", (SplitView() with { Key = "k" }).Key);

    [Fact] public void Viewbox_WithExpr_Sets_Key()
        => Assert.Equal("k", (Viewbox(TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void Canvas_WithExpr_Sets_Key()
        => Assert.Equal("k", (Canvas() with { Key = "k" }).Key);

    [Fact] public void Flex_WithExpr_Sets_Property()
        => Assert.Equal(Microsoft.UI.Reactor.Layout.FlexDirection.Column,
            (Flex() with { Direction = Microsoft.UI.Reactor.Layout.FlexDirection.Column }).Direction);

    [Fact] public void Flex_Direction_WithExpr_Sets_Key()
        => Assert.Equal("k", (Flex(Microsoft.UI.Reactor.Layout.FlexDirection.Row) with { Key = "k" }).Key);

    [Fact] public void FlexRow_WithExpr_Sets_Key()
        => Assert.Equal("k", (FlexRow() with { Key = "k" }).Key);

    [Fact] public void FlexColumn_WithExpr_Sets_Key()
        => Assert.Equal("k", (FlexColumn() with { Key = "k" }).Key);

    [Fact] public void Grid_Typed_WithExpr_Sets_Key()
        => Assert.Equal("k", (Grid(new[] { GridSize.Auto }, new[] { GridSize.Auto }) with { Key = "k" }).Key);

#pragma warning disable CS0618 // deprecated overload — still exercise the with-mutation path
    [Fact] public void Grid_StringTracks_WithExpr_Sets_Key()
        => Assert.Equal("k", (Grid(new[] { "Auto" }, new[] { "Auto" }) with { Key = "k" }).Key);
#pragma warning restore CS0618

    [Fact] public void InterspersedGrid_WithExpr_Sets_Key()
        => Assert.Equal("k", (InterspersedGrid(
                Orientation.Horizontal,
                new[] { (Element)TextBlock("a"), TextBlock("b") },
                new[] { 1.0, 1.0 },
                4,
                _ => Border(null)) with { Key = "k" }).Key);

    [Fact] public void UniformGrid_WithExpr_Sets_Key()
        => Assert.Equal("k", (UniformGrid(Orientation.Horizontal, TextBlock("a")) with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Navigation
    // ════════════════════════════════════════════════════════════════

    [Fact] public void NavigationView_WithExpr_Sets_Key()
        => Assert.Equal("k", (NavigationView(Array.Empty<NavigationViewItemData>()) with { Key = "k" }).Key);

    [Fact] public void NavItem_WithExpr_Sets_Property()
        => Assert.Equal("ic", (NavItem("c") with { Icon = "ic" }).Icon);

    [Fact] public void NavItemHeader_WithExpr_Sets_Property()
        => Assert.True((NavItemHeader("c") with { IsHeader = true }).IsHeader);

    [Fact] public void TitleBar_WithExpr_Sets_Key()
        => Assert.Equal("k", (TitleBar("t") with { Key = "k" }).Key);

    [Fact] public void TabView_WithExpr_Sets_Key()
        => Assert.Equal("k", (TabView() with { Key = "k" }).Key);

    [Fact] public void Tab_WithExpr_Sets_Property()
        => Assert.Equal("new", (Tab("h", TextBlock("c")) with { Header = "new" }).Header);

    [Fact] public void BreadcrumbBar_WithExpr_Sets_Key()
        => Assert.Equal("k", (BreadcrumbBar(Array.Empty<BreadcrumbBarItemData>()) with { Key = "k" }).Key);

    [Fact] public void Breadcrumb_WithExpr_Sets_Property()
        => Assert.Equal("new", (Breadcrumb("l") with { Label = "new" }).Label);

    [Fact] public void Pivot_WithExpr_Sets_Key()
        => Assert.Equal("k", (Pivot() with { Key = "k" }).Key);

    [Fact] public void PivotItem_WithExpr_Sets_Property()
        => Assert.Equal("new", (PivotItem("h", TextBlock("c")) with { Header = "new" }).Header);

    // ════════════════════════════════════════════════════════════════
    //  Collections
    // ════════════════════════════════════════════════════════════════

    [Fact] public void ListView_Untyped_WithExpr_Sets_Key()
        => Assert.Equal("k", (ListView(TextBlock("a")) with { Key = "k" }).Key);

    [Fact] public void GridView_Untyped_WithExpr_Sets_Key()
        => Assert.Equal("k", (GridView(TextBlock("a")) with { Key = "k" }).Key);

    [Fact] public void TreeView_WithExpr_Sets_Key()
        => Assert.Equal("k", (TreeView() with { Key = "k" }).Key);

    [Fact] public void TreeNode_WithExpr_Sets_Property()
        => Assert.Equal("new", (TreeNode("c") with { Content = "new" }).Content);

    [Fact] public void FlipView_Untyped_WithExpr_Sets_Key()
        => Assert.Equal("k", (FlipView() with { Key = "k" }).Key);

    [Fact] public void ListView_Typed_WithExpr_Sets_Key()
        => Assert.Equal("k", (ListView(new[] { 1, 2 }, i => $"k{i}", (i, _) => TextBlock($"{i}")) with { Key = "k" }).Key);

    [Fact] public void GridView_Typed_WithExpr_Sets_Key()
        => Assert.Equal("k", (GridView(new[] { 1, 2 }, i => $"k{i}", (i, _) => TextBlock($"{i}")) with { Key = "k" }).Key);

    [Fact] public void FlipView_Typed_WithExpr_Sets_Key()
        => Assert.Equal("k", (FlipView(new[] { 1, 2 }, i => $"k{i}", (i, _) => TextBlock($"{i}")) with { Key = "k" }).Key);

    [Fact] public void LazyVStack_WithExpr_Sets_Key()
        => Assert.Equal("k", (LazyVStack(new[] { 1, 2 }, i => $"k{i}", (i, _) => TextBlock($"{i}")) with { Key = "k" }).Key);

    [Fact] public void LazyHStack_WithExpr_Sets_Key()
        => Assert.Equal("k", (LazyHStack(new[] { 1, 2 }, i => $"k{i}", (i, _) => TextBlock($"{i}")) with { Key = "k" }).Key);

    [Fact] public void ItemsView_WithExpr_Sets_Key()
        => Assert.Equal("k", (ItemsView(new[] { 1, 2 }, i => $"k{i}", (i, _) => TextBlock($"{i}")) with { Key = "k" }).Key);

    [Fact] public void SemanticZoom_WithExpr_Sets_Key()
        => Assert.Equal("k", (SemanticZoom(TextBlock("z1"), TextBlock("z2")) with { Key = "k" }).Key);

    [Fact] public void ListBox_WithExpr_Sets_Property()
        => Assert.Equal(1, (ListBox(new[] { "a", "b" }) with { SelectedIndex = 1 }).SelectedIndex);

    [Fact] public void SelectorBar_WithExpr_Sets_Property()
        => Assert.Equal(1, (SelectorBar(new[] { SelectorBarItem("a"), SelectorBarItem("b") }) with { SelectedIndex = 1 }).SelectedIndex);

    [Fact] public void SelectorBarItem_WithExpr_Sets_Property()
        => Assert.Equal("new", (SelectorBarItem("t") with { Text = "new" }).Text);

    [Fact] public void PipsPager_WithExpr_Sets_Property()
        => Assert.Equal(2, (PipsPager(5) with { SelectedPageIndex = 2 }).SelectedPageIndex);

    [Fact] public void AnnotatedScrollBar_WithExpr_Sets_Key()
        => Assert.Equal("k", (AnnotatedScrollBar() with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Dialogs / overlays / popups
    // ════════════════════════════════════════════════════════════════

    [Fact] public void ContentDialog_WithExpr_Sets_Key()
        => Assert.Equal("k", (ContentDialog("t", TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void Flyout_WithExpr_Sets_Key()
        => Assert.Equal("k", (Flyout(TextBlock("t"), TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void TeachingTip_WithExpr_Sets_Key()
        => Assert.Equal("k", (TeachingTip("t") with { Key = "k" }).Key);

    [Fact] public void ContentFlyout_WithExpr_Sets_Property()
        => Assert.Equal(Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
            (ContentFlyout(TextBlock("c")) with { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom }).Placement);

    [Fact] public void MenuItems_WithExpr_Sets_Property()
        => Assert.Equal(Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top,
            (MenuItems() with { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top }).Placement);

    [Fact] public void MenuItems_Placement_WithExpr_Sets_Key()
        => Assert.Equal("k", (MenuItems(Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top) with { Key = "k" }).Key);

    [Fact] public void Popup_WithExpr_Sets_Property()
        => Assert.True((Popup(TextBlock("c")) with { IsOpen = true }).IsOpen);

    [Fact] public void RefreshContainer_WithExpr_Sets_Key()
        => Assert.Equal("k", (RefreshContainer(TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void CommandBarFlyout_WithExpr_Sets_Key()
        => Assert.Equal("k", (CommandBarFlyout(TextBlock("t")) with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Menus
    // ════════════════════════════════════════════════════════════════

    [Fact] public void MenuBar_WithExpr_Sets_Key()
        => Assert.Equal("k", (MenuBar() with { Key = "k" }).Key);

    [Fact] public void Menu_WithExpr_Sets_Property()
        => Assert.Equal("new", (Menu("t") with { Title = "new" }).Title);

    [Fact] public void MenuItem_WithExpr_Sets_Property()
        => Assert.Equal("new", (MenuItem("t") with { Text = "new" }).Text);

    [Fact] public void ToggleMenuItem_WithExpr_Sets_Property()
        => Assert.True((ToggleMenuItem("t") with { IsChecked = true }).IsChecked);

    [Fact] public void RadioMenuItem_WithExpr_Sets_Property()
        => Assert.True((RadioMenuItem("t", "g") with { IsChecked = true }).IsChecked);

    [Fact] public void MenuSeparator_WithExpr_Returns_Same_Type()
        => Assert.IsType<MenuFlyoutSeparatorData>(MenuSeparator() with { });

    [Fact] public void MenuSubItem_WithExpr_Sets_Property()
        => Assert.Equal("new", (MenuSubItem("t") with { Text = "new" }).Text);

    [Fact] public void MenuFlyout_WithExpr_Sets_Key()
        => Assert.Equal("k", (MenuFlyout(TextBlock("t")) with { Key = "k" }).Key);

    [Fact] public void CommandBar_WithExpr_Sets_Key()
        => Assert.Equal("k", (CommandBar() with { Key = "k" }).Key);

    [Fact] public void AppBarButton_WithExpr_Sets_Property()
        => Assert.Equal("new", (AppBarButton("l") with { Label = "new" }).Label);

    [Fact] public void AppBarToggleButton_WithExpr_Sets_Property()
        => Assert.True((AppBarToggleButton("l") with { IsChecked = true }).IsChecked);

    [Fact] public void AppBarSeparator_WithExpr_Returns_Same_Type()
        => Assert.IsType<AppBarSeparatorData>(AppBarSeparator() with { });

    // ════════════════════════════════════════════════════════════════
    //  Media
    // ════════════════════════════════════════════════════════════════

    [Fact] public void Image_WithExpr_Sets_Key()
        => Assert.Equal("k", (Image("u") with { Key = "k" }).Key);

    [Fact] public void PersonPicture_WithExpr_Sets_Key()
        => Assert.Equal("k", (PersonPicture() with { Key = "k" }).Key);

    [Fact] public void WebView2_WithExpr_Sets_Key()
        => Assert.Equal("k", (WebView2() with { Key = "k" }).Key);

    [Fact] public void MediaPlayerElement_WithExpr_Sets_Key()
        => Assert.Equal("k", (MediaPlayerElement() with { Key = "k" }).Key);

    [Fact] public void AnimatedVisualPlayer_WithExpr_Sets_Key()
        => Assert.Equal("k", (AnimatedVisualPlayer() with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Shapes
    // ════════════════════════════════════════════════════════════════

    [Fact] public void Rectangle_WithExpr_Sets_Key()
        => Assert.Equal("k", (Rectangle() with { Key = "k" }).Key);

    [Fact] public void Ellipse_WithExpr_Sets_Key()
        => Assert.Equal("k", (Ellipse() with { Key = "k" }).Key);

    [Fact] public void Line_WithExpr_Sets_Property()
        => Assert.Equal(99.0, (Line(0, 0, 1, 1) with { X2 = 99.0 }).X2);

    [Fact] public void Path2D_WithExpr_Sets_Key()
        => Assert.Equal("k", (Path2D() with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Misc
    // ════════════════════════════════════════════════════════════════

    [Fact] public void RelativePanel_WithExpr_Sets_Key()
        => Assert.Equal("k", (RelativePanel() with { Key = "k" }).Key);

    [Fact] public void CalendarView_WithExpr_Sets_Key()
        => Assert.Equal("k", (CalendarView() with { Key = "k" }).Key);

    [Fact] public void SwipeControl_WithExpr_Sets_Key()
        => Assert.Equal("k", (SwipeControl(TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void AnimatedIcon_WithExpr_Sets_Key()
        => Assert.Equal("k", (AnimatedIcon() with { Key = "k" }).Key);

    [Fact] public void ParallaxView_WithExpr_Sets_Property()
        => Assert.Equal(5.0, (ParallaxView(TextBlock("c")) with { VerticalShift = 5.0 }).VerticalShift);

    [Fact] public void MapControl_WithExpr_Sets_Property()
        => Assert.Equal(4.0, (MapControl() with { ZoomLevel = 4.0 }).ZoomLevel);

    [Fact] public void Frame_WithExpr_Sets_Key()
        => Assert.Equal("k", (Frame() with { Key = "k" }).Key);

    [Fact] public void CommandHost_WithExpr_Sets_Key()
        => Assert.Equal("k", (CommandHost(Array.Empty<global::Microsoft.UI.Reactor.Core.Command>(), TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void ErrorBoundary_FuncFallback_WithExpr_Sets_Key()
        => Assert.Equal("k", (ErrorBoundary(TextBlock("c"), _ => TextBlock("f")) with { Key = "k" }).Key);

    [Fact] public void ErrorBoundary_ElementFallback_WithExpr_Sets_Key()
        => Assert.Equal("k", (ErrorBoundary(TextBlock("c"), TextBlock("f")) with { Key = "k" }).Key);

    [Fact] public void NavigationHost_WithExpr_Sets_Key()
    {
        var stack = new Microsoft.UI.Reactor.Navigation.NavigationStack<string>("root");
        var nav = new Microsoft.UI.Reactor.Navigation.NavigationHandle<string>(stack);
        var host = NavigationHost(nav, r => TextBlock(r));
        Assert.Equal("k", (host with { Key = "k" }).Key);
    }

    // ════════════════════════════════════════════════════════════════
    //  Rich text inline data
    // ════════════════════════════════════════════════════════════════

    [Fact] public void Paragraph_WithExpr_Returns_Same_Type()
        => Assert.IsType<RichTextParagraph>(Paragraph(Run("a")) with { });

    [Fact] public void Run_WithExpr_Sets_Property()
        => Assert.Equal("new", (Run("t") with { Text = "new" }).Text);

    [Fact] public void Hyperlink_WithExpr_Sets_Property()
        => Assert.Equal("new", (Hyperlink("t", new Uri("https://x")) with { Text = "new" }).Text);

    // ════════════════════════════════════════════════════════════════
    //  Icons
    // ════════════════════════════════════════════════════════════════

    [Fact] public void SymbolIcon_WithExpr_Sets_Property()
        => Assert.Equal("new", (SymbolIcon("Home") with { Symbol = "new" }).Symbol);

    [Fact] public void FontIcon_WithExpr_Sets_Property()
        => Assert.Equal("new", (FontIcon("X") with { Glyph = "new" }).Glyph);

    [Fact] public void BitmapIcon_WithExpr_Sets_Property()
        => Assert.False((BitmapIcon(new Uri("https://x")) with { ShowAsMonochrome = false }).ShowAsMonochrome);

    [Fact] public void PathIcon_WithExpr_Sets_Property()
        => Assert.Equal("new", (PathIcon("d") with { Data = "new" }).Data);

    [Fact] public void ImageIcon_WithExpr_Returns_Same_Type()
        => Assert.IsType<ImageIconData>(ImageIcon(new Uri("https://x")) with { });

    [Fact] public void Icon_FromData_WithExpr_Sets_Key()
        => Assert.Equal("k", (Icon(new SymbolIconData("Home")) with { Key = "k" }).Key);

    [Fact] public void Icon_FromSymbol_WithExpr_Sets_Key()
        => Assert.Equal("k", (Icon(Symbol.Home) with { Key = "k" }).Key);

    [Fact] public void Icon_FromString_WithExpr_Sets_Key()
        => Assert.Equal("k", (Icon("Home") with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Named-style factories
    // ════════════════════════════════════════════════════════════════

    [Fact] public void Card_WithExpr_Sets_Key()
        => Assert.Equal("k", (Card(TextBlock("c")) with { Key = "k" }).Key);

    [Fact] public void Title_WithExpr_Sets_Property()
        => Assert.Equal(40, (Title("hi") with { FontSize = 40 }).FontSize);

    [Fact] public void Subtitle_WithExpr_Sets_Key()
        => Assert.Equal("k", (Subtitle("hi") with { Key = "k" }).Key);

    [Fact] public void Body_WithExpr_Sets_Key()
        => Assert.Equal("k", (Body("hi") with { Key = "k" }).Key);

    [Fact] public void BodyStrong_WithExpr_Sets_Key()
        => Assert.Equal("k", (BodyStrong("hi") with { Key = "k" }).Key);

    [Fact] public void BodyLarge_WithExpr_Sets_Key()
        => Assert.Equal("k", (BodyLarge("hi") with { Key = "k" }).Key);

    // ════════════════════════════════════════════════════════════════
    //  Component-wrapped factories — the issue #308 gap.
    //  These returned bare `ComponentElement` before, erasing the typed
    //  Props. Now they return `ComponentElement<TProps>` so a caller can
    //  mutate the typed props directly via a nested with-expression.
    // ════════════════════════════════════════════════════════════════

    private record DemoProps(string Label);
    private sealed class DemoComponent : Component<DemoProps>
    {
        public override Element Render() => new TextBlockElement(Props.Label);
    }

    [Fact]
    public void Component_TProps_Returns_Generic_ComponentElement()
    {
        var el = Component<DemoComponent, DemoProps>(new DemoProps("a"));
        ComponentElement<DemoProps> typed = el; // compile-time check
        Assert.Equal("a", typed.Props.Label);
    }

    [Fact]
    public void Component_TProps_WithExpr_Mutates_Typed_Props()
    {
        var el = Component<DemoComponent, DemoProps>(new DemoProps("a"));
        var mutated = el with { Props = el.Props with { Label = "b" } };

        Assert.Equal("b", mutated.Props.Label);
        // Reconciler reads base.Props (object?) — the redirected init setter
        // must keep both views in sync, otherwise reconciliation breaks.
        Assert.Equal("b", ((DemoProps)((ComponentElement)mutated).Props!).Label);
    }

    [Fact]
    public void Component_TProps_WithKey_Preserves_Generic_Type()
    {
        var el = Component<DemoComponent, DemoProps>(new DemoProps("a"));
        var keyed = el.WithKey("k");

        // WithKey<T> is generic, so the static type round-trips.
        ComponentElement<DemoProps> _ = keyed;
        Assert.Equal("k", keyed.Key);
        Assert.Equal("a", keyed.Props.Label);
    }

    [Fact]
    public void LocaleProvider_Returns_Generic_ComponentElement()
    {
        var el = LocaleProvider("en-US", TextBlock("c"));
        ComponentElement<LocaleProviderElement> typed = el; // compile-time check
        Assert.Equal("en-US", typed.Props.Locale);
    }

    [Fact]
    public void LocaleProvider_WithExpr_Mutates_Typed_Props()
    {
        var el = LocaleProvider("en-US", TextBlock("c"));
        var mutated = el with { Props = el.Props with { Locale = "fr-FR" } };

        Assert.Equal("fr-FR", mutated.Props.Locale);
        Assert.Equal("fr-FR", ((LocaleProviderElement)((ComponentElement)mutated).Props!).Locale);
    }

    // ── DataGrid (the original PR #300 / issue #263 / #308 case) ────

    private record GridRow(int Id, string Name);
    private static ListDataSource<GridRow> MakeSource() =>
        new(new[] { new GridRow(1, "a"), new GridRow(2, "b") }, r => (RowKey)r.Id);
    private static IReadOnlyList<FieldDescriptor> MakeColumns() => new FieldDescriptor[]
    {
        new() { Name = "Id", FieldType = typeof(int), GetValue = o => ((GridRow)o).Id },
        new() { Name = "Name", FieldType = typeof(string), GetValue = o => ((GridRow)o).Name },
    };

    [Fact]
    public void DataGrid_ExplicitColumns_Returns_Generic_ComponentElement()
    {
        var el = DataGrid(MakeSource(), MakeColumns());
        ComponentElement<DataGridElement<GridRow>> typed = el; // compile-time check
        Assert.NotNull(typed.Props.Source);
        Assert.Equal(40, typed.Props.RowHeight);
    }

    [Fact]
    public void DataGrid_ExplicitColumns_WithExpr_Mutates_Typed_Props()
    {
        var el = DataGrid(MakeSource(), MakeColumns());
        // The natural ergonomics the issue describes:
        var mutated = el with { Props = el.Props with { RowHeight = 60, Editable = true } };

        Assert.Equal(60.0, mutated.Props.RowHeight);
        Assert.True(mutated.Props.Editable);

        // Reconciler view stays in sync.
        var basePropsView = (DataGridElement<GridRow>)((ComponentElement)mutated).Props!;
        Assert.Equal(60.0, basePropsView.RowHeight);
        Assert.True(basePropsView.Editable);
    }

    [Fact]
    public void DataGrid_TypeRegistry_Returns_Generic_ComponentElement()
    {
        var registry = new TypeRegistry();
        var el = DataGrid<GridRow>(MakeSource(), registry);
        ComponentElement<DataGridElement<GridRow>> typed = el; // compile-time check
        Assert.Same(registry, typed.Props.Registry);
    }

    [Fact]
    public void DataGrid_WithKey_Preserves_Generic_Type()
    {
        var el = DataGrid(MakeSource(), MakeColumns());
        var keyed = el.WithKey("dg");

        ComponentElement<DataGridElement<GridRow>> _ = keyed;
        Assert.Equal("dg", keyed.Key);
    }

    // ── PropertyGrid ────────────────────────────────────────────────

    [Fact]
    public void PropertyGrid_Returns_Generic_ComponentElement()
    {
        var target = new GridRow(1, "a");
        var registry = new TypeRegistry();
        var el = PropertyGrid(target, registry);
        ComponentElement<PropertyGridElement> typed = el; // compile-time check
        Assert.Same(target, typed.Props.Target);
    }

    [Fact]
    public void PropertyGrid_WithExpr_Mutates_Typed_Props()
    {
        var el = PropertyGrid(new GridRow(1, "a"), new TypeRegistry());
        var mutated = el with { Props = el.Props with { ShowSearch = true } };

        Assert.True(mutated.Props.ShowSearch);
        Assert.True(((PropertyGridElement)((ComponentElement)mutated).Props!).ShowSearch);
    }

    // ── VirtualList ─────────────────────────────────────────────────

    [Fact]
    public void VirtualList_Returns_Generic_ComponentElement()
    {
        var el = VirtualList(10, i => TextBlock($"{i}"));
        ComponentElement<VirtualListElement> typed = el; // compile-time check
        Assert.Equal(10, typed.Props.ItemCount);
    }

    [Fact]
    public void VirtualList_WithExpr_Mutates_Typed_Props()
    {
        var el = VirtualList(10, i => TextBlock($"{i}"));
        var mutated = el with { Props = el.Props with { ItemCount = 50, Spacing = 4.0 } };

        Assert.Equal(50, mutated.Props.ItemCount);
        Assert.Equal(4.0, mutated.Props.Spacing);

        var basePropsView = (VirtualListElement)((ComponentElement)mutated).Props!;
        Assert.Equal(50, basePropsView.ItemCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  ComponentElement<TProps> base/derived contract.
    //
    //  These guard the invariant that lets the reconciler keep working:
    //  any change made through the derived `Props` is observable on the
    //  base `ComponentElement.Props` (object?) slot — and vice versa.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ComponentElement_Generic_Base_And_Derived_Props_Stay_In_Sync()
    {
        var el = Component<DemoComponent, DemoProps>(new DemoProps("a"));
        ComponentElement asBase = el;

        Assert.Same(el.Props, asBase.Props);

        var mutated = el with { Props = new DemoProps("b") };
        Assert.Same(mutated.Props, ((ComponentElement)mutated).Props);
    }

    [Fact]
    public void ComponentElement_Generic_Equality_Compares_Props()
    {
        var a1 = Component<DemoComponent, DemoProps>(new DemoProps("x"));
        var a2 = Component<DemoComponent, DemoProps>(new DemoProps("x"));
        var b  = Component<DemoComponent, DemoProps>(new DemoProps("y"));

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }
}
