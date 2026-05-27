using System.Diagnostics.CodeAnalysis;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 10) — descriptor variant of the hand-coded
/// <c>MountLine</c> / <c>UpdateLine</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event shape leaf. <c>X1</c> / <c>Y1</c> /
/// <c>X2</c> / <c>Y2</c> / <c>StrokeThickness</c> are unconditional doubles.
/// <c>Stroke</c> is written only when non-null (legacy parity).</para>
///
/// <para><b>Phase 1 parity note:</b> the legacy mount writes
/// <c>StrokeThickness</c> only when <c>&gt; 0</c>; the legacy update writes
/// it unconditionally. The descriptor mirrors the update path with plain
/// <see cref="ControlDescriptor{TElement,TControl}.OneWay"/>. The element
/// defaults <c>StrokeThickness</c> to <c>1</c>, matching WinUI default.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class LineDescriptor
{
    public static readonly ControlDescriptor<LineElement, WinShapes.Line> Descriptor =
        new ControlDescriptor<LineElement, WinShapes.Line>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.X1,
            set: static (c, v) => c.X1 = v)
        .OneWay(
            get: static e => e.Y1,
            set: static (c, v) => c.Y1 = v)
        .OneWay(
            get: static e => e.X2,
            set: static (c, v) => c.X2 = v)
        .OneWay(
            get: static e => e.Y2,
            set: static (c, v) => c.Y2 = v)
        .OneWayConditional(
            get:         static e => e.Stroke,
            set:         static (c, v) => c.Stroke = v,
            shouldWrite: static e => e.Stroke is not null)
        .OneWay(
            get: static e => e.StrokeThickness,
            set: static (c, v) => c.StrokeThickness = v);
}
