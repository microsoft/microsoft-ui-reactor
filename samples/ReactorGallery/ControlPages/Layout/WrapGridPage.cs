using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class WrapGridPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(10.0);
        var colors = new[] { "#E74C3C", "#3498DB", "#2ECC71", "#F39C12", "#9B59B6", "#1ABC9C" };

        return ScrollView(
            VStack(16,
                PageHeader("WrapGrid", "Wraps items to the next row or column automatically."),

                SampleCard("Basic WrapGrid",
                    WrapGrid(
                        Enumerable.Range(1, (int)count)
                            .Select(i =>
                                Border(TextBlock($"{i}").Center().Foreground("#FFFFFF"))
                                    .Background(colors[(i - 1) % colors.Length])
                                    .Size(70, 70).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft).Margin(4)
                            ).ToArray()
                    ),
                    """
                    WrapGrid(
                        items.Select(i =>
                            Border(TextBlock($"{i}")).Size(70, 70).Margin(4)
                        ).ToArray()
                    )
                    """,
                    OptionPanel(
                        TextBlock($"Item count: {(int)count}"),
                        Slider(count, 1, 24, setCount)
                    )),

                SampleCard("MaxRowsOrColumns WrapGrid",
                    WrapGrid(4,
                        Enumerable.Range(1, 12)
                            .Select(i =>
                                Border(TextBlock($"#{i}").Center().Foreground("#FFFFFF"))
                                    .Background("#5B6ABF").Size(80, 50).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft).Margin(2)
                            ).ToArray()
                    ),
                    """
                    WrapGrid(4,  // max 4 per row
                        items.Select(i => Border(TextBlock($"#{i}")).Size(80, 50)).ToArray()
                    )
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
