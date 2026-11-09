using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// A pie and a line chart that rebuild at their container's size via <c>.FitParent()</c>.
/// Two sliders drive the container's width and height.
/// </summary>
public sealed class ResponsiveChartsSample : GallerySample
{
    public override string Title => "Responsive charts (FitParent)";
    public override string Description =>
        "Charts bake their geometry from an explicit width and height, so WinUI's stretch layout " +
        "cannot resize them. Ending the builder chain with .FitParent() rebuilds the chart at its " +
        "parent's size whenever that size changes. Drag the sliders to resize the container.";
    public override string Category => "Interactive";

    public override string SourceCode => """
        using static Microsoft.UI.Reactor.Charting.Charts;

        var (width, setWidth) = ctx.UseState(960.0);
        var (height, setHeight) = ctx.UseState(360.0);

        // .FitParent() must be the last builder call. Each chart sits in a
        // finite-size slot: a flex child with grow inside a sized container.
        FlexRow(
            PieChart(share, d => d.Value, d => d.Name)
                .Title("Market Share")
                .FitParent()
                .Flex(grow: 1),
            LineChart(days, d => d.Day, d => d.Temp)
                .Title("Daily Temperature")
                .FitParent()
                .Flex(grow: 2)
        ) with { ColumnGap = 16 }
            .Width(width).Height(height)
        """;

    static readonly (string Name, double Value)[] Share =
    [
        ("Chrome", 65.0), ("Safari", 18.0), ("Firefox", 7.0), ("Edge", 5.0), ("Other", 5.0),
    ];

    static readonly (double Day, double Temp)[] Days =
    [
        .. new[]
        {
            5.2, 6.1, 7.8, 6.5, 8.3, 10.1, 11.4, 12.0, 11.2, 13.5,
            14.8, 15.1, 13.9, 12.7, 14.2, 16.0, 17.3, 18.1, 17.5, 16.8,
            18.4, 19.2, 20.1, 19.0, 17.6, 18.9, 20.5, 21.3, 20.8, 22.0,
        }.Select((t, i) => ((double)(i + 1), t)),
    ];

    public override Element Render()
    {
        return RenderEachTime(ctx =>
        {
            var (width, setWidth) = ctx.UseState(960.0);
            var (height, setHeight) = ctx.UseState(360.0);

            // Builders are created per render: one builder instance per FitParent.
            var pie = PieChart(Share, d => d.Value, d => d.Name)
                .Title("Market Share")
                .Description("Pie chart of browser market share that rebuilds at its container's size.")
                .FitParent()
                .Flex(grow: 1);

            var line = LineChart(Days, d => d.Day, d => d.Temp)
                .Title("Daily Temperature")
                .Description("Line chart of daily temperature over 30 days that rebuilds at its container's size.")
                .Units(yUnits: "°C")
                .FitParent()
                .Flex(grow: 2);

            var charts = (FlexRow(pie, line) with { ColumnGap = 16 })
                .Width(width)
                .Height(height);

            return VStack(12,
                TextBlock(
                    "Each chart ends its builder chain with .FitParent() and sits in a finite-size slot " +
                    "(a flex child with grow inside a container with an explicit width and height). " +
                    "While you drag, the previous frame is scaled; once the size settles the chart is rebuilt.")
                    .TextWrapping(TextWrapping.Wrap)
                    .MaxWidth(900)
                    .HAlign(HorizontalAlignment.Left),
                HStack(16,
                    SizeSlider("Container width", width, 360, 1100, setWidth),
                    SizeSlider("Container height", height, 200, 520, setHeight)
                ),
                Border(charts)
                    .WithBorder(Theme.CardStroke)
                    .CornerRadius(4)
                    .HAlign(HorizontalAlignment.Left),
                TextBlock(
                    "Why not Viewbox? Viewbox scales the rendered pixels, so strokes, dots and axis text " +
                    "grow and shrink with the container and the aspect ratio is fixed. FitParent rebuilds " +
                    "the chart at the new size, so strokes keep their width and labels stay crisp at their font size. " +
                    "Prefer Viewbox only when stretched strokes and text are acceptable: it costs no re-render.")
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.Wrap)
                    .MaxWidth(900)
                    .HAlign(HorizontalAlignment.Left)
            ).Padding(8)
                .AutomationName("Responsive charts")
                .FullDescription("A pie chart and a line chart that rebuild at the size of a container whose width and height are set by two sliders.");
        });
    }

    static Element SizeSlider(string label, double value, double min, double max, Action<double> onChange) =>
        HStack(8,
            TextBlock(label).VAlign(VerticalAlignment.Center),
            Slider(value, min, max, onChange)
                .StepFrequency(10)
                .Width(200)
                .AutomationName(label),
            TextBlock($"{value:F0} px").Width(60).VAlign(VerticalAlignment.Center)
        );
}
