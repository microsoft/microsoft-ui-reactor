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
/// Phase 1 ships only the shape and storage. Legacy MountXxx paths still
/// use the shared <c>EventHandlerState</c> (Toggled / Click / TextChanged
/// trampolines); ported V1 controls opt into a per-control payload via
/// <see cref="ReactorBinding{TElement}.OnCustomEvent{TArgs}"/>.
///
/// Handlers verify the payload type via <see cref="ControlEventStateBox.HandlerType"/>
/// before unboxing (§9.2 "Why the discriminator matters"). The pool reset
/// contract clears <c>ReactorState.ControlEventState</c> on return.
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
