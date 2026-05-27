using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 8) — descriptor variant of the hand-coded
/// <c>MountCanvas</c> / <c>UpdateCanvas</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event panel container with three
/// conditional one-way props (<c>Width</c>, <c>Height</c>, <c>Background</c>).
/// Children are dispatched through the
/// <see cref="Panel{TElement,TControl}"/> strategy.</para>
///
/// <para><b>Known gaps:</b> the legacy hand-coded path applies
/// <see cref="CanvasAttached"/> (Canvas.Left / Canvas.Top) per child after
/// children mount. The Panel strategy in V1HandlerAdapter doesn't surface a
/// per-child post-mount hook yet, so descriptor-mounted children stay at
/// the panel origin. Authors who need <c>Canvas.SetLeft</c> /
/// <c>Canvas.SetTop</c> stay on V1 OFF (legacy arm). Pure-children scenarios
/// have parity.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class CanvasDescriptor
{
    private static readonly Panel<CanvasElement, WinUI.Canvas> ChildrenStrategy =
        new Panel<CanvasElement, WinUI.Canvas>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children);

    public static readonly ControlDescriptor<CanvasElement, WinUI.Canvas> Descriptor =
        new ControlDescriptor<CanvasElement, WinUI.Canvas>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.Width,
            set:         static (c, v) => c.Width = v!.Value,
            shouldWrite: static e => e.Width.HasValue)
        .OneWayConditional(
            get:         static e => e.Height,
            set:         static (c, v) => c.Height = v!.Value,
            shouldWrite: static e => e.Height.HasValue)
        .OneWayConditional(
            get:         static e => e.Background,
            set:         static (c, v) => c.Background = v,
            shouldWrite: static e => e.Background is not null);
}
