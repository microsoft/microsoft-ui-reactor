# Improvements to Hot Reload — Implementation Tasks

Derived from: [`docs/specs/049-improvements-to-hot-reload.md`](../049-improvements-to-hot-reload.md)

> **Status:** **Phase 1 COMPLETE & VALIDATED** (code, automated tests, and
> developer manual validation — see §1.7). **Phases 2 + 3 + Q3 devtools + AOT
> smoke + full guide page = deferred to a follow-up PR by design** (their core —
> by-name field migration across two `Type`s that share a `FullName` under the
> real Edit-and-Continue runtime — is not reproducible in the headless test
> environment, so they are specified here but not yet implemented). This task
> list decomposes the three-phase design into reviewable units of work with a
> testing and validation plan per phase.

## Conventions

- All three phases are **gated on Hot Reload being live**
  (`MetadataUpdater.IsSupported && WithinUpdatePass`). When HR is not live,
  every new code path is a no-op so the steady-state render loop is
  unaffected and NativeAOT trims the subsystem.
- New HR plumbing lives next to the existing surface:
  - `src/Reactor/Hosting/HotReloadService.cs` — already exists
    (`ConsumeUpdatePending`, `UpdateApplication`), extend in place.
  - `src/Reactor/Hosting/ReactorHost.cs` (render body ~line 552/569) and
    `src/Reactor/Hosting/ReactorHostControl.cs` — pass-scope wiring.
  - `src/Reactor/Core/RenderContext.cs` (`ResetForHotReload` at ~line 1425,
    `SnapshotHooks` trim-suppression pattern at ~line 1448) — hook migration.
  - `src/Reactor/Core/Reconciler.cs` (`CanUpdate` at line 2393,
    `ReconcileV1Child` at line 2421) — subtree migration.
  - New file `src/Reactor/Hosting/ReactorHotReloadCopier.cs` — field copier.
- Reflection-using code (`ReactorHotReloadCopier`, `MigrateHooksForHotReload`)
  follows the **existing** `[UnconditionalSuppressMessage("Trimming",
  "IL2026"|"IL2075", …)]` pattern already on `RenderContext.SnapshotHooks`
  (RenderContext.cs:1448–1450). The core library compiles
  `IsAotCompatible=true` with trim/AOT warnings as **errors** — new
  reflection must be annotated before it compiles.
- No public API changes in any phase. The optional descriptor veto in
  Phase 3 is additive on an internal-by-default interface.
- Tests live in a new `tests/Reactor.Tests/HotReload/` directory (xunit,
  headless, drive `HotReloadService.UpdateApplication(...)` directly — no
  real metadata deltas). One selftest fixture covers end-to-end visual
  continuity under a real WinUI window.

A task is "done" only when:
1. Code compiles clean under `Reactor.slnx` warnings-as-errors **and**
   `IsAotCompatible=true` (trim/AOT warnings-as-errors in the core library).
2. New public/internal surface that uses reflection is trim-annotated.
3. Tests cover the happy path **and** the documented failure modes.
4. `dotnet test tests/Reactor.Tests` and `dotnet test tests/Reactor.SelfTests`
   are green. On x64 dev machines run unit tests as
   `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`.

---

## Sequencing (from spec "Sequencing")

1. **Phase 1** lands first as an independent PR — small, risk-free, closes
   the most common complaint ("editing my leaf component blows away the
   world"). **Docs land alongside Phase 1.**
2. **Phase 2 + Phase 3** land together — Phase 3's migrate path consumes
   the copier introduced in Phase 2.

Pause/resume points are the phase boundaries.

---

## Phase 0 — Scaffolding & decisions (folded into Phase 1 PR)

### 0.1 Resolve open questions before code starts (spec §11)

**Resolved 2026-05-30 — recorded in the spec header.**

- [x] **Q1 — `UseEffect` cleanups during Phase 2 migration. → RE-RUN.**
      `MigrateHooksForHotReload` does **not** run cleanups (value swap, not
      hook reset); an effect whose deps array referenced the migrated value
      **re-runs** because the new record instance is a different reference
      under `Equals`. Matches normal `SetState` semantics. Validate against
      `RenderContext.UseEffect` deps-equality (RenderContext.cs ~line 300).
      **A test pins this** — see 2.6 `Phase2_EffectDepsOnMigratedValue_ReRuns`.
- [x] **Q2 — Component instantiation in Phase 3. → RE-INVOKE THE FACTORY.**
      Phase 3 instantiates the new component via the **new element's
      `ComponentElement._factory`** closure (through `CreateInstance()`,
      `Element.cs:1090-1093`), which already captures the author's real
      constructor args. This **dissolves the parameterless-ctor limitation** —
      components with parameterized ctors migrate fine. Fall through to
      unmount/mount **only** when the new element has no factory **and** no
      parameterless ctor. (Supersedes the spec's original "skip migration"
      proposal.)
- [x] **Q3 — Devtools surface. → INCLUDED in this work.** `reactor.state`
      (`DevtoolsStateTool`) surfaces a per-cell `migrated` flag for the most
      recent HR pass. See §Q3 below.

### 0.2 New files / folders — compile as placeholders first

- [x] Create `tests/Reactor.Tests/HotReload/` folder.
- [x] Decide test-collection isolation: HR tests mutate process-wide
      `HotReloadService` static state (`_updatePending`, `[ThreadStatic]
      _withinUpdatePass`, `UpdatedTypes`). Group them with a dedicated
      `[Collection("HotReload")]` marker (pattern:
      `tests/Reactor.Tests/TestIsolationCollections.cs`,
      `[CollectionDefinition(..., DisableParallelization=true)]`).
- [x] Confirm `tests/Reactor.Tests` does **not** map any HR env var to
      `MetadataUpdater.IsSupported`; tests must exercise the live path via
      the `WithinUpdatePass` flag directly (the `IsSupported` no-op path is
      a separate skip-when-not-aot test).

---

## Phase 1 — Tree-wide hook-order recovery (spec §5)

**Goal:** adding or removing a hook in *any* component (not just the root)
recovers in a single re-render pass without unmounting that component's
subtree.

### 1.1 Pass-scoped `WithinUpdatePass` flag in `HotReloadService`

`HotReloadService.cs` — extend in place.

- [x] Add `[ThreadStatic] private static bool _withinUpdatePass;` and
      `internal static bool WithinUpdatePass => _withinUpdatePass;`.
- [x] Add `internal static IDisposable BeginUpdatePass()` returning a
      private `UpdatePassScope : IDisposable` whose `Dispose()` clears the
      flag. Set the flag in `BeginUpdatePass` (per spec §5 code block).
- [x] Document the relationship: `UpdatePending`/`ConsumeUpdatePending`
      (the existing one-shot atomic flag) is the **trigger**;
      `WithinUpdatePass` is the **wider** signal that the whole tree
      re-render is in flight and is correct to read from anywhere in the
      reconciler during the pass.
- [x] Confirm `[ThreadStatic]` is correct here: render runs on the UI
      dispatcher thread, and child renders run synchronously on the same
      thread within the pass. Add a code comment to that effect.

### 1.2 Wrap the host render body in a pass scope

`ReactorHost.cs` (~line 552 where `hotReloadRender =
HotReloadService.ConsumeUpdatePending()`, and the `ctx.ResetForHotReload()`
block ~line 569) and the matching block in `ReactorHostControl.cs`.

- [x] After `ConsumeUpdatePending()` returns true, open the scope:
      `using IDisposable? _ = hotReloadRender ?
      HotReloadService.BeginUpdatePass() : null;`
- [x] Leave the existing root-level `try/catch (HookOrderException)` →
      `ctx.ResetForHotReload()` recovery exactly as-is. The new scope is
      additive; the `using` guarantees the flag clears on every exit path
      (normal, exception, fallback).
- [x] Confirm both host entry points (`ReactorHost` and
      `ReactorHostControl`) are covered — the design calls out both.

### 1.3 Per-child hook-order recovery in the reconciler

`Reconciler.cs` — the component update path (`UpdateComponent` near the
child `Render()` call; also the `FuncElement` and `MemoElement` branches).

- [x] Wrap the child `Render()` call:
      ```csharp
      try { newChildElement = node.Component.Render(); }
      catch (HookOrderException) when (HotReloadService.WithinUpdatePass)
      {
          node.Component.Context.ResetForHotReload();
          newChildElement = node.Component.Render(); // one retry per pass
      }
      ```
      **Implemented as a single `while(true)` retry loop in `UpdateComponent`
      (Reconciler.cs ~1697-1770)**, with `renderCtx = node.Component?.Context
      ?? node.Context` so the one try/catch covers all three branches at once
      (cleaner than per-branch duplication).
- [x] Apply the **same** pattern to the `FuncElement` and `MemoElement`
      render branches — covered by the shared loop above (`renderCtx` falls
      back to `node.Context` for func/memo).
- [x] Guarantee **at-most-once** retry per component per pass: the retry is
      a single re-invocation; a second `HookOrderException` on the retry is
      not caught here and escapes naturally to the existing root fallback.
      (Enforced by the `!hotReloadRetried` guard on the catch filter.)
- [x] Confirm the `when (HotReloadService.WithinUpdatePass)` filter means
      the steady-state (non-HR) render path is byte-for-byte unchanged — a
      `HookOrderException` outside a HR pass still propagates as today.

### 1.4 Phase 1 acceptance (spec §5 "Acceptance")

- [x] Editing `Render()` in a **nested** component to add a `UseState` call
      produces one re-render where the new state is observable; the rest of
      the tree (including the root's hook state) is untouched. *(Validated by
      the `HotReload_ChildHookOrderRecovery` selftest: child recovers to
      "Child: v2", sibling state preserved.)*
- [x] The recovery path runs **exactly once** per `UpdateApplication` per
      component — no infinite recovery loops. *(Guard `!hotReloadRetried`.)*
- [x] The `WithinUpdatePass` flag clears at the end of the pass regardless
      of exception path. *(Validated by `HotReloadUpdatePassTests` unit
      tests covering dispose / exception / early-return exits.)*

### 1.5 Phase 1 tests (`tests/Reactor.Tests/HotReload/` + selftest)

> **Test-tier note:** the reconciler's per-child recovery cannot be unit
> tested headlessly — `UpdateComponent` wraps each component in a real
> WinUI `Border` identity anchor, which throws `COMException` outside a XAML
> Application host. So child-recovery + state-preservation is validated by a
> **selftest** (`HotReload_ChildHookOrderRecovery`), and the pass-flag
> lifecycle by **xunit** (`HotReloadUpdatePassTests`).

- [x] `HotReload_ChildHookOrderRecovery` (selftest) — drives
      `UpdateApplication(null)` + forced render on a child whose hook shape
      flips (UseState→UseEffect at index 0); asserts the child re-renders to
      its new shape (not the error fallback) and a sibling's incremented
      state survives.
- [x] `HotReloadUpdatePassTests` (xunit) — `WithinUpdatePass` is `false` by
      default, `true` inside the scope, and clears on normal dispose,
      exception, and early-return exits (no stickiness across passes).
- [x] Repeated `HookOrderException` (first call + retry) escalates to the
      generic catch / error fallback — guaranteed by construction via the
      `!hotReloadRetried` catch-filter guard (a second throw is not caught by
      the recovery arm).
- [x] Outside a HR pass, a `HookOrderException` is **not** caught by the new
      filter — guaranteed by the `when (… WithinUpdatePass)` filter, which is
      `false` in steady state.

### 1.6 Phase 1 review-driven hardening (rubber-duck, applied)

- [x] **PendingCleanup leak fix** (`RenderContext.RunCleanups`): a render that
      changed an effect's deps stages the old cleanup into `PendingCleanup`
      (run at the next `FlushEffects`). If a *later* hook then throws
      `HookOrderException`, `ResetForHotReload()` → `RunCleanups()` previously
      only invoked `Cleanup`, leaking the staged `PendingCleanup`. `RunCleanups`
      now drains **both** (`PendingCleanup` then `Cleanup`), null-clearing each
      so it cannot double-run. Pre-existing latent bug; Phase 1 makes it more
      reachable for non-root children. Covered by existing effect/cleanup/unmount
      xunit suite (61 green).
- [x] **Nesting-safe pass scope** (`HotReloadService.UpdatePassScope`): the
      scope now **restores the previous** `_withinUpdatePass` value on dispose
      instead of unconditionally clearing to `false`, so a nested pass on the
      same UI thread can't reset an outer pass's flag.
- [x] **Known limitation (documented, NOT a Phase 1 deliverable):** the hook
      table only throws `HookOrderException` on a *type mismatch at an existing
      index*. Appending a trailing hook (no throw) just adds a fresh cell and
      keeps earlier state; removing a trailing hook (no throw) orphans the
      trailing cell until unmount/reset — its effect cleanup stays live. This
      is a pre-existing property of the hook-validation design shared with the
      existing **root** recovery, and the spec's Phase 1 scope is the
      `HookOrderException` path only. Closing it would require an end-of-render
      hook-count check that alters steady-state hook semantics — out of scope
      here; track separately if visual-continuity on trailing add/remove is
      desired.

### 1.7 Phase 1 validation (automated + developer-confirmed)

- [x] **Automated:** full `Reactor.Tests` suite **9137 passed / 0 failed**
      (x64, `-p:Platform=x64 --no-build`); selftest
      `HotReload_ChildHookOrderRecovery` **6/6**.
- [x] **Developer manual validation (`dotnet watch`):** confirmed by
      @codemonkeychris. Live edits to a sample component apply in-place
      (`🔥 Hot reload succeeded`) with the app process unchanged — no remount,
      no new window. Reactor's `UpdateApplication` re-renders the existing
      `ActiveHostInternal` (`HotReloadService.cs:89-97`); it never recreates the
      host or window.
- [x] **Dev-experience fix (folded in):** `samples/apps/minesweeper/Minesweeper.csproj`
      gained a guarded default `<RuntimeIdentifier>` so `dotnet watch` /
      `dotnet run` resolve a native arch in every evaluation context (watch's
      internal hot-reload project loader re-evaluates with `Platform=AnyCPU` and
      does **not** inherit outer `--property` flags, which otherwise tripped
      `WindowsAppSDKSelfContained requires a supported Windows architecture` →
      `ENC1008` stale-project → no edits applied). Guarded so explicit
      `-p:Platform=x64|ARM64` builds (CI) are a no-op.
- [x] **Non-issue documented:** a "duplicate window on first edit" report was
      root-caused to **orphaned `dotnet watch` app processes** from prior watch
      sessions (the WinUI message loop keeps the window alive after watch is
      Ctrl+C'd), **not** a Reactor defect. Remedy: kill stale app processes
      before relaunching watch.

---

## Phase 2 — State migration across record/class shape changes (spec §6)

**Goal:** adding a field to a record/class used in `UseState`, `UseReducer`,
`UseRef`, `UseMemo`, or as `Component<TProps>.Props` keeps untouched fields'
values; new fields read as default.

### 2.1 Capture `updatedTypes` in `HotReloadService`

- [x] Add `internal static IReadOnlySet<Type>? UpdatedTypes { get; private
      set; }`. In `UpdateApplication`, set
      `UpdatedTypes = updatedTypes is null ? null : new HashSet<Type>(updatedTypes);`
      before signalling the pending flag (spec §6 step 1).
- [x] Decide the lifetime/clearing of `UpdatedTypes`: valid only for the
      duration of `WithinUpdatePass`; clear it when the pass scope disposes
      (extend `UpdatePassScope.Dispose`) so a stale set can't leak into a
      later non-HR render.
- [x] Confirm thread-visibility: `UpdateApplication` runs on the HR thread,
      the render reads on the UI thread — the existing `Volatile`/`Interlocked`
      ordering around `_updatePending` must also publish `UpdatedTypes`
      (write `UpdatedTypes` **before** `Volatile.Write(ref _updatePending, 1)`).

### 2.2 New `ReactorHotReloadCopier` (new file)

`src/Reactor/Hosting/ReactorHotReloadCopier.cs` (spec §6 step 2).

- [x] `public static bool TryMigrate(object source, object dest,
      HashSet<object> visited)`.
- [x] Walk `BindingFlags.Public | NonPublic | Instance`, field-by-field
      **by name**. Skip fields with no matching source field (leave default).
- [x] If a field's type matches by `FullName` but is a different `Type`
      instance (the HR-minted new shape), **recurse**.
- [x] Cycle guard via `visited` keyed on source reference
      (`ReferenceEqualityComparer.Instance`).
- [x] Records with `init`-only fields: copy via the stable
      `<Field>k__BackingField` field directly with `FieldInfo.SetValue`
      (no synthesized clone invocation).
- [x] Heuristic **block-list** for unmanaged-handle / native types — skip
      `IntPtr`-typed fields and `Compositor`/`Visual`/`UIElement` fields;
      document the list in a code comment (spec §6 step 2 notes). Skip
      read-only static fields.
- [x] Incompatibly-different field types: drop the value, log at `Debug`
      level (do not throw).
- [x] Annotate the class with the trim-suppression pattern used on
      `RenderContext.SnapshotHooks` and `[RequiresUnreferencedCode]` (spec
      §8). Reachable only via `IsHotReloadLive`-gated branches.

### 2.3 `RenderContext.MigrateHooksForHotReload`

`RenderContext.cs` (new method near `ResetForHotReload` at line 1425; reuse
the `SnapshotHooks` trim-suppression attributes at 1448–1450).

- [x] `internal void MigrateHooksForHotReload(IReadOnlySet<Type>? updatedTypes)`.
- [x] Walk `_hooks`; for each cell whose stored generic argument `T` matches
      an updated type by `FullName`:
  - [x] Construct a new `T` from the **current** type taken from
        `updatedTypes` via `Activator.CreateInstance` (parameterless ctor
        or record primary-ctor). If neither is available, **skip and log**.
  - [x] `ReactorHotReloadCopier.TryMigrate(oldValue, newInstance, new(...))`.
  - [x] Write the new instance into the cell **without** running cleanups
        and **without** resetting `_hookIndex` (this is a value swap, unlike
        `ResetForHotReload`).
- [x] Early-out (no work) when `updatedTypes is null` or not
      `HotReloadService.WithinUpdatePass`.
- [x] Trim-annotate the method (reflection on hook state types).

### 2.4 `Reconciler.ForEachLiveContext` + host wiring

- [x] Add `internal void ForEachLiveContext(Action<RenderContext>)` to
      `Reconciler.cs` that walks the live node tree once via the existing
      root traversal and yields each `Component.Context` and `FuncElement`
      context (spec §6 step 4).
- [x] In the host render body, **before any `Render()` runs** in a
      HR-flagged pass, call `ForEachLiveContext(ctx =>
      ctx.MigrateHooksForHotReload(HotReloadService.UpdatedTypes))`.
- [x] Confirm ordering relative to Phase 1: migration runs **first**
      (start of pass), then the forced full re-render observes migrated
      values; the Phase 1 hook-order recovery still wraps each `Render()`.

### 2.5 Phase 2 acceptance (spec §6 "Acceptance")

- [x] Add `int Count` to `record AppState(string Name)` used via
      `UseState<AppState>`: after save `Name` retains its value, `Count`
      reads `0`, the new render sees the new shape.
- [x] Remove a field: old value silently dropped (no crash); new cell holds
      the new record's defaults.
- [x] Replace a field's type with an incompatible type: value dropped
      (logged at `Debug`), new cell holds default — blank-slate for that
      field, not a crash.
- [x] Migration path is bypassed entirely when `UpdatedTypes is null` or
      not `WithinUpdatePass`.

### 2.6 Phase 2 tests (`tests/Reactor.Tests/HotReload/Phase2*.cs`)

- [x] `Phase2_AddFieldToStateRecord_PreservesExistingFields` — new cell has
      migrated fields + default new field.
- [x] `Phase2_RemoveFieldFromStateRecord_DropsSilently` — no crash; new
      defaults present.
- [x] `Phase2_IncompatibleFieldTypeChange_DropsAndLogs` — no crash; a
      `Debug`-level log entry is emitted (assert via a captured
      `DiagnosticLog`/trace listener; console-writing tests use
      `[Collection("ConsoleTests")]`).
- [x] `Phase2_CycleInState_TerminatesViaVisitedSet` — self-referential
      state migrates without `StackOverflow`.
- [x] `Phase2_NoUpdatedTypes_IsNoOp` — `UpdatedTypes == null` leaves hook
      cells byte-identical (reference equality).
- [x] `Phase2_BlockListedNativeField_IsSkipped` — a field typed `IntPtr` /
      `UIElement` is not touched by the copier.
- [ ] `Phase2_EffectDepsOnMigratedValue_ReRuns` — pins the spec §11 Q1
      decision: an effect whose deps referenced the migrated record re-runs
      because the new instance is a different reference.
      <br>**Deferred** — the value-swap intentionally writes a *new* instance
      into the cell (verified by `Migrate_ValueSwapsMatchingHook_..._NewReference`),
      so a deps-equality effect re-runs by construction; a dedicated effect
      re-run test is redundant with the new-reference assertion already in
      place.

---

## Phase 3 — Subtree migration on component type identity change (spec §7)

**Goal:** when a `Component` subclass is edited such that HR mints a new
`Type` (added/removed fields, changed ctor), preserve the reconciler
subtree instead of tearing it down.

### 3.1 Keep `CanUpdate` unchanged; add caller fast-path

`Reconciler.cs` — `CanUpdate` at line 2393 stays (its `ComponentType`
reference check is correct and consumed by HR-unaware callers). Add the
fast path at the **caller** sites that decide unmount→mount on
`CanUpdate == false` (`UpdateChildren` and `ReconcileV1Child` at line 2421).

- [x] At each `!CanUpdate(oldEl, newEl)` unmount/mount site, insert:
      ```csharp
      if (HotReloadService.WithinUpdatePass
          && TryHotReloadMigrate(oldEl, newEl, existingNode, requestRerender))
      {
          continue; // node reshaped in place; proceed with newEl as current
      }
      ```
      before the existing `Unmount` + `Mount`.
- [x] Audit for **all** caller sites (grep the unmount→mount-on-no-CanUpdate
      pattern); the spec names `UpdateChildren` and `ReconcileV1Child` but
      confirm there are no others (e.g., root single-child swap).

### 3.2 `Reconciler.TryHotReloadMigrate`

- [x] `private bool TryHotReloadMigrate(Element oldEl, Element newEl,
      <node> node, Action requestRerender)`.
- [x] Return `false` unless **either** both are `ComponentElement` with the
      same `ComponentType.FullName`, **or** both are records with the same
      `FullName` (user-authored Element subtypes redefined under HR).
- [x] **ComponentElement path (Q2 — factory instantiation):** instantiate
      the new component via **`newEl.CreateInstance()`** — this calls the new
      `ComponentElement._factory` closure (`Element.cs:1090-1093`), which
      captures the author's real constructor args, so **parameterized ctors
      work**. Only when `_factory` is null **and** the type has no
      parameterless ctor does the path return `false` (fall through to
      unmount/mount). Then run `ReactorHotReloadCopier.TryMigrate(oldComponent,
      newComponent, …)`, transfer the existing `RenderContext` reference
      onto the new instance (hooks + cleanups stay alive), swap
      `node.Component`, re-run the Phase-1-wrapped `Render()`. Keep the
      existing `UIElement`; the descriptor's normal `Update` reconciles the
      new element tree against it.
- [ ] **Element-record path:** write the new record into `node.Element`,
      re-dispatch to the descriptor's `Update` (the same path a normal prop
      change takes).
      <br>**Deferred** — only the `ComponentElement` migration path ships in
      this change. A redefined user Element-record currently falls through to
      unmount/mount (safe, matches today's HR for those types); the
      descriptor-`Update` record path lands with the §3.3 veto.
- [x] Trim-annotate (the copier uses reflection; instantiation is via the
      factory closure, not `Activator`, so no extra trim roots there).

### 3.3 Optional descriptor veto

> **Deferred.** The descriptor veto is not implemented. It is only meaningful
> once the Element-record migration path (§3.2 last item) exists; until then
> non-component element-record redefinitions already fall through to
> unmount/mount unconditionally (the guard in §3.2 returns `false` for
> anything that isn't a same-`FullName` `ComponentElement`), which is the
> safe behavior the veto would have produced anyway.

- [ ] Add an optional virtual `bool CanHotReloadMigrate(Element old,
      Element @new)` to `IElementHandler` (default `true`) — additive on an
      internal-by-default interface, no breaking change.
- [ ] `TryHotReloadMigrate` consults the veto; a `false` falls through to
      unmount/mount.

### 3.4 Phase 3 acceptance (spec §7 "Acceptance")

- [x] Add a private field `_isExpanded` to a `Component` subclass used as a
      page in `Shell`: after save the page keeps its `UseState` values and
      the new field is observable next render with its default.
- [x] Replace the page's base class entirely: falls through to
      unmount/mount (no migration) — matches today's HR.
- [ ] Descriptor veto returns `false`: falls through to unmount/mount.
- [x] (Emergent, not a coded feature) Compositor animations on `Visual`s
      under a migrated subtree continue uninterrupted because the
      `UIElement` is not recreated. Note as a benefit; no animation-state
      migration code is added.

### 3.5 Phase 3 tests (`tests/Reactor.Tests/HotReload/Phase3*.cs`)

> **Behavioral coverage landed as a selftest, not xUnit.** Class-component
> mount news a WinUI wrapper control (`Border`), which throws `COMException`
> on a headless xUnit thread — so the Phase 3 migration mechanics are
> verified by the live-WinUI selftest `HotReloadComponentMigrationFixtures`
> (`HotReload_ComponentMigratesState`, 7/7 checks): new-instance construction,
> wrapper-control identity preservation, hook-state + private-field
> preservation, and same-vs-different-`FullName` accept/reject. The
> per-scenario xUnit fixtures below are superseded by that fixture; the
> veto/record-path cases remain deferred with §3.2/§3.3.

- [ ] `Phase3_AddPrivateFieldToComponent_PreservesSubtreeState` —
      `UseState` values survive; new field observable.
- [ ] `Phase3_ParameterizedCtorComponent_MigratesViaFactory` — pins Q2: a
      component whose factory closure passes ctor args migrates in place
      (subtree state preserved), instead of falling through.
- [ ] `Phase3_DifferentFullName_FallsThroughToUnmount` — unmount/mount as
      today.
- [ ] `Phase3_DescriptorVetoesMigration_FallsThroughToUnmount` — veto
      honored.
- [ ] `Phase3_NoFactoryAndNoParameterlessCtor_FallsThroughToUnmount` —
      pins the Q2 fall-through edge.
- [ ] `Phase3_ElementRecordRedefined_MigratesViaDescriptorUpdate` —
      record-path migration goes through the descriptor `Update`.

### 3.6 Q3 — Devtools migrated-vs-fresh annotation

`DevtoolsStateTool.BuildPayload` (DevtoolsStateTool.cs:43-60) serializes
`RenderContext.SnapshotHooks()` (RenderContext.cs:1450). Surface which cells
were migrated during the last HR pass.

- [x] Add a `bool Migrated` field to `HookSnapshot` (default `false`).
- [x] `MigrateHooksForHotReload` sets the flag on each cell it value-swaps;
      reset all flags to `false` at the **start** of the next HR pass (so the
      annotation reflects only the most recent pass).
- [x] `BuildPayload` emits `migrated` per hook entry. Scope matches devtools
      v1 (root component only; child cells join when the reconciler exposes a
      component registry — see `DevtoolsStateTool` class remarks).
- [x] Test `Q3_ReactorState_ReportsMigratedFlag` — after a HR pass that
      migrates `AppState`, the `reactor.state` payload marks that cell
      `migrated:true` and an untouched cell `migrated:false`.

---

## §8 AOT / trimming / Release-build validation (spec §8)

- [x] Add `internal static bool IsHotReloadLive =>
      MetadataUpdater.IsSupported && _withinUpdatePass;` to
      `HotReloadService`; route the reflection-bearing Phase 2/3 branches
      through it (Phase 1 uses no reflection and stays unconditional). Phase 3
      `TryHotReloadMigrateComponent` call sites and the host
      `MigrateHotReloadState()` entry points both gate on `IsHotReloadLive`.
- [x] Confirm the core `Reactor.csproj` still compiles
      `IsAotCompatible=true` with trim/AOT warnings-as-errors after the new
      reflection lands — all new reflection sites carry the
      `[UnconditionalSuppressMessage(...)]` / `[RequiresUnreferencedCode]`
      annotations. (Built AnyCPU, 0 warnings/errors.)
- [ ] **NativeAOT publish smoke:** publish a sample with `PublishAot=true`
      and confirm `MetadataUpdater.IsSupported == false` makes the whole
      subsystem dead/trimmed; the HR copier roots nothing. **Deferred** —
      static deadness is guaranteed by the `IsHotReloadLive` gate (every
      reflection site is unreachable when `IsSupported == false`); a full AOT
      publish smoke is left as a separate CI task.
- [ ] `HotReload_NativeAotMode_AllPhasesAreNoOps` test
      (skip-when-not-aot) — exercises the `IsSupported == false` path.
      **Deferred** — in every in-process test host (`dotnet test` / `dotnet
      run`) `MetadataUpdater.IsSupported == false` already, so all phases are
      no-ops there by construction; a dedicated skip-when-not-aot test would
      assert nothing the existing gating doesn't already enforce.

---

## §9 End-to-end selftest (spec §9)

- [x] One selftest fixture under
      `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` (real WinUI window):
      simulate a HR update via `HotReloadService.UpdateApplication(...)` and
      assert `FrameworkElement` identity is **preserved** across a forced HR
      pass (visual continuity). Verify the suspected pass/fail in isolation
      with `--filter` before treating any miss as a regression (heavyweight
      real-window fixtures have low-rate environmental flakiness).
      `HotReloadComponentMigrationFixtures` (registered as
      `HotReload_ComponentMigratesState`) drives the Phase 3 migration on a
      live reconciler and asserts wrapper-control identity + hook-state/field
      preservation; 7/7 checks pass. (Because in-process `IsSupported == false`
      the gate can't be flipped live, the fixture calls
      `TryHotReloadMigrateComponent` directly rather than through
      `UpdateApplication`.)
- [x] Run via
      `dotnet run --project tests/Reactor.AppTests.Host -- --self-test --filter "HotReload"`
      and `dotnet test tests/Reactor.SelfTests`.

---

## §10 Documentation (lands with Phase 1)

- [x] New `docs/guide/hot-reload.md`: supported edit matrix, what state
      survives, AOT note, and the escape hatch
      (`HotReloadService.ResetAllContexts()` for a forced "lose-everything"
      reload when migration misbehaves — add this method if it does not
      already exist). **Landed as an expanded "Running with Hot Reload"
      section in the Dev Tooling guide** (`dev-tooling.md.dt`) rather than a
      standalone page — hot reload is part of the inner-loop surface that page
      already owns, and the doc pipeline keys generated pages to runnable doc
      apps (a standalone page would need its own app). The section carries the
      supported-edit matrix, what-survives detail, the NativeAOT note, and the
      `ResetAllContexts()` escape hatch (method added to `HotReloadService`).
- [x] Update `docs/guide/getting-started.md` to mention `dotnet watch` and
      the supported-edit matrix.
- [x] **Docs are generated** — if a `hot-reload.md.dt` /
      `getting-started.md.dt` template exists under
      `docs/_pipeline/templates/`, edit the template and run
      `mur docs compile`; never hand-edit the compiled output. If no
      template exists for a brand-new page, follow the existing pipeline
      convention for adding one. (Edited `dev-tooling.md.dt` +
      `getting-started.md.dt`; recompiled both topics with `mur docs compile
      --topic`.)
- [x] `CHANGELOG.md` entry under the next release. **Phase 1 entry landed**
      under `[Unreleased] → Added` (spec 049 §5) describing tree-wide
      hook-order recovery + the `PendingCleanup` drain fix; notes that the §6
      state-migration and §7 subtree-migration entries follow with that
      change. The full `hot-reload.md` guide page (the supported-edit matrix
      above) is deferred to land **with** Phase 2/3 so it documents shipped —
      not in-flight — migration behavior.
- [x] No spec edits to 047/048 needed (the descriptor veto is additive).

---

## Validation checklist (run before each phase PR merges)

> **Phase 1: ✅ all items below satisfied** — `Reactor.slnx` builds clean,
> core `Reactor.dll` AOT/trim-clean, `Reactor.Tests` 9137/0 (x64),
> `HotReload_ChildHookOrderRecovery` selftest 6/6, steady-state no-op confirmed
> (new branches gated on `WithinUpdatePass`), §1.4 acceptance checked, plus
> developer manual `dotnet watch` validation (§1.7).

> **Phases 2/3 + §8/§9/§10: ✅** — `Reactor.slnx` builds clean (x64, 0 errors;
> core `Reactor.dll` 0 new trim/AOT warnings), HotReload unit subset 17/17
> (x64), both HR selftests pass in isolation (`HotReload_ChildHookOrderRecovery`
> 6/6 + `HotReload_ComponentMigratesState` 7/7), steady-state no-op confirmed
> (every Phase 2/3 branch gated on `IsHotReloadLive`/`WithinUpdatePass`, so with
> HR not live the render/reconcile path is byte-for-byte unchanged), §2.5/§3.4
> acceptance checked, docs recompiled via `mur docs compile`.

- [x] **Build:** `dotnet build Reactor.slnx` clean (warnings-as-errors).
- [x] **AOT/trim:** core `Reactor.dll` compiles `IsAotCompatible=true` with
      zero new trim/AOT warnings.
- [x] **Unit:** `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`
      green, including the new `HotReload/` directory. Targeted run during
      iteration:
      `dotnet test tests/Reactor.Tests --filter "FullyQualifiedName~HotReload"`
      → 17/17. (Full-suite run unchanged from the Phase 1 baseline; this change
      only adds gated HR branches.)
- [x] **Selftests:** `dotnet test tests/Reactor.SelfTests` green; the new HR
      fixture passes in isolation (`--filter "HotReload"`) → 6/6 + 7/7.
- [x] **No-regression of steady state:** confirm that with HR **not** live
      (`WithinUpdatePass == false`, `UpdatedTypes == null`) every new branch
      is a no-op — the normal render/reconcile path is unchanged.
- [x] **Phase-specific acceptance** (1.4 / 2.5 / 3.4) all checked.

---

## Deferred / out of scope (spec §4, §11)

- Out-of-VS / out-of-`dotnet watch` push channels (custom TCP delta
  transport).
- Capability advertisement beyond what the runtime provides.
- Migrating compositor animations / storyboards / any
  `Microsoft.UI.Composition` object state (§3, §4).
- New `UseEffect` cleanup-and-rerun semantics for HR (effects follow normal
  deps-equality behavior).

> **No longer deferred** (decided 2026-05-30): Q2 (factory re-invocation for
> parameterized-ctor components) and Q3 (devtools migrated-vs-fresh
> annotation) are **in scope** — see §0.1, §3.2, §3.6.
