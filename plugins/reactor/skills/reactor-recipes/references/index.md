---
name: reactor-recipes
description: >
  Paste-ready Reactor snippets indexed by intent. Each recipe is a complete,
  compilable single-file program — copy, adjust, ship. Use these instead of
  re-deriving patterns from the narrative skill files.
---

# Reactor Recipes

Each recipe is a self-contained `.cs` file that compiles via `dotnet run`. The
recipes here use `#:package Microsoft.UI.Reactor@0.0.0-local` so they resolve
through this clone's `local-nupkgs/` feed (run `mur pack-local` once to
populate it). **For a NuGet consumer**, replace `0.0.0-local` with the
released version you depend on. Drop the body into any `Program.cs` or class
component.

| Intent | Recipe | Hooks / APIs used |
|---|---|---|
| Add / remove items in a list | [`list-add-delete.cs`](list-add-delete.cs) | `UseReducer`, `WithKey`, `Command` |
| Sidebar navigation between pages | [`sidebar-nav.cs`](sidebar-nav.cs) | `UseNavigation`, `NavigationView`, `NavigationHost` |
| Form with validation + submit | [`form-with-validation.cs`](form-with-validation.cs) | `UseValidationContext`, `FormField`, `Validate.*` |
| Fetch data, show loading / error | [`async-fetch-list.cs`](async-fetch-list.cs) | `UseResource`, `AsyncValue<T>.Match` |
| Themed card with 4px grid spacing | [`themed-card.cs`](themed-card.cs) | `Theme.*`, `Border`, `FlexColumn`, `.Padding`, `.CornerRadius` |

## How to use a recipe

1. Find the intent above and open the recipe file.
2. The file is a complete, compilable program — read the whole thing, it's short.
3. Copy the relevant pieces into your code. Don't grep `src/Reactor/**` to verify
   signatures; load `skills/reactor.api.txt` if you need to confirm one.

## Adding a new recipe

A good recipe:
- Compiles standalone (`dotnet run` works against the file).
- Targets one intent — don't combine "form + nav + async" into a kitchen sink.
- Stays under ~120 lines including imports and the `ReactorApp.Run` shell.
- Uses real Reactor APIs only (no pseudocode, no placeholders).
- Comments only the *non-obvious* — recipes are read for the code, not the prose.
