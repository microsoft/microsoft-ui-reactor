using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class LineChart : GallerySample
{
    public override string Title => "Line Chart";
    public override string Description => "A simple line chart showing daily temperature readings over 30 days using LineGenerator with linear interpolation.";
    public override string Category => "Lines";

    public override string SourceCode => """
        D3Canvas(W, H,
            ..D3Grid(ys, left, width),
            ..D3Axes(xs, ys, left, top, width, height),
            D3LinePath(data, x: d => xs.Map(d.x), y: d => ys.Map(d.y),
                stroke: lineBrush, strokeWidth: 2),
            ..dots
        )
            .AutomationName("Daily Temperature (°C)")
            .FullDescription("Line chart showing daily temperature readings over 30 days, ranging from 5.2°C to 22.0°C.")
        """;

    public override Element Render()
    {
        const double canvasW = 700, canvasH = 400;
        const double left = 50, top = 20, right = 20, bottom = 40;
        double width = canvasW - left - right;
        double height = canvasH - top - bottom;

        double[] temps =
        [
            5.2, 6.1, 7.8, 6.5, 8.3, 10.1, 11.4, 12.0, 11.2, 13.5,
            14.8, 15.1, 13.9, 12.7, 14.2, 16.0, 17.3, 18.1, 17.5, 16.8,
            18.4, 19.2, 20.1, 19.0, 17.6, 18.9, 20.5, 21.3, 20.8, 22.0
        ];

        var data = temps.Select((t, i) => (x: (double)(i + 1), y: t)).ToArray();

        var (yMin, yMax) = D3Extent.Extent(temps);
        var xs = new LinearScale([1, 30], [left, left + width]);
        var ys = new LinearScale([yMax + 2, yMin - 2], [top, top + height]).Nice();

        var lineBrush = Brush(Palette[0]);

        return D3Canvas(canvasW, canvasH,
            [.. D3Grid(ys, left, width),
             .. D3Axes(xs, ys, left, top, width, height),
             D3LinePath(data, x: d => xs.Map(d.x), y: d => ys.Map(d.y), stroke: lineBrush, strokeWidth: 2),
             .. data.Select(d => (Element)(D3Circle(xs.Map(d.x), ys.Map(d.y), 3) with { Fill = lineBrush })),
             D3Charts.Text(canvasW / 2 - 20, canvasH - 12, "Day", 11, ChartMutedForeground),
             D3Charts.Text(2, top - 14, "\u00b0C", 11, ChartMutedForeground)]
        )
            .AutomationName("Daily Temperature (\u00b0C)")
            .FullDescription("Line chart showing daily temperature readings over 30 days, ranging from 5.2\u00b0C to 22.0\u00b0C.");
    }
}
