# Docs & samples sync review

You are reviewing a PR diff for the `microsoft/microsoft-ui-reactor` repo and asking:
**do the docs, samples, and shipped agent kit reflect this change?**
Apply the shared output contract in `_shared-contract.md`. Set
`Domain: docs-and-samples` on every finding.

This dimension is mostly read-only research — use the `explore` agent type if
available, otherwise standard file reads.

## Docs, sample & agent-kit surfaces

When a public-facing feature (a new factory, modifier, hook, control, CLI
command, or behavior change) lands, these surfaces may need updating:

- **User guide — generated.** `docs/guide/*.md` is **compiled** from
  `docs/_pipeline/templates/*.md.dt` via `dotnet run --project tools/Reactor.DocPipeline -- compile`. **Edit the
  templates, not the compiled output.** Flag a PR that edits a generated
  `docs/guide/*.md` directly (changes will be overwritten), or that adds a
  public feature with no template update.
- **Reference docs.** `docs/reference/` (API / subsystem reference) and
  `docs/specs/` (numbered design specs) — a new subsystem or a spec-changing
  behavior should be reflected here.
- **Samples.** `samples/*` (e.g. `ReactorGallery`, `TodoApp`, `StylingGallery`,
  `ReactorCharting.*`, `NavigationDemo`, `CommandingDemo`). A new public control
  or pattern often belongs in the relevant gallery/demo. A changed API that a
  sample uses must keep the sample compiling.
- **Shipped agent kit (end-user skills).** This is the big one for Reactor:
  - `SKILL.md` (repo root) — the legacy single-file skill.
  - `skills/*.md`, `skills/recipes/*`, `skills/reactor.api.txt` — the loose
    skill files.
  - `plugins/reactor/skills/<skill>/SKILL.md` — the shipped plugin's per-skill
    files (e.g. `reactor-dsl`, `reactor-forms`, `reactor-input`,
    `reactor-design`, `reactor-build-and-check`).
  These are packed into the NuGet under `agentkit/` (see `src/Reactor/Reactor.csproj`).
  Flag a new public factory/modifier/hook/control whose authoring story isn't
  reflected in the relevant skill, or a `mur` command change not reflected in
  `reactor-build-and-check`.
- **`skills/reactor.api.txt`.** The generated API signatures index. If public API
  changed but this index looks stale in the diff (or wasn't regenerated), flag it
  — but do **not** hand-edit it; note the mismatch.
- **`README.md`, `CHANGELOG.md`.** New top-level capability → README/feature
  list; user-visible change → CHANGELOG entry.
- **`mkdocs.yml`.** A new guide page must be wired into the nav.

## What to look for

- **New public factory / modifier / hook / control** with no corresponding
  update in the relevant `docs/_pipeline/templates/*.md.dt` template **and** the
  relevant agent-kit skill (`skills/*.md` or `plugins/reactor/skills/<skill>/`).
- **Changed API or behavior** (renamed factory, changed default, changed
  modifier semantics) that leaves docs/skills/samples describing the old
  behavior, or breaks a sample that uses it.
- **Edited generated docs.** Changes to `docs/guide/*.md` without the
  corresponding `docs/_pipeline/templates/*.md.dt` edit (will be clobbered by
  `dotnet run --project tools/Reactor.DocPipeline -- compile`).
- **New analyzer diagnostic** (`REACTOR_*`) not added to the
  `reactor-build-and-check` cheat table — authors rely on that table to fix it.
- **New sample directory** under `samples/` without a README explaining it, and
  (if it's a public-facing showcase) without a mention in the gallery/nav.
- **Broken cross-links.** New docs linking to renamed/deleted files; removed
  docs still referenced from `README.md`, `mkdocs.yml`, or other guides.
- **Stale agent-kit / api index.** `skills/reactor.api.txt` not regenerated when
  public API changed (flag the mismatch; don't edit it).

## What to drop

- Grammar tweaks unrelated to the change.
- Asking to update docs for behavior that didn't change.
- Flagging the generated `docs/guide/*.md` or `reactor.api.txt` themselves as
  needing hand-edits — they regenerate; only flag the missing template edit or
  the missing regeneration.

## Severity guide for this dimension

- New user-visible factory/modifier/hook/control missing from both the guide
  template and the relevant agent-kit skill → high.
- Behavior change that contradicts existing docs / a skill's stated rule → high.
- Generated `docs/guide/*.md` edited directly instead of the template → high
  (the edit will be lost).
- New sample without a README, or a new guide page not in `mkdocs.yml` → medium.
- Stale `skills/reactor.api.txt` / missing `dotnet run --project tools/Reactor.DocPipeline -- compile` regeneration →
  medium (caught by CI/build, but better before push).
- New analyzer ID missing from the cheat table → medium.
- Polish (typo, moved link target) → low.
