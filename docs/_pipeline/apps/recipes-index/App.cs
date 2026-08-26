using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<RecipesIndexApp>("Recipes Gallery", width: 520, height: 320
);

// <snippet:app>
class RecipesIndexApp : Component
{
    public override Element Render() => VStack(12,
        Heading("Recipes"),
        TextBlock("Real-world compositions made of Reactor primitives.")
            .Opacity(0.7),
        VStack(8,
            HStack(8,
                Tile("Login", "Validation + async submit"),
                Tile("Master-detail", "Selection-driven layout"),
                Tile("Settings", "Persisted preferences")),
            HStack(8,
                Tile("Paginated list", "Loading + empty + error states"),
                Tile("Modal dialog", "Scrim + confirmation flow"),
                Tile("Multi-step form", "Wizard validation")),
            HStack(8,
                Tile("Search", "Memoized suggestions"),
                Tile("Command palette", "Keyboard-opened overlay"),
                Tile("Drag-reorder", "Keyed list reordering"))
        )
    ).Padding(20);

    // <snippet:tile>
    private static Element Tile(string title, string sub) => VStack(4,
        TextBlock(title).Bold(),
        TextBlock(sub).Opacity(0.6)
    ).Padding(12);
    // </snippet:tile>
}
// </snippet:app>

// <snippet:rendering-shape>
// Every recipe page in this folder pulls a tiny dedicated doc app under
// docs/_pipeline/apps/recipe-<name>/. The recipe template renders three
// snippet markers (state / shape / render) plus one screenshot for the
// gallery thumbnail above.
class GalleryShape : Component
{
    public override Element Render() => TextBlock("see docs/_pipeline/apps/recipe-*");
}
// </snippet:rendering-shape>
