using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Charting.Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// Line chart of a 14-day temperature forecast where both axis tick labels are
/// custom Reactor Elements via <c>XTickLabelView</c> / <c>YTickLabelView</c>.
/// </summary>
public sealed class WeeklyForecastSample : GallerySample
{
    public override string Title => "Tick Label Views";
    public override string Description =>
        "A two-week temperature forecast where both axis tick labels are Reactor Elements: " +
        "each X tick is a stacked '№ / day' label and each Y tick pairs the temperature " +
        "with a small swatch that warms with the value. Demonstrates " +
        "Charts.LineChart(...).XTickLabelView(...).YTickLabelView(...) — plain text remains " +
        "the right default; reach for these only when extra signal would actually help.";
    public override string Category => "Lines";
    public override string IconName => "WeeklyForecast";

    public override string SourceCode => """
        Charts.LineChart(data, d => d.Day, d => d.TempC)
            .Width(700).Height(380)
            .XTickLabelView(t => VStack(0,
                (TextBlock($"{Math.Round(t):F0}") with { FontSize = 12 })
                    .FontWeight(FontWeights.SemiBold).Foreground(ChartForeground),
                (TextBlock("day") with { FontSize = 8 }).Foreground(ChartMutedForeground))
                .HAlign(HorizontalAlignment.Center))
            .YTickLabelView(t => HStack(4,
                Rectangle().Width(10).Height(10).CornerRadius(2).Fill(SwatchFor(t)),
                (TextBlock($"{Math.Round(t):F0}°") with { FontSize = 11 })
                    .Foreground(ChartAxis))
                .VAlign(VerticalAlignment.Center))
        """;

    private record Day(double Index, double TempC);

    private static readonly double[] Temps =
        [12, 14, 13, 15, 18, 21, 24, 26, 23, 20, 17, 19, 22, 25];

    private static readonly Day[] Data =
        Temps.Select((t, i) => new Day(i + 1, t)).ToArray();

    public override Element Render()
    {
        var chart = LineChart(Data, d => d.Index, d => d.TempC)
            .Width(700).Height(380)
            .Title("Two-Week Forecast")
            .Description("Daily temperature forecast over 14 days, ranging from 12°C to 26°C.")
            .Units(yUnits: "°C")
            .XTickLabelView(t => VStack(0,
                (TextBlock($"{Math.Round(t):F0}") with { FontSize = 12 })
                    .FontWeight(FontWeights.SemiBold).Foreground(ChartForeground),
                (TextBlock("day") with { FontSize = 8 }).Foreground(ChartMutedForeground)
            ).HAlign(HorizontalAlignment.Center))
            .YTickLabelView(t => HStack(4,
                Rectangle().Width(10).Height(10).CornerRadius(2).Fill(SwatchFor(t)),
                (TextBlock($"{Math.Round(t):F0}°") with { FontSize = 11 })
                    .Foreground(ChartAxis)
            ).VAlign(VerticalAlignment.Center));

        return VStack(8,
            (TextBlock("Two-Week Forecast") with { FontSize = 14 })
                .FontWeight(FontWeights.SemiBold).Foreground(ChartForeground),
            chart
        ).Padding(16)
            .AutomationName("Two-Week Forecast")
            .FullDescription("Line chart of a 14-day temperature forecast with rich axis tick labels rendered as Reactor Elements.");
    }

    /// <summary>Cold (0°C, blue) → warm (30°C, red) gradient for the Y-axis swatch.</summary>
    private static SolidColorBrush SwatchFor(double tempC)
    {
        var t = Math.Clamp(tempC / 30.0, 0, 1);
        var color = D3InterpolateColor.Rgb("#4A90E2", "#E25C3D")(t);
        return Brush(color);
    }
}
