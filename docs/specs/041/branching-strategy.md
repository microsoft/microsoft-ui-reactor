# PR & branching strategy for spec 041

**Decision (2026-05-16):** Long-lived integration branch
`docs/041-uplift` for the duration of the uplift, with per-phase
merge commits into it. PRs to `main` are squashed once a phase exits
its review.

## Cadence

- **Phase 1 (Foundation):** one PR → main once the pipeline tooling is
  end-to-end (validate, source-snippets, SVG/Mermaid, ref-gen
  prototype, analyzers, skeleton templates, readme rewrite,
  devtools-ux merge). Skeletons + stub-state new pages all land in
  the same PR so the readme index is honest.
- **Phases 2-4:** one PR per page (or small group of related pages,
  e.g. all four `recipes/login.md.dt` + sibling recipes in one PR).
  GitHub-rendering preview happens on the integration branch before
  the PR retargets main.
- **Phase 3.5:** one PR per page (these are dense and benefit from
  per-page review).

## Tag

Every PR tagged `docs-041` (GitHub label) so the rollup is searchable.

## Why long-lived integration branch

- Phase 1 stubs in ~38 new pages. Without a single integration branch,
  individual page PRs would each rebase against churning stub files.
- GitHub-rendering preview needs the full nav to be present; per-page
  PRs against `main` would each show a broken Previous/Next chain
  until the last one lands.
- Lets the cross-link analyzer (Phase 4.5) operate on the full
  intended docset before any of it ships to `main`.

## Why not phase-only PRs

- Phase 3 is 5 pages × ~500 lines each + doc apps. One reviewer
  reading one PR with 5+ doc apps and all generated guide diffs is
  a bad review.
- Page-level review catches AI-slop patterns earlier — small PRs
  rendered on GitHub get more eyeballs.

## Risk: branch divergence

If `main` ships a doc that conflicts with the integration branch
(e.g. someone edits `effects.md.dt` to add a hook), resolve by
rebasing the integration branch onto main weekly. The cross-link
analyzer (Phase 4.5) is the safety net.
