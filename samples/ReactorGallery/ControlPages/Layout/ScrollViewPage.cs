using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class ScrollViewPage : Component
{
    public override Element Render()
    {
        var (itemCount, setItemCount) = UseState(20.0);

        return ScrollView(
            VStack(16,
                PageHeader("ScrollView", "A scrollable container for content that exceeds available space. ScrollView is the modern InteractionTracker-backed control; ScrollViewer is the classic Control-shaped one for cases that need parallax animations, attached-property scroll-mode overrides on templated parents, or the IsIntermediate view-changed flag."),

                SampleCard("Vertical ScrollView (modern)",
                    Border(
                        ScrollView(
                            VStack(4,
                                Enumerable.Range(1, (int)itemCount)
                                    .Select(i => Border(TextBlock($"Item {i}").Padding(8))
                                        .Background(i % 2 == 0 ? Theme.SubtleFill : Theme.LayerFill))
                                    .ToArray()
                            )
                        )
                    ).Size(300, 200).WithBorder(Theme.CardStroke).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft),
                    @"ScrollView(\n    VStack(4, items.Select(i =>\n        Border(TextBlock($""Item {i}"")).Padding(8)\n    ).ToArray())\n)",
                    OptionPanel(
                        TextBlock($"Item count: {(int)itemCount}"),
                        Slider(itemCount, 5, 50, setItemCount)
                    )),

                SampleCard("Horizontal ScrollView (modern)",
                    Border(
                        (ScrollView(
                            HStack(4,
                                Enumerable.Range(1, 15)
                                    .Select(i => Border(TextBlock($"  {i}  ").Center().Foreground("#FFFFFF"))
                                        .Background("#5B6ABF").Size(80, 60).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft))
                                    .ToArray()
                            )
                        ) with { ContentOrientation = Microsoft.UI.Xaml.Controls.ScrollingContentOrientation.Horizontal })
                    ).Height(80).Width(350).WithBorder(Theme.CardStroke).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft),
                    @"(ScrollView(HStack(...)) with {\n    ContentOrientation = ScrollingContentOrientation.Horizontal\n})"),

                SampleCard("Classic ScrollViewer (legacy)",
                    Border(
                        (ScrollViewer(
                            VStack(4,
                                Enumerable.Range(1, 10)
                                    .Select(i => Border(TextBlock($"Item {i}").Padding(8))
                                        .Background(Theme.SubtleFill))
                                    .ToArray()
                            )
                        ) with { HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled,
                                 HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled })
                    ).Size(300, 160).WithBorder(Theme.CardStroke).CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft),
                    @"(ScrollViewer(VStack(...)) with {\n    HorizontalScrollMode = ScrollMode.Disabled,\n    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled\n})")
            ).Margin(36, 24, 36, 36)
        );
    }
}
