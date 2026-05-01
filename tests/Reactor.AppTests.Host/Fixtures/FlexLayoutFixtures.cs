using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// Comprehensive Flex layout fixtures covering nesting, composition with
/// other layout panels, scroll interaction, and cross-axis sizing.
/// All elements that tests measure have AutomationIds.
/// </summary>
internal static class FlexLayoutFixtures
{
    // 1. Nested Flex: FlexRow children inside FlexColumn (holy grail)
    internal static Element FlexNestedRowInColumn(RenderContext ctx) =>
        FlexColumn(
            // Header row: fixed height
            FlexRow(
                TextBlock("Logo").Flex(basis: 0, grow: 1).AutomationId("Logo").Background("LightCoral"),
                TextBlock("Nav").Flex(basis: 0, grow: 2).AutomationId("Nav").Background("LightBlue")
            ).Height(50).AutomationId("HeaderRow"),

            // Body row: grows to fill remaining space
            FlexRow(
                TextBlock("Sidebar").Flex(basis: 0, grow: 1).AutomationId("Sidebar").Background("LightGreen"),
                TextBlock("Content").Flex(basis: 0, grow: 3).AutomationId("Content").Background("LightYellow")
            ).Flex(grow: 1).AutomationId("BodyRow"),

            // Footer row: fixed height
            FlexRow(
                TextBlock("Footer").Flex(grow: 1).AutomationId("Footer")
            ).Height(40).Background("LightGray").AutomationId("FooterRow")
        ).Width(600).Height(400);

    // 2. Nested Flex: FlexColumn inside FlexRow (sidebar layout)
    internal static Element FlexNestedColumnInRow(RenderContext ctx) =>
        FlexRow(
            // Sidebar: 1/4 width
            FlexColumn(
                TextBlock("Menu A").AutomationId("MenuA"),
                TextBlock("Menu B").AutomationId("MenuB"),
                TextBlock("Menu C").AutomationId("MenuC")
            ).Flex(basis: 0, grow: 1).Background("LightCoral").AutomationId("SidebarCol"),

            // Content area: 3/4 width
            FlexColumn(
                TextBlock("Title").Height(40).AutomationId("Title"),
                TextBlock("Body text here").Flex(grow: 1).AutomationId("BodyText")
            ).Flex(basis: 0, grow: 3).Background("LightBlue").AutomationId("ContentCol")
        ).Width(600).Height(300);

    // 3. Deep nesting: 3+ levels of FlexPanels
    internal static Element FlexNestedDeep(RenderContext ctx) =>
        // Level 1: Column
        FlexColumn(
            TextBlock("L1-Header").Height(30).AutomationId("L1Header"),

            // Level 2: Row (grows)
            FlexRow(
                TextBlock("L2-Left").Flex(basis: 0, grow: 1).AutomationId("L2Left"),

                // Level 3: Column inside the row
                FlexColumn(
                    TextBlock("L3-Top").AutomationId("L3Top"),
                    TextBlock("L3-Mid").Flex(grow: 1).AutomationId("L3Mid"),
                    TextBlock("L3-Bot").AutomationId("L3Bot")
                ).Flex(basis: 0, grow: 2).AutomationId("L3Col")
            ).Flex(grow: 1).AutomationId("L2Row"),

            TextBlock("L1-Footer").Height(30).AutomationId("L1Footer")
        ).Width(600).Height(400);

    // 4. Flex inside Grid star cells
    internal static Element FlexInsideGrid(RenderContext ctx) =>
        Grid([GridSize.Star(), GridSize.Star(2)], [GridSize.Star(), GridSize.Star()],
            // Top-left: FlexRow in 1* cell
            FlexRow(
                TextBlock("A1").Flex(grow: 1).AutomationId("A1").Background("LightCoral"),
                TextBlock("A2").Flex(grow: 1).AutomationId("A2").Background("LightBlue")
            ).Grid(row: 0, column: 0),

            // Top-right: FlexColumn in 2* cell
            FlexColumn(
                TextBlock("B1").Flex(grow: 1).AutomationId("B1").Background("LightGreen"),
                TextBlock("B2").Flex(grow: 2).AutomationId("B2").Background("LightYellow")
            ).Grid(row: 0, column: 1),

            // Bottom-left: single grow child
            FlexRow(
                TextBlock("C1").Flex(grow: 1).AutomationId("C1").Background("Wheat")
            ).Grid(row: 1, column: 0),

            // Bottom-right: mixed fixed + grow
            FlexRow(
                TextBlock("D1").Width(80).AutomationId("D1").Background("Plum"),
                TextBlock("D2").Flex(grow: 1).AutomationId("D2").Background("PeachPuff")
            ).Grid(row: 1, column: 1)
        ).Width(600).Height(400);

    // 5. Flex inside Border -- cross-axis content sizing
    internal static Element FlexInsideBorder(RenderContext ctx) =>
        VStack(8,
            // A FlexRow inside a Border -- should only be as tall as its content
            Border(
                FlexRow(
                    TextBlock("Left").Flex(grow: 1).AutomationId("Left").Background("LightCoral"),
                    TextBlock("Right").Flex(grow: 1).AutomationId("Right").Background("LightBlue")
                ).AutomationId("FlexInBorderRow")
            ).Width(600).Background("LightGray"),

            // Reference: plain text for height comparison
            TextBlock("Reference line").AutomationId("ReferenceLine")
        ).Width(600).Height(400);

    // 6. Flex inside VStack
    internal static Element FlexInsideVStack(RenderContext ctx) =>
        VStack(4,
            TextBlock("Before").AutomationId("Before"),

            FlexRow(
                TextBlock("Row1-A").Flex(basis: 0, grow: 1).AutomationId("Row1A").Background("LightCoral"),
                TextBlock("Row1-B").Flex(basis: 0, grow: 2).AutomationId("Row1B").Background("LightBlue"),
                TextBlock("Row1-C").Flex(basis: 0, grow: 1).AutomationId("Row1C").Background("LightGreen")
            ).AutomationId("FlexRow1"),

            FlexRow(
                TextBlock("Row2-A").Flex(basis: 0, grow: 1).AutomationId("Row2A").Background("Wheat"),
                TextBlock("Row2-B").Flex(basis: 0, grow: 1).AutomationId("Row2B").Background("Plum")
            ).AutomationId("FlexRow2"),

            TextBlock("After").AutomationId("After")
        ).Width(600).Height(400);

    // 7. Flex inside ScrollViewer (infinite height)
    internal static Element FlexInsideScrollView(RenderContext ctx) =>
        ScrollView(
            FlexColumn(
                TextBlock("Item 1").Height(60).AutomationId("ScrollItem1").Background("LightCoral"),
                TextBlock("Item 2").Height(60).AutomationId("ScrollItem2").Background("LightBlue"),
                TextBlock("Item 3").Height(60).AutomationId("ScrollItem3").Background("LightGreen"),
                TextBlock("Item 4").Height(60).AutomationId("ScrollItem4").Background("Wheat"),
                TextBlock("Item 5").Height(60).AutomationId("ScrollItem5").Background("Plum")
            ).Width(400).AutomationId("ScrollFlexCol")
        ).Width(400).Height(200);

    // 8. ScrollViewer inside a Flex grow child
    internal static Element ScrollViewInsideFlex(RenderContext ctx) =>
        FlexColumn(
            TextBlock("Header").Height(50).Flex(shrink: 0).AutomationId("Header").Background("LightCoral"),

            ScrollView(
                VStack(0,
                    TextBlock("Scroll Item 1").Height(80).AutomationId("SIFScrollItem1"),
                    TextBlock("Scroll Item 2").Height(80).AutomationId("SIFScrollItem2"),
                    TextBlock("Scroll Item 3").Height(80).AutomationId("SIFScrollItem3"),
                    TextBlock("Scroll Item 4").Height(80).AutomationId("SIFScrollItem4"),
                    TextBlock("Scroll Item 5").Height(80).AutomationId("SIFScrollItem5")
                )
            ).Flex(grow: 1).AutomationId("ScrollViewerInFlex"),

            TextBlock("Footer").Height(50).Flex(shrink: 0).AutomationId("Footer").Background("LightBlue")
        ).Width(400).Height(400);

    // 9. FlexColumn grow distribution (vertical main axis)
    internal static Element FlexColumnGrow(RenderContext ctx) =>
        FlexColumn(
            TextBlock("Top").Flex(basis: 0, grow: 1).AutomationId("Top").Background("LightCoral"),
            TextBlock("Middle").Flex(basis: 0, grow: 2).AutomationId("Middle").Background("LightGreen"),
            TextBlock("Bottom").Flex(basis: 0, grow: 1).AutomationId("Bottom").Background("LightBlue")
        ).Width(400).Height(400);

    // 10. Mixed grow + fixed-width children
    internal static Element FlexMixedGrowFixed(RenderContext ctx) =>
        FlexRow(
            TextBlock("Fixed1").Width(100).AutomationId("Fixed1").Background("LightCoral"),
            TextBlock("Grow").Flex(grow: 1).AutomationId("Grow").Background("LightGreen"),
            TextBlock("Fixed2").Width(100).AutomationId("Fixed2").Background("LightBlue")
        ).Width(600).Height(60);

    // 11. Flex with gaps
    internal static Element FlexWithGaps(RenderContext ctx) =>
        new FlexElement([
            TextBlock("A").Flex(basis: 0, grow: 1).AutomationId("GapA").Background("LightCoral"),
            TextBlock("B").Flex(basis: 0, grow: 1).AutomationId("GapB").Background("LightGreen"),
            TextBlock("C").Flex(basis: 0, grow: 1).AutomationId("GapC").Background("LightBlue")
        ])
        {
            Direction = FlexDirection.Row,
            ColumnGap = 20,
        }.Width(600).Height(60);

    // 12. Flex with FlexPadding
    internal static Element FlexWithPadding(RenderContext ctx) =>
        FlexRow(
            TextBlock("Padded-A").Flex(basis: 0, grow: 1).AutomationId("PaddedA").Background("LightCoral"),
            TextBlock("Padded-B").Flex(basis: 0, grow: 1).AutomationId("PaddedB").Background("LightBlue")
        ).FlexPadding(20).Width(600).Height(100);

    // 13. Flex cross-axis content sizing regression
    internal static Element FlexCrossAxisTextHeight(RenderContext ctx)
    {
        var longText = "This is a moderately long piece of text that will wrap " +
            "within its allocated width. The number of lines depends on the " +
            "width constraint, so the height should be consistent.";

        return VStack(8,
            // FlexRow with grow + long text
            FlexRow(
                TextBlock("Sidebar").Width(100).AutomationId("CrossSidebar").Background("LightCoral"),
                TextBlock(longText).Flex(grow: 1).AutomationId("CrossLongText").Background("LightBlue")
            ).Width(600).Background("LightGray").AutomationId("CrossFlexRow"),

            // Reference: same text in a fixed-width container
            TextBlock(longText).Width(500).AutomationId("CrossRefText").Background("LightGreen"),

            TextBlock("Below-all-visible").AutomationId("BelowAll")
        ).Width(600).Height(600);
    }

    // 14. Grid inside Flex -- Grid as a grow child
    internal static Element GridInsideFlex(RenderContext ctx) =>
        FlexColumn(
            TextBlock("Header").Height(50).AutomationId("GIFHeader").Background("LightCoral"),

            Grid([GridSize.Star(), GridSize.Star(2)], [GridSize.Star()],
                TextBlock("GridLeft").Grid(row: 0, column: 0).AutomationId("GridLeft").Background("LightGreen"),
                TextBlock("GridRight").Grid(row: 0, column: 1).AutomationId("GridRight").Background("LightBlue")
            ).Flex(grow: 1).AutomationId("GridInFlex"),

            TextBlock("Footer").Height(50).AutomationId("GIFFooter").Background("Wheat")
        ).Width(600).Height(400);

    // 15. Flex with child margins
    internal static Element FlexWithChildMargins(RenderContext ctx) =>
        FlexRow(
            TextBlock("M1").Flex(basis: 0, grow: 1).Margin(10).AutomationId("M1").Background("LightCoral"),
            TextBlock("M2").Flex(basis: 0, grow: 1).Margin(10).AutomationId("M2").Background("LightBlue"),
            TextBlock("M3").Flex(basis: 0, grow: 1).Margin(10).AutomationId("M3").Background("LightGreen")
        ).Width(600).Height(80);

    // 16. Flex with JustifyContent=SpaceBetween
    internal static Element FlexJustifySpaceBetween(RenderContext ctx) =>
        new FlexElement([
            TextBlock("J1").Width(100).AutomationId("J1").Background("LightCoral"),
            TextBlock("J2").Width(100).AutomationId("J2").Background("LightGreen"),
            TextBlock("J3").Width(100).AutomationId("J3").Background("LightBlue"),
        ])
        {
            Direction = FlexDirection.Row,
            JustifyContent = FlexJustify.SpaceBetween,
        }.Width(600).Height(60);

    // 17. Layout cycle prevention: FlexPanel inside Grid star column
    internal static Element FlexLayoutCycleInGridStar(RenderContext ctx) =>
        Grid([GridSize.Px(200), GridSize.Star()], [GridSize.Star()],
            // Fixed sidebar
            TextBlock("Sidebar").Grid(row: 0, column: 0).AutomationId("CycleSidebar").Background("LightCoral"),

            // Star column: FlexPanel
            FlexRow(
                TextBlock("Grow-A").Flex(basis: 0, grow: 1).AutomationId("GrowA").Background("LightGreen"),
                TextBlock("Grow-B").Flex(basis: 0, grow: 2).AutomationId("GrowB").Background("LightBlue")
            ).Grid(row: 0, column: 1)
        ).Width(600).Height(300);

    // 18. Layout cycle prevention: deeply nested FlexPanels
    internal static Element FlexLayoutCycleNestedDeep(RenderContext ctx) =>
        // Level 1: Column with grow
        FlexColumn(
            TextBlock("L1-Top").Height(40).AutomationId("CycleL1Top"),

            // Level 2: Row with grow children
            FlexRow(
                TextBlock("L2-Left").Flex(basis: 0, grow: 1).AutomationId("CycleL2Left"),

                // Level 3: Column inside grow child
                FlexColumn(
                    TextBlock("L3-Header").Height(30).AutomationId("CycleL3Header"),

                    // Level 4: Row inside grow child
                    FlexRow(
                        TextBlock("L4-A").Flex(basis: 0, grow: 1).AutomationId("CycleL4A").Background("LightCoral"),
                        TextBlock("L4-B").Flex(basis: 0, grow: 1).AutomationId("CycleL4B").Background("LightBlue"),
                        TextBlock("L4-C").Flex(basis: 0, grow: 1).AutomationId("CycleL4C").Background("LightGreen")
                    ).Flex(grow: 1),

                    TextBlock("L3-Footer").Height(30).AutomationId("CycleL3Footer")
                ).Flex(basis: 0, grow: 2)
            ).Flex(grow: 1),

            TextBlock("L1-Bottom").Height(40).AutomationId("CycleL1Bottom")
        ).Width(600).Height(400);

    // 19. Layout cycle prevention: FlexPanel with auto-sizing text
    internal static Element FlexLayoutCycleAutoText(RenderContext ctx)
    {
        var longText = "This text is long enough to wrap at narrow widths but " +
            "should display in fewer lines at wider widths. The layout cycle " +
            "bug would cause a crash when text is re-measured during arrange.";

        return Grid([GridSize.Star(), GridSize.Star(2)], [GridSize.Auto, GridSize.Star()],
            // Row 0: Auto-height row with FlexPanel containing wrapping text
            FlexRow(
                TextBlock("Label").Width(80).AutomationId("AutoLabel").Background("LightCoral"),
                TextBlock(longText).Flex(grow: 1).AutomationId("AutoLongText").Background("LightBlue")
            ).Grid(row: 0, column: 0, columnSpan: 2).AutomationId("AutoTextFlexRow"),

            // Row 1: star row with more flex content
            FlexRow(
                TextBlock("Body-A").Flex(basis: 0, grow: 1).AutomationId("AutoBodyA").Background("LightGreen"),
                TextBlock("Body-B").Flex(basis: 0, grow: 1).AutomationId("AutoBodyB").Background("Wheat")
            ).Grid(row: 1, column: 0, columnSpan: 2)
        ).Width(600).Height(400);
    }

    // 20. Layout cycle prevention: size mismatch between Measure and Arrange
    internal static Element FlexLayoutCycleSizeMismatch(RenderContext ctx) =>
        // Outer Grid constrains the viewport
        Grid([GridSize.Star()], [GridSize.Px(60), GridSize.Star(), GridSize.Px(60)],
            // Header
            TextBlock("Header").Grid(row: 0, column: 0).AutomationId("MismatchHeader").Background("LightCoral"),

            // Middle: ScrollViewer constrains the FlexPanel
            ScrollView(
                FlexColumn(
                    TextBlock("Item-1").Height(50).AutomationId("MismatchItem1").Background("LightBlue"),
                    TextBlock("Item-2").Height(50).AutomationId("MismatchItem2").Background("LightGreen"),
                    TextBlock("Item-3").Height(50).AutomationId("MismatchItem3").Background("Wheat"),
                    TextBlock("Item-4").Height(50).AutomationId("MismatchItem4").Background("Plum"),
                    TextBlock("Item-5").Height(50).AutomationId("MismatchItem5").Background("LightCoral"),
                    TextBlock("Item-6").Height(50).AutomationId("MismatchItem6").Background("PeachPuff")
                ).Width(400).AutomationId("MismatchFlexCol")
            ).Grid(row: 1, column: 0),

            // Footer
            TextBlock("Footer").Grid(row: 2, column: 0).AutomationId("MismatchFooter").Background("LightGray")
        ).Width(400).Height(300);
}
