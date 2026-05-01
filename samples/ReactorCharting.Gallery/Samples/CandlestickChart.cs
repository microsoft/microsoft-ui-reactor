using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

public class CandlestickChart : GallerySample
{
    public override string Title => "Candlestick Chart";
    public override string Description => "An OHLC candlestick chart showing 20 trading days. Green candles indicate close > open (bullish), red candles indicate close < open (bearish).";
    public override string Category => "Lines";

    public override string SourceCode => """
        D3Canvas(canvasW, canvasH,
            [..D3Grid(ys, left, width),
             ..D3Axes(xs, ys, left, top, width, height),
             ..candles.SelectMany((c, i) => {
                 var brush = c.Close >= c.Open ? bullBrush : bearBrush;
                 return new Element[] {
                     D3Line(cx, ys.Map(c.High), cx, ys.Map(c.Low))
                         with { Stroke = brush, StrokeThickness = 1.5 },
                     D3Rect(cx - barW/2, bodyTop, barW, bodyH)
                         with { Fill = brush },
                 };
             }),
            ]
        )
            .AutomationName("Stock Price Candlestick Chart")
            .FullDescription("Candlestick chart showing 20 trading days of OHLC price data, with green candles for bullish and red for bearish days.")
        """;

    public override Element Render()
    {
        const double canvasW = 700, canvasH = 400;
        const double left = 55, top = 20, right = 20, bottom = 40;
        double width = canvasW - left - right;
        double height = canvasH - top - bottom;

        // Generate 20 days of realistic OHLC data
        var candles = new (double Open, double High, double Low, double Close)[20];
        double price = 150.0;
        // Deterministic pseudo-random using a simple seed
        int seed = 42;
        double NextRand()
        {
            seed = (seed * 1103515245 + 12345) & 0x7fffffff;
            return (double)seed / 0x7fffffff;
        }

        for (int i = 0; i < 20; i++)
        {
            double open = price;
            double change = (NextRand() - 0.48) * 6; // slight upward bias
            double close = open + change;
            double highExtra = NextRand() * 3 + 0.5;
            double lowExtra = NextRand() * 3 + 0.5;
            double high = Math.Max(open, close) + highExtra;
            double low = Math.Min(open, close) - lowExtra;
            candles[i] = (Math.Round(open, 2), Math.Round(high, 2), Math.Round(low, 2), Math.Round(close, 2));
            price = close;
        }

        // Compute extent over all high/low values
        var allHigh = candles.Select(c => c.High);
        var allLow = candles.Select(c => c.Low);
        var (yMin, _) = D3Extent.Extent(allLow);
        var (_, yMax) = D3Extent.Extent(allHigh);

        var xs = new LinearScale([0, 19], [left + 15, left + width - 15]);
        var ys = new LinearScale([yMax + 2, yMin - 2], [top, top + height]).Nice();

        double barW = width / 20 * 0.6;

        // Bullish and bearish brushes
        var bullBrush = Brush("#26a269");
        var bearBrush = Brush("#e01b24");

        return D3Canvas(canvasW, canvasH,
            [.. D3Grid(ys, left, width),
             .. D3Axes(xs, ys, left, top, width, height),
             // Candlesticks
             .. (from t in candles.Select((c, i) => (c, i))
                 let cx = xs.Map(t.i)
                 let bullish = t.c.Close >= t.c.Open
                 let brush = bullish ? bullBrush : bearBrush
                 let bodyTop = ys.Map(Math.Max(t.c.Open, t.c.Close))
                 let bodyH = Math.Max(ys.Map(Math.Min(t.c.Open, t.c.Close)) - bodyTop, 1)
                 from el in new Element[]
                 {
                     D3Line(cx, ys.Map(t.c.High), cx, ys.Map(t.c.Low)) with { Stroke = brush, StrokeThickness = 1.5 },
                     D3Rect(cx - barW / 2, bodyTop, barW, bodyH) with { Fill = brush },
                 }
                 select el),
             // X-axis labels (every 5 days)
             .. Enumerable.Range(0, 4).Select(n => n * 5)
                 .Select(i => D3Charts.Text(xs.Map(i) - 12, top + height + 4, $"Day {i + 1}", 10, ChartMutedForeground)),
             D3Charts.Text(2, top - 14, "Price", 11, ChartMutedForeground),
             .. D3Legend(left + width - 120, top + 5, [("Bullish", bullBrush), ("Bearish", bearBrush)])]
        )
            .AutomationName("Stock Price Candlestick Chart")
            .FullDescription("Candlestick chart showing 20 trading days of OHLC price data, with green candles for bullish and red for bearish days.");
    }
}
