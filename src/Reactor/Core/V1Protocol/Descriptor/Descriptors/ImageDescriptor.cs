using System;
using System.Diagnostics.CodeAnalysis;
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
/// optional layout props.</para>
///
/// <para><b>Phase 1 parity note:</b> the legacy arm threads the element
/// through <c>EnsureImageWiring</c> trampolines so <c>ImageOpened</c> /
/// <c>ImageFailed</c> can route back to the element callbacks. The
/// descriptor relies on the V1 handler adapter to set the element tag (it
/// already does before invoking the descriptor) — but the descriptor never
/// calls <c>EnsureImageWiring</c>, so <c>ImageOpened</c> /
/// <c>ImageFailed</c> events ARE NOT fired by the descriptor path.</para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b><c>OnImageOpened</c> / <c>OnImageFailed</c> callbacks:</b> not
///   fired by the descriptor — Batch 3 covers zero-event props only (no
///   <c>Controlled</c> / <c>HandCoded*</c>). Authors who need image-load
///   notifications must continue to use the legacy arm (V1-OFF) until the
///   descriptor is extended.</item>
///   <item><b><c>Source</c> diffing:</b> the legacy arm reassigns
///   <c>image.Source</c> only when the source string changes
///   (<c>o.Source != n.Source</c>). The descriptor's per-prop comparer
///   captures the same intent — write happens only when the string differs.</item>
///   <item><b>Malformed URI:</b> the legacy <c>MountImage</c> swallows
///   <see cref="UriFormatException"/> at construction time. The descriptor
///   mirrors this exactly in its <c>set</c> lambda.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class ImageDescriptor
{
    public static readonly ControlDescriptor<ImageElement, WinUI.Image> Descriptor =
        new ControlDescriptor<ImageElement, WinUI.Image>
        {
            GetSetters = static e => e.Setters,
        }
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
