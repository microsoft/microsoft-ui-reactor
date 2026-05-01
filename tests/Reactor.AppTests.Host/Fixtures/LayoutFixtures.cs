using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal static class LayoutFixtures
{
    internal static Element FlexRowDistribution(RenderContext ctx) =>
        FlexRow(
            TextBlock("A").Flex(grow: 1).AutomationId("ItemA").Background("Red"),
            TextBlock("B").Flex(grow: 2).AutomationId("ItemB").Background("Green"),
            TextBlock("C").Flex(grow: 1).AutomationId("ItemC").Background("Blue")
        ).Width(600).Height(100);

    internal static Element FlexColumnWrap(RenderContext ctx)
    {
        var children = Enumerable.Range(0, 8)
            .Select(i => TextBlock($"Box {i}").Width(80).Height(80).AutomationId($"Box{i}"))
            .ToArray();
        return new FlexElement(children)
        {
            Direction = FlexDirection.Column,
            Wrap = FlexWrap.Wrap,
        }.Width(400).Height(200);
    }

    internal static Element GridRowColumn(RenderContext ctx) =>
        Grid([GridSize.Px(200), GridSize.Star()], [GridSize.Auto, GridSize.Star()],
            TextBlock("TopLeft").Grid(row: 0, column: 0).AutomationId("TopLeft"),
            TextBlock("TopRight").Grid(row: 0, column: 1).AutomationId("TopRight"),
            TextBlock("BottomLeft").Grid(row: 1, column: 0).AutomationId("BottomLeft"),
            TextBlock("BottomRight").Grid(row: 1, column: 1).AutomationId("BottomRight")
        ).Width(600).Height(400);

    internal static Element GridVsFlexStarSizing(RenderContext ctx) =>
        VStack(8,
            // Grid with 1* / 2* / 1* star columns
            Grid([GridSize.Star(), GridSize.Star(2), GridSize.Star()], [GridSize.Star()],
                TextBlock("G1").Grid(row: 0, column: 0).AutomationId("G1").Background("LightCoral"),
                TextBlock("G2").Grid(row: 0, column: 1).AutomationId("G2").Background("LightGreen"),
                TextBlock("G3").Grid(row: 0, column: 2).AutomationId("G3").Background("LightBlue")
            ).Width(600).Height(80).AutomationId("StarGrid"),

            // FlexRow with grow 1 / 2 / 1 (basis:0 to match star behavior)
            FlexRow(
                TextBlock("F1").Flex(grow: 1, basis: 0).AutomationId("F1").Background("LightCoral"),
                TextBlock("F2").Flex(grow: 2, basis: 0).AutomationId("F2").Background("LightGreen"),
                TextBlock("F3").Flex(grow: 1, basis: 0).AutomationId("F3").Background("LightBlue")
            ).Width(600).Height(80).AutomationId("StarFlex")
        ).Width(600).Height(400);
}
