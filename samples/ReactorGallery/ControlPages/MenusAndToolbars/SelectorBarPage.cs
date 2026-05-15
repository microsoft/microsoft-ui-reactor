using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.MenusAndToolbars;

class SelectorBarPage : Component
{
    public override Element Render()
    {
        var (selectedIdx, setSelectedIdx) = UseState(0);
        var views = new[] { "Recent", "Shared", "Favorites" };

        return ScrollView(
            VStack(16,
                PageHeader("SelectorBar",
                    "A bar that lets users switch between different views or modes."),

                SampleCard("Basic SelectorBar",
                    VStack(8,
                        SelectorBar(
                            new[]
                            {
                                SelectorBarItem("Recent", icon: "Clock"),
                                SelectorBarItem("Shared", icon: "People"),
                                SelectorBarItem("Favorites", icon: "Favorite"),
                            },
                            selectedIdx,
                            i => setSelectedIdx(i)),
                        TextBlock($"Selected: {views[selectedIdx]}").Foreground(Theme.SecondaryText)
                    ),
                    @"SelectorBar(
    new[] {
        SelectorBarItem(""Recent"", icon: ""Clock""),
        SelectorBarItem(""Shared"", icon: ""People""),
        SelectorBarItem(""Favorites"", icon: ""Favorite""),
    },
    selectedIndex: idx,
    onSelectedIndexChanged: i => setIdx(i))"),

                SampleCard("SelectorBar without Icons",
                    SelectorBar(
                        new[]
                        {
                            SelectorBarItem("Day"),
                            SelectorBarItem("Week"),
                            SelectorBarItem("Month"),
                        },
                        0),
                    @"SelectorBar(
    new[] {
        SelectorBarItem(""Day""),
        SelectorBarItem(""Week""),
        SelectorBarItem(""Month""),
    })")
            ).Margin(36, 24, 36, 36)
        );
    }
}
