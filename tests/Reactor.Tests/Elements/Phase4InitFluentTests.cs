using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 Phase 4 contract tests: each frequently-set init→fluent must
/// produce a record whose init property equals the passed value, and
/// chained fluents must preserve previously-set fluent values (the
/// <c>with</c>-expression semantics guarantee this — these tests pin it).
/// One fact per fluent.
/// </summary>
public class Phase4InitFluentTests
{
    // ── 4.1 Slider ────────────────────────────────────────────────────

    [Fact]
    public void Slider_Orientation_Sets()
    {
        var el = Slider(0).Orientation(Microsoft.UI.Xaml.Controls.Orientation.Vertical);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Vertical, el.Orientation);
    }

    [Fact]
    public void Slider_TickFrequency_Sets()
    {
        var el = Slider(0).TickFrequency(5.0);
        Assert.Equal(5.0, el.TickFrequency);
    }

    [Fact]
    public void Slider_TickPlacement_Sets()
    {
        var el = Slider(0).TickPlacement(TickPlacement.Outside);
        Assert.Equal(TickPlacement.Outside, el.TickPlacement);
    }

    [Fact]
    public void Slider_SnapsTo_Sets()
    {
        var el = Slider(0).SnapsTo(SliderSnapsTo.Ticks);
        Assert.Equal(SliderSnapsTo.Ticks, el.SnapsTo);
    }

    [Fact]
    public void Slider_ThumbToolTip_Sets()
    {
        var el = Slider(0).ThumbToolTip(false);
        Assert.False(el.IsThumbToolTipEnabled);
        Assert.True(Slider(0).ThumbToolTip().IsThumbToolTipEnabled); // default true
    }

    [Fact]
    public void Slider_Chaining_Preserves_Prior_Settings()
    {
        var el = Slider(0)
            .Orientation(Microsoft.UI.Xaml.Controls.Orientation.Vertical)
            .TickFrequency(2.0)
            .TickPlacement(TickPlacement.Outside)
            .SnapsTo(SliderSnapsTo.Ticks)
            .ThumbToolTip(false);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Vertical, el.Orientation);
        Assert.Equal(2.0, el.TickFrequency);
        Assert.Equal(TickPlacement.Outside, el.TickPlacement);
        Assert.Equal(SliderSnapsTo.Ticks, el.SnapsTo);
        Assert.False(el.IsThumbToolTipEnabled);
    }

    // ── 4.2 NumberBox ─────────────────────────────────────────────────

    [Fact]
    public void NumberBox_NumberFormatter_Sets()
    {
        var formatter = new global::Windows.Globalization.NumberFormatting.DecimalFormatter();
        var el = NumberBox(0).NumberFormatter(formatter);
        Assert.Same(formatter, el.NumberFormatter);
    }

    [Fact]
    public void NumberBox_AcceptsExpression_Sets()
    {
        var el = NumberBox(0).AcceptsExpression();
        Assert.True(el.AcceptsExpression);
        Assert.False(NumberBox(0).AcceptsExpression(false).AcceptsExpression);
    }

    [Fact]
    public void NumberBox_ValidationMode_Sets()
    {
        var el = NumberBox(0).ValidationMode(NumberBoxValidationMode.Disabled);
        Assert.Equal(NumberBoxValidationMode.Disabled, el.ValidationMode);
    }

    [Fact]
    public void NumberBox_Description_Sets()
    {
        var el = NumberBox(0).Description("enter a price");
        Assert.Equal("enter a price", el.Description);
    }

    [Fact]
    public void NumberBox_Chaining_Preserves_Prior_Settings()
    {
        var formatter = new global::Windows.Globalization.NumberFormatting.DecimalFormatter();
        var el = NumberBox(0)
            .NumberFormatter(formatter)
            .AcceptsExpression()
            .ValidationMode(NumberBoxValidationMode.Disabled)
            .Description("desc");
        Assert.Same(formatter, el.NumberFormatter);
        Assert.True(el.AcceptsExpression);
        Assert.Equal(NumberBoxValidationMode.Disabled, el.ValidationMode);
        Assert.Equal("desc", el.Description);
    }

    // ── 4.3 ColorPicker ───────────────────────────────────────────────

    private static readonly global::Windows.UI.Color Black = global::Microsoft.UI.Colors.Black;

    [Fact]
    public void ColorPicker_AlphaEnabled_Sets()
    {
        var el = ColorPicker(Black).AlphaEnabled();
        Assert.True(el.IsAlphaEnabled);
        Assert.False(ColorPicker(Black).AlphaEnabled(false).IsAlphaEnabled);
    }

    [Fact]
    public void ColorPicker_MoreButtonVisible_Sets()
    {
        var el = ColorPicker(Black).MoreButtonVisible();
        Assert.True(el.IsMoreButtonVisible);
    }

    [Fact]
    public void ColorPicker_ColorSpectrumVisible_Sets()
    {
        var el = ColorPicker(Black).ColorSpectrumVisible(false);
        Assert.False(el.IsColorSpectrumVisible);
    }

    [Fact]
    public void ColorPicker_ColorSliderVisible_Sets()
    {
        var el = ColorPicker(Black).ColorSliderVisible(false);
        Assert.False(el.IsColorSliderVisible);
    }

    [Fact]
    public void ColorPicker_ColorChannelTextInputVisible_Sets()
    {
        var el = ColorPicker(Black).ColorChannelTextInputVisible(false);
        Assert.False(el.IsColorChannelTextInputVisible);
    }

    [Fact]
    public void ColorPicker_HexInputVisible_Sets()
    {
        var el = ColorPicker(Black).HexInputVisible(false);
        Assert.False(el.IsHexInputVisible);
    }

    [Fact]
    public void ColorPicker_Chaining_Preserves_Prior_Settings()
    {
        var el = ColorPicker(Black)
            .AlphaEnabled()
            .MoreButtonVisible()
            .ColorSpectrumVisible(false)
            .ColorSliderVisible(false)
            .ColorChannelTextInputVisible(false)
            .HexInputVisible(false);
        Assert.True(el.IsAlphaEnabled);
        Assert.True(el.IsMoreButtonVisible);
        Assert.False(el.IsColorSpectrumVisible);
        Assert.False(el.IsColorSliderVisible);
        Assert.False(el.IsColorChannelTextInputVisible);
        Assert.False(el.IsHexInputVisible);
    }

    // ── 4.4 TabView ───────────────────────────────────────────────────

    private static TabViewElement EmptyTabView() => TabView();

    [Fact]
    public void TabView_TabWidthMode_Sets()
    {
        var el = EmptyTabView().TabWidthMode(TabViewWidthMode.Compact);
        Assert.Equal(TabViewWidthMode.Compact, el.TabWidthMode);
    }

    [Fact]
    public void TabView_CloseButtonOverlayMode_Sets()
    {
        var el = EmptyTabView().CloseButtonOverlayMode(TabViewCloseButtonOverlayMode.Always);
        Assert.Equal(TabViewCloseButtonOverlayMode.Always, el.CloseButtonOverlayMode);
    }

    [Fact]
    public void TabView_CanDragTabs_Sets()
    {
        Assert.True(EmptyTabView().CanDragTabs().CanDragTabs);
        Assert.False(EmptyTabView().CanDragTabs(false).CanDragTabs);
    }

    [Fact]
    public void TabView_CanReorderTabs_Sets()
    {
        Assert.True(EmptyTabView().CanReorderTabs().CanReorderTabs);
    }

    [Fact]
    public void TabView_AllowDropTabs_Sets()
    {
        Assert.True(EmptyTabView().AllowDropTabs().AllowDropTabs);
    }

    [Fact]
    public void TabView_TabStripHeader_Sets()
    {
        var header = TextBlock("Header");
        var el = EmptyTabView().TabStripHeader(header);
        Assert.Same(header, el.TabStripHeader);
    }

    [Fact]
    public void TabView_TabStripFooter_Sets()
    {
        var footer = TextBlock("Footer");
        var el = EmptyTabView().TabStripFooter(footer);
        Assert.Same(footer, el.TabStripFooter);
    }

    // ── 4.5 NavigationView ────────────────────────────────────────────

    private static NavigationViewElement EmptyNav() => NavigationView(Array.Empty<NavigationViewItemData>());

    [Fact]
    public void NavigationView_AutoSuggestBox_Sets()
    {
        var box = AutoSuggestBox("query");
        var el = EmptyNav().AutoSuggestBox(box);
        Assert.Same(box, el.AutoSuggestBox);
    }

    [Fact]
    public void NavigationView_PaneFooter_Sets()
    {
        var footer = TextBlock("Footer");
        var el = EmptyNav().PaneFooter(footer);
        Assert.Same(footer, el.PaneFooter);
    }

    [Fact]
    public void NavigationView_PaneCustomContent_Sets()
    {
        var content = TextBlock("Custom");
        var el = EmptyNav().PaneCustomContent(content);
        Assert.Same(content, el.PaneCustomContent);
    }

    [Fact]
    public void NavigationView_OpenPaneLength_Sets()
    {
        var el = EmptyNav().OpenPaneLength(280);
        Assert.Equal(280, el.OpenPaneLength);
    }

    [Fact]
    public void NavigationView_CompactModeThresholdWidth_Sets()
    {
        var el = EmptyNav().CompactModeThresholdWidth(500);
        Assert.Equal(500, el.CompactModeThresholdWidth);
    }

    [Fact]
    public void NavigationView_ExpandedModeThresholdWidth_Sets()
    {
        var el = EmptyNav().ExpandedModeThresholdWidth(1200);
        Assert.Equal(1200, el.ExpandedModeThresholdWidth);
    }

    // ── 4.6 TitleBar ──────────────────────────────────────────────────

    [Fact]
    public void TitleBar_BackButtonVisible_Sets()
    {
        var el = TitleBar("App").BackButtonVisible(true);
        Assert.True(el.IsBackButtonVisible);
    }

    [Fact]
    public void TitleBar_BackButtonEnabled_Sets()
    {
        var el = TitleBar("App").BackButtonEnabled(true);
        Assert.True(el.IsBackButtonEnabled);
    }

    [Fact]
    public void TitleBar_PaneToggleButtonVisible_Sets()
    {
        var el = TitleBar("App").PaneToggleButtonVisible(true);
        Assert.True(el.IsPaneToggleButtonVisible);
    }

    [Fact]
    public void TitleBar_Content_Sets()
    {
        var content = TextBlock("Search");
        var el = TitleBar("App").Content(content);
        Assert.Same(content, el.Content);
    }

    [Fact]
    public void TitleBar_RightHeader_Sets()
    {
        var header = TextBlock("Profile");
        var el = TitleBar("App").RightHeader(header);
        Assert.Same(header, el.RightHeader);
    }

    [Fact]
    public void TitleBar_Icon_IconData_Sets()
    {
        var icon = new SymbolIconData("Home");
        var el = TitleBar("App").Icon(icon);
        Assert.Same(icon, el.Icon);
    }

    [Fact]
    public void TitleBar_Icon_UriString_Sets()
    {
        var el = TitleBar("App").Icon("ms-appx:///Assets/Logo.ico");
        var icon = Assert.IsType<ImageIconData>(el.Icon);
        Assert.Equal(new Uri("ms-appx:///Assets/Logo.ico"), icon.Source);
    }

    // ── 4.7 TextField ─────────────────────────────────────────────────

    [Fact]
    public void TextField_MaxLength_Sets()
    {
        var el = TextField("").MaxLength(32);
        Assert.Equal(32, el.MaxLength);
    }

    [Fact]
    public void TextField_IsSpellCheckEnabled_Sets()
    {
        var el = TextField("").IsSpellCheckEnabled();
        Assert.True(el.IsSpellCheckEnabled);
        Assert.False(TextField("").IsSpellCheckEnabled(false).IsSpellCheckEnabled);
    }

    [Fact]
    public void TextField_CharacterCasing_Sets()
    {
        var el = TextField("").CharacterCasing(Microsoft.UI.Xaml.Controls.CharacterCasing.Upper);
        Assert.Equal(Microsoft.UI.Xaml.Controls.CharacterCasing.Upper, el.CharacterCasing);
    }

    [Fact]
    public void TextField_TextAlignment_Sets()
    {
        var el = TextField("").TextAlignment(Microsoft.UI.Xaml.TextAlignment.Right);
        Assert.Equal(Microsoft.UI.Xaml.TextAlignment.Right, el.TextAlignment);
    }

    [Fact]
    public void TextField_Description_Sets()
    {
        var el = TextField("").Description("enter your name");
        Assert.Equal("enter your name", el.Description);
    }
}
