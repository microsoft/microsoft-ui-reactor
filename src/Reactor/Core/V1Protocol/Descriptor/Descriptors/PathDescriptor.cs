using System.Diagnostics.CodeAnalysis;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 10) — descriptor variant of the hand-coded
/// <c>MountPath</c> / <c>UpdatePath</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event shape leaf — styling and stroke props
/// only. All paint / dash / cap / join / transform props use
/// <see cref="ControlDescriptor{TElement,TControl}.OneWay"/> or
/// <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional"/>.</para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b><c>Data</c> / <c>PathDataString</c> is escape-hatched.</b> The
///   legacy <c>MountPath</c> branches between three strategies to set
///   <c>Path.Data</c>: (1) XamlReader-load a constructed
///   <c>&lt;Path Data="..."/&gt;</c> when <c>PathDataString</c> is set
///   (avoids COM re-parent issues); (2) assign a pre-built
///   <see cref="Microsoft.UI.Xaml.Media.Geometry"/> via <c>pa.Data</c> with
///   structured error reporting; (3) fall back to
///   <c>PathDataParser.Parse</c> for the SVG-string case. <c>UpdatePath</c>
///   also gates the Data write on <c>PathDataString</c> string-diff (since
///   parser output never reference-equals). None of these fit a single
///   <c>OneWay</c> setter — the engine's general per-prop comparer can't
///   replicate the string-diff-against-old-element trick, and the error
///   reporting needs both old + new + xaml-text + parser-text context.
///   Authors who need <c>Path.Data</c> stay on V1 OFF; the descriptor handles
///   the rest of the surface (Fill / Stroke / dash / cap / join / transform),
///   which is the bulk of the per-render write pressure for a D3-style
///   chart.</item>
///   <item><b><c>FillRule</c> propagation</b> in the legacy handler writes
///   <c>FillRule</c> onto the inner <see cref="Microsoft.UI.Xaml.Media.PathGeometry"/>
///   (not the <see cref="WinShapes.Path"/> itself). Since the descriptor
///   doesn't set Data, the inner PathGeometry is not the descriptor's to
///   inspect — also escape-hatched.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class PathDescriptor
{
    public static readonly ControlDescriptor<PathElement, WinShapes.Path> Descriptor =
        new ControlDescriptor<PathElement, WinShapes.Path>
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
        .OneWayConditional(
            get:         static e => e.StrokeDashArray,
            set:         static (c, v) => c.StrokeDashArray = v,
            shouldWrite: static e => e.StrokeDashArray is not null)
        .OneWayConditional(
            get:         static e => e.RenderTransform,
            set:         static (c, v) => c.RenderTransform = v,
            shouldWrite: static e => e.RenderTransform is not null)
        .OneWay(
            get: static e => e.StrokeStartLineCap,
            set: static (c, v) => c.StrokeStartLineCap = v)
        .OneWay(
            get: static e => e.StrokeEndLineCap,
            set: static (c, v) => c.StrokeEndLineCap = v)
        .OneWay(
            get: static e => e.StrokeLineJoin,
            set: static (c, v) => c.StrokeLineJoin = v)
        .OneWay(
            get: static e => e.StrokeMiterLimit,
            set: static (c, v) => c.StrokeMiterLimit = v)
        .OneWay(
            get: static e => e.StrokeDashCap,
            set: static (c, v) => c.StrokeDashCap = v)
        .OneWay(
            get: static e => e.StrokeDashOffset,
            set: static (c, v) => c.StrokeDashOffset = v);
}
