# Phase 4 — Comparison Alignment Check

Captured 2026-05-17. Verifies spec §13 "Comparison alignment" success
criterion: every category in `docs/research/compare/overview.md` maps
to ≥1 Microsoft.UI.Reactor (Reactor) doc page, and the mental-model paragraph on each page
aligns with the comparison's Reactor rating commentary.

## Source

`docs/research/compare/overview.md` enumerates **21** numbered
categories. Spec 041 §13 says "19 categories"; the 19 number is the
shared-with-competitors set (§§1-19). Categories 20 and 21 are
Reactor-specific (Charting & Chart Accessibility, Devtools & Tracing
Infrastructure) and have no competitor analog in the scorecard tables.
Both still have Reactor doc homes and are checked here for
completeness.

## Mapping

| # | Category | Reactor rating | Primary page(s) | Alignment notes |
|---|---|---|---|---|
| 1 | Declarative Syntax | B | `components.md`, `hooks.md`, `xaml-developers.md`, `reactor-vs-xaml.md` | components.md opens with "a pure function from (state + props) to an element"; reactor-vs-xaml.md makes the push-vs-pull binding-model comparison explicit. Aligned. |
| 2 | Component Architecture | B+ | `components.md`, `advanced.md`, `hooks.md` | components.md covers the function-component model + composition; advanced.md covers ErrorBoundary / Memo / .Set / custom hooks (the "five composition forms" the comparison cites from spec 033). Aligned. |
| 3 | State & Reactivity | B+ | `hooks.md`, `hooks-internals.md`, `reactivity-model.md`, `effects.md` | reactivity-model.md is the dedicated mental-model essay; hooks.md is the surface. Comparison commentary cites "React-style hooks (UseState, UseReducer, UseEffect, UseMemo, …)" — every named hook has a page section. Aligned. |
| 4 | Rendering & Performance | B | `reconciliation.md`, `element-pool.md`, `perf-instrumentation.md`, `performance.md` | reconciliation.md covers the single-threaded reconciler the comparison flags; performance.md (user-facing) and perf-instrumentation.md (internals) cover ETW / frame-aligned sampling. Aligned. |
| 5 | Layout | B+ | `layout.md`, `flex-layout.md` | flex-layout.md is the dedicated FlexPanel page the comparison calls out as "a genuine differentiator." Aligned. |
| 6 | Styling & Theming | B- | `styling.md`, `theming-tokens.md` | theming-tokens.md enumerates the ~40 tokens the comparison cites; styling.md covers the `.Resources()` / `Theme.Ref(key)` surface. Aligned. |
| 7 | Navigation | B+ | `navigation.md` | navigation.md mental-model lead opens on type-safe routes via C# records — verbatim aligned with the comparison's B+ commentary. Aligned. |
| 8 | Animation | B- | `animation.md`, `animation-pipeline.md` | animation.md covers the four animation systems (implicit / compositor / layout / keyframes); animation-pipeline.md is the internals view. The comparison's B- citation around four-systems and the compositor ceiling lands directly. Aligned. |
| 9 | Accessibility | B | `accessibility.md` | accessibility.md mental-model lead frames the three-layer surface (modifiers / hooks / analyzers); comparison commentary cites "16 first-class accessibility modifiers across two storage tiers." Modifier count and storage-tier breakdown are both in the reference table. Aligned. |
| 10 | Input & Gestures | B | `input-and-gestures.md`, `focus-and-input-internals.md` | input-and-gestures.md covers the pointer-event routing model + gesture phases; focus-and-input-internals.md covers UseFocus dispatcher and FocusTrap. Aligned. |
| 11 | Developer Experience | B+ | `dev-tooling.md`, `devtools-internals.md`, `getting-started.md` | dev-tooling.md covers `mur`, MCP, hot reload, devtools panel; devtools-internals.md covers the MCP loop and overlay families. Comparison cites "hot reload works via .NET's MetadataUpdateHandler" — getting-started.md walks the hot-reload first event. Aligned. |
| 12 | Platform Reach | D | `windows.md`, `winforms-interop.md`, `wpf-interop.md` | Windows-only is fundamental — the comparison gives Reactor the same D as WinForms/WPF/WinUI 3. Reactor docs honestly frame Windows as the platform; no over-promising. Aligned. |
| 13 | Testing | B | `testing.md` | testing.md covers headless component tests via Reactor.AppTests.Host, the ErrorBoundary, and analyzer + accessibility-scanner gates the comparison cites. Aligned. |
| 14 | Error Handling | B | `advanced.md` (ErrorBoundary section) | advanced.md owns the ErrorBoundary surface the comparison calls "a genuine advantage." No dedicated `error-handling.md` page — `advanced.md` is the home, cross-linked from effects.md and components.md. Aligned. |
| 15 | Data Loading & Async | B+ | `async-resources.md`, `effects.md`, `effects-scheduling.md` | async-resources.md mental-model lead covers UseResource / QueryCache (spec 020, the PR #29 the comparison cites). effects-scheduling.md is the internals view of cancellation and cleanup ordering. Aligned. |
| 16 | Lists & Virtualization | B+ | `collections.md`, `data-system.md` | collections.md covers `ListView<T>` / `GridView<T>` / `LazyVStack` / `VirtualList` with `viewBuilder` pattern; data-system.md covers DataGrid<T>. Both surfaces the comparison calls "Reactor's bet" for tabular data. Aligned. |
| 17 | I18n & Localization | B+ | `localization.md` | localization.md is Solid tier and references ICU MessageFormat per the comparison's B+ commentary. Aligned. |
| 18 | Interop & Incremental Adoption | A- | `winforms-interop.md`, `wpf-interop.md`, `xaml-developers.md` | winforms-interop.md (Comprehensive) covers `XamlIslandControl` end-to-end; wpf-interop.md covers the parallel surface (currently roadmap-shape, honestly framed); xaml-developers.md is the migration cookbook. Comparison cites "best interop story in the Microsoft ecosystem" — three pages own this category. Aligned. |
| 19 | Forms & Data Entry | B | `forms.md`, `data-system.md`, `recipes/login.md`, `recipes/multi-step-form.md` | forms.md (Comprehensive after Phase 3.5) covers TextField, CheckBox, Slider, NumberBox, ComboBox, RadioButtons, ToggleSwitch, PasswordBox, AutoSuggestBox, DatePicker, TimePicker, CalendarView, ColorPicker. The recipes apply them. The comparison cites `ValidationContext` — covered in forms.md. Aligned. |
| 20 | Charting & Chart Accessibility (Reactor-specific) | A (no competitor median) | `charting.md` | charting.md (Comprehensive) opens with the two-layer DSL the comparison frames as Reactor-original (high-level chart factories over ReactorD3 primitives). Aligned. |
| 21 | Devtools & Tracing Infrastructure (Reactor-specific) | A (no competitor median) | `devtools-internals.md`, `perf-instrumentation.md`, `dev-tooling.md`, `analyzer-architecture.md` | Four pages own this surface: dev-tooling.md (user-facing), devtools-internals.md (MCP loop + overlay families), perf-instrumentation.md (ETW), analyzer-architecture.md (REACTOR_* diagnostic family). Spec 024+025 (MCP) and spec 027 (overlays) the comparison cites are documented across the four. Aligned. |

## Sample-checked deeply (5 categories)

Per the §4.6 brief: 5 categories deep-checked by reading the comparison
section AND the corresponding page's mental-model paragraph end-to-end.

### Category 2 — Component Architecture (B+)

Comparison commentary (`overview.md` §2):
> Reactor (B+): React-style function components with hooks. Context system,
> memoization. The mental model transfers from React cleanly. No slots
> pattern (unlike Compose's named slot APIs), but children via params work.
> Spec 033 added typed ElementRef<T> + UseElementRef<T>(ctx) hook…

Page check — `docs/guide/components.md` mental-model lead:
> A component in Reactor is a pure function from (state + props) to an element
> tree…

Coverage: components.md covers function-component, props-via-record,
children-via-params, and ElementRef<T>. advanced.md cross-links to
the five composition forms (raw method, propless Component,
Component<TProps>, Func, Memo). **Aligned.** No mention of slots-vs-params
distinction the comparison flags — minor gap, not material.

### Category 5 — Layout (B+)

Comparison commentary:
> Reactor (B+): FlexPanel (full Flexbox implementation) is ambitious… A
> genuine differentiator… competitive with or ahead of most declarative
> frameworks.

Page check — `docs/guide/flex-layout.md` mental-model lead:
> Flex layout reasons about two axes at once. The main axis distributes
> children with grow / shrink / basis; the cross axis aligns them.

Coverage: flex-layout.md covers the CSS-Flexbox feature set (flex-grow,
flex-shrink, flex-basis, justify-content, align-items, wrap, gap).
layout.md (parent page) cross-links to it as the "ratio-based sizing"
exit. **Aligned.** Voice is technical and concrete, matches the
comparison's "ambitious / genuine differentiator" framing.

### Category 9 — Accessibility (B)

Comparison commentary:
> Reactor (B): 16 first-class accessibility modifiers across two storage
> tiers… Reactor moved from B- to B…

Page check — `docs/guide/accessibility.md` mental-model lead:
> Reactor's accessibility surface is two layers: modifiers that map to
> UIA properties (Tier 1: AutomationProperties.Name, HelpText, IsRequired,
> LiveSetting, Level…) and hooks that own runtime behavior (UseFocus,
> UseFocusTrap, UseAnnounce, AccessibilityScanner)…

Coverage: modifier count and the two-tier storage model both surface in
the lead. The comparison's "B" rating implicitly cites the May 2026
analyzer additions (REACTOR_A11Y_001..003); accessibility.md covers
those in the Analyzer integration section. **Aligned.**

### Category 15 — Data Loading & Async (B+)

Comparison commentary:
> Reactor (B+): Spec 020 (PR #29, 3814def..9f2f5de, ~40 sub-commits)
> shipped a unified UseResource + QueryCache system…

Page check — `docs/guide/async-resources.md` (Solid tier; intentionally
Solid per Wave-D), `docs/guide/effects.md`, and
`docs/guide/effects-scheduling.md` together cover the surface. The lead
on async-resources.md (the new wave-D table) names UseResource,
QueryCache, AsyncValue<T>, and the prefetch / refetch / mutate
operations the comparison cites. **Aligned.** Spec 020 cited inline.

### Category 18 — Interop & Incremental Adoption (A-)

Comparison commentary:
> Reactor (A-): Best interop story in the Microsoft ecosystem… ReactorHostControl
> drops into any WinUI XAML layout — no ReactorApp required…
> WinForms interop via Reactor.Interop.WinForms library… XamlIslandControl…

Page check — `docs/guide/winforms-interop.md` (Comprehensive) opens on the
XAML Islands model and covers `XamlIslandBootstrap.Run` end-to-end. The
new `## Data flow across the boundary` section (Phase 4 wave-C) has a
4-row mechanism table that matches the comparison's three-way framing
(Reactor→WinForms via UseObservable, WinForms→Reactor via the island,
shared accessibility tree). wpf-interop.md is honest about
`Reactor.Interop.Wpf` being roadmap-shape rather than shipping. **Aligned.**

## Lightly-checked (16 categories)

The other 16 categories were verified by:
1. Reading each category section's "Reactor" rating bullet (1-2 lines).
2. Opening the primary page identified in the table above.
3. Spot-reading the mental-model lead and the table-of-contents.

All 16 pass the "category maps to ≥1 page and the page's lead doesn't
contradict the comparison commentary" bar. No drift found.

## Findings

- **Zero unmapped categories.** All 21 numbered categories (19 standard
  + 2 Reactor-specific) have ≥1 Reactor doc page.
- **Zero mental-model contradictions.** No page's lead understates or
  overstates the comparison rating commentary.
- **Two minor notes worth tracking:**
  - **Component slots gap (cat 2).** Comparison mentions "no slots
    pattern (unlike Compose's named slot APIs)" — components.md does
    not explicitly call this out. Not material; readers don't lose
    anything. Future enrichment: add a one-line caveat callout if/when
    a slots API is considered.
  - **Hot-reload context (cat 11).** dev-tooling.md describes hot
    reload via `dotnet watch` but doesn't reference the .NET
    `MetadataUpdateHandler` mechanism the comparison cites. Not
    blocking; framework readers won't search for the mechanism name.

## Spec §13 success-criterion result

> **Comparison alignment:** the 19 categories in
> `docs/research/compare/overview.md` each map to at least one page;
> the page's "Discussion" paragraph aligns with the rating commentary.

**Met.** All 19 shared categories map to ≥1 page; both Reactor-specific
categories (20-21) also map. Mental-model paragraphs align with the
comparison commentary on every page sample-checked.
