using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class FlexPage : Component
{
    public override Element Render()
    {
        var (dirIndex, setDirIndex) = UseState(0);
        var (justifyIndex, setJustifyIndex) = UseState(0);
        var (alignIndex, setAlignIndex) = UseState(0);
        var (wrapIndex, setWrapIndex) = UseState(0);

        var dirs = new[] { "Row", "Column", "RowReverse", "ColumnReverse" };
        var justifies = new[] { "FlexStart", "Center", "FlexEnd", "SpaceBetween", "SpaceAround", "SpaceEvenly" };
        var aligns = new[] { "Stretch", "FlexStart", "Center", "FlexEnd" };
        var wraps = new[] { "NoWrap", "Wrap", "WrapReverse" };

        var direction = (FlexDirection)new[] { 2, 0, 3, 1 }[dirIndex];
        var justify = (FlexJustify)new[] { 1, 2, 3, 4, 5, 6 }[justifyIndex];
        var align = (FlexAlign)new[] { 4, 1, 2, 3 }[alignIndex];
        var wrap = (FlexWrap)wrapIndex;

        Element Box(string label, string color) =>
            Border(TextBlock(label).Center().Foreground("#FFFFFF"))
                .Background(color).Size(70, 50).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft);

        return ScrollView(
            VStack(16,
                PageHeader("Flex", "CSS Flexbox-inspired layout — a Reactor-specific showcase."),

                SampleCard("Interactive Flex Container",
                    Border(
                        Flex(new[]
                        {
                            Box("1", "#E74C3C"), Box("2", "#3498DB"), Box("3", "#2ECC71"),
                            Box("4", "#F39C12"), Box("5", "#9B59B6")
                        }) with
                        {
                            Direction = direction,
                            JustifyContent = justify,
                            AlignItems = align,
                            Wrap = wrap,
                            ColumnGap = 8,
                            RowGap = 8,
                        }
                    ).Size(400, 250).Background(Theme.SubtleFill).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft),
                    @"new FlexElement(children) {\n    Direction = FlexDirection.Row,\n    JustifyContent = FlexJustify.SpaceBetween,\n    AlignItems = FlexAlign.Center,\n    Wrap = FlexWrap.Wrap\n}",
                    OptionPanel(
                        TextBlock("Direction"), ComboBox(dirs, dirIndex, setDirIndex),
                        TextBlock("Justify"), ComboBox(justifies, justifyIndex, setJustifyIndex),
                        TextBlock("Align"), ComboBox(aligns, alignIndex, setAlignIndex),
                        TextBlock("Wrap"), ComboBox(wraps, wrapIndex, setWrapIndex)
                    )),

                SampleCard("Grow & Shrink",
                    Flex(new Element[]
                    {
                        Border(TextBlock("Fixed").Center().Foreground("#FFFFFF"))
                            .Background("#E74C3C").Height(40).Width(80).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft),
                        Border(TextBlock("Grow 1").Center().Foreground("#FFFFFF"))
                            .Background("#3498DB").Height(40).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
                            .Flex(grow: 1),
                        Border(TextBlock("Grow 2").Center().Foreground("#FFFFFF"))
                            .Background("#2ECC71").Height(40).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)
                            .Flex(grow: 2),
                    }) with
                    {
                        Direction = FlexDirection.Row,
                        ColumnGap = 8,
                    },
                    @"Border(...).Flex(grow: 1)  // takes 1 share\nBorder(...).Flex(grow: 2)  // takes 2 shares"),

                SampleCard("FlexRow & FlexColumn Shortcuts",
                    HStack(16,
                        VStack(4,
                            TextBlock("FlexRow:").Bold(),
                            FlexRow(
                                Box("A", "#1ABC9C"), Box("B", "#E67E22"), Box("C", "#8E44AD")
                            )
                        ),
                        VStack(4,
                            TextBlock("FlexColumn:").Bold(),
                            FlexColumn(
                                Box("X", "#C0392B"), Box("Y", "#27AE60"), Box("Z", "#2980B9")
                            )
                        )
                    ),
                    @"FlexRow(Box(""A""), Box(""B""), Box(""C""))\nFlexColumn(Box(""X""), Box(""Y""), Box(""Z""))")
            ).Margin(36, 24, 36, 36)
        );
    }
}
