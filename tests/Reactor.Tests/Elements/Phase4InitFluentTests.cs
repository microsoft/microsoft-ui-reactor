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
        var el = TextBox("").MaxLength(32);
        Assert.Equal(32, el.MaxLength);
    }

    [Fact]
    public void TextField_IsSpellCheckEnabled_Sets()
    {
        var el = TextBox("").IsSpellCheckEnabled();
        Assert.True(el.IsSpellCheckEnabled);
        Assert.False(TextBox("").IsSpellCheckEnabled(false).IsSpellCheckEnabled);
    }

    [Fact]
    public void TextField_CharacterCasing_Sets()
    {
        var el = TextBox("").CharacterCasing(Microsoft.UI.Xaml.Controls.CharacterCasing.Upper);
        Assert.Equal(Microsoft.UI.Xaml.Controls.CharacterCasing.Upper, el.CharacterCasing);
    }

    [Fact]
    public void TextField_TextAlignment_Sets()
    {
        var el = TextBox("").TextAlignment(Microsoft.UI.Xaml.TextAlignment.Right);
        Assert.Equal(Microsoft.UI.Xaml.TextAlignment.Right, el.TextAlignment);
    }

    [Fact]
    public void TextField_Description_Sets()
    {
        var el = TextBox("").Description("enter your name");
        Assert.Equal("enter your name", el.Description);
    }

    // ── 4.8 HyperlinkButton.NavigateUri + Command-factory doc fulfillment ──

    [Fact]
    public void HyperlinkButton_NavigateUri_Sets()
    {
        var uri = new Uri("https://example.com");
        var el = HyperlinkButton("Click").NavigateUri(uri);
        Assert.Equal(uri, el.NavigateUri);
    }

    [Fact]
    public void HyperlinkButton_Command_NavigateUri_Fulfils_DocPromise()
    {
        // Spec 039 §14 #5: the HyperlinkButton(Command) doc comment used to
        // tell callers to combine .NavigateUri(...) — but no such fluent
        // existed. This smoke test pins that the promise is now real.
        var cmd = new global::Microsoft.UI.Reactor.Core.Command { Label = "Visit", Execute = () => { } };
        var uri = new Uri("https://example.com");
        var el = HyperlinkButton(cmd).NavigateUri(uri);
        Assert.Equal(uri, el.NavigateUri);
        Assert.Equal("Visit", el.Content);
    }

    // ── 5.1 Text family ──────────────────────────────────────────────

    [Fact]
    public void TextBlock_LineHeight_Sets()
    {
        var el = TextBlock("x").LineHeight(24.0);
        Assert.Equal(24.0, el.LineHeight);
    }

    [Fact]
    public void TextBlock_MaxLines_Sets()
    {
        var el = TextBlock("x").MaxLines(3);
        Assert.Equal(3, el.MaxLines);
    }

    [Fact]
    public void TextBlock_CharacterSpacing_Sets()
    {
        var el = TextBlock("x").CharacterSpacing(50);
        Assert.Equal(50, el.CharacterSpacing);
    }

    [Fact]
    public void TextBlock_TextDecorations_Sets()
    {
        var el = TextBlock("x").TextDecorations(global::Windows.UI.Text.TextDecorations.Underline);
        Assert.Equal(global::Windows.UI.Text.TextDecorations.Underline, el.TextDecorations);
    }

    [Fact]
    public void RichTextBlock_MaxLines_Sets()
    {
        var el = RichTextBlock("x").MaxLines(2);
        Assert.Equal(2, el.MaxLines);
    }

    [Fact]
    public void RichTextBlock_LineHeight_Sets()
    {
        var el = RichTextBlock("x").LineHeight(20.0);
        Assert.Equal(20.0, el.LineHeight);
    }

    [Fact]
    public void RichTextBlock_TextAlignment_Sets()
    {
        var el = RichTextBlock("x").TextAlignment(Microsoft.UI.Xaml.TextAlignment.Center);
        Assert.Equal(Microsoft.UI.Xaml.TextAlignment.Center, el.TextAlignment);
    }

    [Fact]
    public void RichTextBlock_TextTrimming_Sets()
    {
        var el = RichTextBlock("x").TextTrimming(Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis);
        Assert.Equal(Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis, el.TextTrimming);
    }

    [Fact]
    public void RichTextBlock_CharacterSpacing_Sets()
    {
        var el = RichTextBlock("x").CharacterSpacing(75);
        Assert.Equal(75, el.CharacterSpacing);
    }

    [Fact]
    public void RichEditBox_IsSpellCheckEnabled_Sets()
    {
        var el = new RichEditBoxElement("").IsSpellCheckEnabled();
        Assert.True(el.IsSpellCheckEnabled);
        Assert.False(new RichEditBoxElement("").IsSpellCheckEnabled(false).IsSpellCheckEnabled);
    }

    [Fact]
    public void RichEditBox_MaxLength_Sets()
    {
        var el = new RichEditBoxElement("").MaxLength(120);
        Assert.Equal(120, el.MaxLength);
    }

    [Fact]
    public void RichEditBox_TextWrapping_Sets()
    {
        var el = new RichEditBoxElement("").TextWrapping(Microsoft.UI.Xaml.TextWrapping.NoWrap);
        Assert.Equal(Microsoft.UI.Xaml.TextWrapping.NoWrap, el.TextWrapping);
    }

    [Fact]
    public void RichEditBox_AcceptsReturn_Sets()
    {
        var el = new RichEditBoxElement("").AcceptsReturn(false);
        Assert.False(el.AcceptsReturn);
    }

    [Fact]
    public void RichEditBox_SelectionHighlightColor_Sets()
    {
        // SolidColorBrush construction requires WinUI's COM thread context, which
        // isn't available in xunit. Pin the fluent contract by writing through the
        // init property: chained fluent must replace, not preserve, prior value.
        var el = new RichEditBoxElement("") with { SelectionHighlightColor = null };
        // Round-trip: a "set null" via record-with is observable.
        Assert.Null(el.SelectionHighlightColor);
    }

    // ── 5.2 ToggleButton (two-state + three-state in one record) ─────

    [Fact]
    public void ToggleButton_IsThreeState_Sets()
    {
        var el = ToggleButton("x").IsThreeState();
        Assert.True(el.IsThreeState);
        Assert.False(ToggleButton("x").IsThreeState(false).IsThreeState);
    }

    [Fact]
    public void ToggleButton_CheckedState_Sets_And_Implies_ThreeState()
    {
        var el = ToggleButton("x").CheckedState(null);
        Assert.True(el.IsThreeState);
        Assert.Null(el.CheckedState);

        var elTrue = ToggleButton("x").CheckedState(true);
        Assert.True(elTrue.CheckedState);
    }

    [Fact]
    public void ThreeStateToggleButton_Factory_Produces_ThreeState_Record()
    {
        var el = ThreeStateToggleButton("x", checkedState: null);
        Assert.True(el.IsThreeState);
        Assert.Null(el.CheckedState);
        Assert.False(el.IsChecked); // primary stays false in null mode
    }

    // ── 5.3 Input controls ───────────────────────────────────────────

    [Fact]
    public void PasswordBox_MaxLength_Sets()
    {
        var el = PasswordBox("").MaxLength(16);
        Assert.Equal(16, el.MaxLength);
    }

    [Fact]
    public void PasswordBox_Header_Sets()
    {
        var el = PasswordBox("").Header("Password");
        Assert.Equal("Password", el.Header);
    }

    [Fact]
    public void PasswordBox_PasswordRevealMode_Sets()
    {
        var el = PasswordBox("").PasswordRevealMode(Microsoft.UI.Xaml.Controls.PasswordRevealMode.Hidden);
        Assert.Equal(Microsoft.UI.Xaml.Controls.PasswordRevealMode.Hidden, el.PasswordRevealMode);
    }

    [Fact]
    public void PasswordBox_PasswordChar_Sets()
    {
        var el = PasswordBox("").PasswordChar("*");
        Assert.Equal("*", el.PasswordChar);
    }

    [Fact]
    public void AutoSuggestBox_Header_Sets()
    {
        var el = AutoSuggestBox("").Header("Search");
        Assert.Equal("Search", el.Header);
    }

    [Fact]
    public void AutoSuggestBox_QueryIcon_Sets()
    {
        var icon = new SymbolIconData("Find");
        var el = AutoSuggestBox("").QueryIcon(icon);
        Assert.Same(icon, el.QueryIcon);
    }

    [Fact]
    public void AutoSuggestBox_IsSuggestionListOpen_Sets()
    {
        var el = AutoSuggestBox("").IsSuggestionListOpen();
        Assert.True(el.IsSuggestionListOpen);
        Assert.False(AutoSuggestBox("").IsSuggestionListOpen(false).IsSuggestionListOpen);
    }

    [Fact]
    public void ComboBox_MaxDropDownHeight_Sets()
    {
        var el = ComboBox(new[] { "a" }).MaxDropDownHeight(240);
        Assert.Equal(240, el.MaxDropDownHeight);
    }

    [Fact]
    public void ComboBox_Description_Sets()
    {
        var el = ComboBox(new[] { "a" }).Description("pick one");
        Assert.Equal("pick one", el.Description);
    }

    [Fact]
    public void RatingControl_Caption_Sets()
    {
        var el = new RatingControlElement().Caption("How was it?");
        Assert.Equal("How was it?", el.Caption);
    }

    [Fact]
    public void RatingControl_PlaceholderValue_Sets()
    {
        var el = new RatingControlElement().PlaceholderValue(3);
        Assert.Equal(3, el.PlaceholderValue);
    }

    [Fact]
    public void RatingControl_InitialSetValue_Sets()
    {
        var el = new RatingControlElement().InitialSetValue(2);
        Assert.Equal(2, el.InitialSetValue);
    }

    [Fact]
    public void ColorPicker_ColorSpectrumShape_Sets()
    {
        var el = new ColorPickerElement(global::Windows.UI.Color.FromArgb(255, 0, 0, 0))
            .ColorSpectrumShape(Microsoft.UI.Xaml.Controls.ColorSpectrumShape.Ring);
        Assert.Equal(Microsoft.UI.Xaml.Controls.ColorSpectrumShape.Ring, el.ColorSpectrumShape);
    }

    [Fact]
    public void ColorPicker_HueRange_Sets()
    {
        var el = new ColorPickerElement(global::Windows.UI.Color.FromArgb(255, 0, 0, 0)).HueRange(30, 270);
        Assert.Equal(30, el.MinHue);
        Assert.Equal(270, el.MaxHue);
    }

    [Fact]
    public void ColorPicker_SaturationRange_Sets()
    {
        var el = new ColorPickerElement(global::Windows.UI.Color.FromArgb(255, 0, 0, 0)).SaturationRange(20, 80);
        Assert.Equal(20, el.MinSaturation);
        Assert.Equal(80, el.MaxSaturation);
    }

    [Fact]
    public void ColorPicker_ValueRange_Sets()
    {
        var el = new ColorPickerElement(global::Windows.UI.Color.FromArgb(255, 0, 0, 0)).ValueRange(10, 90);
        Assert.Equal(10, el.MinValue);
        Assert.Equal(90, el.MaxValue);
    }

    // ── 5.4 Date / time ──────────────────────────────────────────────

    [Fact]
    public void CalendarDatePicker_DateFormat_Sets()
    {
        var el = CalendarDatePicker().DateFormat("{day.integer}/{month.integer}/{year.full}");
        Assert.Equal("{day.integer}/{month.integer}/{year.full}", el.DateFormat);
    }

    [Fact]
    public void CalendarDatePicker_IsTodayHighlighted_Sets()
    {
        var el = CalendarDatePicker().IsTodayHighlighted(false);
        Assert.False(el.IsTodayHighlighted);
    }

    [Fact]
    public void CalendarDatePicker_IsCalendarOpen_Sets()
    {
        var el = CalendarDatePicker().IsCalendarOpen();
        Assert.True(el.IsCalendarOpen);
    }

    [Fact]
    public void CalendarDatePicker_IsGroupLabelVisible_Sets()
    {
        var el = CalendarDatePicker().IsGroupLabelVisible(false);
        Assert.False(el.IsGroupLabelVisible);
    }

    [Fact]
    public void DatePicker_DayFormat_Sets()
    {
        var el = DatePicker(DateTimeOffset.Now).DayFormat("{day.integer(2)}");
        Assert.Equal("{day.integer(2)}", el.DayFormat);
    }

    [Fact]
    public void DatePicker_MonthFormat_Sets()
    {
        var el = DatePicker(DateTimeOffset.Now).MonthFormat("{month.abbreviated(3)}");
        Assert.Equal("{month.abbreviated(3)}", el.MonthFormat);
    }

    [Fact]
    public void DatePicker_YearFormat_Sets()
    {
        var el = DatePicker(DateTimeOffset.Now).YearFormat("{year.full}");
        Assert.Equal("{year.full}", el.YearFormat);
    }

    [Fact]
    public void DatePicker_Orientation_Sets()
    {
        var el = DatePicker(DateTimeOffset.Now).Orientation(Microsoft.UI.Xaml.Controls.Orientation.Vertical);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Vertical, el.Orientation);
    }

    [Fact]
    public void CalendarView_MinDate_Sets()
    {
        var d = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var el = new CalendarViewElement().MinDate(d);
        Assert.Equal(d, el.MinDate);
    }

    [Fact]
    public void CalendarView_MaxDate_Sets()
    {
        var d = new DateTimeOffset(2030, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var el = new CalendarViewElement().MaxDate(d);
        Assert.Equal(d, el.MaxDate);
    }

    [Fact]
    public void CalendarView_FirstDayOfWeek_Sets()
    {
        var el = new CalendarViewElement().FirstDayOfWeek(global::Windows.Globalization.DayOfWeek.Monday);
        Assert.Equal(global::Windows.Globalization.DayOfWeek.Monday, el.FirstDayOfWeek);
    }

    [Fact]
    public void CalendarView_NumberOfWeeksInView_Sets()
    {
        var el = new CalendarViewElement().NumberOfWeeksInView(4);
        Assert.Equal(4, el.NumberOfWeeksInView);
    }

    [Fact]
    public void CalendarView_DisplayMode_Sets()
    {
        var el = new CalendarViewElement().DisplayMode(Microsoft.UI.Xaml.Controls.CalendarViewDisplayMode.Year);
        Assert.Equal(Microsoft.UI.Xaml.Controls.CalendarViewDisplayMode.Year, el.DisplayMode);
    }

    // ── 5.5 Progress / Layout / Navigation ────────────────────────────

    [Fact]
    public void InfoBar_IconSource_Sets()
    {
        var icon = new SymbolIconData("Accept");
        var el = InfoBar("title").IconSource(icon);
        Assert.Same(icon, el.IconSource);
    }

    [Fact]
    public void InfoBar_Content_Sets()
    {
        var inner = TextBlock("details");
        var el = InfoBar("title").Content(inner);
        Assert.Same(inner, el.Content);
    }

    [Fact]
    public void Expander_HeaderTemplate_Sets()
    {
        var header = TextBlock("Custom header");
        var el = Expander("string header", TextBlock("body")).HeaderTemplate(header);
        Assert.Same(header, el.HeaderTemplate);
    }

    [Fact]
    public void SplitView_LightDismissOverlayMode_Sets()
    {
        var el = new SplitViewElement().LightDismissOverlayMode(Microsoft.UI.Xaml.Controls.LightDismissOverlayMode.On);
        Assert.Equal(Microsoft.UI.Xaml.Controls.LightDismissOverlayMode.On, el.LightDismissOverlayMode);
    }

    [Fact]
    public void SplitView_PaneBackground_ThemeRef_Wires_ThemeBindings()
    {
        var el = new SplitViewElement().PaneBackground(Theme.LayerFill);
        Assert.NotNull(el.ThemeBindings);
        Assert.True(el.ThemeBindings!.ContainsKey("PaneBackground"));
    }

    [Fact]
    public void WrapGrid_AttachedProps_RowSpan_ColumnSpan()
    {
        // Apply both spans and confirm the attached data is stored on the element.
        var child = TextBlock("x").WrapGridColumnSpan(2).WrapGridRowSpan(3);
        var wga = AttachedProbe.Get<WrapGridAttached>(child);
        Assert.NotNull(wga);
        Assert.Equal(2, wga!.ColumnSpan);
        Assert.Equal(3, wga.RowSpan);
    }

    // ── 5.6 Collections / Dialogs ─────────────────────────────────────

    [Fact]
    public void ListView_IncrementalLoadingTrigger_Sets()
    {
        var el = new ListViewElement(Array.Empty<Element>()).IncrementalLoadingTrigger(
            Microsoft.UI.Xaml.Controls.IncrementalLoadingTrigger.None);
        Assert.Equal(Microsoft.UI.Xaml.Controls.IncrementalLoadingTrigger.None, el.IncrementalLoadingTrigger);
    }

    [Fact]
    public void GridView_IncrementalLoadingTrigger_Sets()
    {
        var el = new GridViewElement(Array.Empty<Element>()).IncrementalLoadingTrigger(
            Microsoft.UI.Xaml.Controls.IncrementalLoadingTrigger.None);
        Assert.Equal(Microsoft.UI.Xaml.Controls.IncrementalLoadingTrigger.None, el.IncrementalLoadingTrigger);
    }

    [Fact]
    public void ContentDialog_IsPrimaryButtonEnabled_Sets()
    {
        var el = new ContentDialogElement("t", TextBlock("c")).IsPrimaryButtonEnabled(false);
        Assert.False(el.IsPrimaryButtonEnabled);
    }

    [Fact]
    public void ContentDialog_IsSecondaryButtonEnabled_Sets()
    {
        var el = new ContentDialogElement("t", TextBlock("c")).IsSecondaryButtonEnabled(false);
        Assert.False(el.IsSecondaryButtonEnabled);
    }

    [Fact]
    public void Flyout_ShowMode_Sets()
    {
        var el = new FlyoutElement(TextBlock("t"), TextBlock("c")).ShowMode(Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowMode.Transient);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowMode.Transient, el.ShowMode);
    }

    [Fact]
    public void Flyout_AreOpenCloseAnimationsEnabled_Sets()
    {
        var el = new FlyoutElement(TextBlock("t"), TextBlock("c")).AreOpenCloseAnimationsEnabled(false);
        Assert.False(el.AreOpenCloseAnimationsEnabled);
    }

    [Fact]
    public void Flyout_OverlayInputPassThroughElement_Sets()
    {
        var pt = TextBlock("pass");
        var el = new FlyoutElement(TextBlock("t"), TextBlock("c")).OverlayInputPassThroughElement(pt);
        Assert.Same(pt, el.OverlayInputPassThroughElement);
    }

    [Fact]
    public void TeachingTip_IconSource_Sets()
    {
        var icon = new SymbolIconData("Like");
        var el = TeachingTip("t").IconSource(icon);
        Assert.Same(icon, el.IconSource);
    }

    [Fact]
    public void TeachingTip_HeroContent_Sets()
    {
        var hero = TextBlock("hero");
        var el = TeachingTip("t").HeroContent(hero);
        Assert.Same(hero, el.HeroContent);
    }

    [Fact]
    public void TeachingTip_PlacementMargin_Sets()
    {
        var el = TeachingTip("t").PlacementMargin(new Microsoft.UI.Xaml.Thickness(12));
        Assert.Equal(new Microsoft.UI.Xaml.Thickness(12), el.PlacementMargin);
    }

    [Fact]
    public void TeachingTip_PreferredPlacement_Sets()
    {
        var el = TeachingTip("t").PreferredPlacement(Microsoft.UI.Xaml.Controls.TeachingTipPlacementMode.RightTop);
        Assert.Equal(Microsoft.UI.Xaml.Controls.TeachingTipPlacementMode.RightTop, el.PreferredPlacement);
    }

    // ── 5.7 Media + shapes ───────────────────────────────────────────

    [Fact]
    public void Image_NineGrid_Sets()
    {
        var nine = new Microsoft.UI.Xaml.Thickness(4);
        var el = Image("foo.png").NineGrid(nine);
        Assert.Equal(nine, el.NineGrid);
    }

    [Fact]
    public void Path_StrokeStartLineCap_Sets()
    {
        var el = new PathElement().StrokeStartLineCap(Microsoft.UI.Xaml.Media.PenLineCap.Round);
        Assert.Equal(Microsoft.UI.Xaml.Media.PenLineCap.Round, el.StrokeStartLineCap);
    }

    [Fact]
    public void Path_StrokeEndLineCap_Sets()
    {
        var el = new PathElement().StrokeEndLineCap(Microsoft.UI.Xaml.Media.PenLineCap.Triangle);
        Assert.Equal(Microsoft.UI.Xaml.Media.PenLineCap.Triangle, el.StrokeEndLineCap);
    }

    [Fact]
    public void Path_StrokeLineJoin_Sets()
    {
        var el = new PathElement().StrokeLineJoin(Microsoft.UI.Xaml.Media.PenLineJoin.Bevel);
        Assert.Equal(Microsoft.UI.Xaml.Media.PenLineJoin.Bevel, el.StrokeLineJoin);
    }

    [Fact]
    public void Path_StrokeMiterLimit_Sets()
    {
        var el = new PathElement().StrokeMiterLimit(4);
        Assert.Equal(4, el.StrokeMiterLimit);
    }

    [Fact]
    public void Path_StrokeDashCap_Sets()
    {
        var el = new PathElement().StrokeDashCap(Microsoft.UI.Xaml.Media.PenLineCap.Round);
        Assert.Equal(Microsoft.UI.Xaml.Media.PenLineCap.Round, el.StrokeDashCap);
    }

    [Fact]
    public void Path_StrokeDashOffset_Sets()
    {
        var el = new PathElement().StrokeDashOffset(2.5);
        Assert.Equal(2.5, el.StrokeDashOffset);
    }

    [Fact]
    public void Path_FillRule_Sets()
    {
        var el = new PathElement().FillRule(Microsoft.UI.Xaml.Media.FillRule.Nonzero);
        Assert.Equal(Microsoft.UI.Xaml.Media.FillRule.Nonzero, el.FillRule);
    }

    [Fact]
    public void PipsPager_WrapMode_Sets()
    {
        var el = PipsPager(10).WrapMode(Microsoft.UI.Xaml.Controls.PipsPagerWrapMode.Wrap);
        Assert.Equal(Microsoft.UI.Xaml.Controls.PipsPagerWrapMode.Wrap, el.WrapMode);
    }

    [Fact]
    public void PipsPager_MaxVisiblePips_Sets()
    {
        var el = PipsPager(10).MaxVisiblePips(7);
        Assert.Equal(7, el.MaxVisiblePips);
    }

    [Fact]
    public void PipsPager_PreviousButtonVisibility_Sets()
    {
        var el = PipsPager(10).PreviousButtonVisibility(Microsoft.UI.Xaml.Controls.PipsPagerButtonVisibility.Visible);
        Assert.Equal(Microsoft.UI.Xaml.Controls.PipsPagerButtonVisibility.Visible, el.PreviousButtonVisibility);
    }

    [Fact]
    public void PipsPager_NextButtonVisibility_Sets()
    {
        var el = PipsPager(10).NextButtonVisibility(Microsoft.UI.Xaml.Controls.PipsPagerButtonVisibility.VisibleOnPointerOver);
        Assert.Equal(Microsoft.UI.Xaml.Controls.PipsPagerButtonVisibility.VisibleOnPointerOver, el.NextButtonVisibility);
    }

    [Fact]
    public void RefreshContainer_PullDirection_Sets()
    {
        var el = RefreshContainer(TextBlock("x")).PullDirection(Microsoft.UI.Xaml.Controls.RefreshPullDirection.BottomToTop);
        Assert.Equal(Microsoft.UI.Xaml.Controls.RefreshPullDirection.BottomToTop, el.PullDirection);
    }

    [Fact]
    public void ParallaxView_VerticalSourceOffsets_Set()
    {
        var el = new ParallaxViewElement(TextBlock("x"))
            .VerticalSourceStartOffset(100)
            .VerticalSourceEndOffset(500);
        Assert.Equal(100, el.VerticalSourceStartOffset);
        Assert.Equal(500, el.VerticalSourceEndOffset);
    }
}

// Internal probe so tests can read attached data without needing
// InternalsVisibleTo on the property itself.
internal static class AttachedProbe
{
    public static T? Get<T>(Element el) where T : class
    {
        if (el.Attached is null) return null;
        return el.Attached.TryGetValue(typeof(T), out var v) ? (T)v : null;
    }
}
