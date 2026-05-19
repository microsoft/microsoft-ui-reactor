# Doc Audit — May 2026

Per-file scorecard for spec 041 (Docs Comprehensive Uplift). 26 current
guide pages × current tier × gaps × Phase-target tier.

**Tier counts (verified against spec §5.1):**

| Tier | Count |
|------|-------|
| Comprehensive | 11 |
| Solid | 11 |
| Thin | 4 |

Total = 26. Matches §5.1 — no drift.

---

## Audit table

Column legend:
- **Page** — `docs/guide/<name>.md` (drop the `.md`).
- **LOC** — line count as of 2026-05-16.
- **Current tier** — comprehensive / solid / thin.
- **Gaps** — top missing elements relative to the target tier.
- **Phase target** — the phase that promotes/expands this page.
- **Target tier** — comprehensive / solid (no stubs allowed at exit).

| Page | LOC | Current tier | Top gaps (for promotion) | Phase | Target tier |
|------|-----|--------------|--------------------------|-------|-------------|
| `readme` | 164 | thin | No decision-tree / roadmap; index missing the 10-section nav from spec §7.1 | 1 (1.12) | solid (index page; stays narrative) |
| `getting-started` | 432 | comprehensive | OK; light cross-link audit only | 4 (4.5) | comprehensive |
| `dev-tooling` | 185 | thin | No `mur` CLI subcommand walkthrough; no MCP server; no VS Code panel; no `dotnet watch` integration; devtools-ux content not merged in | 1 (1.13) | comprehensive |
| `devtools-ux` | 156 | thin | Will be merged into `dev-tooling` and deleted | 1 (1.13) | (deleted) |
| `components` | 266 | comprehensive | Cross-link sweep; ref:Member markers to factories | 4 (4.5) | comprehensive |
| `hooks` | 399 | comprehensive | Cross-link to `hooks-internals` (Phase 3.5); ref:Member markers to hooks reference (Phase 1.7) | 4 (4.5) | comprehensive |
| `layout` | 328 | solid | Missing FlexPanel cross-link; Common Mistakes section thin | 4 (4.5) | comprehensive |
| `flex-layout` | 280 | solid | Patterns section minimal; no "Don't" examples | 4 (4.5) | comprehensive |
| `forms` | 487 | solid | Missing AutoSuggestBox, DatePicker, TimePicker, CalendarView, CalendarDatePicker, ColorPicker, NumberBox, PasswordBox, RadioButtons, ToggleSwitch | 3 (3.4) | comprehensive |
| `collections` | 391 | solid | No grouping recipes; no drag-reorder doc; virtual-list deep dive thin; no lazy-loading patterns | 3 (3.5) | comprehensive |
| `navigation` | 642 | comprehensive | Already comprehensive; cross-link audit | 4 (4.1) | comprehensive |
| `styling` | 357 | comprehensive | Cross-link to theming-tokens (new in Phase 2) | 4 (4.5) | comprehensive |
| `effects` | 221 | solid | No `UseResource` cross-link; no async-error patterns; mental-model paragraph weak | 4 (4.1) | comprehensive |
| `commanding` | 217 | solid | No `Command<T>`; no flyout integration; no global accelerators | 3 (3.3 cross-link) + 4 (4.1) | comprehensive |
| `context` | 209 | solid | Cross-link audit; Patterns section thin | 4 (4.5) | comprehensive |
| `accessibility` | 430 | comprehensive | Cross-link to focus-and-input-internals (Phase 3.5) | 4 (4.1) | comprehensive |
| `localization` | 268 | solid | Cross-link to `docs/reference/localization-howto.md` (which is also being audited) | 4 (4.5) | solid |
| `animation` | 550 | comprehensive | Cross-link to `animation-pipeline` (Phase 3.5); promote to comprehensive verification | 4 (4.1) | comprehensive |
| `charting` | 460 | comprehensive | Cross-link sweep; ref:Member markers to charting reference (Phase 1.7) | 4 (4.1) | comprehensive |
| `advanced` | 393 | comprehensive | Cross-link to `hooks-internals` / `modifier-system` (Phase 3.5) | 4 (4.5) | comprehensive |
| `data-system` | 352 | solid | Promote to comprehensive: Patterns section, Common Mistakes, more snippets | 4 (4.1) | comprehensive |
| `winforms-interop` | 280 | solid | No data-flow section; no threading constraints called out; no parallel WPF link | 4 (4.5) | comprehensive |
| `async-resources-cookbook` | 340 | thin | Only "thin" because standalone — quality is fine; rename to `async-resources` and cross-link from `effects` | 4 (rename in 4.5) | solid |
| `input-and-gestures` | 413 | comprehensive | Cross-link to `focus-and-input-internals` (Phase 3.5) | 4 (4.5) | comprehensive |
| `windows` | 290 | solid | Move from Section 3 (UI surface) to Section 5 (App architecture) — front-matter `order:` rebase only | 1 (1.12) | solid |
| `xaml-developers` | 357 | comprehensive | Add cross-link to new `reactor-vs-xaml.md` (Phase 3.5); move to Section 1 (Get Started) | 1 (1.12) + 4 (4.1) | comprehensive |

---

## Tier rollup (current state, pre-uplift)

**Comprehensive (11):** getting-started, components, hooks, navigation,
styling, accessibility, animation, charting, advanced,
input-and-gestures, xaml-developers.

**Solid (11):** layout, flex-layout, forms, collections, effects,
commanding, context, localization, data-system, winforms-interop,
windows.

**Thin (4):** readme, dev-tooling, devtools-ux, async-resources-cookbook.

Matches spec §5.1 counts (11 / 11 / 4).

---

## Target rollup (end of Phase 4, per spec §13)

≥36 Comprehensive, ≤4 Solid, 0 Stub.

After all phases:
- 26 existing → 25 (devtools-ux merged into dev-tooling)
- + 38 new pages = 63 pages
- Of which ~58 Comprehensive, ≤4 Solid (likely candidates: persistence,
  testing initial pass, threading-and-dispatch, source-mapping until
  promotion), 0 Stub.

---

## Discoverability gap notes (spec §5.3)

Controls listed in `ai-author-skill.md` or shipping under
`src/Reactor/Controls/**` but with no narrative coverage in the current
26 pages:

- **Text & media:** MarkdownTextBlock, RichEditBox, RichTextBlock,
  MediaPlayerElement, InkCanvas, MapControl, WebView2 (→ Phase 3.1)
- **Forms/pickers:** AutoSuggestBox, DatePicker, TimePicker,
  CalendarView, CalendarDatePicker, ColorPicker, NumberBox, PasswordBox,
  RadioButtons, ToggleSwitch (→ Phase 3.4)
- **Status & info:** InfoBar, InfoBadge, ProgressBar, ProgressRing,
  TeachingTip, PipsPager, PersonPicture, RatingControl (→ Phase 3.2)
- **Dialogs & flyouts:** ContentDialog, MenuFlyout, CommandBarFlyout,
  Popup, TeachingTip (→ Phase 3.3)
- **Navigation extras:** BreadcrumbBar, SplitView, SelectorBar (verify
  per audit; expand `navigation.md` in Phase 4 if missing)

Concepts missing entirely:

- Testing Reactor apps (→ Phase 2.2)
- `UsePersisted` (→ Phase 2.4)
- Theming token reference (→ Phase 2.3)
- Backdrop / Mica / Acrylic narrative (→ Phase 4 styling expand)
- Custom hooks authoring (→ Phase 2.7 `rules-of-reactor` + Phase 3.5
  `hooks-internals`)
- WPF interop (→ Phase 4.2)
- Packaging & distribution (→ Phase 4.4)
- Performance & profiling top-down (→ Phase 4.3)
- Error handling beyond ErrorBoundary (→ Phase 4 advanced expand)
- `.ScrollLinked`, AnimatedIcon, reduced-motion (→ Phase 4 animation
  promote)
- `Command<T>`, context menus, global accelerators (→ Phase 3.3 + 4
  commanding promote)
