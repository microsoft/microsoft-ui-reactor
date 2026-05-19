using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<RecipesIndexApp>("Recipes Gallery", width: 520, height: 320
#if DEBUG
    , preview: true
#endif
);

// <snippet:app>
class RecipesIndexApp : Component
{
    public override Element Render() => VStack(12,
        Heading("Recipes"),
        TextBlock("Real-world compositions made of Reactor primitives.")
            .Opacity(0.7),
        HStack(8,
            Tile("Login", "Validation + async submit"),
            Tile("Master-detail", "Selection-driven layout"),
            Tile("Settings", "Persisted preferences")
        )
    ).Padding(20);
// </snippet:app>

    // <snippet:tile>
    private static Element Tile(string title, string sub) => VStack(4,
        TextBlock(title).Bold(),
        TextBlock(sub).Opacity(0.6)
    ).Padding(12);
    // </snippet:tile>
}

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
