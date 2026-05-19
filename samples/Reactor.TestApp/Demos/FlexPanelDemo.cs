using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

class FlexPanelDemo : Component
{
    public override Element Render()
    {
        var (direction, setDirection) = UseState(FlexDirection.Row);
        var (wrap, setWrap) = UseState(FlexWrap.NoWrap);
        var (justify, setJustify) = UseState(FlexJustify.FlexStart);
        var (alignItems, setAlignItems) = UseState(FlexAlign.Stretch);

        return ScrollView(VStack(16,
            Heading("FlexPanel"),
            TextBlock("Interactive CSS Flexbox layout."),

            // Controls
            HStack(8,
                VStack(4,
                    TextBlock("Direction").SemiBold(),
                    Button("Row", () => setDirection(FlexDirection.Row))
                        .IsEnabled(!(direction == FlexDirection.Row)),
                    Button("Column", () => setDirection(FlexDirection.Column))
                        .IsEnabled(!(direction == FlexDirection.Column)),
                    Button("Row Reverse", () => setDirection(FlexDirection.RowReverse))
                        .IsEnabled(!(direction == FlexDirection.RowReverse))
                ),
                VStack(4,
                    TextBlock("Wrap").SemiBold(),
                    Button("No Wrap", () => setWrap(FlexWrap.NoWrap))
                        .IsEnabled(!(wrap == FlexWrap.NoWrap)),
                    Button("Wrap", () => setWrap(FlexWrap.Wrap))
                        .IsEnabled(!(wrap == FlexWrap.Wrap))
                ),
                VStack(4,
                    TextBlock("Justify Content").SemiBold(),
                    Button("Start", () => setJustify(FlexJustify.FlexStart))
                        .IsEnabled(!(justify == FlexJustify.FlexStart)),
                    Button("Center", () => setJustify(FlexJustify.Center))
                        .IsEnabled(!(justify == FlexJustify.Center)),
                    Button("Space Between", () => setJustify(FlexJustify.SpaceBetween))
                        .IsEnabled(!(justify == FlexJustify.SpaceBetween)),
                    Button("Space Evenly", () => setJustify(FlexJustify.SpaceEvenly))
                        .IsEnabled(!(justify == FlexJustify.SpaceEvenly))
                ),
                VStack(4,
                    TextBlock("Align Items").SemiBold(),
                    Button("Stretch", () => setAlignItems(FlexAlign.Stretch))
                        .IsEnabled(!(alignItems == FlexAlign.Stretch)),
                    Button("Center", () => setAlignItems(FlexAlign.Center))
                        .IsEnabled(!(alignItems == FlexAlign.Center)),
                    Button("Start", () => setAlignItems(FlexAlign.FlexStart))
                        .IsEnabled(!(alignItems == FlexAlign.FlexStart)),
                    Button("End", () => setAlignItems(FlexAlign.FlexEnd))
                        .IsEnabled(!(alignItems == FlexAlign.FlexEnd))
                )
            ),

            Caption($"Direction={direction}  Wrap={wrap}  Justify={justify}  Align={alignItems}")
                .Foreground(TertiaryText),

            // Live flex container
            SubHeading("Live Preview"),
            Border(
                new FlexElement([
                    ColorBox("A", "#4A90D9").Flex(grow: 1).Size(80, 60),
                    ColorBox("B", "#50B86C").Flex(grow: 2).Size(80, 80),
                    ColorBox("C", "#E8834A").Flex(grow: 1).Size(80, 40),
                    ColorBox("D", "#9B59B6").Size(80, 50),
                    ColorBox("E", "#E74C3C").Flex(grow: 1).Size(80, 70),
                ])
                {
                    Direction = direction,
                    Wrap = wrap,
                    JustifyContent = justify,
                    AlignItems = alignItems,
                    ColumnGap = 8,
                    RowGap = 8,
                }
            ).Background(SubtleFill).CornerRadius(8).Padding(8).Height(300),

            // Grow ratio demo
            SubHeading("Grow Ratios"),
            TextBlock("Items with grow 1 : 2 : 1 share available space proportionally."),
            new FlexElement([
                ColorBox("1x", "#4A90D9").Flex(grow: 1).Height(50),
                ColorBox("2x", "#50B86C").Flex(grow: 2).Height(50),
                ColorBox("1x", "#E8834A").Flex(grow: 1).Height(50),
            ])
            {
                Direction = FlexDirection.Row,
                ColumnGap = 8,
            },

            // Wrap demo
            SubHeading("Wrap"),
            TextBlock("Eight items with wrap enabled flow onto multiple lines."),
            new FlexElement(
                Enumerable.Range(1, 8).Select(i =>
                    ColorBox($"{i}", i % 2 == 0 ? "#4A90D9" : "#E8834A")
                        .Size(120, 50).Flex(grow: 1)
                ).ToArray()
            )
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                ColumnGap = 8,
                RowGap = 8,
            }
        ));
    }

    static Element ColorBox(string label, string color) =>
        Border(
            TextBlock(label).SemiBold().HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
        ).Background(color).CornerRadius(4).Padding(8);
}
