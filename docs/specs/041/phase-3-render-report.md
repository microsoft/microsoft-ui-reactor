# Phase 3 — GitHub render report

Captured 2026-05-17. This report mirrors `phase-1-render-report.md`
and `phase-2-render-report.md` — a checklist of items the
GitHub-rendered preview must show correctly, plus per-page notes from
the local rendered review. Real GitHub-side verification happens at
PR-open time against `main`; this report captures the local walk
through `docs/guide/<page>.md` after each `mur docs compile` regen.

## Pages added or changed

| Page | Source | Notes |
|---|---|---|
| `docs/guide/text-and-media.md` | new — `text-and-media.md.dt` | Comprehensive. 9 controls; 1 caveat; Patterns + Common Mistakes populated. |
| `docs/guide/status-and-info.md` | new — `status-and-info.md.dt` | Solid. 8 controls; Reference table in first half. |
| `docs/guide/dialogs-and-flyouts.md` | new — `dialogs-and-flyouts.md.dt` | Comprehensive. 4 primitives + commanding integration + focus/ARIA discussion. |
| `docs/guide/forms.md` | expand — `forms.md.dt` | Promoted to Comprehensive. 5 new sections + caveat + Patterns + Common Mistakes. |
| `docs/guide/collections.md` | expand — `collections.md.dt` | Promoted to Comprehensive. 3 new sections + caveat + Patterns + Common Mistakes. |
| `docs/guide/controls.md` | edited — `controls.md.dt` | "Detail page (Phase 3)" markers replaced with real links for the three new pages. InkCanvas marked "Not yet wrapped". |

## Local render checklist (each page)

- [x] Title heading renders at top.
- [x] WinUI-ref blockquote renders for the four pages that declare
      `winui-ref:` (text-and-media, status-and-info, dialogs-and-flyouts,
      forms — collections, too, both as Phase 3.5 expansions).
- [x] Mental-model lead paragraph renders **above** the first
      `#`-heading on Comprehensive pages (text-and-media,
      dialogs-and-flyouts, forms, collections).
- [x] Caveat blockquote renders with the "Caveat:" label on
      Comprehensive pages.
- [x] Reference table renders inside the first half of every Solid
      and Comprehensive page.
- [x] `## Tips` section present on every page.
- [x] `## Next Steps` section present with ≥3 inline links on every page.
- [x] Comprehensive pages have `## Patterns` and `## Common Mistakes`
      sections.
- [x] Snippet code blocks render with `csharp` syntax-highlight hint.
- [x] Image references resolve to placeholder PNGs (real captures
      deferred — see retro).

## GitHub-side checks (deferred to PR-open against `main`)

- [ ] Tables render with correct alignment.
- [ ] PNG screenshots render at reasonable sizes (placeholders today
      will look like gray 320×240 boxes; that's the known state).
- [ ] Code blocks have correct syntax highlighting.
- [ ] Cross-links resolve — no 404s. Local walk found zero broken
      inbound or outbound links across the five new/changed pages.
- [ ] Previous/Next chain unbroken from `readme` through the catalog
      detail pages.
- [ ] Mobile rendering acceptable (sidebar collapses; no overflow on
      the comparison tables; the catalog thumbnail strip stacks).

## Items to inspect specifically on the PR preview

1. **The `ContentDialog` `with { ... }` syntax in
   `dialogs-and-flyouts.md`.** C# `with` expressions render correctly
   in code blocks; verify the `=` alignment on init-only properties
   reads cleanly at GitHub's font.

2. **The 4-row severity InfoBar example in `status-and-info.md`.**
   The placeholder image is one gray rectangle, not four — when real
   captures land, confirm the four severities stack correctly inside
   the screenshot frame.

3. **The MapControl section in `text-and-media.md`.** WinUI's `MapControl`
   needs a Bing Maps service token to render tiles; the sample app
   leaves the token null so the grid-only state is what the eventual
   capture will show. The page text matches.

4. **`forms.md` length.** The expansion pushed the page from ~260
   lines to ~470. Verify the GitHub TOC nav (the `[[ ToC ]]` injected
   on render or the sticky-side nav, depending on theme) handles the
   longer structure.

5. **`collections.md` length.** Same concern, ~210 → ~430 lines.
   The three new sections (grouping / drag-reorder / lazy-loading)
   each carry a placeholder screenshot; confirm the visual density
   doesn't push the cross-comparison table out of the first half on
   narrow viewports.

## Local-compile validation summary

`mur docs compile --validate-only` reports **validation passed** across
all 64 templates. The five Phase 3 templates are tier-lint clean at
their declared tiers:

| Template | Tier | Findings |
|---|---|---|
| `text-and-media.md.dt` | comprehensive | 0 |
| `status-and-info.md.dt` | solid | 0 |
| `dialogs-and-flyouts.md.dt` | comprehensive | 0 |
| `forms.md.dt` | comprehensive (promoted) | 0 |
| `collections.md.dt` | comprehensive (promoted) | 0 |

All remaining info-level findings are on the 26 pre-existing tierless
pages (same surface as Phase 1.14 and Phase 2.8 reports).
