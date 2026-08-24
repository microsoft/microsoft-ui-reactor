# Release runbook

This runbook describes how to prepare and trigger a public Microsoft.UI.Reactor preview release.

## Quick checklist (happy path)

Releases are **tag-driven**, but pushing a tag only *starts* the pipelines — it does **not** publish to NuGet.org. The two steps people most often miss (the approval gate and the two-person rule) are called out below.

1. **Pre-flight** — pick the next `v0.1.0-preview.N` and confirm the tag, GitHub release, and the four NuGet packages don't already exist (`git tag --list`, `gh release view`, NuGet.org).
2. **Prep PR (bump one property + recompile docs)** — bump `<ReactorPublicVersion>` in the root `Directory.Build.props` to the version you're about to tag, run `mur docs compile --skip-screenshots --skip-diagrams`, and commit the regenerated `docs/guide` pages. That single property is the source of truth: the guide's `{{reactorVersion}}` token and the template's `MicrosoftUIReactorVersion` fallback both derive from it, and `README.md` is version-agnostic (no sweep needed). CI's docs freshness gate fails the PR if you bump the property without recompiling. Land a PR, merge to `main`.
3. **Tag `main`** — `git checkout main && git pull`, then `git tag -a v<version> -m "Release <version>"` and `git push origin v<version>`. This starts the GitHub `Package` workflow and the OneBranch official pipeline.
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

- **Docs freshness gate** (CI `docs-build` job) fails the PR if `<ReactorPublicVersion>`
  changed but `docs/guide/{getting-started,packaging}.md` weren't recompiled.
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
dotnet test --project tests/Reactor.Tests/Reactor.Tests.csproj `
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
3. Approve the `Production_PublishNuGet` stage when ready to publish to NuGet.org.
4. Verify the packages appear on NuGet.org.
5. Install the released template package or a locally packed template and create a smoke app that restores against NuGet.org.

> **Two-person rule (submitter ≠ approver).** Publishing passes through two distinct gates, not one: the ADO Environment approval *and* the OneBranch ApprovalService / ServiceTree compliance check. The OneBranch approver **must be a different person than whoever pushed the release tag** — a self-approval by the tag pusher will be rejected by the compliance gate (and has bitten past release cycles). Line up a second approver before tagging so the publish is not blocked.

## If a release tag is wrong

Do not move or rewrite a pushed release tag after release workflows or publishing have started. Instead:

1. Fix the issue in a new PR.
2. Merge to `main`.
3. Mint the next preview version with a new tag.
