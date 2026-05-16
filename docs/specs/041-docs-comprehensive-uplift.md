# 041 — Docs Comprehensive Uplift

**Status:** Draft
**Date:** 2026-05-16
**Owner:** TBD
**Related:**
- [013 — Doc System Design](013-doc-system-design.md) (pipeline that compiles `.md.dt` → `docs/guide/`)
- [021 — Docs Reorganization](021-docs-reorganization.md) (folder layout)
- [docs/_pipeline/ai-author-skill.md](../_pipeline/ai-author-skill.md) (template & app contract)
- [docs/research/compare/overview.md](../research/compare/overview.md) (19-category scorecard)

---

## 1. Problem

Reactor has 26 pages of user-facing documentation in `docs/guide/` (~8,800
lines). Quality is uneven and several pages are stubs. Cross-cutting topics
that are first-class in competitor docsets — testing, persistence, a
controls catalog, theming tokens, error handling, packaging — are
missing entirely. Topics that the AI Author Skill lists as part of
Reactor's API surface (Markdown control, date/time pickers, AutoSuggestBox,
ColorPicker, MediaPlayerElement, InkCanvas, BreadcrumbBar, ContentDialog,
TeachingTip, Backdrop, `.ScrollLinked`, `UsePersisted`, `Command<T>`) have
no narrative coverage and won't surface to users who don't already know
to grep for them.

The comparison docset at `docs/research/compare/` rated Reactor on 19
capability categories. Several of those capability ratings (B+ in
Navigation, B in Accessibility, B+ in Data Loading, A- in Interop) are
not legible to a new user landing on the docs site because the docs
under-sell what the framework can do, gate features behind discovery,
or split a single concept across multiple pages without an index.

We need a systematic uplift, treated as a regular product feature: spec'd,
ranked, tracked, with explicit "definition of done" for each page tier.

## 2. Goals

1. **Every Reactor capability has a discoverable home.** A user who knows
   the WinUI control name (or React/SwiftUI/Compose equivalent) can find
   the Reactor doc for it via the index in two clicks.
2. **Every doc page meets one of three quality tiers** (see §6) with a
   uniform structure that mirrors competitor docs (React `Caveats`,
   SwiftUI `Discussion`, Compose `Phases`, WinUI thumbnail catalog).
3. **WinUI is linked, not duplicated.** Where a control is a transparent
   WinUI wrapper, the Reactor page covers Reactor-specific usage (factory
   signature, modifiers, hooks, common patterns) and links to the WinUI
   design / API page for the underlying control's design guidance,
   accessibility behavior, and full property surface.
4. **Reactor-original surface area is documented exhaustively.**
   `DataGrid`, `MarkdownTextBlock`, `VirtualList`, `FlexPanel`,
   `ErrorBoundary`, `AccessibilityScanner`, `UseFocusTrap`, `UseAnnounce`,
   `Command` system, async resources, navigation hooks, and devtools —
   none of these exist in WinUI, so they get full first-class pages.
5. **Sequential learning path works end-to-end.** A reader can start at
   `readme.md` and traverse Previous/Next links across all topics without
   hitting a dead end or out-of-order prerequisite.

## 3. Non-goals

- Rewriting the doc pipeline. The `.md.dt` template + Reactor-app + screenshot
  flow stays as-is (spec 013).
- Hand-editing `docs/guide/*.md`. Templates are the source of truth
  ([feedback_docs_pipeline]: never hand-write generated guide files).
- Translating docs. Localization of the docset itself is out of scope;
  we'll keep authoring in English.
- Adding a search index or static-site build (Docusaurus / VitePress /
  custom). Those are downstream and tracked separately. GitHub-rendered
  Markdown is the deployment target.
- Preserving `docs/reference/` as a user-facing parallel docset. The
  Under-the-hood track (§7.1.1) absorbs the user-facing portion. What
  remains in `docs/reference/` becomes framework-contributor-only
  process material (test suite, release process, source tree layout,
  CI matrix). The existing reference files (`async-system.md`,
  `localization-*.md`, `mur-check-did-you-mean.md`) either get promoted
  into the guide or are demoted to contributor-only — case by case.

---

## 4. Industry shape we're patterning after

From the agent audit of React (react.dev), SwiftUI (Apple Developer),
Jetpack Compose (developer.android.com), and WinUI 3 (Microsoft Learn),
the common shape is:

| Bucket | Purpose | Reactor equivalent |
|--------|---------|--------------------|
| Get Started | Quick start, hello world, capstone tutorial, "Thinking in X" essay | `getting-started.md` + new `thinking-in-reactor.md` |
| Learn (chaptered) | Mental-model narrative — describing UI, interactivity, state, escape hatches | `components.md`, `hooks.md`, `effects.md`, `advanced.md` (re-chaptered) |
| Controls catalog | Thumbnail-indexed reference for every control / element | **new** `controls.md` index + per-control pages |
| API reference | Uniform Parameters / Returns / Usage / Caveats template | Inline reference tables in topic pages (kept) |
| Recipes / cheat sheet | Sample app gallery + cheat-sheet card | **new** `recipes.md`, **new** `cheat-sheet.md` |
| Migration / interop | UIKit↔SwiftUI etc. | `xaml-developers.md`, `winforms-interop.md`, **new** `wpf-interop.md` |
| Tooling | Setup, IDE, devtools | `dev-tooling.md` (expanded), `devtools-ux.md` merged in |
| Idioms / rules | "Rules of React", anti-patterns | **new** `rules-of-reactor.md` |

Distinctive shapes we're stealing:
- **React `Caveats` callouts** on each hook / API.
- **SwiftUI `Discussion` sections** — long-form prose under each
  symbol explaining when/why to use it.
- **Compose cheat sheet** — single-page reference card.
- **WinUI thumbnail control table** — visual control index.

---

## 5. Current state ranking

Audit ranks each existing guide doc into one of three tiers and groups by
competitor-comparison category. See [doc-audit-2026-05.md](041/doc-audit-2026-05.md)
for the per-file scorecard (created alongside this spec).

### 5.1 Tier summary

| Tier | Definition | Count |
|------|------------|-------|
| **Comprehensive** | Progressive examples, full reference tables, tips, troubleshooting, cross-links, edge cases | 11 |
| **Solid** | Multiple examples, modifiers covered, some edge cases | 11 |
| **Thin** | One example + a few paragraphs; key features omitted | 4 |

**Thin pages (Sprint-1 priority):** `dev-tooling.md`, `devtools-ux.md`,
`readme.md` (intentionally thin, but lacks decision-tree / roadmap),
`async-resources-cookbook.md` (only "thin" because it's standalone
without effects-page cross-context — quality is fine).

### 5.2 Per-category strongest / weakest

| Category | Strongest | Weakest | Action |
|----------|-----------|---------|--------|
| Core / fundamentals | `hooks.md` | `effects.md` (no UseResource cross-link) | Expand effects.md, add async-error patterns |
| Controls reference | `navigation.md` | `forms.md` (no date / time / picker / color / autosuggest) | New `controls.md` catalog + expand forms |
| Reactor-unique | `accessibility.md` | `commanding.md` (no Command\<T\>, no flyouts) | Expand commanding |
| Cross-cutting how-to | `animation.md` | `dev-tooling.md` (no `mur`, no MCP, no VS Code panel) | Promote dev-tooling, merge devtools-ux |
| Platform / migration | `xaml-developers.md` | `winforms-interop.md` (no WPF parallel, no data flow) | New `wpf-interop.md`, expand winforms |

### 5.3 Missing-entirely list

Controls referenced in `ai-author-skill.md` or shipping in
`src/Reactor/Controls/**` but with **no doc home**:

- **MarkdownTextBlock** — Reactor-original Markdown renderer
- **DatePicker / CalendarDatePicker / CalendarView / TimePicker**
- **AutoSuggestBox**
- **ColorPicker**
- **MediaPlayerElement, InkCanvas, MapControl, WebView2**
- **RichEditBox, RichTextBlock**
- **RatingControl, PersonPicture, PipsPager**
- **ContentDialog, TeachingTip, MenuFlyout, CommandBarFlyout**
- **InfoBar, InfoBadge, ProgressBar, ProgressRing**
- **BreadcrumbBar, SplitView, SelectorBar**

Concepts with **no doc home**:

- **Testing Reactor apps** (renderer fixtures, headless tests, snapshot tests)
- **`UsePersisted`** (zero coverage despite first-class hook status)
- **Theming token reference** (37+ tokens promised, ~10 enumerated)
- **Backdrop / Mica / Acrylic** (`.Backdrop()` modifier — no narrative)
- **Custom hooks authoring**
- **WPF interop** (parallel to WinForms)
- **Packaging & distribution** (MSIX, single-file, ARM64, AOT)
- **Performance & profiling** (top-down ETW / `EventDispatch` tutorial)
- **Error handling beyond ErrorBoundary** (async patterns, telemetry)
- **`.ScrollLinked` animation, AnimatedIcon, reduced-motion**
- **`Command<T>`, context menus, global accelerators**

---

## 6. Quality tiers (definition of done)

Every page targets a tier. The compile checklist gates landing each tier.

### 6.1 Tier "Stub" (allowed only as a placeholder)

- Title, one-paragraph description, "Coming soon" notice, link to API
  surface in `ai-author-skill.md` so it's discoverable.
- No code examples required. Stubs MUST appear in the readme index so
  the surface area is visible even when incomplete.

### 6.2 Tier "Solid" (default new-page bar)

- YAML front-matter with `title`, `app`, `order`, `audience`, `goal`.
- **Lead snippet within first 30 lines.** Code first, prose second.
- At least 3 progressive examples from a working Reactor doc app
  (`docs/_pipeline/apps/<topic>/`) — each snippet pulled via
  `snippet="<topic>/<id>"`.
- At least 1 screenshot per major example, captured via `doc-manifest.yaml`.
- A reference table (modifier list, hook signatures, or control options).
- A "Tips" section: 3-5 bullets, bold lead sentence + paragraph.
- A `## Next Steps` section with 3-5 inline links + Previous/Next by
  topic order.
- All `snippet=` and `screenshot://` references resolve; doc app compiles.

### 6.3 Tier "Comprehensive" (target for top-traffic pages)

Everything in Solid, plus:

- **Mental-model paragraph** at the top — the SwiftUI `Discussion`
  treatment. Why this concept exists, when to reach for it, what it
  replaces in classic WinUI / WPF / React.
- **Caveats / gotchas callouts** inline (React-style) for non-obvious
  behavior: lifecycle order, threading, performance, accessibility
  side-effects.
- **At least one "Patterns" subsection** with a real-world recipe
  (login form, paginated list, settings page, etc.).
- **At least one "Common mistakes" subsection** — anti-patterns with
  the right alternative shown.
- **Cross-links to related topics inline in prose**, not only in Next Steps.
- **WinUI link** when relevant — link to the official Microsoft Learn
  page for the underlying control, framed as "for design guidance and
  full property reference, see X."
- **Migration callout** (where applicable) — "Coming from WPF /
  WinUI XAML, this is how Style / ResourceDictionary / DataTemplate
  maps."

Per-control pages additionally need:

- **Thumbnail screenshot** suitable for the controls catalog index.
- **Factory signature** with all overloads.
- **Modifier table** (chainable modifiers specific to that control).
- **WinUI link** to design guidance.
- **At least one "Don't" example** (anti-pattern + correct version).

---

## 7. Target shape

### 7.1 Top-level navigation (`docs/guide/readme.md` index)

```
1. Get Started                   [XAML developers are a primary audience —
                                  surface them here, do not bury]
   ├── readme (landing)
   ├── getting-started
   ├── thinking-in-reactor       [NEW]
   ├── xaml-developers           (migration cookbook — surfaced as
   │                              first-class welcome ramp for the
   │                              largest expected adopter pool)
   └── reactor-vs-xaml           [NEW] (architectural essay
                                  cross-linked from §7.1.1; appears in
                                  Get Started AND Under-the-hood so
                                  XAML readers find it immediately)

2. Learn the framework
   ├── components
   ├── hooks
   ├── effects
   ├── context
   ├── commanding
   └── advanced                  (ErrorBoundary, Memo, .Set, custom hooks)

3. UI surface
   ├── layout
   ├── flex-layout
   ├── styling
   ├── animation
   └── input-and-gestures

4. Controls catalog              [NEW PAGE + INDEX]
   ├── controls                  (thumbnail index, links into below)
   ├── forms                     (TextField, CheckBox, Slider, NumberBox,
   │                              ComboBox, RadioButtons, ToggleSwitch,
   │                              PasswordBox, AutoSuggestBox,
   │                              DatePicker, TimePicker, CalendarView,
   │                              ColorPicker)                [EXPAND]
   ├── collections               (ListView, GridView, LazyVStack,
   │                              VirtualList; grouping recipes)
   ├── text-and-media            [NEW] (TextBlock variants, RichTextBlock,
   │                              RichEditBox, MarkdownTextBlock, Image,
   │                              MediaPlayerElement, WebView2, InkCanvas,
   │                              MapControl)
   ├── status-and-info           [NEW] (InfoBar, InfoBadge, ProgressBar,
   │                              ProgressRing, TeachingTip, PipsPager,
   │                              PersonPicture, RatingControl)
   ├── dialogs-and-flyouts       [NEW] (ContentDialog, MenuFlyout,
   │                              CommandBarFlyout, Popup)
   ├── data-system               (DataGrid)
   └── charting

5. App architecture
   ├── navigation
   ├── windows                   (moved from "UI surface" — multi-window,
   │                              tray, OpenWindow, lifecycle is
   │                              architecture, not layout)
   ├── async-resources           (renamed from async-resources-cookbook)
   ├── persistence               [NEW] (UsePersisted, scopes, migration)
   ├── localization
   └── accessibility

6. Patterns & recipes            [NEW SECTION — recipes/ is a FOLDER]
   ├── recipes/                  [NEW FOLDER]
   │   ├── index.md              (gallery index)
   │   ├── login.md
   │   ├── master-detail.md
   │   ├── settings-page.md
   │   ├── paginated-list.md
   │   ├── modal-dialog.md
   │   ├── multi-step-form.md
   │   ├── search-with-suggestions.md
   │   ├── command-palette.md
   │   ├── drag-reorder.md
   │   └── (add as patterns emerge — folder scales without nav churn)
   ├── cheat-sheet               [NEW] (single-page API card)
   ├── rules-of-reactor          [NEW] (hook rules, idioms, anti-patterns)
   └── theming-tokens            [NEW] (full token catalog + visual swatches)

7. Tooling & process
   ├── dev-tooling               [EXPAND — merge devtools-ux]
   ├── testing                   [NEW]
   ├── performance               [NEW]
   └── packaging                 [NEW]

8. Interop & integration
   ├── winforms-interop
   └── wpf-interop               [NEW]
   (xaml-developers lives in Section 1 — XAML is a primary entry path,
    not a side-interop concern. Cross-link from here.)

9. Under the hood                [NEW SECTION — go deep, this is OSS]
   ├── architecture-overview     [NEW]
   ├── reactivity-model          [NEW]
   ├── hooks-internals           [NEW]
   ├── reconciliation            [NEW — supersedes docs/reference/reconciliation.md]
   ├── element-pool              [NEW]
   ├── effects-scheduling        [NEW]
   ├── threading-and-dispatch    [NEW]
   ├── source-mapping            [NEW]
   ├── modifier-system           [NEW]
   ├── analyzer-architecture     [NEW]
   ├── devtools-internals        [NEW]
   ├── animation-pipeline        [NEW]
   ├── focus-and-input-internals [NEW]
   └── perf-instrumentation      [NEW]

10. API Reference                [NEW SECTION — auto-generated, §7.1.2]
    ├── factories                (Factories class — every element factory)
    ├── hooks                    (every Use* hook)
    ├── modifiers                (every chainable modifier)
    ├── elements                 (every Element record type)
    └── system                   (App, Window, Navigation, Context, etc.)
```

**Net delta:** 26 → ~64 user-facing pages (excluding auto-generated
reference). 38 new, 6 existing pages substantially expanded, 1 merge
(`devtools-ux` → `dev-tooling`), 1 rename (`async-resources-cookbook`
→ `async-resources`), 1 move (`windows.md` to App architecture).
Section 10 (API Reference) adds another ~150-300 auto-generated pages
on top.

### 7.1.0 Images and diagrams policy

**Default: SVG, not ASCII art.** This docset is published on GitHub
which renders SVG inline in Markdown. ASCII boxes-and-arrows diagrams
are difficult to maintain, accessibility-hostile (screen readers
narrate raw characters), and look poor relative to the SVG-rendering
competitor docs we benchmarked against. SVG handles arbitrary scale,
respects light/dark themes via CSS, and is plain-text diff-friendly
in PRs.

Rules:

- **Architectural diagrams, flowcharts, state machines, reconciler
  walks, hook-table snapshots** — author as SVG and reference via
  `![Alt text](images/<topic>/<diagram-id>.svg)`.
- **Screenshots of running apps** — stay PNG as today. SVG is for
  vector content authored by hand or generated from source-of-truth
  (e.g. Mermaid → SVG at build time).
- **ASCII diagrams** are allowed inline only when (a) ≤8 lines,
  (b) the visual is purely structural (folder tree, code skeleton),
  and (c) no spatial precision is needed. Anything else is SVG.
- **Dark-mode aware** — every SVG should use CSS variables or
  duplicate paths so the diagram is readable on both GitHub's light
  and dark themes. The default is light/dark-agnostic palette.
- **Hand-authored or Mermaid-sourced.** Mermaid `.mmd` files live
  alongside their `.svg` outputs in `docs/_pipeline/diagrams/<topic>/`;
  `mur docs compile` renders Mermaid → SVG at build time and writes
  to `docs/guide/images/<topic>/`. Hand-authored SVG files can also
  live directly in `docs/_pipeline/diagrams/` and are copied through.

Pipeline implications (see §10.1):

- `mur docs compile` must copy `.svg` outputs into
  `docs/guide/images/<topic>/` alongside the existing screenshot copy.
- Mermaid rendering needs a Node-based renderer (`@mermaid-js/mermaid-cli`)
  available on the build machine; CI environments may need this added
  to the toolchain.
- The `ai-author-skill.md` template guidance needs to add: "for any
  architectural concept that benefits from a diagram, emit a Mermaid
  block in `docs/_pipeline/diagrams/<topic>/<id>.mmd` and reference
  the resulting SVG in the template."

### 7.1.1 Under the hood — deep dive track

Many early adopters — especially those coming from XAML/WinUI — want to
understand not just *how* to write Reactor but *why it works the way it
does*. The Learn section (§7.1 Section 2) covers the surface programming
model; this section dives into the runtime. It targets readers who would
otherwise read the source code to satisfy curiosity.

**Audience policy: go deep.** Reactor is open source. Curious readers
will read the source either way; we'd rather they read the source
*after* a well-written prose walkthrough than instead of one. There is
no upper bound on depth in this section. The only material that stays
out of `docs/guide/under-the-hood/` is framework-contributor-only
process material — how to run the test suite, where the source tree
lives, how releases are cut, the CI matrix. That stays in
`docs/reference/`. Everything that explains *what the runtime does
and why* belongs in the guide.

Source material partially exists in `docs/reference/` (hand-written,
not through the doc pipeline). This track **absorbs and supersedes**
those files — `docs/reference/reconciliation.md` and
`docs/reference/state-and-hooks.md` are promoted into the guide and
the originals deleted. `docs/reference/async-system.md` (1,263 lines,
already comprehensive) is linked to from `effects-scheduling.md` and
remains as the deeper internal reference for now, with a planned
absorption into the guide later if the under-the-hood track demands
it.

Every page in this track:
- Has a Mermaid/SVG architecture diagram at the top.
- Pulls real snippets from `src/Reactor/` via the new
  `snippet="source:<path>#<region>"` directive (§10.1).
- Ends with a "Read the source" callout linking to the relevant
  directory.
- Cross-links liberally — these pages reference each other heavily.

| Page | Tier | Covers |
|------|------|--------|
| `architecture-overview.md` | Comprehensive | The big picture: declarative shell → element records → reconciler → WinUI tree. The render loop. Where each subsystem lives in the codebase. The component / hook / element / control four-layer model. Diagram-led. |
| `reactivity-model.md` | Comprehensive | What "reactive" means in Reactor. The state-setter → re-render mental model. Why hooks instead of `INotifyPropertyChanged`. ShouldUpdate. Memo. Why no auto-tracking today (and where SwiftUI/Compose go that Reactor doesn't, yet). Comparison vs MVVM observable property change. |
| `hooks-internals.md` | Comprehensive | How `UseState` actually stores its value. The hook slot table. Why hooks must be called in order. The dispatcher / current-component pattern. Closure capture and the "stale state" problem. Custom hooks composition. ThreadSafe setters. How `UseEffect` queues. How `UseMemo` keys. How `UseRef` differs. |
| `reconciliation.md` | Comprehensive | Element-record diff against the previous tree. Identity and `WithKey`. In-place mutation vs replace. Bitmask property diffs. The reconciler's three phases (reconcile, mount/unmount, commit). Special cases: text, lists, conditional rendering. What `ForEach` actually does. Identity-preservation invariants. Promotes `docs/reference/reconciliation.md`. |
| `element-pool.md` | Solid | Allocation reduction (spec 034). Why element records are pooled. What gets pooled, what doesn't. Why this matters for GC pressure in scroll-heavy lists. |
| `effects-scheduling.md` | Comprehensive | When effects run relative to render and commit. Dependency-array semantics (referential vs value equality). Cleanup ordering. Mount vs update vs unmount. Async effects and cancellation. Links to `docs/reference/async-system.md` for `UseResource` / `QueryCache` internals. |
| `threading-and-dispatch.md` | Solid | UI thread invariants. Trampoline dispatch. Batched renders. How `setX` from a background thread is marshaled. Why `UseEffect` callbacks run on the UI thread. Off-thread work patterns. |
| `source-mapping.md` | Solid | Spec 010 in plain language: how stack traces and devtools attribute work back to user source. Why this exists. What it enables (per-component perf attribution, layout-cost overlay, reconcile-highlight overlay). |
| `modifier-system.md` | Comprehensive | How `.FontSize(24).Bold().Padding(8)` actually works. Modifier records vs immediate property writes. The chained-builder pattern. How modifiers compose with reconciliation (modifiers are part of the element record diff). Custom modifier authoring. Why modifiers aren't extension methods that mutate. |
| `analyzer-architecture.md` | Comprehensive | How REACTOR_THEME_*, REACTOR_A11Y_*, REACTOR_GRID_001, REACTOR_FUNC_001, REACTOR_PERSIST_001 work. Roslyn source generators vs analyzers. Where each diagnostic is defined. Authoring a new analyzer for your own conventions. |
| `devtools-internals.md` | Comprehensive | How the in-app dev menu observes the runtime. The reconcile-highlight overlay's draw cycle. Layout-cost overlay (ETW-sourced, attributed by spatial bounds). MCP server protocol surface. How `dotnet watch` integrates. |
| `animation-pipeline.md` | Comprehensive | Composition API end-to-end. Why we have 4 animation systems (implicit / compositor / layout / keyframes) and which one runs on which thread. The 5-compositor-property ceiling and why animating width is hard. `WithAnimation` ambient scope mechanics. `.Transition()` enter/exit machinery. |
| `focus-and-input-internals.md` | Comprehensive | UseFocus dispatcher coalescing. UseFocusTrap container injection. Pointer/gesture event flow vs WinUI's routed events. Why `.OnTapped` exists and where it differs from `.OnPointerPressed`. AccessKey wiring. Tab order computation. |
| `perf-instrumentation.md` | Comprehensive | What's instrumented (every render, every effect, every reconciliation). ETW event sources. Frame-aligned sampling (spec 031). Layout-cost attribution (spec 032). How to read a flame graph of a Reactor app. How to reproduce a bench. |
| `reactor-vs-xaml.md` | Comprehensive | Side-by-side mapping for XAML developers. **DependencyProperty → element record + modifier**. **Binding → closure over state**. **DataTemplate → function component**. **Style → modifier composition / `.Resources()`**. **ResourceDictionary → ThemeRef + lightweight styling**. **VisualStateManager → `.InteractionStates()`**. **Storyboard → `.Animate()` / `WithAnimation`**. **INotifyPropertyChanged → hook re-render**. **MVVM ViewModel → component state + `UseObservable` bridge**. Frames the philosophical shift: pull-based binding evaluation vs push-based render-from-state. Complements `xaml-developers.md` (which is migration-recipe focused) by explaining the architectural *why*. **Lives in Section 1 (Get Started) AND Section 9 (Under the hood)** — same file, indexed twice. |

Note: `reactor-vs-xaml.md` and `xaml-developers.md` are distinct.
`xaml-developers.md` is "I have a XAML page; rewrite it as Reactor"
(migration cookbook). `reactor-vs-xaml.md` is "why is the binding
model fundamentally different" (architectural essay).
Both live in Section 1 (XAML developers are a primary audience and
should hit both immediately), with `reactor-vs-xaml.md` also indexed
under Under-the-hood for readers approaching from the architecture
direction.

### 7.1.2 API Reference — auto-generated from XML doc comments

A separate Section-10 reference axis, generated at compile time from
the canonical source: the XML doc comments authored alongside the code
in `src/Reactor/`.

**Why auto-generate.** Reactor's public API surface is ~500 members and
growing. Hand-maintaining reference tables in topic pages (current
approach) drifts within weeks of any API change. Reviewer feedback
loops (spec 033) and ongoing API scrubs (specs 039/040) churn the
surface faster than humans can keep tables current. XML doc comments
are already written alongside code — they are the canonical source.
Building MD reference pages from them keeps the docset in sync
automatically, and pushes the cost of API churn onto the analyzer that
already enforces XML doc presence rather than onto doc reviewers.

**What gets generated.** Every public type and member in:
- `src/Reactor/` → factories, elements, modifiers, hooks, App / Window / Navigation / Context / Command primitives
- `src/Reactor.Charting/` → chart factories and types
- `src/Reactor.Interop.WinForms/` → host control
- `src/Reactor.Analyzers/` → analyzer rules (one page per `REACTOR_*`
  diagnostic, source = the analyzer's `DiagnosticDescriptor`
  description)

Pages are grouped semantically (factories / hooks / modifiers /
elements / system), not alphabetically by namespace. Grouping is
declared in `docs/_pipeline/reference-map.yaml` — a registry that
maps types and members (by namespace prefix, type name pattern, or
fully-qualified cref) to a category. **No code attributes.** The
registry is the one place where doc-only categorization lives. A
sensible default ("everything under `Microsoft.UI.Reactor.Hooks` is
in Hooks unless overridden") keeps the file small; one-off overrides
are explicit lines in the YAML.

**Page shape (uniform template).** Modeled after SwiftUI's symbol pages
and React's reference pages:

```
# <Name>

<Signature>

## Summary
<XML doc <summary>>

## Parameters
<XML doc <param> for each>

## Returns
<XML doc <returns>>

## Discussion
<XML doc <remarks>>

## Examples
<XML doc <example> blocks, or pulled via snippet="source:..."
referenced from <see langword="snippet" cref="..."/>>

## Caveats
<XML doc custom <caveat> tags — new convention>

## See Also
<XML doc <seealso> links, rendered as inline page links>
```

**Hand-written intro per group.** Each Section-10 subdirectory has a
hand-authored `index.md` that introduces the group ("Hooks are how
components access state and effects...") and then lists the
auto-generated pages. The intro is human; the leaves are generated.

**Build phase.** `mur docs compile` gains a `--reference` step (or
runs by default) that:
1. Locates the `*.xml` doc files produced alongside each
   `Reactor*.dll` build output.
2. Parses them with the same XML-doc reader Roslyn uses.
3. Emits one `.md` file per public type/member into
   `docs/guide/reference/<category>/<name>.md`.
4. Resolves `<see cref="..."/>` to relative links into the same
   reference tree.
5. Validates that every public type/member has at least `<summary>` —
   missing entries fail the build (`REACTOR_DOC_001`).

**Relationship with hand-written pages.** The topic pages in Sections
1-9 stay narrative ("here's how UseState fits with effects") and
reference inline tables for the 3-5 hooks/modifiers they cover. The
Section-10 reference is the exhaustive index. Topic pages cross-link
into Section 10 for the full member list and parameter docs; reference
pages cross-link back to topic pages for tutorial-style usage.

**Why not Docusaurus / DocFX directly.** DocFX already does most of
this and we could host its output, but it imposes its own site
structure, theming, and search. Our pipeline is purposely lightweight
(MD + screenshots) and the GitHub-rendered docset is the deployment
target. A `mur docs compile --reference` step keeps the toolchain
homogeneous: same compile invocation, same output folder structure,
same lint, no second site generator. We may revisit if/when we adopt
a dedicated static-site generator.

### 7.2 Sequential order numbers

Order numbers in front-matter rebase to integers `0…N` after Phase 1.
Until then, new pages slot in as `.5` (e.g. `thinking-in-reactor` =
`order: 1.5`) to avoid renumbering churn during the uplift.

---

## 8. WinUI link policy

For controls that are transparent WinUI wrappers, Reactor's page covers:

- Factory signatures and overloads
- Reactor modifiers and hooks that apply
- Common controlled-input or event-handling pattern
- One screenshot of the default + one of a customized state
- Reactor-specific gotchas (commit-on-blur, FocusTrap interaction, etc.)

And **links** (does not duplicate) to Microsoft Learn for:

- Full property surface
- Design / Fluent guidance
- Accessibility behavior of the underlying control
- Platform-version availability

Link format in templates:

```markdown
> **WinUI reference:** For the full property surface and design guidance,
> see [DatePicker on Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/design/controls/date-picker).
```

For Reactor-original controls (`DataGrid`, `VirtualList`, `FlexPanel`,
`MarkdownTextBlock`, `ErrorBoundary`, `AccessibilityScanner`), the
Reactor page **is** the reference — no upstream link is appropriate.

---

## 9. Phased rollout

### Phase 1 — Foundation (Sprint 1-2, ~2 weeks)

Mostly pipeline/tooling work to unblock the content phases.

Deliverables:
- This spec accepted; per-file audit checked in alongside it.
- Page-template skeletons created for the three new quality tiers
  (stub / solid / comprehensive) in `docs/_pipeline/templates/_skeletons/`.
- `readme.md` rewritten with the new 10-section index, including
  placeholder links to all new pages (each new page filed as Stub tier).
- `dev-tooling.md` promoted Solid → Comprehensive; `devtools-ux.md`
  merged in and removed.
- Audit table linked from spec → readme so the surface area is visible.
- **Pipeline: image/SVG handling (§10.3).** `mur docs compile` copies
  SVG from `docs/_pipeline/diagrams/<topic>/` to
  `docs/guide/images/<topic>/`; Mermaid → SVG rendering wired up;
  CI install of `mermaid-cli` confirmed.
- **Pipeline: snippet source-tree (§10.2).** `snippet="source:..."`
  directive working end-to-end against `src/Reactor/`.
- **Pipeline: tier lint (§11).** `mur docs compile --validate-only`
  enforces the per-tier checklist.
- **Pipeline: reference generation prototype (§10.4).** End-to-end
  prototype on one category (Hooks) to validate the approach and size
  the generated diff. Categorization driven entirely by
  `docs/_pipeline/reference-map.yaml` — no code attributes. Confirms
  cref resolution (§10.4.1) round-trips correctly through GitHub
  Markdown rendering.
- **AI Author Skill updated (§10.5)** with new directives, SVG policy,
  source-snippet directive.

Exit: `mur docs compile --validate-only` passes for every page including
new stubs; one auto-generated reference category (Hooks) renders
correctly; one SVG-illustrated stub page round-trips through the
pipeline.

### Phase 2 — Reactor-unique gaps (Sprint 2-3, ~2 weeks)

These are pages with no upstream WinUI parallel — full ownership ours.
Sequence by traffic impact:

1. `controls.md` — catalog index (table with thumbnail + one-line per
   control, grouped by category)
2. `testing.md` — Solid tier
3. `theming-tokens.md` — Comprehensive (full token enumeration with
   swatches, generated from the theme source)
4. `persistence.md` — Solid (UsePersisted, both scopes, migration story)
5. `recipes.md` — Solid (8-10 focused recipes pulled from sample apps)
6. `cheat-sheet.md` — Solid (single-page reference card)
7. `rules-of-reactor.md` — Solid (hook rules, render purity, key idioms)

Exit: every Reactor-original concept that lives only in `ai-author-skill.md`
also has a user-facing page.

### Phase 3 — Controls catalog (Sprint 3-5, ~3 weeks)

Three new pages + two existing expansions, working through the catalog:

1. `text-and-media.md` — NEW, Comprehensive
2. `status-and-info.md` — NEW, Solid
3. `dialogs-and-flyouts.md` — NEW, Comprehensive
4. `forms.md` — expand to include date/time/color/autosuggest pickers
5. `collections.md` — expand with grouping, drag-reorder where supported

Each new page follows the per-control template from §6.3.

Exit: the Controls Catalog index has zero unlinked controls; every
control listed in `ai-author-skill.md` resolves to either a Reactor
page or an explicit "not yet wrapped — track in spec NNN" note.

### Phase 3.5 — Under-the-hood deep dive (Sprint 4-6, ~3 weeks)

Parallel to Phase 3 (controls catalog). Different author skillset — these
pages should be written by someone who has shipped renderer or hook
internals, not auto-drafted. Now 14 pages (depth ceiling lifted per §12.5).
Sequence by reader dependency:

1. `architecture-overview.md` — Comprehensive (entry point; sets up
   vocabulary; diagram-led)
2. `reactivity-model.md` — Comprehensive (the philosophical core;
   second-read after overview)
3. `reactor-vs-xaml.md` — Comprehensive (highest-value page for the
   XAML-developer audience; ALSO indexed in Section 1)
4. `reconciliation.md` — Comprehensive (promotes
   `docs/reference/reconciliation.md` and deletes the source)
5. `hooks-internals.md` — Comprehensive (promotes
   `docs/reference/state-and-hooks.md` and deletes the source)
6. `effects-scheduling.md` — Comprehensive
7. `modifier-system.md` — Comprehensive
8. `threading-and-dispatch.md` — Solid
9. `element-pool.md` — Solid
10. `source-mapping.md` — Solid
11. `analyzer-architecture.md` — Comprehensive
12. `animation-pipeline.md` — Comprehensive
13. `focus-and-input-internals.md` — Comprehensive
14. `devtools-internals.md` — Comprehensive
15. `perf-instrumentation.md` — Comprehensive

Each page:
- Opens with a Mermaid-or-hand-SVG diagram (per §7.1.0).
- Pulls real source snippets via `snippet="source:..."` (§10.2).
- Closes with a "Read the source" callout linking to the relevant
  `src/Reactor/` directory.

Exit: every XAML/WinUI developer evaluating Reactor can answer "how
does the binding model work?", "when does my component re-render?",
"what is the diff actually doing?", "how do modifiers compose?",
"when does my effect fire?", and "what does the runtime do that
WinUI doesn't?" without reading the source.

### Phase 4 — Polish, migration, and process (Sprint 5-6, ~2 weeks)

1. Promote remaining Solid pages to Comprehensive where high-traffic
   (forms, collections, navigation, animation, accessibility,
   data-system, charting).
2. New `wpf-interop.md` — Solid.
3. New `performance.md` — Solid.
4. New `packaging.md` — Solid.
5. Cross-link sweep — every prose mention of a concept becomes an
   inline link (React-style). Run the analyzer in §11.

Exit: tier audit shows 0 Stub, ≤4 Solid, ≥36 Comprehensive.

### Phase 5 — Continuous quality

- Add `mur docs check-tier` (or similar) that asserts the tier claimed
  in front-matter matches the structural checklist.
- Tier-drift CI check on every PR that touches templates or apps.
- Each new framework feature lands with a doc page at Solid or higher;
  no merge without doc.

---

## 10. Template & pipeline additions

### 10.1 Template format additions

To support the new tiers, the `.md.dt` template format needs three
small additions ([013](013-doc-system-design.md) compatible):

1. **`tier:` front-matter field** — `stub | solid | comprehensive`.
   Default `solid`. Drives the lint in §11.
2. **`winui-ref:` front-matter field** — optional URL. Renders a
   styled callout near the top of the page.
3. **`<!-- ai:caveat -->...<!-- /ai:caveat -->` block** — renders as
   a styled "Caveats" callout (per-API gotcha). The pipeline already
   handles `<!-- ai:lock -->`; the same mechanism extends.

### 10.2 Snippet source-tree extension

Add a new snippet directive variant:

```markdown
```csharp snippet="source:src/Reactor/Hooks/UseState.cs#main"
```​
```

Resolves to a `// <snippet:main>...// </snippet:main>` region within
the named source file. Used by the Under-the-hood track (§7.1.1) to
show real framework internals rather than hand-written illustrations.
Existing `snippet="<topic>/<id>"` form (referencing doc apps) keeps
working unchanged — the `source:` prefix is the disambiguator.

Compiler change: when a `source:` snippet is detected, the snippet
extractor walks `src/` instead of `docs/_pipeline/apps/`. No build
dependency change — the source tree is already on disk during
`mur docs compile`.

### 10.3 Image and diagram pipeline changes

Per §7.1.0, SVG is the default for diagrams.

**Build script changes (`mur docs compile`):**

1. **Copy screenshot output to `docs/guide/images/<topic>/`** — already
   happens for PNGs from doc-app captures.
2. **Copy hand-authored `.svg` files** from
   `docs/_pipeline/diagrams/<topic>/*.svg` → `docs/guide/images/<topic>/`.
3. **Render Mermaid sources** — for each
   `docs/_pipeline/diagrams/<topic>/*.mmd`, run
   `mermaid-cli` (`mmdc`) → emit corresponding `.svg` into
   `docs/guide/images/<topic>/`. Cache by content hash so unchanged
   Mermaid files are not re-rendered on every compile.
4. **Validate `![..](images/...)` references** in compiled output —
   every image reference must resolve to a file in
   `docs/guide/images/<topic>/`. Missing files fail the build.

**`mur` tool changes:**

- New subcommand `mur docs render-diagrams [--topic <id>] [--watch]`
  for fast inner-loop iteration on diagram authoring without a full
  compile.
- `mur docs compile` gains `--skip-screenshots` and
  `--skip-diagrams` flags for partial rebuilds during local
  authoring.
- `mur docs new-diagram <topic> <id>` scaffolds a `.mmd` file with
  a starter graph and adds it to the topic's manifest.

**CI changes:**

- Build agents need `mermaid-cli` installed. For Windows CI this is
  `npm install -g @mermaid-js/mermaid-cli` plus a Chromium dependency
  (Mermaid CLI uses Puppeteer). Document in `docs/contributing/`.

### 10.4 Reference generation pipeline (§7.1.2)

`mur docs compile` gains a reference-generation step:

- Reads `*.xml` doc files from `bin/<config>/<tfm>/Reactor*.xml`.
- Parses with `System.Xml.Linq` + a small Roslyn-compatible reader.
- Groups members by the registry in
  `docs/_pipeline/reference-map.yaml` (no code attributes).
- Emits one MD page per public type/member into
  `docs/guide/reference/<category>/<name>.md`.
- Resolves `<see cref="..."/>` and `<seealso cref="..."/>` →
  relative MD link to the generated reference page for that member.
- **Post-processes generated pages to inject conceptual-guide links
  (§10.4.1).**
- Runs the `REACTOR_DOC_001` analyzer to fail the build if any
  public member lacks `<summary>`.

The generated files are committed to git (consistent with the
pre-rendered guide policy in spec 021 §1 — the guide is checked-in
output, not built on the fly).

#### 10.4.1 Conceptual-guide link injection

Raw XML-doc-derived reference pages have a known weakness: they're
member-shaped and de-contextualized. A reader on `UseState.md` who
wants to learn *how UseState fits into the broader hooks model* gets
nothing from the canonical XML doc — just the signature and a one-line
summary. SwiftUI and React both close this gap differently:

- **SwiftUI** — every symbol page has hand-authored "Discussion" and
  "See Also" sections curated per-symbol by Apple's writers. High
  quality, high cost, high drift.
- **React** — each Hook page has a hand-authored "Usage" section with
  3-5 narrative subsections. Same trade-offs.

**Reactor's approach: automate the cross-link injection.** Generated
reference pages get post-processed to inject links into the conceptual
guide based on a registry of mappings. **No code attributes are used
for pure-doc concerns** — XML doc comments and a registry file are the
only inputs. Two sources of truth:

1. **`<seealso cref="..."/>` in XML doc** — author-curated, highest
   priority. The post-processor resolves the `cref` to a member,
   produces a link to that member's reference page, and additionally
   produces a link to the conceptual guide page(s) the registry maps
   that member's category to. Example: `<seealso cref="UseEffect"/>`
   renders as a link to `UseEffect.md` (reference) AND a link to
   `effects.md` / `hooks.md` (guide).
2. **Category-default mappings** — a registry in
   `docs/_pipeline/reference-map.yaml` says "everything in the Hooks
   category gets a link to `hooks.md`; everything in Modifiers gets a
   link to `styling.md` AND `layout.md`; everything in Charting gets a
   link to `charting.md`." Provides baseline coverage so most
   reference pages have a "Learn more" callout without any author
   annotation.

**How cref resolves to the right link.** Authors write standard XML
doc crefs — the same Roslyn-validated `<see cref="..."/>` and
`<seealso cref="..."/>` syntax C# already understands. No new syntax,
no Reactor-specific extensions. The compile pipeline resolves each
cref through three lookup stages:

1. **Cref → member.** Roslyn already does this; `mur docs compile`
   reuses the same resolution against the `.xml` doc files (which are
   keyed by the canonical cref form `M:Namespace.Type.Method`). An
   unresolvable cref is a build error (`REACTOR_DOC_002`).
2. **Member → reference-page URL.** The post-processor knows the
   output path of every generated page (determined by §10.4 category
   routing), so it can emit a relative MD link from the current
   reference page to the target reference page. This is the link
   that renders on GitHub when the docset is browsed.
3. **Member → conceptual-guide URL (if any).** Look the member up in
   `reference-map.yaml`; if its category maps to one or more guide
   pages, additionally emit a "(see also: [hooks](../../hooks.md))"
   inline link next to the reference link.

The author writes `<seealso cref="UseEffect"/>` once. The pipeline
renders something like:

```markdown
**See also:** [UseEffect](../UseEffect.md) — also covered in
[Effects](../../effects.md) and [Hooks](../../hooks.md).
```

Crefs work in both directions: a cref in a generated reference page
links to another reference page (resolved via stage 2); a cref written
in a hand-authored guide page (`docs/_pipeline/templates/<topic>.md.dt`)
links into the generated reference (`reference/hooks/UseEffect.md`).
The compile pipeline supports a small extension to the snippet/screenshot
directive family: an `<!-- ref:UseState -->` inline marker in a guide
template expands to a properly-resolved Markdown link to the generated
reference page. This is the only Reactor-specific syntax; everything
else is standard XML doc.

**Injection points on each reference page:**

- A "**Learn more**" callout near the top, after the signature, with
  links to the conceptual page(s).
- The "See Also" section at the bottom merges author `<seealso>`
  links with the category-default guide links.
- For each in-summary `<see cref="..."/>` link to another reference
  page, the post-processor checks if that target also has a
  conceptual page; if so, the inline link renders as two links —
  `[UseState](../UseState.md) ([guide](../../hooks.md#usestate))`.

**Reverse direction.** The conceptual guide also gets links injected
into the reference. The compile pipeline scans each guide template for
`<!-- ref:Member -->` markers; every reference page whose member is
referenced from a guide page gets a "**Featured in**" callout listing
those guide pages. No front-matter declaration required — the markers
in prose are the source of truth.

**Registry shape.** `docs/_pipeline/reference-map.yaml` is the one
place doc-only metadata lives. Sketch:

```yaml
# Namespace-prefix defaults (apply unless a more specific rule wins)
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
    guide-pages: [hooks, effects]
  - match: "Microsoft.UI.Reactor.Factories.*"
    category: factories
    guide-pages: [layout, forms, collections]   # generic; per-member overrides below
  - match: "Microsoft.UI.Reactor.Charting.*"
    category: charting
    guide-pages: [charting]

# Per-member or per-pattern overrides
overrides:
  - cref: "M:Microsoft.UI.Reactor.Factories.DataGrid``1"
    guide-pages: [data-system]
  - cref: "T:Microsoft.UI.Reactor.ErrorBoundary"
    guide-pages: [advanced]
  - match: "Microsoft.UI.Reactor.Factories.*Chart*"
    guide-pages: [charting]
```

A lint warns when a category exists with no guide-page mapping, and
when a guide page exists with no member pointing at it.

**Why no `[DocCategory]` / `[DocSeeAlso]` attributes.** Three reasons:

1. **Doc concerns shouldn't pollute source code.** Categorization and
   conceptual cross-links are pure documentation. They don't affect
   runtime behavior, API surface, or compilation. Attributes for them
   add ceremony at every public type without buying anything the
   compiler can check.
2. **PR reviewer load lands in the wrong place.** Code reviewers
   should evaluate API correctness, not curate documentation taxonomy.
   A reviewer is unlikely to push back on a missing `[DocCategory]` or
   a stale `[DocSeeAlso]`; a registry file edited in a doc PR gets the
   right reviewer.
3. **Doc taxonomy churns faster than the API surface.** Renaming or
   splitting a guide page is a doc concern; it shouldn't require
   touching `src/`. Sprinkling 500 attributes and re-reviewing each
   on every nav rename is exactly the cost we're avoiding.

The only inputs to reference generation are: (a) standard XML doc
comments — which we already require — and (b) the registry YAML.

**Acceptance bar.** A reader landing on any reference page can reach
the corresponding conceptual page in one click. A reader landing on
any conceptual page can reach the full reference for any API the page
mentions in one click. No dead ends.

### 10.5 AI Author Skill update

`docs/_pipeline/ai-author-skill.md` updates for these changes:

- Document the `tier:` and `winui-ref:` front-matter fields.
- Document the `<!-- ai:caveat -->` block.
- Document the `snippet="source:..."` form.
- Add the SVG-over-ASCII policy with Mermaid examples.
- Add a "Diagram authoring" subsection alongside the existing
  "Snippet markers" subsection.
- Update the "Topic Ideas" table at the bottom to reflect the new
  64-page layout.

No compiler-level changes to the existing snippet/screenshot
mechanism; everything additive.

---

## 11. Tier lint

A new validator step in `mur docs compile --validate-only`:

| Tier | Required elements |
|------|------------------|
| stub | front-matter, title, one paragraph |
| solid | + 3 snippets, 1 screenshot, reference table, Tips, Next Steps |
| comprehensive | + Discussion paragraph, ≥1 Caveats block, Patterns section, Common mistakes section, WinUI link (if applicable), inline cross-links |

Pages claim a tier and the lint enforces it. Failing CI > silently shipping
a "comprehensive" page that's actually a stub.

---

## 12. Open questions — resolved

All seven open questions were resolved during the 2026-05-16 review.
Captured here as decisions; left in numbered form for traceability.

1. **XAML developers as a primary audience.** *Resolved:* XAML
   developers are a primary target adoption pool — surface them, do
   not bury them. `xaml-developers.md` lives in Section 1 (Get Started)
   as a first-class welcome ramp, not in Section 8 (Interop).
   `reactor-vs-xaml.md` (the architectural essay) also surfaces in
   Section 1, indexed a second time under Section 9 (Under the hood)
   for readers approaching from the architecture direction.
   Section 8 cross-links to both rather than owning them.
2. **`windows.md` placement.** *Resolved:* moved to Section 5 (App
   architecture). Multi-window, OpenWindow, tray icons, and window
   lifecycle are architectural concerns, not layout primitives.
3. **`recipes.md` one page vs folder.** *Resolved:* folder
   (`recipes/`) with `index.md` as the gallery view. Each recipe is
   its own page so they scale without nav churn.
4. **Separate API Reference axis with XML-doc → MD generation.**
   *Resolved:* yes, both. Section 10 is a separate auto-generated
   reference axis (§7.1.2). `mur docs compile` gains a reference
   step that reads each assembly's `.xml` doc output and emits MD
   pages grouped by `[DocCategory(...)]` attribute. Hand-written
   intros per group; generated leaves. A `REACTOR_DOC_001` analyzer
   fails the build for public members lacking `<summary>`.
5. **Depth ceiling for Under-the-hood.** *Resolved:* no ceiling. This
   is an OSS project; curious readers will read the source anyway.
   Better they read prose first. Framework-contributor-only material
   (test suite, release process, source tree layout, CI matrix)
   stays in `docs/reference/`.
6. **Snippet source-tree directive.** *Resolved:* adopted. New
   `snippet="source:<path>#<region>"` form (§10.2) extracts
   `// <snippet:region>` from files under `src/`. Used by the
   Under-the-hood track to show real internals.
7. **Catalog thumbnail strategy.** *Resolved:* option (b) — dedicated
   `catalog-thumb` screenshots in each doc-app's manifest. More work
   per control but produces a polished catalog. Worth it.

### 12.1 New open questions raised by the resolutions above

1. **Mermaid CLI on Windows CI.** Confirming `mermaid-cli` install
   on `windows-latest` runners works without sandboxing surprises
   (Puppeteer / Chromium dependency). Spike during Phase 1.
2. **Generated reference file count.** ~500 public members today
   suggests ~150-300 generated pages depending on grouping
   granularity. Confirm the git diff size is acceptable; if not,
   consider single-page-per-category with anchor links instead of
   page-per-member.
3. **Reference-map.yaml drift detection.** When a new public type
   ships in `src/Reactor/` and the registry has no rule that matches
   it, the namespace default still produces a page — but maybe in
   the wrong category. Recommend: the build emits a warning listing
   newly-generated pages that fell back to a default rule, so doc
   authors can decide whether to add an explicit registry entry.

## 13. Success criteria

- **Coverage:** 100% of controls in `ai-author-skill.md` resolve from
  the controls catalog. 100% of hooks have at least one usage example
  in the corresponding topic page.
- **Discoverability:** New user can answer "does Reactor support X?"
  (where X is any WinUI control or capability category) in ≤30 seconds
  starting from `readme.md`.
- **Tier distribution:** ≥36 pages Comprehensive, ≤4 Solid, 0 Stub at
  end of Phase 4.
- **Cross-linking:** zero pages where a concept is mentioned in prose
  without a link to its topic page (run the lint).
- **Sequential traversal:** Previous/Next links form a complete chain
  from `readme.md` through to the last topic.
- **Comparison alignment:** the 19 categories in
  `docs/research/compare/overview.md` each map to at least one page;
  the page's "Discussion" paragraph aligns with the rating commentary.
- **Internals literacy:** a XAML/WinUI developer can read the
  Under-the-hood track and answer "when does my component re-render?",
  "what is reconciliation doing?", "how is `UseState` not just a global
  variable?", and "what's the equivalent of DependencyProperty here?"
  without reading source. `docs/reference/reconciliation.md` and
  `docs/reference/state-and-hooks.md` no longer exist — promoted into
  the guide.

---

## 14. Risks

- **Doc app proliferation.** ~14 new doc apps in
  `docs/_pipeline/apps/`. Each compiles and runs. Build time grows.
  Mitigation: share solution file, batch screenshot capture, profile
  worst-cost apps.
- **Token catalog drift.** A hand-written theming-tokens page goes
  stale instantly. Mitigation: generate the swatch table from the
  theme source at compile time (extends spec 015).
- **Tier inflation.** Authors mark pages "comprehensive" without doing
  the work. Mitigation: the §11 lint blocks merge.
- **Author throughput.** 14 new pages + 6 expansions ≈ 4-6 weeks of
  focused doc work. Mitigation: AI Author Skill (`ai-author-skill.md`)
  is designed exactly for this — most pages should be AI-drafted then
  human-reviewed, not human-authored from scratch.

---

## 15. Companion files

To be created with this spec:

- `docs/specs/041/doc-audit-2026-05.md` — the per-file scorecard (26
  files × tier × gaps), source-of-truth for §5.
- `docs/specs/tasks/041-docs-comprehensive-uplift-implementation.md` —
  phased task checklist mirroring §9.
- `docs/_pipeline/templates/_skeletons/stub.md.dt` —
  `docs/_pipeline/templates/_skeletons/solid.md.dt` —
  `docs/_pipeline/templates/_skeletons/comprehensive.md.dt`
  (skeleton templates with placeholder snippet IDs and lint-compliant
  section headings).

These live as Phase-1 deliverables, not as part of accepting this spec.
