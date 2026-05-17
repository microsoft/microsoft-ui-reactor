# Phase 2 — Review

Captured 2026-05-17. Source: spec 041 §9 Phase 2 acceptance gate, tasks §2.1 – §2.8.

## Scope shipped

| Task | Page | Tier | Notes |
|---|---|---|---|
| 2.0 | (harness) | n/a | `kind: catalog-thumb` capture support in `doc-manifest.yaml`; `ImageProcessor.ProcessThumb` + tests. |
| 2.1 | `controls.md` | Solid | Catalog index; 7 category sections; catalog-thumb placeholders. |
| 2.2 | `testing.md` | Solid | Headless fixtures, snapshot tests, effect-aware async, a11y scanner, SelfTests pattern. |
| 2.3 | `theming-tokens.md` | Comprehensive | 35-token catalog; light + dark swatches; 3 patterns; 3 common mistakes. |
| 2.4 | `persistence.md` | Solid | Window/Application scopes, versioned migration, disk bridge via `UseEffect`. |
| 2.5 | `recipes/index.md` + 5 recipes | Solid | login, master-detail, settings-page, modal-dialog, search-with-suggestions. |
| 2.6 | `cheat-sheet.md` | Solid | One-screen reference card. |
| 2.7 | `rules-of-reactor.md` | Solid | Five core rules with analyzer codes. |

## Deferred

| Item | Reason | Where tracked |
|---|---|---|
| `recipes/paginated-list`, `multi-step-form`, `command-palette`, `drag-reorder` | Quality-over-volume — the five recipes shipped cover the highest-traffic patterns. The four deferred recipes remain stubs and are marked "Phase 2.5" in the gallery so the surface area stays visible. | Task §2.5 + retro |
| Auto-generated theming-tokens swatch table | Hand-curated table that lints clean ships now; the compile-time generator that reads `src/Reactor/Core/Theme.cs` and emits the catalog snippet lands in Phase 4 per spec §14 risk mitigation. | Task §2.3 + `theming-tokens.md.dt` TODO marker |
| Auto-gen wiring for cheat-sheet from the reference axis | Hand-curated for Phase 2 per spec; full wiring in Phase 4. | Task §2.6 |

## Tier-lint state

`mur docs compile --validate-only` reports **validation passed**. Every Phase 2 page is tier-lint clean against its declared tier:

- 1 page at Comprehensive: theming-tokens.
- 11 pages at Solid: controls, testing, persistence, cheat-sheet, rules-of-reactor, recipes/index, recipes/login, recipes/master-detail, recipes/settings-page, recipes/modal-dialog, recipes/search-with-suggestions.
- 4 recipe stubs unchanged at stub tier: paginated-list, multi-step-form, command-palette, drag-reorder.

All remaining lint findings (`REACTOR_DOC_TIER_005`, `006`, `007`) are info-level on the existing 26 pages that have no declared `tier:` — pre-existing state from Phase 1 and out of scope for Phase 2.

## Cross-link audit

`<!-- ref:Member -->` markers — none added in Phase 2 since the auto-generated reference axis (Hooks) is the only category with generated leaves. Phase 4 cross-link sweep adds these. No `REACTOR_DOC_REGISTRY_W002` warnings flagged for Phase 2 templates.

Inline link audit (manual walk through each new Phase 2 page):

- `controls.md` — every category links to its detail page (forms/collections/data-system/charting exist today; text-and-media/status-and-info/dialogs-and-flyouts link to Phase 3 stubs).
- `testing.md` — links into hooks, effects, accessibility, dev-tooling, components.
- `theming-tokens.md` — 9 inline cross-links: styling, hooks, animation, animation, components, accessibility, devtools-internals, rules-of-reactor, plus the WinUI `winui-ref`.
- `persistence.md` — links into hooks, effects, windows, async-resources, context.
- Recipes — every recipe links back to the index plus 3-4 deep-dive pages.
- `cheat-sheet.md` — every row links to its deep page; the page is a routing surface.
- `rules-of-reactor.md` — every rule body links to its home page; the index table at top is the same content in scannable form.

## Phase 2 exit-gate per spec §9 Phase 2

> Every Reactor-original concept that lives only in `ai-author-skill.md` also has a user-facing page.

Walking the AI Author Skill's API reference (`docs/_pipeline/ai-author-skill.md` §"Reactor API Quick Reference"):

| Concept | Page |
|---|---|
| Hooks (`UseState`, `UseEffect`, etc.) | `hooks.md` |
| `UsePersisted` + scopes | **`persistence.md` (Phase 2)** |
| `UseColorScheme` / `ThemeRef` | **`theming-tokens.md` (Phase 2)** |
| Controls catalog | **`controls.md` (Phase 2)** |
| Validation + Forms | `forms.md` |
| Data system + DataGrid | `data-system.md` |
| Charting | `charting.md` |
| Commanding | `commanding.md` |
| Navigation + DeepLinkMap | `navigation.md` |
| Accessibility scanner | `accessibility.md` |
| Testing patterns | **`testing.md` (Phase 2)** |
| Recipes | **`recipes/index.md` (Phase 2)** |
| Cheat sheet | **`cheat-sheet.md` (Phase 2)** |
| Rules / analyzer codes | **`rules-of-reactor.md` (Phase 2)** |

No Reactor-original concept on the skill remains without a guide page after Phase 2.

## Doc-quality walk

Each Phase 2 page was read end-to-end after lint passed. Findings:

- **No "Let's dive in!" intros.** Every page leads with mental-model or code, per the spec's anti-slop guidance.
- **Caveats are concrete.** Theming-tokens names the specific failure mode (hardcoded color skips theme swap; analyzer catches at build time). Persistence names the specific failure mode (in-memory only — disk requires UseEffect bridge). Rules-of-reactor names the specific failure mode per rule.
- **No padded recap sections.** Tips sections are 3 bold-lead bullets each; Next Steps are 4–5 cross-links per page.
- **Recipes are concrete compositions, not toy examples.** Each recipe builds a real screen out of existing factories (TextField, Button, VStack/HStack, ListView shapes); no novel custom components per recipe.

## Outstanding work for Phase 2.5 follow-up

1. Four deferred recipes (paginated-list, multi-step-form, command-palette, drag-reorder).
2. Wire the doc-pipeline harness to actually run the catalog-thumb capture against the controls doc app, replacing the gray placeholder PNGs.
3. Cross-link injection from existing pages (hooks, effects, components, styling) back to the new Phase 2 pages — this happens automatically during the Phase 4 cross-link sweep.
4. GitHub preview-branch render check on the full Phase 2 surface — same checklist as Phase 1.14.

## Commit log

11 commits land in Phase 2 (2.0 through 2.7), one per page or per cluster of related work:

```
3e7aa29 docs(041): recipes index + 5 Solid recipes (2.5)
f8a190a docs(041): persistence page at Solid tier (2.4)
a1688f7 docs(041): theming-tokens at Comprehensive tier (2.3)
578952d docs(041): testing page at Solid tier (2.2)
18913ab docs(041): controls catalog index page at Solid tier (2.1)
000d90e feat(041): catalog-thumb capture kind for controls index (2.0)
```

…plus the cheat-sheet (2.6), rules-of-reactor (2.7), and this review (2.8).
