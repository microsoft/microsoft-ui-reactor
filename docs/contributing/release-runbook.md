# Release runbook

This runbook describes how to prepare and trigger a public Microsoft.UI.Reactor preview release.

## Quick checklist (happy path)

Releases are **tag-driven**, but pushing a tag only *starts* the pipelines — it does **not** publish to NuGet.org. The two steps people most often miss (the approval gate and the two-person rule) are called out below.

1. **Pre-flight** — pick the next `v0.1.0-preview.N` and confirm the tag, GitHub release, and the four NuGet packages don't already exist (`git tag --list`, `gh release view`, NuGet.org).
2. **Prep PR (bump one property + recompile docs)** — bump `<ReactorPublicVersion>` in the root `Directory.Build.props` to the version you're about to tag, run `mur docs compile --skip-screenshots --skip-diagrams`, and commit the regenerated `docs/guide` pages. That single property is the source of truth: the guide's `{{reactorVersion}}` token and the template's `MicrosoftUIReactorVersion` fallback both derive from it, and `README.md` is version-agnostic (no sweep needed). CI's docs freshness gate fails the PR if you bump the property without recompiling. Land a PR, merge to `main`.
3. **Tag `main`** — `git checkout main && git pull`, then `git tag -a v<version> -m "Release <version>"` and `git push origin v<version>`. This starts the GitHub `Package` workflow and the OneBranch official pipeline, and publishes the versioned docs site (see [Versioned documentation site](#versioned-documentation-site)).
4. **Publish (gated)** — the tag push does **not** publish to NuGet.org. Approve the OneBranch `Production_PublishNuGet` stage, then verify the packages appear on NuGet.org.
5. **Two-person rule** — the publish approver **must be a different person than whoever pushed the tag** (a self-approval is rejected by the compliance gate). Line up a second approver *before* you tag.
6. **Smoke-test** — scaffold from the published template and restore against NuGet.org.

Each step is expanded in the sections below.

## Internal nightly channel

Reactor also has an internal nightly channel for signed pre-release packages on
the Azure Artifacts feed used by the OneBranch build. The nightly pipeline is
`build/pipelines/OneBranch.Nightly.Reactor.yml`; it runs on a daily schedule,
builds `main`, and publishes versions shaped like
`0.2.0-nightly.<yyyyMMdd>.<counter>` to the internal feed.

To consume the nightly channel internally, add this feed source to your
`nuget.config` and float the package version on the nightly label, for example:

`https://pkgs.dev.azure.com/github-private/microsoft/_packaging/microsoft-ui-reactor-internal/nuget/v3/index.json`

```xml
<PackageReference Include="Microsoft.UI.Reactor" Version="0.*-nightly*" />
```

Nightly packages are internal-only and auto-published; they do not go through
the public NuGet.org approval gate below.

## Release model

Reactor versions are tag-driven. A tag named `v0.1.0-preview.3` makes MinVer resolve package version `0.1.0-preview.3` for that exact commit. The tag push starts the GitHub packaging workflow and the OneBranch official pipeline; the OneBranch NuGet publish stage is approval-gated.

Prepare release content in a PR before tagging. Do not create the release tag first if docs need to point at the new version; otherwise the release assets are built from a commit whose docs still name the previous version. (The template's framework reference is auto-stamped at pack time, so it no longer needs a pre-tag bump — see [Prepare the release PR](#prepare-the-release-pr).)

See the [Quick checklist](#quick-checklist-happy-path) above for the actionable order.

## Choose the version

Use SemVer prerelease tags with the `v` prefix:

```powershell
$version = "0.1.0-preview.3"
$tag = "v$version"
```

Before starting, check that the tag and package version do not already exist:

```powershell
git fetch origin --tags
git tag --list $tag
gh release view $tag
```

Also check NuGet.org for already-published packages:

- `Microsoft.UI.Reactor`
- `Microsoft.UI.Reactor.Advanced`
- `Microsoft.UI.Reactor.Devtools`
- `Microsoft.UI.Reactor.ProjectTemplates`

## Prepare the release PR

Start from current `main`:

```powershell
git checkout main
git pull origin main
git switch -c release/$version
```

Bump the **single source of truth** for the public package version — the
`<ReactorPublicVersion>` property in the root `Directory.Build.props` — to the version you
are about to tag:

```xml
<ReactorPublicVersion>0.1.0-preview.N</ReactorPublicVersion>
```

That one property feeds every version-bearing surface, so there is **no `rg` sweep and no
per-file bump**:

- **Guide docs** — `docs/_pipeline/templates/*.md.dt` reference the version through the
  `{{reactorVersion}}` token, which `mur docs compile` substitutes from this property.
- **Template fallback** — `tools/Templates/Microsoft.UI.Reactor.Templates.csproj` derives its
  `MicrosoftUIReactorVersion` fallback default from `$(ReactorPublicVersion)`.
- **README** — is deliberately version-agnostic (it names no version and links to NuGet /
  Releases), so it needs no edit at all and `mur docs compile` never touches it.

The template's framework reference is *also* stamped automatically for the published package:
the release workflow's *Pack Templates* step passes `-p:MicrosoftUIReactorVersion=<resolved
version>` (guarded by `TemplateMetadataTests`), so the published `ProjectTemplates` package
always references the framework version shipped in the same run. `bootstrap.ps1` runs `mur
pack-local --framework-version latest`, which resolves the newest published package from NuGet
for local scaffolds. The `$(ReactorPublicVersion)`-derived value is therefore only a *fallback*
(a bare `mur pack-local`, or when the `latest` lookup can't reach NuGet) — but keeping it
current is free now, since you bump the one property anyway.

Two guards keep the bump honest so it can't silently go stale:

- **Docs freshness gate** (CI `docs-build` job) recompiles the whole guide and fails the
  PR if any generated file under `docs/guide` differs from what is committed — so a
  `<ReactorPublicVersion>` bump that skipped a recompile is caught along with every other
  kind of compiled-doc drift. See
  [doc-pipeline.md §10](doc-pipeline.md#10-compiled-output-freshness-gate).
- **Tag-vs-property guard** (`release.yml`, tag pushes only) fails the release if
  `<ReactorPublicVersion>` doesn't equal the MinVer-resolved tag version — so the tag, the
  docs, and the stamped template pack are provably one version.

Then regenerate the guide (authored docs live under `docs/_pipeline/templates/`; never edit
generated `docs/guide/` files directly). Use a full compile for release-prep changes because
some pages (for example `getting-started`) pull snippets from other topics:

```powershell
mur docs compile --skip-screenshots --skip-diagrams
```

## Validate the release PR

Run the focused template tests:

```powershell
dotnet test tests/Reactor.Tests/Reactor.Tests.csproj `
  -p:Platform=x64 `
  --filter FullyQualifiedName~TemplateMetadataTests
```

Pack the template locally and inspect the generated default. Pass
`-p:MicrosoftUIReactorVersion=$version` to mirror what the release workflow stamps, so the
generated app references the version being released:

```powershell
dotnet pack tools/Templates/Microsoft.UI.Reactor.Templates.csproj `
  --configuration Release `
  -p:Version=0.0.0-local `
  -p:MicrosoftUIReactorVersion=$version `
  -p:Platform=AnyCPU `
  -o local-nupkgs
```

Create a throwaway app from the packed template and verify its `.csproj` references the chosen public version. Skip restore before the tag is published because the new package version will not exist on NuGet.org yet:

```powershell
dotnet new uninstall Microsoft.UI.Reactor.ProjectTemplates
dotnet new install local-nupkgs/Microsoft.UI.Reactor.ProjectTemplates.0.0.0-local.nupkg

$scratch = Join-Path $env:TEMP "reactor-template-smoke"
Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
dotnet new reactorapp -n ReactorTemplateSmoke -o $scratch --no-restore
Select-String "$scratch\ReactorTemplateSmoke.csproj" -Pattern $version
```

Open the PR and wait for CI. Do not tag until the release PR is merged.

## Tag the merged release commit

After the release PR merges:

```powershell
git checkout main
git pull origin main
git tag -a v0.1.0-preview.3 -m "Release 0.1.0-preview.3"
git push origin v0.1.0-preview.3
```

Tagging the merge commit ensures the generated packages, template defaults, docs, GitHub Release assets, and OneBranch official packages all correspond to the same source tree.

## Monitor and publish

After pushing the tag:

1. Confirm the GitHub `Package` workflow runs for the tag and creates a GitHub Release.
2. Confirm the OneBranch official pipeline starts for the tag.
3. Confirm the `Publish docs` workflow runs for the tag and that the new version is selectable at <https://microsoft.github.io/microsoft-ui-reactor/> (see [Versioned documentation site](#versioned-documentation-site)). Check **all three** of its jobs:
   - a green `publish` beside a red `deploy` means the environment refused the tag ref, and the site stays stale even though `gh-pages` is already correct — see [Which refs may deploy](#which-refs-may-deploy);
   - a green `deploy` beside a red `verify` means the deployment reported success but the live site is serving something else — see [Why a release commit deploys twice](#why-a-release-commit-deploys-twice).
4. Approve the `Production_PublishNuGet` stage when ready to publish to NuGet.org.
5. Verify the packages appear on NuGet.org.
6. Install the released template package or a locally packed template and create a smoke app that restores against NuGet.org.

> **Two-person rule (submitter ≠ approver).** Publishing passes through two distinct gates, not one: the ADO Environment approval *and* the OneBranch ApprovalService / ServiceTree compliance check. The OneBranch approver **must be a different person than whoever pushed the release tag** — a self-approval by the tag pusher will be rejected by the compliance gate (and has bitten past release cycles). Line up a second approver before tagging so the publish is not blocked.

## Versioned documentation site

<https://microsoft.github.io/microsoft-ui-reactor/> is versioned with
[mike](https://github.com/jimporter/mike). Every published version is rendered once and
kept as its own directory in the `gh-pages` branch, and the Pages artifact is that whole
branch — so a version's pages stay byte-identical after it ships. Material's version
selector in the site header reads the `versions.json` mike maintains at the site root.

Nothing about this is manual on the happy path. `.github/workflows/docs.yml` handles it:

| Trigger | Result |
| --- | --- |
| Push to `main` touching the docs | Republishes the `main (development)` version |
| Push of a `v*` tag | Publishes `<version>`, moves the `latest` alias to it, and repoints the site root |

A release commit triggers both, and both deploy — see
[Why a release commit deploys twice](#why-a-release-commit-deploys-twice).

The site root redirects to whichever version holds the `latest` alias, so readers landing
on the bare URL always get the newest release rather than unreleased `main`. Every version
that does *not* hold `latest` renders the outdated-version banner defined in
`docs/_overrides/main.html`.

A tag only takes the `latest` alias if it is the highest tag by version sort. Re-cutting or
backporting an older tag therefore publishes that version without dragging `latest`
backwards.

### Which refs may deploy

The workflow's `deploy` job targets the `github-pages` environment, and that environment
carries a deployment-branch policy naming the refs allowed to deploy from it. Because a
release tag publishes the docs, the policy must admit **both** `main` (a branch) and `v*`
(a tag). A policy listing only `main` — which was correct while `main` was the sole
publisher — rejects every release tag.

That policy lives in repository settings, not in this repo, so nothing in `docs.yml` hints
at it. Read it with:

```powershell
gh api repos/microsoft/microsoft-ui-reactor/environments/github-pages/deployment-branch-policies `
  --jq '.branch_policies[] | "\(.type): \(.name)"'
```

It should print `branch: main` and `tag: v*`. Adding a missing entry needs repository
**admin** — `maintain` gets `HTTP 403: Must have admin rights to Repository`:

```powershell
gh api -X POST repos/microsoft/microsoft-ui-reactor/environments/github-pages/deployment-branch-policies `
  -f name='v*' -f type='tag'
```

The failure is worth recognising on sight, because the run half-succeeds: `publish` goes
green and `deploy` goes red with

```text
Tag "v0.1.0-preview.15" is not allowed to deploy to github-pages due to environment protection rules.
```

Nothing is lost when that happens. `publish` is the job that writes to `gh-pages`, so the
new version *is* rendered there — and the `latest` alias has already moved to it if the tag
qualifies under the version-sort rule above. Only the serving step was refused, which is
why the live site keeps showing the previous release while the branch is already correct.
Once the policy admits the ref, dispatch `Publish docs` on `main` to serve the stranded
release: the artifact is the whole `gh-pages` branch, so a deploy from any allowed ref
publishes every version already committed to it.

### Why a release commit deploys twice

A release PR always edits `docs/guide/**` — the `{{reactorVersion}}` substitutions — so the
push that merges it matches the workflow's path filter, and the release tag then points at
that same merge commit. Left alone, that produces **two** `Publish docs` runs with an
identical `github.sha`.

That is a problem because `actions/deploy-pages` sends `pages_build_version = github.sha`
and exposes no input to override it, so both runs create a Pages deployment under one
identity. One wins and the other is silently stranded: every job green, the deployment
reporting `success`, `gh-pages` byte-correct — and the live site still showing the previous
release. That is exactly what happened cutting 0.1.0-preview.16, and it went unnoticed for
2h25m (issue #1268).

What prevents it now:

- The `verify` job polls the live site after every deployment and asserts that the artifact
  being served is the one **that run** produced. This is the part that carries correctness,
  and it is the only thing in the pipeline that looks at the live site at all.

**The duplicate deployment is tolerated, not avoided.** Standing one of the two runs down
sounds obvious, and it was tried; every form of it requires *predicting* that the other run
will deploy, and each way that prediction can fail is silent in the direction that matters:

- a still-pending tag run can be cancelled by a later docs push, so the run deferred to
  never happens;
- a tag deleted or re-cut on `origin` after checkout leaves a stale local tag that still
  points at the commit;
- an unreachable `origin` makes the check answer "no tag here" from stale data;
- the two runs are not guaranteed to enter the concurrency group in the order their events
  fired, so the tag run can deploy an artifact that predates the branch run's `main`.

Each of those skips the deployment **and** its verification together, leaving the site stale
with every job green — strictly worse than a duplicate deployment that `verify` catches and
reports. So both runs deploy, and a stranded one turns red.

**The concurrency queue still matters.** The workflow's concurrency block sets `queue: max`.
Under the Actions default (`queue: single`) only one run may be *pending* per group, and a
newly queued run cancels the previous pending one. During a release that is reachable: the
merge's `main` run holds the group, the tag run waits behind it, and any further docs push
to `main` evicts the tag run before it ever starts — so the release version never reaches
`gh-pages` at all, with nothing failing. `queue: max` makes runs wait in FIFO order instead.
Removing it silently re-arms that failure, which is why `DocsDeployWiringTests` asserts it.

`verify` distinguishes two failures, and they call for different responses:

| Annotation | Meaning | What to do |
| --- | --- | --- |
| `…is not serving the artifact this run published` | The deployment was stranded. It names the run id the live site *is* serving. | Dispatch `Publish docs` on `main` (below). |
| `Could not read … at all, so the deployment is unverified` | Not one probe reached known-good content, so the run says nothing about the deployment. | Treat it as a broken check: confirm the site is up, then re-run the job. |

Every probe uses a unique `?nc=` query key, so a pass cannot come from a cached response,
and an already-published version is fetched with the identical request shape as a positive
control — a no-match is not a measurement until the same probe is shown able to match.

What `verify` actually asserts, beyond the stamp: that the live `versions.json` contains
every version the artifact declared and puts `latest` where this run put it; that each
version **this run published** serves its own `index.html` (not just the `latest` holder,
which a backported tag deliberately does not move and a `main` push never touches); and
that the site root — the URL readers land on, written only by `mike set-default` — responds
and still forwards to the expected default. The list of versions a run published is
recorded by the publishing steps and cross-checked against mike's own `versions.json`
before the artifact is uploaded, so the two cannot drift apart unnoticed.

**The remedy, verified.** Dispatch `Publish docs` on `main`. The `publish` job assembles the
artifact from the whole `gh-pages` branch, so any allowed ref re-serves every published
version. With no `backfill_tags` the release step is skipped, and `mike set-default` runs
only `if ! mike list latest` — `latest` already exists — so it **cannot drag `latest`
backwards**. This is what restored preview.16.

The gate's own oracle is `deploy-stamp.json` at the site root: `publish` writes it into the
artifact with the run id that built it, and `verify` asserts the live copy matches. It is
refreshed from the workflow on every deploy rather than committed to `gh-pages`, for the
same reason `404.html` is — it describes a deployment, not a published version. Comparing
`versions.json` alone would not do: `mike deploy main` republishes an existing version, so
on a `main` push the version set is unchanged and the comparison would pass whether or not
the deployment ever landed.

### Legacy unversioned links

Before versioning, pages lived at unversioned paths such as
`https://microsoft.github.io/microsoft-ui-reactor/getting-started/`. Those paths no longer
exist — every page now sits under a version directory — so the publish workflow copies
`docs/_site-root/404.html` to the published site root, where GitHub Pages serves it for
unmatched paths. It forwards those legacy paths to the same page under `latest`, preserving
any query string and anchor.

It deliberately does **not** forward a path that already starts with a published version or
alias (`latest`, `main`, or anything beginning with a digit): those are genuine 404s inside a
published version, and forwarding them would produce nonsense paths or a redirect loop. If
version identifiers ever stop matching that shape, update the guard in that file.

MkDocs never sees this file, so `mkdocs build --strict` cannot catch a mistake in it. Its
behaviour is covered by `docs/_site-root/404.redirect.test.js` instead — run it with
`node docs/_site-root/404.redirect.test.js`, or let CI run it via `SiteRootRedirectTests` in
`tests/Reactor.DocPipeline.Tests`.

### Publishing a version retroactively

Run the `Publish docs` workflow manually and set **backfill_tags** to a space-separated tag
list, e.g. `v0.1.0-preview.12 v0.1.0-preview.13`. Every tag is verified to exist before
anything is published — a typo or a missing leading `v` fails the run up front rather than
halfway through — and each tag is then checked out and built in turn.

Tags cut before versioning existed carry no version selector in their own `mkdocs.yml`, so
the backfill layers the `mkdocs.yml` and `docs/_overrides/` from the ref you dispatch from
over each tag checkout. That only holds while the tag's page set still satisfies the current
`nav`; the workflow's `mkdocs build --strict` step fails loudly if a tag has drifted too far,
rather than publishing a broken version.

## If a release tag is wrong

Do not move or rewrite a pushed release tag after release workflows or publishing have started. Instead:

1. Fix the issue in a new PR.
2. Merge to `main`.
3. Mint the next preview version with a new tag.
