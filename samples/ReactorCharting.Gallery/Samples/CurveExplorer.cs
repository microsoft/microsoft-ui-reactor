using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// A line chart that lets you pick the curve interpolation (Linear, Natural,
/// Cardinal, CatmullRom, Basis, Step, etc.) and adjust the tension parameter
/// via a slider. Shows how UseState drives D3LinePath redraws.
/// </summary>
public sealed class CurveExplorerSample : GallerySample
{
    public override string Title => "Curve Explorer";
    public override string Description =>
        "A line chart with a ComboBox to pick the curve type and a Slider for tension/alpha. " +
        "Instantly see how different interpolation strategies affect the same dataset.";
    public override string Category => "Interactive";

    public override string SourceCode => """
        var (curveIdx, setCurveIdx) = ctx.UseState(0);
        var (tension, setTension) = ctx.UseState(0.5);

        ComboBox(curveNames, curveIdx, setCurveIdx)
        Slider(tension, 0, 1, setTension)

        var curve = curveIdx switch {
            3 => D3Curve.CardinalWithTension(tension),
            4 => D3Curve.CatmullRomWithAlpha(tension),
            ...
        };
        D3LinePath(data, x, y, stroke, strokeWidth, curve)
        .AutomationName("Curve Explorer")
        .FullDescription("Interactive line chart with 13 data points and controls to select curve interpolation type and tension.");
        """;

    static readonly string[] CurveNames =
        ["Linear", "Natural", "Basis", "Cardinal", "Catmull-Rom", "Step", "Step Before", "Step After", "Monotone X"];

    public override Element Render()
    {
        return Func(ctx =>
        {
            var (curveIdx, setCurveIdx) = ctx.UseState(0);
            var (tension, setTension) = ctx.UseState(0.5);

            const double canvasW = 660, canvasH = 360;
            const double left = 50, top = 20, right = 20, bottom = 40;
            double plotW = canvasW - left - right;
            double plotH = canvasH - top - bottom;

            var data = new (double x, double y)[]
            {
                (0, 2), (1, 8), (2, 5), (3, 12), (4, 9),
                (5, 15), (6, 7), (7, 18), (8, 11), (9, 20),
                (10, 14), (11, 22), (12, 17),
            };

            var xs = new LinearScale([0, 12], [left, left + plotW]);
            var ys = new LinearScale([24, 0], [top, top + plotH]);
            ys.Nice();

            bool hasTension = curveIdx is 3 or 4;

            CurveFactory curve = curveIdx switch
            {
                0 => D3Curve.Linear,
                1 => D3Curve.Natural,
                2 => D3Curve.Basis,
                3 => D3Curve.CardinalWithTension(tension),
                4 => D3Curve.CatmullRomWithAlpha(tension),
                5 => D3Curve.Step,
                6 => D3Curve.StepBefore,
                7 => D3Curve.StepAfter,
                8 => D3Curve.MonotoneX,
                _ => D3Curve.Linear,
            };

            var lineBrush = Brush(Palette[0]);
            var dotBrush = Brush(Palette[1]);

            var chart = D3Canvas(canvasW, canvasH,
            [
                .. D3Grid(ys, left, plotW),
                .. D3Axes(xs, ys, left, top, plotW, plotH),
                D3LinePath(data, d => xs.Map(d.x), d => ys.Map(d.y),
                    stroke: lineBrush, strokeWidth: 2.5, curve: curve),
                .. data.Select(d =>
                    (Element)(D3Circle(xs.Map(d.x), ys.Map(d.y), 4) with
                    {
                        Fill = dotBrush,
                        Stroke = ChartSurface,
                        StrokeThickness = 1.5,
                    })),
                D3Charts.Text(canvasW / 2 - 20, canvasH - 10, "X", 11, ChartMutedForeground),
                D3Charts.Text(2, top - 14, "Y", 11, ChartMutedForeground),
            ]);

            return VStack(12,
                HStack(16,
                    HStack(8,
                        TextBlock("Curve:") with { FontSize = 12 },
                        ComboBox(CurveNames, curveIdx, setCurveIdx).Width(160)
                    ).VAlign(VerticalAlignment.Center),
                    HStack(8,
                        (TextBlock("Tension:") with { FontSize = 12 }).Opacity(hasTension ? 1 : 0.4),
                        Slider(tension, 0, 1, setTension)
                            .StepFrequency(0.05)
                            .Width(160)
                            .Disabled(!hasTension),
                        (TextBlock($"{tension:F2}") with { FontSize = 11 }).Opacity(hasTension ? 1 : 0.4)
                    ).VAlign(VerticalAlignment.Center)
                ).Padding(8, 0, 8, 0),
                chart
            ).Padding(12)
                .AutomationName("Curve Explorer")
                .FullDescription("Interactive line chart with 13 data points and controls to select curve interpolation type and tension.");
        });
    }
}
