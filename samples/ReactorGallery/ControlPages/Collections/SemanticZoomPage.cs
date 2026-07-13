using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Collections;

class SemanticZoomPage : Component
{
    public override Element Render()
    {
        var labels = Enumerable.Range(1, 12).Select(i => $"Item {i}").ToArray();
        var groups = new[] { "A-F", "G-L", "M-R", "S-Z" };

        return ScrollView(VStack(16,
            PageHeader("SemanticZoom", "Lets users switch between detailed and summarized views of the same collection."),
            SampleCard("Grid and group views",
                VStack(8,
                    SemanticZoom(
                        GridView(labels
                            .Select(label => Border(TextBlock(label).Center())
                                .Padding(12)
                                .Margin(4)
                                .Background(Theme.SubtleFill)
                                .CornerRadius(4))
                            .ToArray()),
                        ListView(groups
                            .Select(group => Border(TextBlock(group).SemiBold())
                                .Padding(12)
                                .Margin(0, 0, 0, 4)
                                .Background(Theme.CardBackground)
                                .WithBorder(Theme.DividerStroke))
                            .ToArray())
                    ).Height(300),
                    Caption("Use Ctrl+scroll or the zoomed-out header affordance to switch views.")
                ),
                sourceCode: @"SemanticZoom(
    GridView(items.Select(item =>
        Border(TextBlock(item).Center()).Padding(12)).ToArray()),
    ListView(groups.Select(group =>
        Border(TextBlock(group).SemiBold()).Padding(12)).ToArray())
).Height(300)"),
            SampleCard("Letter sections",
                SemanticZoom(
                    GridView(groups
                        .SelectMany(group => Enumerable.Range(1, 3).Select(i => $"{group} sample {i}"))
                        .Select(text => Border(TextBlock(text).Center())
                            .Padding(10)
                            .Margin(4)
                            .Background(Theme.SubtleFill))
                        .ToArray()),
                    ListView(groups
                        .Select(group => (Element)TextBlock(group).FontSize(20).Bold().Padding(10))
                        .ToArray())
                ).Height(300),
                sourceCode: @"SemanticZoom(
    GridView(sectionItems.Select(text => Border(TextBlock(text))).ToArray()),
    ListView(groups.Select(group => TextBlock(group).FontSize(20).Bold()).ToArray())
).Height(300)")
        ).Margin(36, 24, 36, 36));
    }
}
