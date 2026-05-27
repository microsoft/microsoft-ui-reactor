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
/// three Q1 head-to-head ports — ToggleSwitch, Slider, Border. The remaining
/// ports (TextBox, ListView) keep their hand-coded Phase 1 handlers so every
/// bench still has a working dispatch for every control type; M1 / M2 / M5
/// / M7 / M10 land directly on the contested controls so the descriptor
/// tax is exposed where it matters.</para>
/// </summary>
internal static class DescriptorVariantFactory
{
    public static Reconciler Create()
    {
        // Internal ctor: V1 ON but no auto-register, so we can plug
        // descriptor handlers in for the Q1 head-to-head ports while leaving
        // the remaining Phase 1 ports on hand-coded handlers.
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

        // Non-contested ports keep their Phase 1 hand-coded handlers — every
        // bench still has a working dispatch for every control.
        rec.RegisterHandler<TextBoxElement, WinUI.TextBox>(new TextBoxHandler());
        rec.RegisterHandler<ListViewElement, WinUI.ListView>(new ListViewHandler());

        return rec;
    }
}
