# Phase 1 retro — Doc pipeline foundation

Date opened: 2026-05-16 (delete this file at the end of Phase 4 if no
decisions changed.)

This page captures decisions / workarounds discovered while landing
spec 041 Phase 1. Each entry is short — write only what is needed to
explain "why this is the way it is" to a future maintainer.

## Decisions

### Pipeline lives in `src/Reactor.Cli/Docs/`

Task list mentions `tools/doc-pipeline/` in some places but the
compiler actually lives under the `mur` CLI's `Docs/` folder. The
spec is correct in `tools/doc-pipeline/` references but the
implementation reuses the CLI host (one binary on PATH, no extra
project to manage). All Phase-1 modules — `TemplateParser.cs`,
`SnippetExtractor.cs`, `TierLint.cs`, `DiagramProcessor.cs` —
sit under `src/Reactor.Cli/Docs/`.

### Test project TFM matches the CLI

`tests/Reactor.DocPipeline.Tests/` targets the same
`net10.0-windows10.0.22621.0` TFM as `Reactor.Cli` so the test
project can `ProjectReference` it without a TFM-compat downgrade.
The compiler itself doesn't use any Windows-specific API, but
sharing the TFM avoids a separate netstandard slice of the CLI
just for tests.

### `DocTier` is public

It's an internal type by intent (the spec only exposes the literal
string in front-matter), but the test harness needs to take
`[InlineData(..., DocTier.Solid)]` parameters on public `[Theory]`
methods. Cheapest fix: make the enum public. The compiler still
gates entry through `TemplateParser.ParseTier` so the only path
from raw string → enum has full validation.

### `tier:` not declared → info-level lint, not error

Per the prompt's guidance: spec 041 needs `mur docs compile --validate-only`
to *run* across the current 26 pages without crashing. None of those
pages have declared a tier yet, so applying the full Solid checklist
would flood stderr with errors. Fallback: pages without `TierDeclared`
default to `Solid` *for parsing* but every lint finding for them is
demoted to info severity. Authors get visible feedback (the prefixed
`ℹ` line) without blocking the build. Once a page adds `tier:` to its
front-matter, the same finding becomes a real error.

### `--no-screenshots` and `--skip-screenshots` are aliases

Spec 041 §10.3 uses `--skip-screenshots`; the existing CLI used
`--no-screenshots`. Both names map to the same flag so docs and
muscle memory match.

### Caveat-block error code

`<!-- ai:caveat -->` without a closing tag raises
`REACTOR_DOC_CAVEAT_001` from `TemplateParser`. Distinct namespace
from the `REACTOR_DOC_TIER_*` and `REACTOR_DOC_SNIPPET_*` codes so
authors can grep by family.

## Known follow-ups (defer to later phases)

- Mermaid-cli render path is implemented but the CI install /
  cache job lives in spec §1.5 / Phase-5 polish (`tools/ci/`); the
  compiler emits `REACTOR_DOC_DIAGRAM_001` when `mmdc` is missing,
  so the failure mode is clear without the workflow change.
- The §11 mental-model heuristic is a word count rather than a
  semantic check. False positives expected on technical pages that
  open with a dense table-of-contents paragraph; revisit if it
  becomes noisy.
- `mur docs render-diagrams --watch` is a TODO marker — the
  FileSystemWatcher plumbing is more than this phase needs. Authors
  can re-run without `--watch` for now.

### Task 1.7 — Hooks ref-gen page count

First end-to-end run of the §10.4 reference generator against
`src/Reactor/bin/x64/Debug/net10.0-windows10.0.22621.0/Reactor.xml`
emitted **73 hook pages** plus the hand-authored
`docs/guide/reference/hooks/index.md`. Extrapolating to the eventual
five active categories (hooks / factories / charting + the later
modifiers and system slots) puts the docset on the order of
300-400 generated pages — comfortably inside the 150-300 expected
range from spec §12.1 Q2 for a single category but above the
overall bound. Acceptable for Phase 1B; we'll revisit the
single-page-per-category fallback after Phase 3.5 ships and the
real bound is known.

### Task 1.7 — Phase 1B prototype downgrades

Two finding severities are softer in Phase 1B than the spec's
"failure → build error" wording suggests:

- `REACTOR_DOC_REFGEN_001` (unresolvable cref) is **warning** in
  Phase 1B because only the Hooks category emits pages — most
  cross-namespace crefs (Core, Input, Data, System) legitimately
  fall outside the routed set. Once factories + charting + the
  remaining categories generate, those crefs become resolvable and
  the severity can be re-elevated. The canonical Roslyn-level
  cref check stays an error via the `REACTOR_DOC_002` analyzer
  (task 1.8).
- `REACTOR_DOC_REFGEN_002` (name collision) is **warning** in
  Phase 1B for the same prototype reason. Parallel extension
  classes (`UseMemoCellsExtensions` and
  `ComponentUseMemoCellsExtensions`) collapse to the same short
  name; the first wins the page. A later phase will emit
  per-type subsections or rename via the registry.
- Constructors collapse to `#ctor` which collides catastrophically;
  Phase 1B drops standalone `#ctor` pages and a later phase will
  surface them as overload subsections on the parent type page.

## Open questions

None — all of §12.1's Phase-1 questions were resolved during the
spike.
