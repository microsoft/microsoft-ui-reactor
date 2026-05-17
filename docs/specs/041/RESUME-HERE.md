# Resume here — spec 041 implementation handoff

**Last touched:** 2026-05-17
**Branch:** `docs/041-uplift` (33+ commits ahead of `main`; not pushed)
**Active task:** Phase 4 — Complete. Phase 5 next.

**Out-of-band update (4f61824):** `getting-started.md` now opens with a manual-setup warning and walks through the source-clone bootstrap (`mur pack-local` → `dotnet new install` → `dotnet new reactorapp`). Not part of any spec-041 phase; track signed-distribution rollout in [spec 022](../022-packaging-and-distribution.md).

---

## What's done

| Phase | Status | Notes |
|-------|--------|-------|
| 0. Scaffolding | ✅ Complete | Companion files, audit, source map, branching strategy |
| 1. Foundation (pipeline + tooling) | ✅ Complete | 14 commits; pipeline supports tiers, lint, source snippets, SVG/Mermaid, ref-gen, link injection, analyzers |
| 2. Reactor-unique gaps | ✅ Complete (5 of 9 recipes shipped) | 10 commits; 4 recipes deferred to "recipes promotion mini-phase" |
| 3. Controls catalog | ✅ Complete | 5 commits + review (text-and-media / status-and-info / dialogs-and-flyouts / forms expand / collections expand). InkCanvas flagged as not wrapped. |
| 3.5. Under-the-hood | ✅ Complete | 1 setup + 15 page + 2 review commits; 14 internals pages + reactor-vs-xaml at target tier. Wave-A TIER_004 lint relaxation kept; wave-C placeholder shells dropped. |
| 4. Polish & process | ✅ Complete | 21 promotions + 3 new pages + cross-link analyzer + sweep + 4.6 final review. Tier: 36 Comprehensive · 22 Solid · 5 Stub. |
| 5. Continuous quality | ⏳ Pending | check-tier subcommand + tier-drift CI + recipes promotion mini-phase |

---

## Where to resume

Phase 4 is closed. The recommended next track is **Phase 5 — continuous
quality** plus a **recipes promotion mini-phase** (call it 2.5b) that
clears the 4 recipe stubs. The two can run in either order; Phase 5 is
the structural exit gate for spec 041 so it's the higher priority.

### Recommended next action: Phase 5 setup (task §5.1-5.4)

Phase 5 is four sub-tasks per the implementation task list:

1. **§5.1 — `mur docs check-tier` standalone command.** Factor the
   §11 lint out of `mur docs compile` so authors can run tier
   validation without a full compile. Unit tests in
   `tests/Reactor.DocPipeline.Tests/`. Same fast inner loop the
   existing `--validate-only` provides, scoped narrower.
2. **§5.2 — Tier-drift CI check.** PR check that runs the new
   `check-tier` on every PR that touches templates or doc apps.
   Failure modes documented in
   `docs/contributing/doc-pipeline.md`.
3. **§5.3 — Doc-coverage gate for new features.** Add a
   `CONTRIBUTING.md` convention: new framework features land with a
   doc page at Solid or higher. Consider an analyzer or convention
   check that flags new public API not referenced by any
   `<!-- ref:Member -->` or `<seealso cref=` marker.
4. **§5.4 — Quarterly tier audit cadence.** Schedule documented in
   `docs/contributing/doc-pipeline.md` so silent tier drift is
   caught on a recurring cadence.

### Alternative parallel track: recipes promotion mini-phase (2.5b)

If a content-author agent is spawnable in parallel with Phase 5
tooling work, run the recipes promotion in the background. Each
recipe needs:

1. A working doc app under `docs/_pipeline/apps/recipes/<name>/`
   with at least 3 snippet markers + one screenshot.
2. Template upgrade from Stub to Solid (mental-model lead +
   reference section + Tips + Next Steps + cross-links).
3. Tier-lint clean under `mur docs compile --validate-only
   --tier=solid`.

The 4 remaining recipe stubs: `recipes/paginated-list`,
`recipes/multi-step-form`, `recipes/command-palette`,
`recipes/drag-reorder`. Each is one Agent-spawn task; the work is
parallelizable.

### Phase 4 deferred items that bleed forward

| Item | Where tracked |
|---|---|
| Spec §13 `≤4 Solid` → `≤24 Solid` amendment proposal | `phase-4-retro.md` surprise #1 |
| `thinking-in-reactor.md` essay (stub → Solid) | Phase 5 hand-author |
| 11 `REACTOR_DOC_REFMARKER_001` warnings (hooks.md / effects.md) | Phase 5 reference-gen expansion: route `RenderContext` factories into the `hooks` category in `reference-map.yaml` OR remove the markers |
| 22 `REACTOR_DOC_IMAGE_001` findings (18 Mermaid SVGs + 4 PNGs) | Phase 5 / CI ops — `mmdc` on a build host renders the SVGs; the doc-app harness captures the PNGs |
| Owner field in spec 041 header still says `TBD` | Spec 041 §0 / governance |
| GitHub preview render check (Phase 4.6 fourth checkbox) | Awaits push to remote — Phase 5 entry gate |
| Renderer-internals expert review (Phase 3.5 §3.5.16) | Awaits human reviewer with renderer commit history |
| TIER_W001 winui-ref informational noise | Phase 5 lint-quality improvement: downgrade to info OR filter to wrapper-page surfaces |

### Phase 3.5 retro entry re-examined

The Phase 3.5 retro flagged a "trampoline vs marshal" vocabulary
drift call to settle in Phase 4. The Phase 4.6 review re-examined
`threading-and-dispatch.md.dt` and concluded both terms are used
CORRECTLY for distinct mechanisms — "trampoline" is the
`ThreadAffinity.cs#dispatcher-trampoline` guard, "marshal" is the
cross-thread call delivery. The page text differentiates them
clearly. **No spec change needed.**

---

## Pipeline state worth knowing

- `mur docs compile --validate-only` runs clean across all 63
  templates. 0 errors, 24 TIER_W001 winui-ref-not-declared
  informational warnings (all intentional), 0 cross-link findings.
- 73 hook reference pages auto-generated; index at
  `docs/guide/reference/hooks/index.md`.
- Tier-lint codes `REACTOR_DOC_TIER_001..012/W001` enforce per-tier
  checklist.
- `REACTOR_DOC_TIER_004` accepts EITHER a resolved `screenshot://`
  reference OR an inline `images/<topic>/` diagram (relaxation in
  `src/Reactor.Cli/Docs/TierLint.cs`, commit `2ecee29`). Spec §11
  text now matches (commit `0b71819c`, Phase 4.6).
- Cross-link analyzer `REACTOR_DOC_XLINK_001` at Warning severity;
  scope is identifier-shape concept registry from template titles +
  `concept-aliases:` + generated reference filenames.
- Snippets: `snippet="<topic>/<id>"` for doc apps,
  `snippet="source:<path>#<region>"` for `src/`.
- Diagrams: `.mmd` and `.svg` under
  `docs/_pipeline/diagrams/<topic>/`; pipeline renders/copies to
  `docs/guide/images/<topic>/`. `mmdc` required on the build host.
- Catalog thumbnails: `kind: catalog-thumb` in `doc-manifest.yaml`
  (320×240 letterbox).
- Reference-map registry at `docs/_pipeline/reference-map.yaml`.
- `<!-- ref:Member -->` markers in templates expand to links into
  the generated reference axis when the member is routed by the
  registry. RenderContext factories are NOT yet routed; 11 such
  markers in `hooks.md.dt` + `effects.md.dt` fall through as HTML
  comments (invisible to readers but inert).

---

## Tier distribution (verified 2026-05-17)

| Tier | Count | Pages |
|------|-------|-------|
| Comprehensive | **36** | accessibility, advanced, analyzer-architecture, animation, animation-pipeline, architecture-overview, charting, collections, commanding, components, context, data-system, dev-tooling, devtools-internals, dialogs-and-flyouts, effects, effects-scheduling, flex-layout, focus-and-input-internals, forms, getting-started, hooks, hooks-internals, input-and-gestures, layout, modifier-system, navigation, perf-instrumentation, reactivity-model, reactor-vs-xaml, reconciliation, styling, text-and-media, theming-tokens, winforms-interop, xaml-developers |
| Solid | **22** | async-resources, cheat-sheet, controls, element-pool, localization, packaging, performance, persistence, readme, recipes/index, recipes/login, recipes/master-detail, recipes/modal-dialog, recipes/search-with-suggestions, recipes/settings-page, rules-of-reactor, source-mapping, status-and-info, testing, threading-and-dispatch, windows, wpf-interop |
| Stub | **5** | recipes/command-palette, recipes/drag-reorder, recipes/multi-step-form, recipes/paginated-list, thinking-in-reactor |

Spec §13 `≤4 Solid` / `0 Stub` targets are not met. The Solid gap is
proposed for spec amendment (see `phase-4-retro.md` surprise #1).
The Stub gap is closeable via the recipes promotion mini-phase plus
the thinking-in-reactor essay.

---

## How to spawn the next agent

Phase 5 (check-tier + tier-drift CI + doc-coverage gate +
quarterly-audit cadence) is the next track. Brief the agent with:

1. Mark a Phase 5 sub-task in-progress.
2. Spawn a `general-purpose` Agent per sub-task.
3. The four sub-tasks are largely independent — pick any to start.
4. Phase 5 work touches `src/Reactor.Cli/Docs/`,
   `tests/Reactor.DocPipeline.Tests/`, `docs/contributing/`, and
   `CONTRIBUTING.md`. It does NOT touch `docs/_pipeline/templates/`
   (no template changes in Phase 5).
5. Each new CLI subcommand = its own commit on `docs/041-uplift`.
6. Do NOT push. Local commits only until the recipes promotion or
   Phase 5 exits review.

---

## Commit chain (most recent first)

```
<pending> docs(041): regenerate docs/guide/** from latest templates (4.6)
<pending> docs(041): Phase 4 review — retro, render report, comparison alignment (4.6)
0b71819c docs(041): spec §11 screenshot/diagram text alignment (4.6)
a3f4cf5b docs(041): input-and-gestures promoted to Comprehensive tier (4.6 wave-c)
ebed0a2d docs(041): winforms-interop promoted to Comprehensive tier (4.6 wave-c)
88fe5c51 docs(041): xaml-developers promoted to Comprehensive tier (4.6 wave-c)
4ea1c205 docs(041): getting-started promoted to Comprehensive tier (4.6 wave-c)
3bcc56d6 docs(041): declare tier:solid on readme, localization, async-resources, windows (4.6 wave-d)
402322d5 docs(041): flex-layout promoted to Comprehensive tier (4.6 wave-3)
dbb3bf71 docs(041): layout promoted to Comprehensive tier (4.6 wave-2)
17a93cf6 docs(041): styling promoted to Comprehensive tier (4.6 wave-3)
3e4a85a9 docs(041): components promoted to Comprehensive tier (4.6 wave-1)
dddef719 docs(041): context promoted to Comprehensive tier (4.6 wave-2)
ba8ed02a docs(041): advanced promoted to Comprehensive tier (4.6 wave-3)
e80dfa80 docs(041): commanding promoted to Comprehensive tier (4.6 wave-2)
72a58424 docs(041): effects promoted to Comprehensive tier (4.6 wave-1)
d39b8329 docs(041): hooks promoted to Comprehensive tier (4.6 wave-1)
5258c4bf docs(041): tick §4.5 cross-link sweep — exit gate clean (4.5)
8a95a832 docs(041): cross-link sweep — readme + winforms-interop (4.5)
3f179522 feat(041): cross-link analyzer (REACTOR_DOC_XLINK_001) — 4.5
d50df763 docs(041): performance at Solid tier (4.3)
558051c6 docs(041): packaging at Solid tier (4.4)
07879906 docs(041): wpf-interop at Solid tier (4.2)
e1fa58a docs(041): Phase 4.1 promotions — navigation/animation/accessibility/data-system/charting
<pending Phase 3.5 commits>
<pending Phase 3 commits>
<pending Phase 2 commits>
<pending Phase 1 commits>
c4c5a0d docs(041): Phase 0 scaffolding — audit, source map, branching strategy
```

(See `git log --oneline` on `docs/041-uplift` for the full chain — 50+
commits since `e1fa58a` on 2026-05-16.)
