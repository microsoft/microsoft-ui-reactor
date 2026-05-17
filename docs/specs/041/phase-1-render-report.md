# 041 Phase 1 — Render & Validation Report

Date: 2026-05-16. Verifies the Phase 1 exit criteria (spec §9 Phase 1):
`mur docs compile --validate-only` passes, the Hooks reference category
renders, every new stub round-trips, and the Previous/Next chain across
the index is unbroken.

## Inputs

- Branch: `docs/041-uplift`
- Pipeline: `src/Reactor.Cli/Docs/` (TemplateParser, SnippetExtractor,
  TierLint, DiagramProcessor, ReferenceGen, ReferenceLinkInjector,
  ReferenceMap, CompileCommand)
- `Reactor.xml` source for ref-gen:
  `src/Reactor/bin/x64/Debug/net10.0-windows10.0.22621.0/Reactor.xml`
- `mermaid-cli` (`mmdc`) — available locally per `docs/contributing/doc-pipeline.md`

## Commands run

```
dotnet build src/Reactor.Cli/Reactor.Cli.csproj
mur docs compile --validate-only                    # validate-only path
mur docs compile --validate-only --skip-reference   # subset for stubs
mur docs compile --validate-only --tier=comprehensive
mur docs compile --no-build --no-screenshots       # full pipeline
```

## Page inventory

| Section | Pages discovered | Tier filed | Status |
|---------|------------------|------------|--------|
| Existing topic templates (no tier declared) | 25 | (info-only) | Pass |
| New stub templates | 38 | stub | Pass |
| dev-tooling.md.dt (merged) | 1 | comprehensive | Pass (only W001 winui-ref warning) |
| **Total templates** | **64** | — | Pass |
| Generated Hooks reference pages | 73 | (n/a) | Pass |
| Hand-authored Hooks reference index | 1 | (n/a) | Pass |
| **Total guide MD files** | **138** | — | — |

## Tier-lint findings (Phase 1 end state)

`mur docs compile --validate-only`:

- **0 errors.**
- **1 warning** (`REACTOR_DOC_TIER_W001` on `dev-tooling.md.dt`,
  expected — non-fatal, dev-tooling is not a WinUI-wrapper page so
  `winui-ref:` is correctly unset).
- **22 info-level findings** on pre-existing templates that have not
  yet declared a `tier:` field. These are surfaced (per the Phase 1A
  retro decision: pages without a declared tier emit findings at info
  severity only) so the validate-only run does not fail, but they
  serve as a TODO list for Phase 4 promotions.

## Reference-generation findings

`Phase 5.7: Reference` (running against the live `Reactor.xml`):

- **73 pages emitted** to `docs/guide/reference/hooks/`.
- **REACTOR_DOC_REFGEN_001** (unresolvable cref) warnings on 14 pages.
  All cite types outside the routed Hooks set (`Core.Component`,
  `Core.QueryCache`, `Core.AsyncValue<T>`, `System.OperationCanceledException`,
  etc.). These resolve once Phase 2-3 add factories / system categories;
  the canonical Roslyn-level cref check stays in the `REACTOR_DOC_002`
  analyzer per the retro entry.
- **REACTOR_DOC_REFGEN_002** (name collision) warnings on 4 pages
  (`Register`, `UseMemoCells`, `UseMemoCellsByKey`, `UseMemoCellsByIndex`).
  Parallel extension classes collapse to the same short name; first
  wins the page. A later phase will emit per-type subsections or use
  the registry to rename.
- **REACTOR_DOC_REFGEN_W001** (missing `<summary>` — placeholder
  emitted) on 2 pages (`UseElementFocus`, `UseElementRef`). These are
  captured in `xmldoc-backlog.md` for the Phase 4 elevation to
  `REACTOR_DOC_001 = Error`.

## Image-ref findings (pre-existing, not Phase 1C scope)

`REACTOR_DOC_IMAGE_001` reports four missing screenshots:

- `images/forms/keep-submit-reachable.png`
- `images/winforms-interop/island-control.png`
- `images/winforms-interop/designer.png`
- `images/winforms-interop/background.png`

These are screenshot IDs that exist in the templates but were not
captured. The `--no-screenshots` flag was used during this validation
run; with the full pipeline (capture enabled) the harness produces
them. Logged here as an open item for the Phase 4 cross-link sweep —
not blocking Phase 1.

## Previous/Next chain walk

Verified by reading the generated `docs/guide/readme.md` and the
front-matter `order:` values across the 64 templates:

| Order | File | Status |
|-------|------|--------|
| 0 | readme.md | Reachable from index |
| 1 | getting-started.md | Reachable from `readme → §1` |
| 1.5 | thinking-in-reactor.md | Reachable from `readme → §1` (NEW stub) |
| 1.7 | reactor-vs-xaml.md | Reachable from `readme → §1` AND `§9` (double-indexed) |
| 2 | dev-tooling.md | Reachable from `readme → §7` |
| 2.5 | testing.md | Reachable from `readme → §7` (NEW stub) |
| 2.7 | performance.md | Reachable from `readme → §7` (NEW stub) |
| 2.8 | packaging.md | Reachable from `readme → §7` (NEW stub) |
| 3 | components.md | Reachable from `readme → §2` |
| 4 | hooks.md | Reachable from `readme → §2` |
| … | (remaining 47 existing + new stubs) | — |
| 30-43 | architecture-overview … perf-instrumentation | Reachable from `readme → §9` (14 NEW stubs) |

Every link target in the generated `readme.md` was checked against
the discovered template list — zero broken inbound links.

## Mermaid render

`docs/_pipeline/diagrams/architecture-overview/overview.mmd` (the
placeholder Mermaid authored in Phase 1.5) round-trips through the
`mmdc` step when `--skip-diagrams` is omitted. The rendered SVG lands
at `docs/guide/images/architecture-overview/overview.svg`.

Visual contrast verification on GitHub's light + dark themes is
deferred to the preview-branch PR (see "GitHub-rendering caveats"
below) since the placeholder mermaid is not yet referenced from any
guide page.

## GitHub-rendering caveats

The full preview-branch render check (task 1.14 last bullet) lands
when the Phase 1 PR is opened against `main`. Specific items to verify
on that preview:

- **Mermaid SVGs** render inline on both light and dark themes.
- **Stub pages** show correctly with relative links resolving across
  the new `recipes/` subfolder.
- **Reference-page tree** (`docs/guide/reference/hooks/*.md`) renders
  with collapsible navigation in GitHub's tree view (it's 73 pages —
  worth confirming the directory listing is browsable).
- **Cross-axis link rendering** (`<!-- ref:UseState -->` markers
  expanded by the link injector) lands as expected hyperlinks.

## Exit-criteria verification (spec §9 Phase 1)

| Criterion | Status |
|-----------|--------|
| `mur docs compile --validate-only` passes for every page including new stubs | Pass |
| One auto-generated reference category (Hooks) renders correctly | Pass — 73 pages |
| Tier lint enforces per-tier checklist | Pass — `REACTOR_DOC_TIER_001..012` + W001 |
| Page-template skeletons exist (stub / solid / comprehensive) | Pass — under `_skeletons/`, excluded from discovery |
| Readme rewritten with 10-section index | Pass — 64 entries across §1-§10 |
| `dev-tooling.md` promoted Comprehensive; `devtools-ux.md` merged in and removed | Pass |
| Reference-map registry seeded for Hooks / Factories / Charting | Pass — `docs/_pipeline/reference-map.yaml` |
| AI Author Skill updated for tier / winui-ref / ai:caveat / source-snippet / SVG policy / diagram authoring / ref:Member / 64-page topic index | Pass |
| Pipeline image/SVG handling functional; Mermaid render wired up | Pass (CI install step still pending — see §1.5) |
| Pipeline snippet source-tree (`source:`) functional | Pass — covered by Phase 1.4 |
| Reference generation prototype on Hooks | Pass — 73 pages, retro entry captured |
| Conceptual-guide link injection | Pass — see Phase 1.9 |
| `REACTOR_DOC_001` / `_002` analyzers in place | Pass at Warning severity — see Phase 1.8 / retro |

## Open follow-ups (not blocking Phase 1)

- CI install of `mermaid-cli` (spec §1.5 task list bullet still
  unchecked; install steps documented in `docs/contributing/doc-pipeline.md`,
  workflow change deferred).
- 4 missing `images/.../*.png` references on `forms.md` and
  `winforms-interop.md` — surface when the full screenshot capture
  runs at next Phase boundary.
- 22 info-level tier-lint findings on the 25 pre-existing pages that
  have not yet declared a `tier:` field. Phase 4 promotes them to
  Solid/Comprehensive and elevates the lint severity per declared tier.
- `REACTOR_DOC_REFGEN_001` cross-namespace cref warnings — resolve
  automatically once factories / system categories generate.
