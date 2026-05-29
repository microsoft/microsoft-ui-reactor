using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 3) — descriptor variant of the hand-coded
/// <c>MountProgressRing</c> / <c>UpdateProgressRing</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event circular progress indicator. Five
/// one-way props — four non-nullable (<c>IsIndeterminate</c>,
/// <c>IsActive</c>, <c>Minimum</c>, <c>Maximum</c>) plus one nullable
/// (<c>Value</c>).</para>
///
/// <para><b>Phase 1 parity note:</b> the legacy <c>UpdateProgressRing</c>
/// only writes <c>IsIndeterminate</c>, <c>IsActive</c> and the optional
/// <c>Value</c>; the descriptor additionally diff-writes <c>Minimum</c> and
/// <c>Maximum</c> (which Mount sets unconditionally but Update skips). The
/// per-prop diff means we only touch them when the element value changes,
/// so this is a tighter superset — no behavior delta for round-tripped
/// elements.</para>
/// </summary>
internal static class ProgressRingDescriptor
{
    public static readonly ControlDescriptor<ProgressRingElement, WinUI.ProgressRing> Descriptor =
        new ControlDescriptor<ProgressRingElement, WinUI.ProgressRing>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.IsIndeterminate,
            set: static (c, v) => c.IsIndeterminate = v)
        .OneWay(
            get: static e => e.IsActive,
            set: static (c, v) => c.IsActive = v)
        .OneWay(
            get: static e => e.Minimum,
            set: static (c, v) => c.Minimum = v)
        .OneWay(
            get: static e => e.Maximum,
            set: static (c, v) => c.Maximum = v)
        .OneWayConditional(
            get:         static e => e.Value,
            set:         static (c, v) => c.Value = v!.Value,
            shouldWrite: static e => e.Value.HasValue);
}
