using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class GridViewPage : Component
{
    public override Element Render()
    {
        var (mode, setMode) = UseState(0);
        var modes = new[] { "Single", "Multiple", "None" };
        var items = Enumerable.Range(1, 12).ToArray();

        var selectionMode = mode switch
        {
            1 => ListViewSelectionMode.Multiple,
            2 => ListViewSelectionMode.None,
            _ => ListViewSelectionMode.Single
        };

        return ScrollView(
            VStack(16,
                PageHeader("GridView", "Displays items in a horizontally wrapping grid layout."),

                SampleCard("Basic GridView",
                    GridView(
                        items.Select(i =>
                            Border(TextBlock($"Item {i}").Center())
                                .Background(Theme.SubtleFill)
                                .Size(100, 100)
                                .CornerRadius(ThemeResource.CornerRadius("ControlCornerRadius").TopLeft)                        ).ToArray()
                    ).SelectionMode(selectionMode),
                    """
                    GridView(
                        items.Select(i =>
                            Border(TextBlock($"Item {i}")).Size(100, 100)
                        ).ToArray()
                    ).SelectionMode(ListViewSelectionMode.Multiple)
                    """,
                    OptionPanel(
                        TextBlock("Selection Mode"),
                        ComboBox(modes, mode, setMode)
                    )),

                SampleCard("Data-Driven GridView",
                    GridView(
                        items.Select(i => i.ToString()).ToArray().AsReadOnly(),
                        s => s,
                        (s, i) => Border(TextBlock(s).Center().Bold())
                            .Background(i % 2 == 0 ? "#335588" : "#885533")
                            .Foreground("#FFFFFF")
                            .Size(80, 80).CornerRadius(ThemeResource.CornerRadius("OverlayCornerRadius").TopLeft)
                    ),
                    """
                    GridView(
                        items, s => s,
                        (s, i) => Border(TextBlock(s)).Size(80, 80)
                    )
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
