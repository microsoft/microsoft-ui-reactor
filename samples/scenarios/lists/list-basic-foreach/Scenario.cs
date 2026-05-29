// id: list-basic-foreach
// intent: render a static list using ForEach
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Basic ForEach", width: 500, height: 400);

record GroceryItem(string Id, string Name, string Aisle);

class App : Component
{
    public override Element Render()
    {
        var items = new[]
        {
            new GroceryItem("fruit", "Apples", "Produce"),
            new GroceryItem("bread", "Sourdough", "Bakery"),
            new GroceryItem("milk", "Whole Milk", "Dairy"),
        };

        return VStack(12,
            Heading("Static shopping list"),
            TextBlock("ForEach maps fixed data into UI rows."),
            VStack(8,
                ForEach(items, item =>
                    HStack(12,
                        TextBlock(item.Name).Bold().Width(160),
                        TextBlock(item.Aisle).Opacity(0.7))
                    .Padding(8)
                    .WithKey(item.Id))))
            .Padding(16);
    }
}
