# Phase 3 — Retro

Captured 2026-05-17. Phase 3 lifts the controls-catalog detail pages
from stubs (or, for forms/collections, from Solid) to their target
tiers: three new Comprehensive/Solid pages and two Solid → Comprehensive
expansions. Five commits, ~3,800 lines of template + doc-app code, no
rework.

## Scope shipped

| Task | Page | Tier | Notes |
|---|---|---|---|
| 3.1 | text-and-media | Comprehensive | 9 controls; Markdown is the Reactor-original; InkCanvas flagged as not yet wrapped. |
| 3.2 | status-and-info | Solid | 9 components (InfoBar × 2 + 7 others); switched off deprecated `ProgressBar` factory. |
| 3.3 | dialogs-and-flyouts | Comprehensive | 7 components; ContentDialog three-button shape + gated primary + commanding integration end-to-end. |
| 3.4 | forms (expand) | Solid → Comprehensive | 5 new components (AutoSuggestBox, DatePicker/CalendarDatePicker, TimePicker, CalendarView, ColorPicker); existing controls preserved. |
| 3.5 | collections (expand) | Solid → Comprehensive | 3 new sections (grouping recipe, drag-reorder via `.Set`, lazy-loading deep dive); `itemHeight` vs. `estimatedItemHeight` caveat. |

## What surprised us

1. **The Markdown factory is `Markdown(string)`, not `MarkdownTextBlock`.**
   The spec and the AI Author Skill both referenced "MarkdownTextBlock"
   from the WinUI Community Toolkit naming. The Reactor surface is
   `Markdown(string)` and `Markdown(string, MarkdownOptions)`, both
   returning a generic `Element` rooted at a `Markdown.MarkdownBuilder`
   composition. The text-and-media page documents it under "Markdown
   (Reactor-original)" with that signature. Updating the spec
   reference is a Phase 4 polish item.

2. **InkCanvas is not wrapped.** The spec includes it in the
   text-and-media surface; the codebase has no `InkCanvasElement` and
   no factory in `Dsl.cs`. The catalog row is marked
   "Not yet wrapped — track in spec TBD" per the §3.6 acceptance.
   Spec number can land when InkCanvas wraps; for now the marker
   keeps the gap visible without blocking Phase 3 exit.

3. **`ProgressBar` is obsolete.** First-pass doc-app code used
   `ProgressBar(double)` / `ProgressBar()` per the existing AI Author
   Skill, but the codebase has marked both `[Obsolete]` per spec 039 §5
   in favor of `Progress(double)` / `ProgressIndeterminate()`. The
   status-and-info app caught this via warnings-as-errors; the page
   documents the new factories and notes the migration. Phase 4 should
   sweep the AI Author Skill table.

4. **Grouping and drag-reorder are recipe-only, not first-class.**
   Reactor doesn't ship a grouped-list factory or a `.CanReorder()`
   fluent. Both work via composition — grouping via a `VStack` of
   `header + items` per group, drag-reorder via the existing WinUI
   `CanReorderItems` / `AllowDrop` / `CanDragItems` plumbed through
   `.Set(...)`. The collections page documents the composition shape
   rather than implying a missing API. A first-class
   `.CanReorderItems()` fluent on `ListViewElement` is a clear Phase 4
   ergonomics win.

5. **CommandBarFlyout uses `AppBarItemBase` arrays, not modifiers.**
   The flyout takes its commands as constructor arguments —
   `primaryCommands`, `secondaryCommands` — both arrays of
   `AppBarItemBase` records (`AppBarButton(label, onClick, icon?)`,
   `AppBarToggleButton`, etc.). This is consistent with `CommandBar`
   and the same `AppBarItemBase` array fills both surfaces
   interchangeably. The dialogs-and-flyouts page documents this with
   a worked snippet.

6. **`UseCommand(Command)` is the right shape, not `UseCommand(...args)`.**
   The Reactor pattern is to construct a `new Command { Label = ...,
   Execute = ... }` record and pass it to `RenderContext.UseCommand`
   (for async lifecycle tracking). Sync commands need no wrapping —
   they pass through unchanged. The dialogs-and-flyouts page uses the
   record-construction shape.

## Deferred

| Item | Reason | Where tracked |
|---|---|---|
| Real screenshot capture | Phase 2 deferred catalog-thumb capture pending a CI runner with the WinAppSDK installed. Phase 3 inherits the same constraint. Every page ships with placeholder 320×240 PNGs at the expected `docs/guide/images/<topic>/<id>.png` slot so the broken-image lint stays green; real captures land when the harness runs in CI. | Spec §10.3 ops follow-up |
| InkCanvas wrapper | Surface needs design before the wrapper lands (ink stroke serialization, pointer-pressure semantics, persistence shape). Marked "Not yet wrapped — track in spec TBD" in the catalog. | Catalog row |
| First-class drag-reorder fluents | Doc surface uses `.Set` passthrough — workable but verbose. A `.CanReorderItems()` / `.OnReorder(Action<...>)` fluent on `ListViewElement` would compress the snippet. | Phase 4 ergonomics |
| First-class grouped-list | Composition-based today. A `GroupedListView<T,K>` factory with built-in sticky headers is a worthwhile add. | Phase 4 / spec TBD |
| AI Author Skill: rename MarkdownTextBlock → Markdown | The skill's Topic Ideas table still uses `MarkdownTextBlock`; the canonical factory is `Markdown(string)`. | Phase 4 cross-link sweep |
| Inbound `<!-- ref: -->` markers from existing pages into Phase 3 surfaces | Same as Phase 2 — Phase 4 cross-link sweep handles this. | Phase 4 |

## Process notes

- **Background-task interleaving stayed disciplined.** All `dotnet
  build` invocations against `Reactor.csproj` ran serially (per the
  `feedback_test_parallelism` note). The doc-app builds themselves are
  cheap (~7–20s each); the cost is the first build that re-resolves
  the Windows App SDK reference assemblies.
- **Compile-validate loop remains the right granularity.** A single
  `mur docs compile --validate-only` after each template edit
  surfaces tier-lint issues in < 2s. Phase 3 hit `REACTOR_DOC_TIER_*`
  findings exactly twice across all five commits — both caught on the
  first validate run, both fixed by structural moves (table position,
  cross-link count) rather than content rewrites.
- **Per-control template (spec §6.3) survived contact with reality.**
  Factory signature → default screenshot → modifier table → customized
  snippet → WinUI-Learn link → "Don't" anti-pattern was the right
  scaffold for every Comprehensive control on the page. Solid pages
  drop the "Don't" requirement and the customized variant, keeping
  factory signature / modifier table / screenshot / Learn link.
- **Voice held against the anti-slop checklist.** No "Let's dive in!"
  intros. Every page leads with mental-model or a concrete shape
  table. Caveats name specific failure modes (commit-on-blur input
  → stale Submit-disabled trap; itemHeight unset → measure storm;
  Image decode on UI thread → blocked frame). Tips are 3–5
  bold-lead bullets. Reads consistently with the Phase 2 set when
  walked end-to-end.
- **Doc-app warnings as errors works.** Two pre-existing duplicate
  `using` warnings on `forms/App.cs` surfaced when I rebuilt the app
  for the 3.4 expansion; cleaning them up was a one-line drop. The
  status-and-info app surfaced the `[Obsolete]` `ProgressBar` factory
  the same way. Warnings-as-errors prevented two doc-rot signals from
  shipping.

## Phase 3 exit-gate verification

Per task §3.6:

- [x] Tier-lint clean across all 5 templates at their target tiers.
- [x] Doc-app builds clean against `Reactor.csproj` (all five apps:
      text-and-media / status-and-info / dialogs-and-flyouts / forms /
      collections).
- [x] Catalog index `controls.md.dt` updated — "Detail page (Phase 3)"
      markers replaced with real links to the three new pages; forms
      and collections were already direct-linked.
- [x] Generated `docs/guide/*.md` regenerated.
- [x] Task-list checkboxes updated.
- [x] InkCanvas explicitly flagged in the catalog as not yet wrapped.
- [x] Phase-3 retro + render report committed.
- [ ] GitHub preview render check — deferred to PR-open against `main`
      per the Phase 1/2 deferral pattern.
