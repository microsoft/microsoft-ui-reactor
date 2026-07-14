# Reactor.MstatVerifier

Build-time verifier used by CI to guard the devtools trimming / AOT-isolation story
(spec-051). It has three modes, all invoked via `dotnet run` from
[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml):

- `absence|presence <path-to.mstat> <path-to.exe>` — reads a NativeAOT `*.mstat` payload and
  asserts a curated set of type/symbol names (devtools MCP server, `PreviewCaptureServer`,
  `System.Net.Http.*`, `System.Text.Json` metadata, …) are **absent** (devtools switched off)
  or **present** (positive control), and — in `absence` mode — that the hello-world AOT exe
  stays under a size budget.
- `reactor-il <path-to-Reactor.dll>` — walks `Reactor.dll` with Mono.Cecil and fails if any
  devtools *implementation* type leaked into the core assembly.
- `negative-resolution <path-to-fixture.csproj>` — shells `dotnet build` on a fixture and
  asserts it fails with `CS0246`/`CS0234` (devtools impl types are unavailable without the
  `Microsoft.UI.Reactor.Devtools` package).

It reads assembly metadata via **Mono.Cecil** and scans raw bytes for ASCII strings; it does
**no runtime reflection over its own managed types**, emits no JSON, and uses no `Regex`.

## Trimming / NativeAOT

Part of the repo-wide AOT effort ([#70](https://github.com/microsoft/microsoft-ui-reactor/issues/70)).

The IL2\*/IL3\* analyzer is **on by default** for this project (`IsAotCompatible`, via
`Directory.Build.targets`) and this tool's own code is warning-clean — it never reflects over
managed types; it parses metadata out of *files* with Mono.Cecil. Because it does not enumerate
a live assembly's members through runtime reflection, it needs **no `TrimmerRootAssembly`** and
no `[RequiresUnreferencedCode]` / `[DynamicallyAccessedMembers]` annotations — the trimmer
cannot prune anything this tool relies on reflectively.

**A native single-file build works** via an opt-in gate:

```pwsh
dotnet publish tools/Reactor.MstatVerifier/Reactor.MstatVerifier.csproj `
  -c Release -r win-x64 -p:ReactorMstatAot=true
```

Two things worth knowing:

- **`ReactorMstatAot=true` is opt-in, not unconditional.** CI runs the tool with plain
  `dotnet run … -c Release` (no RID). An always-on `<PublishAot>` would make a RID-less
  `dotnet publish` of this project fail with *"PublishAot requires a RuntimeIdentifier"*, so the
  gate keeps the normal path byte-identical and RID-free. `PublishAot` is set **inside** the
  csproj rather than forced as a global `-p:PublishAot=true` property, so the opt-in stays
  self-contained; `Directory.Build.targets` already forces `PublishAot=false` on the repo's
  `netstandard2.0` analyzer/generator projects to keep a global `PublishAot` from leaking there
  (`NETSDK1207`), though this tool references none of them.
- **The only ILC findings come from Mono.Cecil, in a path this tool never runs.** Mono.Cecil's
  dynamic PDB/MDB symbol-provider discovery (`Mono.Cecil.Cil.SymbolProvider`, which locates a
  symbol reader by name via `Activator.CreateInstance` / `Type.GetType` / `Assembly.GetType`)
  trips `IL2072`/`IL2057`/`IL2026`. This tool always reads with the default `ReaderParameters`
  (`ReadSymbols=false`), so that code path is never reached at runtime. The gate demotes
  **exactly those three codes** from error to warning for the ILC step (via
  `WarningsNotAsErrors`), rather than a blanket `IlcTreatWarningsAsErrors=false` — so any *other*
  trim/AOT warning, including new reflection in this tool's own code, still fails the native
  publish. Native link also needs the VS C++ tools (`vswhere.exe` / `link.exe`) on `PATH`.

**Verdict:** native NativeAOT publish is **feasible and complete** — the ~3 MB self-contained
exe produces output **byte-identical** to the JIT build across all three modes (`reactor-il`,
`absence` / `presence`, `negative-resolution`) plus the no-args usage path. The gate exists
purely as a reproducible proof; the everyday path is the plain host-arch `dotnet run` CI uses.
If you add
new reflection to this tool, the enabled analyzer will flag it — annotate it honestly, don't
re-disable the analyzer.
