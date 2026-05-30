using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Media;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 10) — descriptor variant of the hand-coded
/// <c>MountPath</c> / <c>UpdatePath</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event shape leaf — styling and stroke props,
/// plus full <c>Data</c> propagation covering both the pre-built
/// <see cref="Geometry"/> path and the SVG-string <c>PathDataString</c> path
/// (the latter via the §14 Phase 3 finish Engine (4) <c>.Imperative</c>
/// entry). All paint / dash / cap / join / transform props use
/// <see cref="ControlDescriptor{TElement,TControl}.OneWay"/> or
/// <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional"/>.</para>
///
/// <para><b>Behavior parity vs. legacy:</b> the legacy <c>MountPath</c>
/// branches between three strategies for <c>Path.Data</c>:
/// (1) XamlReader-load a constructed <c>&lt;Path Data="..."/&gt;</c> when
/// <c>PathDataString</c> is set; (2) assign a pre-built
/// <see cref="Geometry"/> via <c>p.Data</c> with structured error reporting;
/// (3) fall back to <c>PathDataParser.Parse</c> for the SVG-string case.
/// The descriptor's <c>.Imperative</c> Data entry replicates all three
/// strategies end-to-end. <c>PathDataString</c> now ports via the Engine (4)
/// <c>.Imperative</c> entry — the legacy gate is gone and authors who use
/// the SVG-string surface get full V1 ON parity.</para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b><c>FillRule</c> propagation</b> writes <c>FillRule</c> onto the
///   inner <see cref="PathGeometry"/> (not the <see cref="WinShapes.Path"/>
///   itself). The descriptor inspects <c>c.Data</c> after the Data write
///   and propagates FillRule when it owns a <see cref="PathGeometry"/> —
///   matches the legacy arm's "set FillRule when we can" treatment.</item>
/// </list></para>
/// </summary>
internal static class PathDescriptor
{
    public static readonly ControlDescriptor<PathElement, WinShapes.Path> Descriptor =
        new ControlDescriptor<PathElement, WinShapes.Path>
        {
            GetSetters = static e => e.Setters,
        }
        // Data entry: §14 Phase 3 finish Engine (4) `.Imperative` — Path.Data
        // has two source surfaces (pre-built Geometry vs. SVG string) and the
        // three-way branching + multi-source error reporting that the legacy
        // MountPath / UpdatePath arms perform can't be expressed by the
        // value-comparer fast-path of `.OneWayConditional`. The Mount lambda
        // replicates the XamlReader → pre-built → PathDataParser strategy
        // chain; the Update lambda replicates the legacy string-diff gate
        // (`o.PathDataString` vs `n.PathDataString` for the string surface,
        // `n.Data is not null` for the Geometry surface).
        .Imperative(
            mount: static (c, e) => WriteData(c, e),
            update: static (c, oldEl, newEl) =>
            {
                // Same `pathChanged` shape as legacy UpdatePath: string
                // equality for the PathDataString surface (parser output
                // never reference-equals so we must compare the source),
                // `n.Data is not null` for the Geometry surface.
                bool pathChanged = newEl.PathDataString is null
                    ? newEl.Data is not null
                    : !string.Equals(newEl.PathDataString, oldEl.PathDataString, global::System.StringComparison.Ordinal);
                if (!pathChanged) return;
                WriteData(c, newEl);
            })
        // FillRule propagation onto the inner PathGeometry. Mirrors the legacy
        // arm's `p.Data is PathGeometry pg => pg.FillRule = n.FillRule` —
        // gating on the LIVE control's resolved Data, not the element field,
        // so the PathDataString surface (where e.Data is null but XamlReader/
        // PathDataParser produces a PathGeometry on c.Data) propagates too.
        // Use `.OneWay` so the entry runs on every Mount and on every change
        // to e.FillRule; the set lambda's `c.Data is PathGeometry` check is
        // the actual gate (no-op when c.Data isn't a PathGeometry).
        .OneWay(
            get: static e => e.FillRule,
            set: static (c, v) =>
            {
                if (c.Data is PathGeometry pg && pg.FillRule != v) pg.FillRule = v;
            })
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

    /// <summary>Three-strategy Data write — mirrors the former legacy
    /// <c>Reconciler.MountPath</c> body verbatim. Strategy 1:
    /// XamlReader-load a synthesized <c>&lt;Path Data="..."/&gt;</c> when
    /// <c>PathDataString</c> is set; if it parses, lift the resulting
    /// Geometry off the loaded Path and assign to the live control.
    /// Strategy 2: assign a pre-built <see cref="Geometry"/> directly when
    /// <c>e.Data</c> is set. Strategy 3 (fallback): <c>PathDataParser.Parse</c>
    /// when XamlReader didn't yield a Path and no pre-built Geometry was
    /// supplied. Multi-source error context is accumulated and rethrown so a
    /// regression here surfaces with the same actionable detail as the
    /// hand-coded arm.</summary>
    private static void WriteData(WinShapes.Path c, PathElement e)
    {
        global::System.Exception? xamlReaderError = null;
        string? attemptedXaml = null;

        // Strategy 1: XamlReader.Load constructed Path. Lift the Geometry
        // off the loaded Path and assign to the live control. A Geometry
        // attached to one Path cannot be re-parented, so we read .Data on
        // the throwaway Path and let WinUI internally rebind it.
        if (e.PathDataString is { Length: > 0 } pds)
        {
            try
            {
                var safe = global::System.Net.WebUtility.HtmlEncode(pds);
                attemptedXaml =
                    "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""
                    + safe + "\" />";
                if (Microsoft.UI.Xaml.Markup.XamlReader.Load(attemptedXaml) is WinShapes.Path loaded
                    && loaded.Data is not null)
                {
                    c.Data = loaded.Data;
                    return;
                }
            }
            catch (global::System.Exception ex)
            {
                xamlReaderError = ex;
            }
        }

        // Strategy 2: pre-built Geometry. Wrap in the same structured
        // error reporting the legacy arm uses so the next regression has
        // actionable context (both surfaces visible in the message).
        if (e.Data is not null)
        {
            try { c.Data = e.Data; }
            catch (global::System.Exception ex)
            {
                var xamlNote = xamlReaderError is not null
                    ? $" XamlReader.Load also failed: {xamlReaderError.GetType().Name}: {xamlReaderError.Message}. Attempted XAML: {attemptedXaml}"
                    : " (XamlReader.Load returned non-Path or wasn't attempted)";
                throw new global::System.ArgumentException(
                    $"Path.Data rejected by WinUI. PathDataString={e.PathDataString ?? "(null)"}; "
                    + $"DataType={e.Data.GetType().Name}; inner={ex.Message}.{xamlNote}", ex);
            }
            return;
        }

        // Strategy 3: PathDataParser fallback for the SVG-string surface.
        // Only reached when XamlReader failed (or returned non-Path) AND no
        // pre-built Geometry was supplied. A non-empty PathDataString must
        // never silently mount as an empty Path — surface both errors
        // together on parser failure.
        if (e.PathDataString is { Length: > 0 } pdsFallback)
        {
            global::System.Exception? parserError = null;
            try { c.Data = global::Microsoft.UI.Reactor.Charting.PathDataParser.Parse(pdsFallback); }
            catch (global::System.Exception ex) { parserError = ex; }

            if (parserError is not null)
            {
                var xamlNote = xamlReaderError is not null
                    ? $"XamlReader.Load failed: {xamlReaderError.GetType().Name}: {xamlReaderError.Message}. Attempted XAML: {attemptedXaml}. "
                    : "XamlReader.Load returned non-Path. ";
                throw new global::System.ArgumentException(
                    $"Could not mount PathElement from PathDataString='{pdsFallback}'. "
                    + xamlNote
                    + $"PathDataParser.Parse also failed: {parserError.GetType().Name}: {parserError.Message}.",
                    parserError);
            }
        }
    }
}
