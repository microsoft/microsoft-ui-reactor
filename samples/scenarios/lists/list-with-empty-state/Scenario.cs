// id: list-with-empty-state
// intent: list with empty-state placeholder when no items exist
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Empty State", width: 500, height: 400);

record Favorite(string Id, string Label);

class App : Component
{
    public override Element Render()
    {
        var (showItems, setShowItems) = UseState(false);
        var items = showItems
            ? new[] { new Favorite("one", "Design notes"), new Favorite("two", "Release checklist") }
            : Array.Empty<Favorite>();

        return VStack(12,
            Heading("Favorites"),
            Button(showItems ? "Clear items" : "Load sample items", () => setShowItems(!showItems)),
            items.Length == 0
                ? Border(TextBlock("No items yet").Opacity(0.7)).Padding(16)
                : VStack(8,
                    ForEach(items, item =>
                        TextBlock(item.Label)
                            .Padding(8)
                            .WithKey(item.Id))))
            .Padding(16);
    }
}
