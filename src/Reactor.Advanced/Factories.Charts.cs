using System.Collections;
using Microsoft.UI.Reactor.Advanced.Charts;

namespace Microsoft.UI.Reactor.Advanced;

// Chart (Microsoft.UI.Xaml.Controls.Charts) factories. The element, series and axis
// records and the fluent modifiers live in Microsoft.UI.Reactor.Advanced.Charts.
public static partial class Factories
{
    /// <summary>Creates a WinUI <c>Chart</c> that plots the given series.</summary>
    /// <param name="series">Series in drawing order (see <see cref="LineSeries"/>, <see cref="BarSeries"/>, <see cref="AreaSeries"/>).</param>
    /// <remarks>Requires the Windows App SDK experimental channel; the API may change.</remarks>
    public static ChartElement Chart(params ChartSeries[] series) => new() { Series = series };

    /// <summary>Creates a line series.</summary>
    /// <param name="xValues">X values: strings or numbers (plotted as categories), or dates.</param>
    /// <param name="yValues">Y values, one per X value.</param>
    /// <param name="title">Legend title.</param>
    /// <param name="xAxis">Category or date-time axis; inferred from <paramref name="xValues"/> when <c>null</c>.</param>
    /// <param name="yAxis">Value axis; a shared linear axis when <c>null</c>.</param>
    public static ChartLineSeries LineSeries(IEnumerable xValues, IEnumerable yValues, string? title = null, ChartAxis? xAxis = null, ChartAxis? yAxis = null) =>
        new() { XValues = xValues, YValues = yValues, Title = title, XAxis = xAxis, YAxis = yAxis };

    /// <summary>Creates a bar series. Bars are horizontal unless you call <c>.Vertical()</c> for columns.</summary>
    /// <inheritdoc cref="LineSeries" path="/param"/>
    public static ChartBarSeries BarSeries(IEnumerable xValues, IEnumerable yValues, string? title = null, ChartAxis? xAxis = null, ChartAxis? yAxis = null) =>
        new() { XValues = xValues, YValues = yValues, Title = title, XAxis = xAxis, YAxis = yAxis };

    /// <summary>Creates an area series.</summary>
    /// <inheritdoc cref="LineSeries" path="/param"/>
    public static ChartAreaSeries AreaSeries(IEnumerable xValues, IEnumerable yValues, string? title = null, ChartAxis? xAxis = null, ChartAxis? yAxis = null) =>
        new() { XValues = xValues, YValues = yValues, Title = title, XAxis = xAxis, YAxis = yAxis };

    /// <summary>Creates a numeric value axis, for a series' Y values.</summary>
    /// <param name="label">Axis title.</param>
    /// <param name="minimum">Lower bound; computed from the data when <c>null</c>.</param>
    /// <param name="maximum">Upper bound; computed from the data when <c>null</c>.</param>
    public static ChartLinearAxis LinearAxis(string? label = null, double? minimum = null, double? maximum = null) =>
        new() { Label = label, Minimum = minimum, Maximum = maximum };

    /// <summary>Creates a category axis for string or numeric X values.</summary>
    /// <param name="label">Axis title.</param>
    public static ChartCategoryAxis CategoryAxis(string? label = null) => new() { Label = label };

    /// <summary>Creates a time axis for <see cref="DateTimeOffset"/> X values.</summary>
    /// <param name="label">Axis title.</param>
    /// <param name="minimum">Lower bound; computed from the data when <c>null</c>.</param>
    /// <param name="maximum">Upper bound; computed from the data when <c>null</c>.</param>
    public static ChartDateTimeAxis DateTimeAxis(string? label = null, DateTimeOffset? minimum = null, DateTimeOffset? maximum = null) =>
        new() { Label = label, Minimum = minimum, Maximum = maximum };
}
