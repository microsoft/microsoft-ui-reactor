# Release runbook

This runbook describes how to prepare and trigger a public Microsoft.UI.Reactor preview release.

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

Prepare release content in a PR before tagging. Do not create the release tag first if templates or docs need to point at the new version; otherwise the release assets are built from a commit that still points at the previous version.

Recommended order:

1. Create a release-prep PR that updates docs and template defaults to the new version.
2. Merge that PR to `main`.
3. Tag the merged `main` commit.
4. Push the tag to start the release workflows.
5. Approve/publish the gated OneBranch NuGet release.

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

Update versioned references that consumer-facing templates or docs should show. At minimum, check:

```powershell
rg "0\.1\.0-preview\.[0-9]+|MicrosoftUIReactorVersion|Microsoft\.UI\.Reactor" `
  README.md `
  tools/Templates `
  docs/_pipeline/templates `
  .github/workflows `
  build/pipelines
```

The template's framework reference — `MicrosoftUIReactorVersion`, baked into the generated
app's `.csproj` — is **stamped automatically for the published package**, so there is **no
manual version bump gating the release**. The release workflow's *Pack Templates* step passes
`-p:MicrosoftUIReactorVersion=<resolved version>` (guarded by `TemplateMetadataTests`), so the
published `ProjectTemplates` package always references the framework version shipped in the same
run. The hardcoded default in

```text
tools/Templates/Microsoft.UI.Reactor.Templates.csproj
```

is what *local* `bootstrap.ps1` / `mur pack-local` scaffolds use (it is not overridden there),
so keep it current with the latest **public** package as hygiene — bump it in this same PR — but
it does not affect the published template.

Update authored docs under `docs/_pipeline/templates/`, not generated `docs/guide/` files directly. Then regenerate the guide. Use a full compile for release-prep changes because some pages (for example `getting-started`) pull snippets from other topics:

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
3. Approve the `Production_PublishNuGet` stage when ready to publish to NuGet.org.
4. Verify the packages appear on NuGet.org.
5. Install the released template package or a locally packed template and create a smoke app that restores against NuGet.org.

> **Two-person rule (submitter ≠ approver).** Publishing passes through two distinct gates, not one: the ADO Environment approval *and* the OneBranch ApprovalService / ServiceTree compliance check. The OneBranch approver **must be a different person than whoever pushed the release tag** — a self-approval by the tag pusher will be rejected by the compliance gate (and has bitten past release cycles). Line up a second approver before tagging so the publish is not blocked.

## If a release tag is wrong

Do not move or rewrite a pushed release tag after release workflows or publishing have started. Instead:

1. Fix the issue in a new PR.
2. Merge to `main`.
3. Mint the next preview version with a new tag.
