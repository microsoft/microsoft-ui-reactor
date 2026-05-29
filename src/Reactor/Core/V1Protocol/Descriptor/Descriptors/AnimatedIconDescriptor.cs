using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 10) — descriptor variant of the hand-coded
/// <c>MountAnimatedIcon</c> / <c>UpdateAnimatedIcon</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event display leaf. <c>Source</c> and
/// <c>FallbackIconSource</c> are both written when non-null. <c>Source</c>
/// is a runtime-checked <see cref="WinUI.IAnimatedVisualSource2"/> — the
/// legacy arm silently no-ops when the supplied object isn't of that type,
/// and the descriptor mirrors that behavior in its <c>set</c> lambda.</para>
///
/// <para><b>Phase 1 parity note:</b> AnimatedIcon has no events that the
/// legacy arm wires up; the State property is settable through the
/// <c>Setters</c> array (callers go via <c>.Set</c>) — there's no top-level
/// <c>State</c> property on <c>AnimatedIconElement</c>, so the descriptor
/// surface is exactly the two source-typed props the legacy arm writes.</para>
/// </summary>
internal static class AnimatedIconDescriptor
{
    public static readonly ControlDescriptor<AnimatedIconElement, WinUI.AnimatedIcon> Descriptor =
        new ControlDescriptor<AnimatedIconElement, WinUI.AnimatedIcon>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.Source,
            set:         static (c, v) =>
            {
                if (v is WinUI.IAnimatedVisualSource2 src)
                    c.Source = src;
            },
            shouldWrite: static e => e.Source is not null)
        .OneWayConditional(
            get:         static e => e.FallbackIconSource,
            set:         static (c, v) => c.FallbackIconSource = v,
            shouldWrite: static e => e.FallbackIconSource is not null);
}
