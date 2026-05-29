using System.Diagnostics.CodeAnalysis;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 7) — descriptor variant of the hand-coded
/// <c>MountScrollView</c> / <c>UpdateScrollView</c> arms in
/// <see cref="Reconciler"/>. Targets the modern
/// <see cref="WinUI.ScrollView"/> (InteractionTracker-backed) — the typed
/// surface is parallel to <see cref="ScrollViewerElement"/> but uses the
/// <c>Scrolling*</c> enum family.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Content</c> — <see cref="SingleContent{TElement,TControl}"/>.</item>
///   <item><c>ViewChanged</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   against the existing <see cref="ScrollViewEventPayload"/>. The modern
///   control's <c>ViewChanged</c> reports only the settled value, so the
///   element's callback shape is parameterless <c>Action</c>.</item>
///   <item><c>ContentOrientation</c>, both scroll-bar visibilities, both
///   scroll modes, <c>ZoomMode</c>, <c>MinZoomFactor</c>, <c>MaxZoomFactor</c>,
///   <c>HorizontalAnchorRatio</c>, <c>VerticalAnchorRatio</c> — all one-way
///   writes that mirror the legacy mount/update arms verbatim.</item>
/// </list></para>
/// </summary>
internal static class ScrollViewDescriptor
{
    private static readonly SingleContent<ScrollViewElement, WinUI.ScrollView> ChildrenStrategy =
        new SingleContent<ScrollViewElement, WinUI.ScrollView>(
            GetChild: static el => el.Child,
            SetChild: static (ctrl, ui) => ctrl.Content = ui)
        {
            GetCurrentChild = static ctrl => ctrl.Content as global::Microsoft.UI.Xaml.UIElement,
        };

    private static readonly TypedEventHandler<WinUI.ScrollView, object>
        ViewChangedTrampoline = (s, _) =>
            (Reconciler.GetElementTag((WinUI.ScrollView)s!) as ScrollViewElement)
                ?.OnViewChanged?.Invoke();

    public static readonly ControlDescriptor<ScrollViewElement, WinUI.ScrollView> Descriptor =
        new ControlDescriptor<ScrollViewElement, WinUI.ScrollView>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.ContentOrientation,
            set: static (c, v) => c.ContentOrientation = v)
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
        .OneWay(
            get: static e => e.MinZoomFactor,
            set: static (c, v) => c.MinZoomFactor = v)
        .OneWay(
            get: static e => e.MaxZoomFactor,
            set: static (c, v) => c.MaxZoomFactor = v)
        .OneWay(
            get: static e => e.HorizontalAnchorRatio,
            set: static (c, v) => c.HorizontalAnchorRatio = v)
        .OneWay(
            get: static e => e.VerticalAnchorRatio,
            set: static (c, v) => c.VerticalAnchorRatio = v)
        .HandCodedEvent<ScrollViewEventPayload,
            TypedEventHandler<WinUI.ScrollView, object>>(
            subscribe:        static (c, h) => c.ViewChanged += h,
            callbackPresent:  static e => e.OnViewChanged,
            trampoline:       ViewChangedTrampoline,
            slotIsNull:       static p => p.ViewChangedTrampoline is null,
            setSlot:          static (p, h) => p.ViewChangedTrampoline = h);
}
