
# Recipes

A recipe is a small composition of Microsoft.UI.Reactor (Reactor) primitives that solves a
real UI problem — login, master-detail, settings, modal confirmation,
search-with-suggestions. The recipes here are not exhaustive apps;
each is a single screen showing the pattern, and each ships a tiny
doc app you can clone and adapt.

```csharp
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
```

![Recipes gallery preview](images/recipes-index/gallery.png)

The gallery uses the same primitives every recipe page does — a
`VStack` for the column layout, `TextBlock` for descriptions, `HStack`
for the tile row. The tile helper is a private static method, not a
component, so it has no hook scope:

```csharp
private static Element Tile(string title, string sub) => VStack(4,
    TextBlock(title).Bold(),
    TextBlock(sub).Opacity(0.6)
).Padding(12);
```

Every page in this folder follows the same shape:

```csharp
// Every recipe page in this folder pulls a tiny dedicated doc app under
// docs/_pipeline/apps/recipe-<name>/. The recipe template renders three
// snippet markers (state / shape / render) plus one screenshot for the
// gallery thumbnail above.
class GalleryShape : Component
{
    public override Element Render() => TextBlock("see docs/_pipeline/apps/recipe-*");
}
```

## Gallery

| Recipe | What it shows |
|---|---|
| [Login](login.md) | Validation, async submit with optimistic UI, error display. |
| [Master-detail](master-detail.md) | Two-pane selection-driven layout from a list and a record. |
| [Settings page](settings-page.md) | Per-key `UsePersisted` for `Toggle` / `ComboBox` / `Slider`. |
| [Modal dialog](modal-dialog.md) | Confirmation pattern with scrim and conditional render. |
| [Search with suggestions](search-with-suggestions.md) | `UseMemo`-debounced suggestion list against a static catalog. |
| Paginated list *(Phase 2.5)* | Coming soon — page cursor + lazy-load. |
| Multi-step form *(Phase 2.5)* | Coming soon — wizard with per-step validation. |
| Command palette *(Phase 2.5)* | Coming soon — Ctrl+K palette wired through `commanding`. |
| Drag-reorder *(Phase 2.5)* | Coming soon — drag-handle list with optimistic reorder. |

The "Phase 2.5" rows are stubs that ship in this docset so the gallery
shape is visible and discoverable; the full recipes land in a follow-up.

## How to read a recipe

Every recipe has the same shape:

1. **A snippet of the recipe's working code**, pulled from a real
   doc app under `docs/_pipeline/apps/recipe-<name>/`.
2. **A screenshot of the recipe running**, captured by the
   doc-pipeline harness.
3. **A walkthrough paragraph or two** naming the primitives the
   recipe combines and the design decisions that hold the pattern up.

The recipes prefer composition of existing factories — no custom
`Component` per recipe. If you want the recipe in your app, copy the
snippet and replace the catalog data with yours.

## Reference

| Primitive | Used in |
|---|---|
| `UseState` | Every recipe. |
| `UsePersisted` | [Settings](settings-page.md). |
| `UseMemo` | [Search](search-with-suggestions.md). |
| `UseEffect` + `Task` | [Login](login.md) (async submit). |
| Conditional render | [Modal dialog](modal-dialog.md). |
| Two-pane HStack | [Master-detail](master-detail.md). |

## Tips

**Reach for a recipe before custom code.** Most "I need a settings
page" or "we need a login form" needs are met by one of these
patterns. The recipe is the composition; the cost of inventing your
own is the cost of debugging it.

**Recipes are starting points, not products.** Drop the snippet into
your app and adapt it — the data, the styling tokens, the validation
rules. The shape of the composition is the value here.

**Search the controls catalog before reaching for a recipe.** A
problem solved by a single control ([forms](../forms.md),
[data-system](../data-system.md)) doesn't need a recipe; recipes
exist for shapes that span multiple controls and hooks.

## Next Steps

- **[Controls](../controls.md)** — Previous: the catalog of factories
  the recipes compose.
- **[Forms](../forms.md)** — Forms-heavy recipes start here.
- **[Persistence](../persistence.md)** — Behind the Settings recipe.
- **[Commanding](../commanding.md)** — Backs the (Phase 2.5) Command-palette recipe.
- **[Navigation](../navigation.md)** — Recipes that span multiple
  screens lean on this.
