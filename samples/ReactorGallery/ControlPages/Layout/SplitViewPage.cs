using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class SplitViewPage : Component
{
    public override Element Render()
    {
        var (isOpen, setIsOpen) = UseState(true);
        var (modeIndex, setModeIndex) = UseState(0);
        var modes = new[] { "Overlay", "Inline", "CompactOverlay", "CompactInline" };

        var displayMode = modeIndex switch
        {
            1 => SplitViewDisplayMode.Inline,
            2 => SplitViewDisplayMode.CompactOverlay,
            3 => SplitViewDisplayMode.CompactInline,
            _ => SplitViewDisplayMode.Overlay
        };

        return ScrollViewer(
            VStack(16,
                PageHeader("SplitView", "A container with a collapsible pane and a content area."),

                SampleCard("Basic SplitView",
                    Border(
                        (SplitView(
                            pane: VStack(8,
                                TextBlock("Pane").Bold(),
                                TextBlock("Item 1"),
                                TextBlock("Item 2"),
                                TextBlock("Item 3")
                            ).Padding(12),
                            content: VStack(8,
                                Button(isOpen ? "Close Pane" : "Open Pane", () => setIsOpen(!isOpen)),
                                TextBlock("Main content area").Foreground(Theme.SecondaryText)
                            ).Padding(12)
                        ) with { IsPaneOpen = isOpen })
                        .Set(sv => sv.DisplayMode = displayMode)
                    ).Size(500, 250).WithBorder(Theme.CardStroke).CornerRadius(4),
                    @"SplitView(\n    pane: VStack(8, TextBlock(""Pane""), ...),\n    content: VStack(8, ...)\n) with { IsPaneOpen = isOpen }\n.Set(sv => sv.DisplayMode = SplitViewDisplayMode.Inline)",
                    OptionPanel(
                        TextBlock("Display Mode"),
                        ComboBox(modes, modeIndex, setModeIndex),
                        ToggleSwitch(isOpen, setIsOpen, header: "Pane Open")
                    ))
            ).Margin(36, 24, 36, 36)
        );
    }
}
