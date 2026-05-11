using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// A donut chart with sliders that dynamically control each wedge's value
/// and the inner radius. Demonstrates UseState driving live chart updates.
/// </summary>
public sealed class DonutMixerSample : GallerySample
{
    public override string Title => "Donut Mixer";
    public override string Description =>
        "An interactive donut chart with a slider per wedge controlling its relative size, " +
        "plus a slider to morph between pie and donut. Uses UseState to drive live D3 redraws.";
    public override string Category => "Interactive";

    public override string SourceCode => """
        var (values, setValues) = ctx.UseState(initialValues);
        var (innerR, setInnerR) = ctx.UseState(70.0);

        // Slider per category drives wedge size
        Slider(values[i], 10, 200, v => {
            var copy = values.ToArray();
            copy[i] = v;
            setValues(copy);
        })

        // Donut redraws from current values
        var arcs = PieGenerator.Generate(values, v => v);
        D3ArcPath(arc.StartAngle, arc.EndAngle, cx, cy,
            outerRadius: 140, innerRadius: innerR)
        .AutomationName("Donut Mixer")
        .FullDescription("Interactive donut chart with 6 budget categories and sliders to adjust wedge sizes and inner radius.");
        """;

    static readonly string[] Labels = ["Housing", "Food", "Transport", "Utilities", "Health", "Savings"];
    static readonly double[] Defaults = [180, 65, 40, 25, 30, 60];

    public override Element Render()
    {
        return RenderEachTime(ctx =>
        {
            var (values, setValues) = ctx.UseState(Defaults.ToArray());
            var (innerR, setInnerR) = ctx.UseState(70.0);

            const double chartW = 360, chartH = 360;
            double cx = chartW / 2, cy = chartH / 2;
            const double outerR = 140;

            var arcs = PieGenerator.Generate(values, v => v, sort: false, padAngle: 0.03);
            double total = values.Sum();

            var sliders = Labels.Select((label, i) =>
                (Element)HStack(8,
                    D3Rect(0, 0, 12, 12) with
                    {
                        Fill = Brush(Palette[i % Palette.Count]),
                        RadiusX = 2, RadiusY = 2,
                    },
                    (TextBlock(label) with { FontSize = 11 }).Width(70),
                    Slider(values[i], 10, 200, v =>
                    {
                        var copy = values.ToArray();
                        copy[i] = v;
                        setValues(copy);
                    }).StepFrequency(5).Width(140),
                    (TextBlock($"{values[i] / total * 100:F0}%") with { FontSize = 11 }).Width(36)
                ).VAlign(VerticalAlignment.Center)
            ).ToArray();

            var chart = D3Canvas(chartW, chartH,
            [
                .. arcs.SelectMany((a, i) =>
                {
                    var (lx, ly) = ArcGenerator.Centroid(a.StartAngle, a.EndAngle,
                        innerRadius: outerR + 30, outerRadius: outerR + 30);
                    return new Element[]
                    {
                        D3ArcPath(a.StartAngle, a.EndAngle, cx, cy,
                            outerRadius: outerR, innerRadius: innerR, padAngle: 0.03,
                            fill: Brush(Palette[i % Palette.Count])),
                        TextCenter(cx + lx - 20, cy + ly - 7, Labels[i], 40, 10,
                            Brush(Palette[i % Palette.Count])),
                    };
                }),
                TextCenter(cx - 30, cy - 10, $"{total:F0}", 60, 16, ChartForeground),
                TextCenter(cx - 20, cy + 10, "total", 40, 10, ChartMutedForeground),
            ]);

            return HStack(24,
                chart,
                VStack(10,
                    SubHeading("Adjust Wedges"),
                    VStack(6, sliders),
                    SubHeading("Inner Radius").Margin(horizontal: 12, vertical: 0),
                    HStack(8,
                        Slider(innerR, 0, 130, setInnerR).StepFrequency(5).Width(180),
                        TextBlock($"{innerR:F0}px") with { FontSize = 11 }
                    )
                ).Padding(8)
            ).Padding(16)
                .AutomationName("Donut Mixer")
                .FullDescription("Interactive donut chart with 6 budget categories and sliders to adjust wedge sizes and inner radius.");
        });
    }
}
