using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class StackPanelPage : Component
{
    public override Element Render()
    {
        var (spacing, setSpacing) = UseState(8.0);

        Element ColorBox(string color, string label) =>
            Border(TextBlock(label).Center().Foreground("#FFFFFF"))
                .Background(color).Size(80, 50).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft);

        return ScrollView(
            VStack(16,
                PageHeader("StackPanel", "Arranges children in a vertical or horizontal line with configurable spacing."),

                SampleCard("VStack (Vertical)",
                    VStack(spacing,
                        ColorBox("#E74C3C", "A"),
                        ColorBox("#3498DB", "B"),
                        ColorBox("#2ECC71", "C"),
                        ColorBox("#F39C12", "D")
                    ),
                    """
                    VStack(8,
                        Box("A"), Box("B"), Box("C"), Box("D")
                    )
                    """,
                    OptionPanel(
                        TextBlock($"Spacing: {(int)spacing}"),
                        Slider(spacing, 0, 32, setSpacing)
                    )),

                SampleCard("HStack (Horizontal)",
                    HStack(spacing,
                        ColorBox("#9B59B6", "1"),
                        ColorBox("#1ABC9C", "2"),
                        ColorBox("#E67E22", "3"),
                        ColorBox("#2C3E50", "4")
                    ),
                    """
                    HStack(8,
                        Box("1"), Box("2"), Box("3"), Box("4")
                    )
                    """),

                SampleCard("Nested Stacks",
                    VStack(12,
                        HStack(8,
                            ColorBox("#E74C3C", "R1"),
                            ColorBox("#3498DB", "R1"),
                            ColorBox("#2ECC71", "R1")
                        ),
                        HStack(8,
                            ColorBox("#F39C12", "R2"),
                            ColorBox("#9B59B6", "R2")
                        )
                    ),
                    """
                    VStack(12,
                        HStack(8, Box("R1"), Box("R1"), Box("R1")),
                        HStack(8, Box("R2"), Box("R2"))
                    )
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
