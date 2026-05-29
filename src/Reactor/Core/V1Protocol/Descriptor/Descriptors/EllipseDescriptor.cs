using System.Diagnostics.CodeAnalysis;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 10) — descriptor variant of the hand-coded
/// <c>MountEllipse</c> / <c>UpdateEllipse</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event shape leaf. <c>Fill</c> / <c>Stroke</c>
/// are written when non-null; <c>StrokeThickness</c> is a <see cref="double"/>
/// prop.</para>
///
/// <para><b>Phase 1 parity note:</b> as with <see cref="RectangleDescriptor"/>
/// — legacy mount writes <c>StrokeThickness</c> only when <c>&gt; 0</c>;
/// update writes it unconditionally. The descriptor mirrors the update path
/// (plain <see cref="ControlDescriptor{TElement,TControl}.OneWay"/>); zero
/// remains the element's default so unset callers observe legacy behavior.</para>
/// </summary>
internal static class EllipseDescriptor
{
    public static readonly ControlDescriptor<EllipseElement, WinShapes.Ellipse> Descriptor =
        new ControlDescriptor<EllipseElement, WinShapes.Ellipse>
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
            set: static (c, v) => c.StrokeThickness = v);
}
