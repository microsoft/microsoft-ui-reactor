---
name: reactor-recipes
description: "Intent-first map into the Reactor scenario catalogue — list add/delete/toggle, sidebar navigation, validation and submit gating forms, async fetch states, card surfaces, and canvas positioning. Prefer the scenario source under samples/scenarios/ over legacy recipe files."
---

## How to use this skill

The primary source is now the **scenario catalogue** under `samples/scenarios/`. Each scenario folder contains a compilable single-file app in `Scenario.cs`; copy the matching scenario and adapt it instead of synthesizing from prose.

The old `references/` folder no longer contains the legacy recipe `.cs` files. It only keeps markdown docs such as `references/index.md` and `references/animated-list.md`.

## Intent → scenario map

| Intent | Scenario source | APIs used |
|---|---|---|
| Add / remove / toggle items in a list | `samples/scenarios/lists/list-add-delete-toggle/Scenario.cs` | `UseReducer`, `WithKey`, `Command` |
| Animate list insert / move / remove | `samples/apps/animated-list-demo/` | `Animations.Animate`, `AnimationKind`, `IReactorKeyed`, `UseReducedMotion` |
| Sidebar navigation between pages | `samples/scenarios/navigation/sidebar-nav/Scenario.cs` | `UseNavigation`, `NavigationView`, `NavigationHost`, `WithNavigation` |
| Form with validation + submit gating | `samples/scenarios/forms/form-validation-context/Scenario.cs` and `samples/scenarios/forms/form-submit-gating/Scenario.cs` | `UseValidationContext`, `FormField`, `Validate.*`, `ShowWhen` |
| Fetch data with loading / error / data states | `samples/scenarios/hooks/async-fetch-list/Scenario.cs` | `UseResource`, `AsyncValue<T>.Match` |
| Themed Win11 card surface | `samples/scenarios/layout/card-surface/Scenario.cs` | `Theme.*` tokens, `Border`, `FlexColumn`, `.Padding`, `.CornerRadius`, `.WithBorder` |
| Absolute positioning with Canvas | `samples/scenarios/layout/canvas-positioning/Scenario.cs` | `Canvas`, `.Canvas(left, top)`, `.CenterAt`, shapes |

See `samples/scenarios/README.md` for the catalogue contract and `samples/scenarios/_generated/scenarios.json` for the generated index.

## Scenario contract

A good scenario:
- Compiles standalone (`dotnet run` works against the file).
- Targets one intent — no kitchen-sink demos.
- Stays concise, with real Reactor APIs only.
- Comments only the *non-obvious*.

## Adapting a scenario

Scenarios use `#:package Microsoft.UI.Reactor@0.0.0-local` (selfhost default). Replace the version with whatever you depend on outside the source clone.

If you need analyzer coverage (`REACTOR_DSL_001` and friends), promote the scenario to a `.csproj` — single-file `.cs` builds don't load analyzers. See `reactor-getting-started` for the `.csproj` template.
