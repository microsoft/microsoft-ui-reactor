using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class FlipViewPage : Component
{
    public override Element Render()
    {
        var (index, setIndex) = UseState(0);
        var colors = new[] { "#FF4444", "#44AA44", "#4444FF", "#AAAA00" };

        return ScrollView(
            VStack(16,
                PageHeader("FlipView", "Presents a collection of items one at a time with flipping navigation."),

                SampleCard("Basic FlipView",
                    FlipView(
                        colors.Select((c, i) =>
                            Border(TextBlock($"Item {i + 1}").Center().Foreground("#FFFFFF").Bold())
                                .Background(c).Size(300, 200)
                        ).ToArray()
                    ),
                    """
                    FlipView(
                        Border(TextBlock("Item 1")).Background("#FF4444").Size(300, 200),
                        Border(TextBlock("Item 2")).Background("#44AA44").Size(300, 200)
                    )
                    """),

                SampleCard("Data-Driven FlipView",
                    FlipView(
                        colors,
                        c => c,
                        (c, i) => Border(
                            VStack(4,
                                TextBlock($"Slide {i + 1}").Bold().Foreground("#FFFFFF"),
                                TextBlock(c).Foreground("#FFFFFF")
                            ).Center()
                        ).Background(c).Size(300, 200)
                    ),
                    """
                    FlipView(
                        colors,
                        c => c,
                        (c, i) => Border(VStack(...)).Background(c).Size(300, 200)
                    )
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
