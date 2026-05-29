// id: list-with-loading
// intent: list with loading indicator during async fetch
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Loading List", width: 500, height: 400);

record Order(string Id, string Label);

class App : Component
{
    public override Element Render()
    {
        var (loading, setLoading) = UseState(true, threadSafe: true);
        var (items, setItems) = UseState<IReadOnlyList<Order>>(Array.Empty<Order>(), threadSafe: true);

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(900);
                setItems(new[] { new Order("101", "Order #101"), new Order("102", "Order #102"), new Order("103", "Order #103") });
                setLoading(false);
            });
        }, Array.Empty<object>());

        return VStack(12,
            Heading("Recent orders"),
            loading
                ? HStack(8, ProgressRing().IsActive(true).Width(20).Height(20), TextBlock("Loading items…"))
                : VStack(8,
                    ForEach(items, item =>
                        TextBlock(item.Label)
                            .Padding(8)
                            .WithKey(item.Id))))
            .Padding(16);
    }
}
