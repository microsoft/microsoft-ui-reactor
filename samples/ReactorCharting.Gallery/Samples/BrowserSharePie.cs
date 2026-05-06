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
/// Pie chart whose slice labels are rich Reactor Elements (bold percent stacked over the
/// browser name) instead of plain text. Built with <c>Charts.PieChart(...).LabelView(...)</c>.
/// </summary>
public sealed class BrowserSharePieSample : GallerySample
{
    public override string Title => "Pie with Rich Labels";
    public override string Description =>
        "Browser market-share pie where each slice's label is a stacked Element — bold " +
        "percent on top, browser name below — anchored on the slice centroid. The callback " +
        "receives PieSliceLayout (Index, Fraction, Color, centroid coords) so it can adapt the " +
        "label per slice; tiny slices drop the name and just show the percent.";
    public override string Category => "Radial";
    public override string IconName => "BrowserSharePie";

    public override string SourceCode => """
        Charts.PieChart(data, d => d.Share, d => d.Name)
            .Width(520).Height(380)
            .Title("Browser Market Share")
            .LabelView((slice, layout) =>
            {
                var fg = new SolidColorBrush(Microsoft.UI.Colors.White);
                var pct = (TextBlock($"{layout.Fraction * 100:F0}%") with
                    { FontSize = layout.Fraction < 0.06 ? 10 : 16 })
                    .FontWeight(FontWeights.SemiBold).Foreground(fg);

                if (layout.Fraction < 0.06)
                    return pct;

                return VStack(2, pct,
                    (TextBlock(slice.Name) with { FontSize = 11 }).Foreground(fg)
                ).HAlign(HorizontalAlignment.Center);
            })
        """;

    private record BrowserShare(string Name, double Share);

    private static readonly BrowserShare[] Data =
    [
        new("Chrome", 65),
        new("Safari", 18),
        new("Firefox", 7),
        new("Edge", 5),
        new("Other", 5),
    ];

    public override Element Render()
    {
        var pie = PieChart(Data, d => d.Share, d => d.Name)
            .Width(520).Height(380)
            .Title("Browser Market Share")
            .Description("Pie chart of browser market share — Chrome 65%, Safari 18%, Firefox 7%, Edge 5%, Other 5%.")
            .LabelView((slice, layout) =>
            {
                var fg = new SolidColorBrush(Microsoft.UI.Colors.White);
                var pct = (TextBlock($"{layout.Fraction * 100:F0}%") with
                {
                    FontSize = layout.Fraction < 0.06 ? 10 : 16,
                })
                    .FontWeight(FontWeights.SemiBold)
                    .Foreground(fg);

                // Slivers (< 6%) keep just the percent so the chart doesn't get crowded.
                if (layout.Fraction < 0.06)
                    return pct;

                return VStack(2,
                    pct,
                    (TextBlock(slice.Name) with { FontSize = 11 }).Foreground(fg)
                ).HAlign(HorizontalAlignment.Center);
            });

        return VStack(8,
            (TextBlock("Browser Market Share") with { FontSize = 14 })
                .FontWeight(FontWeights.SemiBold).Foreground(ChartForeground),
            pie
        ).Padding(16)
            .AutomationName("Browser Market Share")
            .FullDescription("Pie chart of browser market share with rich slice labels rendered as Reactor Elements.");
    }
}
