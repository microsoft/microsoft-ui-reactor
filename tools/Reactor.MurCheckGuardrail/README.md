# Reactor.MurCheckGuardrail

Offline / CI guardrail for spec 038 §8 — the `mur check` **suppress→error invariant**. It reads
two `mur check --trace` JSONL files (one written from an iteration-mode run, one from `--final`)
and asserts that every diagnostic surfaced as an **Error** in the `--final` trace still scores
`> 0` at Error severity in the live `PolicyTable` iteration column. In plain terms: the
"universal error floor" can never be edited away to let a real build break hide mid-iteration.

- Exit `0` — no violation (plus any non-failing `[advisory]` lines on stdout).
- Exit `1` — at least one violation; details on stderr.
- Exit `2` — usage / IO error.

```pwsh
Reactor.MurCheckGuardrail <iter-trace.jsonl> <final-trace.jsonl>
```

It re-uses `PolicyTable` from `src/Reactor.Cli` directly (ProjectReference +
`InternalsVisibleTo`) so the audit and the runtime ranker can never drift. The logic lives in
`GuardrailRunner`; `GuardrailRunnerTests` (in `tests/Reactor.Tests`) drives it in-process.

## Trimming / NativeAOT

The tool is trim- and NativeAOT-clean and publishes to a **single native exe**:

```pwsh
# vswhere.exe / link.exe must be discoverable for native linking:
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"

dotnet publish tools/Reactor.MurCheckGuardrail -r win-x64 -c Release -p:ReactorGuardrailAot=true
```

(Add `-p:SkipSignaturesGen=true` for faster local iteration — it only skips the unrelated
`reactor.api.txt` regen that building `Reactor.Cli` otherwise triggers; CI sets `CI=true`, which
skips it too.)

- **Byte-identical output.** The native exe's stdout / stderr / exit codes match the JIT build
  exactly, verified across all reachable paths — the clean pass, the `-warnaserror` advisory,
  the usage error, and the missing-file error.
- **Zero ILC warnings, no rooting, no warning-downgrade.** The only things reachable from `Main`
  are the pure `PolicyTable.Score` and `System.Text.Json`'s `JsonDocument` (no
  `JsonSerializer<T>`, no reflection, no `Regex`). Everything the `Reactor.Cli` ProjectReference
  drags in — Roslyn (`Microsoft.CodeAnalysis.CSharp`), YamlDotNet, `System.Drawing`, the Copilot
  SDK — is unreachable, so the trimmer removes it. That is why **no** `TrimmerRootAssembly` and
  **no** `IlcTreatWarningsAsErrors=false` are needed here, unlike the sibling `Reactor.SearchIndex`
  tool: it parses gallery *source* with Roslyn at runtime, so `Microsoft.CodeAnalysis` stays
  reachable and trips `IL3000` (`Assembly.Location`) and must downgrade the warning — whereas this
  tool calls only `PolicyTable.Score`, so ILC is clean even under Release's repo-wide
  `TreatWarningsAsErrors`.

### Why opt-in (`ReactorGuardrailAot`) instead of always-on

`PublishAot` is set **inside** the csproj behind the off-by-default `ReactorGuardrailAot`
property rather than as a global `-p:PublishAot=true`. Three reasons:

- **No analyzer-graph leak.** A global `PublishAot` flows across the whole ProjectReference
  graph; scoping it per-project keeps it from ever landing on a `netstandard2.0`
  analyzer/generator, which reject AOT with `NETSDK1207`.
- **Normal build/publish stay green.** Always-on `PublishAot` forces every `dotnet publish`
  self-contained and RID-bound, breaking the ordinary framework-dependent publish. The gate
  leaves the normal build, the framework-dependent publish, and the whole-solution
  `dotnet build Reactor.slnx` untouched.
- **`Reactor.Cli` is a framework-dependent Exe.** A self-contained AOT exe referencing the
  non-self-contained `mur` executable trips `NETSDK1150`. The gate sets
  `ValidateExecutableReferencesMatchSelfContained=false` — correct here because we consume only
  the managed `PolicyTable` *type*, never `mur`'s apphost, and ILC reads `mur.dll`'s IL directly.

**Verdict:** NativeAOT-publishable — feasible and complete via the opt-in gate, byte-identical
output, ILC warning-clean. Part of the repo-wide AOT effort (issue #70).
