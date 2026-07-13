using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Collections;

class RefreshContainerPage : Component
{
    public override Element Render()
    {
        IReadOnlyList<string> initialItems = Enumerable.Range(1, 8)
            .Select(i => $"Feed item {i}")
            .ToList();
        var (items, setItems) = UseState(initialItems);

        return ScrollView(VStack(16,
            PageHeader("RefreshContainer", "Wraps scrollable content with a pull-to-refresh gesture."),
            SampleCard("Pull to refresh list",
                VStack(8,
                    RefreshContainer(
                        ListView(items.Select(i => (Element)TextBlock(i).Padding(8)).ToArray())
                            .Height(240),
                        () => setItems(new[] { $"Refreshed {DateTime.Now:T}" }
                            .Concat(items)
                            .ToList())),
                    Caption("Pull down from the top of the list to request a refresh.")
                ),
                sourceCode: @"var (items, setItems) = UseState(initialItems);

RefreshContainer(
    ListView(items.Select(i => (Element)TextBlock(i).Padding(8)).ToArray())
        .Height(240),
    () => setItems(new[] { $""Refreshed {DateTime.Now:T}"" }
        .Concat(items)
        .ToList()))"),
            SampleCard("Static refresh surface",
                VStack(8,
                    RefreshContainer(
                        ListView(
                            TextBlock("Messages").Padding(8),
                            TextBlock("Tasks").Padding(8),
                            TextBlock("Notifications").Padding(8))
                            .Height(160),
                        () => { }),
                    Caption("The refresh action can be connected to any app-specific data reload.")
                ),
                sourceCode: @"RefreshContainer(
    ListView(
        TextBlock(""Messages"").Padding(8),
        TextBlock(""Tasks"").Padding(8),
        TextBlock(""Notifications"").Padding(8))
        .Height(160),
    () => { })")
        ).Margin(36, 24, 36, 36));
    }
}
