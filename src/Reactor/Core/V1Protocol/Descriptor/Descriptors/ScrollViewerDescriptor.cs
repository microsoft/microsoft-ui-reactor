using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 7) — descriptor variant of the hand-coded
/// <c>MountScrollViewer</c> / <c>UpdateScrollViewer</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Content</c> — <see cref="SingleContent{TElement,TControl}"/>.</item>
///   <item><c>ViewChanged</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   against the existing <see cref="ScrollViewerEventPayload"/>. The event
///   is fire-only (no DP round-trip), so the regular <c>.HandCodedEvent</c>
///   path applies.</item>
///   <item><c>HorizontalScrollBarVisibility</c> / <c>VerticalScrollBarVisibility</c>
///   / <c>HorizontalScrollMode</c> / <c>VerticalScrollMode</c> / <c>ZoomMode</c>
///   — one-way enum writes. Note the legacy arm casts the element's mode
///   to <see cref="WinUI.ScrollMode"/> / <see cref="WinUI.ZoomMode"/> on each
///   write (element type IS the WinUI enum, so the cast is a no-op for the
///   descriptor's typed write).</item>
/// </list></para>
/// </summary>
internal static class ScrollViewerDescriptor
{
    private static readonly SingleContent<ScrollViewerElement, WinUI.ScrollViewer> ChildrenStrategy =
        new SingleContent<ScrollViewerElement, WinUI.ScrollViewer>(
            GetChild: static el => el.Child,
            SetChild: static (ctrl, ui) => ctrl.Content = ui)
        {
            GetCurrentChild = static ctrl => ctrl.Content as global::Microsoft.UI.Xaml.UIElement,
        };

    private static readonly global::System.EventHandler<WinUI.ScrollViewerViewChangedEventArgs>
        ViewChangedTrampoline = (s, args) =>
            (Reconciler.GetElementTag((WinUI.ScrollViewer)s!) as ScrollViewerElement)
                ?.OnViewChanged?.Invoke(args);

    public static readonly ControlDescriptor<ScrollViewerElement, WinUI.ScrollViewer> Descriptor =
        new ControlDescriptor<ScrollViewerElement, WinUI.ScrollViewer>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.HorizontalScrollBarVisibility,
            set: static (c, v) => c.HorizontalScrollBarVisibility = v)
        .OneWay(
            get: static e => e.VerticalScrollBarVisibility,
            set: static (c, v) => c.VerticalScrollBarVisibility = v)
        .OneWay(
            get: static e => e.HorizontalScrollMode,
            set: static (c, v) => c.HorizontalScrollMode = v)
        .OneWay(
            get: static e => e.VerticalScrollMode,
            set: static (c, v) => c.VerticalScrollMode = v)
        .OneWay(
            get: static e => e.ZoomMode,
            set: static (c, v) => c.ZoomMode = v)
        .HandCodedEvent<ScrollViewerEventPayload,
            global::System.EventHandler<WinUI.ScrollViewerViewChangedEventArgs>>(
            subscribe:        static (c, h) => c.ViewChanged += h,
            callbackPresent:  static e => e.OnViewChanged,
            trampoline:       ViewChangedTrampoline,
            slotIsNull:       static p => p.ViewChangedTrampoline is null,
            setSlot:          static (p, h) => p.ViewChangedTrampoline = h);
}
