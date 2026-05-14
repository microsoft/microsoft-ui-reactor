---
name: reactor-dev
description: "Builds WinUI 3 desktop apps with Microsoft.UI.Reactor — a React-style declarative C# projection (no XAML, no data binding, no ViewModels). The concepts are React's; the spelling is C#. Use for creating new Reactor apps, adding features, converting from XAML/MVVM to Reactor, fixing bugs, or any Microsoft.UI.Reactor / functional WinUI task."
user-invocable: true
---

## Process

> ### ⚠️ ALWAYS start a new app with `dotnet new reactorapp`
>
> Unless the user has explicitly asked you to do something else (e.g. modify an existing project, write a single-file `#:package` script on purpose, or convert from XAML/MVVM into an existing tree), **your very first action on a new Reactor app is:**
>
> ```
> dotnet new reactorapp -n <AppName>
> ```
>
> Run it from the workspace root. It produces a working `.csproj` and `App.cs` already wired against `Microsoft.UI.Reactor` (the `App.cs` carries its own `using` directives — there is no `GlobalUsings.cs`, and you should not add one). **Edit that. Do not hand-write a `.csproj` and `App.cs` from scratch** — that path consistently leads to invented API names (`UseElementFocus`, `AutomationLandmarkType.Complementary`, etc.), wasted `mur check` round-trips, and longer sessions.
>
> If the template isn't installed yet, install it before scaffolding:
>
> ```
> dotnet new install Microsoft.UI.Reactor.ProjectTemplates
> ```

You build Reactor apps in this rhythm: scaffold → understand requirements → draft component tree → write files in a batch → `mur check`.

Before continuing

1. Load the `reactor-getting-started` skill — it carries the hooks table, the most-used factories, the React-to-Reactor mapping, theme tokens, and the critical gotchas.
2. Load the `reactor-design` skill — it has Fluent Design rules for Reactor (theme tokens, typography, 4px grid, High Contrast, accessibility audit).

Then for each task:

1. **Scaffold first** (see the callout above). For a new app: `dotnet new reactorapp -n <AppName>`. Skip this step *only* if the user has told you to write a single-file script, edit an existing project, or otherwise asked for a non-scaffolded shape.
2. **Understand the task.** Note what the app needs to do. Don't guess at requirements.
3. **Draft.** Sketch the component tree, identify state, decide where each piece lives. If you know how you'd build the equivalent in React, you already know the shape — just translate to the C# spelling.
4. **Write the files in a batch.** Add models and child components in one stretch on top of the scaffolded `App.cs`. Don't stop and rebuild after each file — build once at the end.
5. **Run `mur check` and read the output.** `mur check <path>` is the build — it runs `dotnet build` under the hood, returns the same exit code, and adds skill pointers for `REACTOR_*` IDs plus `→ try: <name>` did-you-mean suggestions for unknown identifiers. When it exits 0, you are done — **do not re-run `dotnet build` to confirm**; it's the same compilation. Read errors and analyzer warnings; fix them in another batch; re-run `mur check`. Fall back to `dotnet build` / `dotnet run` only if `mur` isn't on PATH.

Beyond those two, load topical skills only when the task hits them — see the table below. The full signatures index lives in `reactor-dsl/references/reactor.api.txt` if you need a less-common control.

## When to load each skill

| Skill | Trigger |
|---|---|
| `reactor-getting-started` | **Always load up front** — hooks table, factories, React-to-Reactor mapping, theme tokens, critical gotchas. |
| `reactor-design` | **Always load up front** — Fluent Design rules, theme tokens, typography, 4px grid, High Contrast, accessibility audit. |
| `reactor-dsl` | Only when `reactor-getting-started` doesn't list the factory/modifier you need — points to the full api index. |
| `reactor-build-and-check` | `mur check` reports an error you can't fix from the diagnostic alone, you see a `REACTOR_*` analyzer warning you don't recognize, or you need the iteration-vs-`--final` / passthrough-flag details. |
| `reactor-async` | Fetching data, caching, pagination, optimistic writes. `UseResource`, `UseMutation`, `UseInfiniteResource`. |
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
- **Batch your edits.** A single turn that creates five related files is much cheaper than five turns that each create one. Same for fix-ups: read the build output, plan all the fixes, apply them together, then re-run `mur check`.
- **Build at the end, not after every file.** One green `mur check` at the end is the goal, not a green build at every step.
- **`mur check` is the inner loop; trust `→ try:` suggestions verbatim.** A `→ try: <name>` suggestion has already been computed against the live Reactor surface for that exact diagnostic — use the suggested name in your next edit. **Do not grep the codebase, `reactor.api.txt`, or sibling names to second-guess it.** If it's wrong, the next `mur check` will say so and emit a new suggestion — that self-correcting cycle is the cheap loop; manual verification breaks it. Once `mur check` exits 0, you're done — never re-run `dotnet build` to "confirm" the same compilation.
- **Hooks must be called unconditionally.** Same order every render. Conditionally use the *result*, not the call.
- **Don't grep `src/Reactor/`.** The bundled api index is the source of truth for public API. Source-code grep is slower and includes private/internal noise.
- **Don't add features beyond what's asked.** Reactor's DSL composes; resist building elaborate scaffolding for simple tasks.
