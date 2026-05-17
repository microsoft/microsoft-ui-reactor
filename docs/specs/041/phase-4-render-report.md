# Phase 4 — GitHub render report

Captured 2026-05-17. Mirrors `phase-1-render-report.md`,
`phase-2-render-report.md`, `phase-3-render-report.md`, and
`phase-3-5-render-report.md`. The constraint for Phase 4.6 is "do not
push to remote"; this report captures everything that could be verified
locally and flags what defers to PR-open time.

## Inputs

- Branch: `docs/041-uplift` (33+ commits ahead of main, not pushed).
- Pipeline: `src/Reactor.Cli/Docs/` (TemplateParser, SnippetExtractor,
  TierLint, DiagramProcessor, ReferenceGen, ReferenceLinkInjector,
  ReferenceMap, CrossLinkLint, CompileCommand).
- Reactor.xml source: `src/Reactor/bin/ARM64/Debug/net10.0-windows10.0.22621.0/Reactor.xml`
  (picked up by the bin-walker's platform-stamped layout).
- `mermaid-cli` (`mmdc`): NOT available in this environment — the
  Mermaid `.mmd` sources do not render. The 18 internals-page SVGs
  remain unresolved image refs (see "Image-ref findings" below).

## Commands run

```
dotnet run --project src/Reactor.Cli -- docs compile --validate-only
dotnet run --project src/Reactor.Cli -- docs compile --skip-screenshots --no-build
```

The `--no-build` flag avoids retriggering the Reactor.csproj XAML
compiler race per `feedback_test_parallelism.md`. Reactor.xml from the
last out-of-band build is reused.

## Validation summary

`mur docs compile --validate-only`:

- **0 errors.**
- **24 TIER_W001 warnings** — `winui-ref not declared` on pages that
  intentionally lack a WinUI wrapper (internals pages + meta pages like
  getting-started, dev-tooling, xaml-developers, components, hooks,
  flex-layout, effects, commanding, context, accessibility, charting,
  advanced, data-system). All expected; per spec §11, `winui-ref:` is
  required only for transparent-wrapper pages.
- **0 cross-link findings** — `REACTOR_DOC_XLINK_001` clean since the
  4.5 cross-link sweep.
- **Validation passed** message at the foot.

## Full compile run

`mur docs compile --skip-screenshots --no-build` end-to-end:

- **63 templates assembled** (the 64-template count from Phase 3.5
  minus the deleted wave-C shells; see phase-3-5-retro.md).
- **73 reference pages generated** under
  `docs/guide/reference/hooks/`.
- **11 `REACTOR_DOC_REFMARKER_001` warnings** on two templates
  (`hooks.md.dt`, `effects.md.dt`). The cross-link sweep (Phase 4.5)
  added `<!-- ref:UseState -->`, `<!-- ref:UseEffect -->`, etc. markers
  in the new mental-model leads and reference tables; the ref-gen
  prototype generates pages for the `Microsoft.UI.Reactor.Hooks` *
  namespace contents (extension classes like `UseMemoCellsExtensions`,
  `UseElementRefExtensions`) but does NOT generate per-hook pages for
  the factory methods on `RenderContext` (where `UseState` /
  `UseReducer` / `UseEffect` actually live). The markers fall through
  as HTML comments — invisible to readers on GitHub but inert; they do
  not produce broken links. **Tracked forward, not blocking Phase 4
  exit.**
- **22 `REACTOR_DOC_IMAGE_001` findings**:
  - **18 Mermaid SVGs**: every Under-the-hood page diagram (15) +
    `images/packaging/publish-pipeline.svg`,
    `images/performance/top-down-flow.svg`,
    `images/wpf-interop/host-architecture.svg`,
    `images/xaml-developers/mental-model-shift.svg`. These render when
    a build host with `mmdc` runs `mur docs compile` (without
    `--skip-diagrams`). Tracked from Phase 3.5 render report; the
    Phase 4 additions (packaging, performance, wpf-interop,
    xaml-developers) add four to the same surface.
  - **3 PNGs** under `images/winforms-interop/` (`island-control.png`,
    `designer.png`, `background.png`): pre-existing from Phase 1.14;
    these are doc-app captures that require the WinAppSDK doc-app
    harness to run. Tracked in `RESUME-HERE.md`.
  - **1 PNG** under `images/forms/keep-submit-reachable.png`:
    pre-existing from Phase 1.14; same WinAppSDK constraint. (Was on
    the prior list as 4 winforms-interop entries; the actual current
    count splits as 3 winforms-interop + 1 forms.)

## Pages added or substantially changed in Phase 4

| Page | Tier change | Phase 4 sub-task |
|---|---|---|
| `hooks.md.dt` | Solid → Comprehensive | 4.6 wave-1 (`d39b8329`) |
| `effects.md.dt` | Solid → Comprehensive | 4.6 wave-1 (`72a58424`) |
| `components.md.dt` | Solid → Comprehensive | 4.6 wave-1 (`3e4a85a9`) |
| `commanding.md.dt` | Solid → Comprehensive | 4.6 wave-2 (`e80dfa80`) |
| `context.md.dt` | Solid → Comprehensive | 4.6 wave-2 (`dddef719`) |
| `layout.md.dt` | Solid → Comprehensive | 4.6 wave-2 (`dbb3bf71`) |
| `flex-layout.md.dt` | Solid → Comprehensive | 4.6 wave-3 (`402322d5`) |
| `advanced.md.dt` | Solid → Comprehensive | 4.6 wave-3 (`ba8ed02a`) |
| `styling.md.dt` | Solid → Comprehensive | 4.6 wave-3 (`17a93cf6`) |
| `getting-started.md.dt` | Solid → Comprehensive | 4.6 wave-C (`4ea1c205`) |
| `xaml-developers.md.dt` | Solid → Comprehensive | 4.6 wave-C (`88fe5c51`) |
| `winforms-interop.md.dt` | Solid → Comprehensive | 4.6 wave-C (`ebed0a2d`) |
| `input-and-gestures.md.dt` | Solid → Comprehensive | 4.6 wave-C (`a3f4cf5b`) |
| `navigation.md.dt` | Solid → Comprehensive | 4.1 |
| `animation.md.dt` | Solid → Comprehensive | 4.1 |
| `accessibility.md.dt` | Solid → Comprehensive | 4.1 |
| `data-system.md.dt` | Solid → Comprehensive | 4.1 |
| `charting.md.dt` | Solid → Comprehensive | 4.1 |
| `wpf-interop.md.dt` | NEW (Solid) | 4.2 |
| `performance.md.dt` | NEW (Solid) | 4.3 |
| `packaging.md.dt` | NEW (Solid) | 4.4 |
| `readme.md.dt` | `tier: solid` declared (wave-D, `3bcc56d6`) | 4.6 wave-D |
| `localization.md.dt` | `tier: solid` declared (wave-D) | 4.6 wave-D |
| `async-resources.md.dt` | `tier: solid` declared (wave-D) | 4.6 wave-D |
| `windows.md.dt` | `tier: solid` declared (wave-D) | 4.6 wave-D |

## Local render checklist

Walked the rendered `docs/guide/` output for ~10 sampled Phase 4 pages:

- [x] **Mental-model lead** renders above the first `#`-heading on every
      Comprehensive page sampled (getting-started, hooks, components,
      effects, layout, flex-layout, advanced, accessibility, charting,
      data-system, input-and-gestures, winforms-interop, xaml-developers).
- [x] **Reference table** present in the first half of every page
      sampled. The TIER_005 lint passes for all Phase 4 promotions.
- [x] **`## Tips`** section present on every page sampled.
- [x] **`## Next Steps`** section with ≥3 inline links — visually
      verified for hooks.md, layout.md, winforms-interop.md, and
      getting-started.md. Order numbers from front-matter form a coherent
      readme → Get Started → Learn → UI surface → Catalog → App
      architecture → Patterns → Tooling → Interop → Under the hood
      → API Reference chain.
- [x] **Caveats block** renders as a `> **Caveat:**` blockquote on
      every Comprehensive page sampled.
- [x] **Tables** render with correct alignment in raw markdown. The
      hooks.md reference table (5 columns) and the analyzer-architecture
      diagnostic-ID table (14 rows) both render cleanly.
- [x] **Cross-links** resolve to `.md` extensions consistently. The
      cross-link analyzer (REACTOR_DOC_XLINK_001) reports zero
      findings.
- [x] **Code-fence languages** recognized — `csharp` on every snippet,
      `yaml` on doc-manifest examples, `xml` on the WinUI-host snippets
      in winforms-interop.

## Items deferred to GitHub PR-open

The "do not push to remote" Phase 4.6 constraint means these have to
wait until the docs/041-uplift branch is pushed:

- [ ] **Tables with dense column counts** — the hooks.md reference
      table (5 columns), the analyzer-architecture diagnostic-ID table
      (14 rows × 4 columns), and the winforms-interop data-flow
      mechanism table (4 rows × 3 columns) should be visually
      verified on the GitHub light-and-dark theme switcher.
- [ ] **SVG dark-theme contrast** for the 18 Mermaid diagrams — once
      `mmdc` runs on a CI host with the build pipeline, the
      Mermaid-default palette occasionally produces light-on-light or
      dark-on-dark issues. Same item carried over from
      phase-3-5-render-report; Phase 4 adds 4 more diagrams
      (`performance/top-down-flow`, `packaging/publish-pipeline`,
      `wpf-interop/host-architecture`,
      `xaml-developers/mental-model-shift`).
- [ ] **PNG screenshot capture** — the 4 pre-existing missing
      screenshots under `images/winforms-interop/` and
      `images/forms/keep-submit-reachable.png` need a doc-app
      harness run on a WinAppSDK host. These are tracked in
      Phase 1.14 render report; not new in Phase 4.
- [ ] **Mobile rendering check** — Comprehensive pages tend to be
      long (200-400 lines). The sticky-side TOC nav on the GitHub web
      view collapses cleanly in local-mode raw-markdown preview;
      worth verifying once the docset is on github.com.
- [ ] **Code-block syntax highlighting in dark theme** — local raw
      preview shows `csharp` fence working; GitHub's dark theme has
      occasionally surprised on this in prior render reports.
- [ ] **Previous/Next chain on mobile** — front-matter `order:`
      values form a coherent chain (verified locally); GitHub may
      render adjacent-page nav differently on small viewports.

## Risks flagged for the PR-open render check

1. **The `<!-- ref:UseState -->` markers in `hooks.md` and `effects.md`
   render as HTML comments.** They are invisible to readers — not a
   broken-link risk — but they also don't add value. The ref-gen
   prototype generated 73 pages for `Microsoft.UI.Reactor.Hooks.*` but
   the core `RenderContext` hook factories (UseState, UseEffect,
   UseMemo, UseRef, UseCallback, UseContext, UseObservable, UseReducer,
   UsePersisted) do not yet produce per-symbol reference pages. Either
   the registry needs an additional `Microsoft.UI.Reactor.Core.RenderContext`
   routing rule, or the markers should be removed from the templates
   pending Phase 5 reference-gen expansion. Tracked in
   `phase-4-retro.md`.

2. **The `getting-started.md` manual-setup walkthrough.** The page
   opens with a multi-paragraph warning that Reactor doesn't yet ship
   a signed installer and walks the source-clone bootstrap. This
   reads cleanly locally; the PowerShell code-fence rendering on
   GitHub for the four `mur` commands should be eyeball-checked
   for line-wrapping on narrow widths.

3. **The `xaml-developers.md` mental-model-shift diagram.** New in
   Phase 4 wave-C; the Mermaid source exists at
   `docs/_pipeline/diagrams/xaml-developers/mental-model-shift.mmd`,
   the rendered SVG path is one of the 22 image findings above.
   First-impression diagram for the XAML-developer audience —
   visual quality matters more here than on most under-the-hood
   pages.

4. **The new wpf-interop page is honest about
   `Reactor.Interop.Wpf` not existing yet.** The page documents the
   surface roadmap-shape inside comment-form snippets. Readers may
   miss the "not shipping today" framing if they skim — verify the
   `> **Status:**` callout (or equivalent) renders above the fold
   when the page is viewed on github.com.

## Local-compile validation summary

`dotnet run --project src/Reactor.Cli -- docs compile --validate-only`
returns "Validation passed" across all 63 templates. The Phase 4
promotions and new pages are tier-lint clean at their declared tiers
(only TIER_W001 winui-ref-not-declared warnings on the pages where
that's expected).

`dotnet run --project src/Reactor.Cli -- docs compile --skip-screenshots
--no-build` returns "Documentation compiled successfully" with the 22
image findings + 11 REFMARKER findings noted above. All Markdown files
in `docs/guide/**` write cleanly; the full output is committed as part
of the Phase 4.6 regeneration commit.
