# Reactor.Tests — AOT-honesty

`Reactor.Tests` is the large headless xUnit unit-test host (~2,200 test methods /
~12,500 xUnit cases). It is **never trimmed or NativeAOT-published in the normal
`dotnet test` path** — it runs on the JIT. This note documents why the IL2\*/IL3\*
trimming/AOT analyzer is nonetheless **enabled** here, how the intentional test-only
reflection is annotated, and the evidence-based verdict on a native-AOT test *run*.
Part of the repo-wide AOT-honesty effort tracked in
[issue #70](https://github.com/microsoft/microsoft-ui-reactor/issues/70).

## Analyzer-on (the primary deliverable)

Previously the project set `ReactorSkipAotAnalysis=true`, which turned the IL2\*/IL3\*
analyzer **off** (see `Directory.Build.targets`). That is a blunt opt-out: it silences
*all* trimming/AOT diagnostics for the project, so a genuinely-unsafe *new* reflection
call would slip in unnoticed.

That opt-out is now removed and replaced with `IsAotCompatible=true`. The analyzer runs,
promotes the curated IL codes to errors (per `Directory.Build.targets`), and the ~175
surfaced IL2\*/IL3\* diagnostics are resolved honestly per-site — **92 justified
`[UnconditionalSuppressMessage]` attributes added across 40 files** (two more were already
present in `SanitizeUrlTests`, for 94 total), **plus two tests rewritten to drop an avoidable
`dynamic` dependency (a fix, not a suppression)**. The analyzer therefore keeps guarding
against *new*, unintended reflection in test code.

### How the reflection is annotated

Almost every site carries a **`[UnconditionalSuppressMessage]`** with a per-site
justification. `[UnconditionalSuppressMessage]` (not `#pragma warning disable`, which ILC
ignores) is the form that survives a whole-program ILC pass, and it is **behaviour-neutral**:
unlike a `[DynamicallyAccessedMembers]` annotation it neither preserves nor prunes members, so
it cannot cause the runtime narrowing regression documented in issue #70 (comment 3, where a
`[DynamicallyAccessedMembers]` on a test helper stripped collection-enumeration members and
broke ~20 PropertyGrid selftests). A suppression is therefore **not** a claim that the
reflection is trim-safe — it documents that the reflection is intentional and that this host
runs on the JIT (never trimmed). Where the reflection was *avoidable*, it was **fixed** rather
than suppressed: the two `dynamic` chart-accessor tests (`AreaTests`/`LineTests`) now use a
concrete `record struct` instead of `Create<dynamic>`, so no suppression is needed and the
tests are genuinely AOT-clean. **No product (core Reactor) type was touched** — all changes
live inside this test project.

The suppressions fall into a small number of honest categories:

| Category | IL code(s) | Why it is intentional (JIT-only; not claimed trim-safe) |
|---|---|---|
| **Devtools/MCP reflection-based JSON** (`JsonSerializer.Serialize`/`Deserialize` with `DevtoolsMcpServer.JsonOpts`, no source-gen context) | IL2026 + IL3050 | Intentionally exercises the devtools JSON surface that issue #70 documents as RUC/RDC-by-design and not-yet-AOT-clean. Run on JIT. |
| **Generic reflection-based JSON** (tuning report, search-index round-trip into concrete records, anonymous-type serialize) | IL2026 + IL3050 | Reflection-based `System.Text.Json` without a source-gen context; JIT only. |
| **Assembly-wide contract/architecture scans** (`Assembly.GetTypes()` + member reflection: `CoreControlFamilyBoundaryTests`, `PublicApiSurfaceGuardTests`, `DescriptorSilentDropGuardTests`, `RuleRegistryCompletenessTests`, `AnalyzerPackagingTests`, the RegisterAllBuiltIns guard) | IL2026, IL2070, IL2067 | Enumerating the full type surface *is the point of the test* — exactly what trimming would prune. JIT only. |
| **Member access on concrete runtime objects** (`obj.GetType().GetProperty/GetField/GetMethod`) | IL2075 | Reflects a known member on a concrete object the test constructs. Intentional and JIT-only — **not** claimed trim-safe; behaviour-neutral (neither preserves nor prunes members). |
| **`Type`-parameter theory reflection** (`type.GetProperties()` over `typeof(...)` theory data) | IL2070, IL2067 | The `Type` flows from `typeof(...)` literals in the theory data. JIT-only, not claimed trim-safe; suppression preferred over a DAM annotation on the parameter (see the narrowing note above). |
| **`Assembly.Location`** (feed Roslyn `MetadataReference.CreateFromFile` / `PEReader` / doc-XML lookup) | IL3000 | Only affects single-file publish (Location is empty there); these Roslyn-compilation/metadata tests can't run single-file and the host is never single-file-published. |

**Class-level vs method-level scope.** A suppression is placed at **class scope** only where
the majority of the class's test methods exercise the one suppressed surface *and* the class
is named/scoped for it (e.g. `DevtoolsSerializationTests`, `TreeSchemaTests`,
`TypeRegistryUnmountTests`). Where the reflecting methods are a minority of the class (e.g.
`ReferenceOverlayTests`, `DevtoolsFireToolTests`, `McpDispatchTests`, `StdioTransportTests`,
and the large `MoreCoverageTests` grab-bags), the suppression is placed on the individual
method, helper, or field where the diagnostic fires — so a future *different* unsafe
reflection added elsewhere in the class is still caught.

**Result:** `dotnet build tests/Reactor.Tests -p:Platform=x64 -p:SkipSignaturesGen=true`
is warning/error-clean with the analyzer on, and all tests stay green:
`dotnet test --project tests/Reactor.Tests -p:Platform=x64 -p:SkipSignaturesGen=true`
→ **Failed: 0, Passed: 12454, Skipped: 64** (the skips are pre-existing).

## Native-AOT test *run* — DOCUMENTED BLOCKER (upstream)

The secondary question was whether the suite could be **published with NativeAOT and run as
a native executable** (a native regression gate). The answer, established by attempting it,
is **no — and the blocker is upstream in the xUnit v3 framework, not in this repo's test
code.**

Attempting the publish (with `PublishAot` set *inside* the csproj behind an opt-in property —
never a global `-p:PublishAot=true`, which leaks through the `ProjectReference` graph into the
netstandard2.0 analyzer/generator projects and trips `NETSDK1207`) surfaces two blockers, in
order:

1. **`NETSDK1150`** — the test host references sibling *executable* projects (`Reactor.Cli`,
   `minesweeper`, `Reactor.MurCheckGuardrail`, `Reactor.SearchIndex`) for their managed
   assemblies in in-process tests; a self-contained (AOT) exe rejects non-self-contained exe
   references. Worked around with `ValidateExecutableReferencesMatchSelfContained=false`.

2. **ILC hard-fails on the xUnit v3 runner itself.** Every remaining trim/AOT diagnostic in
   the ILC phase (IL2026/IL2055/IL2057/IL2060/IL2065/IL2067/IL2070/IL2072/IL2075/IL2077/
   IL2080/IL3000/IL3050) originates from the **xUnit v3 packages** —
   `xunit.v3.common`, `xunit.v3.core`, `xunit.v3.assert`, `xunit.v3.runner.*` — in
   `ArgumentFormatter`, `ReflectionExtensions`, `TypeHelper`, `AsyncUtility`,
   `FixtureMappingManager`, and `XunitTestRunnerBase`. These are the framework's own
   discovery / invocation / assertion-formatting paths: `Type.MakeGenericType` and
   `MethodInfo.MakeGenericMethod` for generic `[Theory]` data and generic test classes
   (genuine `RequiresDynamicCode`), `Activator.CreateInstance` for test classes and fixtures,
   `EventSource` object-graph serialization, and reflection over test types.
   **Zero** of these diagnostics come from this project's own (now-annotated) test code.

Because the failing reflection lives in the xUnit v3 assemblies, it is **not annotatable in
this repo** — `MakeGenericMethod`/`MakeGenericType` are real runtime-codegen requirements
that cannot be made AOT-safe by annotation; they would throw at runtime even if the ILC
warnings were downgraded. A native-AOT *run* of this suite is therefore not feasible with the
current xUnit v3 runner.

**Verdict:** analyzer-on is the correct **end state** for this project. This project's test
code is AOT-honest and AOT-clean; a native run is blocked upstream in xUnit v3. The framework
already ships an AOT regression gate via the selftest host (`docs/aot-support.md`,
`TESTING.md` §"Running selftests under NativeAOT"), which does not depend on the xUnit runner.
If xUnit v3 ships an AOT-safe runner (source-generated discovery, no runtime generic
instantiation), revisit this note.

### Adding new reflection to a test here

The analyzer is on. If you add a new reflecting call, it will flag it — **annotate it
honestly** (`[UnconditionalSuppressMessage]` with a specific justification, or restructure to
avoid the reflection). Do **not** re-add `ReactorSkipAotAnalysis=true`, and do **not** add
`[DynamicallyAccessedMembers]` to product types to silence a test diagnostic.
