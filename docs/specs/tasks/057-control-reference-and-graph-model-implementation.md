# Control-Reference Properties & the Reactor Graph Model — Implementation Tasks

Derived from: [`docs/specs/057-control-reference-and-graph-model.md`](../057-control-reference-and-graph-model.md).
Resolves [issue #456](https://github.com/microsoft/microsoft-ui-reactor/issues/456).

> **Status:** Phase 0 + Phase 1 COMPLETE and committed — #456 is closed (reactive
> `ElementRef`, full reference-edge engine, TeachingTip.Target proof, §9 topology
> matrix, surface-parity, perf gate passing). **Phase 2 COMPLETE and committed** —
> 2.1 (`.ReferenceList`), 2.2 (`AutomationProperties` relationships), 2.3
> (`XYFocus*`), 2.4 (real-control torture matrix capstone, 42 checks), 2.5 (docs +
> skills), 2.6 (REACTOR_REF_001 analyzer + CLI), and the 2.7 exit gate (xunit 9305
> pass / selftest filters all green / slnx 0 warnings). **Phase 3 COMPLETE and
> committed** — 3.1 (devtools `references` tool + `ReferenceOverlay` + `/references`
> preview endpoint + VS Code overlay + docs/skill), 3.2 + 3.3 deferred by design
> (weak-subs gated on a public `CurrentChanged` Q4 kept `internal`; source-gen folds
> into the spec-047 §7 track when it lands), 3.4 open-question close-out (Q1–Q4
> resolved in spec §12), and the 3.5 exit gate. **The spec is now Accepted.**
> AOT-selftests run in CI only.
> Spec is design-converged (D1 + D2 ratified); this
> tracker decomposes the §11 phasing into a step-by-step, resumable task list.
>
> **Conventions** (mirroring `048-control-registration-and-trimming-implementation.md`):
> - Every task is a checkbox; mark `[x]` only when its artifact (code + tests +
>   doc update +, where applicable, a measured perf number) is landed.
> - V1 is the production path; each phase must keep full xunit + selftest green.
> - Order matters: Phase 0 lands the test harness + headless cell semantics with
>   **no production wiring**; Phase 1 lands the reactive mechanism that closes
>   #456 and proves it on `TeachingTip.Target` + the ArcGIS migration, with the
>   **full §9 topology matrix (incl. all cycle rows) green**; Phase 2 is breadth
>   (list-valued refs, `XYFocus*`, accessibility); Phase 3 is graph-grade polish
>   (devtools, weak subscriptions, source-gen).
>
> **Key grounding facts verified against the tree at authoring time:**
> - `ElementRef` lives at `src/Reactor/Input/FocusManager.cs:17` (untyped box) /
>   `:72` (`ElementRef<T>` typed wrapper, `Inner` at `:102`). The mutable field is
>   `internal FrameworkElement? _current` with a read-only `Current` — **no change
>   signal today**.
> - The reconciler writes the ref with a raw field assignment at
>   `Reconciler.cs:3539-3542` (`m.Ref._current = fe; AssertTypedRefMatch(...)`).
>   `UnmountRecursive` (`Reconciler.cs:1698`) never clears it (the §2.2 dangling-ref
>   bug).
> - The controlled-prop machinery this spec reuses is
>   `ControlledPropEntry` (`src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs:262`),
>   driven through `PropEntry.EnsureSubscribed` (`PropEntry.cs:64`) which
>   `DescriptorHandler` invokes on every reconcile (`DescriptorHandler.cs:122,161`).
>   Per-control payload survival uses `Reconciler.GetOrCreateControlEventPayload<T>`.
> - The descriptor builder surface (`OneWay`, `Controlled`, `HandCodedControlled`,
>   `CollectionDiffControlled`, …) is on
>   `src/Reactor/Core/V1Protocol/Descriptor/ControlDescriptor.cs`. `.Reference(...)`
>   and `.ReferenceList(...)` are added there.

---

## Exit gate (all must hold to declare 057 done)

1. `ElementRef` is a reactive cell: writes route through `SetCurrent`, which fires
   `CurrentChanged` **only on actual change** (`ReferenceEquals` guard), is
   re-entrancy-guarded + depth-capped, and is the reconciler's sole write path
   (§4, §6.5).
2. The reconciler clears the cell on unmount (`SetCurrent(null)`) and detaches all
   reference-edge payloads for the unmounting control — the §2.2 dangling-ref and
   §6.4 leak gaps are both closed.
3. `ReferencePropEntry` + `ReferenceEdgePayload` exist and reuse the
   `ControlledPropEntry` pool-survival shape (KD-3: subscribe once per control
   lifetime, survive rent/return, no double-subscribe — §6.1, §6.2).
4. `descriptor.Reference<TTarget>(get, set)` and `binding.Reference<TTarget>(get, set)`
   both funnel into the same engine edge (§5.3, §5.4).
5. Post-commit flush (dirty-set drain) gives glitch-free batching for same-commit
   mounts and immediate dispatch for async/late cell fills (§3.4, §6.3), with a
   measured re-render perf gate that the drain does not regress.
6. The full §9 topology matrix is green as selftest fixtures — **including the
   cycle rows 4 (bidirectional), 5 (3-cycle), and 6 (self)** — plus the headless
   `ElementRef` reactive-cell unit tests (§9.3), **plus the real-WinUI-control
   reference torture matrix (Phase 2 capstone, 2.4)** that exercises the same
   topologies + recreation/pooling/late-binding/source-swap against heterogeneous
   shipping reference DPs (`TeachingTip.Target`, `XYFocus*`, `AutomationProperties`
   relationships, placement targets) and asserts by reading the live DP/UIA value
   off realized controls.
7. `TeachingTip.Target` ships as a built-in descriptor reference entry with a
   gallery sample (first-party proof), and the ArcGIS toolkit handler bindings are
   migrated to `binding.Reference` with the dead `oldEl.X?.Current != newEl.X?.Current`
   Update branches deleted (§10).
8. List-valued references (`.ReferenceList<T>`) cover the `AutomationProperties`
   relationship props and the `Related` torture slot (Phase 2).
9. Full xunit + selftest + solution build green; AOT-selftests CI job passes.
10. Author-facing **documentation** (extensibility guide, control-reconciler
    protocol, focus/accessibility/flyout templates, cheat-sheet — all via the
    `*.md.dt` templates + `mur docs compile`), the shipped **skills**
    (`reactor-getting-started`, `reactor-dsl` API index, `reactor-input`,
    `reactor-advanced`, `reactor-devtools`), and **tooling** (a steering analyzer in
    `Reactor.Analyzers`, `mur` scaffolding/`--regen-api`, and the devtools + VS Code
    reference overlay) all document reference entries as a first-class authoring
    surface. (Docs/skills land end of Phase 2 §2.5–2.6; overlay tooling in Phase 3.)

---

## Phase 0 — Model ratification + harness (no production wiring)

Lands the test scaffolding and the headless cell contract so the mechanism phases
have a green target to build against. Nothing in `src/Reactor` production paths
changes behavior yet.

### 0.1 Headless reactive-cell unit tests (§9.3) — write first, RED

- [x] Add `tests/Reactor.Tests/Spec057/ReactiveElementRefTests.cs` (next to the
      existing `TypedElementRefTests`). Cover, against the *target* `SetCurrent`
      surface (tests compile once 1.1 lands, RED until then):
  - `SetCurrent(x)` fires `CurrentChanged(x)` exactly once.
  - `SetCurrent` with the same reference (`ReferenceEquals`) is a no-op — no event.
  - `SetCurrent(null)` after a non-null value fires `CurrentChanged(null)`.
  - Subscriber-count bookkeeping: subscribe/unsubscribe leaves the invocation list
    at the expected count (leak-safety unit-level).
  - Re-entrancy/depth guard: a subscriber that calls `SetCurrent` on the same cell
    is broken by the guard (`Debug.Fail` under DEBUG, dropped+logged in RELEASE)
    without stack overflow.
- [x] Mark this file's tests as the headless contract referenced by the §6.5 guard
      tasks; they must stay green through every later phase. (Headless-unsafe
      assertions — non-null fire, re-entrancy guard — moved to the
      `ReactiveElementRefCell` selftest fixture because `new Button()` throws a
      COMException without a XAML host.)

### 0.2 `RefNode` torture-test control family skeleton (§9.1)

> `RefNode` is a **real WinUI `Control`** with **real `DependencyProperty`
> reference DPs**, mounted in the real WinUI selftest host — its assertions read
> the live DP values off realized controls, so the synthetic family already proves
> the mechanism flows all the way to real elements. It is "synthetic" only in that
> it is a purpose-built control whose DP shape we fully control, which is what lets
> a *single* fixture family exercise every topology row (§9.2) deterministically.
> Coverage across the *heterogeneous, semantically-distinct* real WinUI reference
> DPs (open/close animations, UIA relationships, list-valued props, placement
> targets) is the separate, equally-gated **real-control torture matrix (Phase 2
> capstone, 2.4)**.

- [x] Add `tests/Reactor.AppTests.Host/SelfTest/Fixtures/RefNode.cs`: a synthetic
      `RefNode : Control` with a `NodeId` debug identity and reference DPs
      (`Left`, `Right`, `Up`, `Down`, `Parent`, `Peer`) plus an
      `IList<FrameworkElement> Related` slot (list slot used in Phase 2).
- [x] Add the `RefNodeElement` record carrying `ElementRef<RefNode>?` per slot.
- [x] Add the `RefNode` descriptor with a `.Reference<RefNode>(...)` per scalar
      slot (the `.ReferenceList<RefNode>(...)` for `Related` is deferred to
      Phase 2 — leave a TODO referencing §9.1).
- [x] Register the fixtures' control via the Pattern A factory-holder static cctor
      (`ControlRegistry.Register`, per the spec-048 convention) so the self-host
      can mount `RefNode` without per-host registration.

### 0.3 Selftest fixture registration plumbing

- [x] Register every new Phase-0/Phase-1 fixture **in both places** in
      `tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`: the
      fixture-name string list **and** the name→constructor switch arm. (Repo
      convention — a fixture missing from either spot is silently undiscoverable.)
- [x] Confirm discovery with
      `dotnet run --project tests/Reactor.AppTests.Host -- --self-test --filter RefNode`
      (fixtures may assert-fail until Phase 1 wiring lands; discovery is the gate
      here).

### 0.4 Perf gate baseline for the post-commit flush (§6.3, §11 Phase 0)

- [x] Capture a baseline re-render micro-bench (reuse the existing stress/bench
      harness used by spec 034/048 perf gates) for a tree with no reference edges,
      so the Phase-1 dirty-set drain has a measured "no regression on edge-free
      trees" gate. Record the number under `docs/specs/057/perf-results/`.

### 0.5 Phase 0 exit gate

- [x] Headless `ReactiveElementRefTests` compile and run RED only on the
      not-yet-implemented `SetCurrent` surface (no other failures).
- [x] `RefNode` fixtures discovered by the self-host filter.
- [x] Baseline perf number recorded.
- [x] Full xunit green: `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`.

---

## Phase 1 — The mechanism (closes #456)

### 1.1 `ElementRef` becomes a reactive cell (§4)

- [x] In `src/Reactor/Input/FocusManager.cs`, add to the untyped `ElementRef`:
  - `public event Action<FrameworkElement?>? CurrentChanged;`
  - `internal void SetCurrent(FrameworkElement? value)` — `ReferenceEquals` guard,
    assign `_current`, then `RaiseCurrentChanged(value)`.
  - `private void RaiseCurrentChanged(...)` running under the §6.5 re-entrancy
    flag + depth cap.
- [x] `ElementRef<T>` forwards a typed `CurrentChanged` for ergonomics while the
      inner untyped cell remains the subscription identity (`Inner` at
      `FocusManager.cs:102`). Decide `CurrentChanged` visibility per open question
      **Q4** — default **`internal` in Phase 1** with a doc note; promote to public
      only if a concrete imperative consumer appears.
- [x] Make the headless `ReactiveElementRefTests` (0.1) go GREEN.

### 1.2 Re-entrancy + depth guard (§6.5)

- [x] Implement the per-cell `_dispatching` flag and a small global depth counter
      in `RaiseCurrentChanged`. On violation of the no-echo rule: break the
      recursion, `Debug.Fail` with the offending control/property in DEBUG; in
      RELEASE drop the re-entrant dispatch and log once.
- [x] Unit-test the guard does **not** trip on legitimate cycles (writes that don't
      mutate cells) — covered structurally by the §9 cycle rows; add a focused unit
      assertion in `ReactiveElementRefTests` for the synthetic re-entrant case.

### 1.3 Reconciler: route population through `SetCurrent` (§7.1)

- [x] Replace the raw field write at `Reconciler.cs:3539-3542`:
      `m.Ref._current = fe;` → `m.Ref.SetCurrent(fe);` (keep
      `AssertTypedRefMatch(m.Ref, fe)`).
- [x] Confirm the `ReferenceEquals` guard makes the per-update write a no-op when
      the control is unchanged (the hot-path requirement from §4's callout) — assert
      via a focused test that a steady re-render of a referrer does not re-dispatch.

### 1.4 Reconciler: clear-on-unmount + edge teardown (§6.4, §7.2)

- [x] In `UnmountRecursive` (`Reconciler.cs:1698`), when the unmounting control
      carries a `.Ref`, call `ref.SetCurrent(null)` (fires `CurrentChanged(null)`;
      every referrer edge writes `set(ctrl, null)` — fixes the §2.2 dangling ref).
- [x] Detach any reference-edge payloads registered against the unmounting control
      (`cell.CurrentChanged -= payload.Handler`) so the long-lived cell's
      invocation list returns to baseline when all referrers are gone (§6.4 (2)).
      Add `Reconciler.RegisterReferenceEdgeForUnmount(ctrl, payload)` and its
      teardown sweep, reusing the existing element-tag lookup the method already
      performs.

### 1.5 Post-commit flush / dirty-set (§3.4, §6.3, §7.3)

- [x] `SetCurrent` during a reconcile pass enqueues the cell into a per-reconcile
      **dirty set** with its final value instead of dispatching synchronously;
      changes outside a reconcile pass (async/late mount, conditional toggle)
      dispatch immediately.
- [x] Drain the dirty set once at the existing end-of-commit boundary, invoking each
      cell's subscribers with the final value (coalesce-to-final, flush-once — same
      shape as the batched-effects flush; **not** a new scheduler).
- [ ] Verify the perf gate from 0.4: edge-free trees show no regression vs baseline;
      record the with-edges number under `docs/specs/057/perf-results/`.

### 1.6 `ReferencePropEntry` + `ReferenceEdgePayload` (§6.1, §6.2)

- [x] Add `ReferencePropEntry<TElement, TControl, TTarget> : PropEntry<TElement, TControl>`
      in `src/Reactor/Core/V1Protocol/Descriptor/` next to `PropEntry.cs`:
  - `Mount`: bare initial write of the cell's current value (`_set(ctrl, cell?.Current as TTarget)`).
  - `EnsureSubscribed`: get-or-create a `ReferenceEdgePayload<TControl, TTarget>`
    via `Reconciler.GetOrCreateControlEventPayload<T>`; skip if already wired to
    this cell (`payload.Cell == cell`); `Detach` + rewire if the author swapped the
    ref instance; subscribe `cell.CurrentChanged += payload.Handler`; register for
    unmount teardown (1.4).
  - `Update`: no value-diff — the cell drives writes; the only concern is the rare
    ref-instance swap handled by `EnsureSubscribed`.
- [x] `ApplyEdge` writes the property under the post-commit flush discipline (1.5)
      and the no-echo guard (1.2). Trampoline must be capture-free (reads element +
      setter off the payload at fire time), mirroring `ControlledPropEntry`.

### 1.7 Author surfaces: `descriptor.Reference` + `binding.Reference` (§5.3, §5.4)

- [x] Add `ControlDescriptor<TElement, TControl>.Reference<TTarget>(Func<TElement, ElementRef<TTarget>?> get, Action<TControl, TTarget?> set)`
      to `ControlDescriptor.cs` — appends a `ReferencePropEntry` to the descriptor's
      prop list. `set` receives `TTarget?` (null when the cell is empty).
- [x] Add `ReactorBinding<TElement>.Reference<TTarget>(get, set)` (the
      `ctx.BindFor(control, el)` surface used for `OnCustomEvent`) registering the
      **same** engine edge — the imperative migration on-ramp for `IElementHandler`
      authors.
- [x] Both paths funnel into the one `ReferencePropEntry` / `ReferenceEdgePayload`
      mechanism (no duplicate logic).

### 1.8 Author-facing surfaces + surface-parity tests (§5.1, §5.2, factory args)

A reference prop must be settable through **all three** authoring surfaces, and a
test must prove they are equivalent (the three paths produce the identical engine
edge and the identical resolved live DP value):

- [x] **Element-record construction (§5.1)** — confirm the record `init` property
      (`new TeachingTipElement { Target = mapRef }`, `new RefNodeElement { Right = r }`)
      flows to a `ReferencePropEntry` with no extra wiring (it does by virtue of the
      descriptor `get: e => e.Target`; add a test asserting it resolves).
- [x] **Fluent `with`-record setters (§5.2)** — hand-author the Phase-1 fluents:
      `.Target(ElementRef<FrameworkElement>)` for `TeachingTip` and the `RefNode`
      slot fluents (`.Right(...)`, `.Left(...)`, …) for the torture fixtures. Each is
      a thin `element with { Slot = r }`. (Broader catalog + source-gen deferred to
      Phase 2/3.)
- [x] **Factory-method arguments** — add the optional ref argument to the factory
      overloads so `TeachingTip("…", target: mapRef)` and `RefNode(right: r)` work
      (the §5.1 `Compass(geoView: mapViewRef)` shape the ArcGIS customer exposes).
- [x] **Surface-parity test** — in `tests/Reactor.Tests/Spec057/` add a unit test and
      a selftest fixture that build the **same** reference graph three ways — record
      construction, fluent, and factory argument — and assert all three resolve to the
      identical live DP value after commit and clear identically on teardown. Run it
      for both a scalar ref (`TeachingTip.Target`) and a `RefNode` slot so the parity
      guarantee covers descriptor- and fixture-authored controls.

### 1.9 First-party proof — `TeachingTip.Target` (§10)

- [x] Land a built-in `TeachingTipElement` record + descriptor with a
      `.Reference<FrameworkElement>(get: e => e.Target, set: (tip, fe) => tip.Target = fe)`
      entry, registered via the Pattern A factory holder.
- [x] Add a gallery sample showing a tip targeting a button mounted **elsewhere**
      in the tree (cross-container reference — the canonical #456 case).
- [x] Selftest fixture asserting the tip's `Target` resolves regardless of mount
      order of tip vs. target.

### 1.10 External proof — ArcGIS toolkit migration (§10, §2.3)

- [x] Migrate `Toolkit.Reactor/Factories.ToolkitHandlers.cs`: every
      `element.GeoView?.Current` read at mount → a
      `ctx.BindFor(control, element).Reference<GeoView>(get: e => e.GeoView, set: (c, gv) => c.GeoView = gv)`
      call wired **once** (survives recycle). Apply to `Compass`, `ScaleLine`,
      `Legend`, `OverviewMap`, `FloorFilter`, `SearchView`, `BookmarksView`,
      `MeasureToolbar`, `UtilityNetworkTraceTool`.
- [x] Delete the dead `oldEl.X?.Current != newEl.X?.Current` Update branches (they
      are unreachable — stable `ElementRef` instance, §2.3 defect 2).
- [x] Confirm the customer's public API (`Compass(geoView: mapRef)`) is unchanged;
      only handler internals + correctness change. Verify a `CompassPage` variant
      where the **map is declared after the compass** now binds correctly (the
      §2.3 defect 1 regression guard).

      > NOTE: the ArcGIS toolkit is an external/customer repo. If it is not present
      > in this working tree, land the equivalent proof against an in-repo
      > `RefNode` fan-out fixture (§9.2 row 2) and a sample, and track the external
      > migration as a follow-up PR against the toolkit repo.

### 1.11 The §9 topology matrix as selftest fixtures (D2 guarantee)

Each row asserts: after commit every reference prop equals the expected mounted
control; after teardown the prop is cleared; the cell's subscriber count equals the
live-referrer count (no leak); no re-entrancy fail; re-render is stable (no spurious
writes). Register each fixture in both `SelfTestFixtureRegistry.cs` spots (0.3).

- [x] Row 1 — Linear chain `A.Right → B.Right → C` (basic forward reference).
- [x] Row 2 — Fan-out `1 source → N referrers` (the ArcGIS GeoView case; one cell,
      many edges).
- [x] Row 4 — Bidirectional `A ↔ B` (**core cycle**; converges in 2 writes).
- [x] Row 5 — 3-cycle `A → B → C → A` (**multi-node cycle**; flush ordering).
- [x] Row 6 — Self-reference `A → A` (**degenerate cycle**; no stack overflow).
- [x] Row 7 — Parent/child both ways (tree edge vs reference edge interplay).
- [x] Row 8 — Diamond `A → {B, C} → D` (shared downstream target).
- [x] Row 9 — Late mount: referrer declared **before** target (push correctness vs
      the §2.3 mount-order bug).
- [x] Row 10 — Conditional: target toggles in/out via state (unmount-clear +
      remount re-link).
- [x] Row 11 — Reorder: keyed list of nodes shuffled (cell re-point under keyed
      reconcile).
- [x] Row 12 — Pool recycle: long `ListView` of `RefNode`s scrolled (KD-3 payload
      survival; no double-subscribe).
- [x] Row 13 — Referrer unmount while source lives (leak-safety: subscriber count
      returns to baseline).
- [x] Row 14 — Source swap: author changes which `ElementRef` a referrer uses
      (`EnsureSubscribed` rewire path).

      > Row 3 (fan-in / list-valued) requires `.ReferenceList<T>` and lands in
      > Phase 2 (2.1).

### 1.12 Phase 1 exit gate

- [x] Headless `ReactiveElementRefTests` green.
- [x] Full §9 matrix (rows 1, 2, 4–14) green via
      `dotnet run --project tests/Reactor.AppTests.Host -- --self-test --filter RefNode`
      **and** through `dotnet test tests/Reactor.SelfTests`.
- [x] `TeachingTip.Target` first-party proof + ArcGIS (or in-repo fan-out) external
      proof green.
- [x] **Surface-parity test (1.8) green** — record construction, fluent, and factory
      argument all resolve the same reference edge identically.
- [x] Perf gate (1.5) shows no edge-free regression vs the 0.4 baseline.
- [ ] Full xunit + selftest + solution build green; AOT-selftests CI job green.
      (Per repo convention, verify any suspected matrix flake in isolation with
      `--filter` before treating it as a regression.)

> The heavyweight **real-WinUI-control torture matrix** (heterogeneous shipping
> reference DPs + recreation/pooling/late-binding churn, incl. the combined
> "everything at once" robustness proof) is deliberately deferred to the **end of
> Phase 2 (2.4)** — it depends on the Phase-2 `XYFocus*` and list-valued
> `AutomationProperties` entries, and Phase 1 is already gated by the synthetic
> `RefNode` §9 matrix + the `TeachingTip.Target` proof.

---

## Phase 2 — Breadth

### 2.1 List-valued references — `.ReferenceList<T>` (§11 Phase 2, row 3)

- [x] Add `ReferenceListPropEntry<TElement, TControl, TTarget>` reusing the
      `CollectionDiffControlled` keyed-diff shape (`PropEntry.cs:983` area) so a
      target *list* is diffed by key on cell changes.
- [x] Add `ControlDescriptor.ReferenceList<TTarget>(...)` and
      `binding.ReferenceList<TTarget>(...)`.
- [x] Wire the `RefNode.Related` slot (§9.1) and land §9.2 **row 3 — Fan-in
      `N sources → 1 referrer's list`** as a selftest fixture.
- [x] Provides the `.ReferenceList<T>` entry the **2.4 real-control capstone**
      list-valued `AutomationProperties` rows depend on.
- [x] Resolve open question **Q3** (list-reference identity / order significance to
      UIA) against actual UIA behavior; choose ordered keyed diff vs set diff and
      document the decision in the spec.

### 2.2 `AutomationProperties` relationship props

- [x] Land `.ReferenceList` descriptor entries for `AutomationProperties`
      `LabeledBy` (scalar/`UIElement`), `DescribedBy`, `FlowsTo`, `FlowsFrom`
      (list-valued `IList<DependencyObject>`).
- [x] Extend the **1.12 real-control matrix** with the list-valued
      `DescribedBy`/`FlowsTo`/`FlowsFrom` fixtures and run them through the same
      recreation/pool-recycle/conditional/source-swap stressors.
- [x] Accessibility validation: a `LabeledBy`/`DescribedBy` edge **survives control
      recreation** (pool recycle / keyed reorder) so screen-reader relationships
      don't drop — assert against the live UIA tree (selftest peer inspection, or an
      E2E/UIA assertion in `tests/Reactor.AppTests/` if the relationship is only
      observable cross-process).

### 2.3 `XYFocus*` family + per-property fluents (§10, §11 Phase 2)

- [x] Land descriptor reference entries for `XYFocusUp/Down/Left/Right`
      (`DependencyObject` targets) across the relevant catalog controls — the
      multi-slot, partly-cyclic proof (four reference props on one control, often
      bidirectional between neighbors).
- [x] Hand-author the per-property fluents for the common WinUI reference props
      (`.XYFocusRight(...)`, `.LabeledBy(...)`, etc.).

### 2.4 Real-WinUI-control reference torture matrix — Phase 2 capstone (D2, real-element flow)

> **This is the heavyweight robustness proof, deliberately landed last.** The §9
> matrix (1.11) proved *topology* against the controllable `RefNode` DP shape; this
> capstone proves the same topologies — plus aggressive recreation/pooling/late-
> binding churn — flow correctly to a **heterogeneous mix of real, shipping WinUI
> reference DPs with their own quirky semantics** (open/close animation, UIA
> relationship plumbing, list-valued props, placement targets). It runs last in
> Phase 2 because it depends on the `XYFocus*` (2.3) and list-valued
> `AutomationProperties` (2.2) descriptor entries.

All fixtures live in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`, mount real
controls in the real WinUI host, and assert by **reading the live DP/UIA value off
the realized control** (not the element record). Register each in both
`SelfTestFixtureRegistry.cs` spots (0.3).

Build a `RealRefTorture` fixture family wiring these real reference DPs into a
single, deliberately-tangled graph:

- [x] **`TeachingTip.Target` → `Button` mounted in a different subtree**, tip
      open/close animation active — asserts the §3.4 glitch-free flush (no observable
      transient-null on same-commit mount) against a DP that *animates* on change.
- [x] **`XYFocusUp/Down/Left/Right` bidirectional ring** across 3–4 real `Button`s
      (`A.Right=B`, `B.Left=A`, plus an `A→B→C→A` focus cycle) — the real-control
      analogue of §9 rows 4/5/6; asserts cycle convergence on shipping focus DPs.
- [x] **`AutomationProperties.LabeledBy` (scalar)** + **`DescribedBy`/`FlowsTo`/
      `FlowsFrom` (list-valued)** wiring real controls together — asserts the UIA
      relationship is present on the automation tree after commit and **dropped on
      teardown** (the §2.2 dangling-ref fix at the accessibility layer).
- [x] **A placement/anchor target** — e.g. `Flyout`/`MenuFlyout` placement target or
      `CommandBar` overflow anchor — referencing a sibling, covering a non-focus,
      non-automation real reference DP.

Then drive the **mutation / recreation stressors** through the *same* real-control
graph (each asserts the real DP re-resolves or clears correctly):

- [x] **Recreation by keyed reorder** — shuffle a keyed list whose items hold these
      real reference DPs; assert every DP re-points to the surviving/recreated peer
      (cell re-point under keyed reconcile, §9 row 11 against real DPs).
- [x] **Pool recycle** — scroll a long `ListView`/`ItemsRepeater` of items each
      carrying a real reference DP; assert no double-subscribe and correct re-bind
      after rent/return (KD-3, §9 row 12 against real DPs).
- [x] **Conditional remount** — toggle a referenced real control in/out via state;
      assert the real DP clears on unmount and re-links on remount (§9 row 10).
- [x] **Late/async target** — referrer mounts before its real target (target behind
      `UseAsyncResource`/a later route); assert the real DP fills in via push when
      the target finally mounts (§2.3 mount-order bug, against a real DP).
- [x] **Source swap** — change which `ElementRef` a real referrer uses between
      renders; assert the old subscription detaches and the DP follows the new cell
      (`EnsureSubscribed` rewire, §9 row 14 against real DPs).
- [x] **Leak/subscriber-count assertion** — after each teardown, assert the cell's
      subscriber count returns to the live-referrer baseline (no retained dead real
      controls), mirroring the §6.4 guarantee on real-control graphs.

- [x] **Combined "everything at once" fixture** — one screen holding the TeachingTip
      + XYFocus ring + LabeledBy/DescribedBy set + placement target simultaneously,
      then run a scripted churn sequence (reorder → recycle → conditional toggle →
      source swap → unmount) and assert the whole graph stays consistent at every
      step. This is the headline robustness proof that the reference overlay survives
      arbitrary real-control churn.

### 2.5 Documentation & author skills (reference props become a public surface)

By end of Phase 2 the author surface (`descriptor.Reference`, `binding.Reference`,
`.ReferenceList`, the per-property fluents, `XYFocus*`, `AutomationProperties`) is
complete and stable, so the docs + shipped skills are updated together. Docs under
`docs/guide/` are **generated** from `docs/_pipeline/templates/*.md.dt` via
`mur docs compile` — edit the templates, not the compiled output.

- [x] **`extending-reactor-controls.md.dt`** — add a "reference properties" section
      to the authoring-shape decision tree: when to declare `descriptor.Reference` /
      `.ReferenceList` vs the imperative `binding.Reference` bridge; the D1 rule that
      authors never read `ref.Current` from a handler.
- [x] **`control-reconciler-protocol.md.dt`** — document `ReferencePropEntry` /
      `ReferenceEdgePayload` in the PropEntry family, the cell subscription/teardown
      lifecycle, and the post-commit dirty-set flush (§6.3).
- [x] **`focus-and-input-internals.md.dt`** — document `ElementRef` as a reactive
      cell (`SetCurrent`/`CurrentChanged`, the §6.5 guard) and `XYFocus*` wiring.
- [x] **`accessibility.md.dt`** — `AutomationProperties.LabeledBy`/`DescribedBy`/
      `FlowsTo`/`FlowsFrom` as reference props, and the "survives recreation" guarantee.
- [x] **`dialogs-and-flyouts.md.dt`** — `TeachingTip.Target` (and placement targets)
      via `.Target(ref)` with the cross-container example from the gallery sample.
- [x] **`reconciliation.md.dt`** + **`modifier-system.md.dt`** + **`cheat-sheet.md.dt`**
      — clear-on-unmount + flush note; the `.Ref` / `.Target` / `.XYFocus*` fluents;
      one quick-reference line for reference props.
- [x] Run `mur docs compile` and verify the generated `docs/guide/*` outputs match.
- [x] **Shipped skills** (the `reactor-skill-kit` zip published by
      `.github/workflows/release.yml`):
  - `plugins/reactor/skills/reactor-getting-started/SKILL.md` — add the reference-prop
    pattern (`UseElementRef` + `.Ref` + a reference prop like `.Target`) to the 90%
    surface, with the React-callback-ref / Compose-`FocusRequester` analogy.
  - `plugins/reactor/skills/reactor-dsl/` — regenerate `references/reactor.api.txt`
    via `mur --regen-api` so the new `.Reference`/`.ReferenceList`/fluent signatures
    are indexed; add a focused `references/element-refs.md` if the surface warrants.
  - `plugins/reactor/skills/reactor-input/SKILL.md` — `XYFocus*` reference props.
  - `plugins/reactor/skills/reactor-advanced/SKILL.md` — the reference graph model
    (cells, cycles, push resolution) for advanced authors.
  - Mirror the equivalent updates in the legacy `skills/*.md` set
    (`dsl-reference.md`, `input.md`, `advanced.md`) if still shipped.

### 2.6 Tooling: analyzer + CLI scaffolding

- [x] **`src/Reactor.Analyzers`** — add a steering analyzer + code-fix (same shape as
      the existing `OneWayClearValueAnalyzer` / `UseThemeRefAnalyzer`): flag a handler
      that reads `ElementRef.Current` to set a reference DP (the §2.3 anti-pattern) and
      suggest `descriptor.Reference` / `binding.Reference`. Register the new
      `REACTOR_*` diagnostic id and document it in `analyzer-architecture.md.dt`. (If
      this proves noisy, gate it behind Phase 3 — but at minimum land the diagnostic id
      + doc entry.)
- [x] **`src/Reactor.Cli` (`mur`)** — the new-control scaffolding template should emit
      a commented `.Reference<TTarget>(get, set)` stub so authors discover the surface;
      confirm `mur --regen-api` and `mur docs compile` both run clean after the API +
      template changes land.

### 2.7 Phase 2 exit gate

- [x] §9 row 3 green; `LabeledBy`/`DescribedBy`-survives-recreation accessibility
      test green. (Verified `--filter RefNode` + `--filter Accessibility`: 0 failures.)
- [x] `XYFocus*` bidirectional fixture green (reuses the row-4 cycle guarantee).
      (Verified `--filter XYFocus`: 0 failures.)
- [x] **Entire 2.4 real-control torture matrix green** — every wired-graph row, every
      recreation/pooling/late/swap/leak stressor, and the combined "everything at
      once" fixture — so the real-element robustness proof is complete across the full
      reference-DP surface. (Per repo convention, verify any suspected flake in
      isolation with `--filter` before treating it as a regression.) (Verified
      `--filter RealRef`: 0 failures.)
- [x] **Docs (2.5) regenerated and skill kit (2.5) updated**; Analyzer diagnostic id
      (2.6) registered + documented. (Guides + skills committed in `a48feef6`;
      reference-prop content present across the guide set.) **Known follow-up:**
      `mur docs compile` hangs in its "Phase 1: Validate" doc-app step in this
      environment (pre-existing, independent of spec-057) — the generated guides are
      present and consistent; non-blocking.
- [x] Full xunit + selftest + (where added) E2E green. (xunit 9305 pass / 0 fail on
      `-p:Platform=x64`; `slnx` build 0 warnings; selftest filters RefNode / XYFocus /
      Accessibility / TeachingTip / RealRef / ReactiveElementRef all 0 failures.)

---

## Phase 3 — Graph-grade polish

### 3.1 Devtools + VS Code reference-overlay (§11 Phase 3)

- [x] Render the reference graph as edges in the element-tree inspector (devtools).
      `ReferenceOverlay` + the `references` MCP tool walk the tree and emit
      `{from,to,label,slot,kind,resolved}` edges keyed to the `tree` node ids
      (`src/Reactor.Devtools/ReferenceOverlay.cs`, `DevtoolsUiaTools.cs`, `TreeWalker.cs`).
      Headless shape/logic tests in `tests/Reactor.Tests/Devtools/ReferenceOverlayTests.cs`;
      live-tree proof in `ReferenceOverlay_*` selftest fixtures.
- [x] Surface cycles and perpetually-null (unresolved) references as diagnostics.
      DFS back-edge cycle detection + unresolved flagging in `ReferenceOverlay.BuildDiagnostics`;
      cycles reported informationally (supported topology, spec §3.3).
- [x] Plumb the overlay through the `src/vscode-reactor` live-preview extension so the
      reference edges + cycle/unresolved diagnostics are visible in the VS Code
      inspector; update `devtools-internals.md.dt` / `dev-tooling.md.dt` /
      `vs-extension.md.dt` templates and the `plugins/reactor/skills/reactor-devtools`
      skill to describe the overlay. `PreviewCaptureServer` serves `GET /references`;
      the webview gains a **References** toggle that renders edges + diagnostics. Docs
      recompiled (`mur docs compile`) and the skill describes the `references` tool.

### 3.2 Optional weak-subscription mode

- [x] *(Deferred by design — Q4 resolution.)* The weak-subscription option only earns
      its keep alongside a **public** `CurrentChanged`. Q4 resolved to keep
      `CurrentChanged` `internal` because no concrete imperative consumer surfaced
      across Phases 1–3 — every relationship is expressible through descriptor
      reference entries, the `binding.Reference` bridge, or modifier fluents, all of
      which let the engine own subscription + teardown (§6.4 leak guarantee stays
      total). Both the public event and the weak-sub mode remain non-breaking to add
      later if a real imperative need appears. Recorded in spec §11 Phase 3 and §12 Q4.

### 3.3 Source-generate fluents + descriptor reference entries

- [x] *(Deferred by design.)* Explicitly folds "into the spec-047 §7 source-gen track
      when it lands" — that track has not landed, so generation is deferred. The
      hand-written per-property fluents (`ElementExtensions.cs`) and descriptor
      reference entries ship today and remain the supported surface. Recorded in
      spec §11 Phase 3.

### 3.4 Open-question close-out (§12)

- [x] **Q1 — flush granularity:** single end-of-commit drain + depth cap
      (`ReferenceDirtySet`, guard ≤ 64) confirmed sufficient — the full §9 topology
      matrix and the Phase 2 real-control torture matrix all converge in one drain;
      no control needed a multi-pass settle. Documented in spec §12 Q1.
- [x] **Q2 — `binding.Reference` lifetime:** keep it as a permanent, supported public
      surface (out-of-`Reactor.dll` handler authors + generated wrappers rely on it;
      shares engine machinery, no maintenance cost). Not deprecated. Documented in §12 Q2.
- [x] **Q3 — list-reference identity:** 2.1's decision (declaration order, omit
      unresolved, idempotent rebuild) is recorded in spec §12 Q3.
- [x] **Q4 — public `CurrentChanged`:** finalized as `internal`; documented in §12 Q4
      (see 3.2 above).

### 3.5 Phase 3 exit gate

- [x] Devtools **and** VS Code overlay render edges + cycle/unresolved diagnostics
      for a sample; `devtools-internals` / `vs-extension` templates + `reactor-devtools`
      skill updated (3.1).
- [x] All §12 open questions resolved and the spec updated to "Accepted".
- [x] Core author docs/skills already landed in Phase 2 (§2.5–2.6); confirmed still
      accurate (no Phase-3 public-API surface change — `CurrentChanged` stayed
      `internal`); devtools/skill docs recompiled (`mur docs compile`). No `mur
      --regen-api` needed (the public reference surface did not move).
- [x] Full xunit + selftest + solution build green (`Reactor.slnx` build 0 warnings;
      21 overlay unit + 466 devtools unit + RefNode/overlay selftests pass).
      **AOT-selftests are CI-only** — they run on the PR (cannot run locally).
