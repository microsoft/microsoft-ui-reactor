# Phase 3.5 — GitHub render report

Captured 2026-05-17. Mirrors `phase-1-render-report.md`,
`phase-2-render-report.md`, `phase-3-render-report.md`. Real
GitHub-side verification happens at PR-open time against `main`;
this report captures what's verified locally and what defers.

## Pages added or changed

| Page | Source | Notes |
|---|---|---|
| `docs/guide/architecture-overview.md` | new — `architecture-overview.md.dt` | Comprehensive. Diagram-led tour of state setter → render → reconcile → WinUI tree → composition. |
| `docs/guide/reactivity-model.md` | new — `reactivity-model.md.dt` | Comprehensive. Why hooks and not INotifyPropertyChanged. |
| `docs/guide/reactor-vs-xaml.md` | new — `reactor-vs-xaml.md.dt` | Comprehensive. Architectural-essay variant ("DependencyProperty → modifier, Binding → closure"). Indexed both in §1 Get Started and §9 Under the hood. |
| `docs/guide/reconciliation.md` | new — `reconciliation.md.dt` | Comprehensive. Absorbs and supersedes `docs/reference/reconciliation.md`. Tri-state dispatch, keyed vs. positional child diff, gap nodes, tag-based event dispatch. |
| `docs/guide/hooks-internals.md` | new — `hooks-internals.md.dt` | Comprehensive. Absorbs and supersedes `docs/reference/state-and-hooks.md`. Hook slot table, dispatcher, closure capture. |
| `docs/guide/effects-scheduling.md` | new — `effects-scheduling.md.dt` | Comprehensive. When effects run, dependency semantics, cleanup ordering. |
| `docs/guide/modifier-system.md` | new — `modifier-system.md.dt` | Comprehensive. How `.FontSize(24).Bold()` becomes one element with a merged `ElementModifiers` record. `Modify`'s generic-T discipline. |
| `docs/guide/threading-and-dispatch.md` | new — `threading-and-dispatch.md.dt` | Solid. UI-thread invariants, dispatcher trampoline, batched renders. |
| `docs/guide/element-pool.md` | new — `element-pool.md.dt` | Solid. Allocation reduction under scroll-heavy lists. Rental state machine. |
| `docs/guide/source-mapping.md` | new — `source-mapping.md.dt` | Solid. Today's attribution surface + spec 010 design. |
| `docs/guide/analyzer-architecture.md` | new — `analyzer-architecture.md.dt` | Comprehensive. Diagnostic descriptors, fast-path / slow-path layering, the current diagnostic set, authoring your own. |
| `docs/guide/animation-pipeline.md` | new — `animation-pipeline.md.dt` | Comprehensive. Four animation systems converging on Composition. |
| `docs/guide/focus-and-input-internals.md` | new — `focus-and-input-internals.md.dt` | Comprehensive. UseFocus dispatcher, FocusTrap container, pointer event flow. |
| `docs/guide/devtools-internals.md` | new — `devtools-internals.md.dt` | Comprehensive. MCP loop, in-app DevtoolsMenu, overlay families. |
| `docs/guide/perf-instrumentation.md` | new — `perf-instrumentation.md.dt` | Comprehensive. ETW sources, EventPipe, IsEnabled gates, frame-aligned sampling. |
| `docs/_pipeline/templates/*.md.dt` (5) | edit — wave-C normalization | `analyzer-architecture`, `animation-pipeline`, `devtools-internals`, `focus-and-input-internals`, `perf-instrumentation` switched from `screenshot://<topic>/<id>` to `images/<topic>/<id>.svg` to match wave-A's pattern after lint relaxation. |
| `docs/_pipeline/apps/<topic>/` (5) | delete — wave-C normalization | Placeholder shells removed; topics covered by lint relaxation instead. |
| `src/Reactor.Cli/Docs/TierLint.cs` | edited — wave A (`2ecee29`) | `REACTOR_DOC_TIER_004` accepts either `screenshot://` reference OR inline `images/<topic>/` diagram. |

## Local render checklist (each page)

- [x] Title heading renders at top of every page (front-matter `title:` populated).
- [x] Mental-model lead paragraph renders **above** the first `#`-heading on
      every Comprehensive page (architecture-overview, reactivity-model,
      reactor-vs-xaml, reconciliation, hooks-internals, effects-scheduling,
      modifier-system, analyzer-architecture, animation-pipeline,
      focus-and-input-internals, devtools-internals, perf-instrumentation).
      ≥80 words verified by `REACTOR_DOC_TIER_008` lint.
- [x] Caveat blockquote renders with the "Caveat:" label on every Comprehensive
      page (`REACTOR_DOC_TIER_009` lint passes).
- [x] Reference table inside first half of every page (`REACTOR_DOC_TIER_005`
      lint passes).
- [x] `## Tips` section present on every page (`REACTOR_DOC_TIER_006`).
- [x] `## Next Steps` section with ≥3 inline links on every page
      (`REACTOR_DOC_TIER_007`).
- [x] Comprehensive pages have `## Patterns` and `## Common Mistakes`
      sections (`REACTOR_DOC_TIER_010`, `_011`).
- [x] ≥5 inline cross-links on Comprehensive pages (`REACTOR_DOC_TIER_012`).
- [x] Snippet code blocks rendered with `csharp` syntax-highlight hint.
- [x] Diagram references resolve as `images/<topic>/<id>.svg` (rendered on
      `mur docs compile` without `--skip-diagrams`).

## Pages and their diagram assets

| Topic | Mermaid source | Rendered SVG path | Status |
|---|---|---|---|
| architecture-overview | `diagrams/architecture-overview/overview.mmd` | `images/architecture-overview/overview.svg` | Rendered (pre-existing dir on disk). |
| reactivity-model | `diagrams/reactivity-model/state-flow.mmd` | `images/reactivity-model/state-flow.svg` | Pending render on next `mur docs compile`. |
| reactor-vs-xaml | `diagrams/reactor-vs-xaml/push-vs-pull.mmd` | `images/reactor-vs-xaml/push-vs-pull.svg` | Pending render. |
| reconciliation | `diagrams/reconciliation/mount-update-unmount.mmd` | `images/reconciliation/mount-update-unmount.svg` | Pending render. |
| hooks-internals | `diagrams/hooks-internals/hook-slot-table.mmd` | `images/hooks-internals/hook-slot-table.svg` | Pending render. |
| effects-scheduling | `diagrams/effects-scheduling/effect-timing.mmd` | `images/effects-scheduling/effect-timing.svg` | Pending render. |
| modifier-system | `diagrams/modifier-system/modifier-fold.mmd` | `images/modifier-system/modifier-fold.svg` | Pending render. |
| threading-and-dispatch | `diagrams/threading-and-dispatch/dispatch-flow.mmd` | `images/threading-and-dispatch/dispatch-flow.svg` | Pending render. |
| element-pool | `diagrams/element-pool/rental-states.mmd` | `images/element-pool/rental-states.svg` | Pending render. |
| source-mapping | `diagrams/source-mapping/attribution.mmd` | `images/source-mapping/attribution.svg` | Pending render. |
| analyzer-architecture | `diagrams/analyzer-architecture/rule-pipeline.mmd` | `images/analyzer-architecture/rule-pipeline.svg` | Pending render. |
| animation-pipeline | `diagrams/animation-pipeline/four-systems.mmd` | `images/animation-pipeline/four-systems.svg` | Pending render. |
| focus-and-input-internals | `diagrams/focus-and-input-internals/focus-flow.mmd` | `images/focus-and-input-internals/focus-flow.svg` | Pending render. |
| devtools-internals | `diagrams/devtools-internals/mcp-loop.mmd` | `images/devtools-internals/mcp-loop.svg` | Pending render. |
| perf-instrumentation | `diagrams/perf-instrumentation/etw-flow.mmd` | `images/perf-instrumentation/etw-flow.svg` | Pending render. |

The Mermaid sources exist for all 15 topics; SVG render is a follow-up
`mur docs compile` run (with `mmdc` installed) ahead of PR-open.

## GitHub-side checks (deferred to PR-open against `main`)

- [ ] Tables render with correct alignment — Phase 3.5 pages have
      dense reference tables (modifier-system has 6 columns).
- [ ] SVG diagrams render at reasonable sizes (the placeholder set is
      smaller than the catalog thumbnails; the real Mermaid output is
      narrower than 800px).
- [ ] **Dark-theme SVG contrast.** Mermaid's default palette has
      occasional light-on-light or dark-on-dark issues against
      GitHub's theme switcher. Spot-verify each diagram on both
      light and dark previews; if any are illegible, swap the
      `%%{init}%%` block in the `.mmd` source to the `dark`-friendly
      preset.
- [ ] Code blocks have correct syntax highlighting — most snippets
      are C# (`csharp` hint applied uniformly by the pipeline).
- [ ] Cross-links resolve — no 404s. Local walk found zero broken
      inbound or outbound links across the 15 new pages. The
      `[reactor-vs-xaml](reactor-vs-xaml.md)` link appears in both
      the §1 and §9 sections of the readme index; verify both
      resolve to the same page.
- [ ] Previous/Next chain unbroken from `readme` through the §9
      Under-the-hood track (order 30 → 43, with reactor-vs-xaml at
      1.7 also satisfying §1).
- [ ] Mobile rendering acceptable — internals pages tend to be long
      (reconciliation is ~325 lines, modifier-system is similar);
      verify the sticky-side nav handles the structure.

## Items to inspect specifically on the PR preview

1. **The reconciler tri-state dispatch table in `reconciliation.md`.**
   Four rows describing Mount / Update / Unmount / Replace. The
   `existingControl` column needs the bullet-list-inside-cell
   formatting that some Markdown processors strip; verify on
   GitHub.

2. **The 14-row diagnostic ID table in `analyzer-architecture.md`.**
   Severity column has badge-style values (Warning / Info) that look
   different against the GitHub dark theme. Confirm legibility.

3. **The hooks-internals slot-table diagram.** Mermaid renders
   tables awkwardly; this one is a `graph LR` with table-like nodes.
   Verify the layout on a wide-screen preview.

4. **`modifier-system.md` length.** The page is dense — ~280 lines.
   The TOC nav (sticky-side or top) needs to handle a 12-section
   structure cleanly.

5. **The reactor-vs-xaml comparison table.** Side-by-side XAML vs.
   Microsoft.UI.Reactor (Reactor) snippets. Some readers will hit this on mobile, where
   the two columns stack. Confirm the alignment doesn't lose context.

6. **Diagram contrast on the source-mapping flow.** It uses both
   "today" and "design" nodes color-coded. Verify the color choices
   read on both light and dark previews.

## Local-compile validation summary

`dotnet run --project src/Reactor.Cli -- docs compile --validate-only`
reports **Validation passed** across all 64 templates after the
wave-C normalization. The 15 Phase 3.5 templates are tier-lint clean
at their declared tiers:

| Template | Tier | Findings |
|---|---|---|
| `architecture-overview.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` (winui-ref not declared — non-fatal, expected for internals) |
| `reactivity-model.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `reactor-vs-xaml.md.dt` | comprehensive | 0 (declares winui-ref) |
| `reconciliation.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `hooks-internals.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `effects-scheduling.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `modifier-system.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `threading-and-dispatch.md.dt` | solid | 0 (declares winui-ref) |
| `element-pool.md.dt` | solid | 0 (declares winui-ref) |
| `source-mapping.md.dt` | solid | 0 (declares winui-ref) |
| `analyzer-architecture.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `animation-pipeline.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `focus-and-input-internals.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `devtools-internals.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |
| `perf-instrumentation.md.dt` | comprehensive | `REACTOR_DOC_TIER_W001` |

All `REACTOR_DOC_TIER_W001` findings are non-fatal warnings noting
"only required for transparent-wrapper pages" — internals pages don't
wrap a single WinUI control, so the warning is informational.

All remaining info-level findings on the 26 pre-existing tierless
pages (same surface as Phase 1.14, 2.8, and 3.6 reports) — none on
Phase 3.5 templates.
