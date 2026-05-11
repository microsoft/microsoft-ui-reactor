---
name: reactor-build-and-check
description: "Building Reactor apps and reading diagnostics — `mur check <path>` is the build (same exit code as `dotnet build`, never re-run to confirm) with one-line diagnostics, skill pointers, and `→ try:` did-you-mean suggestions to use verbatim. Covers iteration vs `--final` workflow, the common-build-errors cheat table mapping `REACTOR_HOOKS_*` / `REACTOR_DSL_*` / `REACTOR_THEME_*` / `REACTOR_A11Y_*` / `CS*` IDs to fixes, when single-file vs `.csproj` matters for analyzer coverage, build prerequisites. Use when a build fails, you see an analyzer warning, or you want a structured diagnostic stream instead of raw MSBuild output."
---

## Build & verify

Run after every non-trivial edit. **Read the output** — `dotnet run` exits with code 1 on build failure; silent ≠ success.

### Single-file `.cs` (default for new apps)

```powershell
dotnet run App.cs -p:Platform=ARM64        # or -p:Platform=x64
```

Single-file builds **do not load analyzers**. You'll catch CS errors but not the Reactor-specific `REACTOR_*` warnings.

### `.csproj` (multi-file, analyzer coverage)

```powershell
dotnet build MyApp.csproj -p:Platform=ARM64
```

Analyzers are bundled in the `Microsoft.UI.Reactor` package and load automatically.

### `mur check` — structured output with skill pointers

```powershell
mur check MyApp.csproj                       # iteration mode (default)
mur check --final MyApp.csproj               # once iteration is clean — pre-merge sweep
```

**`mur check` is the build, not a separate check step.** It runs `dotnet build` under the hood and returns the same exit code. When `mur check` exits 0, the build is green — **do not re-run `dotnet build` to confirm**. They're the same compilation; a redundant `dotnet build` after a green `mur check` is wasted work.

Two enrichments over raw `dotnet build`:

1. **Skill pointers** for known `REACTOR_*` IDs — one-line links into the relevant skill section.
2. **Did-you-mean suggestions** for unknown identifiers, surfaced as `→ try: <name>  // [<evidence>]`.

Emits one diagnostic per line:

```
C:\path\Program.cs:15:23  W  REACTOR_DSL_001  Element produced by Select(...)…   → SKILL.md gotcha #6 (.WithKey on dynamic list items)
C:\path\Program.cs:34:16  E  CS1061  'ButtonElement' does not contain a definition for 'OnClick'   → try: Button(label, onClick: ...)  // [factory has Action onClick parameter]
```

`<path>` defaults to `.` and accepts a `.csproj`, a directory, or a single `.cs` file. Skill pointers fire only for known `REACTOR_*` IDs — vanilla `CS` errors come through with severity + code + message, plus the `→ try:` suggestion when the suggester has a high-confidence candidate.

If `mur` isn't on PATH, fall back to `dotnet build` and read the output directly. Don't spelunk the package cache for it — `mur` is published with the framework but is a separate install.

#### `→ try:` suggestions — trust them

When `mur check` emits `→ try: <name>`, use that exact name in your next edit. The suggestion has already been computed against the live Reactor surface for this exact diagnostic — **do not search adjacent or sibling names in the codebase, the skill cache, or `reactor.api.txt` to second-guess it.** If the suggestion turns out to be wrong, the next `mur check` will tell you and emit a new suggestion. That self-correcting cycle is the cheap inner loop; manual verification breaks it.

Anti-pattern: agents who treated `→ try:` as a hint to verify (re-grepping the namespace, reading `reactor.api.txt`, calling into reflection) regressed in evals because the verification cost dwarfed the cost of just trying the suggestion and letting the next build correct it.

#### Iteration vs `--final`

`mur check` (no flag) is **iteration mode**: a ranker suppresses noise (CS1591 XML-doc, CS0168 unused-var, IDE0xxx style hints, NuGet restore chatter) so you only see what's actually blocking the build. Run this inside the fix loop.

When `mur check` exits 0, you are done — the build is green. `mur check --final` is an optional pre-merge sweep that re-runs the build and emits the cosmetic/transient diagnostics the iteration ranker suppressed (XML doc gaps, unused locals, style hints, nullable warnings, NuGet restore chatter). It's the right tool for human code review or a CI ship-readiness gate. **It does not gate task completion** — running it is not required to declare done; if you choose to run it, treat any new diagnostics it surfaces as polish work, not blockers.

Additional flags:

- `mur check --strict` — promotes warnings to errors. Use for one-shot CI gates; not the inner loop.
- `mur check --quiet` — errors only. For sub-iteration loops where you want the smallest possible signal.
- `mur check -- <msbuild args>` — anything after `--` is forwarded verbatim to `dotnet build`. Override platform, config, restore, verbosity:
  ```powershell
  mur check -- -p:Platform=x64
  mur check --final -- -c Release --no-restore
  ```
  `mur` auto-injects `--nologo`, `-v:m`, and `-p:Platform={host arch}` only if you didn't already name the same flag in the passthrough section.

## Common build errors — cheat table

| ID | Severity | What it means | Fix |
|---|---|---|---|
| `REACTOR_HOOKS_001` | warning | Hook called inside `if` / `for` / `while` / `switch` / `try` | Move the hook to the top of `Render()`. Use the result conditionally, not the call. |
| `REACTOR_HOOKS_004` | warning | Hook `deps` contains a freshly-allocated object/array/lambda | Memoize with `UseMemo`, hoist to a field, or project to a scalar key. |
| `REACTOR_HOOKS_005` | warning | Hook called outside `Render()` or a custom-hook method | Move the call into `Render()` or a `Use*` helper. Hooks read slot state that only exists during render. |
| `REACTOR_HOOKS_006` | info | `UseResource` fetcher looks non-idempotent (`Post*`/`Create*`/`Delete*`/`Save*`) | Use `UseMutation` for writes — `UseResource` re-runs on deps change, retry, focus revalidation. |
| `REACTOR_HOOKS_007` | warning | `UseMemoCells` builder closure missing dependencies | Add the captured variable to the deps array. |
| `REACTOR_DSL_001` | warning | `Select(...)` projecting into a layout container without `.WithKey(...)` | `items.Select(i => Row(i).WithKey(i.Id)).ToArray<Element?>()`. Keys keep focus + animation state across reorders. |
| `REACTOR_THEME_001` | warning | Hardcoded color on a themed surface | Use `Theme.*` tokens (e.g. `Theme.PrimaryText`, `Theme.CardBackground`). See `reactor-design`. |
| `REACTOR_THEME_002` | info | Lightweight styling opportunity | Optional. Use `.Resources(r => r.Set("ButtonBackground", …))` for visual-state overrides. |
| `REACTOR_THEME_003` | info | `RequestedTheme` modifier available | Use `.RequestedTheme(ElementTheme.Dark)` for subtree theme overrides. |
| `REACTOR_A11Y_001` | warning | Icon-only button missing accessible name | Add `.AutomationName("Delete")` (or similar). |
| `REACTOR_A11Y_002` | warning | Image missing alt text | Add `.AutomationName(...)` or `.AccessibilityHidden(true)` for decorative images. |
| `REACTOR_A11Y_003` | warning | Form field missing label | Wrap in `FormField(input, label: "Email", required: true)`. |
| `CS0103` | error | "The name 'X' does not exist in the current context" | Missing `using` — most often `Microsoft.UI.Reactor.Layout` (FlexAlign), `Microsoft.UI.Xaml.Controls` (InfoBarSeverity, Orientation), or `static Microsoft.UI.Reactor.Factories`. |
| `CS1061` | error | "'X' does not contain a definition for 'Y'" | Modifier order — type-specific sugar (`.Bold()`, `.Foreground()` on `TextBlockElement`) must come before generic modifiers (`.Margin()`, `.Padding()`) that return base `Element`. |
| `CS0117` | error | "'Element' does not contain a definition for X" | Same root cause as CS1061 — modifier order, OR you're calling a factory that doesn't exist. Confirm against `reactor-dsl/references/reactor.api.txt`. |
| `MSB4025` | error | "The project file could not be loaded" | Single-file `.cs` build attempted without `-p:Platform=...` on a WinUI project. Add `-p:Platform=ARM64` (or x64). |
| `NETSDK1136` | error | "platform required" | Same fix — pass `-p:Platform=ARM64` or `x64`. |

If a `REACTOR_*` ID isn't in this table, the bundled analyzer DLL has more docs. The descriptions ship in the warnings themselves.

## Iteration discipline

- **`mur check` is the build.** Same exit code as `dotnet build`. Don't re-run `dotnet build` to confirm a green `mur check` — it's redundant work on the same compilation.
- **Trust `→ try:` suggestions directly.** They're precomputed against the actual Reactor surface for the exact diagnostic. Use the suggested name verbatim; don't grep adjacent or sibling names. If it's wrong, the next `mur check` will say so — that's the self-correcting loop.
- **Batch fixes.** Read every error/warning in one pass, fix them all, then re-build. Don't re-build after each single fix.
- **`mur check` in the loop. When it exits 0, you are done.** Iteration mode suppresses cosmetic noise so the real blocker doesn't scroll off attention. `mur check --final` is an optional pre-merge sweep for human review / CI gates — not a task-completion requirement; skipping it is fine.
- **Don't introspect via `[System.Reflection]`.** Enumerating Reactor types or members at runtime to "discover" the API is unnecessary and slow. This cheat table plus `mur check`'s did-you-mean suggestions plus `reactor-dsl/references/reactor.api.txt` cover the surface.
- **Trust the analyzer over your memory.** If `REACTOR_DSL_001` says "missing `.WithKey`", add `.WithKey(...)` — the analyzer is right.
- **Don't bypass.** Avoid `#pragma warning disable REACTOR_*` unless you have a specific known reason. The analyzers exist because the runtime symptoms are subtle (focus loss, identity drift, refetch storms).

## Prerequisites

| Requirement | Minimum | Install |
|---|---|---|
| .NET SDK | 10.0 | `winget install Microsoft.DotNet.SDK.10` |
| `mur` (optional) | latest | Build from source: `dotnet build src/Reactor.Cli`. Selfhost only today. |
| Microsoft.UI.Reactor | 0.0.0-local (selfhost) or a published version | Selfhost: `mur pack-local`. Consumer: `<PackageReference>` in `.csproj`. |
