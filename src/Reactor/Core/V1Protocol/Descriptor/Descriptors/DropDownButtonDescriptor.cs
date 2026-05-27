using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountDropDownButton</c> / <c>UpdateDropDownButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> <c>Label</c> one-way (Content). DropDownButton's
/// legacy arm has no <c>Click</c> event subscription.</para>
///
/// <para><b>Known gap vs. hand-coded handler:</b> <c>Flyout</c> is
/// escape-hatched — it requires the engine-internal
/// <c>CreateFlyoutFromElement</c> helper which the descriptor builders
/// don't yet expose. Authors needing a Flyout fall through to the
/// legacy arm (V1 OFF) or use setters chain.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class DropDownButtonDescriptor
{
    public static readonly ControlDescriptor<DropDownButtonElement, WinUI.DropDownButton> Descriptor =
        new ControlDescriptor<DropDownButtonElement, WinUI.DropDownButton>
        {
            Children = new None<DropDownButtonElement, WinUI.DropDownButton>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v);
}
