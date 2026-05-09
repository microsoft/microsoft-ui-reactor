---
name: reactor-build-and-check
description: "Building Reactor apps and reading diagnostics — `mur check <path>` for one-line diagnostics with skill pointers, the common-build-errors cheat table mapping `REACTOR_HOOKS_*` / `REACTOR_DSL_*` / `REACTOR_THEME_*` / `REACTOR_A11Y_*` / `CS*` IDs to fixes, when single-file vs `.csproj` matters for analyzer coverage, build prerequisites. Use when a build fails, you see an analyzer warning, or you want a structured diagnostic stream instead of raw MSBuild output."
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
mur check MyApp.csproj
```

Emits one diagnostic per line:

```
C:\path\Program.cs:15:23  W  REACTOR_DSL_001  Element produced by Select(...)…   → SKILL.md gotcha #6 (.WithKey on dynamic list items)
```

`<path>` defaults to `.` and accepts a `.csproj`, a directory, or a single `.cs` file. Skill pointers fire only for known `REACTOR_*` IDs — vanilla `CS` errors come through with severity + code + message but no pointer.

If `mur` isn't on PATH, fall back to `dotnet build` and read the output directly. Don't spelunk the package cache for it — `mur` is published with the framework but is a separate install.

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

- **Batch fixes.** Read every error/warning in one pass, fix them all, then re-build. Don't re-build after each single fix.
- **Trust the analyzer over your memory.** If `REACTOR_DSL_001` says "missing `.WithKey`", add `.WithKey(...)` — the analyzer is right.
- **Don't bypass.** Avoid `#pragma warning disable REACTOR_*` unless you have a specific known reason. The analyzers exist because the runtime symptoms are subtle (focus loss, identity drift, refetch storms).

## Prerequisites

| Requirement | Minimum | Install |
|---|---|---|
| .NET SDK | 10.0 | `winget install Microsoft.DotNet.SDK.10` |
| `mur` (optional) | latest | Build from source: `dotnet build src/Reactor.Cli`. Selfhost only today. |
| Microsoft.UI.Reactor | 0.0.0-local (selfhost) or a published version | Selfhost: `mur pack-local`. Consumer: `<PackageReference>` in `.csproj`. |
