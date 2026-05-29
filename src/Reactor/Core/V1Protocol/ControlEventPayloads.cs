// Phase 1 (1.7) ships the per-control payload shape only. Slot assignments
// land with the corresponding control ports in 1.11–1.15; suppress the
// unassigned-field warning for the placeholders.
#pragma warning disable CS0649

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §9.2 / §14 Phase 1 (1.7) — per-control event payload structs.
///
/// One payload per control with control-intrinsic events (the seven audited
/// in §9.2: ToggleSwitch, Button, TextBox, Image, ScrollViewer, ScrollView,
/// NumberBox). Each carries 1–3 slots: a stable Trampoline (rooted for the
/// control's lifetime) plus a Current* user delegate the trampoline reads
/// on each fire.
///
/// The control-intrinsic events live here in per-control payload boxes; the
/// routed/modifier input family (pointer / key / focus / etc.) lives in the
/// shared <c>ModifierEventHandlerState</c>. Ported V1 controls opt into a
/// per-control payload via
/// <see cref="ReactorBinding{TElement}.OnCustomEvent{TArgs}"/>.
///
/// Handlers verify the payload type via <see cref="ControlEventStateBox.HandlerType"/>
/// before unboxing (§9.2 "Why the discriminator matters"). The pool reset
/// contract PRESERVES <c>ReactorState.ControlEventState</c> across rent/return
/// (issue #114) — trampolines stay subscribed for the control's lifetime and
/// the box is only dropped on full detach.
/// </summary>
internal sealed class ToggleSwitchEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? ToggledTrampoline;
    public Action<Element, Microsoft.UI.Xaml.RoutedEventArgs>? CurrentToggled;
}

internal sealed class ButtonEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? ClickTrampoline;
    public Action<Element, Microsoft.UI.Xaml.RoutedEventArgs>? CurrentClick;
}

internal sealed class TextBoxEventPayload
{
    public Microsoft.UI.Xaml.Controls.TextChangedEventHandler? TextChangedTrampoline;
    public Action<Element, Microsoft.UI.Xaml.Controls.TextChangedEventArgs>? CurrentTextChanged;
    public Microsoft.UI.Xaml.RoutedEventHandler? SelectionChangedTrampoline;
    public Action<Element, Microsoft.UI.Xaml.RoutedEventArgs>? CurrentSelectionChanged;

    // Spec 047 §8 value-diff echo suppression (PoC) — see TextBoxHandler. A
    // programmatic controlled `Text` write arms ExpectedEchoText with the value
    // it just wrote; the TextChanged trampoline drops the single matching echo
    // (readback == ExpectedEchoText) once instead of the legacy counter.
    public string? ExpectedEchoText;
    public bool HasExpectedEchoText;
}

internal sealed class ImageEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? ImageOpenedTrampoline;
    public Action<Element, Microsoft.UI.Xaml.RoutedEventArgs>? CurrentImageOpened;
    public Microsoft.UI.Xaml.ExceptionRoutedEventHandler? ImageFailedTrampoline;
    public Action<Element, Microsoft.UI.Xaml.ExceptionRoutedEventArgs>? CurrentImageFailed;
}

internal sealed class ScrollViewerEventPayload
{
    public global::System.EventHandler<Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs>? ViewChangedTrampoline;
    public Action<Element, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs>? CurrentViewChanged;
}

internal sealed class ScrollViewEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.ScrollView, object>? ViewChangedTrampoline;
    public Action<Element, object>? CurrentViewChanged;
}

internal sealed class NumberBoxEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.NumberBox, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs>? ValueChangedTrampoline;
    public Action<Element, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs>? CurrentValueChanged;

    /// <summary>Spec 047 §14 Phase 3-final — <c>.Immediate</c> entry's
    /// per-keystroke trampoline against the NumberBox's <c>TextProperty</c>
    /// change callback. Lives once per control lifetime alongside the
    /// commit-mode <see cref="ValueChangedTrampoline"/>.</summary>
    public Microsoft.UI.Xaml.DependencyPropertyChangedCallback? ImmediateTextChangedCallback;

    /// <summary>Spec 047 §14 Phase 3-final — inner <c>TextBox</c> template-part
    /// trampoline for per-keystroke observation BEFORE NumberBox's
    /// <c>TextProperty</c> sync (matches the legacy
    /// <c>EnsureNumberBoxImmediateTextBoxWiring</c> flow).</summary>
    public Microsoft.UI.Xaml.Controls.TextChangedEventHandler? ImmediateInnerTextChangedTrampoline;

    /// <summary>Idempotency flag — once the inner template-part has been
    /// found and wired this flips to <c>true</c> so the <c>Loaded</c> hook
    /// stops re-walking the visual tree on subsequent renders.</summary>
    public bool ImmediateInnerWired;
}

/// <summary>Spec 047 §9.2 typed payload — Slider was missed in the original
/// seven-event audit but uses the same control-intrinsic pattern
/// (RangeBase.ValueChanged round-tripping against the element's
/// OnValueChanged callback).</summary>
internal sealed class SliderEventPayload
{
    public Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventHandler? ValueChangedTrampoline;
    public Action<Element, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs>? CurrentValueChanged;
}

/// <summary>Spec 047 §14 Phase 3 batch 4 — HyperlinkButton Click payload.</summary>
internal sealed class HyperlinkButtonEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? ClickTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 4 — RepeatButton Click payload.</summary>
internal sealed class RepeatButtonEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? ClickTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 4 — ToggleButton Click payload.
/// Click fires both OnIsCheckedChanged (bool) and OnCheckedStateChanged (bool?)
/// — see <see cref="V1Protocol.Descriptor.Descriptors.ToggleButtonDescriptor"/>.</summary>
internal sealed class ToggleButtonEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? ClickTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 4 — SplitButton Click payload.
/// SplitButton's Click event is a <c>TypedEventHandler&lt;SplitButton,
/// SplitButtonClickEventArgs&gt;</c>, not a plain RoutedEventHandler.</summary>
internal sealed class SplitButtonEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.SplitButton, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs>? ClickTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 5 — RichEditBox TextChanged payload.
/// Round-trips <c>Document.GetText</c> into <c>OnTextChanged(string)</c>.
/// RichEditBox's <c>TextChanged</c> uses <see cref="Microsoft.UI.Xaml.RoutedEventHandler"/>
/// (unlike the plain TextBox's typed <c>TextChangedEventHandler</c>).</summary>
internal sealed class RichEditBoxEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? TextChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 5 — PasswordBox PasswordChanged payload.
/// Round-trips <c>Password</c> into <c>OnPasswordChanged(string)</c> with
/// the manual <c>ChangeEchoSuppressor.ShouldSuppress</c> gate that the
/// hand-coded mount/update arms use.</summary>
internal sealed class PasswordBoxEventPayload
{
    public Microsoft.UI.Xaml.RoutedEventHandler? PasswordChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 5 — RadioButtons SelectionChanged
/// payload. Round-trips <c>SelectedIndex</c> into
/// <c>OnSelectedIndexChanged(int)</c>.</summary>
internal sealed class RadioButtonsEventPayload
{
    public Microsoft.UI.Xaml.Controls.SelectionChangedEventHandler? SelectionChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 6 — AutoSuggestBox multi-event
/// payload. Three slots: <c>Text</c> round-trip (filtered to
/// <c>UserInput</c>), <c>QuerySubmitted</c> fire-only, and
/// <c>SuggestionChosen</c> fire-only. Each event uses its native
/// <see cref="global::Windows.Foundation.TypedEventHandler{TSender,TArgs}"/>
/// because <see cref="Microsoft.UI.Xaml.Controls.AutoSuggestBox"/> emits
/// typed args (<c>AutoSuggestBoxTextChangedEventArgs</c> etc.) we need to
/// inspect inside the trampolines (the TextChanged trampoline gates on
/// <c>args.Reason == UserInput</c>).</summary>
internal sealed class AutoSuggestBoxEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.AutoSuggestBox,
        Microsoft.UI.Xaml.Controls.AutoSuggestBoxTextChangedEventArgs>? TextChangedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.AutoSuggestBox,
        Microsoft.UI.Xaml.Controls.AutoSuggestBoxQuerySubmittedEventArgs>? QuerySubmittedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.AutoSuggestBox,
        Microsoft.UI.Xaml.Controls.AutoSuggestBoxSuggestionChosenEventArgs>? SuggestionChosenTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 6 — ComboBox multi-event payload.
/// Three slots: <c>SelectedIndex</c> round-trip via SelectionChanged,
/// <c>DropDownOpened</c> fire-only, <c>DropDownClosed</c> fire-only.
/// The Items collection is escape-hatched (not surfaced by the
/// descriptor); the legacy arm continues to handle Items reconciliation
/// for authors who need it.</summary>
internal sealed class ComboBoxEventPayload
{
    public Microsoft.UI.Xaml.Controls.SelectionChangedEventHandler? SelectionChangedTrampoline;
    public global::System.EventHandler<object>? DropDownOpenedTrampoline;
    public global::System.EventHandler<object>? DropDownClosedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 7 — Expander IsExpanded round-trip
/// payload. Two trampoline slots because the legacy arm wires both
/// <c>Expanding</c> (fires on transition to expanded) and <c>Collapsed</c>
/// (fires on transition to collapsed). The descriptor uses
/// <c>HandCodedControlled</c> for Expanding (the DP round-trip with
/// <c>OnIsExpandedChanged(true)</c>) and <c>HandCodedEvent</c> for
/// Collapsed (fire-only with <c>OnIsExpandedChanged(false)</c>) — both
/// fire the same element callback with the corresponding bool.</summary>
internal sealed class ExpanderEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.Expander,
        Microsoft.UI.Xaml.Controls.ExpanderExpandingEventArgs>? ExpandingTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.Expander,
        Microsoft.UI.Xaml.Controls.ExpanderCollapsedEventArgs>? CollapsedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 9 — SplitView named-slot container
/// payload. <c>IsPaneOpen</c> is a plain <c>.OneWay</c> write (mirrors the
/// legacy arm — programmatic writes fire the same events as user toggles).
/// <c>PaneOpening</c> and <c>PaneClosing</c> are fire-only
/// <c>.HandCodedEvent</c> trampolines; both invoke
/// <c>OnPaneOpenChanged</c> with the corresponding bool.</summary>
internal sealed class SplitViewEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.SplitView,
        object>? PaneOpeningTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.SplitView,
        Microsoft.UI.Xaml.Controls.SplitViewPaneClosingEventArgs>? PaneClosingTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 9 — InfoBar named-slot container
/// payload. <c>IsOpen</c> is a plain <c>.OneWay</c> write (mirrors the
/// legacy arm). <c>Closed</c> is a fire-only <c>.HandCodedEvent</c>
/// trampoline that invokes <c>OnClosed</c> when the user dismisses the
/// InfoBar.</summary>
internal sealed class InfoBarEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.InfoBar,
        Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs>? ClosedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 9 — TeachingTip named-slot container
/// payload. <c>IsOpen</c> is a plain <c>.OneWay</c> write (mirrors the
/// legacy arm). <c>Closed</c> and <c>ActionButtonClick</c> are fire-only
/// <c>.HandCodedEvent</c> trampolines.</summary>
internal sealed class TeachingTipEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TeachingTip,
        Microsoft.UI.Xaml.Controls.TeachingTipClosedEventArgs>? ClosedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TeachingTip,
        object>? ActionButtonClickTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 11 — PipsPager SelectedPageIndex
/// round-trip payload. Single slot: <c>SelectedIndexChanged</c> is the only
/// event we surface; the descriptor uses <see cref="ChangeEchoSuppressor"/>
/// to drain the programmatic write.</summary>
internal sealed class PipsPagerEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.PipsPager,
        Microsoft.UI.Xaml.Controls.PipsPagerSelectedIndexChangedEventArgs>? SelectedIndexChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 11 — ListBox SelectedIndex round-trip
/// payload. The single <c>SelectionChanged</c> trampoline fires BOTH the
/// element's <c>OnSelectedIndexChanged</c> and (if present) the
/// <c>OnSelectionChanged</c> snapshot callback — mirrors the legacy
/// <c>MountListBox</c> arm's twin-invoke shape.</summary>
internal sealed class ListBoxEventPayload
{
    public Microsoft.UI.Xaml.Controls.SelectionChangedEventHandler? SelectionChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 completion — GridView selected-index and
/// item-click payload. SelectionChanged fires both single-index and
/// multi-select snapshot callbacks; ItemClick maps the clicked item back to
/// the flat ItemsHost collection index.</summary>
internal sealed class GridViewEventPayload
{
    public Microsoft.UI.Xaml.Controls.SelectionChangedEventHandler? SelectionChangedTrampoline;
    public Microsoft.UI.Xaml.Controls.ItemClickEventHandler? ItemClickTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 completion — ItemsView item-invoked and
/// selection-changed payload. Trampolines translate ReactorRow payloads back
/// to the user's typed item indices through ItemsViewElementBase.</summary>
internal sealed class ItemsViewEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.ItemsView,
        Microsoft.UI.Xaml.Controls.ItemsViewItemInvokedEventArgs>? ItemInvokedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.ItemsView,
        Microsoft.UI.Xaml.Controls.ItemsViewSelectionChangedEventArgs>? SelectionChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 11 — SelectorBar SelectedIndex
/// round-trip payload. <c>SelectionChanged</c> trampoline reads the live
/// SelectedItem reference and converts back to the index via
/// <c>Items.IndexOf</c> to feed <c>OnSelectedIndexChanged</c>.</summary>
internal sealed class SelectorBarEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.SelectorBar,
        Microsoft.UI.Xaml.Controls.SelectorBarSelectionChangedEventArgs>? SelectionChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 finish — Port (8). TreeView event
/// trampoline payload. Both events are fire-only (no programmatic
/// echo to suppress): ItemInvoked carries the invoked
/// <see cref="TreeViewNodeData"/>; Expanding fires as a node opens.</summary>
internal sealed class TreeViewEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TreeView,
        Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs>? ItemInvokedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TreeView,
        Microsoft.UI.Xaml.Controls.TreeViewExpandingEventArgs>? ExpandingTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 finish — Port (9). FlipView
/// <c>SelectionChanged</c> trampoline payload. Mirrors the simple
/// SelectedIndex round-trip used by ListBox / PipsPager.</summary>
internal sealed class FlipViewEventPayload
{
    public Microsoft.UI.Xaml.Controls.SelectionChangedEventHandler? SelectionChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 finish — Ports (10) + (11). TabView /
/// Pivot event trampoline payload. SelectionChanged is the primary
/// round-trip; TabCloseRequested + AddTabButtonClick are TabView-only
/// fire-only events. Pivot only uses the SelectionChanged slot.</summary>
internal sealed class TabViewEventPayload
{
    public Microsoft.UI.Xaml.Controls.SelectionChangedEventHandler? SelectionChangedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TabView,
        Microsoft.UI.Xaml.Controls.TabViewTabCloseRequestedEventArgs>? TabCloseRequestedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TabView,
        object>? AddTabButtonClickTrampoline;
    // Spec 045 §2.4 docking drag pipeline — fire-only slots wired by the
    // §4.0.3 TabViewDescriptor port (replaces the always-subscribed legacy
    // MountTabView arms).
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TabView,
        Microsoft.UI.Xaml.Controls.TabViewTabDragStartingEventArgs>? TabDragStartingTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.TabView,
        Microsoft.UI.Xaml.Controls.TabViewTabDragCompletedEventArgs>? TabDragCompletedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3-final Batch B — Frame multi-event payload.
/// Three fire-only slots for Navigated / Navigating / NavigationFailed.
/// Frame has NO controlled-prop round-trip (SourcePageType is .Initial /
/// Navigate-on-mount only — see <c>FrameDescriptor</c>).</summary>
internal sealed class FrameEventPayload
{
    public Microsoft.UI.Xaml.Navigation.NavigatedEventHandler? NavigatedTrampoline;
    public Microsoft.UI.Xaml.Navigation.NavigatingCancelEventHandler? NavigatingTrampoline;
    public Microsoft.UI.Xaml.Navigation.NavigationFailedEventHandler? NavigationFailedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3-final Batch C — CalendarView SelectedDates
/// two-way payload. Single slot for the <c>SelectedDatesChanged</c>
/// trampoline wired by the <c>.CollectionDiffControlled</c> entry on the
/// element's <c>SelectedDates</c> <c>IObservableVector&lt;DateTimeOffset&gt;</c>.</summary>
internal sealed class CalendarViewEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.CalendarView,
        Microsoft.UI.Xaml.Controls.CalendarViewSelectedDatesChangedEventArgs>? SelectedDatesChangedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 batch 11 — BreadcrumbBar ItemClicked
/// fire-only payload. Trampoline maps <c>args.Index</c> back to the live
/// element's <c>Items[idx]</c> data — mirrors the legacy arm.</summary>
internal sealed class BreadcrumbBarEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.BreadcrumbBar,
        Microsoft.UI.Xaml.Controls.BreadcrumbBarItemClickedEventArgs>? ItemClickedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 deferred controls — RefreshContainer RefreshRequested payload.</summary>
internal sealed class RefreshContainerEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.RefreshContainer,
        Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs>? RefreshRequestedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 deferred controls — WebView2 event payload.</summary>
internal sealed class WebView2EventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.WebView2,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs>? NavigationStartingTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.WebView2,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs>? NavigationCompletedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.WebView2,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs>? WebMessageReceivedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.WebView2,
        Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs>? CoreInitializedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 deferred controls — TitleBar event payload.</summary>
internal sealed class TitleBarEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.TitleBar, object>? BackRequestedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.TitleBar, object>? PaneToggleRequestedTrampoline;
}

/// <summary>Spec 047 §14 Phase 3 deferred controls — NavigationView event payload.</summary>
internal sealed class NavigationViewEventPayload
{
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.NavigationView,
        Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs>? SelectionChangedTrampoline;
    public global::Windows.Foundation.TypedEventHandler<
        Microsoft.UI.Xaml.Controls.NavigationView,
        Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs>? BackRequestedTrampoline;
}

/// <summary>
/// Spec 047 §9.2 — open-ended anchor for delegates registered via
/// <see cref="ReactorBinding{TElement}.OnCustomEvent{TArgs}"/>. Holds a
/// strongly-typed delegate so the GC keeps the captured closure alive
/// for the control's lifetime. Multiple custom events on the same control
/// stack into the list. Cleared by the pool reset contract.
/// </summary>
internal sealed class CustomEventAnchorPayload
{
    public List<Delegate> Trampolines { get; } = new();
}
