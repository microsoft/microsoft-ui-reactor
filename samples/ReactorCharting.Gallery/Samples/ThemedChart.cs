// Themed Chart — shows that chart brush parameters accept any WinUI theme
// resource brush (resolved via Reactor's Theme API), so series colors adapt
// automatically when the app switches between Light and Dark.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class ThemedChartSample : GallerySample
{
    public override string Title => "Themed Chart";
    public override string Description =>
        "A multi-line chart whose series colors come from WinUI theme resource brushes. " +
        "Because the brushes are resolved via Theme.Ref(...)/ThemeResource.Brush(...), the chart " +
        "picks up the current Light/Dark theme on each render — no custom palette needed.";
    public override string Category => "Design";

    public override string SourceCode => """
        // Any Brush works in chart parameters — including theme resource brushes.
        var accent   = ThemeResource.Brush("AccentFillColorDefaultBrush");
        var success  = ThemeResource.Brush("SystemFillColorSuccessBrush");
        var caution  = ThemeResource.Brush("SystemFillColorCautionBrush");
        var critical = ThemeResource.Brush("SystemFillColorCriticalBrush");

        D3LinePath(data, x: d => xs.Map(d.x), y: d => ys.Map(d.y),
            stroke: accent, strokeWidth: 2);
        .AutomationName("Themed Chart")
        .FullDescription("Multi-line chart with 4 series using WinUI theme resource brushes that adapt to Light and Dark themes.");
        """;

    public override Element Render()
    {
        const double W = 700, H = 400;
        const double left = 50, top = 20, right = 140, bottom = 40;
        double width = W - left - right;
        double height = H - top - bottom;

        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

        double[] accentData   = [10, 14, 12, 18, 22, 20, 25, 28, 27, 30, 34, 36];
        double[] successData  = [ 5,  7,  9, 11, 14, 17, 18, 21, 24, 26, 28, 31];
        double[] cautionData  = [20, 18, 17, 15, 16, 14, 12, 11, 13, 14, 12, 10];
        double[] criticalData = [ 2,  3,  5,  7,  6,  9, 11,  8, 10, 12, 15, 14];

        // ── Theme brushes: resolved per-render from Application.Resources,
        //    so they match the current Light/Dark theme. ──
        var accent   = (SolidColorBrush)ThemeResource.Brush("AccentFillColorDefaultBrush");
        var success  = (SolidColorBrush)ThemeResource.Brush("SystemFillColorSuccessBrush");
        var caution  = (SolidColorBrush)ThemeResource.Brush("SystemFillColorCautionBrush");
        var critical = (SolidColorBrush)ThemeResource.Brush("SystemFillColorCriticalBrush");

        var series = new (string Label, SolidColorBrush Brush, double[] Data)[]
        {
            ("Accent",   accent,   accentData),
            ("Success",  success,  successData),
            ("Caution",  caution,  cautionData),
            ("Critical", critical, criticalData),
        };

        var all = series.SelectMany(s => s.Data);
        var (yMin, yMax) = D3Extent.Extent(all);

        var xs = new LinearScale([0, 11], [left, left + width]);
        var ys = new LinearScale([yMax + 3, yMin - 3], [top, top + height]).Nice();

        var monthLabels = months.Select((m, i) =>
            D3Charts.Text(xs.Map(i) - 10, top + height + 4, m, 10, ChartMutedForeground));

        var lines = series.Select(s =>
        {
            var data = s.Data.Select((v, i) => (x: (double)i, y: v)).ToArray();
            return (Element)D3LinePath(data,
                x: d => xs.Map(d.x), y: d => ys.Map(d.y),
                stroke: s.Brush, strokeWidth: 2);
        });

        var dots = series.SelectMany(s =>
            s.Data.Select((v, i) => (Element)(D3Circle(xs.Map(i), ys.Map(v), 3) with { Fill = s.Brush })));

        double legendX = W - right + 10;

        return D3Canvas(W, H,
            [.. D3Grid(ys, left, width),
             .. D3Axes(xs, ys, left, top, width, height),
             .. monthLabels,
             .. lines,
             .. dots,
             .. D3Legend(legendX, top + 10, series.Select(s => (s.Label, s.Brush))),
             D3Charts.Text(2, top - 14, "count", 11, ChartMutedForeground)]
        )
            .AutomationName("Themed Chart")
            .FullDescription("Multi-line chart with 4 series using WinUI theme resource brushes that adapt to Light and Dark themes.");
    }
}
