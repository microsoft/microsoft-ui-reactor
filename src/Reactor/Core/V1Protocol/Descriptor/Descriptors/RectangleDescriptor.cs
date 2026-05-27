using System.Diagnostics.CodeAnalysis;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 10) — descriptor variant of the hand-coded
/// <c>MountRectangle</c> / <c>UpdateRectangle</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event shape leaf. <c>Fill</c> / <c>Stroke</c>
/// are written when non-null; <c>StrokeThickness</c>, <c>RadiusX</c>,
/// <c>RadiusY</c> are <see cref="double"/>-valued props.</para>
///
/// <para><b>Phase 1 parity note:</b> the legacy mount writes
/// <c>StrokeThickness</c> / <c>RadiusX</c> / <c>RadiusY</c> only when
/// <c>&gt; 0</c>, while the legacy <c>UpdateRectangle</c> writes them
/// unconditionally. We mirror the <i>update</i> behavior (unconditional
/// <see cref="ControlDescriptor{TElement,TControl}.OneWay"/>) — the per-prop
/// comparer makes the write a no-op when the values match anyway, and the
/// engine doesn't distinguish mount vs. update sites. The element's default
/// zero values keep the visible-output equivalent to the legacy mount for
/// callers who never set them.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class RectangleDescriptor
{
    public static readonly ControlDescriptor<RectangleElement, WinShapes.Rectangle> Descriptor =
        new ControlDescriptor<RectangleElement, WinShapes.Rectangle>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.Fill,
            set:         static (c, v) => c.Fill = v,
            shouldWrite: static e => e.Fill is not null)
        .OneWayConditional(
            get:         static e => e.Stroke,
            set:         static (c, v) => c.Stroke = v,
            shouldWrite: static e => e.Stroke is not null)
        .OneWay(
            get: static e => e.StrokeThickness,
            set: static (c, v) => c.StrokeThickness = v)
        .OneWay(
            get: static e => e.RadiusX,
            set: static (c, v) => c.RadiusX = v)
        .OneWay(
            get: static e => e.RadiusY,
            set: static (c, v) => c.RadiusY = v);
}
