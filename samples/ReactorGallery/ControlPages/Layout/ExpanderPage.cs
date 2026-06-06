using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class ExpanderPage : Component
{
    public override Element Render()
    {
        var (expanded1, setExpanded1) = UseState(true);
        var (expanded2, setExpanded2) = UseState(false);
        var (dirIndex, setDirIndex) = UseState(0);

        var direction = dirIndex == 0 ? ExpandDirection.Down : ExpandDirection.Up;

        return ScrollView(
            VStack(16,
                PageHeader("Expander", "Expands and collapses to show or hide content."),

                SampleCard("Basic Expander",
                    VStack(8,
                        Expander("Settings", VStack(4,
                            TextBlock("Option 1: Enabled"),
                            TextBlock("Option 2: Auto-save"),
                            TextBlock("Option 3: Dark mode")
                        ), expanded1, setExpanded1).Width(350),

                        Expander("Advanced", VStack(4,
                            TextBlock("Cache size: 256 MB"),
                            TextBlock("Thread count: 4"),
                            TextBlock("Log level: Debug")
                        ), expanded2, setExpanded2).Width(350)
                    ),
                    """
                    Expander("Settings", VStack(
                        TextBlock("Option 1: Enabled"), ...
                    ), expanded, setExpanded)
                    """),

                SampleCard("Direction Control",
                    Expander("Expand Direction", VStack(4,
                        TextBlock("This content appears based on the direction setting."),
                        TextBlock("Try switching between Down and Up.")
                    ), true).Direction(direction).Width(350),
                    """
                    Expander("Header", content, true)
                        .Direction(ExpandDirection.Up)
                    """,
                    OptionPanel(
                        TextBlock("Direction"),
                        ComboBox(new[] { "Down", "Up" }, dirIndex, setDirIndex)
                    ))
            ).Margin(36, 24, 36, 36)
        );
    }
}
