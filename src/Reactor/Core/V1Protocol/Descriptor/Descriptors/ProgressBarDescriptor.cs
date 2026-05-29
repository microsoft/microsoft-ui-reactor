using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 3) — descriptor variant of the hand-coded
/// <c>MountProgress</c> / <c>UpdateProgress</c> arms in
/// <see cref="Reconciler"/>. Note: the element type is
/// <see cref="ProgressElement"/> but the WinUI control is
/// <see cref="WinUI.ProgressBar"/>; the descriptor file is named after the
/// control for consistency with the other batch 3 descriptors.
///
/// <para><b>Coverage:</b> a zero-event progress indicator. Six one-way
/// props — five non-nullable (<c>IsIndeterminate</c>, <c>Minimum</c>,
/// <c>Maximum</c>, <c>ShowError</c>, <c>ShowPaused</c>) plus one nullable
/// (<c>Value</c>). Mirrors the legacy arm exactly: <c>Value</c> is only
/// written when <c>HasValue</c>, all others are always written.</para>
/// </summary>
internal static class ProgressBarDescriptor
{
    public static readonly ControlDescriptor<ProgressElement, WinUI.ProgressBar> Descriptor =
        new ControlDescriptor<ProgressElement, WinUI.ProgressBar>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.IsIndeterminate,
            set: static (c, v) => c.IsIndeterminate = v)
        .OneWay(
            get: static e => e.Minimum,
            set: static (c, v) => c.Minimum = v)
        .OneWay(
            get: static e => e.Maximum,
            set: static (c, v) => c.Maximum = v)
        .OneWay(
            get: static e => e.ShowError,
            set: static (c, v) => c.ShowError = v)
        .OneWay(
            get: static e => e.ShowPaused,
            set: static (c, v) => c.ShowPaused = v)
        .OneWayConditional(
            get:         static e => e.Value,
            set:         static (c, v) => c.Value = v!.Value,
            shouldWrite: static e => e.Value.HasValue);
}
