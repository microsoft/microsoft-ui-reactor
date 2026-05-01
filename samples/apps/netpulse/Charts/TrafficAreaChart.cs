using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace NetPulse.Charts;

/// <summary>
/// Streaming stacked area chart showing bytes/sec over a 200-point sliding window.
/// Every 50ms a new point is appended and the oldest dropped, causing the entire
/// path geometry to be regenerated — maximum reconciler work on paths and axes.
/// </summary>
sealed record TrafficAreaChartProps(IReadOnlyList<TrafficSample> History);

sealed class TrafficAreaChart : Component<TrafficAreaChartProps>
{
    const double W = 660, H = 280;
    const double Left = 70, Top = 30, Right = 20, Bottom = 30;
    static readonly double PlotW = W - Left - Right;
    static readonly double PlotH = H - Top - Bottom;

    public override Element Render()
    {
        var history = Props.History;
        if (history.Count < 2)
        {
            return D3Canvas(W, H,
                D3Charts.Text(Left, Top + PlotH / 2, "Waiting for traffic data...", 12, Gray(100)));
        }

        // Build indexed data points for scales
        var points = history.Select((s, i) => (
            idx: (double)i,
            inRate: s.InBytesPerSec,
            outRate: s.OutBytesPerSec,
            total: s.InBytesPerSec + s.OutBytesPerSec
        )).ToArray();

        double maxY = points.Max(p => p.total);
        if (maxY < 1) maxY = 1024; // minimum scale

        var xs = new LinearScale([0, points.Length - 1], [Left, Left + PlotW]);
        var ys = new LinearScale([0, maxY * 1.1], [Top + PlotH, Top]).Nice();

        // Stacked areas: outbound on bottom, inbound stacked on top
        var outAreaData = points.Select(p => (x: p.idx, y0: 0.0, y1: p.outRate)).ToArray();
        var inAreaData = points.Select(p => (x: p.idx, y0: p.outRate, y1: p.total)).ToArray();

        var outArea = D3AreaPath(outAreaData,
            x: d => xs.Map(d.x), y0: d => ys.Map(d.y0), y1: d => ys.Map(d.y1),
            fill: Brush(Palette[0], 0.6));

        var inArea = D3AreaPath(inAreaData,
            x: d => xs.Map(d.x), y0: d => ys.Map(d.y0), y1: d => ys.Map(d.y1),
            fill: Brush(Palette[1], 0.6));

        // Line on top of each area for crispness
        var outLine = D3LinePath(points, x: d => xs.Map(d.idx), y: d => ys.Map(d.outRate),
            stroke: Brush(Palette[0]), strokeWidth: 1.5);
        var inLine = D3LinePath(points, x: d => xs.Map(d.idx), y: d => ys.Map(d.total),
            stroke: Brush(Palette[1]), strokeWidth: 1.5);

        // Current rate text
        var latest = points[^1];
        string inLabel = $"In: {FormatRate(latest.inRate)}";
        string outLabel = $"Out: {FormatRate(latest.outRate)}";

        return D3Canvas(W, H,
        [
            .. D3Grid(ys, Left, PlotW),
            outArea,
            inArea,
            outLine,
            inLine,

            // Axis lines
            D3Line(Left, Top + PlotH, Left + PlotW, Top + PlotH) with { Stroke = Gray(100, 180), StrokeThickness = 1 },
            D3Line(Left, Top, Left, Top + PlotH) with { Stroke = Gray(100, 180), StrokeThickness = 1 },

            // Y axis labels — rate formatted, no duplicates
            .. ys.Ticks(5).Select(t =>
                TextRight(0, ys.Map(t) - 7, FormatRate(t), Left - 6, 9, Gray(100))),

            // Legend
            D3Rect(Left + 8, Top - 20, 10, 10) with { Fill = Brush(Palette[0], 0.6) },
            D3Charts.Text(Left + 22, Top - 22, outLabel, 10, Gray(60)),
            D3Rect(Left + 150, Top - 20, 10, 10) with { Fill = Brush(Palette[1], 0.6) },
            D3Charts.Text(Left + 164, Top - 22, inLabel, 10, Gray(60)),

            D3Charts.Text(Left, 4, "Network Throughput", 13, Gray(40)),
        ]);
    }

    static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec >= 1e9) return $"{bytesPerSec / 1e9:F1} GB/s";
        if (bytesPerSec >= 1e6) return $"{bytesPerSec / 1e6:F1} MB/s";
        if (bytesPerSec >= 1e3) return $"{bytesPerSec / 1e3:F1} KB/s";
        return $"{bytesPerSec:F0} B/s";
    }
}
