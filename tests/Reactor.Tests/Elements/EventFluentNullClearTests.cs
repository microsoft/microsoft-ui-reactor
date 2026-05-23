using System;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 §15 Q2 contract test: every event-fluent must treat <c>null</c>
/// as a clear-handler operation. Re-applying a handler after clearing must
/// restore behavior. One test per fluent — kept tight so a regression on
/// any single extension fails in isolation.
///
/// Note (spec 039 §15 Q1): fluents drop the leading <c>On</c> from the
/// property name to avoid the C# property-vs-extension delegate-invocation
/// clash. Property <c>OnClick</c> is reached via <c>.Click(handler)</c>.
/// </summary>
public class EventFluentNullClearTests
{
    private static readonly Action Sentinel = () => { };
    private static readonly Action<bool> SentinelBool = _ => { };
    private static readonly Action<int> SentinelInt = _ => { };
    private static readonly Action<string> SentinelStr = _ => { };
    private static readonly Action<double> SentinelDouble = _ => { };

    // ── §2 Buttons ────────────────────────────────────────────────────

    [Fact]
    public void Button_Click_NullClears()
    {
        var el = Button("x").Click(Sentinel);
        Assert.Same(Sentinel, el.OnClick);
        Assert.Null(el.Click(null).OnClick);
        Assert.Same(Sentinel, el.Click(null).Click(Sentinel).OnClick);
    }

    [Fact]
    public void HyperlinkButton_Click_NullClears()
    {
        var el = HyperlinkButton("x").Click(Sentinel);
        Assert.Same(Sentinel, el.OnClick);
        Assert.Null(el.Click(null).OnClick);
    }

    [Fact]
    public void RepeatButton_Click_NullClears()
    {
        var el = RepeatButton("x").Click(Sentinel);
        Assert.Same(Sentinel, el.OnClick);
        Assert.Null(el.Click(null).OnClick);
    }

    [Fact]
    public void ToggleButton_IsCheckedChanged_NullClears()
    {
        var el = ToggleButton("x").IsCheckedChanged(SentinelBool);
        Assert.Same(SentinelBool, el.OnIsCheckedChanged);
        Assert.Null(el.IsCheckedChanged(null).OnIsCheckedChanged);
    }

    [Fact]
    public void ToggleButton_CheckedStateChanged_NullClears()
    {
        Action<bool?> h = _ => { };
        var el = ToggleButton("x").CheckedStateChanged(h);
        Assert.Same(h, el.OnCheckedStateChanged);
        Assert.Null(el.CheckedStateChanged(null).OnCheckedStateChanged);
    }

    [Fact]
    public void ComboBox_DropDownOpened_NullClears()
    {
        var el = ComboBox(Array.Empty<string>()).DropDownOpened(Sentinel);
        Assert.Same(Sentinel, el.OnDropDownOpened);
        Assert.Null(el.DropDownOpened(null).OnDropDownOpened);
    }

    [Fact]
    public void ComboBox_DropDownClosed_NullClears()
    {
        var el = ComboBox(Array.Empty<string>()).DropDownClosed(Sentinel);
        Assert.Same(Sentinel, el.OnDropDownClosed);
        Assert.Null(el.DropDownClosed(null).OnDropDownClosed);
    }

    [Fact]
    public void ContentDialog_Opened_NullClears()
    {
        var el = new ContentDialogElement("t", new TextBlockElement("c")).Opened(Sentinel);
        Assert.Same(Sentinel, el.OnOpened);
        Assert.Null(el.Opened(null).OnOpened);
    }

    [Fact]
    public void Image_ImageOpened_NullClears()
    {
        var el = Image("x.png").ImageOpened(Sentinel);
        Assert.Same(Sentinel, el.OnImageOpened);
        Assert.Null(el.ImageOpened(null).OnImageOpened);
    }

    [Fact]
    public void Image_ImageFailed_NullClears()
    {
        var el = Image("x.png").ImageFailed(SentinelStr);
        Assert.Same(SentinelStr, el.OnImageFailed);
        Assert.Null(el.ImageFailed(null).OnImageFailed);
    }

    // ── 5.8 Universal collection multi-select snapshot ───────────────

    [Fact]
    public void ListView_SelectionChanged_NullClears()
    {
        global::System.Action<global::System.Collections.Generic.IReadOnlyList<int>> h = _ => { };
        var el = new ListViewElement(Array.Empty<global::Microsoft.UI.Reactor.Core.Element>()).SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    [Fact]
    public void GridView_SelectionChanged_NullClears()
    {
        global::System.Action<global::System.Collections.Generic.IReadOnlyList<int>> h = _ => { };
        var el = new GridViewElement(Array.Empty<global::Microsoft.UI.Reactor.Core.Element>()).SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    [Fact]
    public void ListBox_SelectionChanged_NullClears()
    {
        global::System.Action<global::System.Collections.Generic.IReadOnlyList<int>> h = _ => { };
        var el = ListBox(Array.Empty<string>()).SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    [Fact]
    public void SplitButton_Click_NullClears()
    {
        var el = new SplitButtonElement("x").Click(Sentinel);
        Assert.Same(Sentinel, el.OnClick);
        Assert.Null(el.Click(null).OnClick);
    }

    [Fact]
    public void ToggleSplitButton_IsCheckedChanged_NullClears()
    {
        var el = new ToggleSplitButtonElement("x").IsCheckedChanged(SentinelBool);
        Assert.Same(SentinelBool, el.OnIsCheckedChanged);
        Assert.Null(el.IsCheckedChanged(null).OnIsCheckedChanged);
    }

    // ── §3 Input ──────────────────────────────────────────────────────

    [Fact]
    public void TextBox_Changed_NullClears()
    {
        var el = TextBox("x").Changed(SentinelStr);
        Assert.Same(SentinelStr, el.OnChanged);
        Assert.Null(el.Changed(null).OnChanged);
    }

    [Fact]
    public void TextBox_SelectionChanged_NullClears()
    {
        Action<string, int, int> h = (_, _, _) => { };
        var el = TextBox("x").SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    [Fact]
    public void PasswordBox_PasswordChanged_NullClears()
    {
        var el = PasswordBox("x").PasswordChanged(SentinelStr);
        Assert.Same(SentinelStr, el.OnPasswordChanged);
        Assert.Null(el.PasswordChanged(null).OnPasswordChanged);
    }

    [Fact]
    public void NumberBox_ValueChanged_NullClears()
    {
        var el = NumberBox(1).ValueChanged(SentinelDouble);
        Assert.Same(SentinelDouble, el.OnValueChanged);
        Assert.Null(el.ValueChanged(null).OnValueChanged);
    }

    [Fact]
    public void AutoSuggestBox_TextChanged_NullClears()
    {
        var el = AutoSuggestBox("x").TextChanged(SentinelStr);
        Assert.Same(SentinelStr, el.OnTextChanged);
        Assert.Null(el.TextChanged(null).OnTextChanged);
    }

    [Fact]
    public void AutoSuggestBox_QuerySubmitted_NullClears()
    {
        var el = AutoSuggestBox("x").QuerySubmitted(SentinelStr);
        Assert.Same(SentinelStr, el.OnQuerySubmitted);
        Assert.Null(el.QuerySubmitted(null).OnQuerySubmitted);
    }

    [Fact]
    public void AutoSuggestBox_SuggestionChosen_NullClears()
    {
        // Spec §3.4 / §14: previously unreachable from any fluent. Asserting
        // it survives the null-clear cycle keeps the newly-exposed surface
        // honest.
        var el = AutoSuggestBox("x").SuggestionChosen(SentinelStr);
        Assert.Same(SentinelStr, el.OnSuggestionChosen);
        Assert.Null(el.SuggestionChosen(null).OnSuggestionChosen);
    }

    [Fact]
    public void CheckBox_IsCheckedChanged_NullClears()
    {
        var el = CheckBox(false).IsCheckedChanged(SentinelBool);
        Assert.Same(SentinelBool, el.OnIsCheckedChanged);
        Assert.Null(el.IsCheckedChanged(null).OnIsCheckedChanged);
    }

    [Fact]
    public void CheckBox_CheckedStateChanged_NullClears()
    {
        Action<bool?> h = _ => { };
        var el = ThreeStateCheckBox(null).CheckedStateChanged(h);
        Assert.Same(h, el.OnCheckedStateChanged);
        Assert.Null(el.CheckedStateChanged(null).OnCheckedStateChanged);
    }

    [Fact]
    public void RadioButton_IsCheckedChanged_NullClears()
    {
        var el = RadioButton("x").IsCheckedChanged(SentinelBool);
        Assert.Same(SentinelBool, el.OnIsCheckedChanged);
        Assert.Null(el.IsCheckedChanged(null).OnIsCheckedChanged);
    }

    [Fact]
    public void RadioButtons_SelectedIndexChanged_NullClears()
    {
        var el = RadioButtons(new[] { "a", "b" }).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void ComboBox_SelectedIndexChanged_NullClears()
    {
        var el = ComboBox(new[] { "a", "b" }).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void Slider_ValueChanged_NullClears()
    {
        var el = Slider(0, 0, 100).ValueChanged(SentinelDouble);
        Assert.Same(SentinelDouble, el.OnValueChanged);
        Assert.Null(el.ValueChanged(null).OnValueChanged);
    }

    [Fact]
    public void ToggleSwitch_IsOnChanged_NullClears()
    {
        var el = ToggleSwitch(false).IsOnChanged(SentinelBool);
        Assert.Same(SentinelBool, el.OnIsOnChanged);
        Assert.Null(el.IsOnChanged(null).OnIsOnChanged);
    }

    [Fact]
    public void RatingControl_ValueChanged_NullClears()
    {
        var el = RatingControl().ValueChanged(SentinelDouble);
        Assert.Same(SentinelDouble, el.OnValueChanged);
        Assert.Null(el.ValueChanged(null).OnValueChanged);
    }

    [Fact]
    public void ColorPicker_ColorChanged_NullClears()
    {
        Action<global::Windows.UI.Color> h = _ => { };
        var el = ColorPicker(global::Windows.UI.Color.FromArgb(255, 0, 0, 0)).ColorChanged(h);
        Assert.Same(h, el.OnColorChanged);
        Assert.Null(el.ColorChanged(null).OnColorChanged);
    }

    [Fact]
    public void RichEditBox_TextChanged_NullClears()
    {
        var el = RichEditBox().TextChanged(SentinelStr);
        Assert.Same(SentinelStr, el.OnTextChanged);
        Assert.Null(el.TextChanged(null).OnTextChanged);
    }

    // ── §4 Date & Time ───────────────────────────────────────────────

    [Fact]
    public void CalendarDatePicker_DateChanged_NullClears()
    {
        Action<DateTimeOffset?> h = _ => { };
        var el = CalendarDatePicker().DateChanged(h);
        Assert.Same(h, el.OnDateChanged);
        Assert.Null(el.DateChanged(null).OnDateChanged);
    }

    [Fact]
    public void DatePicker_DateChanged_NullClears()
    {
        Action<DateTimeOffset> h = _ => { };
        var el = DatePicker(DateTimeOffset.Now).DateChanged(h);
        Assert.Same(h, el.OnDateChanged);
        Assert.Null(el.DateChanged(null).OnDateChanged);
    }

    [Fact]
    public void CalendarView_SelectedDatesChanged_NullClears()
    {
        Action<global::System.Collections.Generic.IReadOnlyList<DateTimeOffset>> h = _ => { };
        var el = CalendarView().SelectedDatesChanged(h);
        Assert.Same(h, el.OnSelectedDatesChanged);
        Assert.Null(el.SelectedDatesChanged(null).OnSelectedDatesChanged);
    }

    [Fact]
    public void Frame_Navigated_NullClears()
    {
        Action<Type> h = _ => { };
        var el = Frame().Navigated(h);
        Assert.Same(h, el.OnNavigated);
        Assert.Null(el.Navigated(null).OnNavigated);
    }

    [Fact]
    public void Frame_Navigating_NullClears()
    {
        Action<Type> h = _ => { };
        var el = Frame().Navigating(h);
        Assert.Same(h, el.OnNavigating);
        Assert.Null(el.Navigating(null).OnNavigating);
    }

    [Fact]
    public void Frame_NavigationFailed_NullClears()
    {
        Action<Type, Exception> h = (_, _) => { };
        var el = Frame().NavigationFailed(h);
        Assert.Same(h, el.OnNavigationFailed);
        Assert.Null(el.NavigationFailed(null).OnNavigationFailed);
    }

    [Fact]
    public void ScrollView_ViewChanged_NullClears()
    {
        Action<Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs> h = _ => { };
        var el = ScrollViewer(TextBlock("x")).ViewChanged(h);
        Assert.Same(h, el.OnViewChanged);
        Assert.Null(el.ViewChanged(null).OnViewChanged);
    }

    [Fact]
    public void Popup_Opened_NullClears()
    {
        Action h = () => { };
        var el = Popup(TextBlock("x")).Opened(h);
        Assert.Same(h, el.OnOpened);
        Assert.Null(el.Opened(null).OnOpened);
    }

    [Fact]
    public void WebView2_WebMessageReceived_NullClears()
    {
        Action<string> h = _ => { };
        var el = WebView2().WebMessageReceived(h);
        Assert.Same(h, el.OnWebMessageReceived);
        Assert.Null(el.WebMessageReceived(null).OnWebMessageReceived);
    }

    [Fact]
    public void WebView2_CoreWebView2Initialized_NullClears()
    {
        Action h = () => { };
        var el = WebView2().CoreWebView2Initialized(h);
        Assert.Same(h, el.OnCoreWebView2Initialized);
        Assert.Null(el.CoreWebView2Initialized(null).OnCoreWebView2Initialized);
    }

    [Fact]
    public void MediaPlayer_MediaOpened_NullClears()
    {
        Action h = () => { };
        var el = MediaPlayerElement().MediaOpened(h);
        Assert.Same(h, el.OnMediaOpened);
        Assert.Null(el.MediaOpened(null).OnMediaOpened);
    }

    [Fact]
    public void MediaPlayer_MediaEnded_NullClears()
    {
        Action h = () => { };
        var el = MediaPlayerElement().MediaEnded(h);
        Assert.Same(h, el.OnMediaEnded);
        Assert.Null(el.MediaEnded(null).OnMediaEnded);
    }

    [Fact]
    public void MediaPlayer_MediaFailed_NullClears()
    {
        Action<string> h = _ => { };
        var el = MediaPlayerElement().MediaFailed(h);
        Assert.Same(h, el.OnMediaFailed);
        Assert.Null(el.MediaFailed(null).OnMediaFailed);
    }

    [Fact]
    public void TimePicker_TimeChanged_NullClears()
    {
        Action<TimeSpan> h = _ => { };
        var el = TimePicker(TimeSpan.Zero).TimeChanged(h);
        Assert.Same(h, el.OnTimeChanged);
        Assert.Null(el.TimeChanged(null).OnTimeChanged);
    }

    // ── §5 InfoBar ────────────────────────────────────────────────────

    [Fact]
    public void InfoBar_ActionButtonClick_NullClears()
    {
        var el = InfoBar().ActionButtonClick(Sentinel);
        Assert.Same(Sentinel, el.OnActionButtonClick);
        Assert.Null(el.ActionButtonClick(null).OnActionButtonClick);
    }

    [Fact]
    public void InfoBar_Closed_NullClears()
    {
        var el = InfoBar().Closed(Sentinel);
        Assert.Same(Sentinel, el.OnClosed);
        Assert.Null(el.Closed(null).OnClosed);
    }

    // ── §6 Layout ─────────────────────────────────────────────────────

    [Fact]
    public void Expander_IsExpandedChanged_NullClears()
    {
        var el = Expander("h", TextBlock("c")).IsExpandedChanged(SentinelBool);
        Assert.Same(SentinelBool, el.OnIsExpandedChanged);
        Assert.Null(el.IsExpandedChanged(null).OnIsExpandedChanged);
    }

    [Fact]
    public void SplitView_PaneOpenChanged_NullClears()
    {
        var el = SplitView(TextBlock("p"), TextBlock("c")).PaneOpenChanged(SentinelBool);
        Assert.Same(SentinelBool, el.OnPaneOpenChanged);
        Assert.Null(el.PaneOpenChanged(null).OnPaneOpenChanged);
    }

    // ── §7 Navigation ─────────────────────────────────────────────────

    [Fact]
    public void NavigationView_SelectedTagChanged_NullClears()
    {
        Action<string?> h = _ => { };
        var el = NavigationView(Array.Empty<NavigationViewItemData>()).SelectedTagChanged(h);
        Assert.Same(h, el.OnSelectedTagChanged);
        Assert.Null(el.SelectedTagChanged(null).OnSelectedTagChanged);
    }

    [Fact]
    public void NavigationView_BackRequested_NullClears()
    {
        var el = NavigationView(Array.Empty<NavigationViewItemData>()).BackRequested(Sentinel);
        Assert.Same(Sentinel, el.OnBackRequested);
        Assert.Null(el.BackRequested(null).OnBackRequested);
    }

    [Fact]
    public void TitleBar_BackRequested_NullClears()
    {
        var el = TitleBar("x").BackRequested(Sentinel);
        Assert.Same(Sentinel, el.OnBackRequested);
        Assert.Null(el.BackRequested(null).OnBackRequested);
    }

    [Fact]
    public void TitleBar_PaneToggleRequested_NullClears()
    {
        var el = TitleBar("x").PaneToggleRequested(Sentinel);
        Assert.Same(Sentinel, el.OnPaneToggleRequested);
        Assert.Null(el.PaneToggleRequested(null).OnPaneToggleRequested);
    }

    [Fact]
    public void TabView_SelectedIndexChanged_NullClears()
    {
        var el = TabView(Array.Empty<TabViewItemData>()).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void TabView_TabCloseRequested_NullClears()
    {
        var el = TabView(Array.Empty<TabViewItemData>()).TabCloseRequested(SentinelInt);
        Assert.Same(SentinelInt, el.OnTabCloseRequested);
        Assert.Null(el.TabCloseRequested(null).OnTabCloseRequested);
    }

    [Fact]
    public void TabView_AddTabButtonClick_NullClears()
    {
        var el = TabView(Array.Empty<TabViewItemData>()).AddTabButtonClick(Sentinel);
        Assert.Same(Sentinel, el.OnAddTabButtonClick);
        Assert.Null(el.AddTabButtonClick(null).OnAddTabButtonClick);
    }

    [Fact]
    public void BreadcrumbBar_ItemClicked_NullClears()
    {
        Action<BreadcrumbBarItemData> h = _ => { };
        var el = BreadcrumbBar(Array.Empty<BreadcrumbBarItemData>()).ItemClicked(h);
        Assert.Same(h, el.OnItemClicked);
        Assert.Null(el.ItemClicked(null).OnItemClicked);
    }

    [Fact]
    public void Pivot_SelectedIndexChanged_NullClears()
    {
        var el = Pivot(Array.Empty<PivotItemData>()).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    // ── §8 Collections ────────────────────────────────────────────────

    [Fact]
    public void ListView_SelectedIndexChanged_NullClears()
    {
        var el = ListView(Array.Empty<Element>()).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void ListView_ItemClick_NullClears()
    {
        var el = ListView(Array.Empty<Element>()).ItemClick(SentinelInt);
        Assert.Same(SentinelInt, el.OnItemClick);
        Assert.Null(el.ItemClick(null).OnItemClick);
    }

    [Fact]
    public void GridView_SelectedIndexChanged_NullClears()
    {
        var el = GridView(Array.Empty<Element>()).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void GridView_ItemClick_NullClears()
    {
        var el = GridView(Array.Empty<Element>()).ItemClick(SentinelInt);
        Assert.Same(SentinelInt, el.OnItemClick);
        Assert.Null(el.ItemClick(null).OnItemClick);
    }

    [Fact]
    public void TreeView_ItemInvoked_NullClears()
    {
        Action<TreeViewNodeData> h = _ => { };
        var el = TreeView(Array.Empty<TreeViewNodeData>()).ItemInvoked(h);
        Assert.Same(h, el.OnItemInvoked);
        Assert.Null(el.ItemInvoked(null).OnItemInvoked);
    }

    [Fact]
    public void TreeView_Expanding_NullClears()
    {
        Action<TreeViewNodeData> h = _ => { };
        var el = TreeView(Array.Empty<TreeViewNodeData>()).Expanding(h);
        Assert.Same(h, el.OnExpanding);
        Assert.Null(el.Expanding(null).OnExpanding);
    }

    [Fact]
    public void FlipView_SelectedIndexChanged_NullClears()
    {
        var el = FlipView(Array.Empty<Element>()).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void ListBox_SelectedIndexChanged_NullClears()
    {
        var el = ListBox(new[] { "a", "b" }).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void TemplatedListView_SelectedIndexChanged_NullClears()
    {
        var el = new TemplatedListViewElement<int>(
            new[] { 1, 2 }, i => i.ToString(), (i, _) => TextBlock(i.ToString()))
            .SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void TemplatedListView_ItemClick_NullClears()
    {
        Action<int> h = _ => { };
        var el = new TemplatedListViewElement<int>(
            new[] { 1, 2 }, i => i.ToString(), (i, _) => TextBlock(i.ToString()))
            .ItemClick(h);
        Assert.Same(h, el.OnItemClick);
        Assert.Null(el.ItemClick(null).OnItemClick);
    }

    // ── §9 Dialogs / overlays ────────────────────────────────────────

    [Fact]
    public void ContentDialog_Closed_NullClears()
    {
        Action<ContentDialogResult> h = _ => { };
        var el = ContentDialog("t", TextBlock("c")).Closed(h);
        Assert.Same(h, el.OnClosed);
        Assert.Null(el.Closed(null).OnClosed);
    }

    [Fact]
    public void Flyout_Opened_NullClears()
    {
        var el = Flyout(TextBlock("t"), TextBlock("c")).Opened(Sentinel);
        Assert.Same(Sentinel, el.OnOpened);
        Assert.Null(el.Opened(null).OnOpened);
    }

    [Fact]
    public void Flyout_Closed_NullClears()
    {
        var el = Flyout(TextBlock("t"), TextBlock("c")).Closed(Sentinel);
        Assert.Same(Sentinel, el.OnClosed);
        Assert.Null(el.Closed(null).OnClosed);
    }

    [Fact]
    public void TeachingTip_ActionButtonClick_NullClears()
    {
        var el = TeachingTip("t").ActionButtonClick(Sentinel);
        Assert.Same(Sentinel, el.OnActionButtonClick);
        Assert.Null(el.ActionButtonClick(null).OnActionButtonClick);
    }

    [Fact]
    public void TeachingTip_Closed_NullClears()
    {
        var el = TeachingTip("t").Closed(Sentinel);
        Assert.Same(Sentinel, el.OnClosed);
        Assert.Null(el.Closed(null).OnClosed);
    }

    [Fact]
    public void Popup_Closed_NullClears()
    {
        var el = Popup(TextBlock("c")).Closed(Sentinel);
        Assert.Same(Sentinel, el.OnClosed);
        Assert.Null(el.Closed(null).OnClosed);
    }

    // ── §10 Media ────────────────────────────────────────────────────

    [Fact]
    public void WebView2_NavigationStarting_NullClears()
    {
        Action<Uri> h = _ => { };
        var el = WebView2().NavigationStarting(h);
        Assert.Same(h, el.OnNavigationStarting);
        Assert.Null(el.NavigationStarting(null).OnNavigationStarting);
    }

    [Fact]
    public void WebView2_NavigationCompleted_NullClears()
    {
        Action<Uri> h = _ => { };
        var el = WebView2().NavigationCompleted(h);
        Assert.Same(h, el.OnNavigationCompleted);
        Assert.Null(el.NavigationCompleted(null).OnNavigationCompleted);
    }

    // ── §12 Niche ────────────────────────────────────────────────────

    [Fact]
    public void SelectorBar_SelectedIndexChanged_NullClears()
    {
        var el = SelectorBar(Array.Empty<SelectorBarItemData>()).SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void PipsPager_SelectedPageIndexChanged_NullClears()
    {
        var el = PipsPager(5).SelectedPageIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedPageIndexChanged);
        Assert.Null(el.SelectedPageIndexChanged(null).OnSelectedPageIndexChanged);
    }

    [Fact]
    public void RefreshContainer_RefreshRequested_NullClears()
    {
        var el = RefreshContainer(TextBlock("c")).RefreshRequested(Sentinel);
        Assert.Same(Sentinel, el.OnRefreshRequested);
        Assert.Null(el.RefreshRequested(null).OnRefreshRequested);
    }

    // ── §13 Specialized ──────────────────────────────────────────────

    [Fact]
    public void AutoSuggest_Selected_NullClears()
    {
        Action<string?> h = _ => { };
        var el = AutoSuggestDsl.AutoSuggest<string>(null).Selected(h);
        Assert.Same(h, el.OnSelected);
        Assert.Null(el.Selected(null).OnSelected);
    }

    [Fact]
    public void MaskedTextField_Changed_NullClears()
    {
        var el = new MaskedTextFieldElement("").Changed(SentinelStr);
        Assert.Same(SentinelStr, el.OnChanged);
        Assert.Null(el.Changed(null).OnChanged);
    }

    [Fact]
    public void VirtualList_VisibleRangeChanged_NullClears()
    {
        Action<int, int> h = (_, _) => { };
        var el = new VirtualListElement { ItemCount = 0, RenderItem = _ => TextBlock("") }
            .VisibleRangeChanged(h);
        Assert.Same(h, el.OnVisibleRangeChanged);
        Assert.Null(el.VisibleRangeChanged(null).OnVisibleRangeChanged);
    }

    private record DataGridTestItem(int Id);

    [Fact]
    public void DataGrid_SelectionChanged_NullClears()
    {
        Action<global::System.Collections.Generic.IReadOnlySet<RowKey>> h = _ => { };
        var source = new ListDataSource<DataGridTestItem>(
            new[] { new DataGridTestItem(1) }, t => (RowKey)t.Id);
        var el = new DataGridElement<DataGridTestItem> { Source = source }
            .SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    // ── Phase 11.3 audit — gaps revealed by cross-referencing CSV inventory ──
    // The reflective surface guard (PublicApiSurfaceGuardTests) ensures every
    // callback property has a fluent. This block ensures every fluent added
    // since Phase 1.6 carries the null-clear fact too. Discovered missing
    // during Phase 11.3 audit: the typed views (TemplatedGridView<T>,
    // TemplatedFlipView<T>, ItemsView<T>), TemplatedListView<T>.SelectionChanged,
    // and PropertyGrid.RootChanged.

    private record NullClearItem(string Key);

    [Fact]
    public void ItemsView_ItemInvoked_NullClears()
    {
        Action<NullClearItem> h = _ => { };
        var el = ItemsView(
                new[] { new NullClearItem("a") },
                it => it.Key,
                (it, _) => TextBlock(it.Key))
            .ItemInvoked(h);
        Assert.Same(h, el.OnItemInvoked);
        Assert.Null(el.ItemInvoked(null).OnItemInvoked);
    }

    [Fact]
    public void ItemsView_SelectionChanged_NullClears()
    {
        Action<global::System.Collections.Generic.IReadOnlyList<NullClearItem>> h = _ => { };
        var el = ItemsView(
                new[] { new NullClearItem("a") },
                it => it.Key,
                (it, _) => TextBlock(it.Key))
            .SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    [Fact]
    public void TemplatedGridView_ItemClick_NullClears()
    {
        Action<NullClearItem> h = _ => { };
        var el = GridView(
                new[] { new NullClearItem("a") },
                it => it.Key,
                (it, _) => TextBlock(it.Key))
            .ItemClick(h);
        Assert.Same(h, el.OnItemClick);
        Assert.Null(el.ItemClick(null).OnItemClick);
    }

    [Fact]
    public void TemplatedGridView_SelectedIndexChanged_NullClears()
    {
        var el = GridView(
                new[] { new NullClearItem("a") },
                it => it.Key,
                (it, _) => TextBlock(it.Key))
            .SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void TemplatedGridView_SelectionChanged_NullClears()
    {
        Action<global::System.Collections.Generic.IReadOnlyList<NullClearItem>> h = _ => { };
        var el = GridView(
                new[] { new NullClearItem("a") },
                it => it.Key,
                (it, _) => TextBlock(it.Key))
            .SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    [Fact]
    public void TemplatedFlipView_SelectedIndexChanged_NullClears()
    {
        var el = FlipView(
                new[] { new NullClearItem("a") },
                it => it.Key,
                (it, _) => TextBlock(it.Key))
            .SelectedIndexChanged(SentinelInt);
        Assert.Same(SentinelInt, el.OnSelectedIndexChanged);
        Assert.Null(el.SelectedIndexChanged(null).OnSelectedIndexChanged);
    }

    [Fact]
    public void TemplatedListView_SelectionChanged_NullClears()
    {
        Action<global::System.Collections.Generic.IReadOnlyList<NullClearItem>> h = _ => { };
        var el = ListView(
                new[] { new NullClearItem("a") },
                it => it.Key,
                (it, _) => TextBlock(it.Key))
            .SelectionChanged(h);
        Assert.Same(h, el.OnSelectionChanged);
        Assert.Null(el.SelectionChanged(null).OnSelectionChanged);
    }

    [Fact]
    public void PropertyGrid_RootChanged_NullClears()
    {
        Action<object> h = _ => { };
        var el = new PropertyGridElement
        {
            Target = new { x = 1 },
            Registry = new TypeRegistry(),
        }.RootChanged(h);
        Assert.Same(h, el.OnRootChanged);
        Assert.Null(el.RootChanged(null).OnRootChanged);
    }
}
