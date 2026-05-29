using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
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
/// <para><b>§14 Phase 3-final Batch E:</b> per-child
/// <see cref="CanvasAttached"/> (Canvas.Left / Canvas.Top, plus the
/// AnchorX / AnchorY post-layout offset) is now applied via
/// <see cref="Panel{TElement,TControl}.PerChildAttached"/>. The callback
/// delegates to <see cref="Reconciler.ApplyCanvasPosition"/> so descriptor
/// children share the same anchor-state ConditionalWeakTable + size-change
/// subscription used by the legacy <c>MountCanvas</c> arm.</para>
/// </summary>
internal static class CanvasDescriptor
{
    private static readonly Panel<CanvasElement, WinUI.Canvas> ChildrenStrategy =
        new Panel<CanvasElement, WinUI.Canvas>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children)
        {
            PerChildAttached = static (canvas, ui, childEl) =>
            {
                if (ui is not FrameworkElement fe) return;
                var ca = childEl.GetAttached<CanvasAttached>();
                if (ca is null)
                {
                    fe.ClearValue(WinUI.Canvas.LeftProperty);
                    fe.ClearValue(WinUI.Canvas.TopProperty);
                    return;
                }
                Reconciler.ApplyCanvasPosition(fe, ca);
            },
        };

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
