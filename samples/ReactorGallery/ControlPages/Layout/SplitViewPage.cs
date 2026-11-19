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
        var (paneLength, setPaneLength) = UseState(200.0);
        var (resizable, setResizable) = UseState(true);
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
                    """
                    SplitView(
                        pane: VStack(8, TextBlock("Pane"), ...),
                        content: VStack(8, ...)
                    ) with { IsPaneOpen = isOpen }
                    .Set(sv => sv.DisplayMode = SplitViewDisplayMode.Inline)
                    """,
                    OptionPanel(
                        TextBlock("Display Mode"),
                        ComboBox(modes, modeIndex, setModeIndex),
                        ToggleSwitch(isOpen, setIsOpen, header: "Pane Open")
                    )),

                SampleCard("Resizable pane",
                    (SplitView(
                        pane: VStack(8, TextBlock("Details").Bold(),
                            TextBlock("Drag the divider, or focus it and use Left/Right/Home/End.")
                                .TextWrapping(TextWrapping.Wrap)).Padding(12),
                        content: TextBlock($"Pane width: {paneLength:0}").Margin(16)) with
                        { DisplayMode = SplitViewDisplayMode.Inline, OpenPaneLength = paneLength })
                        .ResizablePane(120, 360, setPaneLength, enabled: resizable)
                        .Height(240),
                    """
                    (SplitView(pane: TextBlock("Details"),
                        content: TextBlock($"Pane width: {paneLength:0}")) with
                        { DisplayMode = SplitViewDisplayMode.Inline, OpenPaneLength = paneLength })
                        .ResizablePane(120, 360, setPaneLength, enabled: resizable)
                    """,
                    OptionPanel(ToggleSwitch(resizable, setResizable, header: "Allow resizing")))
            ).Margin(36, 24, 36, 36)
        );
    }
}
