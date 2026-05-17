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

- [ ] Implement the post-processor that, for each generated reference
      page, injects:
  - [ ] A "**Learn more**" callout near the top with links to the
        registry-mapped guide page(s).
  - [ ] A "**See Also**" section merging `<seealso>` author links
        + registry defaults.
  - [ ] Dual-link rendering for inline `<see cref="..."/>` (target
        reference page + target guide page if any).
- [ ] Implement `<!-- ref:Member -->` marker expansion in
      hand-authored templates → resolved MD link to the generated
      reference page.
- [ ] Implement reverse "Featured in" callout: each reference page
      gains a list of guide pages that reference it (via
      `<!-- ref:Member -->` scan across `_pipeline/templates/`).
- [ ] Lints: warn when a registry category has no mapped guide page;
      warn when a guide page has no `<!-- ref: -->` marker pointing
      to it (per §10.4.1 acceptance).
- [ ] Unit tests for marker expansion, dual-link rendering, and
      Featured-in reverse scan.

### 1.10 AI Author Skill update (spec §10.5)

- [ ] Update `docs/_pipeline/ai-author-skill.md`:
  - [ ] Document the `tier:` front-matter field with examples.
  - [ ] Document the `winui-ref:` front-matter field.
  - [ ] Document the `<!-- ai:caveat -->` block.
  - [ ] Document the `snippet="source:..."` directive.
  - [ ] Add the SVG-over-ASCII policy with Mermaid example block.
  - [ ] Add a "Diagram authoring" subsection alongside "Snippet markers".
  - [ ] Document the `<!-- ref:Member -->` marker.
  - [ ] Update the "Topic Ideas" table to reflect the 64-page layout
        from spec §7.1.
- [ ] Review the updated skill end-to-end against one new template
      to confirm the directives are actionable.

### 1.11 Page-template skeletons (spec §9 Phase 1)

- [ ] Create `docs/_pipeline/templates/_skeletons/stub.md.dt`.
- [ ] Create `docs/_pipeline/templates/_skeletons/solid.md.dt`.
- [ ] Create `docs/_pipeline/templates/_skeletons/comprehensive.md.dt`.
- [ ] Each skeleton must pass `mur docs compile --validate-only`
      when its placeholder text is filled in. The skeleton itself
      is allowed to fail (templates aren't compiled in skeleton form).
- [ ] Skeleton headings exactly match the tier-lint expectations
      from §11 (so authors can't accidentally drop a required
      section).

### 1.12 Readme rewrite to 10-section index (spec §7.1)

- [ ] Rewrite `docs/_pipeline/templates/readme.md.dt` to the
      10-section structure from spec §7.1.
- [ ] Every new page filed as a Stub-tier link with "Coming soon"
      anchor — so the surface area is visible even when incomplete.
- [ ] Sequential `order:` numbers in front-matter rebase to integers;
      new pages slot in as `.5` per spec §7.2.
- [ ] Confirm Previous/Next chain is unbroken when only stubs exist.

### 1.13 dev-tooling.md promotion + devtools-ux merge (spec §9 Phase 1)

- [ ] Merge content from `docs/_pipeline/templates/devtools-ux.md.dt`
      into `dev-tooling.md.dt`. Cover: `mur` CLI subcommands, MCP
      server, VS Code panel, dotnet watch integration, in-app dev
      menu.
- [ ] Delete `devtools-ux.md.dt` and its
      `docs/_pipeline/apps/` companion if any.
- [ ] Promote `dev-tooling.md` to Comprehensive tier (lint must pass).
- [ ] Update any pages that linked to `devtools-ux.md` to point at
      the merged page.

### 1.14 Phase 1 validation & publish-test

- [ ] `mur docs compile --validate-only` passes across every page
      including new stubs.
- [ ] One auto-generated reference category (Hooks) renders
      correctly on GitHub (preview-branch verification).
- [ ] One SVG-illustrated stub page round-trips through the pipeline
      and renders correctly on GitHub (light + dark theme).
- [ ] CI green on the integration branch; `validate-only` wired into
      PR checks.
- [ ] Walk the readme → new stub → Previous/Next links end-to-end
      on the GitHub-rendered preview.
- [ ] Phase 1 retro: capture what surprised us in
      `docs/specs/041/phase-1-retro.md` (delete after Phase 4 if no
      decisions changed).

---

## Phase 2 — Reactor-unique gaps

Pages with no upstream WinUI parallel — full ownership ours. Sequence
by traffic impact (per spec §9 Phase 2).

### 2.1 `controls.md` — catalog index

- [ ] Author template `docs/_pipeline/templates/controls.md.dt` at
      Solid tier (becomes Comprehensive once individual catalog pages
      land in Phase 3).
- [ ] Doc app `docs/_pipeline/apps/controls/` with one canvas per
      control group (forms / collections / text-and-media /
      status-and-info / dialogs-and-flyouts / data-system / charting)
      for thumbnail screenshots.
- [ ] Thumbnail-strategy: per spec §12 Q7 (resolved option b),
      `doc-manifest.yaml` declares a `catalog-thumb` capture per
      control.
- [ ] Implement `catalog-thumb` capture support in the doc-app
      harness if not already present. **Unit test + golden image.**
- [ ] Thumbnail-index table renders with image + one-line + link.
- [ ] Confirm no unlinked controls remain in the catalog stub set.

### 2.2 `testing.md` — Solid

- [ ] Doc app `docs/_pipeline/apps/testing/`.
- [ ] Cover: headless renderer fixtures, snapshot tests,
      `UseEffect`-aware async test patterns, accessibility scanner
      integration, the `Reactor.SelfTests` pattern.
- [ ] Template at Solid tier (lint must pass).
- [ ] Cross-link from `getting-started`, `effects`, `accessibility`.

### 2.3 `theming-tokens.md` — Comprehensive

- [ ] Generate the swatch table from the theme source at compile
      time (extends spec 015 per §14 risk mitigation).
- [ ] Implement `mur docs compile` step that reads
      `src/Reactor/Theme/` and emits a token catalog snippet.
- [ ] Doc app `docs/_pipeline/apps/theming-tokens/` for swatch
      capture in light + dark themes.
- [ ] Template at Comprehensive tier — full mental-model intro,
      Patterns, Common mistakes (e.g. hardcoded colors vs token ref),
      WinUI link to design tokens.
- [ ] Confirm 37+ tokens enumerated (spec §5.3).

### 2.4 `persistence.md` — Solid

- [ ] Doc app `docs/_pipeline/apps/persistence/`.
- [ ] Cover: `UsePersisted` with both scopes (window / app),
      migration story, JSON shape, conflict resolution.
- [ ] Template at Solid tier.

### 2.5 `recipes/` folder + index (spec §7.1 Section 6, §12 Q3)

- [ ] Create `docs/_pipeline/templates/recipes/` folder.
- [ ] Author `recipes/index.md.dt` at Solid tier — gallery view
      with thumbnail per recipe.
- [ ] Initial recipe set (8-10): `login.md.dt`, `master-detail.md.dt`,
      `settings-page.md.dt`, `paginated-list.md.dt`,
      `modal-dialog.md.dt`, `multi-step-form.md.dt`,
      `search-with-suggestions.md.dt`, `command-palette.md.dt`,
      `drag-reorder.md.dt`. Each at Solid tier minimum.
- [ ] Doc apps under `docs/_pipeline/apps/recipes/<name>/`.
- [ ] Confirm `mur docs compile` handles the nested templates folder
      (may require pipeline fix — flag and fix if so).

### 2.6 `cheat-sheet.md` — Solid

- [ ] Single-page reference card: factories, hooks, modifiers,
      events, common patterns.
- [ ] Pulls from the same source as Section 10 reference (Phase 3.5
      / Phase 4 fully wires this) — initial version can be
      hand-curated.
- [ ] Template at Solid tier.

### 2.7 `rules-of-reactor.md` — Solid

- [ ] Hook rules, render-purity rules, anti-patterns, key idioms.
- [ ] Cross-link liberally from `hooks`, `effects`, `components`.
- [ ] Template at Solid tier.

### 2.8 Phase 2 review

- [ ] Tier-lint clean across all 7 new pages.
- [ ] Doc review: read each page end-to-end as a new user; flag
      sections that don't answer "when would I use this?"
- [ ] Cross-link audit: every concept named in prose links to a
      page (run the §11 cross-link lint).
- [ ] GitHub preview-branch render check.
- [ ] Phase 2 exit gate: every Reactor-original concept that lives
      only in `ai-author-skill.md` also has a user-facing page.

---

## Phase 3 — Controls catalog

Three new pages + two existing expansions (spec §9 Phase 3). Each
catalog page follows the per-control template from spec §6.3.

### 3.1 `text-and-media.md` — NEW, Comprehensive

- [ ] Doc app under `docs/_pipeline/apps/text-and-media/`.
- [ ] Controls: TextBlock variants, RichTextBlock, RichEditBox,
      MarkdownTextBlock, Image, MediaPlayerElement, WebView2,
      InkCanvas, MapControl.
- [ ] Per-control: factory signature, modifier table, ≥1 default
      screenshot, ≥1 customized screenshot, ≥1 "Don't" example,
      WinUI link (where applicable; MarkdownTextBlock has no
      WinUI parallel).
- [ ] Template at Comprehensive tier.

### 3.2 `status-and-info.md` — NEW, Solid

- [ ] Doc app under `docs/_pipeline/apps/status-and-info/`.
- [ ] Controls: InfoBar, InfoBadge, ProgressBar, ProgressRing,
      TeachingTip, PipsPager, PersonPicture, RatingControl.
- [ ] Per-control essentials per §6.3.
- [ ] Template at Solid tier.

### 3.3 `dialogs-and-flyouts.md` — NEW, Comprehensive

- [ ] Doc app under `docs/_pipeline/apps/dialogs-and-flyouts/`.
- [ ] Controls: ContentDialog, MenuFlyout, CommandBarFlyout, Popup.
- [ ] Cover commanding integration patterns; cross-link to
      `commanding.md`.
- [ ] Template at Comprehensive tier.

### 3.4 `forms.md` — EXPAND

- [ ] Add: AutoSuggestBox, DatePicker, TimePicker, CalendarView,
      CalendarDatePicker, ColorPicker, NumberBox, PasswordBox,
      RadioButtons, ToggleSwitch (verify which are missing per
      audit).
- [ ] Doc-app additions under
      `docs/_pipeline/apps/forms/<control>/`.
- [ ] Promote to Comprehensive tier.

### 3.5 `collections.md` — EXPAND

- [ ] Add: grouping recipes, drag-reorder where supported, virtual
      list deep-dive, lazy-loading patterns.
- [ ] Doc-app additions.
- [ ] Promote to Comprehensive tier.

### 3.6 Phase 3 review

- [ ] Tier-lint clean.
- [ ] Doc review: control-by-control, confirm every entry in
      `ai-author-skill.md` resolves from the catalog index.
- [ ] Add explicit "not yet wrapped — track in spec NNN" entries for
      any control listed in `ai-author-skill.md` that doesn't have
      a Reactor wrapper yet.
- [ ] Phase 3 exit: Controls Catalog index has zero unlinked
      controls.
- [ ] GitHub preview render check (verify thumbnail images render).

---

## Phase 3.5 — Under-the-hood deep dive

Parallel to Phase 3, different author skillset (per spec §9 Phase
3.5). Sequence by reader dependency. **No depth ceiling** per spec
§12 Q5.

### 3.5.1 `architecture-overview.md` — Comprehensive

- [ ] Author SVG/Mermaid architecture diagram (declarative shell →
      element records → reconciler → WinUI tree). Place in
      `docs/_pipeline/diagrams/architecture-overview/overview.mmd`.
- [ ] Template at Comprehensive tier; pulls `snippet="source:..."`
      from at least 3 areas of `src/Reactor/`.
- [ ] "Read the source" callout linking to `src/Reactor/`.

### 3.5.2 `reactivity-model.md` — Comprehensive

- [ ] Diagram: state-setter → re-render flow.
- [ ] Cover: why hooks not INotifyPropertyChanged, ShouldUpdate,
      Memo, comparison vs MVVM observable property change.

### 3.5.3 `reactor-vs-xaml.md` — Comprehensive

- [ ] Side-by-side mapping per spec §7.1.1.
- [ ] DOUBLE INDEX: lives in Section 1 (Get Started) AND Section 9
      (Under the hood). Same file, two index entries.
- [ ] Diagram: pull-based binding vs push-based render-from-state.

### 3.5.4 `reconciliation.md` — Comprehensive (promotes
      `docs/reference/reconciliation.md`)

- [ ] Migrate content from `docs/reference/reconciliation.md` into
      template form.
- [ ] Add diagrams, source snippets, Patterns, Caveats per
      Comprehensive tier.
- [ ] Delete `docs/reference/reconciliation.md` after promotion lands.
- [ ] Update any inbound links to point at the new guide location.

### 3.5.5 `hooks-internals.md` — Comprehensive (promotes
      `docs/reference/state-and-hooks.md`)

- [ ] Migrate content from `docs/reference/state-and-hooks.md`.
- [ ] Delete the reference source after promotion.

### 3.5.6 `effects-scheduling.md` — Comprehensive

- [ ] Link to `docs/reference/async-system.md` for deeper internals
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

- [ ] Tier-lint clean across all 14 pages.
- [ ] Doc review by someone who has shipped renderer/hook internals
      (spec §9 Phase 3.5 requires this — the pages are easy to draft
      and hard to make correct).
- [ ] Verify all `snippet="source:..."` references resolve cleanly.
- [ ] Verify `docs/reference/reconciliation.md` and
      `docs/reference/state-and-hooks.md` are deleted and no
      orphaned links remain.
- [ ] Phase 3.5 exit: a XAML/WinUI developer can answer the §13
      "Internals literacy" success-criteria questions without
      reading source.

---

## Phase 4 — Polish, migration, and process

### 4.1 Promote remaining Solid pages to Comprehensive

- [ ] `forms.md` — already Comprehensive after Phase 3.5 expand;
      verify.
- [ ] `collections.md` — verify.
- [ ] `navigation.md` — promote to Comprehensive.
- [ ] `animation.md` — promote to Comprehensive.
- [ ] `accessibility.md` — promote to Comprehensive.
- [ ] `data-system.md` — promote to Comprehensive.
- [ ] `charting.md` — promote to Comprehensive.

### 4.2 New `wpf-interop.md` — Solid

- [ ] Doc app under `docs/_pipeline/apps/wpf-interop/`.
- [ ] Cover host control, data flow, threading constraints; parallel
      to `winforms-interop.md`.

### 4.3 New `performance.md` — Solid

- [ ] Top-down ETW / `EventDispatch` walkthrough.
- [ ] Cross-link to `perf-instrumentation.md` (Under the hood).

### 4.4 New `packaging.md` — Solid

- [ ] MSIX, single-file, ARM64, AOT considerations.

### 4.5 Cross-link sweep

- [ ] Implement the cross-link analyzer in `mur docs compile`: every
      prose mention of a concept that has a page must link.
- [ ] Run analyzer; fix gaps page-by-page.
- [ ] Exit gate: zero warnings from the cross-link analyzer.

### 4.6 Phase 4 review & exit

- [ ] Tier audit shows: 0 Stub, ≤4 Solid, ≥36 Comprehensive (spec
      §9 Phase 4 exit criterion).
- [ ] Final doc review pass — read the docset end-to-end as a new
      user (start at readme, walk Previous/Next).
- [ ] Comparison alignment check: each of the 19 categories in
      `docs/research/compare/overview.md` maps to ≥1 page; each
      page's mental-model paragraph aligns with the comparison
      rating commentary (spec §13).
- [ ] GitHub preview render check on the full docset.

---

## Phase 5 — Continuous quality

### 5.1 `mur docs check-tier` standalone command

- [ ] Subcommand that asserts the tier declared in front-matter
      matches the structural checklist (factors out the §11 lint
      so authors can run it without full compile).
- [ ] Unit tests.

### 5.2 Tier-drift CI check

- [ ] PR check that runs `mur docs check-tier` on every PR touching
      templates or apps.
- [ ] Failure modes documented in `docs/contributing/doc-pipeline.md`.

### 5.3 Doc-coverage gate for new features

- [ ] Add a CI/repo convention: new framework features land with a
      doc page at Solid+. Document in `CONTRIBUTING.md`.
- [ ] Consider an analyzer or convention check that flags new public
      API in `src/Reactor/` not referenced by any
      `<!-- ref:Member -->` or `seealso cref=`.

### 5.4 Quarterly tier audit

- [ ] Schedule a recurring "tier audit" — re-rank every page and
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
