# Reactor.ApiIndex

Builds the text of `skills/reactor.api.txt` (and its byte-identical copy under
`plugins/reactor/skills/reactor-dsl/references/`) by **reflecting over the built
`Reactor.dll`** — `Assembly.GetExportedTypes()` / `Type.GetMembers()` — and emitting a flat,
alphabetized signatures index. It is a build-time-only class library; the apphost that runs it
is the sibling `tools/Reactor.SignaturesGen`, and `ApiIndexGeneratorTests` drives the same
`ApiIndexGenerator.Generate(asm)` in-process (ARM64-safe).

Regenerate the two committed copies with `mur --regen-api` (or just build `Reactor.SignaturesGen`
— its `AfterBuild` target rewrites them).

## Trimming / NativeAOT

This tool's whole job is to **enumerate the full public surface of a WinUI-backed assembly via
reflection** — i.e. exactly what a trimmer removes. That makes it a genuinely hard AOT target
(harder than a source-parsing generator, which never touches built metadata), so the story is
worth spelling out:

**The IL2*/IL3* analyzer is enabled** (`IsAotCompatible=true`, replacing the old
`ReactorSkipAotAnalysis` opt-out). Every reflecting entry point is *annotated* honestly rather
than silenced: `ApiIndexGenerator.Generate` and each reflecting helper carry
`[RequiresUnreferencedCode]`, so the requirement bubbles to callers and the analyzer keeps
guarding against *new*, unintended reflection. (No `[RequiresDynamicCode]` — the generator does
metadata discovery and reflection-invoke only, no runtime codegen.) The `Reactor.SignaturesGen`
apphost acknowledges the resulting IL2026 at its one call site via a justified
`[UnconditionalSuppressMessage]`.

**A native single-file build does work** — with two caveats beyond a plain `dotnet publish`:

```pwsh
dotnet publish tools/Reactor.SignaturesGen -r win-x64 -c Release -p:ReactorApiAot=true
```

- `ReactorApiAot=true` sets `PublishAot` **inside** the csproj rather than passing
  `-p:PublishAot=true` as a global property. A global `PublishAot` leaks through Reactor's
  `ProjectReference` graph into the **netstandard2.0** analyzer/generator projects, which reject
  AOT with `NETSDK1207` before ILC even starts. Scoping it per-project sidesteps that.
- The gate also adds `<TrimmerRootAssembly Include="Reactor" />`. Because the generator reflects
  over precisely the members the trimmer would prune, **without rooting the assembly the native
  exe runs but emits an empty header-only skeleton (~1.7 KB) instead of the full ~315 KB index.**
  With the root, the ~30 MB native exe emits an index **byte-identical** to the committed file.
- `IlcTreatWarningsAsErrors=false` is applied by the same gate; native linking also needs the VS
  C++ tools (`vswhere.exe` / `link.exe`) on `PATH`. This flag covers third-party ILC warnings
  from the WinUI/CsWinRT dependency graph pulled in by `Reactor.dll` — **not** the generator's own
  code, which stays warning-clean through the `[RequiresUnreferencedCode]` annotations above and is
  independent of it.

**Verdict:** trim/AOT-honest, and a native NativeAOT publish is *feasible and complete* via the
opt-in gate above — but only because the generator is allowed to root the entire Reactor
assembly. It is not something to run in the normal build (the apphost path is a plain,
host-arch-matching exe); the opt-in gate exists purely as a reproducible proof. If you add a new
reflection call to the generator, the enabled analyzer will flag it — annotate it (don't
re-disable the analyzer).
