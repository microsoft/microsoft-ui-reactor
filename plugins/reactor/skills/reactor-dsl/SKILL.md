---
name: reactor-dsl
description: "Pointer to the full Reactor API signatures index (`references/reactor.api.txt`). The 90% of DSL content — hooks, components, common factories, modifiers, theme tokens, gotchas, React-to-Reactor mapping — already lives in `reactor-getting-started`. Load this skill only when you need to verify a less-common signature against the alphabetized full index."
---

## What this skill carries

The full alphabetized signatures index — every public factory, modifier, hook, theme token, and enum — lives at:

```
references/reactor.api.txt    (~12K tokens, ~650 lines)
```

It is the source of truth for the public API surface, regenerated from `Reactor.dll` by `mur --regen-api`.

## When to load this skill

You probably don't need to. The `reactor-getting-started` skill carries the hooks table, the most-used factory signatures, the React-to-Reactor mapping, modifiers, theme tokens, and the critical gotchas — that's the 90% case.

Load this skill **only** when:

- You've checked `reactor-getting-started` and the factory/modifier you need isn't listed.
- You see a `REACTOR_*` analyzer ID and want to confirm the exact API surface in question.
- You're using a less-common control (DataGrid column overrides, AnnotatedScrollBar, AcrylicBrush parameters, etc.).

## How to use the api index

Read the file **once**, scan for what you need, then keep working from memory. **Do not re-page through it** — the file is large and re-reading injects ~12K tokens per call. Per-pattern lookups are far cheaper than full re-reads.

If you only need to confirm whether a single name exists, use a `grep` for the symbol against the file rather than viewing it whole.

## Common naming gotchas

- **`FlexElement` record properties** (set via `with { ... }`):
  `Direction`, `JustifyContent`, `AlignItems`, `AlignContent`, `Wrap`, `ColumnGap`, `RowGap`
  ⚠️ It's `JustifyContent` — NOT `Justify`.
  Example: `FlexRow(a, b, c) with { JustifyContent = FlexJustify.SpaceBetween, ColumnGap = 8 }`
