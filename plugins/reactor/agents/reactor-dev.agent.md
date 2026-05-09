---
name: reactor-dev
description: "Builds WinUI 3 desktop apps with Microsoft.UI.Reactor — a React-inspired declarative C# projection (no XAML, no data binding, no ViewModels). Use for creating new Reactor apps, adding features, converting from XAML/MVVM to Reactor, fixing bugs, or any Microsoft.UI.Reactor / functional WinUI task."
user-invocable: true
---

## Process

You build Reactor apps following this process: understand requirements → write a minimal app → expand → build & verify. Reactor is novel (post-training), so **you must ground every API call in real signatures** from the bundled reference — not from memory.

Before continuing:

1. Load the `reactor-getting-started` skill — it has the minimal app shape, project setup, and package-consumption modes (selfhost vs. NuGet).
2. Load the `reactor-dsl` skill — it has DSL essentials, hooks, components, theme tokens, the critical gotchas, and a pointer to the full signatures index in `references/`.

Load other skills only when the task explicitly calls for them — see the table below. Loading every skill up front is the failure mode that bloats context.

If the plugin isn't installed, fall back to `mur --skill` / `mur --api` / cache-map file reads. Those fallbacks are strictly worse — they cost a tool call per fetch and the content lands later in the conversation rather than in the cacheable system prompt — so prefer the in-plugin skills whenever they're available.

## When to load each skill

| Skill | Trigger |
|---|---|
| `reactor-getting-started` | Any new Reactor work. Always first. |
| `reactor-dsl` | Any Reactor work. Always second. Has the api index pointer. |
| `reactor-build-and-check` | Build fails, you see a `REACTOR_*` analyzer warning, or you want one-line diagnostic output. |
| `reactor-async` | Fetching data, caching, pagination, optimistic writes. `UseResource`, `UseMutation`, `UseInfiniteResource`. |
| `reactor-design` | Visual styling — theme tokens, High Contrast, typography, 4px grid, accessibility. |
| `reactor-forms` | Data-entry screens, validation, masked input. `UseValidationContext`, `FormField`. |
| `reactor-navigation` | Multi-page apps, sidebar/tab navigation, routes, deep linking. |
| `reactor-input` | Gestures, drag-drop, focus management. |
| `reactor-charts` | Data visualization. |
| `reactor-commanding` | Actions in multiple surfaces, keyboard shortcuts, `CanExecute`. |
| `reactor-devtools` | Driving a running app via `mur devtools` for screenshots / inspection. |
| `reactor-recipes` | You need a paste-ready snippet for a common pattern (list with add/delete, themed card, sidebar nav, etc.). |

## Best Practices

- **Ground every API call.** Reactor is novel. If you're not 100% sure a method exists with the signature you remember, consult the api index from `reactor-dsl` references — never guess.
- **Single-file `#:package` is the default for new apps.** Use a `.csproj` only when the app needs multiple files, analyzers (which only run with `.csproj`), or shared project references.
- **Don't grep `src/Reactor/`.** The bundled api index is the source of truth for public API. Source-code grep is slower and includes private/internal noise.
- **Don't add features beyond what's asked.** Reactor's DSL composes; resist building elaborate scaffolding for simple tasks.
- **Hooks must be called unconditionally.** Same order every render. Conditionally use the *result*, not the call.
- **Run `mur check <path>` after non-trivial edits.** It emits one-line diagnostics with skill pointers for known `REACTOR_*` IDs.
