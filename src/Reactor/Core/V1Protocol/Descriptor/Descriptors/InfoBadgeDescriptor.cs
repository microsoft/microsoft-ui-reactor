using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 3) — descriptor variant of the hand-coded
/// <c>MountInfoBadge</c> / <c>UpdateInfoBadge</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a minimal zero-event badge — one conditional
/// one-way prop (<c>Value</c>).</para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b><c>Icon</c>:</b> the legacy <c>MountInfoBadge</c> and
///   <c>UpdateInfoBadge</c> do NOT write <c>Icon</c> either — the element
///   record exposes it but the legacy arm ignores it. The descriptor
///   mirrors that gap exactly. Authors who need icon badges land on
///   modifiers / setters.</item>
/// </list></para>
/// </summary>
internal static class InfoBadgeDescriptor
{
    public static readonly ControlDescriptor<InfoBadgeElement, WinUI.InfoBadge> Descriptor =
        new ControlDescriptor<InfoBadgeElement, WinUI.InfoBadge>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.Value,
            set:         static (c, v) => c.Value = v!.Value,
            shouldWrite: static e => e.Value.HasValue);
}
