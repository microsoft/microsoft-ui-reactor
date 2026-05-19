using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// Event-callback fluent extensions. Spec 039 §0.1 + §14 #1.
//
// Every Action / Action<T> callback property on an element record gets a
// matching fluent extension here that mirrors the WinUI XAML event-name
// convention — the property name with the leading "On" dropped:
//
//   Property OnClick                  → .Click(handler)
//   Property OnTextChanged            → .TextChanged(handler)
//   Property OnSelectedIndexChanged   → .SelectedIndexChanged(handler)
//
// Why the rename? C# binds `el.OnClick(arg)` to property-as-delegate
// invocation (`Action?.Invoke(arg)`) BEFORE considering extension methods,
// so a `.OnClick(...)` extension would be permanently unreachable. The
// WinUI XAML convention (`<Button Click="…"/>`) avoids the clash and reads
// naturally. The properties keep their `OnXxx` names so existing
// property-init syntax (`new ButtonElement(…) { OnClick = … }`) is
// unchanged. See spec 039 §15 Q1 for the full constraint analysis.
//
// Null semantics (spec §15 Q2): passing null clears any previously-set
// handler. Enforced by EventFluentNullClearTests.
//
// Parity (every callback has a fluent) is enforced by the public-API
// surface self-test under tests/Reactor.SelfTests/.
public static partial class ElementExtensions
{
    // ── §2 Buttons ─────────────────────────────────────────────────────

    // <snippet:event-modifier>
    /// <summary>Wires a click handler (sets <see cref="ButtonElement.OnClick"/>). Passing <c>null</c> clears any existing handler.</summary>
    public static ButtonElement Click(this ButtonElement el, Action? handler) =>
        el with { OnClick = handler };

    /// <summary>Wires a click handler (sets <see cref="HyperlinkButtonElement.OnClick"/>). Passing <c>null</c> clears.</summary>
    public static HyperlinkButtonElement Click(this HyperlinkButtonElement el, Action? handler) =>
        el with { OnClick = handler };
    // </snippet:event-modifier>

    /// <summary>Wires a click handler that fires repeatedly while held. Passing <c>null</c> clears.</summary>
    public static RepeatButtonElement Click(this RepeatButtonElement el, Action? handler) =>
        el with { OnClick = handler };

    /// <summary>Wires the toggle-state-changed handler. Passing <c>null</c> clears.</summary>
    public static ToggleButtonElement IsCheckedChanged(this ToggleButtonElement el, Action<bool>? handler) =>
        el with { OnIsCheckedChanged = handler };

    /// <summary>Wires the three-state checked-changed handler (<c>null</c> = indeterminate). Passing <c>null</c> as the handler clears it.</summary>
    public static ToggleButtonElement CheckedStateChanged(this ToggleButtonElement el, Action<bool?>? handler) =>
        el with { OnCheckedStateChanged = handler };

    /// <summary>Wires the primary-button click handler. Passing <c>null</c> clears.</summary>
    public static SplitButtonElement Click(this SplitButtonElement el, Action? handler) =>
        el with { OnClick = handler };

    /// <summary>Wires the toggle-state-changed handler for the primary button. Passing <c>null</c> clears.</summary>
    public static ToggleSplitButtonElement IsCheckedChanged(this ToggleSplitButtonElement el, Action<bool>? handler) =>
        el with { OnIsCheckedChanged = handler };

    // ── §3 Input ───────────────────────────────────────────────────────

    /// <summary>Wires the text-changed handler. Receives the new text. Passing <c>null</c> clears.</summary>
    public static TextFieldElement Changed(this TextFieldElement el, Action<string>? handler) =>
        el with { OnChanged = handler };

    /// <summary>Wires the selection-changed handler. Receives (selectedText, selectionStart, selectionLength). Passing <c>null</c> clears.</summary>
    public static TextFieldElement SelectionChanged(this TextFieldElement el, Action<string, int, int>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Wires the password-changed handler. Passing <c>null</c> clears.</summary>
    public static PasswordBoxElement PasswordChanged(this PasswordBoxElement el, Action<string>? handler) =>
        el with { OnPasswordChanged = handler };

    /// <summary>Wires the value-changed handler. Passing <c>null</c> clears.</summary>
    public static NumberBoxElement ValueChanged(this NumberBoxElement el, Action<double>? handler) =>
        el with { OnValueChanged = handler };

    /// <summary>Wires the text-changed handler. Passing <c>null</c> clears.</summary>
    public static AutoSuggestBoxElement TextChanged(this AutoSuggestBoxElement el, Action<string>? handler) =>
        el with { OnTextChanged = handler };

    /// <summary>Wires the query-submitted handler. Passing <c>null</c> clears.</summary>
    public static AutoSuggestBoxElement QuerySubmitted(this AutoSuggestBoxElement el, Action<string>? handler) =>
        el with { OnQuerySubmitted = handler };

    /// <summary>Wires the suggestion-chosen handler. Passing <c>null</c> clears. Spec §3.4: before this fluent the event was only reachable via property-initializer syntax.</summary>
    public static AutoSuggestBoxElement SuggestionChosen(this AutoSuggestBoxElement el, Action<string>? handler) =>
        el with { OnSuggestionChosen = handler };

    /// <summary>Wires the two-state checked-changed handler. Passing <c>null</c> clears.</summary>
    public static CheckBoxElement IsCheckedChanged(this CheckBoxElement el, Action<bool>? handler) =>
        el with { OnIsCheckedChanged = handler };

    /// <summary>Wires the three-state checked-changed handler (<c>null</c> = indeterminate). Passing <c>null</c> as the handler clears it.</summary>
    public static CheckBoxElement CheckedStateChanged(this CheckBoxElement el, Action<bool?>? handler) =>
        el with { OnCheckedStateChanged = handler };

    /// <summary>Wires the checked-changed handler. Passing <c>null</c> clears.</summary>
    public static RadioButtonElement IsCheckedChanged(this RadioButtonElement el, Action<bool>? handler) =>
        el with { OnIsCheckedChanged = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static RadioButtonsElement SelectedIndexChanged(this RadioButtonsElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static ComboBoxElement SelectedIndexChanged(this ComboBoxElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the drop-down-opened handler. Passing <c>null</c> clears.</summary>
    public static ComboBoxElement DropDownOpened(this ComboBoxElement el, Action? handler) =>
        el with { OnDropDownOpened = handler };

    /// <summary>Wires the drop-down-closed handler (fires on dismissal or selection). Passing <c>null</c> clears.</summary>
    public static ComboBoxElement DropDownClosed(this ComboBoxElement el, Action? handler) =>
        el with { OnDropDownClosed = handler };

    /// <summary>Wires the value-changed handler. Passing <c>null</c> clears.</summary>
    public static SliderElement ValueChanged(this SliderElement el, Action<double>? handler) =>
        el with { OnValueChanged = handler };

    /// <summary>Wires the on/off-state-changed handler. Passing <c>null</c> clears.</summary>
    public static ToggleSwitchElement IsOnChanged(this ToggleSwitchElement el, Action<bool>? handler) =>
        el with { OnIsOnChanged = handler };

    /// <summary>Wires the rating-value-changed handler. Passing <c>null</c> clears.</summary>
    public static RatingControlElement ValueChanged(this RatingControlElement el, Action<double>? handler) =>
        el with { OnValueChanged = handler };

    /// <summary>Wires the color-changed handler. Passing <c>null</c> clears.</summary>
    public static ColorPickerElement ColorChanged(this ColorPickerElement el, Action<global::Windows.UI.Color>? handler) =>
        el with { OnColorChanged = handler };

    /// <summary>Wires the text-changed handler. Passing <c>null</c> clears.</summary>
    public static RichEditBoxElement TextChanged(this RichEditBoxElement el, Action<string>? handler) =>
        el with { OnTextChanged = handler };

    // ── §4 Date & Time ─────────────────────────────────────────────────

    /// <summary>
    /// Wires the multi-date selection-changed handler. The callback receives a
    /// snapshot of the full selection (not just added/removed dates). Not raised
    /// on the initial declarative selection applied at mount. Passing <c>null</c>
    /// clears.
    /// </summary>
    public static CalendarViewElement SelectedDatesChanged(this CalendarViewElement el, Action<IReadOnlyList<DateTimeOffset>>? handler) =>
        el with { OnSelectedDatesChanged = handler };

    /// <summary>
    /// Sets the initial multi-date selection. Re-renders with a different list
    /// reconcile the underlying selection via diff (echo events are suppressed).
    /// </summary>
    public static CalendarViewElement SelectedDates(this CalendarViewElement el, IReadOnlyList<DateTimeOffset>? dates) =>
        el with { SelectedDates = dates };

    /// <summary>Wires the date-changed handler. Receives null when the user clears the selection. Passing <c>null</c> as the handler clears it.</summary>
    public static CalendarDatePickerElement DateChanged(this CalendarDatePickerElement el, Action<DateTimeOffset?>? handler) =>
        el with { OnDateChanged = handler };

    /// <summary>Wires the date-changed handler. Passing <c>null</c> clears.</summary>
    public static DatePickerElement DateChanged(this DatePickerElement el, Action<DateTimeOffset>? handler) =>
        el with { OnDateChanged = handler };

    /// <summary>Wires the time-changed handler. Passing <c>null</c> clears.</summary>
    public static TimePickerElement TimeChanged(this TimePickerElement el, Action<TimeSpan>? handler) =>
        el with { OnTimeChanged = handler };

    // ── §5 Status / Info ───────────────────────────────────────────────

    /// <summary>Wires the action-button click handler. Passing <c>null</c> clears.</summary>
    public static InfoBarElement ActionButtonClick(this InfoBarElement el, Action? handler) =>
        el with { OnActionButtonClick = handler };

    /// <summary>Wires the closed handler. Passing <c>null</c> clears.</summary>
    public static InfoBarElement Closed(this InfoBarElement el, Action? handler) =>
        el with { OnClosed = handler };

    // ── §6 Layout containers ───────────────────────────────────────────

    /// <summary>Wires the expand-state-changed handler. Passing <c>null</c> clears.</summary>
    public static ExpanderElement IsExpandedChanged(this ExpanderElement el, Action<bool>? handler) =>
        el with { OnIsExpandedChanged = handler };

    /// <summary>Wires the pane-open-state-changed handler. Passing <c>null</c> clears.</summary>
    public static SplitViewElement PaneOpenChanged(this SplitViewElement el, Action<bool>? handler) =>
        el with { OnPaneOpenChanged = handler };

    /// <summary>Wires the view-changed handler. Inspect <c>args.IsIntermediate</c> to debounce until the scroll settles. Passing <c>null</c> clears.</summary>
    public static ScrollViewElement ViewChanged(this ScrollViewElement el, Action<Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs>? handler) =>
        el with { OnViewChanged = handler };

    // ── §7 Navigation ──────────────────────────────────────────────────

    /// <summary>Wires the selected-tag-changed handler. Passing <c>null</c> clears.</summary>
    public static NavigationViewElement SelectedTagChanged(this NavigationViewElement el, Action<string?>? handler) =>
        el with { OnSelectedTagChanged = handler };

    /// <summary>Wires the back-requested handler. Passing <c>null</c> clears.</summary>
    public static NavigationViewElement BackRequested(this NavigationViewElement el, Action? handler) =>
        el with { OnBackRequested = handler };

    /// <summary>Wires the back-requested handler. Passing <c>null</c> clears.</summary>
    public static TitleBarElement BackRequested(this TitleBarElement el, Action? handler) =>
        el with { OnBackRequested = handler };

    /// <summary>Wires the pane-toggle-requested handler. Passing <c>null</c> clears.</summary>
    public static TitleBarElement PaneToggleRequested(this TitleBarElement el, Action? handler) =>
        el with { OnPaneToggleRequested = handler };

    /// <summary>Wires the navigated-completed handler. Receives the new <c>SourcePageType</c>. Passing <c>null</c> clears.</summary>
    public static FrameElement Navigated(this FrameElement el, Action<Type>? handler) =>
        el with { OnNavigated = handler };

    /// <summary>Wires the navigating-started handler. Receives the target <c>SourcePageType</c>. Passing <c>null</c> clears.</summary>
    public static FrameElement Navigating(this FrameElement el, Action<Type>? handler) =>
        el with { OnNavigating = handler };

    /// <summary>Wires the navigation-failed handler. Receives target type and exception. Passing <c>null</c> clears.</summary>
    public static FrameElement NavigationFailed(this FrameElement el, Action<Type, Exception>? handler) =>
        el with { OnNavigationFailed = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static TabViewElement SelectedIndexChanged(this TabViewElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the tab-close-requested handler. Passing <c>null</c> clears.</summary>
    public static TabViewElement TabCloseRequested(this TabViewElement el, Action<int>? handler) =>
        el with { OnTabCloseRequested = handler };

    /// <summary>Wires the add-tab-button click handler. Passing <c>null</c> clears.</summary>
    public static TabViewElement AddTabButtonClick(this TabViewElement el, Action? handler) =>
        el with { OnAddTabButtonClick = handler };

    /// <summary>Wires the item-clicked handler. Passing <c>null</c> clears.</summary>
    public static BreadcrumbBarElement ItemClicked(this BreadcrumbBarElement el, Action<BreadcrumbBarItemData>? handler) =>
        el with { OnItemClicked = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static PivotElement SelectedIndexChanged(this PivotElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    // ── §8 Collection controls ─────────────────────────────────────────

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static ListViewElement SelectedIndexChanged(this ListViewElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the item-click handler (requires <c>IsItemClickEnabled</c>). Passing <c>null</c> clears.</summary>
    public static ListViewElement ItemClick(this ListViewElement el, Action<int>? handler) =>
        el with { OnItemClick = handler };

    /// <summary>
    /// Wires the multi-select snapshot handler. Receives the FULL list of
    /// selected indices on every change (not added/removed deltas). Snapshot
    /// semantics match <c>CalendarView.SelectedDatesChanged</c>. Passing
    /// <c>null</c> clears.
    /// </summary>
    public static ListViewElement SelectionChanged(this ListViewElement el, Action<IReadOnlyList<int>>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static GridViewElement SelectedIndexChanged(this GridViewElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the item-click handler (requires <c>IsItemClickEnabled</c>). Passing <c>null</c> clears.</summary>
    public static GridViewElement ItemClick(this GridViewElement el, Action<int>? handler) =>
        el with { OnItemClick = handler };

    /// <summary>Wires the multi-select snapshot handler (see <see cref="SelectionChanged(ListViewElement, Action{IReadOnlyList{int}}?)"/>).</summary>
    public static GridViewElement SelectionChanged(this GridViewElement el, Action<IReadOnlyList<int>>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Wires the item-invoked handler. Passing <c>null</c> clears.</summary>
    public static TreeViewElement ItemInvoked(this TreeViewElement el, Action<TreeViewNodeData>? handler) =>
        el with { OnItemInvoked = handler };

    /// <summary>Wires the expanding handler. Passing <c>null</c> clears.</summary>
    public static TreeViewElement Expanding(this TreeViewElement el, Action<TreeViewNodeData>? handler) =>
        el with { OnExpanding = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static FlipViewElement SelectedIndexChanged(this FlipViewElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static ListBoxElement SelectedIndexChanged(this ListBoxElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the multi-select snapshot handler. Passing <c>null</c> clears.</summary>
    public static ListBoxElement SelectionChanged(this ListBoxElement el, Action<IReadOnlyList<int>>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Wires the item-invoked handler. Passing <c>null</c> clears.</summary>
    public static ItemsViewElement<T> ItemInvoked<T>(this ItemsViewElement<T> el, Action<T>? handler) =>
        el with { OnItemInvoked = handler };

    /// <summary>
    /// Wires the multi-select snapshot handler. Receives the full list of
    /// currently selected items (not indices). Passing <c>null</c> clears.
    /// </summary>
    public static ItemsViewElement<T> SelectionChanged<T>(this ItemsViewElement<T> el, Action<IReadOnlyList<T>>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static TemplatedListViewElement<T> SelectedIndexChanged<T>(this TemplatedListViewElement<T> el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the item-click handler. Passing <c>null</c> clears.</summary>
    public static TemplatedListViewElement<T> ItemClick<T>(this TemplatedListViewElement<T> el, Action<T>? handler) =>
        el with { OnItemClick = handler };

    /// <summary>
    /// Wires the multi-select snapshot handler for the typed peer. Receives
    /// the full list of currently selected items. Passing <c>null</c> clears.
    /// </summary>
    public static TemplatedListViewElement<T> SelectionChanged<T>(this TemplatedListViewElement<T> el, Action<IReadOnlyList<T>>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static TemplatedGridViewElement<T> SelectedIndexChanged<T>(this TemplatedGridViewElement<T> el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the item-click handler. Passing <c>null</c> clears.</summary>
    public static TemplatedGridViewElement<T> ItemClick<T>(this TemplatedGridViewElement<T> el, Action<T>? handler) =>
        el with { OnItemClick = handler };

    /// <summary>Wires the multi-select snapshot handler for the typed peer. Passing <c>null</c> clears.</summary>
    public static TemplatedGridViewElement<T> SelectionChanged<T>(this TemplatedGridViewElement<T> el, Action<IReadOnlyList<T>>? handler) =>
        el with { OnSelectionChanged = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static TemplatedFlipViewElement<T> SelectedIndexChanged<T>(this TemplatedFlipViewElement<T> el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    // ── §9 Dialogs / overlays / flyouts ────────────────────────────────

    /// <summary>Wires the closed handler. Receives the <c>ContentDialogResult</c> indicating which button dismissed the dialog. Passing <c>null</c> clears.</summary>
    public static ContentDialogElement Closed(this ContentDialogElement el, Action<ContentDialogResult>? handler) =>
        el with { OnClosed = handler };

    /// <summary>Wires the opened handler (fires after the dialog finishes opening). Passing <c>null</c> clears.</summary>
    public static ContentDialogElement Opened(this ContentDialogElement el, Action? handler) =>
        el with { OnOpened = handler };

    /// <summary>Wires the opened handler. Passing <c>null</c> clears.</summary>
    public static FlyoutElement Opened(this FlyoutElement el, Action? handler) =>
        el with { OnOpened = handler };

    /// <summary>Wires the closed handler. Passing <c>null</c> clears.</summary>
    public static FlyoutElement Closed(this FlyoutElement el, Action? handler) =>
        el with { OnClosed = handler };

    /// <summary>Wires the action-button click handler. Passing <c>null</c> clears.</summary>
    public static TeachingTipElement ActionButtonClick(this TeachingTipElement el, Action? handler) =>
        el with { OnActionButtonClick = handler };

    /// <summary>Wires the closed handler. Passing <c>null</c> clears.</summary>
    public static TeachingTipElement Closed(this TeachingTipElement el, Action? handler) =>
        el with { OnClosed = handler };

    /// <summary>Wires the opened handler. Passing <c>null</c> clears.</summary>
    public static PopupElement Opened(this PopupElement el, Action? handler) =>
        el with { OnOpened = handler };

    /// <summary>Wires the closed handler. Passing <c>null</c> clears.</summary>
    public static PopupElement Closed(this PopupElement el, Action? handler) =>
        el with { OnClosed = handler };

    // ── §10 Media ──────────────────────────────────────────────────────

    /// <summary>Wires the image-opened handler (fires after the source loads successfully). Passing <c>null</c> clears.</summary>
    public static ImageElement ImageOpened(this ImageElement el, Action? handler) =>
        el with { OnImageOpened = handler };

    /// <summary>Wires the image-failed handler. Receives the failure message. Passing <c>null</c> clears.</summary>
    public static ImageElement ImageFailed(this ImageElement el, Action<string>? handler) =>
        el with { OnImageFailed = handler };

    /// <summary>Wires the navigation-starting handler. Passing <c>null</c> clears.</summary>
    public static WebView2Element NavigationStarting(this WebView2Element el, Action<Uri>? handler) =>
        el with { OnNavigationStarting = handler };

    /// <summary>Wires the navigation-completed handler. Passing <c>null</c> clears.</summary>
    public static WebView2Element NavigationCompleted(this WebView2Element el, Action<Uri>? handler) =>
        el with { OnNavigationCompleted = handler };

    /// <summary>
    /// Wires the host-bound <c>WebMessageReceived</c> handler. The callback runs
    /// on the UI thread and receives the message payload as a string (JSON when
    /// the page sends structured data via <c>postMessage(...)</c>). Passing
    /// <c>null</c> clears.
    /// </summary>
    public static WebView2Element WebMessageReceived(this WebView2Element el, Action<string>? handler) =>
        el with { OnWebMessageReceived = handler };

    /// <summary>
    /// Wires the <c>CoreWebView2Initialized</c> handler. Fires once on the UI
    /// thread when the underlying <c>CoreWebView2</c> is available. Passing
    /// <c>null</c> clears.
    /// </summary>
    public static WebView2Element CoreWebView2Initialized(this WebView2Element el, Action? handler) =>
        el with { OnCoreWebView2Initialized = handler };

    /// <summary>Wires the media-opened handler. Marshalled to UI thread. Passing <c>null</c> clears.</summary>
    public static MediaPlayerElementElement MediaOpened(this MediaPlayerElementElement el, Action? handler) =>
        el with { OnMediaOpened = handler };

    /// <summary>Wires the media-ended handler. Marshalled to UI thread. Passing <c>null</c> clears.</summary>
    public static MediaPlayerElementElement MediaEnded(this MediaPlayerElementElement el, Action? handler) =>
        el with { OnMediaEnded = handler };

    /// <summary>Wires the media-failed handler. Receives the failure error message. Marshalled to UI thread. Passing <c>null</c> clears.</summary>
    public static MediaPlayerElementElement MediaFailed(this MediaPlayerElementElement el, Action<string>? handler) =>
        el with { OnMediaFailed = handler };

    // ── §12 Niche / less-common ────────────────────────────────────────

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static SelectorBarElement SelectedIndexChanged(this SelectorBarElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the selected-page-index-changed handler. Passing <c>null</c> clears.</summary>
    public static PipsPagerElement SelectedPageIndexChanged(this PipsPagerElement el, Action<int>? handler) =>
        el with { OnSelectedPageIndexChanged = handler };

    /// <summary>Wires the refresh-requested handler. Passing <c>null</c> clears.</summary>
    public static RefreshContainerElement RefreshRequested(this RefreshContainerElement el, Action? handler) =>
        el with { OnRefreshRequested = handler };

    // ── §13 Specialized Reactor controls (Phase 7.2 quick wins) ────────

    /// <summary>Wires the selected-item-changed handler for the typed auto-suggest. Passing <c>null</c> clears.</summary>
    public static AutoSuggestElement<T> Selected<T>(this AutoSuggestElement<T> el, Action<T?>? handler) =>
        el with { OnSelected = handler };

    /// <summary>Wires the text-changed handler. Passing <c>null</c> clears.</summary>
    public static MaskedTextFieldElement Changed(this MaskedTextFieldElement el, Action<string>? handler) =>
        el with { OnChanged = handler };

    /// <summary>Wires the root-changed handler (fired when an immutable root object is replaced). Passing <c>null</c> clears.</summary>
    public static PropertyGridElement RootChanged(this PropertyGridElement el, Action<object>? handler) =>
        el with { OnRootChanged = handler };

    /// <summary>Wires the visible-range-changed handler (receives <c>firstVisibleIndex</c> and <c>lastVisibleIndex</c>). Passing <c>null</c> clears.</summary>
    public static VirtualListElement VisibleRangeChanged(this VirtualListElement el, Action<int, int>? handler) =>
        el with { OnVisibleRangeChanged = handler };

    /// <summary>
    /// Wires the multi-select snapshot handler for <see cref="DataGridElement{T}"/>.
    /// Receives the full set of currently-selected <c>RowKey</c>s on every
    /// change (not added/removed deltas). Passing <c>null</c> clears.
    /// </summary>
    public static DataGridElement<T> SelectionChanged<T>(this DataGridElement<T> el, Action<IReadOnlySet<RowKey>>? handler) =>
        el with { OnSelectionChanged = handler };
}
