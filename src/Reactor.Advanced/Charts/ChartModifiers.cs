using Microsoft.UI.Xaml.Controls.Charts;
using Microsoft.UI.Xaml.Media;
using WinUIChart = Microsoft.UI.Xaml.Controls.Charts.Chart;

namespace Microsoft.UI.Reactor.Advanced.Charts;

/// <summary>Fluent modifiers for <see cref="ChartElement"/> and <see cref="ChartSeries"/>.</summary>
public static class ChartModifiers
{
    /// <summary>Shows (or hides) the legend, optionally with a heading.</summary>
    public static ChartElement Legend(this ChartElement el, string? title = null, bool show = true) =>
        el with { ShowLegend = show, LegendTitle = title ?? el.LegendTitle };

    /// <summary>Adds a raw <see cref="WinUIChart"/> setter (escape hatch).</summary>
    public static ChartElement Set(this ChartElement el, Action<WinUIChart> setter) =>
        el with { Setters = [.. el.Setters, setter] };

    /// <summary>Sets the line / outline brush and, optionally, thickness.</summary>
    public static ChartSeries Stroke(this ChartSeries series, Brush brush, double? thickness = null) =>
        series with { Stroke = brush, StrokeThickness = thickness ?? series.StrokeThickness };

    /// <summary>Draws a marker at every data point.</summary>
    public static ChartSeries Markers(this ChartSeries series, MarkerShape shape = MarkerShape.Circle) =>
        series with { ShowDataMarkers = true, MarkerShape = shape };

    /// <summary>Draws a value label at every data point.</summary>
    public static ChartSeries DataLabels(this ChartSeries series, bool show = true) =>
        series with { ShowDataLabels = show };

    /// <summary>Shows or hides the series.</summary>
    public static ChartSeries Visible(this ChartSeries series, bool isVisible = true) =>
        series with { IsVisible = isVisible };

    /// <summary>Sets the bar fill brush.</summary>
    public static ChartBarSeries Fill(this ChartBarSeries series, Brush brush) => series with { Fill = brush };

    /// <summary>Sets the area fill brush.</summary>
    public static ChartAreaSeries Fill(this ChartAreaSeries series, Brush brush) => series with { Fill = brush };

    /// <summary>Draws vertical columns.</summary>
    public static ChartBarSeries Vertical(this ChartBarSeries series) =>
        series with { Orientation = BarOrientation.Vertical };

    /// <summary>Draws horizontal bars (the control default).</summary>
    public static ChartBarSeries Horizontal(this ChartBarSeries series) =>
        series with { Orientation = BarOrientation.Horizontal };

    /// <summary>Sets which grid lines extend from the axis.</summary>
    public static TAxis GridLines<TAxis>(this TAxis axis, GridLines gridLines) where TAxis : ChartAxis =>
        (TAxis)(axis with { GridLines = gridLines });
}
