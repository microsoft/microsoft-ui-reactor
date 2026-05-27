using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 7) — descriptor variant of the hand-coded
/// <c>MountViewbox</c> / <c>UpdateViewbox</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a pure single-content container with zero events
/// and two conditional one-way props (<c>Stretch</c>, <c>StretchDirection</c>).
/// Mirrors the <see cref="BorderDescriptor"/> shape — only the child slot
/// strategy and the two enum-valued props differ. The hand-coded handler
/// conditionally writes each prop only when the element has a value; the
/// descriptor preserves that gate via
/// <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional"/>.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class ViewboxDescriptor
{
    private static readonly SingleContent<ViewboxElement, WinUI.Viewbox> ChildrenStrategy =
        new SingleContent<ViewboxElement, WinUI.Viewbox>(
            GetChild: static el => el.Child,
            SetChild: static (ctrl, ui) => ctrl.Child = ui as global::Microsoft.UI.Xaml.UIElement)
        {
            GetCurrentChild = static ctrl => ctrl.Child,
        };

    public static readonly ControlDescriptor<ViewboxElement, WinUI.Viewbox> Descriptor =
        new ControlDescriptor<ViewboxElement, WinUI.Viewbox>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.Stretch,
            set:         static (c, v) => c.Stretch = v!.Value,
            shouldWrite: static e => e.Stretch.HasValue)
        .OneWayConditional(
            get:         static e => e.StretchDirection,
            set:         static (c, v) => c.StretchDirection = v!.Value,
            shouldWrite: static e => e.StretchDirection.HasValue);
}
