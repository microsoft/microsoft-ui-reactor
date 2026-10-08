using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Charts;
using WinUIChart = Microsoft.UI.Xaml.Controls.Charts.Chart;
using WinUISamples = Microsoft.UI.Xaml.Controls.Charts.Samples;

namespace Microsoft.UI.Reactor.Advanced.Charts;

/// <summary>
/// V1 handler that mounts, updates, and unmounts <see cref="ChartElement"/> instances onto
/// the WinUI <see cref="WinUIChart"/>.
/// </summary>
/// <remarks>
/// <para>Axes are deduplicated per slot (horizontal / vertical, and the series' physical
/// layout — horizontal bars versus everything else) by record equality, so series that
/// pass equal axis records share one native axis. On update an axis whose record is
/// unchanged is reused untouched; otherwise an unused native axis of the same type and
/// slot is updated in place before a new one is created.</para>
/// <para>Value collections are deduplicated by reference into shared <c>Samples</c>
/// (the native chart binds an axis to the samples it plots), and a fresh collection
/// reuses an unused <c>Samples</c>, so a re-render with new arrays is a plain
/// <c>ItemsSource</c> write.</para>
/// <para>Series are reconciled by position: a series whose record type is unchanged is
/// updated in place. The chart's <c>Data</c>/<c>Axes</c>/<c>Series</c> collections are
/// rewritten only when the set of native samples, axes or series changes.</para>
/// </remarks>
public sealed class ChartHandler : IElementHandler<ChartElement, WinUIChart>
{
    private static readonly ConditionalWeakTable<WinUIChart, ChartState> s_states = new();

    /// <inheritdoc />
    public WinUIChart Mount(MountContext ctx, ChartElement el)
    {
        ExperimentalControlResources.EnsureMerged<XamlChartsResources>();

        var chart = new WinUIChart();
        Reconciler.SetElementTag(chart, el);
        var state = new ChartState();
        s_states.AddOrUpdate(chart, state);

        ApplyProperties(chart, null, el);
        Sync(chart, state, el.Series);
        ctx.ApplySetters(el.Setters, chart);
        return chart;
    }

    /// <inheritdoc />
    public void Update(UpdateContext ctx, ChartElement oldEl, ChartElement newEl, WinUIChart chart)
    {
        Reconciler.SetElementTag(chart, newEl);
        if (!s_states.TryGetValue(chart, out var state))
            return;

        ApplyProperties(chart, oldEl, newEl);
        Sync(chart, state, newEl.Series);
        ctx.ApplySetters(newEl.Setters, chart);
    }

    /// <inheritdoc />
    public void Unmount(UnmountContext ctx, WinUIChart chart) => s_states.Remove(chart);

    private static void ApplyProperties(WinUIChart c, ChartElement? o, ChartElement n)
    {
        var mount = o is null;
        PropertyDiff.Write(c, static () => WinUIChart.ShowLegendProperty, o?.ShowLegend, n.ShowLegend, mount, v => c.ShowLegend = v!.Value);
        PropertyDiff.Write(c, static () => WinUIChart.LegendTitleProperty, o?.LegendTitle, n.LegendTitle, mount, v => c.LegendTitle = v);
    }

    private static void Sync(WinUIChart chart, ChartState state, IReadOnlyList<ChartSeries> series)
    {
        // 1. Resolve every series' effective axes (explicit, or inferred defaults).
        var effective = new (AxisKey X, AxisKey Y)[series.Count];
        var wanted = new List<AxisKey>();
        for (var i = 0; i < series.Count; i++)
        {
            var s = series[i] ?? throw new ArgumentException($"Series {i} is null.", nameof(series));
            var x = s.XAxis ?? DefaultAxes.ForValues(s.XValues);
            var y = s.YAxis ?? DefaultAxes.Linear;
            var transposed = IsTransposed(s);
            effective[i] = (new AxisKey(true, transposed, x), new AxisKey(false, transposed, y));
            AddDistinct(wanted, effective[i].X);
            AddDistinct(wanted, effective[i].Y);
        }

        // 2. Reconcile native axes.
        var oldAxes = state.Axes;
        var consumed = new bool[oldAxes.Count];
        var nextAxes = new AxisSlot?[wanted.Count];
        for (var i = 0; i < wanted.Count; i++)
        {
            for (var j = 0; j < oldAxes.Count; j++)
            {
                if (!consumed[j] && oldAxes[j].Key.Equals(wanted[i]))
                {
                    consumed[j] = true;
                    nextAxes[i] = oldAxes[j];
                    break;
                }
            }
        }
        var axesChanged = oldAxes.Count != wanted.Count;
        for (var i = 0; i < wanted.Count; i++)
        {
            if (nextAxes[i] is not null) continue;
            var key = wanted[i];
            for (var j = 0; j < oldAxes.Count; j++)
            {
                var candidate = oldAxes[j];
                if (!consumed[j] && candidate.Key.IsX == key.IsX && candidate.Key.Transposed == key.Transposed && candidate.Key.Axis.GetType() == key.Axis.GetType())
                {
                    consumed[j] = true;
                    AxisWriter.Write(candidate.Native, candidate.Key.Axis, key.Axis);
                    candidate.Key = key;
                    nextAxes[i] = candidate;
                    break;
                }
            }
            if (nextAxes[i] is null)
            {
                nextAxes[i] = new AxisSlot(key, AxisWriter.Create(key.Axis));
                axesChanged = true;
            }
        }
        for (var i = 0; !axesChanged && i < wanted.Count; i++)
            axesChanged = !ReferenceEquals(oldAxes[i], nextAxes[i]);
        var axes = new List<AxisSlot>(wanted.Count);
        foreach (var slot in nextAxes) axes.Add(slot!);

        // 3. Reconcile series by position.
        var oldSeries = state.Series;
        var nextSeries = new List<SeriesSlot>(series.Count);
        var seriesChanged = oldSeries.Count != series.Count;
        for (var i = 0; i < series.Count; i++)
        {
            var record = series[i];
            SeriesSlot slot;
            if (i < oldSeries.Count && oldSeries[i].Record.GetType() == record.GetType())
            {
                slot = oldSeries[i];
                SeriesWriter.Write(slot, slot.Record, record);
                slot.Record = record;
            }
            else
            {
                slot = SeriesWriter.Create(record);
                seriesChanged = true;
            }

            var xAxis = Find(axes, effective[i].X);
            var yAxis = Find(axes, effective[i].Y);
            if (!ReferenceEquals(slot.XAxis, xAxis)) { slot.Native.XAxis = xAxis; slot.XAxis = xAxis; }
            if (!ReferenceEquals(slot.YAxis, yAxis)) { slot.Native.YAxis = yAxis; slot.YAxis = yAxis; }
            nextSeries.Add(slot);
        }

        // 4. Reconcile the value collections. The native chart binds an axis to the
        //    Samples it plots, so series that pass the same collection instance must
        //    share one Samples (as XAML does with a single <Samples x:Name="Month"/>).
        //    Samples are keyed by collection reference; a new collection reuses an
        //    unused Samples so a re-render with fresh arrays is a plain ItemsSource write.
        var wantedValues = new List<IEnumerable>();
        foreach (var record in series)
        {
            AddDistinctReference(wantedValues, record.XValues);
            AddDistinctReference(wantedValues, record.YValues);
        }
        var oldData = state.Data;
        var dataConsumed = new bool[oldData.Count];
        var nextData = new DataSlot?[wantedValues.Count];
        for (var i = 0; i < wantedValues.Count; i++)
        {
            for (var j = 0; j < oldData.Count; j++)
            {
                if (!dataConsumed[j] && ReferenceEquals(oldData[j].Values, wantedValues[i]))
                {
                    dataConsumed[j] = true;
                    nextData[i] = oldData[j];
                    break;
                }
            }
        }
        var dataChanged = oldData.Count != wantedValues.Count;
        for (var i = 0; i < wantedValues.Count; i++)
        {
            if (nextData[i] is not null) continue;
            DataSlot? reused = null;
            for (var j = 0; j < oldData.Count; j++)
            {
                if (!dataConsumed[j]) { dataConsumed[j] = true; reused = oldData[j]; break; }
            }
            if (reused is null)
            {
                reused = new DataSlot(new WinUISamples());
                dataChanged = true;
            }
            reused.Values = wantedValues[i];
            reused.Native.ItemsSource = ChartValues.Normalize(wantedValues[i]);
            nextData[i] = reused;
        }
        for (var i = 0; !dataChanged && i < wantedValues.Count; i++)
            dataChanged = !ReferenceEquals(oldData[i], nextData[i]);
        var data = new List<DataSlot>(wantedValues.Count);
        foreach (var slot in nextData) data.Add(slot!);

        for (var i = 0; i < nextSeries.Count; i++)
        {
            var slot = nextSeries[i];
            var x = FindData(data, series[i].XValues);
            var y = FindData(data, series[i].YValues);
            if (!ReferenceEquals(slot.X, x)) { slot.Native.XValues = x; slot.X = x; }
            if (!ReferenceEquals(slot.Y, y)) { slot.Native.YValues = y; slot.Y = y; }
        }

        state.Axes = axes;
        state.Series = nextSeries;
        state.Data = data;
        if (!axesChanged && !seriesChanged && !dataChanged) return;

        // 5. Rewrite the native collections. Series go first so no series ever points at
        //    an axis or Samples the chart no longer owns.
        chart.Series.Clear();
        chart.Axes.Clear();
        chart.Data.Clear();
        foreach (var slot in data) chart.Data.Add(slot.Native);
        foreach (var axis in axes) chart.Axes.Add(axis.Native);
        foreach (var slot in nextSeries) chart.Series.Add(slot.Native);
    }

    private static void AddDistinctReference(List<IEnumerable> values, IEnumerable value)
    {
        foreach (var existing in values)
            if (ReferenceEquals(existing, value)) return;
        values.Add(value);
    }

    private static WinUISamples FindData(List<DataSlot> data, IEnumerable values)
    {
        foreach (var slot in data)
            if (ReferenceEquals(slot.Values, values)) return slot.Native;
        throw new InvalidOperationException("Chart samples were not reconciled.");
    }

    private static void AddDistinct(List<AxisKey> keys, AxisKey key)
    {
        if (!keys.Contains(key)) keys.Add(key);
    }

    private static CartesianAxis Find(List<AxisSlot> axes, AxisKey key)
    {
        foreach (var slot in axes)
            if (slot.Key.Equals(key)) return slot.Native;
        throw new InvalidOperationException("Chart axis was not reconciled.");
    }

    /// <summary>
    /// An axis record and the slot it fills. The same record used as X and as Y is two axes,
    /// and so is one used by a horizontal bar series and by a column/line/area series: the
    /// native chart rejects sharing an axis across those physical layouts.
    /// </summary>
    internal readonly record struct AxisKey(bool IsX, bool Transposed, ChartAxis Axis);

    // BarSeries.Orientation defaults to Horizontal on the native control.
    private static bool IsTransposed(ChartSeries series) =>
        series is ChartBarSeries { Orientation: null or BarOrientation.Horizontal };

    private sealed class ChartState
    {
        public List<AxisSlot> Axes = new();
        public List<SeriesSlot> Series = new();
        public List<DataSlot> Data = new();
    }

    private sealed class AxisSlot(AxisKey key, CartesianAxis native)
    {
        public AxisKey Key { get; set; } = key;
        public CartesianAxis Native { get; } = native;
    }

    private sealed class DataSlot(WinUISamples native)
    {
        public WinUISamples Native { get; } = native;
        public IEnumerable? Values { get; set; }
    }

    internal sealed class SeriesSlot(ChartSeries record, CartesianSeries native)
    {
        public ChartSeries Record { get; set; } = record;
        public CartesianSeries Native { get; } = native;
        public WinUISamples? X { get; set; }
        public WinUISamples? Y { get; set; }
        public CartesianAxis? XAxis { get; set; }
        public CartesianAxis? YAxis { get; set; }
    }

    private static class AxisWriter
    {
        public static CartesianAxis Create(ChartAxis record)
        {
            CartesianAxis native = record switch
            {
                ChartLinearAxis => new LinearAxis(),
                ChartCategoryAxis => new CategoryAxis(),
                ChartDateTimeAxis => new DateTimeAxis(),
                _ => throw new NotSupportedException($"Unsupported chart axis type '{record.GetType().Name}'."),
            };
            Write(native, null, record);
            return native;
        }

        public static void Write(CartesianAxis a, ChartAxis? o, ChartAxis n)
        {
            var mount = o is null;
            PropertyDiff.Write(a, static () => Axis.LabelProperty, o?.Label, n.Label, mount, v => a.Label = v);
            PropertyDiff.Write(a, static () => Axis.IsVisibleProperty, o?.IsVisible, n.IsVisible, mount, v => a.IsVisible = v!.Value);
            PropertyDiff.Write(a, static () => CartesianAxis.GridLinesProperty, o?.GridLines, n.GridLines, mount, v => a.GridLines = v!.Value);
            PropertyDiff.Write(a, static () => CartesianAxis.ShowTickLabelsProperty, o?.ShowTickLabels, n.ShowTickLabels, mount, v => a.ShowTickLabels = v!.Value);
            PropertyDiff.Write(a, static () => CartesianAxis.ShowTickMarksProperty, o?.ShowTickMarks, n.ShowTickMarks, mount, v => a.ShowTickMarks = v!.Value);
            PropertyDiff.Write(a, static () => CartesianAxis.AxisLineBrushProperty, o?.AxisLineBrush, n.AxisLineBrush, mount, v => a.AxisLineBrush = v);
            PropertyDiff.Write(a, static () => CartesianAxis.GridLineMajorBrushProperty, o?.GridLineMajorBrush, n.GridLineMajorBrush, mount, v => a.GridLineMajorBrush = v);
            PropertyDiff.Write(a, static () => CartesianAxis.GridLineMinorBrushProperty, o?.GridLineMinorBrush, n.GridLineMinorBrush, mount, v => a.GridLineMinorBrush = v);
            PropertyDiff.Write(a, static () => CartesianAxis.TickBrushProperty, o?.TickBrush, n.TickBrush, mount, v => a.TickBrush = v);
            PropertyDiff.Write(a, static () => CartesianAxis.TickLabelBrushProperty, o?.TickLabelBrush, n.TickLabelBrush, mount, v => a.TickLabelBrush = v);

            switch (a, n)
            {
                case (LinearAxis linear, ChartLinearAxis nl):
                    var ol = o as ChartLinearAxis;
                    PropertyDiff.Write(linear, null, ol?.Minimum, nl.Minimum, mount, v => linear.Minimum = v);
                    PropertyDiff.Write(linear, null, ol?.Maximum, nl.Maximum, mount, v => linear.Maximum = v);
                    PropertyDiff.Write(linear, null, ol?.Spacing, nl.Spacing, mount, v => linear.Spacing = v);
                    break;
                case (CategoryAxis category, ChartCategoryAxis nc):
                    var oc = o as ChartCategoryAxis;
                    PropertyDiff.Write(category, static () => CategoryAxis.SortKeyProperty, oc?.SortKey, nc.SortKey, mount, v => category.SortKey = v!.Value);
                    PropertyDiff.Write(category, static () => CategoryAxis.SortOrderProperty, oc?.SortOrder, nc.SortOrder, mount, v => category.SortOrder = v!.Value);
                    break;
                case (DateTimeAxis time, ChartDateTimeAxis nt):
                    var ot = o as ChartDateTimeAxis;
                    PropertyDiff.Write(time, null, ot?.Minimum, nt.Minimum, mount, v => time.Minimum = v);
                    PropertyDiff.Write(time, null, ot?.Maximum, nt.Maximum, mount, v => time.Maximum = v);
                    PropertyDiff.Write(time, static () => DateTimeAxis.IntervalTypeProperty, ot?.IntervalType, nt.IntervalType, mount, v => time.IntervalType = v!.Value);
                    PropertyDiff.Write(time, static () => DateTimeAxis.LabelFormatProperty, ot?.LabelFormat, nt.LabelFormat, mount, v => time.LabelFormat = v);
                    break;
            }
        }
    }

    private static class SeriesWriter
    {
        public static SeriesSlot Create(ChartSeries record)
        {
            CartesianSeries native = record switch
            {
                ChartLineSeries => new LineSeries(),
                ChartBarSeries => new BarSeries(),
                ChartAreaSeries => new AreaSeries(),
                _ => throw new NotSupportedException($"Unsupported chart series type '{record.GetType().Name}'."),
            };
            var slot = new SeriesSlot(record, native);
            Write(slot, null, record);
            return slot;
        }

        public static void Write(SeriesSlot slot, ChartSeries? o, ChartSeries n)
        {
            var s = slot.Native;
            var mount = o is null;

            PropertyDiff.Write(s, static () => CartesianSeries.TitleProperty, o?.Title, n.Title, mount, v => s.Title = v);
            PropertyDiff.Write(s, static () => CartesianSeries.StrokeProperty, o?.Stroke, n.Stroke, mount, v => s.Stroke = v);
            PropertyDiff.Write(s, static () => CartesianSeries.StrokeThicknessProperty, o?.StrokeThickness, n.StrokeThickness, mount, v => s.StrokeThickness = v!.Value);
            PropertyDiff.Write(s, static () => CartesianSeries.StrokeDashStyleProperty, o?.StrokeDashStyle, n.StrokeDashStyle, mount, v => s.StrokeDashStyle = v!.Value);
            PropertyDiff.Write(s, static () => CartesianSeries.MarkerShapeProperty, o?.MarkerShape, n.MarkerShape, mount, v => s.MarkerShape = v!.Value);
            PropertyDiff.Write(s, static () => CartesianSeries.ShowDataMarkersProperty, o?.ShowDataMarkers, n.ShowDataMarkers, mount, v => s.ShowDataMarkers = v!.Value);
            PropertyDiff.Write(s, static () => CartesianSeries.ShowDataLabelsProperty, o?.ShowDataLabels, n.ShowDataLabels, mount, v => s.ShowDataLabels = v!.Value);
            PropertyDiff.Write(s, static () => CartesianSeries.DataLabelBrushProperty, o?.DataLabelBrush, n.DataLabelBrush, mount, v => s.DataLabelBrush = v);
            PropertyDiff.Write(s, static () => CartesianSeries.DataMarkerBrushProperty, o?.DataMarkerBrush, n.DataMarkerBrush, mount, v => s.DataMarkerBrush = v);
            if (mount ? !n.IsVisible : o!.IsVisible != n.IsVisible)
                s.IsVisible = n.IsVisible;

            if (mount ? n.DataLabels is not null : !ReferenceEquals(o!.DataLabels, n.DataLabels))
            {
                var map = s.DataLabelOverrides;
                map.Clear();
                if (n.DataLabels is { } labels)
                    foreach (var (index, label) in labels)
                        map[checked((uint)index)] = new DataLabelOverride(label.Text, label.Brush ?? s.DataLabelBrush);
            }
            if (mount ? n.DataMarkers is not null : !ReferenceEquals(o!.DataMarkers, n.DataMarkers))
            {
                var map = s.DataMarkerOverrides;
                map.Clear();
                if (n.DataMarkers is { } markers)
                    foreach (var (index, marker) in markers)
                        map[checked((uint)index)] = new DataMarkerOverride(marker.Shape, marker.Brush ?? s.DataMarkerBrush);
            }

            switch (s, n)
            {
                case (BarSeries bar, ChartBarSeries nb):
                    var ob = o as ChartBarSeries;
                    PropertyDiff.Write(bar, static () => BarSeries.FillProperty, ob?.Fill, nb.Fill, mount, v => bar.Fill = v);
                    PropertyDiff.Write(bar, static () => BarSeries.OrientationProperty, ob?.Orientation, nb.Orientation, mount, v => bar.Orientation = v!.Value);
                    break;
                case (AreaSeries area, ChartAreaSeries na):
                    var oa = o as ChartAreaSeries;
                    PropertyDiff.Write(area, static () => AreaSeries.FillProperty, oa?.Fill, na.Fill, mount, v => area.Fill = v);
                    break;
            }
        }
    }
}

/// <summary>Default axes for series that leave <c>XAxis</c>/<c>YAxis</c> unset.</summary>
internal static class DefaultAxes
{
    internal static readonly ChartLinearAxis Linear = new();
    internal static readonly ChartCategoryAxis Category = new();
    internal static readonly ChartDateTimeAxis DateTime = new();

    /// <summary>
    /// Infers the X axis kind from the first non-null value: dates get a time axis and
    /// everything else (strings and numbers alike) a category axis. The X slot of a
    /// line/bar/area series is categorical or temporal; the native chart rejects a linear
    /// axis there.
    /// </summary>
    internal static ChartAxis ForValues(IEnumerable values)
    {
        foreach (var value in values)
        {
            if (value is null) continue;
            return value is DateTimeOffset or global::System.DateTime ? DateTime : Category;
        }
        return Category;
    }
}

/// <summary>Normalizes series values into collection shapes the native chart reads.</summary>
internal static class ChartValues
{
    /// <summary>
    /// Passes arrays and lists of <see cref="double"/>, <see cref="string"/> and
    /// <see cref="DateTimeOffset"/> through untouched (so an observable collection keeps
    /// raising change notifications). Anything else — other numeric types, a
    /// <see cref="global::System.DateTime"/> sequence, or a lazy LINQ query — is materialized into an
    /// array of one of those three types.
    /// </summary>
    internal static object Normalize(IEnumerable values)
    {
        if (values is IList<double> or IList<string> or IList<DateTimeOffset>)
            return values;

        var items = new List<object?>();
        foreach (var value in values) items.Add(value);

        object? first = null;
        foreach (var item in items)
        {
            if (item is not null) { first = item; break; }
        }

        switch (first)
        {
            case null:
                return Array.Empty<double>();
            case string:
                return items.Select(static v => v?.ToString() ?? string.Empty).ToArray();
            case DateTimeOffset or global::System.DateTime:
                return items.Select(static v => v switch
                {
                    DateTimeOffset dto => dto,
                    global::System.DateTime dt => new DateTimeOffset(dt),
                    _ => default,
                }).ToArray();
            default:
                return items.Select(static v => v is null ? double.NaN : Convert.ToDouble(v, global::System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }
    }
}
