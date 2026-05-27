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

        // Spec 047 §14 Phase 3 (batch 3) — Display family zero-event ports.
        rec.RegisterHandler<TextBlockElement, WinUI.TextBlock>(
            new DescriptorHandler<TextBlockElement, WinUI.TextBlock>(
                TextBlockDescriptor.Descriptor));

        rec.RegisterHandler<ImageElement, WinUI.Image>(
            new DescriptorHandler<ImageElement, WinUI.Image>(
                ImageDescriptor.Descriptor));

        rec.RegisterHandler<PersonPictureElement, WinUI.PersonPicture>(
            new DescriptorHandler<PersonPictureElement, WinUI.PersonPicture>(
                PersonPictureDescriptor.Descriptor));

        rec.RegisterHandler<ProgressElement, WinUI.ProgressBar>(
            new DescriptorHandler<ProgressElement, WinUI.ProgressBar>(
                ProgressBarDescriptor.Descriptor));

        rec.RegisterHandler<ProgressRingElement, WinUI.ProgressRing>(
            new DescriptorHandler<ProgressRingElement, WinUI.ProgressRing>(
                ProgressRingDescriptor.Descriptor));

        rec.RegisterHandler<InfoBadgeElement, WinUI.InfoBadge>(
            new DescriptorHandler<InfoBadgeElement, WinUI.InfoBadge>(
                InfoBadgeDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 4) — Button family ports.
        rec.RegisterHandler<ButtonElement, WinUI.Button>(
            new DescriptorHandler<ButtonElement, WinUI.Button>(
                ButtonDescriptor.Descriptor));

        rec.RegisterHandler<HyperlinkButtonElement, WinUI.HyperlinkButton>(
            new DescriptorHandler<HyperlinkButtonElement, WinUI.HyperlinkButton>(
                HyperlinkButtonDescriptor.Descriptor));

        rec.RegisterHandler<RepeatButtonElement, Microsoft.UI.Xaml.Controls.Primitives.RepeatButton>(
            new DescriptorHandler<RepeatButtonElement, Microsoft.UI.Xaml.Controls.Primitives.RepeatButton>(
                RepeatButtonDescriptor.Descriptor));

        rec.RegisterHandler<ToggleButtonElement, Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(
            new DescriptorHandler<ToggleButtonElement, Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(
                ToggleButtonDescriptor.Descriptor));

        rec.RegisterHandler<DropDownButtonElement, WinUI.DropDownButton>(
            new DescriptorHandler<DropDownButtonElement, WinUI.DropDownButton>(
                DropDownButtonDescriptor.Descriptor));

        rec.RegisterHandler<SplitButtonElement, WinUI.SplitButton>(
            new DescriptorHandler<SplitButtonElement, WinUI.SplitButton>(
                SplitButtonDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 5) — Value-bearing input ports.
        rec.RegisterHandler<RichEditBoxElement, WinUI.RichEditBox>(
            new DescriptorHandler<RichEditBoxElement, WinUI.RichEditBox>(
                RichEditBoxDescriptor.Descriptor));

        rec.RegisterHandler<PasswordBoxElement, WinUI.PasswordBox>(
            new DescriptorHandler<PasswordBoxElement, WinUI.PasswordBox>(
                PasswordBoxDescriptor.Descriptor));

        rec.RegisterHandler<RadioButtonsElement, WinUI.RadioButtons>(
            new DescriptorHandler<RadioButtonsElement, WinUI.RadioButtons>(
                RadioButtonsDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 6) — Multi-event input ports.
        rec.RegisterHandler<AutoSuggestBoxElement, WinUI.AutoSuggestBox>(
            new DescriptorHandler<AutoSuggestBoxElement, WinUI.AutoSuggestBox>(
                AutoSuggestBoxDescriptor.Descriptor));

        rec.RegisterHandler<ComboBoxElement, WinUI.ComboBox>(
            new DescriptorHandler<ComboBoxElement, WinUI.ComboBox>(
                ComboBoxDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 7) — Single-content container ports.
        rec.RegisterHandler<ViewboxElement, WinUI.Viewbox>(
            new DescriptorHandler<ViewboxElement, WinUI.Viewbox>(
                ViewboxDescriptor.Descriptor));

        rec.RegisterHandler<ExpanderElement, WinUI.Expander>(
            new DescriptorHandler<ExpanderElement, WinUI.Expander>(
                ExpanderDescriptor.Descriptor));

        rec.RegisterHandler<ScrollViewerElement, WinUI.ScrollViewer>(
            new DescriptorHandler<ScrollViewerElement, WinUI.ScrollViewer>(
                ScrollViewerDescriptor.Descriptor));

        rec.RegisterHandler<ScrollViewElement, WinUI.ScrollView>(
            new DescriptorHandler<ScrollViewElement, WinUI.ScrollView>(
                ScrollViewDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 8) — Panel container ports.
        rec.RegisterHandler<StackElement, WinUI.StackPanel>(
            new DescriptorHandler<StackElement, WinUI.StackPanel>(
                StackPanelDescriptor.Descriptor));

        rec.RegisterHandler<GridElement, WinUI.Grid>(
            new DescriptorHandler<GridElement, WinUI.Grid>(
                GridDescriptor.Descriptor));

        rec.RegisterHandler<CanvasElement, WinUI.Canvas>(
            new DescriptorHandler<CanvasElement, WinUI.Canvas>(
                CanvasDescriptor.Descriptor));

        rec.RegisterHandler<FlexElement, Microsoft.UI.Reactor.Layout.FlexPanel>(
            new DescriptorHandler<FlexElement, Microsoft.UI.Reactor.Layout.FlexPanel>(
                FlexPanelDescriptor.Descriptor));

        rec.RegisterHandler<RelativePanelElement, WinUI.RelativePanel>(
            new DescriptorHandler<RelativePanelElement, WinUI.RelativePanel>(
                RelativePanelDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 9) — Named-slot container ports.
        rec.RegisterHandler<SplitViewElement, WinUI.SplitView>(
            new DescriptorHandler<SplitViewElement, WinUI.SplitView>(
                SplitViewDescriptor.Descriptor));

        rec.RegisterHandler<InfoBarElement, WinUI.InfoBar>(
            new DescriptorHandler<InfoBarElement, WinUI.InfoBar>(
                InfoBarDescriptor.Descriptor));

        rec.RegisterHandler<TeachingTipElement, WinUI.TeachingTip>(
            new DescriptorHandler<TeachingTipElement, WinUI.TeachingTip>(
                TeachingTipDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 10) — Shape + display-leaf ports.
        rec.RegisterHandler<RectangleElement, Microsoft.UI.Xaml.Shapes.Rectangle>(
            new DescriptorHandler<RectangleElement, Microsoft.UI.Xaml.Shapes.Rectangle>(
                RectangleDescriptor.Descriptor));

        rec.RegisterHandler<EllipseElement, Microsoft.UI.Xaml.Shapes.Ellipse>(
            new DescriptorHandler<EllipseElement, Microsoft.UI.Xaml.Shapes.Ellipse>(
                EllipseDescriptor.Descriptor));

        rec.RegisterHandler<LineElement, Microsoft.UI.Xaml.Shapes.Line>(
            new DescriptorHandler<LineElement, Microsoft.UI.Xaml.Shapes.Line>(
                LineDescriptor.Descriptor));

        rec.RegisterHandler<PathElement, Microsoft.UI.Xaml.Shapes.Path>(
            new DescriptorHandler<PathElement, Microsoft.UI.Xaml.Shapes.Path>(
                PathDescriptor.Descriptor));

        rec.RegisterHandler<AnimatedIconElement, WinUI.AnimatedIcon>(
            new DescriptorHandler<AnimatedIconElement, WinUI.AnimatedIcon>(
                AnimatedIconDescriptor.Descriptor));

        // Spec 047 §14 Phase 3 (batch 11) — Long-tail ports.
        rec.RegisterHandler<PipsPagerElement, WinUI.PipsPager>(
            new DescriptorHandler<PipsPagerElement, WinUI.PipsPager>(
                PipsPagerDescriptor.Descriptor));

        rec.RegisterHandler<ListBoxElement, WinUI.ListBox>(
            new DescriptorHandler<ListBoxElement, WinUI.ListBox>(
                ListBoxDescriptor.Descriptor));

        rec.RegisterHandler<SelectorBarElement, WinUI.SelectorBar>(
            new DescriptorHandler<SelectorBarElement, WinUI.SelectorBar>(
                SelectorBarDescriptor.Descriptor));

        rec.RegisterHandler<BreadcrumbBarElement, WinUI.BreadcrumbBar>(
            new DescriptorHandler<BreadcrumbBarElement, WinUI.BreadcrumbBar>(
                BreadcrumbBarDescriptor.Descriptor));

        rec.RegisterHandler<ListViewElement, WinUI.ListView>(new ListViewHandler());

        return rec;
    }
}
