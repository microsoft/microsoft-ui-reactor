using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class ParallaxViewPage : Component
{
    public override Element Render()
    {
        // ParallaxView does nothing until its Source is bound to a scroller. We
        // capture the foreground ListView on mount, then feed it back in.
        var (scroller, setScroller) = UseState<UIElement?>(null);

        var background =
            Border(TextBlock("Parallax background").FontSize(28).SemiBold().Foreground("#FFFFFF").Center())
                .Background(Theme.Accent);

        var parallax = ParallaxView(background, verticalShift: 100);
        if (scroller is not null)
            parallax = parallax.Source(scroller);

        // Transparent list so the parallaxing background shows through behind the rows.
        var list = ListView(
            Enumerable.Range(1, 24)
                .Select(i => (Element)TextBlock($"Row {i}").Foreground("#FFFFFF").FontSize(16).Padding(12, 8, 12, 8))
                .ToArray())
            .Set(lv => lv.Background = null)
            .OnMount(el => setScroller((UIElement)el));

        return ScrollView(VStack(16,
            PageHeader("ParallaxView", "Shifts a background layer as a foreground surface scrolls, creating a depth effect."),

            SampleCard("Background parallax behind a scrolling list",
                VStack(8,
                    Grid(
                        columns: [GridSize.Star()], rows: [GridSize.Star()],
                        parallax.Grid(row: 0, column: 0),
                        list.Grid(row: 0, column: 0)
                    ).Height(300),
                    Caption("Scroll the list — the background layer drifts behind it (ParallaxView.Source is bound to the list).")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (scroller, setScroller) = UseState<UIElement?>(null);

var parallax = ParallaxView(background, verticalShift: 100);
if (scroller is not null) parallax = parallax.Source(scroller);

Grid(columns: [GridSize.Star()], rows: [GridSize.Star()],
    parallax.Grid(row: 0, column: 0),
    ListView(rows)
        .Set(lv => lv.Background = null)          // transparent → background shows through
        .OnMount(el => setScroller((UIElement)el)) // bind the scroller
        .Grid(row: 0, column: 0))
")
        ).Margin(36, 24, 36, 36));
    }
}
