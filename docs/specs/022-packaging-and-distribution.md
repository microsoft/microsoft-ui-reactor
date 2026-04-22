# 022 — Packaging and Distribution

**Status:** Draft
**Date:** 2026-04-17
**Author:** Chris Anderson

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Audience and Rollout Phases](#3-audience-and-rollout-phases)
4. [Artifacts to Ship](#4-artifacts-to-ship)
5. [NuGet Package Layout](#5-nuget-package-layout)
6. [CLI (`mur`) Distribution](#6-cli-mur-distribution)
7. [VS Code Extension](#7-vs-code-extension)
8. [Versioning](#8-versioning)
9. [CI/CD Pipeline](#9-cicd-pipeline)
10. [Feeds and Access Control](#10-feeds-and-access-control)
11. [Signing](#11-signing)
12. [Consumer Experience](#12-consumer-experience)
13. [Open Questions](#13-open-questions)
14. [Implementation Phases](#14-implementation-phases)

---

## 1. Problem Statement

Today, anyone who wants to use Reactor in their own WinUI 3 app has to clone `microsoft/reactor3`, add a `<ProjectReference>` to `src/Reactor/Reactor.csproj`, and keep their consumer repo in sync with our source tree. This is fine for contributors but a hard blocker for evaluators — we want a developer to be able to write:

```xml
<PackageReference Include="Microsoft.UI.Reactor" Version="1.0.0-preview.42" />
```

…and get a working app with analyzers, the localization source generator, and (if they want it) the `mur` CLI — without enlisting in our source.

There is no packaging, publishing, or release infrastructure in the repo today. `.github/workflows/ci.yml` runs unit tests only. `Reactor.Analyzers.csproj:15` already sets `GeneratePackageOnBuild=true`, but nothing else packs, and nothing publishes anywhere.

## 2. Goals and Non-Goals

### Goals

- A consumer can add Reactor to a `.csproj` with a single `<PackageReference>`, no source enlistment.
- The package carries analyzers and source generators automatically (no second reference needed).
- `mur` (the CLI) is available to package consumers, not just contributors.
- A new build produces a versioned, installable artifact **on every PR** so reviewers and early adopters can test a real install.
- The same build pipeline scales from internal-only → NDA external → public NuGet.org with configuration changes, not a rewrite.

### Non-Goals

- **Not** replacing the dev inner loop. Contributors continue to build from source; this spec only covers consumer-facing distribution.
- **Not** shipping the native Rust differ (`Reactor.Native`) in v1 — it's experimental and platform-specific enough to warrant its own spec.
- **Not** producing MSIX / packaged-app artifacts. Reactor is a library; packaging the consumer's app is the consumer's problem.
- **Not** committing to semantic versioning stability until we go public. Pre-1.0 preview versions can break.

## 3. Audience and Rollout Phases

| Phase | Timing | Audience | Feed | Signing |
|---|---|---|---|---|
| **P1 — Internal** | Now | Microsoft employees only | Azure Artifacts (internal feed) | Optional |
| **P2 — NDA external** | ~2 weeks | Small group of external partners under NDA | GitHub Packages (private), invite-only | Required (ESRP) |
| **P3 — Public** | ~4–6 weeks | Open .NET / WinUI community | NuGet.org | Required (ESRP) |

Each phase adds consumers but keeps the same package ID and build pipeline — we just flip publish destinations and tighten the gate.

## 4. Artifacts to Ship

Three distribution tracks, each with its own cadence:

### 4.1 `Microsoft.UI.Reactor` — the framework NuGet

The primary artifact. Contains:

- `Reactor.dll` (the framework)
- `Reactor.Analyzers.dll` — packed under `analyzers/dotnet/cs/`
- `Reactor.Localization.Generator.dll` — packed under `analyzers/dotnet/cs/`
- Package metadata: authors, license, repository URL, icon, `README.md`

The source generator and analyzers are bundled **inside** the framework package, not shipped separately, because no consumer ever wants Reactor without them. This matches how `Microsoft.Extensions.Logging` ships its source generators today.

### 4.2 `mur` — the CLI

Ships as a **per-RID self-contained executable** attached to GitHub Releases, not as a NuGet package. Rationale under section 6.

### 4.3 VS Code extension

Continues to ship as a `.vsix`. Details under section 7.

### 4.4 Secondary packages (v1+, optional)

- `Microsoft.UI.Reactor.Interop.WinForms` — smaller audience, ship as a separate package so WinForms-free consumers don't pay the cost.

## 5. NuGet Package Layout

```
Microsoft.UI.Reactor.1.0.0-preview.42.nupkg
├── lib/net9.0-windows10.0.22621.0/
│   ├── Reactor.dll
│   └── Reactor.xml                        # XML doc comments
├── analyzers/dotnet/cs/
│   ├── Reactor.Analyzers.dll
│   └── Reactor.Localization.Generator.dll
├── build/
│   └── Microsoft.UI.Reactor.targets       # default props
├── README.md
├── LICENSE
└── Microsoft.UI.Reactor.nuspec
```

### Packaging configuration

Enable packing on `src/Reactor/Reactor.csproj`:

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <PackageId>Microsoft.UI.Reactor</PackageId>
  <Authors>Microsoft</Authors>
  <Company>Microsoft</Company>
  <Description>Functional, declarative UI framework for WinUI 3.</Description>
  <PackageProjectUrl>https://github.com/microsoft/reactor3</PackageProjectUrl>
  <RepositoryUrl>https://github.com/microsoft/reactor3</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>   <!-- assumes license moves to MIT before public -->
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>         <!-- SourceLink -->
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
```

To bundle the analyzer + source generator DLLs inside `Microsoft.UI.Reactor.nupkg` (rather than shipping them as their own packages), adjust the two sub-projects to `IsPackable=false` and add explicit pack items in `Reactor.csproj`:

```xml
<ItemGroup>
  <None Include="$(OutputPath)\Reactor.Analyzers.dll"
        Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  <None Include="$(OutputPath)\Reactor.Localization.Generator.dll"
        Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
</ItemGroup>
```

Move shared packaging properties into a new `Directory.Pack.props` at repo root so each packable project gets them uniformly.

### SourceLink

Add `Microsoft.SourceLink.GitHub` so consumers can step into Reactor source during debugging. This costs us nothing once the repo is public; for the internal phase, SourceLink still resolves against GitHub for employees who have access.

## 6. CLI (`mur`) Distribution

`mur` is short-lived (will fold into the framework once the dev loop stabilizes) but in the meantime it's a real tool consumers need. It cannot ship cleanly as a `dotnet tool` because:

- `src/Reactor.Cli/Reactor.Cli.csproj:4-5` targets `net9.0-windows10.0.22621.0` with `Platforms>x64;ARM64`.
- .NET global tools must be AnyCPU and don't cleanly handle Windows-only TFMs.
- Even if we got it working, Copilot SDK native dependencies (`GitHub.Copilot.SDK`) and WinUI runtime expectations make a self-contained exe the more reliable path.

### Chosen approach: self-contained per-RID executables

On every CI run, publish:

- `mur-win-x64.zip` containing `mur.exe` and its runtime
- `mur-win-arm64.zip` same for ARM64

Attach them to:

- **PR artifacts** (short retention, for review) on every PR.
- **GitHub Releases** on tagged builds, with a stable download URL.

Provide an install helper script (`install-mur.ps1`) hosted in the repo that consumers can `iwr | iex` — downloads the latest release asset, extracts to `%LOCALAPPDATA%\Programs\mur\`, adds to `PATH`. This is the pattern `winget`/`rustup`/`gh` all use.

### Future: fold into `Microsoft.UI.Reactor.Tools`

Once `mur` stops depending on unusual native bits we can revisit shipping it as a `dotnet tool` package. That's out of scope for this spec.

## 7. VS Code Extension

The `vscode-reactor/` project already builds a VSIX via `npm run compile` + `vsce package`. Changes for this spec:

- Publish the `.vsix` as a GitHub Release asset on every tagged framework release, version-matched to the framework.
- **Phase 3 only:** publish to the VS Code Marketplace under the Microsoft publisher ID. This requires its own publisher-token setup in CI and is not on the critical path for P1/P2 — internal users can install from VSIX.

## 8. Versioning

### Scheme

`MAJOR.MINOR.PATCH-preview.<build>+<sha>`

Until we hit `1.0`, every version is a prerelease. Consumers must opt in by allowing prerelease versions in their `<PackageReference>` (either explicit version or `AllowPrereleaseVersions`).

### Tool: MinVer

Use **[MinVer](https://github.com/adamralph/minver)** — tag-driven, no extra files, just reads `git describe`. Alternatives considered: `Nerdbank.GitVersioning` (more powerful but heavier), manual (error-prone). MinVer fits our cadence.

Rules:

- On push to `main` without a tag: `0.1.0-preview.0.<height>+<sha>` where `height` is commits since last tag.
- On PR: `0.1.0-pr.<pr-number>.<sha>` — clearly distinguishable so nobody installs a PR build by mistake.
- On tag `v1.0.0-preview.5`: exactly `1.0.0-preview.5`.
- On tag `v1.0.0`: exactly `1.0.0` (stable, P3 only).

### PR build version collision

PR builds share a PR number across pushes. To keep each push installable, append the short SHA: `0.1.0-pr.123.abc1234`. NuGet allows this; the `+` metadata suffix is NuGet-legal.

## 9. CI/CD Pipeline

Extend `.github/workflows/ci.yml` with a `pack` job that runs after `unit-tests` on every PR and push to `main`.

### New job: `pack`

```yaml
pack:
  name: Pack
  needs: unit-tests
  runs-on: windows-latest
  steps:
    - uses: actions/checkout@...
      with:
        fetch-depth: 0          # required for MinVer
    - uses: actions/setup-dotnet@...
      with:
        dotnet-version: 9.0.x
    - name: Restore
      run: dotnet restore Reactor.sln
    - name: Build
      run: dotnet build Reactor.sln -c Release -p:Platform=x64 --no-restore
    - name: Pack framework
      run: dotnet pack src/Reactor/Reactor.csproj -c Release --no-build -o artifacts/nupkg
    - name: Publish CLI (win-x64)
      run: dotnet publish src/Reactor.Cli -c Release -r win-x64 --self-contained -o artifacts/mur-win-x64
    - name: Publish CLI (win-arm64)
      run: dotnet publish src/Reactor.Cli -c Release -r win-arm64 --self-contained -o artifacts/mur-win-arm64
    - name: Zip CLI
      run: |
        Compress-Archive artifacts/mur-win-x64/* artifacts/mur-win-x64.zip
        Compress-Archive artifacts/mur-win-arm64/* artifacts/mur-win-arm64.zip
    - name: Upload artifacts
      uses: actions/upload-artifact@...
      with:
        name: packages
        path: |
          artifacts/nupkg/*.nupkg
          artifacts/nupkg/*.snupkg
          artifacts/mur-*.zip
        retention-days: 14
```

### New job: `publish` (push to `main` only, not PRs)

```yaml
publish:
  name: Publish
  needs: pack
  if: github.event_name == 'push' && github.ref == 'refs/heads/main'
  runs-on: windows-latest
  environment: internal-feed          # gated via GitHub environment
  steps:
    - uses: actions/download-artifact@... with: { name: packages }
    - name: Push to Azure Artifacts (P1)
      run: dotnet nuget push **/*.nupkg --source ${{ vars.INTERNAL_FEED }} --api-key ${{ secrets.FEED_PAT }} --skip-duplicate
```

### New job: `release` (tag push only)

Runs on `v*` tag pushes. Creates a GitHub Release, uploads the `mur-*.zip` assets and `.nupkg`, and (in P2/P3) pushes to the appropriate public feed.

### PR comment with install instructions (nice-to-have)

A small workflow step that posts a comment on the PR with:

```
Installable preview:
  Add to nuget.config: <add key="reactor-pr" value="https://nuget.pkg.github.com/microsoft/index.json" />
  <PackageReference Include="Microsoft.UI.Reactor" Version="0.1.0-pr.123.abc1234" />
```

Makes PR review against real consumers trivial.

## 10. Feeds and Access Control

| Phase | Primary feed | Auth | Who can read |
|---|---|---|---|
| P1 | Azure Artifacts (internal org feed) | AAD / PAT | Microsoft employees |
| P2 | GitHub Packages (scoped to `microsoft/reactor3`) | GitHub PAT | Invited NDA partners |
| P3 | NuGet.org | Anonymous | Everyone |

PR build artifacts (GitHub Actions artifact storage) are accessible to anyone with repo read access in all phases — we rely on repo permissions, not a separate ACL.

### Switching feeds

All three destinations speak the NuGet v3 protocol; the only thing that changes between phases is the `--source` argument and credentials. No code changes needed.

## 11. Signing

Three layers to consider: the DLLs inside the package, the `.nupkg` itself, and the `mur.exe`.

### Phase 1 (internal, self-host only)

- **DLLs:** unsigned. Loads fine. No SmartScreen involvement for DLLs loaded by a consumer's app.
- **.nupkg:** unsigned. Internal Azure Artifacts accepts unsigned packages if policy allows; if Microsoft's org policy already requires signed packages on the internal feed, we'll need ESRP here too — **action item: confirm with the feed owner before P1 goes live.**
- **`mur.exe`:** unsigned. Users will see SmartScreen on first run. Acceptable for internal pre-release.

### Phase 2–3 (external)

- **DLLs:** Authenticode-signed via **ESRP** (Microsoft's internal signing service). Required by Microsoft OSS policy for anything under `microsoft/`.
- **.nupkg:** NuGet-signed (certificate via ESRP). Required by NuGet.org for packages from the Microsoft publisher.
- **`mur.exe`:** Authenticode-signed. Eliminates SmartScreen warnings.

ESRP integration pattern:

1. Register the repo with the signing team (one-time org setup).
2. Add the `Azure/azure-esrp-signing-action` (or internal equivalent) to the `publish`/`release` jobs.
3. Signing runs after `dotnet pack` and before `dotnet nuget push`.

ESRP onboarding typically takes 1–2 weeks and gates the P2 milestone. Start this process **now**, in parallel with P1, so it's ready when P2 begins.

## 12. Consumer Experience

Target experience after P1 ships:

```xml
<!-- MyApp.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows10.0.22621.0</TargetFramework>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.UI.Reactor" Version="0.1.0-*" />
  </ItemGroup>
</Project>
```

Plus a one-time `nuget.config` in the repo root pointing at our internal feed:

```xml
<configuration>
  <packageSources>
    <add key="reactor-internal" value="https://pkgs.dev.azure.com/microsoft/.../index.json" />
  </packageSources>
</configuration>
```

Consumer gets: framework, analyzers, source generator, and WinUI SDK (transitive). Optionally installs `mur` via the install script. No clone of `microsoft/reactor3` required.

## 13. Open Questions

- **License change.** README says "Microsoft Internal" but the repo lives on the public `microsoft/` org. Public distribution (P3) requires resolving this — almost certainly moving to MIT — before any NuGet.org push.
- **Internal feed ownership.** Which Azure Artifacts organization hosts the P1 feed? Creating one takes ~a day plus approvals.
- **Signing prerequisite for P1.** Does the chosen internal feed enforce signed packages? If yes, P1 needs ESRP too, not just P2.
- **Package ID.** `Microsoft.UI.Reactor` assumes we stay in the `Microsoft.UI.*` namespace (see spec 018 for the namespace rename). If that namespace decision changes, the package ID follows.
- **WinUI SDK version.** We currently pin `Microsoft.WindowsAppSDK` 2.0.0-preview2. Consumers who want a different WinUI version will conflict. Decide: float this transitively, or lock it and force consumers to match.
- **`mur` install-script trust boundary.** `iwr | iex` from GitHub Releases works for P1 but will concern P3 users. Document the signed-binary fallback (direct download + verify signature) before public launch.

## 14. Implementation Phases

### P1 — Internal, ~1 week

1. Add `Directory.Pack.props` with shared packaging metadata.
2. Enable `IsPackable` on `Reactor.csproj`; bundle analyzer + source-generator DLLs.
3. Add MinVer, tag the repo at `v0.1.0-preview.0`.
4. Extend `ci.yml` with `pack` and `publish` jobs; target internal Azure Artifacts feed.
5. Add PR-comment workflow with install snippet.
6. Write a consumer-facing `docs/guide/install.md` page.
7. Verify end-to-end by installing the package into a throwaway consumer repo outside the enlistment.

### P2 — NDA external, ~2 weeks after P1

1. Begin ESRP onboarding (start in parallel with P1).
2. Add signing steps to `publish`/`release` jobs.
3. Set up GitHub Packages as the P2 feed; configure invite-only access.
4. Resolve open question on license (move to MIT or agreed alternative).
5. Publish the VSIX to GitHub Releases alongside the NuGet.
6. Ship the install-`mur` script, signed.

### P3 — Public, ~4–6 weeks after P1

1. Publish to NuGet.org under the `Microsoft` publisher.
2. Publish the VS Code extension to the Marketplace under the Microsoft publisher.
3. Update README with public install instructions; remove the internal `nuget.config`.
4. Cut `v1.0.0-preview.1` as the first public release (still prerelease, still breaking).
5. Announce.

---

## Appendix A — File changes summary

| File | Change |
|---|---|
| `Directory.Pack.props` (new) | Shared packaging metadata |
| `src/Reactor/Reactor.csproj` | `IsPackable=true`, pack analyzer/source-gen DLLs |
| `src/Reactor.Analyzers/Reactor.Analyzers.csproj` | `IsPackable=false` (now bundled) |
| `src/Reactor.Localization.Generator/Reactor.Localization.Generator.csproj` | Stays as-is (already non-packable) |
| `src/Reactor.Cli/Reactor.Cli.csproj` | No changes — published as self-contained exe from CI |
| `.github/workflows/ci.yml` | Add `pack`, `publish`, `release` jobs; PR-comment workflow |
| `nuget.config` (new, at repo root) | Optional — points consumers at the internal/NDA feed during P1/P2 |
| `docs/guide/install.md` (new) | Consumer install docs |
| `install-mur.ps1` (new, at repo root) | Standalone CLI installer script |
