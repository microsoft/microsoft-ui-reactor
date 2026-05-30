# Improvements to Hot Reload — Design Proposal

## Status

**Proposed — design converged, not yet implemented.** Builds on the existing
`Microsoft.UI.Reactor.Hosting.HotReloadService` and `RenderContext.ResetForHotReload`,
which already deliver the baseline edit-Render-body experience.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 What works today](#2-what-works-today)
- [§3 What breaks today](#3-what-breaks-today)
- [§4 Out of scope](#4-out-of-scope)
- [§5 Phase 1 — Tree-wide hook-order recovery](#5-phase-1--tree-wide-hook-order-recovery)
- [§6 Phase 2 — State migration across record/class shape changes](#6-phase-2--state-migration-across-recordclass-shape-changes)
- [§7 Phase 3 — Subtree migration on component type identity change](#7-phase-3--subtree-migration-on-component-type-identity-change)
- [§8 AOT, trimming, and Release-build behavior](#8-aot-trimming-and-release-build-behavior)
- [§9 Testing](#9-testing)
- [§10 Documentation](#10-documentation)
- [§11 Open questions](#11-open-questions)

---

## §1 Motivation

The bigger an app gets, the more painful it becomes to lose state on every code
edit. Today a developer who is six clicks deep into a settings flow, fixing a
bug in the leaf component, will typically lose the navigation stack and the
settings state on every save. The improvements here close that gap for the
three most common edit shapes:

1. **Adding or removing a hook in a deeply nested component.**
2. **Adding a field to a state record (`record AppState(…)`).**
3. **Reshaping a component class** (adding a field, changing a constructor)
   in a way that makes .NET Hot Reload mint a new `Type` for it.

For all three, the goal is *visual continuity*: the running app keeps its
state and its visual tree across the edit; only the bits the developer
actually changed re-render.

---

## §2 What works today

Reactor already implements the `[MetadataUpdateHandler]` contract:

- `HotReloadService.UpdateApplication` sets a one-shot atomic flag and calls
  `ReactorApp.ActiveHostInternal?.RequestRender(force: true)`.
- The host's `Render` reads the flag once per pass via
  `ConsumeUpdatePending()` (`Interlocked.Exchange`), so concurrent updates
  can't be silently dropped.
- A `HookOrderException` raised by the **root** `Component.Render()` during a
  HR-flagged pass is caught and triggers `RenderContext.ResetForHotReload()`
  (runs cleanups, clears `_hooks`, resets `_hookIndex`). A subsequent throw
  on the same pass falls through to `ShowErrorFallback` — at-most-once
  recovery, so genuinely broken edits surface clearly.
- The reconciler's force-rerender bypass (`_forceFullRenderActive`) makes
  every component re-run `Render()` even when memo/props/deps gates would
  otherwise skip it, so the new method body is observed.

This is the right baseline. It correctly handles "edit the body of
`Render()`" for any component, and "add/remove a hook in the **root**
component."

---

## §3 What breaks today

| Scenario | Behavior | Root cause |
|---|---|---|
| Add/remove a hook in a **non-root** component | Subtree unmounts (`HookOrderException` escapes the child render), all descendant state lost | Per-pass recovery wraps only the root render path; child `RenderContext`s have no equivalent guard |
| Add a field to a record used in `UseState<MyRecord>` | New code reads the field as default; the old instance is retained | No state migration — `_hooks[i].Value` is the old object verbatim |
| Add a field on a `Component` subclass | HR mints a new `Type`; `Reconciler.CanUpdate` compares `ComponentType` by reference, returns false → full unmount/mount | No HR-aware fast path on the `CanUpdate == false` branch |
| Animations and Composition Visual state | Restart on any of the above because the underlying `UIElement` is destroyed | Side-effect of the unmount/remount, not the animation system itself |

The last row is important to call out as **not a feature target**: the
animation system is a thin wrapper over `Microsoft.UI.Composition`
(`AnimationHelper.SetOrAnimate` calls `visual.StartAnimation`); all running
animation state lives in the native compositor, not in managed memory.
There is nothing for managed code to migrate. The improvements below
*indirectly* reduce animation jank by reducing how often the framework
remounts native controls, but they do not attempt to migrate animation
state directly. Some visible jank on reload is acceptable; inconsistent
animation behavior is not.

---

## §4 Out of scope

- **Out-of-VS / out-of-`dotnet watch` push channels** (e.g., a TCP server
  listening for assembly deltas from a custom CLI). The MS hot-reload
  pipeline already covers all the cases users actually hit; building a
  parallel transport adds maintenance burden without proportional value.
- **Capability advertisement** beyond what the runtime provides by default.
  The runtime decides which edits it accepts; Reactor consumes whatever
  arrives.
- **Migrating compositor animations, storyboards, or any
  `Microsoft.UI.Composition` object state.** See §3.
- **`UseEffect` cleanup-and-rerun behavior.** Effect deps that didn't change
  do not re-run; effects whose deps did change run as normal. Hot reload
  does not introduce a new semantic for effects.

---

## §5 Phase 1 — Tree-wide hook-order recovery

### Goal

Adding or removing a hook in any component recovers in a single re-render
pass, without unmounting that component's subtree.

### Design

1. Add a dispatcher-pass-scoped flag to `HotReloadService`:

   ```csharp
   internal static class HotReloadService
   {
       [ThreadStatic] private static bool _withinUpdatePass;
       internal static bool WithinUpdatePass => _withinUpdatePass;

       internal static IDisposable BeginUpdatePass()
       {
           _withinUpdatePass = true;
           return new UpdatePassScope();
       }

       private sealed class UpdatePassScope : IDisposable
       {
           public void Dispose() => _withinUpdatePass = false;
       }
   }
   ```

2. In `ReactorHostControl.Render()` and the matching block in
   `ReactorHost.cs` (around line 552 / line 567), wrap the existing
   render body in `using var _ = hotReloadRender ? HotReloadService.BeginUpdatePass() : null;`.
   The existing root-level `ConsumeUpdatePending` and `try/catch` stay as
   they are. `WithinUpdatePass` is the **wider** signal (the whole tree
   re-render) and is correct to read from anywhere in the reconciler
   during this pass.

3. In `Reconciler.UpdateComponent` (`Reconciler.cs` near line 1700), wrap
   the child `Render()` call:

   ```csharp
   try
   {
       newChildElement = node.Component.Render();
   }
   catch (HookOrderException) when (HotReloadService.WithinUpdatePass)
   {
       node.Component.Context.ResetForHotReload();
       newChildElement = node.Component.Render(); // one retry per pass
   }
   ```

   Apply the same pattern to the `FuncElement` and `MemoElement` branches
   that follow. A second throw on the retry escapes naturally and ends up
   in the existing fallback path.

### Acceptance

- Editing `Render()` in a nested component to add a `UseState` call and
  saving produces one re-render in which the new state is observable; the
  rest of the tree (including the root's hook state) is untouched.
- The exception path is exercised exactly once per `UpdateApplication`
  call per component — no infinite recovery loops.
- The flag clears at the end of the pass regardless of exception path
  (the `using` ensures it).

### Cost

≈ 25 lines of changes in `HotReloadService.cs`, `ReactorHostControl.cs`,
`ReactorHost.cs`, `Reconciler.cs`. No public API changes.

---

## §6 Phase 2 — State migration across record/class shape changes

### Goal

Adding a field to a record or class used in `UseState`, `UseReducer`,
`UseRef`, `UseMemo`, or as `Component<TProps>.Props` keeps the existing
values for the fields the developer didn't touch; the new fields read as
their default.

### Design

1. Extend `HotReloadService` to capture the `updatedTypes` array from each
   `UpdateApplication` invocation into a `HashSet<Type>` consumable for the
   duration of `WithinUpdatePass`:

   ```csharp
   internal static IReadOnlySet<Type>? UpdatedTypes { get; private set; }

   public static void UpdateApplication(Type[]? updatedTypes)
   {
       UpdatedTypes = updatedTypes is null ? null : new HashSet<Type>(updatedTypes);
       Volatile.Write(ref _updatePending, 1);
       ReactorApp.ActiveHostInternal?.RequestRender(force: true);
   }
   ```

2. Add a new helper class `ReactorHotReloadCopier`:

   ```csharp
   internal static class ReactorHotReloadCopier
   {
       // Walks all public + non-public instance fields. If a field type
       // matches by FullName but is a different Type instance (the HR-minted
       // new shape), recurses. If a field is missing on source, leave default.
       // If types are incompatibly different, drop with a debug log.
       public static bool TryMigrate(object source, object dest, HashSet<object> visited);
   }
   ```

   Implementation notes:
   - `BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance`.
   - Field-by-field, by name. Skip fields with no matching source field.
   - Cycle guard via the `visited` set keyed on source reference
     (`ReferenceEqualityComparer`).
   - For records with `init`-only fields, copy via the backing field — the
     runtime keeps the `<…>k__BackingField` name stable, so direct
     `FieldInfo.SetValue` works without invoking the synthesized clone.
   - Skip read-only static fields, `Compositor`/`Visual`/`UIElement`/any
     `IntPtr` typed fields (heuristic block-list for unmanaged-handle
     types, documented in code).

3. Add `RenderContext.MigrateHooksForHotReload(IReadOnlySet<Type>? updatedTypes)`
   that walks `_hooks` and for each cell whose stored generic argument
   `T` matches an updated type by `FullName`:
   - Construct a new `T` via `Activator.CreateInstance` of the
     *current* type loaded from `updatedTypes` (parameterless ctor or
     record primary-ctor; if neither, skip and log).
   - `ReactorHotReloadCopier.TryMigrate(oldValue, newInstance, …)`.
   - Write the new instance into the cell, **without** running cleanups
     and **without** resetting `_hookIndex` — unlike `ResetForHotReload`,
     this is a value swap inside existing cells.

4. The host calls `MigrateHooksForHotReload` on every reconciler node's
   `Component.Context` (and the `FuncElement` context) at the **start**
   of a HR-flagged pass, before any `Render()` runs. Walk the tree
   exactly once per pass via the existing root traversal in
   `Reconciler` (a new internal `Reconciler.ForEachLiveContext(Action<RenderContext>)`).

### Acceptance

- Add a field `int Count` to `record AppState(string Name)` already in
  use via `UseState<AppState>`. After save: `Name` retains its value;
  `Count` reads as 0; the new render sees the new shape.
- Remove a field: the old value is silently dropped (no crash); the new
  cell has whatever the new record's defaults provide.
- Replace a field's type with an incompatible type: the value is dropped
  (logged at `Debug` level), the new cell holds the default. The
  developer sees the obvious blank-slate behavior for that field, not a
  crash.
- The migration path is bypassed entirely when `UpdatedTypes` is `null`
  or when not `WithinUpdatePass`.

### Cost

≈ 150 lines:

- `HotReloadService.cs` (+30)
- `ReactorHotReloadCopier.cs` (new, ≈80)
- `RenderContext.cs` (+30, new `MigrateHooksForHotReload`)
- `Reconciler.cs` (+10, `ForEachLiveContext` enumerator)

No public API changes.

---

## §7 Phase 3 — Subtree migration on component type identity change

### Goal

When `[Component] class MyPage : Component` is edited in a way that mints
a new `Type` (adding/removing instance fields or changing the ctor),
preserve the reconciler subtree instead of tearing it down.

### Design

1. In `Reconciler.CanUpdate` (`Reconciler.cs:2410`), the existing
   `ComponentType` reference check is correct and stays. Do **not** widen
   `CanUpdate` itself — its contract is consumed by callers that don't
   know about HR.

2. Add a sibling fast-path in the **caller** sites where `CanUpdate ==
   false` decides to unmount → mount (these are in `UpdateChildren` and
   `ReconcileV1Child`):

   ```csharp
   if (!CanUpdate(oldEl, newEl))
   {
       if (HotReloadService.WithinUpdatePass
           && TryHotReloadMigrate(oldEl, newEl, existingNode, requestRerender))
       {
           // existingNode was reshaped in place; continue with newEl as current.
           continue;
       }
       Unmount(existing);
       Mount(newEl, …);
       return;
   }
   ```

3. `Reconciler.TryHotReloadMigrate(oldEl, newEl, node, requestRerender)`:
   - Returns `false` unless both are `ComponentElement` with the same
     `FullName` on `ComponentType` (most common case) **or** both are
     records with the same `FullName` (covers user-authored Element
     subtypes redefined under HR).
   - For `ComponentElement`: instantiate the new component type, run
     `ReactorHotReloadCopier.TryMigrate(oldComponent, newComponent, …)`,
     transfer the existing `RenderContext` reference onto the new
     instance (the hook list and cleanups stay alive), swap
     `node.Component`, and re-run the Phase 1 wrapped `Render()`. The
     existing `UIElement` is kept; the descriptor's normal `Update`
     pathway reconciles the new element tree against it.
   - For Element-record migration: write the new record into
     `node.Element`, then re-dispatch to the descriptor's `Update` —
     same path a normal prop change takes.

4. The "do not migrate" cases — different `FullName`, ctor signature
   incompatibility, descriptor returns `false` from a (new) optional
   `IElementHandler.CanHotReloadMigrate(old, new)` veto — fall through
   to unmount/mount as before.

### Acceptance

- Add a private field `_isExpanded` to a `Component` subclass used as a
  page in `Shell`. After save: the page keeps its `UseState` values
  *and* the new field is observable in the next render with its
  default.
- Replace the page's base class entirely: falls through to unmount/mount
  (no migration); behavior matches today's HR.
- Compositor animations on `Visual`s under the migrated subtree continue
  uninterrupted as an emergent consequence of the `UIElement` not being
  recreated — `StartAnimation` retargets from the current rendered
  value when the next animated property write fires. This is a benefit,
  not a designed feature: no animation-state migration code is added.

### Cost

≈ 200 lines:

- `Reconciler.cs` (+150) for `TryHotReloadMigrate` and the caller fast
  paths in `UpdateChildren`, `ReconcileV1Child`.
- Optional 1-line virtual on `IElementHandler` for the veto (default
  `true`).

No breaking API changes; the veto is additive on an internal-by-default
interface.

---

## §8 AOT, trimming, and Release-build behavior

All three phases are **gated on HR being live**:

```csharp
internal static bool IsHotReloadLive =>
    System.Reflection.Metadata.MetadataUpdater.IsSupported
    && _withinUpdatePass; // or _updatePending for the broader check
```

In NativeAOT builds (`PublishAot=true`):

- `MetadataUpdater.IsSupported` returns `false`; `UpdateApplication` is
  never invoked by the runtime. The whole subsystem is dead code from
  the AOT compiler's perspective and gets trimmed.
- `ReactorHotReloadCopier` uses `System.Reflection` (`FieldInfo`,
  `Activator.CreateInstance`). It is annotated
  `[RequiresUnreferencedCode]` and is reachable only through
  `IsHotReloadLive`-gated branches, so trimming has nothing to root.
- The core Reactor library's existing
  `IsAotCompatible=true` / warnings-as-errors policy is preserved by
  attributing the copier and the two new public-ish surfaces
  (`MigrateHooksForHotReload`, `TryHotReloadMigrate`) with the standard
  `[UnconditionalSuppressMessage("Trimming", "IL2026|IL2075", …)]`
  pattern already used in `RenderContext.SnapshotHooks`.

The result: AOT users lose Phase 2 and Phase 3 (they still get Phase 1,
which uses no reflection), and that's correct — AOT users don't run
hot reload anyway.

---

## §9 Testing

A new `tests/Reactor.Tests/HotReload/` directory with deterministic xunit
tests that drive `HotReloadService.UpdateApplication(typesArray)` manually
and assert outcomes. **No real metadata deltas needed** — the entry
points are reachable from test code, and the recovery behavior is what
we want to pin.

| Test | Tier | Asserts |
|---|---|---|
| `Phase1_AddHookInChildComponent_RecoversInOnePass` | xunit | Child re-renders, root state intact, hook recovery runs once |
| `Phase1_RepeatedHookOrderThrow_FallsThroughToFallback` | xunit | At-most-once retry; second throw escalates |
| `Phase1_WithinUpdatePassClearsAfterRender` | xunit | Flag clears even on exception |
| `Phase2_AddFieldToStateRecord_PreservesExistingFields` | xunit | New cell has migrated fields + default new field |
| `Phase2_IncompatibleFieldTypeChange_DropsAndLogs` | xunit | No crash; log entry at Debug |
| `Phase2_CycleInState_TerminatesViaVisitedSet` | xunit | No StackOverflow on self-referential state |
| `Phase3_AddPrivateFieldToComponent_PreservesSubtreeState` | xunit | `UseState` values survive; new field observable |
| `Phase3_DifferentFullName_FallsThroughToUnmount` | xunit | Unmount/mount as today |
| `Phase3_DescriptorVetoesMigration_FallsThroughToUnmount` | xunit | Veto path is honored |
| `HotReload_NativeAotMode_AllPhasesAreNoOps` | xunit (skip-when-not-aot) | `MetadataUpdater.IsSupported == false` path |

One **selftest** fixture covers the end-to-end visual continuity case
(real WinUI window, simulated update, assert `FrameworkElement`
identity preserved across a forced HR pass).

---

## §10 Documentation

- New `docs/guide/hot-reload.md` covering: which edits are supported,
  what state survives, AOT note, escape hatch
  (`HotReloadService.ResetAllContexts()` for a forced "lose-everything"
  reload when migration goes wrong).
- Update `docs/guide/getting-started.md` to mention `dotnet watch` and
  the supported edit matrix.
- `CHANGELOG.md` entry under the next release.
- No spec edits to 047/048 needed; the descriptor veto hook is purely
  additive.

---

## §11 Open questions

1. **`UseEffect` cleanups during Phase 2.** `MigrateHooksForHotReload`
   deliberately does *not* run cleanups (it's a value swap, not a hook
   reset). But: if the migrated state value participated in the deps
   array of an effect, does that effect re-run? Current proposal: yes,
   because deps equality is by `Equals`, and the new record instance
   is a different reference. This matches normal `SetState` semantics
   and seems correct. Confirm with the rubber-duck pass.
2. **Components without parameterless ctors.** Phase 3's instantiation
   step assumes a parameterless ctor. If a component takes constructor
   args, the migrate path must skip (fall through to unmount/mount).
   Decide whether to widen by walking the parent's element factory and
   re-invoking it with the same args — probably over-scoped for v1.
3. **Devtools surface.** Should `reactor.state` (devtools snapshot)
   call out which cells were migrated vs. fresh during the last HR
   pass? Useful for debugging Phase 2 incompatibilities. Defer to a
   follow-up.

---

## Sequencing

Land Phase 1 first as an independent PR — small, risk-free, immediately
closes the most common complaint ("editing my leaf component blows away
the world"). Phase 2 and Phase 3 land together because Phase 3's
migrate path consumes the copier introduced in Phase 2. Docs land
alongside Phase 1.
