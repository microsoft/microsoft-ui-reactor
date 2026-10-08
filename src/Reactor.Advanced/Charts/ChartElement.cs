using System.Collections;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls.Charts;
using Microsoft.UI.Xaml.Media;
using WinUIChart = Microsoft.UI.Xaml.Controls.Charts.Chart;

namespace Microsoft.UI.Reactor.Advanced.Charts;

/// <summary>
/// Reactor element for the WinUI <see cref="WinUIChart"/>
/// (<c>Microsoft.UI.Xaml.Controls.Charts</c>, Windows App SDK experimental channel):
/// a native cartesian chart that plots line, bar and area series against linear,
/// category and date-time axes.
/// </summary>
/// <remarks>
/// <para>Create it with <c>Chart(series...)</c> from
/// <see cref="Microsoft.UI.Reactor.Advanced.Factories"/>. Axes are declared on each
/// series; series that share an axis pass equal axis records (records compare by value)
/// and should pass the same X collection instance. A series that leaves an axis unset
/// gets a shared default: a time axis for date X values, a category axis for any other
/// X values, and a linear axis for Y.</para>
/// <para>This maps the platform control. For the D3-based charting toolkit (pie, radial,
/// hierarchy, and custom-drawn charts) see <c>Microsoft.UI.Reactor.Charting</c>.</para>
/// </remarks>
public sealed record ChartElement : Element
{
    /// <summary>The plotted series, in drawing order.</summary>
    public IReadOnlyList<ChartSeries> Series { get; init; } = Array.Empty<ChartSeries>();

    /// <summary>Whether the legend is shown.</summary>
    public bool? ShowLegend { get; init; }

    /// <summary>Heading shown above the legend entries.</summary>
    public string? LegendTitle { get; init; }

    /// <summary>Raw control setters applied after typed properties (escape hatch).</summary>
    public Action<WinUIChart>[] Setters { get; init; } = Array.Empty<Action<WinUIChart>>();

    internal ChartElement() { }
}

/// <summary>A per-point data label that replaces the series' computed label.</summary>
/// <param name="Text">Label text.</param>
/// <param name="Brush">Label brush, or <c>null</c> for the series' (theme-aware) label brush.</param>
public sealed record ChartDataLabel(string Text, Brush? Brush = null);

/// <summary>A per-point data marker that replaces the series' marker.</summary>
/// <param name="Shape">Marker shape.</param>
/// <param name="Brush">Marker brush, or <c>null</c> for the series' (theme-aware) marker brush.</param>
public sealed record ChartDataMarker(MarkerShape Shape, Brush? Brush = null);

/// <summary>
/// A series plotted by a <see cref="ChartElement"/>. Use <see cref="ChartLineSeries"/>,
/// <see cref="ChartBarSeries"/> or <see cref="ChartAreaSeries"/>. Optional properties
/// left <c>null</c> keep the control default.
/// </summary>
public abstract record ChartSeries
{
    private protected ChartSeries() { }

    /// <summary>
    /// X values: strings or numbers (plotted as categories) or <see cref="DateTimeOffset"/>
    /// values (plotted on a time axis). The chart re-reads the collection when the
    /// reference changes; pass an
    /// <see cref="global::System.Collections.ObjectModel.ObservableCollection{T}"/> to append in place.
    /// Series that share an X axis should pass the same collection instance.
    /// </summary>
    public required IEnumerable XValues { get; init; }

    /// <summary>Y values (numbers), one per X value.</summary>
    public required IEnumerable YValues { get; init; }

    /// <summary>Series name, shown in the legend.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Category or date-time axis for the X values. When <c>null</c>, a shared default is
    /// inferred from <see cref="XValues"/>. A <see cref="ChartLinearAxis"/> is a value axis
    /// and is only valid as <see cref="YAxis"/>.
    /// </summary>
    public ChartAxis? XAxis { get; init; }

    /// <summary>Value axis for the Y values. When <c>null</c>, a shared default linear axis is used.</summary>
    public ChartAxis? YAxis { get; init; }

    /// <summary>Line / outline brush.</summary>
    public Brush? Stroke { get; init; }

    /// <summary>Line / outline thickness.</summary>
    public double? StrokeThickness { get; init; }

    /// <summary>Line dash pattern.</summary>
    public StrokeDashStyle? StrokeDashStyle { get; init; }

    /// <summary>Data-point marker shape.</summary>
    public MarkerShape? MarkerShape { get; init; }

    /// <summary>Whether data-point markers are drawn.</summary>
    public bool? ShowDataMarkers { get; init; }

    /// <summary>Whether data-point value labels are drawn.</summary>
    public bool? ShowDataLabels { get; init; }

    /// <summary>Data-label brush.</summary>
    public Brush? DataLabelBrush { get; init; }

    /// <summary>Data-marker brush.</summary>
    public Brush? DataMarkerBrush { get; init; }

    /// <summary>Per-point label overrides, keyed by point index.</summary>
    public IReadOnlyDictionary<int, ChartDataLabel>? DataLabels { get; init; }

    /// <summary>Per-point marker overrides, keyed by point index.</summary>
    public IReadOnlyDictionary<int, ChartDataMarker>? DataMarkers { get; init; }

    /// <summary>Whether the series is drawn.</summary>
    public bool IsVisible { get; init; } = true;
}

/// <summary>A line series.</summary>
public sealed record ChartLineSeries : ChartSeries;

/// <summary>A bar (column) series.</summary>
public sealed record ChartBarSeries : ChartSeries
{
    /// <summary>Bar fill brush.</summary>
    public Brush? Fill { get; init; }

    /// <summary>
    /// Bar direction. <c>null</c> keeps the control default, which is
    /// <see cref="BarOrientation.Horizontal"/>; use <see cref="BarOrientation.Vertical"/>
    /// for columns. Horizontal bars never share axes with line, area or column series —
    /// the chart gives them their own.
    /// </summary>
    public BarOrientation? Orientation { get; init; }
}

/// <summary>An area series: a line with the region below it filled.</summary>
public sealed record ChartAreaSeries : ChartSeries
{
    /// <summary>Area fill brush.</summary>
    public Brush? Fill { get; init; }
}

/// <summary>
/// A cartesian chart axis. Use <see cref="ChartLinearAxis"/>, <see cref="ChartCategoryAxis"/>
/// or <see cref="ChartDateTimeAxis"/>. Axes are records: series that pass equal axis
/// records share one native axis.
/// </summary>
public abstract record ChartAxis
{
    private protected ChartAxis() { }

    /// <summary>Axis title.</summary>
    public string? Label { get; init; }

    /// <summary>Whether the axis line, ticks and labels are drawn.</summary>
    public bool? IsVisible { get; init; }

    /// <summary>Which grid lines extend from the axis across the plot area.</summary>
    public GridLines? GridLines { get; init; }

    /// <summary>Whether tick labels are drawn.</summary>
    public bool? ShowTickLabels { get; init; }

    /// <summary>Whether tick marks are drawn.</summary>
    public bool? ShowTickMarks { get; init; }

    /// <summary>Axis line brush.</summary>
    public Brush? AxisLineBrush { get; init; }

    /// <summary>Major grid line brush.</summary>
    public Brush? GridLineMajorBrush { get; init; }

    /// <summary>Minor grid line brush.</summary>
    public Brush? GridLineMinorBrush { get; init; }

    /// <summary>Tick mark brush.</summary>
    public Brush? TickBrush { get; init; }

    /// <summary>Tick label brush.</summary>
    public Brush? TickLabelBrush { get; init; }
}

/// <summary>A numeric value axis. Valid as a series' <see cref="ChartSeries.YAxis"/>.</summary>
public sealed record ChartLinearAxis : ChartAxis
{
    /// <summary>Lower bound. When <c>null</c>, computed from the data.</summary>
    public double? Minimum { get; init; }

    /// <summary>Upper bound. When <c>null</c>, computed from the data.</summary>
    public double? Maximum { get; init; }

    /// <summary>Distance between major ticks. When <c>null</c>, computed from the range.</summary>
    public double? Spacing { get; init; }
}

/// <summary>An axis of discrete categories (string or numeric X values).</summary>
public sealed record ChartCategoryAxis : ChartAxis
{
    /// <summary>Whether categories are ordered by their source index or by their value.</summary>
    public CategorySortKey? SortKey { get; init; }

    /// <summary>Category sort direction.</summary>
    public SortOrder? SortOrder { get; init; }
}

/// <summary>A time axis (<see cref="DateTimeOffset"/> values).</summary>
public sealed record ChartDateTimeAxis : ChartAxis
{
    /// <summary>Lower bound. When <c>null</c>, computed from the data.</summary>
    public DateTimeOffset? Minimum { get; init; }

    /// <summary>Upper bound. When <c>null</c>, computed from the data.</summary>
    public DateTimeOffset? Maximum { get; init; }

    /// <summary>Tick interval unit.</summary>
    public DateTimeIntervalType? IntervalType { get; init; }

    /// <summary>Tick label format (for example <c>"month day"</c>).</summary>
    public string? LabelFormat { get; init; }
}
