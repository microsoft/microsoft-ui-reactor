# Resume here — spec 041 implementation handoff

**Last touched:** 2026-05-17
**Branch:** `docs/041-uplift` (32 commits ahead of `main`; not pushed)
**Active task:** Phase 3.5 — Under-the-hood deep dive (14 pages)

**Out-of-band update (4f61824):** `getting-started.md` now opens with a manual-setup warning and walks through the source-clone bootstrap (`mur pack-local` → `dotnet new install` → `dotnet new reactorapp`). Not part of any spec-041 phase; track signed-distribution rollout in [spec 022](../022-packaging-and-distribution.md).

---

## What's done

| Phase | Status | Notes |
|-------|--------|-------|
| 0. Scaffolding | ✅ Complete | Companion files, audit, source map, branching strategy |
| 1. Foundation (pipeline + tooling) | ✅ Complete | 14 commits; pipeline supports tiers, lint, source snippets, SVG/Mermaid, ref-gen, link injection, analyzers |
| 2. Reactor-unique gaps | ✅ Complete (5 of 9 recipes shipped) | 10 commits; 4 recipes deferred to Phase 2.5 follow-up |
| 3. Controls catalog | ✅ Complete | 5 commits + review (text-and-media / status-and-info / dialogs-and-flyouts / forms expand / collections expand). InkCanvas flagged as not wrapped. |
| 3.5. Under-the-hood | ⏳ Pending | 14 pages; spawn next (different author skillset) |
| 4. Polish & process | ⏳ Pending | 7 promotions + 3 new pages + cross-link sweep |
| 5. Continuous quality | ⏳ Pending | check-tier subcommand + tier-drift CI |

---

## Where to resume

**Open the task list:** `docs/specs/tasks/041-docs-comprehensive-uplift-implementation.md`. Phase 3.5 starts at `### 3.5.1`.

**Read before spawning the next agent:**
- `docs/specs/041/phase-1-retro.md` — Phase 1 decisions and surprises.
- `docs/specs/041/phase-1-render-report.md` — Phase 1 publish-test results.
- `docs/specs/041/phase-2-review.md` and `phase-2-retro.md` — Phase 2 findings.
- `docs/specs/041/phase-3-retro.md` and `phase-3-render-report.md` — Phase 3 findings (InkCanvas not wrapped; deprecated `ProgressBar` factory swept; grouping + drag-reorder are recipe-only today).

**Pipeline state worth knowing:**
- `mur docs compile --validate-only` runs clean across all 64 templates.
- 73 hook reference pages auto-generated; index at `docs/guide/reference/hooks/index.md`.
- Tier-lint codes `REACTOR_DOC_TIER_001..012/W001` enforce per-tier checklist.
- Snippets: `snippet="<topic>/<id>"` for doc apps, `snippet="source:<path>#<region>"` for `src/`.
- Diagrams: `.mmd` and `.svg` under `docs/_pipeline/diagrams/<topic>/`; pipeline renders/copies to `docs/guide/images/<topic>/`.
- Catalog thumbnails: `kind: catalog-thumb` in `doc-manifest.yaml` (320×240 letterbox).
- Reference-map registry at `docs/_pipeline/reference-map.yaml`.
- `<!-- ref:Member -->` markers in templates expand to links into the generated reference axis.

**Known deferred items (track these forward):**
- Owner field in spec 041 header still says `TBD` (Phase 0 task §0.1, last unchecked Phase-0 box).
- 4 recipes still stubs: `paginated-list`, `multi-step-form`, `command-palette`, `drag-reorder`.
- 30 of 35 `<summary>`-missing public members deferred to Phase 4 (catalogued in `docs/specs/041/xmldoc-backlog.md`).
- CI install of `mermaid-cli` not yet wired into the GitHub Actions workflow (Phase 5 ops).
- 4 pre-existing missing screenshot references surface as `REACTOR_DOC_IMAGE_001` findings (`forms/keep-submit-reachable`, three on `winforms-interop`) — pre-Phase-1 issue, not blocking.
- 25 pre-existing topic pages have no declared `tier:` — info-only lint findings; Phase 4 promotes them.

---

## How to spawn the next agent

Phase 3.5 (under-the-hood) is the next track. It is a different
author skillset — these 14 pages require renderer/hook internals
knowledge, and the spec calls for review by someone who has shipped
renderer/hook internals. Brief the agent with:
1. Mark the Phase 3.5 task in-progress.
2. Spawn a `general-purpose` Agent.
3. Briefing must include: **read the source areas listed in
   `docs/specs/041/under-the-hood-source-map.md` before drafting each
   page; pull `snippet="source:..."` from at least 3 source areas per
   Comprehensive page; diagrams via `.mmd` under
   `docs/_pipeline/diagrams/<topic>/` (the pipeline renders to SVG);
   avoid AI slop patterns; match the Reactor voice from
   `docs/guide/hooks.md` / `dev-tooling.md` / `theming-tokens.md`**.
4. Each new page = its own commit on `docs/041-uplift`. Update
   task-list checkboxes as you go.
5. Do NOT push. Local commits only until a phase exits review.

Phase 3.5 can run in parallel with Phase 4 cross-link sweep once
enough Phase 3.5 pages have landed to make the cross-link surface
worth scanning; otherwise serial.

---

## Commit chain (most recent first)

```
80f07db docs(041): collections expanded to Comprehensive tier (3.5)
cf117ba docs(041): forms expanded to Comprehensive tier (3.4)
98418f6 docs(041): dialogs-and-flyouts at Comprehensive tier (3.3)
c769322 docs(041): status-and-info at Solid tier (3.2)
beb881c docs(041): text-and-media at Comprehensive tier (3.1)
cea7210 docs(041): regenerate UseElementFocus / UseElementRef reference pages
f09d572 docs(041): Phase 2 review + recipe tier-lint fixes (2.8)
01ba175 docs(041): rules-of-reactor at Solid tier (2.7)
7a22404 docs(041): cheat-sheet at Solid tier (2.6)
3e7aa29 docs(041): recipes index + 5 Solid recipes (2.5)
f8a190a docs(041): persistence page at Solid tier (2.4)
a1688f7 docs(041): theming-tokens at Comprehensive tier (2.3)
578952d docs(041): testing page at Solid tier (2.2)
18913ab docs(041): controls catalog index page at Solid tier (2.1)
000d90e feat(041): catalog-thumb capture kind for controls index (2.0)
a3b317c docs(041): Phase 1 validation + render report + retro update (1.14)
2e16104 docs(041): dev-tooling promoted to Comprehensive + devtools-ux merge (1.13)
7f5cb18 docs(041): readme rewrites to 10-section index + 38 stub templates (1.12)
db5f930 docs(041): page-template skeletons for stub / solid / comprehensive (1.11)
72d59d5 docs(041): AI author skill update for tiers, source snippets, diagrams (1.10)
0b0f72b feat(041): conceptual-guide link injection (1.9)
9993d3c feat(041): REACTOR_DOC_001 / REACTOR_DOC_002 analyzers (1.8)
d6944ac feat(041): reference generation prototype on Hooks (1.7)
653e106 feat(041): reference-map registry for ref-gen routing (1.6)
a2ec15f feat(041): SVG passthrough + Mermaid render pipeline (1.5)
d2852d5 feat(041): source-tree snippet extraction (1.4)
0b198a5 feat(041): tier-lint validator with REACTOR_DOC_TIER_* codes (1.3)
eaa1533 feat(041): tier, winui-ref, ai:caveat template fields (1.2)
07bf156 docs(041): document mermaid-cli Windows CI install steps (1.1)
c4c5a0d docs(041): Phase 0 scaffolding — audit, source map, branching strategy
```
