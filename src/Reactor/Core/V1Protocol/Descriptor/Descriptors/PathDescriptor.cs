using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Media;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 10) — descriptor variant of the hand-coded
/// <c>MountPath</c> / <c>UpdatePath</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event shape leaf — styling and stroke props,
/// plus the pre-built <see cref="Geometry"/> <c>Data</c> path
/// (Phase 3-final Batch F). All paint / dash / cap / join / transform props
/// use <see cref="ControlDescriptor{TElement,TControl}.OneWay"/> or
/// <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional"/>;
/// <c>Data</c> uses <c>.OneWayConditional</c> with a reference comparer.</para>
///
/// <para><b>Behavior parity vs. legacy:</b> the legacy <c>MountPath</c>
/// branches between three strategies for <c>Path.Data</c>:
/// (1) XamlReader-load a constructed <c>&lt;Path Data="..."/&gt;</c> when
/// <c>PathDataString</c> is set; (2) assign a pre-built
/// <see cref="Geometry"/> via <c>pa.Data</c> with structured error reporting;
/// (3) fall back to <c>PathDataParser.Parse</c> for the SVG-string case.
/// The descriptor ports strategy (2) — the pre-built <see cref="Geometry"/>
/// path — under the <c>PathDataString is null</c> gate. Authors who use
/// <c>PathDataString</c> stay on V1 OFF for that string-parse path.</para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b><c>PathDataString</c> (the XamlReader / PathDataParser
///   strategies) is escape-hatched.</b> The engine's general per-prop
///   comparer can't replicate the string-diff-against-old-element trick that
///   the legacy <c>UpdatePath</c> uses, and the error reporting needs both
///   old + new + xaml-text + parser-text context. The descriptor's
///   <c>Data</c> entry is skipped when <c>PathDataString</c> is non-null so
///   the legacy arm stays the single source of truth for that path.</item>
///   <item><b><c>FillRule</c> propagation</b> writes <c>FillRule</c> onto the
///   inner <see cref="PathGeometry"/> (not the <see cref="WinShapes.Path"/>
///   itself). The descriptor inspects <c>p.Data</c> after the Data write
///   and propagates FillRule when it owns a <see cref="PathGeometry"/> —
///   matches the legacy arm's "set FillRule when we can" treatment.</item>
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
            get:         static e => e.Data,
            set:         static (c, v) => c.Data = v,
            // Gate: skip when PathDataString owns Data (legacy XamlReader /
            // PathDataParser strategies — see xmldoc). Also skip when Data is
            // null so we don't write null on top of a legacy-loaded geometry.
            shouldWrite: static e => e.PathDataString is null && e.Data is not null,
            comparer:    GeometryReferenceComparer.Instance)
        // FillRule propagation onto the inner PathGeometry (only meaningful
        // when the descriptor wrote a PathGeometry above; non-EvenOdd writes
        // on a non-PathGeometry are a no-op). Mirrors the legacy arm's
        // <c>p.Data is PathGeometry pg => pg.FillRule = n.FillRule</c>.
        .OneWayConditional(
            get:         static e => e.FillRule,
            set:         static (c, v) =>
            {
                if (c.Data is PathGeometry pg && pg.FillRule != v) pg.FillRule = v;
            },
            shouldWrite: static e => e.PathDataString is null && e.Data is PathGeometry)
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

    /// <summary>Reference-identity comparer over <c>Geometry?</c>. The legacy
    /// <c>UpdatePath</c> arm gates the Data write on string-diff against
    /// <c>PathDataString</c> (parser output never reference-equals); for the
    /// pre-built <see cref="Geometry"/> path the descriptor uses reference
    /// identity, which matches author intent (the Geometry instance is the
    /// thing being identified).</summary>
    private sealed class GeometryReferenceComparer : IEqualityComparer<Geometry?>
    {
        public static readonly GeometryReferenceComparer Instance = new();
        public bool Equals(Geometry? x, Geometry? y) => ReferenceEquals(x, y);
        public int GetHashCode(Geometry obj)
            => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
