using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 2 (Q1 spike) — descriptor variant of
/// <see cref="V1Protocol.Handlers.BorderHandler"/>.
///
/// <para><b>Coverage:</b> exercises the pure-container path — no events,
/// four conditional one-way props (CornerRadius / Background / BorderBrush /
/// BorderThickness), and the <see cref="SingleContent{TElement,TControl}"/>
/// strategy. Validates that the interpreter integrates cleanly with the
/// existing children-strategy dispatch.</para>
///
/// <para><b>Phase 1 parity note:</b> the hand-coded handler conditionally
/// writes every prop only when the element has a value (e.g.
/// <c>el.CornerRadius.HasValue</c>). The descriptor mirrors this exactly
/// via <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional"/>
/// — the engine never "re-defaults" the control on a prop transition from
/// non-null to null. Matches the Phase 1 behavior the existing fixtures
/// (BorderPortTests, Spec047V1ProtocolFixtures) lock in.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class BorderDescriptor
{
    private static readonly SingleContent<BorderElement, WinUI.Border> ChildrenStrategy =
        new SingleContent<BorderElement, WinUI.Border>(
            GetChild: static el => el.Child,
            SetChild: static (ctrl, ui) => ctrl.Child = ui)
        {
            GetCurrentChild = static ctrl => ctrl.Child,
        };

    public static readonly ControlDescriptor<BorderElement, WinUI.Border> Descriptor =
        new ControlDescriptor<BorderElement, WinUI.Border>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.CornerRadius,
            set:         static (c, v) => c.CornerRadius = new CornerRadius(v!.Value),
            shouldWrite: static e => e.CornerRadius.HasValue)
        .OneWayConditional(
            get:         static e => e.Background,
            set:         static (c, v) => c.Background = v,
            shouldWrite: static e => e.Background is not null)
        .OneWayConditional(
            get:         static e => e.BorderBrush,
            set:         static (c, v) => c.BorderBrush = v,
            shouldWrite: static e => e.BorderBrush is not null)
        .OneWayConditional(
            get:         static e => e.BorderThickness,
            set:         static (c, v) => c.BorderThickness = new Thickness(v!.Value),
            shouldWrite: static e => e.BorderThickness.HasValue);
}
