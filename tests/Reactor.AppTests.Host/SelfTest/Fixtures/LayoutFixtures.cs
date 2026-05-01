using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class LayoutFixtures
{
    internal class FlexRowDistribution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexRow(
                    TextBlock("A").Flex(grow: 1).Background("Red"),
                    TextBlock("B").Flex(grow: 2).Background("Green"),
                    TextBlock("C").Flex(grow: 1).Background("Blue")
                ).Width(600).Height(100)
            );

            await Harness.Render(); // extra time for Yoga layout

            H.Check("FlexLayout_RowDistribution_AllItemsPresent",
                H.FindText("A") is not null &&
                H.FindText("B") is not null &&
                H.FindText("C") is not null);

            // Verify a Microsoft.UI.Reactor.Layout.FlexPanel was created
            H.Check("FlexLayout_RowDistribution_FlexPanelCreated",
                H.FindControl<Microsoft.UI.Reactor.Layout.FlexPanel>(_ => true) is not null);

            // Verify the flex panel has 3 children
            var flexPanel = H.FindControl<Microsoft.UI.Reactor.Layout.FlexPanel>(_ => true);
            H.Check("FlexLayout_RowDistribution_HasThreeChildren",
                flexPanel?.Children.Count >= 3);
        }
    }

    internal class FlexColumnWrap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var children = Enumerable.Range(0, 8)
                    .Select(i => TextBlock($"Box {i}").Width(80).Height(80))
                    .ToArray();
                return new FlexElement(children)
                {
                    Direction = Microsoft.UI.Reactor.Layout.FlexDirection.Column,
                    Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap,
                }.Width(400).Height(200);
            });

            await Harness.Render();

            H.Check("FlexLayout_ColumnWrap_AllItemsPresent",
                H.FindText("Box 0") is not null && H.FindText("Box 7") is not null);

            // With wrap enabled, items should wrap to multiple columns
            var flexPanel = H.FindControl<Microsoft.UI.Reactor.Layout.FlexPanel>(_ => true);
            H.Check("FlexLayout_ColumnWrap_HasFlexPanel",
                flexPanel is not null);

            H.Check("FlexLayout_ColumnWrap_ChildCount",
                flexPanel?.Children.Count == 8);
        }
    }

    internal class GridRowColumn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                Grid([GridSize.Px(200), GridSize.Star()], [GridSize.Auto, GridSize.Star()],
                    TextBlock("TopLeft").Grid(row: 0, column: 0),
                    TextBlock("TopRight").Grid(row: 0, column: 1),
                    TextBlock("BottomLeft").Grid(row: 1, column: 0),
                    TextBlock("BottomRight").Grid(row: 1, column: 1)
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("Grid_RowColumnLayout_AllCellsPresent",
                H.FindText("TopLeft") is not null &&
                H.FindText("TopRight") is not null &&
                H.FindText("BottomLeft") is not null &&
                H.FindText("BottomRight") is not null);

            var grid = H.FindControl<WinUI.Grid>(g => g.ColumnDefinitions.Count == 2);
            H.Check("Grid_RowColumnLayout_GridCreated",
                grid is not null);

            H.Check("Grid_RowColumnLayout_HasTwoRows",
                grid?.RowDefinitions.Count == 2);

            H.Check("Grid_RowColumnLayout_HasTwoColumns",
                grid?.ColumnDefinitions.Count == 2);
        }
    }

    /// <summary>
    /// Compares Grid star column sizing vs FlexPanel grow distribution.
    /// Both should produce identical proportional widths for children.
    /// </summary>
    internal class GridVsFlexStarSizing(Harness h) : SelfTestFixtureBase(h)
    {
        private const double ContainerWidth = 600;
        private const double ContainerHeight = 200;
        private const double Tolerance = 2.0; // layout rounding tolerance

        public override async Task RunAsync()
        {
            // -- Scenario 1: Simple proportional sizing (1:2:1) ----------
            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(8,
                    // Grid with 1* / 2* / 1* star columns
                    Grid([GridSize.Star(), GridSize.Star(2), GridSize.Star()], [GridSize.Star()],
                        TextBlock("G1").Grid(row: 0, column: 0).Background("LightCoral"),
                        TextBlock("G2").Grid(row: 0, column: 1).Background("LightGreen"),
                        TextBlock("G3").Grid(row: 0, column: 2).Background("LightBlue")
                    ).Width(ContainerWidth).Height(80),

                    // FlexRow with grow 1 / 2 / 1 (basis:0 to match star behavior)
                    FlexRow(
                        TextBlock("F1").Flex(grow: 1, basis: 0).Background("LightCoral"),
                        TextBlock("F2").Flex(grow: 2, basis: 0).Background("LightGreen"),
                        TextBlock("F3").Flex(grow: 1, basis: 0).Background("LightBlue")
                    ).Width(ContainerWidth).Height(80)
                ).Width(ContainerWidth).Height(ContainerHeight * 2)
            );

            await Harness.Render();

            // Find the Grid and FlexPanel
            var grid = H.FindControl<WinUI.Grid>(g => g.ColumnDefinitions.Count == 3);
            var flex = H.FindControl<FlexPanel>(_ => true);

            H.Check("GridVsFlex_BothPanelsCreated",
                grid is not null && flex is not null);

            if (grid is null || flex is null) return;

            // Read Grid column actual widths
            double g1Width = grid.ColumnDefinitions[0].ActualWidth;
            double g2Width = grid.ColumnDefinitions[1].ActualWidth;
            double g3Width = grid.ColumnDefinitions[2].ActualWidth;

            // Read FlexPanel children actual widths
            double f1Width = ((FrameworkElement)flex.Children[0]).RenderSize.Width;
            double f2Width = ((FrameworkElement)flex.Children[1]).RenderSize.Width;
            double f3Width = ((FrameworkElement)flex.Children[2]).RenderSize.Width;

            Console.WriteLine($"# Grid columns:  {g1Width:F1}, {g2Width:F1}, {g3Width:F1}");
            Console.WriteLine($"# Flex children: {f1Width:F1}, {f2Width:F1}, {f3Width:F1}");

            // Grid star columns: 1*+2*+1* = 4 parts of 600 -> 150, 300, 150
            H.Check("GridVsFlex_GridProportionsCorrect",
                Near(g1Width, 150) && Near(g2Width, 300) && Near(g3Width, 150));

            // Flex grow: same 1:2:1 ratio with basis:0 should match
            H.Check("GridVsFlex_FlexProportionsCorrect",
                Near(f1Width, 150) && Near(f2Width, 300) && Near(f3Width, 150));

            // Cross-check: Flex widths should match Grid widths
            H.Check("GridVsFlex_ProportionsMatch",
                Near(f1Width, g1Width) && Near(f2Width, g2Width) && Near(f3Width, g3Width));

            // -- Scenario 2: Content-dependent sizing with text ----------
            // Remount with long text in the middle column to test wrapping
            var longText = "This is a long paragraph of text that should wrap within " +
                "the proportional column width. Both Grid and Flex should constrain " +
                "this text to the same width, producing the same line wrapping.";

            host.Mount(ctx =>
                VStack(8,
                    // Grid: auto-height row, star columns
                    Grid([GridSize.Star(), GridSize.Star(2), GridSize.Star()], [GridSize.Auto],
                        TextBlock("Left").Grid(row: 0, column: 0).Background("LightCoral"),
                        TextBlock(longText).Grid(row: 0, column: 1).Background("LightGreen"),
                        TextBlock("Right").Grid(row: 0, column: 2).Background("LightBlue")
                    ).Width(ContainerWidth),

                    // Flex: same ratios with long text
                    FlexRow(
                        TextBlock("Left").Flex(grow: 1, basis: 0).Background("LightCoral"),
                        TextBlock(longText).Flex(grow: 2, basis: 0).Background("LightGreen"),
                        TextBlock("Right").Flex(grow: 1, basis: 0).Background("LightBlue")
                    ).Width(ContainerWidth)
                ).Width(ContainerWidth)
            );

            await Harness.Render();

            // Find the updated panels (find by column count / type)
            var grid2 = H.FindControl<WinUI.Grid>(g => g.ColumnDefinitions.Count == 3);
            var flex2 = H.FindControl<FlexPanel>(_ => true);

            H.Check("GridVsFlex_Text_BothPanelsCreated",
                grid2 is not null && flex2 is not null);

            if (grid2 is null || flex2 is null) return;

            double g2TextWidth = ((FrameworkElement)grid2.Children[1]).RenderSize.Width;
            double f2TextWidth = ((FrameworkElement)flex2.Children[1]).RenderSize.Width;
            double g2TextHeight = ((FrameworkElement)grid2.Children[1]).RenderSize.Height;
            double f2TextHeight = ((FrameworkElement)flex2.Children[1]).RenderSize.Height;

            Console.WriteLine($"# Grid text child:  {g2TextWidth:F1}w x {g2TextHeight:F1}h");
            Console.WriteLine($"# Flex text child:  {f2TextWidth:F1}w x {f2TextHeight:F1}h");

            // Text rendering dimensions should match (same text, same layout constraints)
            H.Check("GridVsFlex_Text_WidthsMatch",
                Near(f2TextWidth, g2TextWidth));

            // Text heights should match (same layout slot height)
            H.Check("GridVsFlex_Text_HeightsMatch",
                Near(f2TextHeight, g2TextHeight, 20));
        }

        private static bool Near(double a, double b, double tolerance = Tolerance)
            => Math.Abs(a - b) <= tolerance;
    }
}
