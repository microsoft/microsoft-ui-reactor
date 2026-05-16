using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
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

    /// <summary>Wires a click handler (sets <see cref="ButtonElement.OnClick"/>). Passing <c>null</c> clears any existing handler.</summary>
    public static ButtonElement Click(this ButtonElement el, Action? handler) =>
        el with { OnClick = handler };

    /// <summary>Wires a click handler (sets <see cref="HyperlinkButtonElement.OnClick"/>). Passing <c>null</c> clears.</summary>
    public static HyperlinkButtonElement Click(this HyperlinkButtonElement el, Action? handler) =>
        el with { OnClick = handler };

    /// <summary>Wires a click handler that fires repeatedly while held. Passing <c>null</c> clears.</summary>
    public static RepeatButtonElement Click(this RepeatButtonElement el, Action? handler) =>
        el with { OnClick = handler };

    /// <summary>Wires the toggle-state-changed handler. Passing <c>null</c> clears.</summary>
    public static ToggleButtonElement IsCheckedChanged(this ToggleButtonElement el, Action<bool>? handler) =>
        el with { OnIsCheckedChanged = handler };

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
    // (CalendarView.OnSelectedDatesChanged lives in Phase 3.1 — modelled separately.)

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

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static GridViewElement SelectedIndexChanged(this GridViewElement el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the item-click handler (requires <c>IsItemClickEnabled</c>). Passing <c>null</c> clears.</summary>
    public static GridViewElement ItemClick(this GridViewElement el, Action<int>? handler) =>
        el with { OnItemClick = handler };

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

    /// <summary>Wires the item-invoked handler. Passing <c>null</c> clears.</summary>
    public static ItemsViewElement<T> ItemInvoked<T>(this ItemsViewElement<T> el, Action<T>? handler) =>
        el with { OnItemInvoked = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static TemplatedListViewElement<T> SelectedIndexChanged<T>(this TemplatedListViewElement<T> el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the item-click handler. Passing <c>null</c> clears.</summary>
    public static TemplatedListViewElement<T> ItemClick<T>(this TemplatedListViewElement<T> el, Action<T>? handler) =>
        el with { OnItemClick = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static TemplatedGridViewElement<T> SelectedIndexChanged<T>(this TemplatedGridViewElement<T> el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    /// <summary>Wires the item-click handler. Passing <c>null</c> clears.</summary>
    public static TemplatedGridViewElement<T> ItemClick<T>(this TemplatedGridViewElement<T> el, Action<T>? handler) =>
        el with { OnItemClick = handler };

    /// <summary>Wires the selected-index-changed handler. Passing <c>null</c> clears.</summary>
    public static TemplatedFlipViewElement<T> SelectedIndexChanged<T>(this TemplatedFlipViewElement<T> el, Action<int>? handler) =>
        el with { OnSelectedIndexChanged = handler };

    // ── §9 Dialogs / overlays / flyouts ────────────────────────────────

    /// <summary>Wires the closed handler. Receives the <c>ContentDialogResult</c> indicating which button dismissed the dialog. Passing <c>null</c> clears.</summary>
    public static ContentDialogElement Closed(this ContentDialogElement el, Action<ContentDialogResult>? handler) =>
        el with { OnClosed = handler };

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

    /// <summary>Wires the closed handler. Passing <c>null</c> clears.</summary>
    public static PopupElement Closed(this PopupElement el, Action? handler) =>
        el with { OnClosed = handler };

    // ── §10 Media ──────────────────────────────────────────────────────

    /// <summary>Wires the navigation-starting handler. Passing <c>null</c> clears.</summary>
    public static WebView2Element NavigationStarting(this WebView2Element el, Action<Uri>? handler) =>
        el with { OnNavigationStarting = handler };

    /// <summary>Wires the navigation-completed handler. Passing <c>null</c> clears.</summary>
    public static WebView2Element NavigationCompleted(this WebView2Element el, Action<Uri>? handler) =>
        el with { OnNavigationCompleted = handler };

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
}
