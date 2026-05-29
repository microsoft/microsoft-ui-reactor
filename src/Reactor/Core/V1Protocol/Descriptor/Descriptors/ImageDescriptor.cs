using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 3) — descriptor variant of the hand-coded
/// <c>MountImage</c> / <c>UpdateImage</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a display leaf with a single complex prop
/// (<c>Source</c> — string parsed to <see cref="Uri"/>, then to either
/// <see cref="BitmapImage"/> or <see cref="SvgImageSource"/>) plus three
/// optional layout props, plus <c>ImageOpened</c> / <c>ImageFailed</c> as
/// fire-only <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
/// entries (Phase 3-final Batch F).</para>
///
/// <para><b>Behavior parity vs. legacy:</b> the legacy <c>MountImage</c> arm
/// threads the element through <c>EnsureImageWiring</c> trampolines so
/// <c>ImageOpened</c>/<c>ImageFailed</c> route back to the element
/// callbacks. The descriptor reuses <see cref="ImageEventPayload"/> with the
/// established slot-gating shape; trampolines read the live element via
/// <see cref="Reconciler.GetElementTag"/> and fire the corresponding
/// callback only when it's wired — mirrors the
/// "always subscribe, read latest callback per fire" pattern.</para>
///
/// <para><b>Known nuances vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b><c>Source</c> diffing:</b> the legacy arm reassigns
///   <c>image.Source</c> only when the source string changes
///   (<c>o.Source != n.Source</c>). The descriptor's per-prop comparer
///   captures the same intent — write happens only when the string differs.</item>
///   <item><b>Malformed URI:</b> the legacy <c>MountImage</c> swallows
///   <see cref="UriFormatException"/> at construction time. The descriptor
///   mirrors this exactly in its <c>set</c> lambda.</item>
///   <item><b>Mount-time event ordering:</b> the legacy arm wires
///   ImageOpened/ImageFailed BEFORE assigning Source so a cached-image
///   synchronous fire still routes. The V1 handler adapter wires
///   <c>.HandCodedEvent</c> entries in the order the descriptor declares
///   them — the event entries are declared BEFORE the Source <c>.OneWay</c>
///   entry below, so the same synchronous-fire path is preserved.</item>
/// </list></para>
/// </summary>
internal static class ImageDescriptor
{
    private static readonly RoutedEventHandler ImageOpenedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((UIElement)s!) as ImageElement)?.OnImageOpened?.Invoke();

    private static readonly ExceptionRoutedEventHandler ImageFailedTrampoline = (s, args) =>
        (Reconciler.GetElementTag((UIElement)s!) as ImageElement)?.OnImageFailed?.Invoke(args.ErrorMessage);

    public static readonly ControlDescriptor<ImageElement, WinUI.Image> Descriptor =
        new ControlDescriptor<ImageElement, WinUI.Image>
        {
            GetSetters = static e => e.Setters,
        }
        // Event entries first so subscriptions land BEFORE the Source write
        // (cached images can synchronously fire ImageOpened during the assign).
        .HandCodedEvent<ImageEventPayload, RoutedEventHandler>(
            subscribe:        static (c, h) => c.ImageOpened += h,
            callbackPresent:  static e => e.OnImageOpened,
            trampoline:       ImageOpenedTrampoline,
            slotIsNull:       static p => p.ImageOpenedTrampoline is null,
            setSlot:          static (p, h) => p.ImageOpenedTrampoline = h)
        .HandCodedEvent<ImageEventPayload, ExceptionRoutedEventHandler>(
            subscribe:        static (c, h) => c.ImageFailed += h,
            callbackPresent:  static e => e.OnImageFailed,
            trampoline:       ImageFailedTrampoline,
            slotIsNull:       static p => p.ImageFailedTrampoline is null,
            setSlot:          static (p, h) => p.ImageFailedTrampoline = h)
        .OneWay(
            get: static e => e.Source,
            set: static (c, v) =>
            {
                try
                {
                    var uri = new Uri(v, UriKind.RelativeOrAbsolute);
                    c.Source = v.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        ? new SvgImageSource(uri)
                        : new BitmapImage(uri);
                }
                catch (UriFormatException)
                {
                    // Mirror legacy: leave source empty rather than crashing.
                }
            })
        .OneWayConditional(
            get:         static e => e.Width,
            set:         static (c, v) => c.Width = v!.Value,
            shouldWrite: static e => e.Width.HasValue)
        .OneWayConditional(
            get:         static e => e.Height,
            set:         static (c, v) => c.Height = v!.Value,
            shouldWrite: static e => e.Height.HasValue)
        .OneWayConditional(
            get:         static e => e.NineGrid,
            set:         static (c, v) => c.NineGrid = v!.Value,
            shouldWrite: static e => e.NineGrid.HasValue);
}
