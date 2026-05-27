using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace PerfBench.ControlModel.Variants;

/// <summary>
/// Spec 047 §14 Phase 2 (Q1 spike) — builds a <see cref="Reconciler"/> for
/// the <see cref="BenchVariant.ReactorDescriptors"/> bench variant.
///
/// <para>The reconciler runs with the V1 flag ON but skips the automatic
/// Phase 1 handler registration (via the internal ctor flag), then registers
/// <see cref="DescriptorHandler{TElement,TControl}"/> instances for the
/// four ported controls — ToggleSwitch, Slider, Border (Q1 spike baseline)
/// and TextBox (Phase 3 prereq 3.0.2 — first multi-event descriptor port
/// using HandCodedControlled + HandCodedEvent). ListView keeps its Phase 1
/// hand-coded handler so every bench still has a working dispatch; M1 / M2 /
/// M5 / M7 / M10 land directly on the contested controls so the descriptor
/// tax is exposed where it matters.</para>
/// </summary>
internal static class DescriptorVariantFactory
{
    public static Reconciler Create()
    {
        // Internal ctor: V1 ON but no auto-register, so we can plug
        // descriptor handlers in for the ported controls while leaving the
        // remaining Phase 1 ports on hand-coded handlers.
        var rec = new Reconciler(logger: null, useV1Protocol: true, registerBuiltinHandlers: false);

        rec.RegisterHandler<ToggleSwitchElement, WinUI.ToggleSwitch>(
            new DescriptorHandler<ToggleSwitchElement, WinUI.ToggleSwitch>(
                ToggleSwitchDescriptor.Descriptor));

        rec.RegisterHandler<SliderElement, WinUI.Slider>(
            new DescriptorHandler<SliderElement, WinUI.Slider>(
                SliderDescriptor.Descriptor));

        rec.RegisterHandler<BorderElement, WinUI.Border>(
            new DescriptorHandler<BorderElement, WinUI.Border>(
                BorderDescriptor.Descriptor));

        rec.RegisterHandler<TextBoxElement, WinUI.TextBox>(
            new DescriptorHandler<TextBoxElement, WinUI.TextBox>(
                TextBoxDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 1) — value-bearing single-event ports.
        rec.RegisterHandler<CheckBoxElement, WinUI.CheckBox>(
            new DescriptorHandler<CheckBoxElement, WinUI.CheckBox>(
                CheckBoxDescriptor.Descriptor));

        rec.RegisterHandler<RadioButtonElement, WinUI.RadioButton>(
            new DescriptorHandler<RadioButtonElement, WinUI.RadioButton>(
                RadioButtonDescriptor.Descriptor));

        rec.RegisterHandler<RatingControlElement, WinUI.RatingControl>(
            new DescriptorHandler<RatingControlElement, WinUI.RatingControl>(
                RatingControlDescriptor.Descriptor));

        rec.RegisterHandler<ToggleSplitButtonElement, WinUI.ToggleSplitButton>(
            new DescriptorHandler<ToggleSplitButtonElement, WinUI.ToggleSplitButton>(
                ToggleSplitButtonDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 2) — value-bearing date/time/color ports.
        rec.RegisterHandler<ColorPickerElement, WinUI.ColorPicker>(
            new DescriptorHandler<ColorPickerElement, WinUI.ColorPicker>(
                ColorPickerDescriptor.Descriptor));

        rec.RegisterHandler<CalendarDatePickerElement, WinUI.CalendarDatePicker>(
            new DescriptorHandler<CalendarDatePickerElement, WinUI.CalendarDatePicker>(
                CalendarDatePickerDescriptor.Descriptor));

        rec.RegisterHandler<DatePickerElement, WinUI.DatePicker>(
            new DescriptorHandler<DatePickerElement, WinUI.DatePicker>(
                DatePickerDescriptor.Descriptor));

        rec.RegisterHandler<TimePickerElement, WinUI.TimePicker>(
            new DescriptorHandler<TimePickerElement, WinUI.TimePicker>(
                TimePickerDescriptor.Descriptor));

        rec.RegisterHandler<ListViewElement, WinUI.ListView>(new ListViewHandler());

        return rec;
    }
}
