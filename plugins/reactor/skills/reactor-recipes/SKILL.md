---
name: reactor-recipes
description: "Paste-ready single-file Reactor recipes indexed by intent — list with add/delete/toggle, sidebar navigation between pages, form with validation + submit gating, async fetch with loading/error/data states, themed Win11 card surface. Use a recipe instead of synthesizing from prose: copy, adjust, ship."
---

## How to use this skill

Each recipe is a complete, compilable single-file program. **Copy the file's body directly into your project**; don't synthesize the pattern from scratch. The recipes use real Reactor APIs only — no pseudocode.

The full recipe content is in `references/<name>.cs`. Read just the recipe(s) that match your intent.

## Intent → recipe map

| Intent | Recipe | APIs used |
|---|---|---|
| Add / remove / toggle items in a list | `references/list-add-delete.cs` | `UseReducer`, `WithKey`, `Command` |
| Animate list insert / move / remove | `references/animated-list.md` | `Animations.Animate`, `AnimationKind`, `IReactorKeyed`, `UseReducedMotion` |
| Sidebar navigation between pages | `references/sidebar-nav.cs` | `UseNavigation`, `NavigationView`, `NavigationHost`, `WithNavigation` |
| Form with validation + submit gating | `references/form-with-validation.cs` | `UseValidationContext`, `FormField`, `Validate.*`, `ShowWhen` |
| Fetch data with loading / error / data states | `references/async-fetch-list.cs` | `UseResource`, `AsyncValue<T>.Match` |
| Themed Win11 card surface | `references/themed-card.cs` | `Theme.*` tokens, `Border`, `FlexColumn`, `.Padding`, `.CornerRadius`, `.WithBorder` |
| Absolute positioning with Canvas | `references/canvas-positioning.cs` | `Canvas`, `.Canvas(left, top)`, `.CenterAt`, shapes |

See `references/index.md` for the full index.

## Recipe contract

A good recipe:
- Compiles standalone (`dotnet run` works against the file).
- Targets one intent — no kitchen-sink demos.
- Stays under ~120 lines including imports and `ReactorApp.Run` shell.
- Uses real Reactor APIs only.
- Comments only the *non-obvious*.

## Adapting a recipe

The recipes use `#:package Microsoft.UI.Reactor@0.0.0-local` (selfhost default). Replace the version with whatever you depend on outside the source clone.

If you need analyzer coverage (`REACTOR_DSL_001` and friends), promote the recipe to a `.csproj` — single-file `.cs` builds don't load analyzers. See `reactor-getting-started` for the `.csproj` template.
