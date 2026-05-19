using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Navigation;

class TabViewPage : Component
{
    public override Element Render()
    {
        var (selectedIdx, setSelectedIdx) = UseState(0);
        var (tabCount, setTabCount) = UseState(3);

        var tabs = Enumerable.Range(1, tabCount)
            .Select(i => Tab($"Tab {i}", TextBlock($"Content of Tab {i}").Padding(16)))
            .ToArray();

        return ScrollView(
            VStack(16,
                PageHeader("TabView",
                    "A control that displays a set of closable, rearrangeable tabs."),

                SampleCard("Basic TabView",
                    (TabView(
                        Tab("Home", TextBlock("Home content").Padding(16)),
                        Tab("Document", TextBlock("Document content").Padding(16)),
                        Tab("Settings", TextBlock("Settings content").Padding(16))
                    ) with
                    {
                        SelectedIndex = selectedIdx,
                        OnSelectedIndexChanged = i => setSelectedIdx(i),
                    }).Height(200),
                    @"TabView(
    Tab(""Home"", TextBlock(""Home content"")),
    Tab(""Document"", TextBlock(""Document content"")),
    Tab(""Settings"", TextBlock(""Settings content""))
) with { SelectedIndex = idx, OnSelectedIndexChanged = i => setIdx(i) }"),

                SampleCard("Dynamic Tabs",
                    VStack(8,
                        (TabView(tabs) with
                        {
                            SelectedIndex = Math.Min(selectedIdx, tabCount - 1),
                            OnSelectedIndexChanged = i => setSelectedIdx(i),
                        }).Height(180),
                        HStack(8,
                            Button("Add Tab", () => setTabCount(tabCount + 1)),
                            Button("Remove Tab", () => { if (tabCount > 1) setTabCount(tabCount - 1); })
                        )
                    ),
                    @"var tabs = Enumerable.Range(1, count)
    .Select(i => Tab($""Tab {i}"", TextBlock($""Content {i}"")))
    .ToArray();
TabView(tabs) with { SelectedIndex = idx }")
            ).Margin(36, 24, 36, 36)
        );
    }
}
