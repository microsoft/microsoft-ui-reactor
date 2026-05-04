# 022 — Packaging and Distribution

**Status:** P0 in flight (interim release-asset path); P1+ still draft
**Date:** 2026-04-17 (last updated 2026-05-03)
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
- **Not** introducing native code dependencies. Reactor today is pure managed C#; an earlier Rust differ experiment was retired and there is no native component to package.
- **Not** producing MSIX / packaged-app artifacts. Reactor is a library; packaging the consumer's app is the consumer's problem.
- **Not** committing to semantic versioning stability until we go public. Pre-1.0 preview versions can break.

## 3. Audience and Rollout Phases

| Phase | Timing | Audience | Feed | Signing |
|---|---|---|---|---|
| **P0 — Interim release assets** | Now | Anyone with repo read access | GitHub Releases (.nupkg + skill-kit zip) | None |
| **P1 — Internal** | When the internal feed is plumbed | Microsoft employees only | Azure Artifacts (internal feed) | Optional |
| **P2 — NDA external** | ~2 weeks after P1 | Small group of external partners under NDA | GitHub Packages (private), invite-only | Required (ESRP) |
| **P3 — Public** | ~4–6 weeks after P1 | Open .NET / WinUI community | NuGet.org | Required (ESRP) |

Each phase adds consumers but keeps the same package ID and build pipeline — we just flip publish destinations and tighten the gate. **P0 exists because P1's internal feed plumbing has lead time and we want a usable artifact today.** Consumers download the `.nupkg` from a Release and add a local-folder NuGet source. When P1 lands, the same workflow gains a `publish` step targeting the internal feed; nothing about the build itself changes.

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

### 4.4 Agent skill kit (`reactor-skill-kit-<version>.zip`)

Single zip that bundles the agent-facing assets and the `mur` CLI:

```
reactor/                          ← extracts here; user copies to ~/.claude/skills/reactor/
├── SKILL.md                      ← root skill (full content, not a bootstrap)
├── skills/                       ← sub-skills loaded on demand
│   ├── design.md
│   ├── devtools.md
│   └── …
├── bin/
│   ├── x64/   mur.exe + runtime  ← self-contained `dotnet publish -r win-x64`
│   └── arm64/ mur.exe + runtime  ← self-contained `dotnet publish -r win-arm64`
└── install-skill-kit.ps1         ← copies the kit to the install location and adds bin/<arch> to user PATH
```

#### Why not split markdown from binary

Considered shipping `reactor-skills.zip` (markdown only) plus separate `mur-win-{x64,arm64}.zip`, but a single per-version artifact keeps the skill content and the CLI it documents in lockstep — `skills/devtools.md` describes a `mur` surface that must match the binary next to it. A user who downloads only the markdown half ends up with skill instructions that disagree with whatever `mur` they happen to have installed; the version-coupled bundle avoids that whole class of breakage.

#### `mur` and PATH (selfhost vs deployed)

Skills are PATH-agnostic — they always invoke `mur ...`, never a relative path. Each mode has its own way of getting `mur` on PATH:

- **Selfhost (cloned repo):** `Reactor.Cli.csproj` has an `AfterTargets="Build"` target (`MirrorBinForSelfhost`) that copies the freshly built CLI to `<repo>/bin/<arch>/`. Devs add that directory to `PATH` once.
- **Deployed kit:** `install-skill-kit.ps1` copies the kit to `~/.claude/skills/reactor/` (or `-Path` override) and prepends the matching `bin/<arch>` to the user's `PATH` via `[Environment]::SetEnvironmentVariable`.

Both modes produce the same on-disk layout (`bin/x64/mur.exe`, `bin/arm64/mur.exe`), which keeps skill content portable and removes any "is this dev or prod?" branching from the markdown.

#### Bootstrap pattern (retired)

The previous `selfhost/SKILL.md` "bootstrap" pattern — a tiny stub skill that told the agent to run `mur --skill` to fetch the real content — has been removed. It existed when the CLI was the source of truth for skill content; now `SKILL.md` at the repo root is. Concretely deleted:

- `src/Reactor.Cli/BOOTSTRAP-SKILL.md`
- `<SelfHostDir>` property + `PopulateSelfHost` MSBuild target in `Reactor.Cli.csproj`
- `selfhost/` entry in `.gitignore`

The `<EmbeddedResource Include="..\..\SKILL.md">` line stays — `mur --skill` still prints the full skill, useful for `mur --skill | less` style debugging without leaving the terminal.

> Mixing a binary into a skill directory is unusual — most agent skills are pure markdown and ask the user to install the CLI separately (winget, scoop, manual). The agent-native shape for runtime tooling is an MCP server, which `mur devtools` already exposes at `/mcp` (see `skills/devtools.md`). The skill-kit bundle is a pragmatic interim: as the MCP surface stabilizes, the kit can shrink back to pure markdown and the binary moves to MCP-only invocation. Tracked under §13.

### 4.5 Secondary packages (v1+, optional)

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

### Chosen approach: framework-dependent per-RID executables

On every CI run, publish:

- `bin/x64/mur.exe` and `bin/arm64/mur.exe` (plus their managed/native dependencies) inside `reactor-skill-kit-<version>.zip`
- Per RID — `--runtime win-x64` and `--runtime win-arm64` — to pick up the right native bits (Copilot SDK natives, etc.)

**Framework-dependent (`--self-contained false`)** — the consumer's machine supplies the .NET 9 desktop runtime. This saves ~70 MB per RID over self-contained. Tradeoffs:

- Requires `winget install Microsoft.DotNet.Runtime.9` on the consumer's machine. Acceptable for the P0 audience (Microsoft engineers) and for P2/P3 consumers willing to install a runtime.
- `install-skill-kit.ps1` checks for .NET 9 and warns clearly if it's missing.

**Sample apps stay self-contained.** Reactor's sample apps and bench/perf projects continue to use `WindowsAppSDKSelfContained=true` (the Directory.Build.props default). Sample apps are sensitive to the WinUI runtime version — bundling makes it trivial to test against different SDK versions during dev. Tools (`mur`) are not.

The kit zip is the deployable unit; consumers extract it and run `install-skill-kit.ps1` which copies to the install location and adds `bin/<arch>` to user `PATH`.

### Future: fold into `Microsoft.UI.Reactor.Tools`

Once `mur` stops depending on unusual native bits we can revisit shipping it as a `dotnet tool` package. That's out of scope for this spec.

## 7. VS Code Extension

The `vscode-reactor/` project already builds a VSIX via `npm run compile` + `vsce package`. Changes for this spec:

- Publish the `.vsix` as a GitHub Release asset on every tagged framework release, version-matched to the framework.
- **Phase 3 only:** publish to the VS Code Marketplace under the Microsoft publisher ID. This requires its own publisher-token setup in CI and is not on the critical path for P1/P2 — internal users can install from VSIX.

## 8. Versioning

### Scheme (P0): MinVer / commit height

Versions come from [MinVer](https://github.com/adamralph/minver), driven by git tags + commit height. The workflow runs `minver-cli` and passes the result via `-p:Version=…` to `dotnet pack` and `dotnet publish`.

| State | Version |
|---|---|
| Past tag `v0.1.0-preview.1` by N commits | `0.1.0-preview.1.N+<sha7>` |
| Exactly at tag `v0.1.0-preview.1` | `0.1.0-preview.1` |
| Tag push of `v0.1.0` | `0.1.0` (stable, P3 only) |
| No tags yet (defaults below) | `0.1.0-experimental.0.<height>+<sha7>` |
| `workflow_dispatch` with explicit `version` input | as supplied |

`minver-cli` flags:

- `-t v` — tag prefix
- `-p experimental.0` — default prerelease identifiers when no tag is reachable
- `-m 0.1` — minimum major.minor when no tag (so an untagged repo doesn't start at `0.0.x`)

### Bootstrap

Tag the repo once before the first CI run:

```sh
git tag v0.1.0-experimental.0
git push --tags
```

After that, every commit gets a unique, ordered version with zero coordination — height strictly increases as commits land. Milestone bumps are explicit tags (`v0.1.0-preview.1`, `v0.2.0-experimental.0`, …); MinVer takes it from there.

### Why MinVer over the alternatives

- **Daily patch** (originally drafted): no git-state requirement, but versions don't communicate "what changed" and collide within a UTC day. Atypical for NuGet packages.
- **Nerdbank.GitVersioning**: more capable but heavier — version.json schema, per-branch override support, deeper integration. Overkill for our cadence.
- **Manual SemVer bumps**: fine for a stable public package, but pre-1.0 we want every commit installable for review and don't want to bump versions by hand on each PR.

MinVer hits the sweet spot: tag-driven, no extra files in the repo, conventional output that matches what most public NuGet packages look like.

### PR vs main distinguishability

Pure MinVer doesn't add a PR identifier — both PR and main builds with the same git height produce the same `0.1.0-experimental.0.<height>` filename. This is acceptable because:

- The `+<sha7>` build metadata in the SemVer string differs (visible in package metadata, even if NuGet drops it from the filename).
- Workflow artifact uploads are run-scoped — each workflow run gets its own download URL regardless of version collision.
- Two PRs branched from the same merge base hitting the same commit height is rare in practice.

If collisions become a problem later, append a CI-only suffix in the workflow (e.g. `-pr.<num>` after MinVer's output). Not adding it now to keep version strings conventional.

### NuGet normalization caveat

NuGet strips leading zeros from numeric components (`0.01.0001` → `0.1.1`), so plain integers are the only option for the version components MinVer produces.

### Local builds

Local `dotnet pack` defaults to `0.0.0-local` (set in `Reactor.csproj`) — there's no MinVer PackageReference, so the local pack doesn't need git history or `minver-cli`. Pass `-p:Version=…` when you want a specific version locally. CI is the source of truth for shipped versions.

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

- **License.** Repo is MIT (root `LICENSE`, README points to it). Cleared for NuGet.org push from a licensing standpoint.
- **Internal feed ownership.** Which Azure Artifacts organization hosts the P1 feed? Creating one takes ~a day plus approvals.
- **Signing prerequisite for P1.** Does the chosen internal feed enforce signed packages? If yes, P1 needs ESRP too, not just P2.
- **Package ID.** `Microsoft.UI.Reactor` assumes we stay in the `Microsoft.UI.*` namespace (see spec 018 for the namespace rename). If that namespace decision changes, the package ID follows.
- **WinUI SDK version.** We currently pin `Microsoft.WindowsAppSDK` 2.0.0-preview2. Consumers who want a different WinUI version will conflict. Decide: float this transitively, or lock it and force consumers to match.
- **`mur` install-script trust boundary.** `iwr | iex` from GitHub Releases works for P1 but will concern P3 users. Document the signed-binary fallback (direct download + verify signature) before public launch.

## 14. Implementation Phases

### P0 — Interim release-asset distribution (in flight)

Done:
1. Add packaging metadata to `src/Reactor/Reactor.csproj` (PackageId, license file, symbol package). Version comes from MinVer in CI; local pack uses `0.0.0-local`.
2. Bundle `Reactor.Analyzers.dll` and `Reactor.Localization.Generator.dll` into the framework `.nupkg` under `analyzers/dotnet/cs/`. Flip `Reactor.Analyzers.csproj` to `IsPackable=false` (no longer ships standalone).
3. Replace the bootstrap pattern (see §4.4) with a `MirrorBinForSelfhost` build target that drops `mur.exe` at `<repo>/bin/<arch>/`. Target is gated on a concrete Platform (x64/ARM64) to avoid creating a `bin/anycpu/` folder when sln-config translation collapses Platform to AnyCPU.
4. Add `.github/workflows/release.yml` (workflow name: **Package**) running on:
   - **PRs** (paths-ignore `docs/guide/**`) → MinVer-computed version, uploads x64 + arm64 artifacts (no Release).
   - **Push to `main`** → MinVer-computed version, uploads artifacts (no Release).
   - **Tag push (`v*`)** → version from tag (no paths-ignore so tag pushes always run), uploads artifacts AND creates a GitHub Release with `.nupkg` + `.snupkg` + skill kit zip attached.
   - **Manual `workflow_dispatch`** → caller-supplied version, uploads artifacts (no Release).
5. Add `tools/install-skill-kit.ps1` (shipped inside the kit zip).
6. Update `skills/devtools.md` with the "Getting `mur` on your PATH" note covering both selfhost and kit modes.

Verified locally:
- `dotnet build Reactor.sln -c Release` → 0 errors.
- `dotnet test tests/Reactor.Tests` → 6836 passed.
- `dotnet test tests/Reactor.SelfTests` → 639 passed.
- `dotnet pack src/Reactor -c Release -p:Platform=x64 -p:Version=0.0.1-smoke` → produces `.nupkg` containing `lib/net9.0-windows10.0.22621/Reactor.dll`, `analyzers/dotnet/cs/Reactor.Analyzers.dll`, `analyzers/dotnet/cs/Reactor.Localization.Generator.dll`, `LICENSE`, `Reactor.xml`.

Still TODO under P0:
- **Bootstrap MinVer**: `git tag v0.1.0-experimental.0 && git push --tags` so the first CI run produces `0.1.0-experimental.0.<height>` rather than the pre-bootstrap default.
- First end-to-end test: after bootstrapping, push a tag like `v0.1.0-preview.1`, verify the workflow produces both assets, install into a throwaway consumer repo from the Release page.
- Verify the on-PR pack produces a complete kit zip (no environment-specific path issues in `Compress-Archive`).
- Decide whether the skill kit should also be smoke-tested by piping it through `install-skill-kit.ps1` on a clean Windows VM.

### P1 — Internal, ~1 week

P0 already covered packaging metadata, analyzer/source-generator bundling, and version generation (see §8). P1 adds the internal-feed publish step on top of the existing `release.yml`:

1. Add a `publish` step to `release.yml`, gated on `push` to `main`, that does `dotnet nuget push` against the internal Azure Artifacts feed using a `FEED_PAT` secret.
2. Add a PR-comment workflow with the install snippet pointing at the internal feed.
3. Write a consumer-facing `docs/guide/install.md` page.
4. Verify end-to-end by installing the package into a throwaway consumer repo outside the enlistment.
5. (Optional) Once a second packable project lands, lift shared metadata into `Directory.Pack.props`.

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

### Done in P0

| File | Change |
|---|---|
| `src/Reactor/Reactor.csproj` | `IsPackable=true`; package metadata; bundles `Reactor.Analyzers.dll` and `Reactor.Localization.Generator.dll` under `analyzers/dotnet/cs/`; ships `LICENSE` |
| `src/Reactor.Analyzers/Reactor.Analyzers.csproj` | `IsPackable=false` — bundled into framework package, no longer ships standalone |
| `src/Reactor.Cli/Reactor.Cli.csproj` | Replaced `PopulateSelfHost` target with `MirrorBinForSelfhost` (mirrors build output to `<repo>/bin/<arch>/`); removed `<SelfHostDir>` |
| `src/Reactor.Cli/BOOTSTRAP-SKILL.md` | **Deleted** — bootstrap pattern retired |
| `.github/workflows/release.yml` (new) | Tag-triggered: pack framework, publish `mur` for x64+arm64, assemble skill kit zip, create GitHub Release with both assets |
| `tools/install-skill-kit.ps1` (new) | Ships inside the kit zip; copies to install location and adds `bin/<arch>` to user PATH |
| `skills/devtools.md` | Added "Getting `mur` on your PATH" section explaining selfhost vs deployed |
| `.gitignore` | Removed `selfhost/` entry — directory no longer generated |

### Planned for P1+

| File | Change |
|---|---|
| `Directory.Pack.props` (new) | Centralize packaging metadata across packable projects (defer until there's a second packable project) |
| `.github/workflows/ci.yml` | Add `pack` and `publish` jobs targeting the internal Azure Artifacts feed |
| `nuget.config` (new, at repo root) | Optional — points consumers at the internal/NDA feed during P1/P2 |
| `docs/guide/install.md` (new) | Consumer install docs |
| `Microsoft.SourceLink.GitHub` | Add to `Reactor.csproj` once the repo is public so consumers can step into source |
