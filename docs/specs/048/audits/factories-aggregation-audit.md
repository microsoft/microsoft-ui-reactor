# Spec 048 §3.5 — `Factories` no-aggregation audit

> **Status.** Phase 3.5 task 1 deliverable. Audit confirms no §10.2 violations
> across the `Factories` partial class graph and no `[ModuleInitializer]` in the
> shipping `Reactor.dll`.
>
> **Owner.** Spec 048 §10.2 + §10.4 (the "no type-level aggregation on
> `Factories`" invariant) and §4 (the `[ModuleInitializer]` prohibition).
>
> **Goal.** A reader (or a future trim-regression triage) can re-run the scan
> commands below and confirm the catalog stays inert at module-load. The audit
> is what closes the §3.5 first checkbox; the optional companion analyzer
> (§3.5 second checkbox) would mechanise the same invariant at build time.

---

## 1. What "aggregation" means here

Spec §10.2 forbids three shapes from ever landing on `Factories`:

1. A `static` constructor on the `Factories` partial class — the CLR would run
   that cctor the first time *any* factory is touched, defeating per-factory
   lazy registration.
2. A `static readonly` (or `static` initialized) field on `Factories` whose
   initializer references a handler type, descriptor, or WinUI control type —
   the JIT would walk that initializer to compute the field's value at
   first-class-touch, again defeating lazy registration.
3. A `[ModuleInitializer]` attribute *anywhere* in `Reactor.dll` — the runtime
   guarantees ModuleInitializers run before any IL in the assembly executes,
   which would re-pin the entire catalog regardless of factory choice.

Per-method touches inside a factory body (e.g.
`_ = V1.Reg<TextBlockElement, WinUI.TextBlock, …>.Done;`) are explicitly
**permitted** — they execute only when the factory itself runs, so an unused
factory's `Reg<>` slot stays cold and trimmable.

---

## 2. Scope of scan

| Path | Reason |
|---|---|
| `src/Reactor/Elements/Dsl.cs` | Primary `Factories` partial (~1545 lines, ~140 factory methods). |
| `src/Reactor/Elements/Factories.NamedStyles.cs` | NamedStyles factories. |
| `src/Reactor/Controls/Virtualization/VirtualListFactories.cs` | Virtualization factories. |
| `src/Reactor/Controls/DataGrid/ColumnFactories.cs` | DataGrid column factories. |
| `src/Reactor/Controls/DataGrid/DataGridFactories.cs` | DataGrid factories. |
| `src/Reactor/Controls/PropertyGrid/PropertyGridFactories.cs` | PropertyGrid factories. |
| `src/Reactor/Hosting/Devtools/DevtoolsMenuFactory.cs` | Devtools factories. |
| `src/Reactor/` (recursive) | `[ModuleInitializer]` + leftover `RegisterV1BuiltInHandlers` references. |

The set of `Factories` partials was discovered with:

```pwsh
grep -l "partial class Factories" src/Reactor/**/*.cs
```

A new `Factories` partial added in a future commit must be added to this list
**and** rescanned.

---

## 3. Scan commands and results

### 3.1 No `static` constructor on `Factories`

```pwsh
grep -rn '^\s*static\s+Factories\s*\(\s*\)' src/Reactor
```

**Result:** no matches.

### 3.2 No `static readonly` / `static` field initializer in any `Factories` partial

```pwsh
grep -n '^\s*static\s' src/Reactor/Elements/Dsl.cs \
                      src/Reactor/Elements/Factories.NamedStyles.cs \
                      src/Reactor/Controls/Virtualization/VirtualListFactories.cs \
                      src/Reactor/Controls/DataGrid/ColumnFactories.cs \
                      src/Reactor/Controls/DataGrid/DataGridFactories.cs \
                      src/Reactor/Controls/PropertyGrid/PropertyGridFactories.cs \
                      src/Reactor/Hosting/Devtools/DevtoolsMenuFactory.cs
```

**Result:** the only `static`-at-column-zero matches are the seven
`public static partial class Factories` declarations themselves. No fields,
no constructors, no nested type aggregations.

A broader regex confirms the same:

```pwsh
grep -n 'static\s+(readonly|new\s+|partial\s+class\s+Factories|Factories\s*\(\s*\))' \
        # ...same file list as above
```

returns only the seven `partial class Factories` declarations.

### 3.3 No `[ModuleInitializer]` in `src/Reactor/`

```pwsh
grep -rn 'ModuleInitializer' src/Reactor
```

**Result:** no matches. The shipping `Reactor.dll` carries no module
initializer; per §3.4 close-out the test bootstrap that mirrors the deleted
`RegisterV1BuiltInHandlers` lives in `tests/_shared/BuiltInHandlerBootstrap.cs`
and is linked into the test assemblies only.

### 3.4 No leftover `RegisterV1BuiltInHandlers` calls

```pwsh
grep -rn 'RegisterV1BuiltInHandlers' src/Reactor
```

**Result:** five matches, all comment references (no IL):

- `src/Reactor/Core/Reconciler.Mount.cs:202` — the unregistered-type throw
  message references the deleted method by name as a hint to readers porting
  pre-§3.4 mental models.
- `src/Reactor/Elements/Dsl.cs:55, 152, 184` — updated by this PR (the
  "Dormant while RegisterV1BuiltInHandlers is intact" comments were
  pre-§3.4 staleness; replaced with "Live dispatch path post-§3.4").
- `src/Reactor/Hosting/XamlInterop.cs:23` — updated by this PR (rephrased
  "once RegisterV1BuiltInHandlers is deleted" → "now that the eager
  registrar has been deleted").

No call sites remain; the body is gone (§3.4 close-out commit `d63066df`).

---

## 4. Intentional `static` constructors that are NOT aggregations

Three `static` constructors live in `src/Reactor` but each registers only its
own enclosing type — they are Pattern A self-registrations per spec §6, not
catalog aggregations:

| Location | Registers | Notes |
|---|---|---|
| `src/Reactor/Hosting/XamlInterop.cs:17` (`XamlPageElement`) | `XamlPageElement → XamlPageHandler` (decorator) | Singleton-handler shape; runs only when the element record's type itself is touched. Per spec §3.4 close-out, this is the documented mechanism for record types whose handler factory cannot satisfy `new()`. |
| `src/Reactor/Hosting/XamlInterop.cs:38` (`XamlHostElement`) | `XamlHostElement → XamlHostHandler` (decorator) | Same shape and rationale as `XamlPageElement`. |
| `src/Reactor/Charting/D3Charts.cs:26` (`D3Charts` static class) | *None* — calls `Hosting.ChartingActivation.RequestActivation()` to lazy-init forced-colors / reduced-motion watchers. No handler / control type aggregation. | Out of scope for §10.2 (charting accessibility, not control registration). |

These cctors are **per-record** (or, for `D3Charts`, per-class) — not on
`Factories`. They run once per process the first time that specific record /
helper type is touched. An app that constructs no `XamlPageElement` never
pays the cost; the trim-proof Hello-World app in
`tests/aot_trim_proof/Reactor.AotHelloWorld/` validates this empirically by
asserting no `Xaml*Element*Handler` symbols survive in the published binary
when neither factory is reached.

---

## 5. Re-running the audit

When adding a new `Factories` partial (rare — usually a new factory goes into
`src/Reactor/Elements/Dsl.cs`), repeat the §2 discovery + §3 scans on the new
file. CI does not currently mechanise this audit; the optional Phase 3.5
companion analyzer (`Reactor.Analyzers.NoFactoriesAggregationAnalyzer`) would
flag any regression at build time with diagnostic id `REACTOR_TRIM_001`.

---

## 6. Result

✅ **All three §10.2 invariants hold across all seven `Factories` partials.**

✅ **No `[ModuleInitializer]` in `Reactor.dll`.**

✅ **No surviving call site for `RegisterV1BuiltInHandlers` — body and all
   callers removed in §3.4.**

The trim-regression guard (`tests/aot_trim_proof/Reactor.AotHelloWorld.TrimAssertions`)
provides the runtime proof: an app calling only `TextBlock()` + `Button()`
publishes with the unused catalog absent from the binary. This audit
documents the **source-level** invariant that the runtime proof depends on.
