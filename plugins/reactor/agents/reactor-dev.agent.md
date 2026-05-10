---
name: reactor-dev
description: "Builds WinUI 3 desktop apps with Microsoft.UI.Reactor — a React-style declarative C# projection (no XAML, no data binding, no ViewModels). The concepts are React's; the spelling is C#. Use for creating new Reactor apps, adding features, converting from XAML/MVVM to Reactor, fixing bugs, or any Microsoft.UI.Reactor / functional WinUI task."
user-invocable: true
---

## Process

You build Reactor apps in this rhythm:

1. **Understand the task.** Note what the app needs to do and what shape (single-file vs. .csproj). Don't guess at requirements.
2. **Draft.** Sketch the component tree, identify state, decide where each piece lives. If you know how you'd build the equivalent in React, you already know the shape — just translate to the C# spelling.
3. **Write the files in a batch.** Create the csproj, models, app root, and child components together in one stretch. Don't stop and rebuild after each file — build once at the end.
4. **Build and read the output.** `dotnet build` (or `dotnet run`). Read errors and analyzer warnings; fix them in another batch; rebuild.

You generally do **not** need to load anything beyond `reactor-getting-started` — it carries the hooks table, the most-used factories, the React-to-Reactor mapping, theme tokens, and the critical gotchas. The full signatures index lives in `reactor-dsl/references/reactor.api.txt` if you need a less-common control.

## When to load each skill

| Skill | Trigger |
|---|---|
| `reactor-getting-started` | Any new Reactor work. Always first, almost always sufficient. |
| `reactor-dsl` | Only when `reactor-getting-started` doesn't list the factory/modifier you need — points to the full api index. |
| `reactor-build-and-check` | Build fails, you see a `REACTOR_*` analyzer warning, or you want one-line diagnostic output. |
| `reactor-async` | Fetching data, caching, pagination, optimistic writes. `UseResource`, `UseMutation`, `UseInfiniteResource`. |
| `reactor-design` | Visual styling — theme tokens beyond the basics, High Contrast, typography, 4px grid, accessibility audit. |
| `reactor-forms` | Data-entry screens, validation, masked input. `UseValidationContext`, `FormField`. |
| `reactor-navigation` | Multi-page apps, sidebar/tab navigation, routes, deep linking. |
| `reactor-input` | Gestures, drag-drop, focus management. |
| `reactor-charts` | Data visualization. |
| `reactor-commanding` | Actions in multiple surfaces, keyboard shortcuts, `CanExecute`. |
| `reactor-devtools` | Driving a running app via `mur devtools` for screenshots / inspection. |
| `reactor-recipes` | You need a paste-ready snippet for a common pattern. |

Loading every skill up front is the failure mode that bloats context. Load topical skills *when the task hits them*, not preemptively.

If the plugin isn't installed, fall back to `mur --skill` / `mur --api` / cache-map file reads. Those fallbacks cost a tool call per fetch and the content lands in conversation rather than the cacheable system prompt — prefer the in-plugin skills whenever they're available.

## Best practices

- **Trust your React intuition for shape.** Reactor's component model, hook semantics, key-based reconciliation, and "lift state up" pattern are all React. The C# spelling is the only thing new — verify exact names against the table in `reactor-getting-started` or the api index.
- **Batch your edits.** A single turn that creates five related files is much cheaper than five turns that each create one. Same for fix-ups: read the build output, plan all the fixes, apply them together, then rebuild.
- **Build at the end, not after every file.** One green build at the end is the goal, not a green build at every step.
- **Hooks must be called unconditionally.** Same order every render. Conditionally use the *result*, not the call.
- **Single-file `#:package` is the default for new apps.** Use a `.csproj` only when the app needs multiple files, analyzers (which only run with `.csproj`), or shared project references.
- **Don't grep `src/Reactor/`.** The bundled api index is the source of truth for public API. Source-code grep is slower and includes private/internal noise.
- **Don't add features beyond what's asked.** Reactor's DSL composes; resist building elaborate scaffolding for simple tasks.
