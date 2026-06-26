using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
// Ergonomic Reactor chart factories — high-level chart components for Reactor's declarative model
// Usage: using static Microsoft.UI.Reactor.Charting.Charts;

using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;

namespace Microsoft.UI.Reactor.Charting;

/// <summary>
/// Static factory methods that integrate D3 charting into Reactor's declarative model.
/// Import with: using static Microsoft.UI.Reactor.Charting.Charts;
/// </summary>
public static partial class Charts
{
    public static ChartElement<T> LineChart<T>(IReadOnlyList<T> data, Func<T, double> x, Func<T, double> y) =>
        new() { Data = data, XAccessor = x, YAccessor = y, ChartType = ChartType.Line };

    public static ChartElement<T> BarChart<T>(IReadOnlyList<T> data, Func<T, double> x, Func<T, double> y) =>
        new() { Data = data, XAccessor = x, YAccessor = y, ChartType = ChartType.Bar };

    public static ChartElement<T> AreaChart<T>(IReadOnlyList<T> data, Func<T, double> x, Func<T, double> y) =>
        new() { Data = data, XAccessor = x, YAccessor = y, ChartType = ChartType.Area };

    public static PieChartElement<T> PieChart<T>(IReadOnlyList<T> data, Func<T, double> value, Func<T, string>? label = null) =>
        new() { Data = data, ValueAccessor = value, LabelAccessor = label };

    /// <summary>
    /// Wraps any chart element with an alternate-view toggle. Pressing <b>T</b> or
    /// <b>Alt+Shift+F11</b> toggles between the chart and the alternate view
    /// (typically a data table). The currently-hidden view is removed from the
    /// accessibility tree so screen readers only see the active presentation.
    /// <para>
    /// Use this with raw D3 chart elements that are not built via <see cref="ChartElement{T}"/>
    /// (which has its own <c>.AlternateView()</c> modifier).
    /// </para>
    /// </summary>
    /// <param name="chartElement">The chart element to wrap.</param>
    /// <param name="alternateView">The alternate view element (e.g., a data table).</param>
    public static Element WithAlternateView(Element chartElement, Element alternateView) =>
        ChartAlternateViewWrapper.Wrap(chartElement, alternateView);
}

public enum ChartType { Line, Bar, Area }

/// <summary>
/// Represents a range of x-axis values selected via brush interaction.
/// </summary>
public record ChartRange(double Start, double End);

// ════════════════════════════════════════════════════════════════════════════
//  ChartElement — Line / Bar / Area
// ════════════════════════════════════════════════════════════════════════════

public sealed class ChartElement<T> : IChartAccessibilityData
{
    internal IReadOnlyList<T> Data { get; init; } = [];
    internal Func<T, double> XAccessor { get; init; } = _ => 0;
    internal Func<T, double> YAccessor { get; init; } = _ => 0;
    internal ChartType ChartType { get; init; }

    private double _width = 400, _height = 300;
    private double _marginTop = 20, _marginRight = 20, _marginBottom = 30, _marginLeft = 40;
    private string _stroke = "#4285f4", _fill = "#4285f4";
    private double _strokeWidth = 2, _fillOpacity = 0.3;
    private bool _showAxes = true, _showGrid = true;
    private Action<ChartHandle<T>>? _onReady;

    // Accessibility fields
    private string? _title;
    private string? _description;
    private string[]? _seriesNames;
    private Func<T, int, string>? _dataLabel;
    private string? _xUnits, _yUnits;
    private string? _xAxisLabel, _yAxisLabel;

    // Double-encoding / palette fields
    private Accessibility.ChartPalette? _palette;
    private bool _colorOnly;
    private bool _rawColors;
    private D3.D3Color? _chartBackground;
    private Accessibility.MarkerShape[]? _seriesShapes;
    private Accessibility.DashStyle[]? _seriesDashes;

    // Alternate view
    private Element? _alternateView;

    // Custom axis tick label rendering — returned element is anchored at the tick
    // position and rendered non-interactive, hidden from the UIA tree.
    private Func<double, Element>? _xTickLabelView;
    private Func<double, Element>? _yTickLabelView;
    // Optional accessible-name projection + opt-in interactive flag per axis (issue #162).
    private Func<double, string>? _xTickName;
    private Func<double, string>? _yTickName;
    private bool _xTickInteractive;
    private bool _yTickInteractive;

    // Interactive / keyboard navigation fields
    private bool _interactive;
    private bool _disableKeyboard;
    private bool _tightHitTest;
    private Action<T, int>? _onPointInvoke;
    private Action<ChartRange>? _onBrushChanged;
    private global::Windows.UI.Color? _customFocusColor;
    private bool _announceEveryFrame;

    public ChartElement<T> Width(double width) { _width = width; return this; }
    public ChartElement<T> Height(double height) { _height = height; return this; }
    public ChartElement<T> Margin(double left, double top, double right, double bottom) { _marginLeft = left; _marginTop = top; _marginRight = right; _marginBottom = bottom; return this; }
    public ChartElement<T> Stroke(string color) { _stroke = color; return this; }
    public ChartElement<T> Fill(string color) { _fill = color; return this; }
    public ChartElement<T> StrokeWidth(double width) { _strokeWidth = width; return this; }
    public ChartElement<T> FillOpacity(double opacity) { _fillOpacity = opacity; return this; }
    public ChartElement<T> ShowAxes(bool show) { _showAxes = show; return this; }
    public ChartElement<T> ShowGrid(bool show) { _showGrid = show; return this; }

    // ── Accessibility modifiers ──────────────────────────────────────

    /// <summary>Sets visible title + accessible name for the chart.</summary>
    public ChartElement<T> Title(string title) { _title = title; return this; }

    /// <summary>Overrides auto-generated accessible description/summary.</summary>
    public ChartElement<T> Description(string description) { _description = description; return this; }

    /// <summary>Sets the series name (for single-series charts).</summary>
    public ChartElement<T> SeriesName(string name) { _seriesNames = [name]; return this; }

    /// <summary>Sets names for multiple series.</summary>
    public ChartElement<T> SeriesNames(params string[] names) { _seriesNames = names; return this; }

    /// <summary>Per-point label override. Receives the data item and its index.</summary>
    public ChartElement<T> DataLabel(Func<T, int, string> labeller) { _dataLabel = labeller; return this; }

    /// <summary>
    /// Replaces the built-in numeric X-axis tick label with a caller-supplied <see cref="Element"/>.
    /// The element is horizontally centered on the tick mark. By default it is rendered
    /// non-interactive and its <em>entire</em> realized subtree is hidden from the UIA tree and
    /// keyboard tab order (the chart's accessibility data continues to describe axis ticks).
    /// </summary>
    /// <param name="render">Renders the tick element from the tick value.</param>
    /// <param name="name">
    /// Optional accessible-name projection applied as the rendered element's
    /// <c>AutomationName</c>. Only observable when <paramref name="interactive"/> is
    /// <see langword="true"/> — non-interactive ticks are hidden from UIA, so the chart's own
    /// descriptor remains the single source of truth. (Unlike <see cref="PieChartElement{T}.LabelView"/>,
    /// an axis tick has no per-tick descriptor for the projection to feed, so it never affects a
    /// hidden tick's accessible name.)
    /// </param>
    /// <param name="interactive">
    /// Opt-in escape hatch. When <see langword="true"/>, the chart does <b>not</b> force
    /// <c>AccessibilityView.Raw</c> / remove the subtree from focus — the caller takes
    /// responsibility for the tick element's accessibility.
    /// </param>
    public ChartElement<T> XTickLabelView(Func<double, Element> render, Func<double, string>? name = null, bool interactive = false)
    {
        _xTickLabelView = render;
        _xTickName = name;
        _xTickInteractive = interactive;
        return this;
    }

    /// <summary>
    /// Replaces the built-in numeric Y-axis tick label with a caller-supplied <see cref="Element"/>.
    /// The element is right-anchored to the axis edge and vertically centered on the tick. By
    /// default it is rendered non-interactive and its <em>entire</em> realized subtree is hidden
    /// from the UIA tree and keyboard tab order.
    /// </summary>
    /// <param name="render">Renders the tick element from the tick value.</param>
    /// <param name="name">
    /// Optional accessible-name projection applied as the rendered element's
    /// <c>AutomationName</c>. Only observable when <paramref name="interactive"/> is
    /// <see langword="true"/>.
    /// </param>
    /// <param name="interactive">
    /// Opt-in escape hatch. When <see langword="true"/>, the chart does <b>not</b> hide the
    /// subtree from UIA / focus — the caller owns the tick element's accessibility.
    /// </param>
    public ChartElement<T> YTickLabelView(Func<double, Element> render, Func<double, string>? name = null, bool interactive = false)
    {
        _yTickLabelView = render;
        _yTickName = name;
        _yTickInteractive = interactive;
        return this;
    }

    /// <summary>Axis unit annotations (e.g., "months", "USD").</summary>
    public ChartElement<T> Units(string? xUnits = null, string? yUnits = null) { _xUnits = xUnits; _yUnits = yUnits; return this; }

    /// <summary>Explicit axis name.</summary>
    public ChartElement<T> AxisLabel(ChartAxisType axis, string label)
    {
        if (axis == ChartAxisType.X) _xAxisLabel = label;
        else _yAxisLabel = label;
        return this;
    }

    // ── Double-encoding modifiers ────────────────────────────────────

    /// <summary>Sets a curated accessible palette (Tier 1).</summary>
    public ChartElement<T> Palette(Accessibility.ChartPalette palette) { _palette = palette; return this; }

    /// <summary>Sets custom series colors (Tier 3 — scanner-validated).</summary>
    public ChartElement<T> SeriesColors(params D3.D3Color[] colors) { _palette = Accessibility.ChartPalette.FromColors(colors); return this; }

    /// <summary>Sets raw series colors — escape hatch with no validation (Tier 4). Triggers scanner warning A11Y_CHART_012.</summary>
    public ChartElement<T> RawColors(params D3.D3Color[] colors) { _palette = Accessibility.ChartPalette.FromRaw(colors); _rawColors = true; return this; }

    /// <summary>
    /// Declares the representative background color the chart actually renders on.
    /// Lets the theme-agnostic accessibility scanner scope A11Y_CHART_011's custom-palette
    /// contrast check to this single active background (a <c>warning</c>) instead of flagging
    /// failure against either fixed light/dark background (an <c>info</c>). Omit for charts
    /// that may render on any background.
    /// <para>The stored value is normalized to opaque RGB: contrast math
    /// (<see cref="Accessibility.ChartPalette.ContrastRatio"/>) cannot evaluate a semi-transparent
    /// background without knowing what is behind it, so any alpha is dropped.</para>
    /// </summary>
    public ChartElement<T> ChartBackground(D3.D3Color background) { _chartBackground = new D3.D3Color(background.R, background.G, background.B); return this; }

    /// <summary>
    /// <inheritdoc cref="ChartBackground(D3.D3Color)"/> Parses a CSS color string (hex, rgb(), hsl(), or named).
    /// </summary>
    public ChartElement<T> ChartBackground(string background) => ChartBackground(D3Color.Parse(background));

    /// <summary>
    /// <inheritdoc cref="ChartBackground(D3.D3Color)"/> Accepts a <see cref="global::Windows.UI.Color"/>.
    /// </summary>
    public ChartElement<T> ChartBackground(global::Windows.UI.Color background)
        => ChartBackground(new D3.D3Color(background.R, background.G, background.B, background.A / 255.0));

    /// <summary>Disables shape/dash double-encoding — color is sole series differentiator. Triggers scanner warning A11Y_CHART_004.</summary>
    public ChartElement<T> ColorOnly() { _colorOnly = true; return this; }

    /// <summary>Explicit marker shapes for series (overrides default cycle).</summary>
    public ChartElement<T> SeriesShapes(params Accessibility.MarkerShape[] shapes) { _seriesShapes = shapes; return this; }

    /// <summary>Explicit dash patterns for series (overrides default cycle).</summary>
    public ChartElement<T> SeriesDashes(params Accessibility.DashStyle[] dashes) { _seriesDashes = dashes; return this; }

    // ── Alternate-view modifier ──────────────────────────────────────

    /// <summary>
    /// Enables alternate-view toggle (T / Alt+Shift+F11). When toggled, the chart is
    /// hidden from UIA and <paramref name="view"/> is shown instead (typically a data table).
    /// </summary>
    public ChartElement<T> AlternateView(Element view) { _alternateView = view; return this; }

    // ── Interactive / keyboard navigation modifiers ──────────────────

    /// <summary>Enables keyboard navigation and virtual focus on the chart.</summary>
    public ChartElement<T> Interactive() { _interactive = true; return this; }

    /// <summary>Disables keyboard navigation on an interactive chart. Triggers scanner warning A11Y_CHART_003.</summary>
    public ChartElement<T> DisableKeyboard() { _disableKeyboard = true; return this; }

    /// <summary>Uses tight (non-expanded) hit areas for markers. Triggers scanner warning A11Y_CHART_005.</summary>
    public ChartElement<T> TightHitTest() { _tightHitTest = true; return this; }

    /// <summary>Callback invoked when Enter/Space is pressed on a focused point or a point is clicked.</summary>
    public ChartElement<T> OnPointInvoke(Action<T, int> handler) { _onPointInvoke = handler; _interactive = true; return this; }

    /// <summary>Callback invoked when brush selection changes.</summary>
    public ChartElement<T> OnBrushChanged(Action<ChartRange> handler) { _onBrushChanged = handler; _interactive = true; return this; }

    /// <summary>Overrides the default double-ring focus indicator color. Scanner validates contrast (A11Y_CHART_006).</summary>
    public ChartElement<T> FocusColor(global::Windows.UI.Color color) { _customFocusColor = color; return this; }

    /// <summary>Announces every animation frame via live region. Not recommended — floods assistive technology. Triggers scanner warning A11Y_CHART_007.</summary>
    public ChartElement<T> AnnounceEveryFrame() { _announceEveryFrame = true; return this; }

    // ── Internal accessors for scanner ───────────────────────────────
    internal bool IsColorOnly => _colorOnly;
    internal bool IsInteractive => _interactive;
    internal bool IsKeyboardDisabled => _disableKeyboard;
    internal bool IsTightHitTest => _tightHitTest;
    internal Accessibility.ChartPalette? CustomPalette => _palette;

    /// <summary>
    /// Called after the chart Canvas is mounted. The handle exposes the Canvas for
    /// escape-hatch scenarios. Prefer state-driven re-renders for data updates.
    /// </summary>
    public ChartElement<T> OnReady(Action<ChartHandle<T>> callback) { _onReady = callback; return this; }

    public Element ToElement()
    {
        var chart = BuildElement(Data);

        // Wrap with keyboard navigator if interactive
        if (_interactive)
        {
            // Capture the inner canvas for the scanner — the FuncElement wrapper is
            // opaque to static analysis, so we attach a hint the scanner can find.
            var innerCanvas = chart as Core.CanvasElement;

            chart = Accessibility.ChartKeyboardNavigator.Wrap(
                chart, this, _width, _height, _disableKeyboard,
                new Accessibility.ChartKeyboardOptions
                {
                    OnPointInvoke = _onPointInvoke is { } handler
                        ? (si, pi) =>
                        {
                            if (pi < Data.Count)
                                handler(Data[pi], pi);
                        }
                        : null,
                });

            if (innerCanvas is not null)
                chart = chart.SetAttached(new Accessibility.ChartScannerHint(innerCanvas));
        }

        if (_alternateView is { } alt)
            chart = Accessibility.ChartAlternateViewWrapper.Wrap(chart, alt);

        // Establish logical tab order within the chart container:
        // Title/toolbar (index 0) → Legend (index 1) → Plot area (index 2) → Overlays (index 3)
        // The chart canvas is the plot area; title and legend are managed by the peer tree.
        if (_interactive)
            chart = chart.TabIndex(2);

        return chart;
    }
    public static implicit operator Element(ChartElement<T> chart)
    {
        ChartingRuntime.Activate();
        return chart.ToElement();
    }

    private Element BuildElement(IReadOnlyList<T> data)
    {
        var chartName = _title ?? "Plot area";
        if (data.Count == 0)
            return AttachChartData(D3Canvas(_width, _height))
                .AutomationName(chartName);

        double plotLeft = _marginLeft, plotTop = _marginTop;
        double plotWidth = _width - _marginLeft - _marginRight;
        double plotHeight = _height - _marginTop - _marginBottom;

        var (xMin, xMax) = D3Extent.Extent(data, XAccessor);
        var (yMin, yMax) = D3Extent.Extent(data, YAccessor);
        var xScale = new LinearScale([xMin, xMax], [plotLeft, plotLeft + plotWidth]).Nice();
        var yScale = new LinearScale([yMin, yMax], [plotTop + plotHeight, plotTop]).Nice();

        var canvas = D3Canvas(_width, _height,
            [.. _showGrid ? D3Grid(yScale, plotLeft, plotWidth) : [],
             .. RenderData(data, xScale, yScale, plotLeft, plotTop, plotWidth, plotHeight),
             .. _showAxes ? D3Axes(xScale, yScale, plotLeft, plotTop, plotWidth, plotHeight,
                    xTickLabel: _xTickLabelView, yTickLabel: _yTickLabelView,
                    xTickInteractive: _xTickInteractive, yTickInteractive: _yTickInteractive,
                    xTickName: _xTickName, yTickName: _yTickName) : []]);

        if (_onReady is { } cb)
            canvas = canvas.Set(c => cb(new ChartHandle<T>(c)));

        canvas = AttachChartData(canvas);

        // Viewport UIA: plot area gets accessible name, live region, and item status
        var seriesCount = ((IChartAccessibilityData)this).Series.Count;
        var itemStatus = $"{seriesCount} series, {data.Count} points";
        if (_xUnits is not null || _yUnits is not null)
        {
            var units = new[] { _xUnits, _yUnits }.Where(u => u is not null);
            itemStatus += $" ({string.Join(", ", units)})";
        }

        return canvas
            .AutomationName(chartName)
            .LiveRegion(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite)
            .ItemStatus(itemStatus);
    }

    private Core.CanvasElement AttachChartData(Core.CanvasElement canvas) =>
        (Core.CanvasElement)canvas.SetAttached(new Accessibility.ChartA11yData(this)
        {
            IsColorOnly = _colorOnly,
            IsRawColors = _rawColors,
            CustomPalette = _palette,
            IsInteractive = _interactive,
            IsKeyboardDisabled = _disableKeyboard,
            IsTightHitTest = _tightHitTest,
            CustomFocusColor = _customFocusColor,
            IsAnnounceEveryFrame = _announceEveryFrame,
            ChartBackground = _chartBackground,
        });

    // Test-only seam (InternalsVisibleTo Reactor.Tests): drives the real AttachChartData
    // wiring against a caller-supplied canvas so unit tests can pin the accessibility
    // metadata flow (e.g. .ChartBackground(...)) without building the chart's D3Canvas,
    // which constructs a SolidColorBrush and therefore needs WinUI COM.
    internal Core.CanvasElement AttachChartDataForTest(Core.CanvasElement canvas) => AttachChartData(canvas);

    private Element[] RenderData(IReadOnlyList<T> data, LinearScale xScale, LinearScale yScale,
        double plotLeft, double plotTop, double plotWidth, double plotHeight)
    {
        return ChartType switch
        {
            ChartType.Line => RenderLine(data, xScale, yScale),
            ChartType.Bar => RenderBars(data, xScale, yScale, plotTop, plotWidth, plotHeight),
            ChartType.Area => RenderArea(data, xScale, yScale, plotTop, plotHeight),
            _ => [],
        };
    }

    private Element[] RenderLine(IReadOnlyList<T> data, LinearScale xScale, LinearScale yScale)
    {
        return [D3LinePath(data,
            d => xScale.Map(XAccessor(d)),
            d => yScale.Map(YAccessor(d)),
            stroke: Brush(_stroke), strokeWidth: _strokeWidth)];
    }

    private Element[] RenderArea(IReadOnlyList<T> data, LinearScale xScale, LinearScale yScale,
        double plotTop, double plotHeight)
    {
        double baseline = plotTop + plotHeight;
        return [
            D3AreaPath(data,
                d => xScale.Map(XAccessor(d)),
                _ => baseline,
                d => yScale.Map(YAccessor(d)),
                fill: Brush(_fill, _fillOpacity)),
            D3LinePath(data,
                d => xScale.Map(XAccessor(d)),
                d => yScale.Map(YAccessor(d)),
                stroke: Brush(_stroke), strokeWidth: _strokeWidth),
        ];
    }

    private Element[] RenderBars(IReadOnlyList<T> data, LinearScale xScale, LinearScale yScale,
        double plotTop, double plotWidth, double plotHeight)
    {
        double barW = Math.Max(1, plotWidth / data.Count * 0.8);
        double baseline = plotTop + plotHeight;
        var fillBrush = Brush(_fill);
        return data.Select((d, i) =>
        {
            double cx = xScale.Map(XAccessor(d)), cy = yScale.Map(YAccessor(d));
            return (Element)(D3Rect(cx - barW / 2, cy, barW, Math.Max(0, baseline - cy))
                with { Fill = fillBrush, RadiusX = 2, RadiusY = 2, Key = $"bar-{i}" });
        }).ToArray();
    }

    internal static SolidColorBrush ColorToBrush(string color) { var c = D3Color.Parse(color); return new SolidColorBrush(global::Windows.UI.Color.FromArgb((byte)(c.Opacity * 255), c.R, c.G, c.B)); }
    internal static Geometry ParsePathData(string pathData) => PathDataParser.Parse(pathData);

    // ── IChartAccessibilityData ──────────────────────────────────────

    string? IChartAccessibilityData.Name => _title;
    string? IChartAccessibilityData.Description => _description;
    string IChartAccessibilityData.ChartTypeName => ChartType.ToString();

    IReadOnlyList<ChartSeriesDescriptor> IChartAccessibilityData.Series
    {
        get
        {
            if (Data.Count == 0) return [];

            var seriesName = _seriesNames?.Length > 0 ? _seriesNames[0] : "Series 1";

            var points = Data.Select((d, i) =>
            {
                var xVal = XAccessor(d);
                var yVal = YAccessor(d);
                var xLabel = xVal.ToString("G");
                string? label = _dataLabel?.Invoke(d, i);
                return new ChartPointDescriptor(xLabel, yVal, label);
            }).ToArray();

            return [new ChartSeriesDescriptor(seriesName, points)];
        }
    }

    IReadOnlyList<ChartAxisDescriptor> IChartAccessibilityData.Axes
    {
        get
        {
            if (Data.Count == 0) return [];

            var (xMin, xMax) = D3Extent.Extent(Data, XAccessor);
            var (yMin, yMax) = D3Extent.Extent(Data, YAccessor);

            return [
                new ChartAxisDescriptor(ChartAxisType.X, _xAxisLabel, xMin, xMax, _xUnits),
                new ChartAxisDescriptor(ChartAxisType.Y, _yAxisLabel, yMin, yMax, _yUnits),
            ];
        }
    }

    ChartViewport? IChartAccessibilityData.Viewport => null;
}

/// <summary>
/// Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios.
/// </summary>
public sealed class ChartHandle<T>
{
    private readonly Microsoft.UI.Xaml.Controls.Canvas _canvas;

    internal ChartHandle(Microsoft.UI.Xaml.Controls.Canvas canvas) { _canvas = canvas; }

    public Microsoft.UI.Xaml.Controls.Canvas Canvas => _canvas;

    /// <summary>Re-renders the chart with new data. Prefer state-driven re-renders instead.</summary>
    [Obsolete("Use state-driven re-renders (e.g. setData(newData)) instead of ChartHandle.Redraw. " +
              "Charts are now native Reactor elements that diff efficiently.")]
    public void Redraw(IReadOnlyList<T> data) { }
}

// ════════════════════════════════════════════════════════════════════════════
//  PieChartElement
// ════════════════════════════════════════════════════════════════════════════

public sealed class PieChartElement<T> : IChartAccessibilityData
{
    internal IReadOnlyList<T> Data { get; init; } = [];
    internal Func<T, double> ValueAccessor { get; init; } = _ => 0;
    internal Func<T, string>? LabelAccessor { get; init; }

    private double _width = 300, _height = 300;
    private double _innerRadius = 0, _padAngle = 0.02;
    private double _labelRadiusOffset = 0;
    private IReadOnlyList<D3Color>? _colorPalette;
    private Action<PieChartHandle<T>>? _onReady;

    // Accessibility fields
    private string? _title;
    private string? _description;
    private string[]? _seriesNames;
    private Func<T, int, string>? _dataLabel;

    // Double-encoding / palette fields
    private Accessibility.ChartPalette? _palette;
    private bool _colorOnly;
    private D3Color? _chartBackground;

    // Custom label rendering — when set, replaces the built-in TextBlock label
    // produced from LabelAccessor. The string LabelAccessor is still consulted
    // for accessibility (slice descriptors), so screen-reader summaries keep working.
    private Func<T, PieSliceLayout, Element>? _labelView;
    // Optional accessible-name projection used when no LabelAccessor/DataLabel is set,
    // plus opt-in interactive flag (issue #162).
    private Func<T, string>? _labelName;
    private bool _labelViewInteractive;

    public PieChartElement<T> Width(double width) { _width = width; return this; }
    public PieChartElement<T> Height(double height) { _height = height; return this; }
    public PieChartElement<T> InnerRadius(double radius) { _innerRadius = radius; return this; }
    public PieChartElement<T> PadAngle(double angle) { _padAngle = angle; return this; }

    /// <summary>
    /// Shifts auto-positioned labels along the radial axis from the slice centroid.
    /// Positive values push labels outward (toward the arc); negative values pull them inward
    /// (toward the chart center). Units are pixels. Applies to both the built-in text label
    /// and <see cref="LabelView"/>-rendered elements.
    /// </summary>
    public PieChartElement<T> LabelRadiusOffset(double offset) { _labelRadiusOffset = offset; return this; }
    /// <summary>
    /// Override the slice color palette. Colors cycle modulo the palette length when
    /// there are more slices than colors. Calling with an empty argument list clears
    /// the override and restores the default palette — we deliberately don't store an
    /// empty palette because every downstream consumer would have to mod-by-zero
    /// guard, and "no colors" isn't a meaningful render state.
    /// <para><b>Accessibility:</b> these are the colors the slices are actually drawn with, so the
    /// a11y scanner validates them as a Tier-3 custom palette (A11Y_CHART_009/010/011) — exactly
    /// like <see cref="Palette(Accessibility.ChartPalette)"/> colors (issue #645). They are the
    /// single source of truth for both rendering and the scanner: when both <c>.SetColors(...)</c>
    /// and <c>.Palette(...)</c> are set, the rendered <c>.SetColors(...)</c> colors are the ones
    /// scanned. Declare <see cref="ChartBackground(D3Color)"/> to scope the contrast check to the
    /// background the chart renders on.</para>
    /// </summary>
    public PieChartElement<T> SetColors(params D3Color[] colors)
    {
        _colorPalette = colors is { Length: > 0 } ? Array.AsReadOnly(colors) : null;
        return this;
    }
    public PieChartElement<T> OnReady(Action<PieChartHandle<T>> callback) { _onReady = callback; return this; }

    /// <summary>Sets visible title + accessible name for the chart.</summary>
    public PieChartElement<T> Title(string title) { _title = title; return this; }

    /// <summary>Overrides auto-generated accessible description/summary.</summary>
    public PieChartElement<T> Description(string description) { _description = description; return this; }

    /// <summary>Sets names for pie slices (mapped to series in accessibility).</summary>
    public PieChartElement<T> SeriesNames(params string[] names) { _seriesNames = names; return this; }

    /// <summary>Per-slice label override.</summary>
    public PieChartElement<T> DataLabel(Func<T, int, string> labeller) { _dataLabel = labeller; return this; }

    /// <summary>
    /// Replaces the built-in text label for each slice with a caller-supplied <see cref="Element"/>.
    /// The element is positioned centered on the slice's centroid (it does not need a known size),
    /// rendered non-interactive by default (no hit-testing), and its <em>entire</em> realized
    /// subtree is hidden from the UIA tree and keyboard tab order so the chart's
    /// <see cref="IChartAccessibilityData"/> remains the single accessible representation of slice
    /// data. Set the original string label via the <c>label</c> parameter on
    /// <see cref="Charts.PieChart{T}"/>, <see cref="DataLabel"/>, or the <paramref name="name"/>
    /// projection to keep accessibility metadata when overriding the visual.
    /// </summary>
    /// <param name="render">Renders the slice label from the data item and its layout.</param>
    /// <param name="name">
    /// Optional accessible-name projection. When neither a string <c>label</c> accessor nor
    /// <see cref="DataLabel"/> is supplied, this becomes the slice's accessible name in the
    /// chart's descriptor (instead of falling back to <c>"Slice {i+1}"</c>), so screen-reader
    /// users get the same meaningful label the visual shows. Note the deliberate asymmetry with
    /// the axis-tick <c>*TickLabelView(name:)</c> overloads: a pie slice <em>has</em> a per-slice
    /// descriptor, so <paramref name="name"/> feeds that descriptor even for non-interactive
    /// labels; an axis tick has no per-tick descriptor, so there the projection only sets
    /// <c>AutomationName</c> and is observable only when <c>interactive: true</c>.
    /// </param>
    /// <param name="interactive">
    /// Opt-in escape hatch. When <see langword="true"/>, the chart does <b>not</b> force
    /// <c>AccessibilityView.Raw</c> / remove the subtree from focus — the caller takes
    /// responsibility for the label element's accessibility.
    /// </param>
    public PieChartElement<T> LabelView(Func<T, PieSliceLayout, Element> render, Func<T, string>? name = null, bool interactive = false)
    {
        _labelView = render;
        _labelName = name;
        _labelViewInteractive = interactive;
        return this;
    }

    /// <summary>
    /// Declares a scanner-visible accessible palette for the pie (Tier 1). For a pie this is
    /// <b>advisory-only</b>: it does <b>not</b> change the rendered slice colors (a pie renders the
    /// <c>.SetColors(...)</c> colors when set, otherwise the built-in Category10 default) and is
    /// consulted by the accessibility scanner only as a contrast-check fallback when
    /// <c>.SetColors(...)</c> is unset (issue #645). Use <see cref="SetColors"/> to control the
    /// colors a pie actually draws.
    /// </summary>
    public PieChartElement<T> Palette(Accessibility.ChartPalette palette) { _palette = palette; return this; }

    /// <summary>
    /// Declares the representative background color the chart actually renders on.
    /// Lets the theme-agnostic accessibility scanner scope A11Y_CHART_011's custom-palette
    /// contrast check to this single active background (a <c>warning</c>) instead of flagging
    /// failure against either fixed light/dark background (an <c>info</c>). Omit for charts
    /// that may render on any background.
    /// <para><b>Note:</b> the palette contrast-checked against this background is the one the pie
    /// actually renders — the <c>.SetColors(...)</c> colors — when set. A
    /// <see cref="Palette(Accessibility.ChartPalette)"/> palette does <b>not</b> change what a pie
    /// draws (a pie renders the built-in Category10 default unless <c>.SetColors(...)</c> overrides
    /// it); it is consulted by the scanner only as an advisory fallback when <c>.SetColors(...)</c>
    /// is unset (issue #645).</para>
    /// <para>The stored value is normalized to opaque RGB: contrast math
    /// (<see cref="Accessibility.ChartPalette.ContrastRatio"/>) cannot evaluate a semi-transparent
    /// background without knowing what is behind it, so any alpha is dropped.</para>
    /// </summary>
    public PieChartElement<T> ChartBackground(D3Color background) { _chartBackground = new D3Color(background.R, background.G, background.B); return this; }

    /// <summary>
    /// <inheritdoc cref="ChartBackground(D3Color)"/> Parses a CSS color string (hex, rgb(), hsl(), or named).
    /// </summary>
    public PieChartElement<T> ChartBackground(string background) => ChartBackground(D3Color.Parse(background));

    /// <summary>
    /// <inheritdoc cref="ChartBackground(D3Color)"/> Accepts a <see cref="global::Windows.UI.Color"/>.
    /// </summary>
    public PieChartElement<T> ChartBackground(global::Windows.UI.Color background)
        => ChartBackground(new D3Color(background.R, background.G, background.B, background.A / 255.0));

    /// <summary>Disables shape/dash double-encoding. Triggers scanner warning A11Y_CHART_004.</summary>
    public PieChartElement<T> ColorOnly() { _colorOnly = true; return this; }

    // Alternate view
    private Element? _alternateView;

    /// <summary>
    /// Enables alternate-view toggle (T / Alt+Shift+F11).
    /// </summary>
    public PieChartElement<T> AlternateView(Element view) { _alternateView = view; return this; }

    // Internal accessors for scanner
    internal bool IsColorOnly => _colorOnly;
    internal Accessibility.ChartPalette? CustomPalette => ScannerPalette;

    /// <summary>
    /// The single custom palette the accessibility scanner validates, kept in lockstep with what
    /// the pie actually renders (issue #645). The pie draws <c>_colorPalette</c>
    /// (<see cref="SetColors"/>) when set, otherwise the default Category10; <see cref="Palette"/>
    /// is advisory-only and never changes pie rendering. So that the scanner sees exactly the
    /// rendered slices, the rendered <c>.SetColors(...)</c> palette wins as a Tier-3
    /// (<see cref="Accessibility.ChartPalette.FromColors(D3Color[])"/>) scanner-validated palette; the
    /// <see cref="Palette"/> palette is the fallback only when <c>.SetColors(...)</c> is unset,
    /// preserving the prior scanner-visible behavior. Null — neither set — means the pre-vetted
    /// Category10 default, which stays unscanned as before.
    /// </summary>
    private Accessibility.ChartPalette? ScannerPalette =>
        _colorPalette is { Count: > 0 } colors
            ? Accessibility.ChartPalette.FromColors(colors)
            : _palette;

    /// <summary>
    /// The DSL modifier the scanner names in its palette-fix suggestions (A11Y_CHART_009/010/011),
    /// tracking <b>which field actually fed</b> <see cref="ScannerPalette"/>: <c>"SetColors"</c> when
    /// the rendered <c>_colorPalette</c> (<see cref="SetColors"/>) is the source, otherwise
    /// <c>"Palette"</c> for the advisory <c>_palette</c> (<see cref="Palette"/>) fallback. Naming a
    /// call the author never made — e.g. telling a pie that only set <c>.Palette(...)</c> to fix or
    /// remove a non-existent <c>.SetColors(...)</c> call — is the wrong-guidance footgun issue #645
    /// exists to kill, so the remediation must reference the call that produced the scanned palette.
    /// </summary>
    private string ScannerPaletteModifier => _colorPalette is { Count: > 0 } ? "SetColors" : "Palette";

    public Element ToElement()
    {
        var chart = BuildElement(Data);
        if (_alternateView is { } alt)
            chart = Accessibility.ChartAlternateViewWrapper.Wrap(chart, alt);
        return chart;
    }
    public static implicit operator Element(PieChartElement<T> chart)
    {
        ChartingRuntime.Activate();
        return chart.ToElement();
    }

    private Element BuildElement(IReadOnlyList<T> data)
    {
        var chartName = _title ?? "Plot area";

        // Clamp width/height to a safe finite, non-negative range up front. Canvas
        // rejects NaN/negative dimensions and ArcGenerator must never see negative
        // or NaN radii — clamping once here also lets the degenerate-size check
        // below rely on simple `<= 0` comparisons (NaN slips past those).
        double w = double.IsFinite(_width) && _width > 0 ? _width : 0;
        double h = double.IsFinite(_height) && _height > 0 ? _height : 0;

        if (data.Count == 0)
            return AttachChartData(D3Canvas(w, h))
                .AutomationName(chartName);

        var palette = _colorPalette ?? D3Color.Category10;
        double cx = w / 2, cy = h / 2;
        double outerRadius = Math.Min(cx, cy) - 10;
        double innerRadius = double.IsFinite(_innerRadius) && _innerRadius > 0 ? _innerRadius : 0;

        // Degenerate canvas (transient layout pass, very small container, or
        // caller passed bogus dimensions): emit an empty canvas rather than feeding
        // non-positive radii into ArcGenerator and tripping Path.Data validation.
        if (outerRadius <= 0 || innerRadius >= outerRadius)
            return AttachChartData(D3Canvas(w, h))
                .AutomationName(chartName);

        var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);

        var labels =
            _labelView != null ? RenderLabelViews(data, palette, cx, cy, outerRadius)
            : LabelAccessor != null ? RenderLabels(data, cx, cy, outerRadius)
            : [];

        // Pass the same palette to D3Pie that RenderLabelViews resolved above, so
        // PieSliceLayout.Color (label-side) always matches the actual rendered slice.
        var canvas = D3Canvas(w, h,
            [.. D3Pie(data, ValueAccessor, cx, cy, outerRadius, innerRadius, _padAngle,
                    stroke: whiteBrush, palette: palette),
             .. labels]);

        if (_onReady is { } cb)
            canvas = canvas.Set(c => cb(new PieChartHandle<T>(c)));

        canvas = AttachChartData(canvas);

        // Viewport UIA: plot area gets accessible name, live region, and item status
        var itemStatus = $"1 series, {data.Count} slices";
        return canvas
            .AutomationName(chartName)
            .LiveRegion(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite)
            .ItemStatus(itemStatus);
    }

    private Core.CanvasElement AttachChartData(Core.CanvasElement canvas) =>
        (Core.CanvasElement)canvas.SetAttached(new Accessibility.ChartA11yData(this)
        {
            IsColorOnly = _colorOnly,
            CustomPalette = ScannerPalette,
            CustomPaletteModifier = ScannerPaletteModifier,
            IsPaletteAdvisoryOnly = true,
            ChartBackground = _chartBackground,
        });

    // Test-only seam (InternalsVisibleTo Reactor.Tests): mirrors ChartElement<T>'s seam so unit
    // tests can pin PieChartElement<T>'s own .ChartBackground(...) → ChartA11yData wiring without
    // building the chart's D3Canvas, which constructs a SolidColorBrush and therefore needs WinUI COM.
    internal Core.CanvasElement AttachChartDataForTest(Core.CanvasElement canvas) => AttachChartData(canvas);

    // Non-finite offsets would propagate NaN/Infinity into Canvas.Left/Top and
    // can crash WinUI layout — treat them as 0 to match how Width/Height/InnerRadius
    // are normalized in BuildElement.
    private double SafeLabelRadiusOffset =>
        double.IsFinite(_labelRadiusOffset) ? _labelRadiusOffset : 0;

    private Element[] RenderLabels(IReadOnlyList<T> data, double cx, double cy, double outerRadius)
    {
        var pieGen = PieGenerator.Create<T>(ValueAccessor).SetPadAngle(_padAngle);
        var arcs = pieGen.Generate(data);
        var arcGen = new ArcGenerator().SetInnerRadius(_innerRadius).SetOuterRadius(outerRadius);
        var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
        double offset = SafeLabelRadiusOffset;

        return arcs.Select(arc =>
        {
            var (lx, ly) = arcGen.Centroid(arc.StartAngle, arc.EndAngle);
            if (offset != 0)
            {
                double midAngle = (arc.StartAngle + arc.EndAngle) / 2 - Math.PI / 2;
                lx += Math.Cos(midAngle) * offset;
                ly += Math.Sin(midAngle) * offset;
            }
            return (Element)D3Charts.Text(cx + lx - 10, cy + ly - 7, LabelAccessor!(arc.Data), 11, whiteBrush);
        }).ToArray();
    }

    private Element[] RenderLabelViews(IReadOnlyList<T> data, IReadOnlyList<D3Color> palette,
        double cx, double cy, double outerRadius)
    {
        var pieGen = PieGenerator.Create<T>(ValueAccessor).SetPadAngle(_padAngle);
        var arcs = pieGen.Generate(data);
        var arcGen = new ArcGenerator().SetInnerRadius(_innerRadius).SetOuterRadius(outerRadius);
        double offset = SafeLabelRadiusOffset;

        // Total of positive values — matches the normalization PieGenerator uses internally.
        double total = 0;
        for (int i = 0; i < arcs.Length; i++)
            if (arcs[i].Value > 0) total += arcs[i].Value;

        return arcs.Select(arc =>
        {
            var (lx, ly) = arcGen.Centroid(arc.StartAngle, arc.EndAngle);
            // PieSliceLayout.CentroidX/Y always reports the true arc centroid;
            // LabelRadiusOffset only shifts the position the chart uses to anchor
            // the returned element, so callers reading CentroidX/Y (e.g. to draw
            // a leader line back to the slice) keep getting the actual centroid.
            double labelX = cx + lx;
            double labelY = cy + ly;
            if (offset != 0)
            {
                double midAngle = (arc.StartAngle + arc.EndAngle) / 2 - Math.PI / 2;
                labelX += Math.Cos(midAngle) * offset;
                labelY += Math.Sin(midAngle) * offset;
            }
            var layout = new PieSliceLayout(
                Index: arc.Index,
                Value: arc.Value,
                Fraction: total > 0 ? arc.Value / total : 0,
                CentroidX: cx + lx,
                CentroidY: cy + ly,
                StartAngle: arc.StartAngle,
                EndAngle: arc.EndAngle,
                InnerRadius: _innerRadius,
                OuterRadius: outerRadius,
                Color: palette[arc.Index % palette.Count]);

            // OnMountAdd (not OnMount) — preserves any mount-time wiring the
            // caller put on the returned element. ElementModifiers stores a
            // single OnMountAction, so plain `.OnMount(…)` would silently
            // overwrite the caller's hook.
            var labelElement = _labelView!(arc.Data, layout)
                .CenterAt(labelX, labelY)
                // Issue #162: key by the interactive flag so toggling it forces a remount
                // (the hide is a mount-time side effect; in-place update can't undo it).
                .WithKey($"__pielabel{arc.Index}_{(_labelViewInteractive ? 1 : 0)}");

            // interactive opt-in (issue #162): caller owns a11y, leave peers intact.
            if (_labelViewInteractive)
                return labelElement;

            // Default (issue #162): force-Raw the whole realized subtree and remove
            // inner focusable children from the tab order, so only the chart's own
            // descriptor surfaces to UIA / assistive tech. OnUpdateAdd re-asserts the
            // hide over descendants realized on a later in-place update (M1); OnUnmountAdd
            // clears the deferred-hide sentinel so a pre-load unmount can't poison reuse (L1).
            return labelElement
                .OnMountAdd(Accessibility.ChartLabelA11y.HideSubtreeOnMount)
                .OnUpdateAdd(Accessibility.ChartLabelA11y.HideSubtreeOnUpdate)
                .OnUnmountAdd(Accessibility.ChartLabelA11y.ClearPendingHide)
                .AccessibilityView(Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }).ToArray();
    }

    // ── IChartAccessibilityData ──────────────────────────────────────

    string? IChartAccessibilityData.Name => _title;
    string? IChartAccessibilityData.Description => _description;
    string IChartAccessibilityData.ChartTypeName => "Pie";

    IReadOnlyList<ChartSeriesDescriptor> IChartAccessibilityData.Series
    {
        get
        {
            if (Data.Count == 0) return [];

            // Pie charts expose each slice as a point in a single "Slices" series
            var points = Data.Select((d, i) =>
            {
                var value = ValueAccessor(d);
                var label = LabelAccessor?.Invoke(d) ?? _labelName?.Invoke(d) ?? $"Slice {i + 1}";
                string? customLabel = _dataLabel?.Invoke(d, i);
                return new ChartPointDescriptor(label, value, customLabel);
            }).ToArray();

            var seriesName = _seriesNames?.Length > 0 ? _seriesNames[0] : "Slices";
            return [new ChartSeriesDescriptor(seriesName, points)];
        }
    }

    IReadOnlyList<ChartAxisDescriptor> IChartAccessibilityData.Axes => [];
    ChartViewport? IChartAccessibilityData.Viewport => null;
}

/// <summary>
/// Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios.
/// </summary>
public sealed class PieChartHandle<T>
{
    private readonly Microsoft.UI.Xaml.Controls.Canvas _canvas;
    internal PieChartHandle(Microsoft.UI.Xaml.Controls.Canvas canvas) { _canvas = canvas; }
    public Microsoft.UI.Xaml.Controls.Canvas Canvas => _canvas;

    /// <summary>Re-renders the chart with new data. Prefer state-driven re-renders instead.</summary>
    [Obsolete("Use state-driven re-renders (e.g. setData(newData)) instead of PieChartHandle.Redraw. " +
              "Charts are now native Reactor elements that diff efficiently.")]
    public void Redraw(IReadOnlyList<T> data) { }
}

/// <summary>
/// Layout metadata for a single pie slice, supplied to <see cref="PieChartElement{T}.LabelView"/>
/// callbacks. Centroid coordinates are absolute within the chart canvas; angles are radians
/// measured clockwise from 12 o'clock (matches d3-shape semantics).
/// </summary>
public readonly record struct PieSliceLayout(
    int Index,
    double Value,
    double Fraction,
    double CentroidX,
    double CentroidY,
    double StartAngle,
    double EndAngle,
    double InnerRadius,
    double OuterRadius,
    D3.D3Color Color);
