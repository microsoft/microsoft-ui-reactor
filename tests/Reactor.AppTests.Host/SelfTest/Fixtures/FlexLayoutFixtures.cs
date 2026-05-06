using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Comprehensive Flex layout E2E tests covering nesting, composition with
/// other layout panels, scroll interaction, and cross-axis sizing.
/// </summary>
internal static class FlexLayoutFixtures
{
    private const double Tolerance = 2.0;

    private static bool Near(double a, double b, double tol = Tolerance)
        => Math.Abs(a - b) <= tol;

    // ----------------------------------------------------------------
    // 1. Nested Flex: FlexRow children inside FlexColumn
    // ----------------------------------------------------------------

    internal class FlexNestedRowInColumn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexColumn(
                    FlexRow(
                        TextBlock("Logo").Flex(basis: 0, grow: 1).Background("LightCoral"),
                        TextBlock("Nav").Flex(basis: 0, grow: 2).Background("LightBlue")
                    ).Height(50),

                    FlexRow(
                        TextBlock("Sidebar").Flex(basis: 0, grow: 1).Background("LightGreen"),
                        TextBlock("Content").Flex(basis: 0, grow: 3).Background("LightYellow")
                    ).Flex(grow: 1),

                    FlexRow(
                        TextBlock("Footer").Flex(grow: 1)
                    ).Height(40).Background("LightGray")
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("FlexNested_RowInCol_AllPresent",
                H.FindText("Logo") is not null &&
                H.FindText("Nav") is not null &&
                H.FindText("Sidebar") is not null &&
                H.FindText("Content") is not null &&
                H.FindText("Footer") is not null);

            var outerCol = H.FindControl<FlexPanel>(p =>
                FlexPanel.GetGrow(p) == 0 && p.Direction == FlexDirection.Column);
            H.Check("FlexNested_RowInCol_OuterColumnExists", outerCol is not null);

            var rows = H.FindAllControls<FlexPanel>(p => p.Direction == FlexDirection.Row);
            H.Check("FlexNested_RowInCol_ThreeInnerRows", rows.Count >= 3);

            if (outerCol is not null)
            {
                var bodyRow = rows.FirstOrDefault(r => FlexPanel.GetGrow(r) > 0);
                H.Check("FlexNested_RowInCol_BodyGrew",
                    bodyRow is not null && Near(bodyRow.ActualHeight, 310, 5));

                var contentText = H.FindText("Content");
                if (contentText is not null)
                {
                    H.Check("FlexNested_RowInCol_ContentWidth",
                        Near(contentText.RenderSize.Width, 450, 5));
                }
            }
        }
    }

    // ----------------------------------------------------------------
    // 2. Nested Flex: FlexColumn inside FlexRow
    // ----------------------------------------------------------------

    internal class FlexNestedColumnInRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexRow(
                    FlexColumn(
                        TextBlock("Menu A"),
                        TextBlock("Menu B"),
                        TextBlock("Menu C")
                    ).Flex(basis: 0, grow: 1).Background("LightCoral"),

                    FlexColumn(
                        TextBlock("Title").Height(40),
                        TextBlock("Body text here").Flex(grow: 1)
                    ).Flex(basis: 0, grow: 3).Background("LightBlue")
                ).Width(600).Height(300)
            );

            await Harness.Render();

            H.Check("FlexNested_ColInRow_AllPresent",
                H.FindText("Menu A") is not null &&
                H.FindText("Title") is not null &&
                H.FindText("Body text here") is not null);

            var columns = H.FindAllControls<FlexPanel>(p => p.Direction == FlexDirection.Column);
            var sidebar = columns.FirstOrDefault(c => c.ActualWidth < 200);
            H.Check("FlexNested_ColInRow_SidebarWidth",
                sidebar is not null && Near(sidebar.ActualWidth, 150, 5));

            var content = columns.FirstOrDefault(c => c.ActualWidth > 400);
            H.Check("FlexNested_ColInRow_ContentWidth",
                content is not null && Near(content.ActualWidth, 450, 5));
        }
    }

    // ----------------------------------------------------------------
    // 3. Deep nesting: 3+ levels of FlexPanels
    // ----------------------------------------------------------------

    internal class FlexNestedDeep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexColumn(
                    TextBlock("L1-Header").Height(30),

                    FlexRow(
                        TextBlock("L2-Left").Flex(basis: 0, grow: 1),

                        FlexColumn(
                            TextBlock("L3-Top"),
                            TextBlock("L3-Mid").Flex(grow: 1),
                            TextBlock("L3-Bot")
                        ).Flex(basis: 0, grow: 2)
                    ).Flex(grow: 1),

                    TextBlock("L1-Footer").Height(30)
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("FlexNested_Deep_AllPresent",
                H.FindText("L1-Header") is not null &&
                H.FindText("L2-Left") is not null &&
                H.FindText("L3-Top") is not null &&
                H.FindText("L3-Mid") is not null &&
                H.FindText("L3-Bot") is not null &&
                H.FindText("L1-Footer") is not null);

            var allFlex = H.FindAllControls<FlexPanel>(_ => true);
            H.Check("FlexNested_Deep_ThreePanels", allFlex.Count >= 3);

            var l2Left = H.FindText("L2-Left");
            H.Check("FlexNested_Deep_L2LeftWidth",
                l2Left is not null && Near(l2Left.RenderSize.Width, 200, 5));

            var l3Columns = H.FindAllControls<FlexPanel>(p =>
                p.Direction == FlexDirection.Column && p.ActualWidth > 350 && p.ActualWidth < 450);
            H.Check("FlexNested_Deep_L3ColumnWidth", l3Columns.Count >= 1);
        }
    }

    // ----------------------------------------------------------------
    // 4. Flex inside Grid star cells
    // ----------------------------------------------------------------

    internal class FlexInsideGrid(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                Grid([GridSize.Star(), GridSize.Star(2)], [GridSize.Star(), GridSize.Star()],
                    FlexRow(
                        TextBlock("A1").Flex(grow: 1).Background("LightCoral"),
                        TextBlock("A2").Flex(grow: 1).Background("LightBlue")
                    ).Grid(row: 0, column: 0),

                    FlexColumn(
                        TextBlock("B1").Flex(grow: 1).Background("LightGreen"),
                        TextBlock("B2").Flex(grow: 2).Background("LightYellow")
                    ).Grid(row: 0, column: 1),

                    FlexRow(
                        TextBlock("C1").Flex(grow: 1).Background("Wheat")
                    ).Grid(row: 1, column: 0),

                    FlexRow(
                        TextBlock("D1").Width(80).Background("Plum"),
                        TextBlock("D2").Flex(grow: 1).Background("PeachPuff")
                    ).Grid(row: 1, column: 1)
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("FlexInGrid_AllPresent",
                H.FindText("A1") is not null &&
                H.FindText("B1") is not null &&
                H.FindText("C1") is not null &&
                H.FindText("D1") is not null &&
                H.FindText("D2") is not null);

            var grid = H.FindControl<WinUI.Grid>(g => g.ColumnDefinitions.Count == 2);
            H.Check("FlexInGrid_GridCreated", grid is not null);

            if (grid is null) return;

            double col0Width = grid.ColumnDefinitions[0].ActualWidth;
            double col1Width = grid.ColumnDefinitions[1].ActualWidth;

            H.Check("FlexInGrid_GridColumnWidths",
                Near(col0Width, 200) && Near(col1Width, 400));

            var a1 = H.FindText("A1");
            var a2 = H.FindText("A2");
            H.Check("FlexInGrid_TopLeftDistribution",
                a1 is not null && a2 is not null &&
                Near(a1.RenderSize.Width, 100, 5) && Near(a2.RenderSize.Width, 100, 5));

            var d1 = H.FindText("D1");
            var d2 = H.FindText("D2");
            H.Check("FlexInGrid_MixedFixedGrow",
                d1 is not null && d2 is not null &&
                Near(d1.RenderSize.Width, 80, 5) && Near(d2.RenderSize.Width, 320, 5));
        }
    }

    // ----------------------------------------------------------------
    // 5. Flex inside Border
    // ----------------------------------------------------------------

    internal class FlexInsideBorder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(8,
                    Border(
                        FlexRow(
                            TextBlock("Left").Flex(basis: 0, grow: 1).Background("LightCoral"),
                            TextBlock("Right").Flex(basis: 0, grow: 1).Background("LightBlue")
                        )
                    ).Width(600).Background("LightGray"),

                    TextBlock("Reference line")
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("FlexInBorder_AllPresent",
                H.FindText("Left") is not null && H.FindText("Right") is not null);

            var flex = H.FindControl<FlexPanel>(_ => true);
            H.Check("FlexInBorder_CrossAxisContentSized",
                flex is not null && flex.ActualHeight < 100);

            var left = H.FindText("Left");
            var right = H.FindText("Right");
            H.Check("FlexInBorder_MainAxisDistributed",
                left is not null && right is not null &&
                Near(left.RenderSize.Width, 300, 5) && Near(right.RenderSize.Width, 300, 5));
        }
    }

    // ----------------------------------------------------------------
    // 6. Flex inside VStack
    // ----------------------------------------------------------------

    internal class FlexInsideVStack(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(4,
                    TextBlock("Before"),

                    FlexRow(
                        TextBlock("Row1-A").Flex(basis: 0, grow: 1).Background("LightCoral"),
                        TextBlock("Row1-B").Flex(basis: 0, grow: 2).Background("LightBlue"),
                        TextBlock("Row1-C").Flex(basis: 0, grow: 1).Background("LightGreen")
                    ),

                    FlexRow(
                        TextBlock("Row2-A").Flex(basis: 0, grow: 1).Background("Wheat"),
                        TextBlock("Row2-B").Flex(basis: 0, grow: 1).Background("Plum")
                    ),

                    TextBlock("After")
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("FlexInVStack_AllPresent",
                H.FindText("Before") is not null &&
                H.FindText("Row1-A") is not null &&
                H.FindText("Row2-A") is not null &&
                H.FindText("After") is not null);

            var flexPanels = H.FindAllControls<FlexPanel>(_ => true);
            H.Check("FlexInVStack_TwoFlexPanels", flexPanels.Count >= 2);

            H.Check("FlexInVStack_ContentHeights",
                flexPanels.All(p => p.ActualHeight < 80));

            var afterText = H.FindText("After");
            H.Check("FlexInVStack_AfterVisible", afterText is not null);

            var row1B = H.FindText("Row1-B");
            H.Check("FlexInVStack_GrowDistribution",
                row1B is not null && Near(row1B.RenderSize.Width, 300, 5));
        }
    }

    // ----------------------------------------------------------------
    // 7. Flex inside ScrollViewer (infinite height)
    // ----------------------------------------------------------------

    internal class FlexInsideScrollView(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                ScrollView(
                    FlexColumn(
                        TextBlock("Item 1").Height(60).Background("LightCoral"),
                        TextBlock("Item 2").Height(60).Background("LightBlue"),
                        TextBlock("Item 3").Height(60).Background("LightGreen"),
                        TextBlock("Item 4").Height(60).Background("Wheat"),
                        TextBlock("Item 5").Height(60).Background("Plum")
                    ).Width(400)
                ).Width(400).Height(200)
            );

            await Harness.Render();

            H.Check("FlexInScroll_AllItemsExist",
                H.FindText("Item 1") is not null &&
                H.FindText("Item 5") is not null);

            var flex = H.FindControl<FlexPanel>(_ => true);
            H.Check("FlexInScroll_ContentTallerThanViewport",
                flex is not null && flex.ActualHeight >= 290);

            var item1 = H.FindText("Item 1");
            H.Check("FlexInScroll_ItemHeight",
                item1 is not null && Near(item1.RenderSize.Height, 60, 5));
        }
    }

    // ----------------------------------------------------------------
    // 8. ScrollViewer inside a Flex grow child
    // ----------------------------------------------------------------

    internal class ScrollViewInsideFlex(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexColumn(
                    TextBlock("Header").Height(50).Flex(shrink: 0).Background("LightCoral"),

                    ScrollView(
                        VStack(0,
                            TextBlock("Scroll Item 1").Height(80),
                            TextBlock("Scroll Item 2").Height(80),
                            TextBlock("Scroll Item 3").Height(80),
                            TextBlock("Scroll Item 4").Height(80),
                            TextBlock("Scroll Item 5").Height(80)
                        )
                    ).Flex(grow: 1),

                    TextBlock("Footer").Height(50).Flex(shrink: 0).Background("LightBlue")
                ).Width(400).Height(400)
            );

            await Harness.Render();

            H.Check("ScrollInFlex_HeaderFooterPresent",
                H.FindText("Header") is not null &&
                H.FindText("Footer") is not null);

            var scrollViewer = H.FindControl<WinUI.ScrollViewer>(_ => true);
            H.Check("ScrollInFlex_ScrollViewerExists", scrollViewer is not null);
            H.Check("ScrollInFlex_ScrollViewerHeight",
                scrollViewer is not null && Near(scrollViewer.RenderSize.Height, 300, 10));

            H.Check("ScrollInFlex_ContentScrollable",
                H.FindText("Scroll Item 1") is not null &&
                H.FindText("Scroll Item 5") is not null);
        }
    }

    // ----------------------------------------------------------------
    // 9. FlexColumn grow distribution (vertical main axis)
    // ----------------------------------------------------------------

    internal class FlexColumnGrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexColumn(
                    TextBlock("Top").Flex(basis: 0, grow: 1).Background("LightCoral"),
                    TextBlock("Middle").Flex(basis: 0, grow: 2).Background("LightGreen"),
                    TextBlock("Bottom").Flex(basis: 0, grow: 1).Background("LightBlue")
                ).Width(400).Height(400)
            );

            await Harness.Render();

            H.Check("FlexColGrow_AllPresent",
                H.FindText("Top") is not null &&
                H.FindText("Middle") is not null &&
                H.FindText("Bottom") is not null);

            var top = H.FindText("Top");
            var mid = H.FindText("Middle");
            var bot = H.FindText("Bottom");

            H.Check("FlexColGrow_TopHeight",
                top is not null && Near(top.RenderSize.Height, 100, 5));
            H.Check("FlexColGrow_MiddleHeight",
                mid is not null && Near(mid.RenderSize.Height, 200, 5));
            H.Check("FlexColGrow_BottomHeight",
                bot is not null && Near(bot.RenderSize.Height, 100, 5));

            H.Check("FlexColGrow_FullWidth",
                top is not null && Near(top.RenderSize.Width, 400, 5));
        }
    }

    // ----------------------------------------------------------------
    // 10. Mixed grow + fixed-width children
    // ----------------------------------------------------------------

    internal class FlexMixedGrowFixed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexRow(
                    TextBlock("Fixed1").Width(100).Background("LightCoral"),
                    TextBlock("Grow").Flex(grow: 1).Background("LightGreen"),
                    TextBlock("Fixed2").Width(100).Background("LightBlue")
                ).Width(600).Height(60)
            );

            await Harness.Render();

            var fixed1 = H.FindText("Fixed1");
            var grow = H.FindText("Grow");
            var fixed2 = H.FindText("Fixed2");

            H.Check("FlexMixed_AllPresent",
                fixed1 is not null && grow is not null && fixed2 is not null);

            H.Check("FlexMixed_Fixed1Width",
                fixed1 is not null && Near(fixed1.RenderSize.Width, 100, 3));
            H.Check("FlexMixed_Fixed2Width",
                fixed2 is not null && Near(fixed2.RenderSize.Width, 100, 3));

            H.Check("FlexMixed_GrowWidth",
                grow is not null && Near(grow.RenderSize.Width, 400, 5));
        }
    }

    // ----------------------------------------------------------------
    // 11. Flex with gaps
    // ----------------------------------------------------------------

    internal class FlexWithGaps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                new FlexElement([
                    TextBlock("A").Flex(basis: 0, grow: 1).Background("LightCoral"),
                    TextBlock("B").Flex(basis: 0, grow: 1).Background("LightGreen"),
                    TextBlock("C").Flex(basis: 0, grow: 1).Background("LightBlue")
                ])
                {
                    Direction = FlexDirection.Row,
                    ColumnGap = 20,
                }.Width(600).Height(60)
            );

            await Harness.Render();

            var a = H.FindText("A");
            var b = H.FindText("B");
            var c = H.FindText("C");

            H.Check("FlexGaps_AllPresent",
                a is not null && b is not null && c is not null);

            double expected = (600 - 2 * 20) / 3.0;
            H.Check("FlexGaps_EqualWidthsWithGap",
                a is not null && b is not null && c is not null &&
                Near(a.RenderSize.Width, expected, 3) &&
                Near(b.RenderSize.Width, expected, 3) &&
                Near(c.RenderSize.Width, expected, 3));

            if (a is not null && b is not null && c is not null)
            {
                double total = a.RenderSize.Width + b.RenderSize.Width + c.RenderSize.Width + 2 * 20;
                Console.WriteLine($"# Gap layout total: {total:F1} (expected 600)");
                H.Check("FlexGaps_TotalWidthCorrect", Near(total, 600, 3));
            }
        }
    }

    // ----------------------------------------------------------------
    // 12. Flex with FlexPadding
    // ----------------------------------------------------------------

    internal class FlexWithPadding(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexRow(
                    TextBlock("Padded-A").Flex(basis: 0, grow: 1).Background("LightCoral"),
                    TextBlock("Padded-B").Flex(basis: 0, grow: 1).Background("LightBlue")
                ).FlexPadding(20).Width(600).Height(100)
            );

            await Harness.Render();

            var a = H.FindText("Padded-A");
            var b = H.FindText("Padded-B");

            H.Check("FlexPadding_AllPresent",
                a is not null && b is not null);

            H.Check("FlexPadding_ChildWidths",
                a is not null && b is not null &&
                Near(a.RenderSize.Width, 280, 3) && Near(b.RenderSize.Width, 280, 3));

            H.Check("FlexPadding_ChildHeights",
                a is not null && Near(a.RenderSize.Height, 60, 5));
        }
    }

    // ----------------------------------------------------------------
    // 13. Flex cross-axis content sizing regression
    // ----------------------------------------------------------------

    internal class FlexCrossAxisTextHeight(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var longText = "This is a moderately long piece of text that will wrap " +
                "within its allocated width. The number of lines depends on the " +
                "width constraint, so the height should be consistent.";

            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(8,
                    FlexRow(
                        TextBlock("Sidebar").Width(100).Background("LightCoral"),
                        TextBlock(longText).Flex(grow: 1).Background("LightBlue")
                    ).Width(600).Background("LightGray"),

                    TextBlock(longText).Width(500).Background("LightGreen"),

                    TextBlock("Below-all-visible")
                ).Width(600).Height(600)
            );

            await Harness.Render();

            H.Check("FlexCrossText_AllPresent",
                H.FindText("Sidebar") is not null &&
                H.FindText("Below-all-visible") is not null);

            var flex = H.FindControl<FlexPanel>(_ => true);
            H.Check("FlexCrossText_ReasonableHeight",
                flex is not null && flex.ActualHeight < 200);

            var flexTexts = H.FindAllControls<TextBlock>(tb => tb.Text == longText);
            if (flexTexts.Count >= 2)
            {
                double flexTextHeight = flexTexts[0].ActualHeight;
                double refTextHeight = flexTexts[1].ActualHeight;
                Console.WriteLine($"# Flex text height: {flexTextHeight:F1}, Ref text height: {refTextHeight:F1}");
                H.Check("FlexCrossText_HeightsMatch",
                    Near(flexTextHeight, refTextHeight, 20));
            }
        }
    }

    // ----------------------------------------------------------------
    // 14. Grid inside Flex
    // ----------------------------------------------------------------

    internal class GridInsideFlex(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexColumn(
                    TextBlock("Header").Height(50).Background("LightCoral"),

                    Grid([GridSize.Star(), GridSize.Star(2)], [GridSize.Star()],
                        TextBlock("GridLeft").Grid(row: 0, column: 0).Background("LightGreen"),
                        TextBlock("GridRight").Grid(row: 0, column: 1).Background("LightBlue")
                    ).Flex(grow: 1),

                    TextBlock("Footer").Height(50).Background("Wheat")
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("GridInFlex_AllPresent",
                H.FindText("Header") is not null &&
                H.FindText("GridLeft") is not null &&
                H.FindText("GridRight") is not null &&
                H.FindText("Footer") is not null);

            var grid = H.FindControl<WinUI.Grid>(g => g.ColumnDefinitions.Count == 2);
            H.Check("GridInFlex_GridHeight",
                grid is not null && Near(grid.ActualHeight, 300, 10));

            H.Check("GridInFlex_StarColumns",
                grid is not null &&
                Near(grid.ColumnDefinitions[0].ActualWidth, 200, 3) &&
                Near(grid.ColumnDefinitions[1].ActualWidth, 400, 3));
        }
    }

    // ----------------------------------------------------------------
    // 15. Flex with child margins
    // ----------------------------------------------------------------

    internal class FlexWithChildMargins(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexRow(
                    TextBlock("M1").Flex(basis: 0, grow: 1).Margin(10).Background("LightCoral"),
                    TextBlock("M2").Flex(basis: 0, grow: 1).Margin(10).Background("LightBlue"),
                    TextBlock("M3").Flex(basis: 0, grow: 1).Margin(10).Background("LightGreen")
                ).Width(600).Height(80)
            );

            await Harness.Render();

            var m1 = H.FindText("M1");
            var m2 = H.FindText("M2");
            var m3 = H.FindText("M3");

            H.Check("FlexMargins_AllPresent",
                m1 is not null && m2 is not null && m3 is not null);

            double expected = (600 - 6 * 10) / 3.0;
            H.Check("FlexMargins_ContentWidths",
                m1 is not null && m2 is not null && m3 is not null &&
                Near(m1.RenderSize.Width, expected, 5) &&
                Near(m2.RenderSize.Width, expected, 5) &&
                Near(m3.RenderSize.Width, expected, 5));
        }
    }

    // ----------------------------------------------------------------
    // 16. Flex with JustifyContent variations
    // ----------------------------------------------------------------

    internal class FlexJustifySpaceBetween(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                new FlexElement([
                    TextBlock("J1").Width(100).Background("LightCoral"),
                    TextBlock("J2").Width(100).Background("LightGreen"),
                    TextBlock("J3").Width(100).Background("LightBlue"),
                ])
                {
                    Direction = FlexDirection.Row,
                    JustifyContent = FlexJustify.SpaceBetween,
                }.Width(600).Height(60)
            );

            await Harness.Render();

            var j1 = H.FindText("J1");
            var j2 = H.FindText("J2");
            var j3 = H.FindText("J3");

            H.Check("FlexJustify_AllPresent",
                j1 is not null && j2 is not null && j3 is not null);

            if (j1 is not null && j2 is not null && j3 is not null)
            {
                var flex = H.FindControl<FlexPanel>(_ => true);
                if (flex is not null)
                {
                    H.Check("FlexJustify_ChildWidthsFixed",
                        Near(j1.RenderSize.Width, 100, 3) &&
                        Near(j2.RenderSize.Width, 100, 3) &&
                        Near(j3.RenderSize.Width, 100, 3));
                }
            }
        }
    }

    // ----------------------------------------------------------------
    // 17. Layout cycle prevention: FlexPanel inside Grid star column
    // ----------------------------------------------------------------

    internal class FlexLayoutCycleInGridStar(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                Grid([GridSize.Px(200), GridSize.Star()], [GridSize.Star()],
                    TextBlock("Sidebar").Grid(row: 0, column: 0).Background("LightCoral"),

                    FlexRow(
                        TextBlock("Grow-A").Flex(basis: 0, grow: 1).Background("LightGreen"),
                        TextBlock("Grow-B").Flex(basis: 0, grow: 2).Background("LightBlue")
                    ).Grid(row: 0, column: 1)
                ).Width(600).Height(300)
            );

            await Harness.Render();

            H.Check("FlexCycle_GridStar_NoException", true);

            var growA = H.FindText("Grow-A");
            var growB = H.FindText("Grow-B");
            H.Check("FlexCycle_GridStar_ChildrenPresent",
                growA is not null && growB is not null);

            H.Check("FlexCycle_GridStar_GrowDistribution",
                growA is not null && growB is not null &&
                Near(growA.RenderSize.Width, 133, 5) && Near(growB.RenderSize.Width, 267, 5));
        }
    }

    // ----------------------------------------------------------------
    // 18. Layout cycle prevention: deeply nested FlexPanels
    // ----------------------------------------------------------------

    internal class FlexLayoutCycleNestedDeep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                FlexColumn(
                    TextBlock("L1-Top").Height(40),

                    FlexRow(
                        TextBlock("L2-Left").Flex(basis: 0, grow: 1),

                        FlexColumn(
                            TextBlock("L3-Header").Height(30),

                            FlexRow(
                                TextBlock("L4-A").Flex(basis: 0, grow: 1).Background("LightCoral"),
                                TextBlock("L4-B").Flex(basis: 0, grow: 1).Background("LightBlue"),
                                TextBlock("L4-C").Flex(basis: 0, grow: 1).Background("LightGreen")
                            ).Flex(grow: 1),

                            TextBlock("L3-Footer").Height(30)
                        ).Flex(basis: 0, grow: 2)
                    ).Flex(grow: 1),

                    TextBlock("L1-Bottom").Height(40)
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("FlexCycle_Nested4_NoException", true);

            H.Check("FlexCycle_Nested4_AllPresent",
                H.FindText("L1-Top") is not null &&
                H.FindText("L2-Left") is not null &&
                H.FindText("L3-Header") is not null &&
                H.FindText("L4-A") is not null &&
                H.FindText("L4-B") is not null &&
                H.FindText("L4-C") is not null &&
                H.FindText("L3-Footer") is not null &&
                H.FindText("L1-Bottom") is not null);

            var l4a = H.FindText("L4-A");
            var l4b = H.FindText("L4-B");
            var l4c = H.FindText("L4-C");
            H.Check("FlexCycle_Nested4_L4Distribution",
                l4a is not null && l4b is not null && l4c is not null &&
                Near(l4a.ActualWidth, l4b.ActualWidth, 3) &&
                Near(l4b.ActualWidth, l4c.ActualWidth, 3));
        }
    }

    // ----------------------------------------------------------------
    // 19. Layout cycle prevention: FlexPanel with auto-sizing text
    // ----------------------------------------------------------------

    internal class FlexLayoutCycleAutoText(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var longText = "This text is long enough to wrap at narrow widths but " +
                "should display in fewer lines at wider widths. The layout cycle " +
                "bug would cause a crash when text is re-measured during arrange.";

            var host = H.CreateHost();
            host.Mount(ctx =>
                Grid([GridSize.Star(), GridSize.Star(2)], [GridSize.Auto, GridSize.Star()],
                    FlexRow(
                        TextBlock("Label").Width(80).Background("LightCoral"),
                        TextBlock(longText).Flex(grow: 1).Background("LightBlue")
                    ).Grid(row: 0, column: 0, columnSpan: 2),

                    FlexRow(
                        TextBlock("Body-A").Flex(basis: 0, grow: 1).Background("LightGreen"),
                        TextBlock("Body-B").Flex(basis: 0, grow: 1).Background("Wheat")
                    ).Grid(row: 1, column: 0, columnSpan: 2)
                ).Width(600).Height(400)
            );

            await Harness.Render();

            H.Check("FlexCycle_AutoText_NoException", true);

            H.Check("FlexCycle_AutoText_ChildrenPresent",
                H.FindText("Label") is not null &&
                H.FindText("Body-A") is not null &&
                H.FindText("Body-B") is not null);

            var flexPanels = H.FindAllControls<FlexPanel>(_ => true);
            var topRow = flexPanels.FirstOrDefault(p =>
                p.Direction == FlexDirection.Row && p.ActualHeight < 200);
            H.Check("FlexCycle_AutoText_ReasonableHeight",
                topRow is not null && topRow.ActualHeight > 10);
        }
    }

    // ----------------------------------------------------------------
    // 20. Layout cycle prevention: size mismatch between Measure and Arrange
    // ----------------------------------------------------------------

    internal class FlexLayoutCycleSizeMismatch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                Grid([GridSize.Star()], [GridSize.Px(60), GridSize.Star(), GridSize.Px(60)],
                    TextBlock("Header").Grid(row: 0, column: 0).Background("LightCoral"),

                    ScrollView(
                        FlexColumn(
                            TextBlock("Item-1").Height(50).Background("LightBlue"),
                            TextBlock("Item-2").Height(50).Background("LightGreen"),
                            TextBlock("Item-3").Height(50).Background("Wheat"),
                            TextBlock("Item-4").Height(50).Background("Plum"),
                            TextBlock("Item-5").Height(50).Background("LightCoral"),
                            TextBlock("Item-6").Height(50).Background("PeachPuff")
                        ).Width(400)
                    ).Grid(row: 1, column: 0),

                    TextBlock("Footer").Grid(row: 2, column: 0).Background("LightGray")
                ).Width(400).Height(300)
            );

            await Harness.Render();

            H.Check("FlexCycle_SizeMismatch_NoException", true);

            H.Check("FlexCycle_SizeMismatch_AllPresent",
                H.FindText("Header") is not null &&
                H.FindText("Item-1") is not null &&
                H.FindText("Item-6") is not null &&
                H.FindText("Footer") is not null);

            var flex = H.FindControl<FlexPanel>(_ => true);
            H.Check("FlexCycle_SizeMismatch_ContentSized",
                flex is not null && Near(flex.ActualHeight, 300, 5));
        }
    }

    // ----------------------------------------------------------------
    // Flex wrapping depth mutation regression
    // ----------------------------------------------------------------
    // When a FlexColumn with a shrink:0 child + grow:1 child is wrapped
    // in increasing layers of FlexColumn during reconciliation, the flex
    // properties get corrupted and both children end up at ~50/50 instead
    // of fit/fill. This is a regression test for that behavior.

    private static Element Wrapit(Element fe, int count)
        => count == 0 ? fe : Wrapit(FlexColumn(fe), count - 1);

    internal class FlexWrapDepthMutation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<int>? setDepth = null;

            host.Mount(ctx =>
            {
                var (cur, setCur) = ctx.UseState(0);
                setDepth = setCur;

                return Wrapit(
                    FlexColumn(
                        TextBlock($"blue: {cur}").Background("LightCoral"),
                        TextBlock("red").Background("LightBlue").Flex(grow: 1)
                    ).Height(400).Width(400),
                    cur
                );
            });

            await Harness.Render();

            // At depth=0: "blue" text takes natural height (~20px), "red" text
            // has grow:1 and should fill the rest (~380px).
            var red0 = H.FindText("red");
            var blue0 = H.FindTextContaining("blue:");
            H.Check("FlexWrapDepth_Initial_AllPresent",
                red0 is not null && blue0 is not null);

            double initialRedH = red0?.RenderSize.Height ?? 0;
            double initialBlueH = blue0?.RenderSize.Height ?? 0;
            H.Check("FlexWrapDepth_Initial_RedMuchTaller",
                initialRedH > initialBlueH * 3);

            // Increment depth twice: 0 → 1 → 2 (two extra wrapper layers)
            setDepth?.Invoke(1);
            await Harness.Render();
            setDepth?.Invoke(2);
            await Harness.Render();

            // After two wrapping changes, the grow:1 "red" text should still
            // fill most of the 400px. The bug causes ~50/50 split.
            var red2 = H.FindText("red");
            var blue2 = H.FindTextContaining("blue:");
            H.Check("FlexWrapDepth_AfterMutation_AllPresent",
                red2 is not null && blue2 is not null);

            double mutatedRedH = red2?.RenderSize.Height ?? 0;
            double mutatedBlueH = blue2?.RenderSize.Height ?? 0;

            // The key assertion: red should still be much taller than blue.
            // With the bug, they're roughly equal (~50/50 at 200px each).
            H.Check("FlexWrapDepth_AfterMutation_RedStillMuchTaller",
                mutatedRedH > mutatedBlueH * 3);

            H.Check("FlexWrapDepth_AfterMutation_NeitherZero",
                mutatedRedH > 10 && mutatedBlueH > 10);
        }
    }
}
