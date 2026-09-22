# Localization CI Integration

Gate localization quality in CI with three `mur loc` commands. All three
return a non-zero exit code on failure, so they work as pipeline gates
without extra scripting.

## Azure Pipelines

Add the following step group to your build pipeline. It runs after
`dotnet build` so that the source generator has already validated
structural issues (missing keys → `REACTOR_LOC001` warnings/errors).

```yaml
# ── Localization quality gates ────────────────────────────────────
- task: DotNetCoreCLI@2
  displayName: 'Loc: check for unextracted strings'
  inputs:
    command: 'run'
    projects: 'src/Reactor.Cli/Reactor.Cli.csproj'
    arguments: '-- loc extract --source $(Build.SourcesDirectory)/src --dry-run'

- task: DotNetCoreCLI@2
  displayName: 'Loc: validate ICU syntax & parameter consistency'
  inputs:
    command: 'run'
    projects: 'src/Reactor.Cli/Reactor.Cli.csproj'
    arguments: '-- loc validate --resources $(Build.SourcesDirectory)/src/Strings'

- task: DotNetCoreCLI@2
  displayName: 'Loc: check for unused keys'
  inputs:
    command: 'run'
    projects: 'src/Reactor.Cli/Reactor.Cli.csproj'
    arguments: '-- loc prune --source $(Build.SourcesDirectory)/src --resources $(Build.SourcesDirectory)/src/Strings --dry-run'
```

### What each gate catches

| Command | Exit code ≠ 0 when | Typical fix |
|---|---|---|
| `extract --dry-run` | Bare string literals found in DSL calls — icon-glyph-only literals don't count | Run `mur loc extract --rewrite` locally |
| `validate` | Broken ICU syntax or parameter mismatch across locales | Fix the `.resw` value |
| `prune --dry-run` | Keys in `.resw` with zero code references | Run `mur loc prune` locally to remove them |

### Optional: translation coverage status

`mur loc status` prints a coverage table but always exits 0 — it's
informational, not a gate. Add it as a non-failing step if you want
coverage visibility in CI logs:

```yaml
- task: DotNetCoreCLI@2
  displayName: 'Loc: translation coverage report'
  inputs:
    command: 'run'
    projects: 'src/Reactor.Cli/Reactor.Cli.csproj'
    arguments: '-- loc status --resources $(Build.SourcesDirectory)/src/Strings'
  continueOnError: true
```

### MSBuild-level gate (source generator)

The source generator already emits `REACTOR_LOC001` diagnostics for keys
present in the default locale but missing in other locales. To make these
build-breaking:

```xml
<!-- In your .csproj or Directory.Build.props -->
<PropertyGroup>
  <ReactorLocMissingKeySeverity>Error</ReactorLocMissingKeySeverity>
</PropertyGroup>
```

This catches missing translations at build time, before the CLI gates
even run.
