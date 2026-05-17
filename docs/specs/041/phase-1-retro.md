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

### Task 1.8 — Analyzer severity stays Warning in Phase 1B

The spec language in §10.4 says missing-summary is a "build error".
Phase 1B starts both REACTOR_DOC_001 and REACTOR_DOC_002 at Warning
severity. Two reasons:

1. The analyzer is **not currently wired into `src/Reactor/`** —
   `Reactor.csproj` deliberately doesn't list `Reactor.Analyzers` as
   `OutputItemType="Analyzer"` (see the comment in the csproj), so
   the rules only run on consumer code (samples, third-party). When
   we wire the analyzer into the framework in Phase 4, severity
   elevates to Error then. Until then, Warning at the rule level is
   the right default for consumer-facing diagnostics.
2. The backlog scan over `Reactor.xml` found **35 missing summaries
   across 3,445 public members** (~1.0%). Fixing those is tractable
   but not a Phase-1B priority; the 5 Hooks-namespace gaps were
   fixed in this commit to unblock the ref-gen prototype, and the
   remaining 30 (recorded in `xmldoc-backlog.md`) move to Phase 4.

The .editorconfig at the repo root suppresses both rules under
`samples/`, `tests/`, and `tools/` so consumer-facing public API is
the only thing the rules can fail.

## What surprised us (Phase 1C — tasks 1.10-1.14)

### `recipes/` forced template-discovery to recurse

The spec §7.1 Section 6 calls for `recipes/` as a folder (one
recipe per page so the gallery scales without nav churn). The
existing `CompileCommand.DiscoverTemplates` used
`Directory.GetFiles(templatesDir, "*.md.dt")` with the implicit
`SearchOption.TopDirectoryOnly`, which would silently skip every
recipe in the new subfolder. Fix landed in the 1.10 commit:
`EnumerateTemplateFiles` now recurses and explicitly excludes
the `_skeletons/` directory authored in task 1.11. Topic id
gains the subfolder prefix (`recipes/login`) so the output
path round-trips to `docs/guide/recipes/login.md`. Worth
remembering: any other subfolder under `templates/` now ships
to the corresponding `docs/guide/` subfolder.

### `order:` needed to widen to `double`

Spec §7.2 says "new pages slot in as `.5` between existing
integer orders until the Phase-4 rebase". The existing parser
used `int.TryParse(value, out var o)` which silently dropped
the fractional part (every `.5` page would have ended up at
order 0). Widened `DocTemplate.Order` to `double` and the
parser to `double.TryParse` with `InvariantCulture`. Cheap
change; no other call site mattered (only `OrderBy(...)` reads
the value).

### The readme is structurally distinct enough to skip `tier:`

The §11 tier-lint checklist for Solid expects a reference
table and a `## Next Steps` section. The new readme is a
landing-page-plus-index — its "table" is the 10-section list
itself, and the "Next Steps" is the entire body. Easiest
honest fix: leave `tier:` undeclared on the readme so the
lint drops to info severity (per the existing Phase-1A
fallback). A future refinement could special-case the readme
in the lint, but the current behavior is already correct: the
information-only findings serve as visible TODOs for Phase 4.

### Comprehensive tier passes cleanly on dev-tooling

The merged `dev-tooling.md.dt` is the first Comprehensive-tier
page to pass the full §11 checklist end-to-end. Useful
shape-reference for Phase 2-3 authoring: the §6.3 requirements
(≥80-word mental-model lead, `<!-- ai:caveat -->` block,
`## Patterns`, `## Common Mistakes`, ≥5 inline cross-links,
reference table, ≥3 snippets, ≥1 screenshot) are all
ergonomic — the page reads naturally without feeling
checklist-stuffed.

### Phase 1 task list — remaining unchecked items

- `Phase 0 §0.1 — Confirm owner assigned in spec header`. The
  spec still lists `Owner: TBD`. Not a Phase-1 blocker; owner
  assignment is an organizational concern handled at PR time.
- `Phase 1 §1.5 — CI install of mermaid-cli`. The local
  install steps are documented in
  `docs/contributing/doc-pipeline.md`; the GitHub Actions
  workflow change is tracked as a Phase-5 ops follow-up.
- `Phase 1 §1.7 — GitHub preview render check`. Deferred to
  task 1.14's GitHub-render walk-through; the chain integrity
  is verified locally in `phase-1-render-report.md`.

## Open questions

None — all of §12.1's Phase-1 questions were resolved during the
spike.
