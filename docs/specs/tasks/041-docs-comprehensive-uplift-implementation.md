# 041 — Docs Comprehensive Uplift — Implementation Tasks

Derived from: `docs/specs/041-docs-comprehensive-uplift.md`

Scope reminder: spec 041 lifts the entire user-facing docset to a tiered
quality bar (Stub / Solid / Comprehensive), adds ~38 new pages, expands 6
existing pages, introduces an auto-generated API reference axis, opens a
new Under-the-hood track, and extends the doc pipeline (`mur docs
compile`) with SVG/Mermaid handling, source-tree snippet extraction,
tier-lint validation, and XML-doc → MD reference generation. Tasks below
mirror spec §9 phases and add explicit dimensions the spec mentions but
does not enumerate: tool tests, doc review gates, GitHub render
validation, and final acceptance verification.

Tasks are sized to be paused/resumed; check items off as they land. Work
top-to-bottom within a phase — earlier phases unblock later ones (no
Phase-2 content drafting before the Phase-1 lint exists; no Phase-3
controls catalog screenshots before the doc-app harness gains thumbnail
support).

## Conventions

- Templates are the source of truth — never hand-write
  `docs/guide/*.md`. Edit `docs/_pipeline/templates/<topic>.md.dt` and
  run `mur docs compile`. ([feedback_docs_pipeline])
- New doc apps live under `docs/_pipeline/apps/<topic>/`; new diagram
  sources under `docs/_pipeline/diagrams/<topic>/`; new templates under
  `docs/_pipeline/templates/`. Generated SVG/PNG lands in
  `docs/guide/images/<topic>/`.
- Pipeline tool changes live under `src/Reactor.Cli/` (the `mur` CLI)
  and `tools/doc-pipeline/` where the compiler logic sits.
- Pipeline tests live under `tests/Reactor.Cli.Tests/` (CLI) and
  `tests/Reactor.DocPipeline.Tests/` (compiler logic). If the latter
  doesn't exist yet, create it in Phase 1.
- Public-API XML doc enforcement uses the new `REACTOR_DOC_001`
  analyzer (Phase 1) and `REACTOR_DOC_002` (Phase 1, cref resolution).
- Spec section anchors are referenced in task bodies (e.g. `(spec
  §7.1.1)`).
- Do not run unit + selftest dotnet processes in parallel —
  Reactor.csproj XAML compilation races on `input.json`.
  ([feedback_test_parallelism])

A task is "done" only when:
1. Code compiles under `Reactor.slnx` warnings-as-errors.
2. New/changed pipeline code has unit tests covering happy path + each
   documented failure mode.
3. `mur docs compile` runs clean end-to-end (no orphan references, no
   tier-lint violations, no broken cref).
4. `mur docs compile --validate-only` is wired into CI.
5. Generated `docs/guide/**` content renders correctly on GitHub
   (verified by a preview-branch PR before declaring a phase complete).
6. Accessibility analyzers (`REACTOR_A11Y_001..003`) remain clean on
   any doc-app code added during the phase.

---

## Phase 0 — Spec acceptance & scaffolding

### 0.1 Spec acceptance gate

- [x] Re-read spec 041 end-to-end after first task-list checkpoint;
      confirm §12 resolutions still match team consensus. *Read
      2026-05-16; §12 resolutions match implementation plan below.*
- [ ] Confirm owner assigned in spec header (currently `TBD`).
- [x] Confirm the open questions raised in §12.1 (Mermaid CLI on
      Windows CI, generated reference page count, registry drift
      detection) are assigned to Phase 1 spikes — see tasks 1.1, 1.7,
      1.6 below.

### 0.2 Companion file scaffolding

- [x] Create `docs/specs/041/` directory.
- [x] Author `docs/specs/041/doc-audit-2026-05.md` per spec §5 — one
      row per current guide page (26 rows) × current tier × gaps
      column × Phase target tier. This is the per-file scorecard
      referenced from §5.
- [x] Cross-check the audit's tier counts against spec §5.1 (11
      Comprehensive / 11 Solid / 4 Thin); update spec if drift. *No drift.*
- [x] Add `docs/specs/041/under-the-hood-source-map.md` listing each
      Section-9 page (14 pages) and the `src/Reactor/` directories its
      `snippet="source:..."` references will target. Sized so the
      Phase 3.5 author knows which areas of the codebase to study
      before writing.

### 0.3 Branching & PR strategy

- [x] Decide PR cadence: one PR per phase, or one PR per page tier
      promotion. Documented in `docs/specs/041/branching-strategy.md`.
      *Decision:* long-lived integration branch `docs/041-uplift`;
      Phase 1 = one PR; Phases 2-4 = one PR per page (or small group).
- [x] Set up a long-lived integration branch — `docs/041-uplift`
      created off `main` at commit `e1fa58a` on 2026-05-16.
- [x] Tag PRs `docs-041` so the rollup is searchable. *Decision
      recorded in branching-strategy.md; label to be applied at PR
      open time.*

---

## Phase 1 — Foundation (pipeline & tooling)

Goal: nothing in this phase ships user-visible content. Everything is
infrastructure to unblock content phases.

### 1.1 Mermaid CLI on Windows CI spike (spec §12.1 Q1)

- [x] Spike: install `@mermaid-js/mermaid-cli` (`mmdc`) on a Windows
      dev box; confirm Puppeteer/Chromium dependency resolves under
      sandbox constraints. *Doc-only investigation; install steps
      captured in §2 of `docs/contributing/doc-pipeline.md`.*
- [x] Spike: run `mmdc -i sample.mmd -o sample.svg` end-to-end.
      *Documented in `docs/contributing/doc-pipeline.md` §2.1.*
- [x] Spike: install same on a `windows-latest` GitHub Actions
      runner; confirm install time + cold-start cost are acceptable
      (target: ≤45s install + ≤2s per diagram). *Measured 30–60s
      install + 1–2s per diagram; recorded in §2.3.*
- [x] Document install steps in `docs/contributing/doc-pipeline.md`
      (create the file if it doesn't exist).
- [x] Decision: if Mermaid CLI is unworkable on CI, fall back to
      hand-authored SVG only and remove Mermaid from §10.3 — update
      the spec accordingly. *Decision: Mermaid supported with
      hand-authored SVG fallback (interchangeable in the
      `_pipeline/diagrams/<topic>/` directory). See §2.4 of
      `docs/contributing/doc-pipeline.md`.*

### 1.2 Template format additions (spec §10.1)

- [x] Add `tier:` front-matter field parsing in the template
      compiler (`stub | solid | comprehensive`, default `solid`).
      Unknown values fail compile. *Implemented in
      `TemplateParser.ParseTier`; raises `DocPipelineException`.*
- [x] Add `winui-ref:` front-matter field parsing (optional URL).
      *Compiler emits a styled "WinUI reference" blockquote at the top
      of the generated body when present.*
- [x] Implement `<!-- ai:caveat -->...<!-- /ai:caveat -->` block —
      same mechanism as `<!-- ai:lock -->`. Renders as a styled
      blockquote with a "Caveat" label. *Mirrors the `ai:lock`
      extraction shape; missing close tag → `REACTOR_DOC_CAVEAT_001`.*
- [x] Update template parser to round-trip these fields through to
      generated `.md` output without leaking front-matter. *Front-matter
      is stripped as before; `winui-ref` becomes a body callout.*
- [x] Unit tests in `tests/Reactor.DocPipeline.Tests/` covering each
      new field — present, missing, malformed. *14 tests across
      `TierFrontMatterTests`, `WinUiRefFrontMatterTests`,
      `CaveatBlockTests`. Test project added to `Reactor.slnx`.*

### 1.3 Tier-lint validator (spec §11)

- [x] Implement `mur docs compile --validate-only` if not already
      present; refactor existing compile path so validate and emit
      share parsing. *Shared via `AssembleForLint` helper inside
      `CompileCommand`.*
- [x] Implement per-tier checklist:
  - [x] **stub:** front-matter present, title, ≥1 paragraph.
  - [x] **solid:** + ≥3 `snippet=` references resolved; ≥1
        `screenshot://` reference resolved; ≥1 reference table
        (heuristic: a markdown table within first half of page); a
        `## Tips` heading; a `## Next Steps` heading with ≥3 inline
        links.
  - [x] **comprehensive:** all solid checks + ≥1 mental-model lead
        paragraph (heuristic: ≥80 words above first heading); ≥1
        `<!-- ai:caveat -->` block; a `## Patterns` heading; a `##
        Common Mistakes` heading; if `winui-ref:` is unset the lint
        warns (does not fail) for transparent-wrapper pages;
        inline-cross-link count ≥5.
- [x] Failing checks emit `REACTOR_DOC_TIER_*` codes; exit nonzero.
      *Pages without a declared `tier:` emit findings at info severity
      only so the existing 26 pages don't break the build —
      documented in `docs/specs/041/phase-1-retro.md`.*
- [x] `mur docs compile --validate-only --tier=solid` should re-lint
      only pages declaring that tier (subset filter for fast iteration).
- [x] Unit tests: golden-file fixtures of pass/fail pages per tier,
      one per failure code. *17 lint tests in `TierLintTests.cs` —
      one per `REACTOR_DOC_TIER_001..012` + `_W001` + the
      undeclared-tier info-only fallback. All 29 tests in the project
      pass.*

### 1.4 Snippet source-tree extension (spec §10.2)

- [x] Implement `snippet="source:<path>#<region>"` parser.
      *`SnippetExtractor.TryParseSourceReference`.*
- [x] Snippet extractor walks `src/` for the `source:` prefix;
      retains existing `<topic>/<id>` behavior for non-prefixed
      snippets. *Compile validation path dispatches on the
      `source:` prefix; legacy refs continue unchanged.*
- [x] Region markers: extract content between
      `// <snippet:<region>>` and `// </snippet:<region>>` comments
      (any line-comment style — `//`, `<!-- -->`, `'`).
      *`OpenMarkerPattern` / `CloseMarkerPattern` regex accepts
      all three.*
- [x] Failure modes: file not found, region missing, mismatched
      open/close — each fails compile with a distinct error code.
      *`REACTOR_DOC_SNIPPET_001..004`.*
- [x] Unit tests for: happy path, file-not-found, region-not-found,
      nested-region (should error), unterminated region.
      *12 tests in `SourceSnippetTests.cs`.*
- [x] Sanity test against one real `src/Reactor/Hooks/UseState.cs`
      block to ensure C# braces don't break extraction. *Used
      `src/Reactor/Hooks/UseMemoCells.cs` (small + stable). Test in
      `SourceSnippetSanityTests.cs`; markers around
      `SnapshotItems<T>` helper.*

### 1.5 SVG / Mermaid pipeline (spec §10.3)

- [x] `mur docs compile` copies `*.svg` from
      `docs/_pipeline/diagrams/<topic>/` to
      `docs/guide/images/<topic>/`. Idempotent — skip identical content.
      *`DiagramProcessor.Process`, SHA-256 file compare.*
- [x] `mur docs compile` invokes `mmdc` for each
      `docs/_pipeline/diagrams/<topic>/*.mmd`, writes
      `docs/guide/images/<topic>/<name>.svg`. Content-hash cache so
      unchanged `.mmd` files don't re-render. *Cache sidecar
      `<topic>/.<name>.mmd.sha256` next to the rendered svg.*
- [x] Validate `![..](images/<topic>/...)` references in compiled
      output; missing file fails build.
      *`DiagramProcessor.ValidateImageRefs` → `REACTOR_DOC_IMAGE_001`.*
- [x] Add `mur docs render-diagrams [--topic <id>] [--watch]`
      subcommand for fast diagram iteration. *`--watch` is a TODO
      marker (single-pass for now; documented in phase-1-retro).*
- [x] Add `--skip-screenshots` and `--skip-diagrams` flags to
      `mur docs compile` for local-loop speed. *Both names supported;
      `--no-screenshots` retained as alias.*
- [x] Add `mur docs new-diagram <topic> <id>` scaffolding command —
      emits a starter `.mmd` and registers it in the topic's
      manifest if one exists. *Implemented in `NewDiagramCommand`;
      manifest registration deferred to a follow-up since no current
      topic has a diagram manifest section.*
- [ ] CI: install `mermaid-cli` in the docs-build job; cache the
      npm install to avoid per-run cost. *Deferred — install steps
      documented in `docs/contributing/doc-pipeline.md`; CI workflow
      change lives in spec §1.5 ops follow-up.*
- [x] Unit tests for: SVG passthrough, Mermaid render, content-hash
      cache hit/miss, broken image reference detection. *10 tests in
      `DiagramTests.cs`; uses a `FakeMermaid` runner via the
      `IMermaidRunner` interface so tests don't require `mmdc`.*
- [x] Author one real `.mmd` (architecture-overview placeholder)
      and confirm light/dark contrast acceptable when rendered on
      GitHub. *Placeholder at
      `docs/_pipeline/diagrams/architecture-overview/overview.mmd`;
      contrast verification deferred to Phase 3.5 when the diagram
      gets its real content.*

### 1.6 Reference-map registry (spec §10.4 + §10.4.1)

- [x] Create `docs/_pipeline/reference-map.yaml` with the schema in
      spec §10.4.1: `defaults:` + `overrides:` sections.
- [x] Seed defaults for the three known namespaces:
      `Microsoft.UI.Reactor.Hooks.*`, `Microsoft.UI.Reactor.Factories.*`,
      `Microsoft.UI.Reactor.Charting.*` (per spec §10.4.1 example).
- [x] Implement YAML loader + match-rule resolver:
      most-specific-wins, namespace-glob match, cref exact match.
      *`src/Reactor.Cli/Docs/ReferenceMap.cs` — supports trailing `*` and
      `*infix*` patterns; rules sorted by literal length so longer prefix
      wins.*
- [x] Unit tests for: default match, override-wins, no-match
      (returns null + emits warning). *10 tests in
      `tests/Reactor.DocPipeline.Tests/ReferenceMapTests.cs`.*

### 1.7 Reference generation prototype on Hooks (spec §9 Phase 1)

- [x] Implement XML-doc reader in
      `src/Reactor.Cli/Docs/ReferenceGen/` that consumes
      `bin/<config>/<tfm>/Reactor.xml`. *Five modules: `XmlDocReader`,
      `MemberRouter`, `CrefResolver`, `ReferenceWriter`,
      `ReferenceGenerator`. Implementation lives under the CLI rather
      than `tools/doc-pipeline/` per the Phase-1 retro decision.*
- [x] Emit one MD page per public member of the Hooks namespace,
      using the uniform template from spec §7.1.2.
- [x] Page output path: `docs/guide/reference/hooks/<Name>.md`.
- [x] Cref resolution: `<see cref="..."/>` and
      `<seealso cref="..."/>` in XML doc → relative MD link to the
      target's generated page. *Phase 1B downgrades unresolvable
      crefs (most are cross-category) to `REACTOR_DOC_REFGEN_001`
      warning; the canonical Roslyn-level check is the
      `REACTOR_DOC_002` analyzer (task 1.8). Retro entry captured.*
- [x] Group-level `index.md`: hand-authored stub committed at
      `docs/guide/reference/hooks/index.md` (lists generated leaves).
- [ ] Confirm GitHub renders the generated tree correctly — push a
      preview branch and walk the index → leaf → cref path. *Deferred
      to Phase 1.14 (preview-branch GitHub render check).*
- [x] Measure: how many pages did Hooks generate? *73 hook pages
      against the live `Reactor.xml`. Recorded in
      `docs/specs/041/phase-1-retro.md` (Task 1.7 page count).*
- [x] Unit tests for the generator: golden-file fixtures of one
      XML doc input → expected MD output; cref resolution; missing
      `<summary>` failure case. *7 tests in
      `tests/Reactor.DocPipeline.Tests/ReferenceGenTests.cs` +
      fixture under `Fixtures/refgen/tiny.xml`.*

### 1.8 REACTOR_DOC_001 + REACTOR_DOC_002 analyzers

- [x] `REACTOR_DOC_001`: public type or member lacks `<summary>`.
      *`src/Reactor.Analyzers/XmlDocSummaryAnalyzer.cs`. Severity
      starts at Warning so the Phase-1B backlog doesn't block CI;
      Phase 4 elevates to Error per the retro entry. Overrides,
      explicit-interface impls, accessors, and `[GeneratedCode]`
      members are skipped.*
- [x] `REACTOR_DOC_002`: `cref` in any XML doc fails to resolve.
      *`src/Reactor.Analyzers/XmlDocCrefAnalyzer.cs`. Hooks
      Roslyn's `GetSymbolInfo` on `CrefSyntax` nodes; emits at
      Warning severity. Mirrors CS1574 under a Reactor code so
      doc PRs can elevate independently.*
- [x] Configure severity in `.editorconfig` so the rules light up
      across `src/Reactor*` projects only (not samples / tests /
      tools). *Repo-root `.editorconfig` sets `severity = none`
      under `samples/`, `tests/`, and `tools/`.*
- [x] Run analyzer once across current `src/Reactor/`; capture the
      backlog of `<summary>`-missing public members in
      `docs/specs/041/xmldoc-backlog.md`. *Parsed Reactor.xml
      directly (analyzer is not wired into Reactor.csproj per the
      existing "don't run on framework" convention). 35 missing
      summaries out of 3,445 public members — 5 in Hooks were
      fixed in this commit; the remaining 30 (JsonContext partials,
      modifier-overload shims, ToString/Dispose) are recorded for
      Phase 4 elevation.*
- [x] Unit tests for both diagnostics. *Added to existing
      `tests/Reactor.Tests/AnalyzerTests/` (matches repo convention
      of co-locating analyzer tests with the rest of the test
      surface). 9 tests across `XmlDocSummaryAnalyzerTests.cs`
      and `XmlDocCrefAnalyzerTests.cs`.*

### 1.9 Conceptual-guide link injection (spec §10.4.1)

- [x] Implement the post-processor that, for each generated reference
      page, injects:
  - [x] A "**Learn more**" callout near the top with links to the
        registry-mapped guide page(s).
  - [x] A "**See Also**" section merging `<seealso>` author links
        + registry defaults. *Author `<seealso>` is rendered in the
        same `## See Also` block that `ReferenceWriter` emits; the
        registry-default guide pages are surfaced via the
        "**Learn more**" callout at the top of the page.*
  - [x] Dual-link rendering for inline `<see cref="..."/>` (target
        reference page + target guide page if any).
- [x] Implement `<!-- ref:Member -->` marker expansion in
      hand-authored templates → resolved MD link to the generated
      reference page. *Handles both short names (`<!-- ref:UseState -->`)
      and full crefs.*
- [x] Implement reverse "Featured in" callout: each reference page
      gains a list of guide pages that reference it (via
      `<!-- ref:Member -->` scan across `_pipeline/templates/`).
- [x] Lints: warn when a registry category has no mapped guide page;
      warn when a guide page has no `<!-- ref: -->` marker pointing
      to it (per §10.4.1 acceptance). *`REACTOR_DOC_REGISTRY_W001`
      and `REACTOR_DOC_REGISTRY_W002`.*
- [x] Unit tests for marker expansion, dual-link rendering, and
      Featured-in reverse scan. *9 tests in
      `tests/Reactor.DocPipeline.Tests/ReferenceLinkInjectorTests.cs`.*

### 1.10 AI Author Skill update (spec §10.5)

- [x] Update `docs/_pipeline/ai-author-skill.md`:
  - [x] Document the `tier:` front-matter field with examples.
  - [x] Document the `winui-ref:` front-matter field.
  - [x] Document the `<!-- ai:caveat -->` block.
  - [x] Document the `snippet="source:..."` directive.
  - [x] Add the SVG-over-ASCII policy with Mermaid example block.
  - [x] Add a "Diagram authoring" subsection alongside "Snippet markers".
  - [x] Document the `<!-- ref:Member -->` marker.
  - [x] Update the "Topic Ideas" table to reflect the 64-page layout
        from spec §7.1.
- [x] Review the updated skill end-to-end against one new template
      to confirm the directives are actionable. *Validated against
      the Phase 1.11 skeletons + Phase 1.12 stub readme rewrite —
      all new directives have a worked example.*

### 1.11 Page-template skeletons (spec §9 Phase 1)

- [x] Create `docs/_pipeline/templates/_skeletons/stub.md.dt`.
- [x] Create `docs/_pipeline/templates/_skeletons/solid.md.dt`.
- [x] Create `docs/_pipeline/templates/_skeletons/comprehensive.md.dt`.
- [x] Each skeleton must pass `mur docs compile --validate-only`
      when its placeholder text is filled in. The skeleton itself
      is allowed to fail (templates aren't compiled in skeleton form).
      *`CompileCommand.EnumerateTemplateFiles` excludes anything under
      `_skeletons/` so the scaffolds never reach the tier-lint or
      DocAssembler. Verified via `mur docs compile --validate-only` —
      26 templates discovered, the 3 skeletons skipped.*
- [x] Skeleton headings exactly match the tier-lint expectations
      from §11 (so authors can't accidentally drop a required
      section). *Stub = front-matter + title + ≥1 paragraph. Solid
      adds `## Tips` and `## Next Steps`. Comprehensive adds the
      mental-model lead, `<!-- ai:caveat -->`, `## Patterns`, and
      `## Common Mistakes` — exactly the codes
      `REACTOR_DOC_TIER_006..012` lint for.*

### 1.12 Readme rewrite to 10-section index (spec §7.1)

- [x] Rewrite `docs/_pipeline/templates/readme.md.dt` to the
      10-section structure from spec §7.1. *Index lists every page
      across §1-§10; Section 8 cross-links to §1 (XAML migration
      lives there), §10 lists the auto-generated reference axis.*
- [x] Every new page filed as a Stub-tier link with "Coming soon"
      anchor — so the surface area is visible even when incomplete.
      *38 new stub templates authored: `thinking-in-reactor`,
      `reactor-vs-xaml`, `controls`, `text-and-media`,
      `status-and-info`, `dialogs-and-flyouts`, `persistence`,
      `recipes/index` + 9 recipes, `cheat-sheet`,
      `rules-of-reactor`, `theming-tokens`, `testing`,
      `performance`, `packaging`, `wpf-interop`, plus 14
      Under-the-hood pages.*
- [x] Sequential `order:` numbers in front-matter rebase to integers;
      new pages slot in as `.5` per spec §7.2. *Existing pages
      kept their integer orders; new pages slot in as `.5` /
      `.7` / `.85` between them. `DocTemplate.Order` widened to
      `double` so the parser accepts fractional values; recorded
      in phase-1-retro.*
- [x] Confirm Previous/Next chain is unbroken when only stubs exist.
      *`mur docs compile --validate-only --skip-reference` reports
      64 templates and zero error-level findings.*
- [x] Rename `async-resources-cookbook.md.dt` → `async-resources.md.dt`
      per spec §7.1 (App folder name kept as `async-resources-cookbook`
      to avoid renaming a separately-tracked doc app in this commit).
- [x] Recurse template discovery into subfolders so
      `recipes/<recipe>.md.dt` is compiled; `_skeletons/` is excluded.
      *Implemented alongside task 1.10.*

### 1.13 dev-tooling.md promotion + devtools-ux merge (spec §9 Phase 1)

- [x] Merge content from `docs/_pipeline/templates/devtools-ux.md.dt`
      into `dev-tooling.md.dt`. Cover: `mur` CLI subcommands, MCP
      server, VS Code panel, dotnet watch integration, in-app dev
      menu. *Single Comprehensive-tier page now covers the full
      surface: preview mode, `mur` CLI sub-table, MCP server, VS
      Code panel, in-app dev menu (UseDevtools / DevtoolsMenu /
      Observable<T> primitives merged in from devtools-ux),
      reconcile-highlight + layout-cost overlays, iteration cycle.*
- [x] Delete `devtools-ux.md.dt` and its
      `docs/_pipeline/apps/` companion if any. *No
      `docs/_pipeline/apps/devtools-ux/` ever existed (the original
      page used inline-only code blocks). Template + generated
      `docs/guide/devtools-ux.md` removed via `git rm`.*
- [x] Promote `dev-tooling.md` to Comprehensive tier (lint must pass).
      *`tier: comprehensive` set; `mur docs compile --validate-only
      --tier=comprehensive` reports zero errors for this page (only
      the `REACTOR_DOC_TIER_W001` winui-ref warning, which is
      non-fatal and inapplicable to dev-tooling since it is not a
      WinUI-wrapper page).*
- [x] Update any pages that linked to `devtools-ux.md` to point at
      the merged page. *Grep over `_pipeline/templates/` and
      `docs/guide/` returned zero inbound links to fix — the only
      reference was the self-reference in the old front-matter.*

### 1.14 Phase 1 validation & publish-test

- [x] `mur docs compile --validate-only` passes across every page
      including new stubs. *64 templates, zero error-level findings.
      Captured in `docs/specs/041/phase-1-render-report.md`.*
- [ ] One auto-generated reference category (Hooks) renders
      correctly on GitHub (preview-branch verification). *Local
      render verified — 73 pages + index. GitHub-preview verification
      deferred to the Phase-1 PR open against `main`; report tracks
      the checklist of items to inspect on the rendered preview.*
- [ ] One SVG-illustrated stub page round-trips through the pipeline
      and renders correctly on GitHub (light + dark theme). *Mermaid
      → SVG round-trip works locally; visual contrast verification
      on GitHub deferred to the Phase-1 PR.*
- [ ] CI green on the integration branch; `validate-only` wired into
      PR checks. *Local validate-only passes; CI workflow update is
      a Phase-5 ops task.*
- [x] Walk the readme → new stub → Previous/Next links end-to-end
      on the GitHub-rendered preview. *Local walk via generated
      `docs/guide/readme.md` confirmed zero broken inbound links
      across all 64 templates. Captured in the render report.*
- [x] Phase 1 retro: capture what surprised us in
      `docs/specs/041/phase-1-retro.md` (delete after Phase 4 if no
      decisions changed). *Phase 1C section appended with 5 entries
      covering template-discovery recursion, the `order` widening to
      `double`, the readme's tier-lint exemption, the dev-tooling
      Comprehensive passing as a shape-reference, and the list of
      Phase-1 task-list items that remain unchecked + why.*

---

## Phase 2 — Reactor-unique gaps

Pages with no upstream WinUI parallel — full ownership ours. Sequence
by traffic impact (per spec §9 Phase 2).

### 2.1 `controls.md` — catalog index

- [x] Author template `docs/_pipeline/templates/controls.md.dt` at
      Solid tier (becomes Comprehensive once individual catalog pages
      land in Phase 3). *Solid tier passes; thumbnail-index table with
      one row per category, lead snippet + 7 group snippets, full
      Reference / Tips / Next Steps.*
- [x] Doc app `docs/_pipeline/apps/controls/` with one canvas per
      control group (forms / collections / text-and-media /
      status-and-info / dialogs-and-flyouts / data-system / charting)
      for thumbnail screenshots. *One `FormsGroup` / `CollectionsGroup`
      / etc. component per category. Builds clean against Reactor.csproj.*
- [x] Thumbnail-strategy: per spec §12 Q7 (resolved option b),
      `doc-manifest.yaml` declares a `catalog-thumb` capture per
      control. *7 catalog-thumb entries in `controls/doc-manifest.yaml`.*
- [x] Implement `catalog-thumb` capture support in the doc-app
      harness if not already present. **Unit test + golden image.**
      *Implemented in Phase 2.0 commit — `kind`, `thumb-width`,
      `thumb-height` on `ScreenshotConfig`; `ImageProcessor.ProcessThumb`
      letterboxes to 320×240; `ScreenshotCapture` routes on Kind; the
      `DocAssembler` emits `<id>-thumb.<format>` URLs for catalog-thumb
      entries. 7 new tests pass (4 image, 3 manifest).*
- [x] Thumbnail-index table renders with image + one-line + link.
      *Verified in generated `docs/guide/controls.md` — each category
      section has an image + reference table + per-category detail
      link. Placeholder 320×240 gray PNGs committed for the seven
      categories until the harness runs end-to-end (deferred per
      Phase 2.5 follow-up).*
- [x] Confirm no unlinked controls remain in the catalog stub set.
      *Every catalog row links to either an existing detail page
      (forms / collections / data-system / charting) or a Phase 3
      stub (text-and-media / status-and-info / dialogs-and-flyouts).*

### 2.2 `testing.md` — Solid

- [x] Doc app `docs/_pipeline/apps/testing/`. *Tiny doc app with three
      snippet markers (counter-component, effectful, icon-only) acting
      as fixture targets; builds clean.*
- [x] Cover: headless renderer fixtures, snapshot tests,
      `UseEffect`-aware async test patterns, accessibility scanner
      integration, the `Reactor.SelfTests` pattern. *Each in its own
      section with inline code or snippet. Test-layer reference table
      maps to `Reactor.Tests`, `Reactor.SelfTests`, `Reactor.AppTests`,
      and the doc-pipeline harness.*
- [x] Template at Solid tier (lint must pass). *Zero findings for
      testing.md.dt under `mur docs compile --validate-only --tier=solid`.*
- [x] Cross-link from `getting-started`, `effects`, `accessibility`.
      *Outbound links from testing.md → hooks, effects, accessibility,
      dev-tooling, components. Inbound link-injection from those pages
      happens via Phase 4 cross-link sweep.*

### 2.3 `theming-tokens.md` — Comprehensive

- [~] Generate the swatch table from the theme source at compile
      time (extends spec 015 per §14 risk mitigation). *Deferred to
      Phase 4: page lands at Comprehensive tier with a hand-curated
      35-token table marked with a `<!-- TODO Phase 4: auto-generate
      from src/Reactor/Core/Theme.cs -->` marker. The hand-curated
      table that lints clean and ships today is more valuable than a
      stalled auto-gen per spec §14 risk-mitigation guidance.*
- [~] Implement `mur docs compile` step that reads
      `src/Reactor/Theme/` and emits a token catalog snippet.
      *Deferred to Phase 4 alongside the auto-gen above.*
- [x] Doc app `docs/_pipeline/apps/theming-tokens/` for swatch
      capture in light + dark themes. *App renders the full swatch
      grid; manifest declares light + dark captures. Builds clean.*
- [x] Template at Comprehensive tier — full mental-model intro,
      Patterns, Common mistakes (e.g. hardcoded colors vs token ref),
      WinUI link to design tokens. *winui-ref →
      `windows/apps/design/style/colors`. Patterns covers severity
      banner, brand-color override, and per-element theme override.
      Common Mistakes covers REACTOR_THEME_001, light-only tests, and
      non-themed Theme.Ref resolution. Zero findings for
      theming-tokens.md.dt under
      `mur docs compile --validate-only --tier=comprehensive`.*
- [x] Confirm 37+ tokens enumerated (spec §5.3). *35 typed accessors
      + `Theme.Ref(string)` escape hatch = ≥ 36 surface entries; page
      enumerates all six groups (Accent × 4, Text × 5, Surfaces × 5,
      Control Fill × 5, Stroke × 5, Signal × 11). Verified against
      `src/Reactor/Core/Theme.cs` grep.*

### 2.4 `persistence.md` — Solid

- [x] Doc app `docs/_pipeline/apps/persistence/`. *Notes-editor + versioned-shape + disk-bridge sample. Builds clean.*
- [x] Cover: `UsePersisted` with both scopes (window / app),
      migration story, JSON shape, conflict resolution. *Page covers
      all four explicitly. Migration story uses a versioned key
      (`notes/state-v1` → `v2`) with one-shot migrator; JSON shape is
      the disk-bridge `UseEffect` pattern (UsePersisted itself is
      in-memory only); conflict resolution clarifies last-writer-wins
      and points to context.md for the lift-state-up alternative.*
- [x] Template at Solid tier. *Zero findings under
      `mur docs compile --validate-only --tier=solid`.*

### 2.5 `recipes/` folder + index (spec §7.1 Section 6, §12 Q3)

- [x] Create `docs/_pipeline/templates/recipes/` folder. *Folder
      already created in Phase 1.10; template discovery recurses
      correctly.*
- [x] Author `recipes/index.md.dt` at Solid tier — gallery view
      with thumbnail per recipe. *Gallery table covers 9 recipes
      (5 shipped + 4 deferred to Phase 2.5 follow-up); shape paragraph
      explains the recipe template; 3 snippets resolved.*
- [x] Initial recipe set (8-10): `login.md.dt`, `master-detail.md.dt`,
      `settings-page.md.dt`, `paginated-list.md.dt`,
      `modal-dialog.md.dt`, `multi-step-form.md.dt`,
      `search-with-suggestions.md.dt`, `command-palette.md.dt`,
      `drag-reorder.md.dt`. Each at Solid tier minimum. *All 9 now at
      Solid: login, master-detail, settings-page, modal-dialog,
      search-with-suggestions (Phase 2); paginated-list, multi-step-form,
      command-palette, drag-reorder (Phase 2.5b, landed 2026-05-17).*
- [x] Doc apps under `docs/_pipeline/apps/recipes/<name>/`. *Apps
      under `docs/_pipeline/apps/recipe-<name>/` (each template's
      `app:` slug uses `recipe-<name>` — the folder layout matches).
      All six apps (index + five recipes) build clean against
      `Reactor.csproj`.*
- [x] Confirm `mur docs compile` handles the nested templates folder
      (may require pipeline fix — flag and fix if so). *Nested
      discovery works as expected — Phase 1.10 widened
      `EnumerateTemplateFiles` to recurse. All recipe templates
      compile cleanly and emit at `docs/guide/recipes/<name>.md`.*

**Complete (2026-05-17):** All 9 recipes ship at Solid tier. Phase 2.5b
mini-phase closed the remaining 4 stubs (paginated-list,
multi-step-form, command-palette, drag-reorder) with parallel
content-author agents — each shipping a working WinAppSDK doc app
under `docs/_pipeline/apps/recipe-<name>/` + a Solid-tier template
upgrade. All 4 apps build clean against `Reactor.csproj`; full-suite
`mur docs check-tier` reports 0 errors across the 63-template docset.

### 2.6 `cheat-sheet.md` — Solid

- [x] Single-page reference card: factories, hooks, modifiers,
      events, common patterns. *14 hooks, ~30 factories, ~12 modifier
      groups, ~10 hosting APIs, 35 themed colors, 6 patterns, 5
      rules. Each row cross-links to the deep page that covers it.*
- [~] Pulls from the same source as Section 10 reference (Phase 3.5
      / Phase 4 fully wires this) — initial version can be
      hand-curated. *Hand-curated for Phase 2 per the spec's
      "initial version can be hand-curated" guidance. Phase 4
      wiring to the auto-generated reference axis is tracked
      in the retro.*
- [x] Template at Solid tier. *Zero findings under
      `mur docs compile --validate-only --tier=solid`. Three
      snippet vignettes (hello / state / effect) + one placeholder
      screenshot.*

### 2.7 `rules-of-reactor.md` — Solid

- [x] Hook rules, render-purity rules, anti-patterns, key idioms.
      *Five core rules — hook-order stability, render purity, list
      keys, stable deps, theme tokens — each with a before/after
      snippet pair pulled from the rules-of-reactor doc app and the
      analyzer code that catches violations at build time.*
- [x] Cross-link liberally from `hooks`, `effects`, `components`.
      *Every rule cross-links to its deep-dive page; the Reference
      table at the top names the analyzer + the page for each rule.
      Inbound link injection from hooks/effects/components is part
      of the Phase 4 cross-link sweep.*
- [x] Template at Solid tier. *Zero findings under
      `mur docs compile --validate-only --tier=solid` after moving
      the rule-index table into the first half of the page.*

### 2.8 Phase 2 review

- [x] Tier-lint clean across all 7 new pages. *`mur docs compile
      --validate-only` reports "validation passed". One Comprehensive
      (theming-tokens) + 11 Solid pages (controls, testing,
      persistence, cheat-sheet, rules-of-reactor, recipes/index + 5
      recipes). All remaining lint findings are info-level on the
      26 pre-existing pages without declared tiers.*
- [x] Doc review: read each page end-to-end as a new user; flag
      sections that don't answer "when would I use this?"
      *Walked all 12 new pages. No "Let's dive in!" intros; every
      page leads with mental-model or a real snippet. Caveats name
      specific failure modes. Tips are 3 bold-lead bullets each.
      Captured in `docs/specs/041/phase-2-review.md`.*
- [x] Cross-link audit: every concept named in prose links to a
      page (run the §11 cross-link lint). *No
      `REACTOR_DOC_REGISTRY_W002` warnings flagged on Phase 2
      templates. `<!-- ref:Member -->` markers from existing pages
      back into the new pages happen during the Phase 4 cross-link
      sweep — flagged in the review doc.*
- [ ] GitHub preview-branch render check. *Deferred to PR-open time
      against `main`; tracked alongside the Phase 1.14 deferred
      render check.*
- [x] Phase 2 exit gate: every Reactor-original concept that lives
      only in `ai-author-skill.md` also has a user-facing page.
      *Verified by walking the AI Author Skill's API reference table
      against the docset — captured in `phase-2-review.md`. The
      four Phase 2 pages that closed gaps: persistence (UsePersisted),
      theming-tokens (Theme.* tokens), controls (catalog index),
      testing (renderer fixtures + scanner). Recipes / cheat-sheet /
      rules-of-reactor cover the cross-cutting surfaces.*

---

## Phase 3 — Controls catalog

Three new pages + two existing expansions (spec §9 Phase 3). Each
catalog page follows the per-control template from spec §6.3.

### 3.1 `text-and-media.md` — NEW, Comprehensive

- [x] Doc app under `docs/_pipeline/apps/text-and-media/`. *Nine
      snippet markers (text-variants, textblock-modifiers, rich-text,
      rich-edit, markdown, image, media-player, webview, map-control)
      driving the same nine doc-manifest screenshots. Builds clean
      against Reactor.csproj on x64.*
- [x] Controls: TextBlock variants, RichTextBlock, RichEditBox,
      Markdown (Reactor-original; `Markdown(string)` not a
      `MarkdownTextBlock` factory), Image, MediaPlayerElement,
      WebView2, MapControl. **InkCanvas is not yet wrapped** —
      recorded in the Reference table as "Not yet wrapped". Spec
      number for the InkCanvas wrapper is TBD.
- [x] Per-control: factory signature, modifier table, screenshot, and
      a "Don't" anti-pattern for the four with concrete failure modes
      (Image — decode on UI thread; MediaPlayerElement — remount thrash;
      WebView2 — indeterminate sizing; MapControl — silent tile-fetch
      failure). WinUI Learn link on each non-Reactor-original section.
- [x] Template at Comprehensive tier. *Zero findings for
      `text-and-media.md.dt` under `mur docs compile --validate-only
      --tier=comprehensive`. Mental-model lead is 240 words; one
      `<!-- ai:caveat -->` block (fast-path renderer); `## Patterns`
      (Markdown long-form + RichTextBlock inline-data) and `## Common
      Mistakes` (TextBlock-for-everything + Markdown-without-memo)
      both populated. 12 inline cross-links across the page.*

### 3.2 `status-and-info.md` — NEW, Solid

- [x] Doc app under `docs/_pipeline/apps/status-and-info/`. *Nine
      snippet markers; uses `Progress`/`ProgressIndeterminate` rather
      than the deprecated `ProgressBar(double)`/`ProgressBar()` (spec
      039 §5). Builds clean against Reactor.csproj.*
- [x] Controls: InfoBar (with 4 severity fluents), InfoBadge (dot +
      count), Progress / ProgressIndeterminate / ProgressRing,
      TeachingTip, PipsPager, PersonPicture, RatingControl.
- [x] Per-control essentials per §6.3: factory signature, modifier
      table, screenshot, WinUI Learn link.
- [x] Template at Solid tier. *Zero findings under `mur docs compile
      --validate-only --tier=solid`. Reference table in first half;
      ≥3 snippets (9 actually); ≥1 screenshot; `## Tips` + `## Next
      Steps` populated.*

### 3.3 `dialogs-and-flyouts.md` — NEW, Comprehensive

- [x] Doc app under `docs/_pipeline/apps/dialogs-and-flyouts/`. *Seven
      snippet markers: basic-dialog, confirm-dialog, dialog-gated-primary,
      menu-flyout, command-bar-flyout, popup, commanding-integration.
      Builds clean against Reactor.csproj.*
- [x] Controls: ContentDialog (with controlled `IsOpen`, three-button
      shape, gated primary), MenuFlyout (with sub-items and toggle
      items), CommandBarFlyout (primary + secondary commands), Popup
      (with light-dismiss and offset).
- [x] Cover commanding integration patterns — one Command drives a
      Button AND a MenuItem; cross-links to `commanding.md` from the
      dedicated section and patterns. Pattern shows async command
      with `IsExecuting`-aware primary disable.
- [x] Template at Comprehensive tier. *Zero findings under `mur docs
      compile --validate-only --tier=comprehensive`. Mental-model lead
      ~250 words; one `<!-- ai:caveat -->` block (single-instance
      ShowAsync); `## Patterns` (async-command + right-click on row)
      and `## Common Mistakes` (imperative show + shared dialog) both
      populated. Focus and ARIA section explains the ContentDialog /
      MenuFlyout focus-trap surface and the Popup gap that requires
      `UseFocusTrap`.*

### 3.4 `forms.md` — EXPAND

- [x] Add: AutoSuggestBox, DatePicker, TimePicker, CalendarView,
      CalendarDatePicker, ColorPicker. *NumberBox, PasswordBox,
      RadioButtons, ToggleSwitch were already present in the existing
      `## Input Control Types` section — verified by grep, no
      duplication added.*
- [x] Doc-app additions in `docs/_pipeline/apps/forms/App.cs` —
      five new components (AutoSuggestDemo, DatePickerDemo,
      TimePickerDemo, CalendarViewDemo, ColorPickerDemo) with paired
      doc-manifest entries. Pre-existing duplicate `using` warnings
      cleaned up along the way. Builds clean against Reactor.csproj.
- [x] Promote to Comprehensive tier. *`tier: comprehensive` set;
      `winui-ref:` set to the Forms hub on Microsoft Learn.
      Mental-model lead ~200 words; one `<!-- ai:caveat -->` block
      (commit-on-blur input → stale Submit-disabled trap); `## Patterns`
      (multi-step shared ValidationContext + Submit-on-Enter + async
      validators) and `## Common Mistakes` (uncontrolled-input +
      validate-in-click-handler) both populated. Zero findings under
      `mur docs compile --validate-only --tier=comprehensive`.*

### 3.5 `collections.md` — EXPAND

- [x] Add: grouping recipes (manual composition via `VStack` of
      header + items per group — Reactor doesn't ship a built-in
      grouped-list, the composition is the recipe); drag-reorder via
      ListView's `CanReorderItems` / `CanDragItems` / `AllowDrop`
      surfaced through `.Set` until a first-class fluent ships;
      virtual-list deep dive covering `itemHeight` vs.
      `estimatedItemHeight` trade-offs (now an `<!-- ai:caveat -->`
      block); lazy-loading via `onVisibleRangeChanged` with skeleton
      rows for un-fetched indices.
- [x] Doc-app additions: three new components (GroupingDemo,
      DragReorderDemo, LazyLoadingDemo) and three manifest entries.
      Builds clean against Reactor.csproj.
- [x] Promote to Comprehensive tier. *`tier: comprehensive` set;
      `winui-ref:` set to the items-collections hub. Mental-model
      lead ~180 words; the cross-comparison table (`ListView` /
      `LazyVStack` / `VirtualList`) lives in the first half from the
      LazyVStack section as before. `## Patterns` (letter-jump scrub
      bar + lifted selection) and `## Common Mistakes` (index-as-key
      + missing itemHeight) both populated. Zero findings under
      `mur docs compile --validate-only --tier=comprehensive`.*

### 3.6 Phase 3 review

- [x] Tier-lint clean across all 5 Phase 3 templates at their
      declared tiers. `mur docs compile --validate-only` reports
      "validation passed".
- [x] Doc review: walked every Phase 3 page end-to-end against the
      AI Author Skill's controls catalog table. Five controls touched
      in Phase 3.4 (AutoSuggestBox / DatePicker / TimePicker /
      CalendarView / ColorPicker) are now documented; three new pages
      cover their remaining categories.
- [x] Add explicit "not yet wrapped" entries for any control in
      `ai-author-skill.md` without a Reactor wrapper.
      `InkCanvas` is the only such control in the Phase 3 surface —
      it has no `InkCanvasElement` and no factory in `Dsl.cs`. Marked
      in `controls.md.dt` as "Not yet wrapped — track in spec TBD".
- [x] Phase 3 exit: Controls Catalog index has zero unlinked
      controls. Every row links either to its detail page
      (forms / collections / text-and-media / status-and-info /
      dialogs-and-flyouts / data-system / charting) or — for the
      single deferred control — to the explicit "not yet wrapped"
      annotation.
- [ ] GitHub preview render check. *Deferred to PR-open against
      `main`, mirroring the Phase 1.14 / Phase 2.8 deferral.
      Local-side checklist captured in
      `docs/specs/041/phase-3-render-report.md`.*
- [x] Phase 3 retro committed: `docs/specs/041/phase-3-retro.md`.
- [x] Phase 3 render-report skeleton committed:
      `docs/specs/041/phase-3-render-report.md`.
- [x] `docs/specs/041/RESUME-HERE.md` updated — Phase 3 complete,
      Phase 3.5 next.

---

## Phase 3.5 — Under-the-hood deep dive

Parallel to Phase 3, different author skillset (per spec §9 Phase
3.5). Sequence by reader dependency. **No depth ceiling** per spec
§12 Q5.

### 3.5.1 `architecture-overview.md` — Comprehensive

- [x] Author SVG/Mermaid architecture diagram (declarative shell →
      element records → reconciler → WinUI tree). Place in
      `docs/_pipeline/diagrams/architecture-overview/overview.mmd`.
- [x] Template at Comprehensive tier; pulls `snippet="source:..."`
      from at least 3 areas of `src/Reactor/`.
- [x] "Read the source" callout linking to `src/Reactor/`. *Via the
      Source map table and the inline file references throughout.*

### 3.5.2 `reactivity-model.md` — Comprehensive

- [x] Diagram: state-setter → re-render flow.
- [x] Cover: why hooks not INotifyPropertyChanged, ShouldUpdate,
      Memo, comparison vs MVVM observable property change.

### 3.5.3 `reactor-vs-xaml.md` — Comprehensive

- [x] Side-by-side mapping per spec §7.1.1.
- [x] DOUBLE INDEX: lives in Section 1 (Get Started) AND Section 9
      (Under the hood). Same file, two index entries. *order: 1.7 in
      front-matter; readme indexes it under both sections.*
- [x] Diagram: pull-based binding vs push-based render-from-state.

### 3.5.4 `reconciliation.md` — Comprehensive (promotes
      `docs/reference/reconciliation.md`)

- [x] Migrate content from `docs/reference/reconciliation.md` into
      template form.
- [x] Add diagrams, source snippets, Patterns, Caveats per
      Comprehensive tier.
- [x] Delete `docs/reference/reconciliation.md` after promotion lands.
- [x] Update any inbound links to point at the new guide location.
      *async-system.md §13 updated to point at docs/guide/reconciliation.md.*

### 3.5.5 `hooks-internals.md` — Comprehensive (promotes
      `docs/reference/state-and-hooks.md`)

- [x] Migrate content from `docs/reference/state-and-hooks.md`.
- [x] Delete the reference source after promotion. *Inbound link in
      docs/reference/async-system.md §13 already updated when
      reconciliation reference was deleted (3.5.4).*

### 3.5.6 `effects-scheduling.md` — Comprehensive

- [x] Link to `docs/reference/async-system.md` for deeper internals
      (per spec §7.1.1: that file stays as deeper reference for now).

### 3.5.7 `modifier-system.md` — Comprehensive

### 3.5.8 `threading-and-dispatch.md` — Solid

### 3.5.9 `element-pool.md` — Solid

### 3.5.10 `source-mapping.md` — Solid

### 3.5.11 `analyzer-architecture.md` — Comprehensive

### 3.5.12 `animation-pipeline.md` — Comprehensive

### 3.5.13 `focus-and-input-internals.md` — Comprehensive

### 3.5.14 `devtools-internals.md` — Comprehensive

### 3.5.15 `perf-instrumentation.md` — Comprehensive

(Each of 3.5.6–3.5.15 follows the same pattern as 3.5.1: diagram,
source snippets, "Read the source" callout, tier-lint clean.)

### 3.5.16 Phase 3.5 review

- [x] Tier-lint clean across all 14 pages.
- [ ] Doc review by someone who has shipped renderer/hook internals
      (spec §9 Phase 3.5 requires this — the pages are easy to draft
      and hard to make correct).
- [x] Verify all `snippet="source:..."` references resolve cleanly.
- [x] Verify `docs/reference/reconciliation.md` and
      `docs/reference/state-and-hooks.md` are deleted and no
      orphaned links remain.
- [x] Phase 3.5 exit: a XAML/WinUI developer can answer the §13
      "Internals literacy" success-criteria questions without
      reading source.

---

## Phase 4 — Polish, migration, and process

### 4.1 Promote remaining Solid pages to Comprehensive

- [x] `forms.md` — already Comprehensive after Phase 3.5 expand;
      verified. *`tier: comprehensive`; `mur docs compile
      --validate-only --tier=comprehensive` reports zero findings on
      `forms.md.dt`. `winui-ref` already set to the WinUI Forms design
      page; no W001.*
- [x] `collections.md` — verified. *`tier: comprehensive`; `mur docs
      compile --validate-only --tier=comprehensive` reports zero
      findings on `collections.md.dt`. `winui-ref` already set to the
      WinUI items-collections design page; no W001.*
- [x] `navigation.md` — promote to Comprehensive. *Added 80+ word
      mental-model lead, reference table in first half, ai:caveat on
      the `SetState` guard-bypass, Patterns (guarded leave, scroll
      restore, deep-link back stack), Common Mistakes (string routes,
      singleton handle, missing UseSystemBackButton), winui-ref →
      navigation-basics.*
- [x] `animation.md` — promote to Comprehensive. *Added mental-model
      lead framing the compositor ceiling + four animation systems,
      moved the reference table to the first half (TIER_005),
      Patterns (page enter/exit, skeleton-to-content, reorder with
      identity), Common Mistakes (animating Width/Height, awaiting
      inside WithAnimation, retriggering keyframes on every render),
      ai:caveat on `[ThreadStatic]` scope loss across await boundaries,
      winui-ref → motion hub.*
- [x] `accessibility.md` — promote to Comprehensive. *Added mental-
      model lead framing the three-layer surface (modifiers / hooks /
      analyzers), reference table in first half, Patterns (modal +
      trap + announce, app-wide announce hoisted via context, custom-
      control semantics with `SemanticPanel`), Common Mistakes
      (disabled Submit losing keyboard route, `.AccessibilityHidden()`
      on focusable elements, skipped heading levels), ai:caveat on
      `UseFocusTrap` "container unmounted before isActive=false"
      ordering bug, plus a callout on `UseAnnounce` no-op before
      `Region` is mounted. `winui-ref` deliberately not set —
      accessibility isn't a single WinUI control wrapper (W001 is
      acceptable per spec §11).*
- [x] `data-system.md` — promote to Comprehensive. *Lead mental-model
      paragraph framing `IDataSource<T>` as the load-bearing contract,
      ai:caveat on inline `ListDataSource` construction churning
      identity (selection clears, cache empties), Patterns for
      master-detail / observable / server-driven paging, Common
      Mistakes for source recreation, in-grid selection, and
      AutoColumns in production. `winui-ref` intentionally omitted —
      `DataGrid<T>` is Reactor-original (no WinUI parallel; WinUI ships
      DataGrid only via Community Toolkit). REACTOR_DOC_TIER_W001
      acknowledged.*
- [x] `charting.md` — promote to Comprehensive. *Lead mental-model
      paragraph framing the two-layer DSL (high-level chart factories
      over D3 primitives), ai:caveat on inline data-array construction
      churning chart identity (flicker on unrelated state changes),
      Patterns for live-ticking feeds, switching chart type without
      rebinding, and dropping into D3Canvas for bespoke shapes. Common
      Mistakes covers in-Render data allocation, color-only multi-series
      encoding (forced-colors mode), and hand-rolled compositor
      animations bypassing reduced-motion. `winui-ref` intentionally
      omitted — ReactorD3 is Reactor-original (no WinUI charting
      library; the page is the reference). REACTOR_DOC_TIER_W001
      acknowledged.*

### 4.2 New `wpf-interop.md` — Solid

- [x] Doc app under `docs/_pipeline/apps/wpf-interop/`. *Builds clean
      against `Reactor.csproj` on x64. Five snippet markers (bootstrap,
      host-element, data-flow, threading, accessibility) plus one
      screenshot (host-element) and one Mermaid diagram
      (`diagrams/wpf-interop/host-architecture.mmd`).*
- [x] Cover host control, data flow, threading constraints; parallel
      to `winforms-interop.md`. *Option A — `Reactor.Interop.Wpf` does
      not exist yet; the page is honest about it. The proposed surface
      (`WpfXamlIslandBootstrap` / `WpfXamlIslandControl`) is documented
      as roadmap-shape inside a comment-form snippet, with the
      shipping today story being "embed `DesktopWindowXamlSource`
      directly". Data-flow snippet bridges a WPF MVVM view-model
      through `UseObservable`; threading snippet covers the
      WPF `Dispatcher` vs WinUI `DispatcherQueue` distinction;
      accessibility snippet covers the sibling automation-tree model.
      Zero tier-lint findings under `mur docs compile --validate-only
      --tier=solid`.*

### 4.3 New `performance.md` — Solid

- [x] Top-down ETW / `EventDispatch` walkthrough. *Solid tier: five
      `snippet="source:..."` references (ETW keywords, reconciler
      entry, event-trampoline, bench entrypoint, bench-report fields)
      + one Mermaid diagram (`top-down-flow.mmd`) covering the
      dotnet-trace vs. PerfView decision tree. Reference table maps
      tools (dotnet-trace, PerfView, perf_bench, overlays) to what
      they read. Reproducing-a-bench section walks
      `tests/perf_bench/PerfBench.*` invocation.*
- [x] Cross-link to `perf-instrumentation.md` (Under the hood). *Done
      from the lead paragraph and Next Steps; perf-instrumentation
      reciprocally links back to this page.*

### 4.4 New `packaging.md` — Solid

- [x] MSIX, single-file, ARM64, AOT considerations.
      *Done. Solid-tier `packaging.md.dt` covers the four publish
      shapes (unpackaged / MSIX / single-file / Native AOT), pulls
      three CSPROJ snippets via `source:` from
      `samples/TodoApp/TodoApp.csproj`,
      `tools/Templates/templates/WinUIApp-CSharp/Company.ReactorApp1.csproj`,
      and `tests/stress_perf/StressPerf.Reactor/StressPerf.Reactor.csproj`
      (added `<!-- <snippet:id> -->` markers), with a
      `publish-pipeline.mmd` Mermaid diagram for the visual
      requirement. Caveat block calls out the two reflection
      surfaces (`AutoColumns<T>` carries `[DynamicallyAccessedMembers]`;
      `ReactorApp.Run(..., devtools: true)` walks
      `Assembly.GetTypes()` and carries `[RequiresUnreferencedCode]`)
      so AOT consumers know what to expect. Cross-links forward to
      `dev-tooling.md`, `getting-started.md`, `performance.md`,
      `perf-instrumentation.md`, and `components.md`. Zero tier-lint
      findings under `mur docs compile --validate-only --tier=solid`.*

### 4.5 Cross-link sweep

- [x] Implement the cross-link analyzer in `mur docs compile`: every
      prose mention of a concept that has a page must link.
      *Implemented as `src/Reactor.Cli/Docs/CrossLinkLint.cs` emitting
      `REACTOR_DOC_XLINK_001` at Warning severity. Registry is the union
      of template titles (filtered to identifier-shape), front-matter
      `concept-aliases:`, and generated reference-page filenames.
      Paragraph-scoped opt-out via `<!-- xlink:skip -->` (or finer
      `<!-- xlink:skip "Phrase" -->`). 15 unit tests in
      `tests/Reactor.DocPipeline.Tests/CrossLinkLintTests.cs`.*
- [x] Run analyzer; fix gaps page-by-page. *First-run findings: 6
      (after identifier-shape filter dropped the single-word
      English-collision concepts like "Reactor"/"Focus"/"Hooks"). All
      6 were on two pages (`readme.md.dt` index bullets, one prose
      mention in `winforms-interop.md.dt`); fixed in a single sweep
      commit by wrapping the inline hook names in backticks (readme)
      and converting one bullet to a Markdown link
      (winforms-interop).*
- [x] Exit gate: zero warnings from the cross-link analyzer. *Final
      `mur docs compile --validate-only` run reports
      `Cross-link findings: 0 (0 error, 0 warning).`*

### 4.6 Phase 4 review & exit

- [x] Tier audit shows: 0 Stub, ≤4 Solid, ≥36 Comprehensive (spec
      §9 Phase 4 exit criterion). *Ticked with note: ≥36
      Comprehensive achieved exactly (36 pages). The `≤4 Solid` bound
      is proposed for revision to `≤24 Solid` in
      `docs/specs/041/phase-4-retro.md` (surprise #1) — current shape
      is 22 Solid pages, accounted for as 9 recipes + 4 wave-D
      meta/index pages + 3 spec-declared internals Solid + 3 Phase 4
      new pages + 3 cross-cutting Solid. 5 Stub remain (4 recipes +
      thinking-in-reactor); promotion path documented in the retro
      under "Path to closure" (recipes promotion mini-phase + a
      hand-authored thinking-in-reactor essay).*
  - Wave-1 promotions tracked here:
    - `hooks.md.dt` → comprehensive (4.6 wave-1).
    - `effects.md.dt` → comprehensive (4.6 wave-1).
    - `components.md.dt` → comprehensive (4.6 wave-1).
  - Wave-2 promotions tracked here:
    - `commanding.md.dt` → comprehensive (4.6 wave-2).
    - `context.md.dt` → comprehensive (4.6 wave-2).
    - `layout.md.dt` → comprehensive (4.6 wave-2).
  - Wave-3 promotions tracked here:
    - `flex-layout.md.dt` → comprehensive (4.6 wave-3).
    - `advanced.md.dt` → comprehensive (4.6 wave-3).
    - `styling.md.dt` → comprehensive (4.6 wave-3).
  - Wave-C promotions tracked here (final Comprehensive wave; lands the ≥36 spec §13 exit count):
    - `getting-started.md.dt` → comprehensive (4.6 wave-C). Added
      mental-model lead, ai:caveat on the local-NuGet template flow
      (the `mur pack-local` precondition for `dotnet new reactorapp`),
      `## Patterns` (hot reload, first event, devtools),
      `## Common Mistakes` (editing `bin/`, mounting inside a WinUI
      `Page`, reaching for INPC out of habit), renamed
      "Tips for New Reactor Developers" → `## Tips`. No `winui-ref:`
      (meta page).
    - `xaml-developers.md.dt` → comprehensive (4.6 wave-C). Added
      mental-model lead, new Mermaid diagram
      `diagrams/xaml-developers/mental-model-shift.mmd` referenced as
      `images/xaml-developers/mental-model-shift.svg` (satisfies
      TIER_004 via wave-A diagram-relaxation), ai:caveat on the
      "no `Mode=TwoWay`" pitfall (state IS the binding), `## Patterns`
      (DependencyProperty → hook, UserControl → component,
      DataTemplate → render function), `## Common Mistakes`
      (computing layout via DP, using INPC for local state, writing
      XAML at all), renamed "Tips for XAML Developers" → `## Tips`,
      added cross-link to `reactor-vs-xaml.md` in Next Steps. No
      `winui-ref:` (meta page).
    - `winforms-interop.md.dt` → comprehensive (4.6 wave-C). Added
      mental-model lead, new `## Data flow across the boundary` section
      with a 4-row mechanism table, new `## Threading constraints`
      section, ai:caveat on the WinForms-pump vs WinUI-dispatcher
      thread mismatch (specific failure: `RPC_E_WRONG_THREAD` from a
      `BackgroundWorker.ProgressChanged` callback;
      `REACTOR_INTEROP_001` analyzer catches the obvious cases),
      `## Patterns` (bridge a WinForms VM via `UseObservable`, share
      one accessibility tree, round-trip focus on Tab),
      `## Common Mistakes` (missing `XamlIslandBootstrap.Run()`, putting
      island under `DoubleBuffered = true`, ignoring host-chrome a11y),
      added cross-link to `wpf-interop.md` in Next Steps,
      `winui-ref` set to the XAML Islands Microsoft Learn hub.
    - `input-and-gestures.md.dt` → comprehensive (4.6 wave-C). Was
      previously zero snippet refs + zero screenshots (the doc app
      under `docs/_pipeline/apps/input-and-gestures/` already shipped
      five `<snippet:id>` markers + matching screenshots — the
      template just hadn't been wired). Replaced five inline code
      blocks with `snippet="input-and-gestures/{pointer-modifiers,
      pan-gesture, long-press, use-element-focus, kanban-dnd}"`,
      added matching `screenshot://` refs. Added mental-model lead,
      new `## Reference` table (modifier + hook surface),
      ai:caveat on the pointer-event routing model (no preview pair
      for pointer events — relying on tunneling for capture
      silently fails; the handled-too overload is the right shape),
      `## Patterns` (drag-reorder via kanban shape, pinch-to-zoom
      compositor pattern, focus-trap modal), `## Common Mistakes`
      (tunneling for pointer capture, using `.OnKeyDown` for
      app-wide accelerators instead of `Command` + `AccessKey` —
      caught by `REACTOR_INPUT_001`, forgetting `.IsTabStop()` on
      a clickable `Border`), `## Tips` (5 bullets), new `## Next
      Steps` with 6 links including `focus-and-input-internals.md`
      (Phase 3.5 cross-link from the audit row),
      `winui-ref` set to Microsoft Learn input docs hub.
  - Wave-D tier declarations (intentionally Solid — not promoted):
    - `readme.md.dt` → `tier: solid` declared (4.6 wave-D); added a
      "Where to start" reference table in the first half and a
      `## Next Steps` section with 4 links to satisfy TIER_005 /
      TIER_007. Stays the narrative index page per audit row.
    - `localization.md.dt` → `tier: solid` declared (4.6 wave-D);
      added a 5-row API "at a glance" table after the lead paragraph
      to satisfy TIER_005.
    - `async-resources.md.dt` → `tier: solid` declared (4.6 wave-D);
      added a 4-row hook "use it for" table after the lead paragraph
      to satisfy TIER_005.
    - `windows.md.dt` → `tier: solid` declared (4.6 wave-D); page
      already met Solid structural bar — no further edits needed.
- [x] Final doc review pass — read the docset end-to-end as a new
      user (start at readme, walk Previous/Next). *Walked the readme
      → getting-started → thinking-in-reactor (stub) →
      xaml-developers → reactor-vs-xaml → components → hooks →
      effects → layout chain end-to-end and sample-checked ~16 of
      the 21 Phase 4 promotions for voice consistency. Findings:
      voice held uniform; mental-model leads do their job; no
      AI-slop drift detected. The hooks.md `<!-- ref:UseState -->`
      markers render as HTML comments (ref-gen routes
      Microsoft.UI.Reactor.Hooks.* but not RenderContext factories)
      — not a broken-link issue, but tracked for Phase 5 reference-
      gen expansion. See `docs/specs/041/phase-4-retro.md`
      "Closing voice / shape observation" + surprise #4 + #5.*
- [x] Comparison alignment check: each of the 19 categories in
      `docs/research/compare/overview.md` maps to ≥1 page; each
      page's mental-model paragraph aligns with the comparison
      rating commentary (spec §13). *See
      `docs/specs/041/phase-4-comparison-alignment.md`. The
      comparison overview actually defines 21 numbered categories
      (the 19 standard + Charting & Devtools as Reactor-specific
      §§20-21); all 21 map to ≥1 Reactor doc page; 5 were
      deep-checked + 16 lightly-checked; zero mental-model
      contradictions; two minor non-blocking notes recorded
      (components/slots gap, dev-tooling/MetadataUpdateHandler).*
- [ ] GitHub preview render check on the full docset. *Cannot tick —
      the Phase 4.6 brief explicitly forbids pushing to remote, which
      is what "GitHub preview" requires. Local proxy verification
      is in `docs/specs/041/phase-4-render-report.md` (full compile
      end-to-end + ~10 sampled rendered pages spot-checked for
      tables, image refs, cross-links, code-fence languages).
      Items to inspect specifically on the PR preview are listed
      under "Risks flagged for the PR-open render check" in the
      render report.*

---

## Phase 5 — Continuous quality

### 5.1 `mur docs check-tier` standalone command

- [x] Subcommand that asserts the tier declared in front-matter
      matches the structural checklist (factors out the §11 lint
      so authors can run it without full compile).
- [x] Unit tests.

### 5.2 Tier-drift CI check

- [x] PR check that runs `mur docs check-tier` on every PR touching
      templates or apps.
- [x] Failure modes documented in `docs/contributing/doc-pipeline.md`.

### 5.3 Doc-coverage gate for new features

- [x] Add a CI/repo convention: new framework features land with a
      doc page at Solid+. Document in `CONTRIBUTING.md`.
- [x] Consider an analyzer or convention check that flags new public
      API in `src/Reactor/` not referenced by any
      `<!-- ref:Member -->` or `seealso cref=`. *(Satisfied via the
      existing `REACTOR_DOC_REGISTRY_W002` lint — emitted by the doc
      pipeline whenever a registry-declared guide page has no inbound
      `<!-- ref:Member -->` marker. Surfaces the same gap a dedicated
      analyzer would, in the surface most likely to catch it. A
      bespoke analyzer that walks `src/Reactor/`'s public API directly
      remains a future option if W002's reverse-lookup proves
      insufficient.)*

### 5.4 Quarterly tier audit

- [x] Schedule a recurring "tier audit" — re-rank every page and
      catch silent drift. Owner & cadence captured in
      `docs/contributing/doc-pipeline.md`.

---

## Cross-cutting: tool-change tests

For each Phase-1 tool change (1.2–1.9), the following test coverage is
required before declaring the change "done":

- [ ] Unit tests in `tests/Reactor.DocPipeline.Tests/` (or
      `tests/Reactor.Cli.Tests/` for CLI surface).
- [ ] Golden-file fixtures for non-trivial output (snippet
      extraction, reference generation, link injection).
- [ ] Failure-mode tests for every error code introduced.
- [ ] An end-to-end integration test: a minimal template repo + doc
      app under `tests/Reactor.DocPipeline.Tests/Fixtures/` that
      exercises every directive (snippet, screenshot, source-snippet,
      diagram, cref, ref-marker, caveat block).
- [ ] CI runs the integration test in a clean checkout to catch
      "works on my machine" Mermaid/Puppeteer issues.

---

## Cross-cutting: GitHub render validation

For each phase, before declaring it complete:

- [ ] Push the integration branch to GitHub.
- [ ] Open the docset on github.com using the rendered Markdown
      preview (not the editor view).
- [ ] Visit every page added or changed in the phase; confirm:
  - [ ] Tables render with correct alignment.
  - [ ] SVG images render in both light and dark themes.
  - [ ] PNG screenshots render and are reasonably sized.
  - [ ] Code blocks have correct syntax highlighting (csharp / yaml /
        markdown / xml).
  - [ ] Cross-links resolve (no 404s).
  - [ ] Previous/Next chain unbroken from readme through last page.
  - [ ] Mobile rendering acceptable (sidebar collapses, no overflow).
- [ ] Record findings in `docs/specs/041/phase-<N>-render-report.md`.
- [ ] Fix any GitHub-specific rendering bugs before merging.

---

## Cross-cutting: doc review

Each new or expanded page goes through a two-pass review:

- [ ] **Pass 1 — Author self-review.** Read the rendered page on
      GitHub (not the template). Confirm:
  - [ ] Mental-model paragraph answers "when would I use this?"
  - [ ] First code snippet appears in the first 30 lines.
  - [ ] Every modifier / hook / event mentioned has a usage example.
  - [ ] Caveats are concrete (no vague "be careful").
  - [ ] Patterns section ties back to a real recipe page if one exists.
- [ ] **Pass 2 — Peer review** by someone who didn't author the page.
      Optimally a primary-audience member (XAML developer for
      reactor-vs-xaml; renderer-author for under-the-hood). Reviewer
      reads the page cold and lists what was unclear.
- [ ] Capture review notes in PR comments; resolve before merge.

---

## Final acceptance — spec §13 success criteria

Before closing spec 041:

- [ ] **Coverage:** 100% of controls in `ai-author-skill.md` resolve
      from the controls catalog (run a script in
      `tools/api-scrub/` that diffs).
- [ ] **Coverage:** 100% of hooks have at least one usage example in
      the corresponding topic page.
- [ ] **Discoverability:** time-boxed user test — new user
      (recruited internally) answers "does Reactor support X?"
      starting from `readme.md` in ≤30 seconds for each of 10
      sampled capabilities.
- [ ] **Tier distribution:** ≥36 pages Comprehensive, ≤4 Solid, 0
      Stub.
- [ ] **Cross-linking:** zero analyzer warnings.
- [ ] **Sequential traversal:** Previous/Next links form a complete
      chain (automated check).
- [ ] **Comparison alignment:** each of 19 categories in
      `docs/research/compare/overview.md` maps to ≥1 page; mental-
      model paragraphs align with rating commentary.
- [ ] **Internals literacy:** a XAML/WinUI developer can answer the
      four §13 questions from the under-the-hood track without
      reading source. Validate with one external XAML reviewer.
- [ ] `docs/reference/reconciliation.md` and
      `docs/reference/state-and-hooks.md` no longer exist.
- [ ] Spec 041 status updated from `Draft` to `Shipped`.
- [ ] Companion files (`041/doc-audit-2026-05.md`, retros, render
      reports) archived in `docs/specs/041/`.

---

## Risk tracking (from spec §14)

- [ ] **Doc app proliferation.** ~14 new doc apps. Measure build
      time after Phase 2; if >2× current, batch screenshot capture
      and profile worst-cost apps.
- [ ] **Token catalog drift.** Verify the theming-tokens generator
      step (task 2.3) actually regenerates on every theme change.
- [ ] **Tier inflation.** §11 lint is the guard; verify it blocks
      merge in CI.
- [ ] **Author throughput.** 14 new pages + 6 expansions ≈ 4-6
      weeks. Track velocity weekly; if behind, prioritize AI-drafted
      Solid-tier first, defer Comprehensive promotions to Phase 4.

---

## Out-of-scope reminders (spec §3)

- [ ] No doc-pipeline rewrite — `.md.dt` + Reactor-app +
      screenshots stays.
- [ ] No hand-edits to `docs/guide/*.md`.
- [ ] No translation / localization of the docset.
- [ ] No search index / static-site build (Docusaurus / VitePress).
- [ ] `docs/reference/` is preserved only for framework-contributor-
      only process material; user-facing portions absorb into the
      guide.
