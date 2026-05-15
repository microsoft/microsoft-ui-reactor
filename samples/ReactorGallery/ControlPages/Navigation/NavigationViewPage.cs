using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Navigation;

class NavigationViewPage : Component
{
    public override Element Render()
    {
        var (selectedTag, setSelectedTag) = UseState("page1");
        var (paneMode, setPaneMode) = UseState(0);

        var items = new[]
        {
            NavItem("Home", icon: "Home", tag: "page1"),
            NavItem("Browse", icon: "Library", tag: "page2"),
            NavItem("Settings", icon: "Setting", tag: "page3"),
        };

        var modes = new[] {
            NavigationViewPaneDisplayMode.Auto,
            NavigationViewPaneDisplayMode.Left,
            NavigationViewPaneDisplayMode.LeftCompact,
            NavigationViewPaneDisplayMode.Top,
        };
        var modeNames = new[] { "Auto", "Left", "LeftCompact", "Top" };

        return ScrollView(
            VStack(16,
                PageHeader("NavigationView",
                    "A side or top navigation pane for app-level navigation."),

                SampleCard("Left-Pane NavigationView",
                    (NavigationView(items,
                        content: TextBlock($"Selected: {selectedTag}")
                            .Foreground(Theme.PrimaryText).Padding(16))
                    with
                    {
                        SelectedTag = selectedTag,
                        OnSelectedTagChanged = tag => { if (tag != null) setSelectedTag(tag); },
                        PaneTitle = "Nav Demo",
                        IsSettingsVisible = false,
                    }).Height(300),
                    @"NavigationView(items, content: TextBlock(""Selected: ...""))
with {
    SelectedTag = tag,
    OnSelectedTagChanged = t => setTag(t),
    PaneTitle = ""Nav Demo"",
}",
                    options: OptionPanel(
                        ComboBox(modeNames, paneMode, i => setPaneMode(i))
                    )),

                SampleCard("Top-Mode NavigationView",
                    (NavigationView(items,
                        content: TextBlock($"Content area for: {selectedTag}")
                            .Foreground(Theme.PrimaryText).Padding(16))
                    with
                    {
                        SelectedTag = selectedTag,
                        OnSelectedTagChanged = tag => { if (tag != null) setSelectedTag(tag); },
                        PaneDisplayMode = NavigationViewPaneDisplayMode.Top,
                        IsSettingsVisible = false,
                    }).Height(200),
                    @"NavigationView(items, content) with {
    PaneDisplayMode = NavigationViewPaneDisplayMode.Top
}")
            ).Margin(36, 24, 36, 36)
        );
    }
}
