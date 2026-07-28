# spec047-aggregator

Reads the JSON-Lines result streams produced by `PerfBench.ControlModel` (micro M1–M13)
and the `StressPerf.*` macro variants, and emits the three Spec 047 §15.6 reporting tables
(absolute comparison, Reactor delta, WinUI gap) plus a per-PR `trend.csv` and an
`excluded.txt` list of rows dropped for environment-metadata mismatch.

```pwsh
dotnet run --project tools/spec047-aggregator -- --in "<glob>.jsonl" --out <dir> [--baseline <sku>] [--ci-min-reps N]
```

## Trimming / NativeAOT

This tool is **trim/AOT-clean with no annotations**. Unlike the reflection-heavy
`tools/Reactor.ApiIndex` (which enumerates a WinUI assembly's surface and must root it), the
aggregator only reads JSON-Lines and writes Markdown/CSV — it has **zero reflection**, no
`Regex`, and already serializes through the **System.Text.Json source generator**
(`AggregatorJsonContext`, `[JsonSerializable(typeof(Row))]`). The IL2\*/IL3\* analyzer is on
for every net10+ project via `Directory.Build.targets` and reports nothing here, so no
`[RequiresUnreferencedCode]`, `[UnconditionalSuppressMessage]`, or `TrimmerRootAssembly` is
required.

A full native NativeAOT publish works via an **opt-in gate** (`-p:Spec047Aot=true`, off by
default):

```pwsh
# vswhere.exe / link.exe (VS C++ tools) must be on PATH for native linking:
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
dotnet publish tools/spec047-aggregator/Spec047Aggregator.csproj -c Release -r win-x64 -p:Spec047Aot=true
```

That produces a ~2.8 MB self-contained native single-file exe (no managed runtime DLLs)
whose output is **byte-identical** to the JIT build's.

### Why opt-in rather than unconditional `PublishAot`

The gate sets `<PublishAot>true</PublishAot>` **inside** the csproj (guarded on `Spec047Aot`)
rather than always-on because this project is a member of `Reactor.slnx`:

- An unconditional `PublishAot` turns *every* plain `dotnet publish` of the tool — and a
  whole-solution publish — into a native ILC compile that requires the VS C++ toolchain
  (`vswhere.exe` / `link.exe`) on PATH, so it would fail on any machine or CI leg that lacks
  them.
- Gating it keeps the default `dotnet build` / `dotnet run` / `dotnet publish` verbs behaving
  exactly as before (a plain managed apphost) and makes native AOT an explicit opt-in.

If you add reflection or a non-source-generated serializer to this tool, the enabled analyzer
will flag it — annotate it honestly (don't disable the analyzer).

Part of the per-tool NativeAOT migration tracked by
[#70](https://github.com/microsoft/microsoft-ui-reactor/issues/70).
