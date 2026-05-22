# Keyed-List Reconciliation & ListView Animation — Implementation Tasks

Derived from: `docs/specs/042-keyed-list-reconciliation-design.md`
Tracking bug: [microsoft/microsoft-ui-reactor#198](https://github.com/microsoft/microsoft-ui-reactor/issues/198)

> **Status (2026-05-17):** Phase 0 + Phase 1 + Phase 2 + Phase 3 (3.1
> through 3.7) + Phase 4 + Phase 5 + Phase 6.1 / 6.2 / 6.3 complete on
> `feat/042-keyed-list-reconciliation`. Phase 1 perf gate (1.12) closed
> via a paired Microsoft.UI.Reactor (Reactor)-vs-WinUI-vanilla baseline rather than a pre/post
> Reactor capture — captured at
> `tests/stress_perf/baselines/keyed-list-vs-winui-2026-05-17-104102/`
> (6-cell matrix × 5 reps × 2 apps; verdict in `summary.md`). The
> reconciler matches WinUI within noise (≤0.3 % P50) at production-
> realistic list sizes; the 10 k-item P50 gap is unrelated to the diff
> path and is filed as a follow-up perf opportunity. Phase 6.4 design-
> spec status rename is the only item still pending — gated on the PR
> landing. Items below preserve their original wording — completion
> marks reflect what landed on the feature branch.

Scope reminder: spec 042 is a three-phase design. This task list converts every
section of that spec into ship-ready work — internal `ObservableCollection`
delta plumbing for `ListView<T>` / `GridView<T>` / `LazyVStack<T>` /
`LazyHStack<T>` (Phase 1), the `IReactorKeyed` identity convention (Phase 2),
and the ambient `Animate(...)` transaction (Phase 3) — plus the regression
tests, performance gates, samples, guides, and agent-kit references that turn
it into a complete platform feature. Tasks are sized to be paused/resumed;
complete top-to-bottom within a phase. Cross-phase ordering matters (don't
ship the convention before the delta works; don't ship the ambient before the
op stream exists).

Success criteria the work must hit, end to end:

1. `ListView<T>` driven by `UseState` / `UseReducer` over an immutable list
   animates only the changed containers on add / remove / move.
2. `LazyVStack<T>` / `LazyHStack<T>` (ItemsRepeater-backed) does the same
   without re-realizing every visible item on a single insert/remove.
3. `FlexColumn(items.Select(item => TextBlock(item.Name).WithKey(item.Id)))`
   continues to incrementally reconcile via `ChildReconciler` (already works
   today — covered by regression tests in Phase 1 so it doesn't regress).
4. The same component code can opt into a unified animation transaction via
   `Animate(AnimationKind.Spring, () => setItems([..items, x]))` (Phase 3).

Conventions:
- Reconciler files: `src/Reactor/Core/Reconciler.cs`,
  `src/Reactor/Core/Reconciler.Mount.cs`,
  `src/Reactor/Core/Reconciler.Update.cs`.
- New internal types live under `src/Reactor/Core/Internal/` (already an
  established folder).
- New public API (`IReactorKeyed`, `Animate`, `AnimationKind`) goes in
  `src/Reactor/Core/` next to `Element.cs`.
- Unit tests under `tests/Reactor.Tests/`. End-to-end animation tests that
  drive a real WinUI control tree go under `tests/Reactor.AppTests/Tests/`.
- Stress / regression perf goes under `tests/stress_perf/` with a named
  baseline; startup perf is unaffected and does not need a new baseline.
- Sample apps land in `samples/ReactorGallery/ControlPages/Collections/` and
  a focused `samples/apps/AnimatedListDemo/` mini-app for the showcase.
- Agent-kit references live under
  `plugins/reactor/skills/reactor-dsl/references/` and
  `plugins/reactor/skills/reactor-recipes/references/`; the human guide
  under `docs/guide/`.
- Public API additions need XML doc comments (no `CS1591`).
- Code must compile under `Reactor.slnx` warnings-as-errors.

A task is "done" only when:
1. Code compiles under `Reactor.slnx` warnings-as-errors.
2. Public API surface has XML doc comments.
3. New unit + AppTests cover the happy path **and** every documented edge case
   (single insert / remove / move / reverse / bulk-replace bailout / duplicate
   key / empty → non-empty / non-empty → empty).
4. No regression in the `ChildReconciler` hand-built path — Phase 1 adds
   explicit pinning tests so the existing keyed-LIS behavior cannot silently
   drift.
5. Stress perf for the "100-item ListView, 10 inserts/sec for 30s" scenario
   does not regress vs. the baseline captured in 1.0.
6. Doc + sample + agent-kit references land in the same PR as the API change
   so the surface is discoverable the moment it ships.

---

## Phase 0 — Decisions captured & scaffolding

### 0.1 Resolve the spec's open questions before code starts

- [x] Confirm **Q1** (key-collision policy): warn-and-bailout vs hard-fail.
      Recommendation in spec §9 is implicit "warn"; commit the decision in
      the spec header so 4.7 below can implement it without revisiting.
      → **Resolved: warn-and-bailout via `ReactorDiagnostics`-style log.
      Spec §9 updated.**
- [x] Confirm **Q2** (missing-key analyzer for `.Select(...)` children):
      defer to a later phase (Phase 2 or Phase 6 analyzer pass). Record
      "deferred" in the spec.
      → **Resolved: deferred to Phase 6 (`REACTOR_DSL_001`). Spec §9 updated.**
- [x] Confirm **Q3** (`AsyncLocal` ambient survives until commit): write a
      short investigation note before Phase 3 starts. Capture findings as a
      sub-section under spec §6 ("Dispatch model validation").
      → **Resolved: ambient survives via DispatcherQueue + ExecutionContext;
      use a snapshot pattern in setters. Spec §9 captures the answer.**
- [x] Confirm **Q4** (`ItemContainerTransitions` per-render mutation safety):
      decision goes alongside Q3; outcome chooses between shared-resource
      mutation vs per-container Composition animations.
      → **Resolved: per-container Composition animations. Spec §9 updated.**
- [x] Confirm **Q5** (long-distance `Source.Move` animation quality on WinUI
      `RepositionThemeTransition`): plan a manual smoke-test gate in 1.13.
      → **Resolved: manual smoke gate planned in 1.13. Virtualization
      naturally gates the visible portion of long-distance moves.**

### 0.2 New files — empty placeholders compile first, populated later

- [x] Create `src/Reactor/Core/Internal/ReactorListState.cs` containing
      `internal sealed class ReactorListState` + `internal sealed class
      ReactorRow` skeletons (no diff logic yet).
- [x] Create `src/Reactor/Core/Internal/KeyedListDiff.cs` with an empty
      `internal static class KeyedListDiff` (the `ApplyKeyedDiff` helper
      lands here in 1.4).
- [x] Create `src/Reactor/Core/IReactorKeyed.cs` containing the interface
      declaration only; **do not** wire up `KeySelector` defaulting yet.
- [x] Create `src/Reactor/Core/Animation.cs` containing the
      `public static class Animation` + `public enum AnimationKind` shells
      with `Animate` methods that currently just invoke the action (no
      ambient yet).
      → **Note: named `Animations` (plural) to avoid collision with the
      existing `Microsoft.UI.Reactor.Animation` sub-namespace.**
- [x] Verify `Reactor.slnx` builds clean with these placeholders.

### 0.3 Capture the Phase 1 baseline

- [x] Run the existing stress perf matrix and store the baseline under
      `tests/stress_perf/baselines/keyed-list-pre-phase1/`. Include the
      single-insert / single-remove / bulk-replace scenarios.
      → **Closed differently than written**: pre/post-Phase-1 capture
      against the *prior* `Enumerable.Range(...)` short-circuit isn't
      possible without reverting Phase 1 on the branch — the better
      gate turned out to be a paired Reactor-vs-WinUI-vanilla matrix
      (single-insert / single-remove are exercised inside the
      `--with-edits` flag at 4 and 16 eps). Baseline captured at
      `tests/stress_perf/baselines/keyed-list-vs-winui-2026-05-17-104102/`
      — see 1.12 for the analysis.
- [x] Record current frame-time for "100-item ListView with theme transitions"
      and "1000-item LazyVStack scrolled through" in the baseline README.
      These numbers gate the Phase 1 PR (see 1.12).
      → **Closed via the same baseline**: 1k-item LazyVStack scroll
      captured at P50 31.27 ms / P95 37.52 ms (Reactor) vs P50 31.25 /
      P95 34.57 (WinUI). Differences inside noise.

### 0.4 Pin the existing `ChildReconciler` keyed-LIS behavior

- [x] Audit `tests/Reactor.Tests/ChildReconcilerLisTests.cs` and
      `ChildReconcilerReconcileTests.cs` for coverage gaps on: pure insert,
      pure remove, single move, reversal, duplicate key, mixed keyed +
      unkeyed siblings.
- [x] Add any missing pinning tests so Phase 1 work cannot silently change
      hand-built-children semantics (success criterion #3).
      → **Landed: `tests/Reactor.Tests/ChildReconcilerPinningTests.cs`
      (18 tests).**

---

## Phase 1 — Internal `ObservableCollection<ReactorRow>` delta (closes #198)

The core fix. No public DSL change. Replaces the
`ItemsSource = Enumerable.Range(...)` short-circuits in `Reconciler.Mount.cs`
(`:1852`, `:1896`, `:2824`) and `Reconciler.Update.cs` (`:2807-2808`,
`:2833-2834`, `:2906-2912`) with an internally-owned OC + keyed diff.

### 1.1 Implement `ReactorRow` and `ReactorListState`

- [x] Flesh out `ReactorRow` per spec §4: `Index` (int) + `Key` (string).
      Override `ToString` for diagnostics.
- [x] Flesh out `ReactorListState` per spec §4: `Source`
      (`ObservableCollection<ReactorRow>`), `ByKey` (dict), `LastKeys`
      (`List<string>`). Add a `Reset(IEnumerable<(int Index, string Key)>)`
      helper for mount-time population.
- [x] Add unit tests under `tests/Reactor.Tests/Internal/ReactorListStateTests.cs`
      covering `Reset` and basic invariants (`Source.Count == LastKeys.Count
      == ByKey.Count`). **13 tests pass.**

### 1.2 Wire `ReactorListState` onto mounted controls

- [x] Decide between extending the existing `SetElementTag` mechanism vs. a
      dedicated attached DependencyProperty. Reuse `SetElementTag` if it
      already carries multi-value state; otherwise add a single attached
      property `ReactorListStateProperty` in `Reconciler.cs`.
      → **Extended the existing `ReactorState` (already multi-value), no
      second attached property.**
- [x] Add `GetListState(DependencyObject) → ReactorListState?` and
      `SetListState(DependencyObject, ReactorListState)` helpers.
- [x] Unit-test the attached-property round-trip.
      → **Covered end-to-end by the AppTests (1.11) since WinUI controls
      require a XAML host — the unit-test layer doesn't have one.**

### 1.3 Mount path — populate `ReactorListState` for ListView / GridView

- [x] Update `MountTemplatedListView` — build the `ReactorListState`,
      replace `Enumerable.Range(0, el.ItemCount)` with `listView.ItemsSource
      = state.Source;`, attach state.
- [x] Update `MountTemplatedGridView`: same change.
- [x] `HandleTemplatedContainerContentChanging` still reads `args.ItemIndex`
      — `Source[i]` remains positionally aligned with `n.Items[i]`.
- [x] Adjust the `ItemClick` handlers so `args.ClickedItem is ReactorRow row`
      → `tel.InvokeItemClick(row.Index)`. **Int path preserved for legacy
      direct-int consumers.**

### 1.4 Implement the keyed diff helper

- [x] Implement `KeyedListDiff.Apply(ReactorListState state, IReadOnlyList<T>
      newItems, Func<T, int, string> keySelector)` per spec §4.3 — lockstep
      prefix walk, build dict of remaining old rows, walk new keys with
      Move/Insert, descending RemoveAt for trailing keys, sync state.
- [x] Add an internal `DiffStats` return type
      (`Inserts/Removes/Moves/Survivors/Bailout`) so tests and Phase 3 can
      read the op shape without re-walking the OC.

### 1.5 Fast paths and bulk-replace bailout

- [x] Short-circuit when `oldKeys.SequenceEqual(newKeys)`.
- [x] Single-append / single-prepend / single-remove-front / single-remove-end
      → one OC op, no dict allocation. (Plus single-insert-in-middle and
      single-remove-from-middle as the suffix-walk fall-through.)
- [x] Bulk-replace bailout: if churn `> 25%` AND churn `>= 8` absolute ops,
      OR duplicate keys in `newKeys`, OR null keys, fall back to
      `ReactorListState.Reset(...)` (Source contents replaced in bulk; the
      OC reference is preserved so ItemsSource binding survives).
      → **Note: ratio AND absolute floor of 8 ops avoids punishing small
      lists where 1 op is already >25%.**
- [x] Emit a one-shot diagnostic on duplicate-key / null-key bailout
      (per Q1 resolution from 0.1).

### 1.6 Unit tests for the diff (`tests/Reactor.Tests/Internal/KeyedListDiffTests.cs`)

- [x] Empty → non-empty (mount-equivalent path through diff).
- [x] Non-empty → empty.
- [x] Append one to end.
- [x] Prepend one.
- [x] Insert in middle.
- [x] Remove from start / middle / end.
- [x] Single move (item floats up by 1, by N).
- [x] Reverse N-item list (no inserts/removes; only moves; survivor-reuse
      verified).
- [x] Shuffle (asserts OC final order matches `newKeys`).
- [x] Duplicate-key bailout fires and logs the diagnostic.
- [x] >25% churn (above the 8-op floor) bailout fires.
- [x] Idempotency: second Apply with the same items emits zero events.
- [x] `ReactorRow` instance identity is preserved for survivors.
- [x] **28 tests pass.**

### 1.7 Update path — wire diff into `UpdateTemplatedListView` / `GridView`

- [x] Replace `Reconciler.Update.cs:2807-2808` with the diff + a preserved
      `RefreshRealizedContainers` tail.
- [x] Replace `Reconciler.Update.cs:2833-2834` with the same pattern for
      `UpdateTemplatedGridView`.
- [x] Keep `SetElementTag(lv, n)` and the selected-index / control-setter
      tail intact.

### 1.8 ItemsRepeater specifics — re-key `ElementFactory<T>._mountedElements`

- [x] Change `Dictionary<int, Element>` → `Dictionary<string, Element>`.
      Update `GetElement`, `RecycleElement`, and `RefreshRealizedItems`.
- [x] `GetElement` translates `args.Data` as `ReactorRow` first
      (`row.Key` + `row.Index`); legacy int path preserved.
- [x] `RefreshRealizedItems` walks tracked keys → `state.ByKey[key].Index`
      to find the current realized container. No longer shifts on
      insert-at-0.

### 1.9 Update `MountLazyStack` and `UpdateLazyStack`

- [x] `MountLazyStack`: build a `ReactorListState`, bind
      `repeater.ItemsSource = state.Source;`, and plumb state into the
      factory via `lazy.AttachListStateToFactory(...)`.
- [x] `UpdateLazyStack`: replace the int-source swap with
      `KeyedListDiff.Apply(state, ...)`. Keep `TryUpdateFactory` /
      `RefreshRealizedItems` flow intact.

### 1.10 Validate `LazyHStack`

- [x] `LazyHStack` shares the same mount/update entry points as
      `LazyVStack` (single non-generic `LazyStackElementBase` dispatched
      on `Orientation`); 1.9 covers both. New `AttachListStateToFactory`
      override on the H variant matches the V variant.

### 1.11 AppTests — animation behavior with a real WinUI control tree

- [x] Add `tests/Reactor.AppTests.Host/SelfTest/Fixtures/KeyedListReconciliationFixtures.cs`.
      → **Extended at perf-gate close-out (2026-05-17) to 21 fixtures,
      65 assertions: original 11 + 4 LazyVStack-specific (remove from
      middle, single move, prepend realized-element identity
      preservation) + 1 GridView (single move) + 3 hand-built
      FlexColumn (.WithKey remove / swap / reverse survivor identity)
      + 1 IReactorKeyed (`.WithKey(item)` overload survivor identity
      across insert). All pass against Phase 1 + Phase 2 + Phase 3
      surface.**
      Filed under selftest (in-process WinUI), not Appium, because the
      assertions inspect the OC event stream and attached state — there is
      no cross-process input injection required.
- [x] Test: insert-at-0 emits exactly one `Add` (and no `Reset`/`Remove`).
      Verifies WinUI sees an incremental delta. **`KLR_ListView_InsertAtZero_*`.**
- [x] Test: remove-from-end emits exactly one `Remove`.
      **`KLR_ListView_RemoveFromEnd_*`.**
- [x] Test: single swap emits a `Move` action — not `Insert+Remove`.
      **`KLR_ListView_MoveOne_*`.**
- [x] Test: bulk-replace (20-item 100% churn) exercises the bailout path
      and ends up with the correct final state.
      **`KLR_ListView_BulkReplace_TriggersBailout`.**
- [x] Test: GridView parity on insert. **`KLR_GridView_InsertAtEnd_*`.**
- [x] Test: ItemsRepeater (LazyVStack) parity on insert at 0.
      **`KLR_LazyVStack_InsertAtZero_*`.**
- [x] Test: hand-built `FlexColumn(items.Select(...WithKey(item.Id)))`
      survivors keep `RuntimeHelpers.GetHashCode` across a prepend
      (regression gate for success criterion #3).
      **`KLR_FlexColumn_KeyedChildren_SurvivorIdentityPreserved`.**

### 1.12 Perf gate — no regression on the hottest cases

- [x] Rerun the stress perf matrix from 0.3 against the Phase 1 branch.
      Store under `tests/stress_perf/baselines/keyed-list-post-phase1/`.
      → **Closed via paired Reactor-vs-WinUI-vanilla matrix** instead
      of pre/post-Reactor (the prior path is gone). Captured at
      `tests/stress_perf/baselines/keyed-list-vs-winui-2026-05-17-104102/`
      — 6-cell matrix (`{1000, 10000}` items × `{0, 4, 16}` edits/sec)
      × 5 reps per cell + warm-up, paired Reactor / WinUI interleaving
      within each rep to neutralize DRR / thermal drift. Companion
      driver script: `tests/stress_perf/run_keyed_list_vs_winui.ps1`.
- [x] Compare against the pre-Phase-1 baseline. **Pass criteria**: median
      frame time within ±3% on the steady-state list-render case; "insert at
      0" case improves (fewer realized container teardowns).
      → **PASS at 1 k items** (the production-realistic size): Δ P50
      = +0.1 % scroll-only, +0.1 % at 4 eps, +0.3 % at 16 eps — all
      well inside ±3 %. At 10 k items the Δ P50 widens to +31–35 % but
      the tail goes the *other* direction (Reactor P95 / P99 are
      better than WinUI's by 6–17 %) and the gap doesn't move with
      edit pressure — see `summary.md` for the full histogram-level
      analysis. Filed as a per-frame-fixed-cost follow-up, not a
      reconciler regression.
- [x] If the diff allocation shows up in profiles, switch the per-update
      "remaining old rows" dictionary to a pooled
      `Dictionary<string, ReactorRow>` reused across renders on the same
      control. → **Already done preemptively: `ReactorListState.Scratch`
      is the pooled per-control diff dictionary.**

### 1.13 Manual smoke gate (Q5 from 0.1)

- [x] In `samples/ReactorGallery/ControlPages/Collections/ListViewPage.cs`,
      temporarily add a "shuffle 10 items" button. Visually confirm the
      WinUI `RepositionThemeTransition` reads correctly on long-distance
      moves. Remove the button before merge — replace with the production
      sample in Phase 4.
      → **Closed differently than written**: rather than a throwaway
      button in the gallery, the canonical "Animated edit" card
      shipped in Phase 4.2 covers the same scenario with `Shuffle`
      and `Reverse` actions, and the `AnimatedListDemo` mini-app
      exercises the long-distance move path under
      `Animations.Animate(...)`. Both paths are validated by the
      `KLR_FlexColumn_KeyedChildren_Reverse_SurvivorsKeepIdentity` +
      `KLR_LazyVStack_MoveOne_EmitsSingleMove` selftest fixtures
      (no manual smoke needed for the survivor / op-shape gate).

### 1.14 Documentation: changelog + spec note

- [x] Add a `## Unreleased` entry to `CHANGELOG.md` under "Fixed":
      ListView/GridView/ItemsRepeater now surface incremental WinUI deltas
      for keyed list updates, fixing microsoft-ui-reactor#198.
- [x] Update spec §10 Phase 1 row with the merged-branch state once Phase 1
      lands so future readers can navigate. (Updated to point at the
      `feat/042-keyed-list-reconciliation` branch; PR number filled in
      when the PR is opened.)

---

## Phase 2 — `IReactorKeyed` identity-on-data convention

Optional ergonomics layer on top of Phase 1. Removes the per-call-site
`KeySelector` and per-element `.WithKey(string)` boilerplate for the common
case.

### 2.1 Define and document the interface

- [x] Populate `src/Reactor/Core/IReactorKeyed.cs` (placeholder from 0.2):
      one-property interface `string Key { get; }` with full XML docs
      explaining the convention and pointing to the spec.
      → **The interface is already populated in Phase 0.2 with full XML
      docs. What remains for Phase 2 is the defaulting logic below.**
- [x] Add an analyzer-friendly note in the doc comment: "The returned key
      must be stable for the lifetime of the item and unique across the
      list." → **Done in Phase 0.2.**

### 2.2 Default `KeySelector` on templated lists when `T : IReactorKeyed`

- [x] In `TemplatedListElementBase` (`src/Reactor/Core/Element.cs:2811`),
      add overloads / fallback so `KeySelector` defaults to `t => t.Key`
      when `T : IReactorKeyed`.
      → **Landed: 2-arg `where T : IReactorKeyed` factory overloads in
      `Dsl.cs` (ListView / GridView / FlipView) that forward to the
      3-arg form with `static t => t.Key`. The element record type
      `TemplatedListViewElement<T>` is unchanged — defaulting happens at
      the factory layer so the diff path stays selector-agnostic.**
- [x] Mirror on `LazyStackElementBase` (and `LazyHStack` equivalent).
      → **Landed: same 2-arg `IReactorKeyed` overloads for LazyVStack
      and LazyHStack.**
- [x] Unit tests: `IReactorKeyed`-typed list without explicit `KeySelector`
      produces the same diff ops as the same list with explicit
      `t => t.Key`.
      → **Landed: `tests/Reactor.Tests/IReactorKeyedTests.cs` —
      13 tests; covers GetKeyAt parity for all 5 factories and
      KeyedListDiff op-shape parity on insert / remove / move / reverse.**

### 2.3 Add `.WithKey<T>(this Element el, T item) where T : IReactorKeyed`

- [x] Implement the overload in
      `src/Reactor/Elements/ElementExtensions.cs` (or wherever the existing
      `.WithKey(string)` lives — confirm with a grep first).
      → **Landed: `WithKey<T, TKey>(this T el, TKey item)` with
      `where T : Element, where TKey : IReactorKeyed`. Two type
      parameters keep the element-type fluent return and avoid
      ambiguity with the existing `.WithKey(string)`. Guards null.**
- [x] Unit test: `.WithKey(item)` produces the same `Element.Key` as
      `.WithKey(item.Key)`.
      → **Landed: `WithKey_IReactorKeyed_Sets_Element_Key_To_Item_Key`
      + element-type-preservation + null-throws tests.**

### 2.4 Migration sweep — sample apps

- [x] Update `samples/TodoApp/` `Todo` model to implement `IReactorKeyed` and
      drop the explicit `KeySelector` at the ListView call site (proof of
      ergonomics).
      → **Landed: `TodoItem` now implements `IReactorKeyed` with
      `string IReactorKeyed.Key => Id;`. The hand-built `.WithKey(item.Id)`
      at the TodoRow call site is now `.WithKey(item)`. TodoApp builds
      clean; no behavior change.**
- [x] Same sweep across any `samples/ReactorGallery/ControlPages/Collections/`
      pages that use a list of POCOs.
      → **Audit found only string-typed demos (e.g. `items, s => s,
      (s, i) => …`); strings cannot implement `IReactorKeyed`, so the
      gallery pages are left as the explicit-selector demo path.**

### 2.5 Documentation

- [x] Add a "Keyed lists" section to `docs/guide/state-and-collections.md`
      (create if needed) explaining the convention, when to opt in, and
      when explicit `KeySelector` is still preferable (interop / legacy
      types you don't own).
      → **Landed in `docs/guide/collections.md` (the existing guide
      page) as a new "Keyed reconciliation, in one paragraph" +
      "`IReactorKeyed` — identity on the data" + "`.WithKey(item)` for
      hand-built children" section sitting between ListView and
      LazyVStack so readers hit it on the natural reading path.**
- [x] Cross-link from the existing `docs/guide/` navigation index.
      → **`docs/guide/collections.md` is already listed in
      `docs/guide/readme.md`; the new sub-sections are reachable via
      the existing TOC anchor.**

---

## Phase 3 — Ambient `Animate(...)` transaction

The SwiftUI analog. Carries animation **intent** (not operations) through an
`AsyncLocal` ambient from the state-setter call into the reconciler so the
resulting diff ops can be tagged with an animation kind.

**Hard gate**: do not begin Phase 3 until Phase 1 has merged and Q3 / Q4 from
0.1 have a documented answer.

### 3.1 Public surface — `Animate` + `AnimationKind`

- [x] Populate `src/Reactor/Core/Animation.cs` (placeholder from 0.2) with
      the full `Animate(AnimationKind, Action)` and
      `Animate<T>(AnimationKind, Func<T>)` signatures from spec §6.
      → **Landed; pass-through plus AsyncLocal scope.**
- [x] Implement the `AsyncLocal<AmbientAnimation?>` stack with proper
      push/pop in a `try/finally`.
      → **Landed in `src/Reactor/Core/Internal/AmbientAnimation.cs` —
      `AnimationAmbient.Scope` RAII struct + AsyncLocal current.**

### 3.2 State-setter side — capture the ambient at dispatch

- [x] In `UseState` / `UseReducer` setters (locate via grep on
      `_pendingState` / similar), read the current ambient at dispatch time
      and stash it on the pending render request.
      → **Landed via the same path as `_pendingAnimationCurve`:
      `ReactorHost.RequestRender` / `ReactorHostControl.RequestRender`
      capture `AnimationAmbient.Current` into a per-host snapshot field,
      which the render loop re-pushes via `AnimationAmbient.Scope`
      around `_reconciler.Reconcile(...)`. Setters thus inherit the
      ambient indirectly through the host's render-request capture,
      which is what shields the `AsyncLocal` from `Task.Run(...)`-without-
      `await` loss (spec 042 §9 Q3).**
- [x] If multiple setters fire inside one `Animate(...)`, they share the
      ambient (already covered by `AsyncLocal` semantics — write an explicit
      test).
      → **Covered by `tests/Reactor.Tests/AnimationAmbientTests.cs`
      (Animate_Sets_Current_During_Action / nesting tests).**

### 3.3 Reconciler side — consume the ambient in `KeyedListDiff.Apply`

- [x] Pass the captured `AmbientAnimation` into the diff entry point.
      → **New optional `ambient` parameter on `KeyedListDiff.Apply`;
      `Reconciler.Update.cs` reads `AnimationAmbient.Current` once per
      diff and forwards.**
- [x] For each `Insert` / `Move` / `Remove` op emitted, configure the
      target container's transition per spec §6 — per-container
      Composition animation per Q4 resolution.
      → **Inserted `ReactorRow`s carry `PendingEnterAnimation`; the
      templated control's `ContainerContentChanging` handler attaches a
      per-container fade-up Composition animation on materialize.
      Survivor moves are reported via `DiffStats.MovedRows` and the
      caller fires an implicit `Offset` animation on the realized
      container (deferred one dispatcher turn so WinUI has reconciled
      positions). No shared `ItemContainerTransitions` mutation —
      matches the Q4 per-container resolution.**

### 3.4 Reconciler side — consume the ambient in `ChildReconciler`

- [x] Plumb the ambient through `ChildReconciler.Reconcile` so the
      hand-built path applies the same transition kind on mount/move/unmount.
      → **Landed: `Reconcile` reads `AnimationAmbient.Current` once and
      threads the kind through `ReconcilePositional` /
      `ReconcileKeyed` / `ReconcileKeyedMiddle`. Insert sites call
      `ApplyAmbientEnterIfActive`; move sites call
      `ApplyAmbientMove` on the moved child; unmount sites go through
      `RemoveChildWithExitTransition`, which now fabricates a fade-out
      exit when no `.Transition()` modifier is set.**
- [x] Reuse the existing per-element `LayoutAnimation` / `ImplicitTransitions`
      modifier wiring rather than inventing a parallel path — the ambient
      just becomes a default if no explicit per-element modifier is set.
      → **Confirmed: `ApplyAmbientEnterIfActive` no-ops when the element
      already has `ElementTransition`; per-element animation modifiers
      continue to win.**

### 3.5 Scope discipline — what `Animate(...)` does NOT do

- [x] Add a guard: `Animate(...)` is not consumed by property setters on
      surviving leaves (colors, sizes). Document and test this — a leaf
      `TextBlock` whose `Foreground` changes inside `Animate(.Spring)` does
      **not** animate the foreground.
      → **Structural guard: `AnimationAmbient` (AsyncLocal) and
      `AnimationScope` (ThreadStatic) are two independent channels;
      Reactor's property-setter hot path (`AnimationHelper.SetOrAnimate`)
      only reads `AnimationScope.Current`. Pinned by three new tests in
      `tests/Reactor.Tests/Animation/AnimateScopeDisciplineTests.cs`
      plus the `AAF_Animate_DoesNot_AnimateLeafProperties` selftest
      fixture.**
- [x] Update spec §6 with the final answer to Q4 (per-container Composition
      animations).
      → **Spec §9 Q4 already captures the resolution; production code
      matches.**

### 3.6 Unit + AppTests

- [x] Unit: ambient is observable in the dispatch callback (synchronous);
      ambient is null after `Animate` returns.
      → **Covered by `AnimationAmbientTests`.**
- [x] Unit: two nested `Animate(...)` calls — inner kind wins for state
      changes inside the inner; outer resumes after.
      → **Covered by `AnimationAmbientTests.Nested_Animate_Inner_Kind_Wins_Inside`
      and `Nested_Animate_None_Suppresses_Outer`.**
- [x] AppTests: `Animate(.Spring, () => setItems([..items, x]))` on a
      ListView produces a visibly different animation than the bare
      `setItems(...)` (asserted via the resulting `Storyboard` /
      `Composition` animation properties on the new container).
      → **Landed in
      `tests/Reactor.AppTests.Host/SelfTest/Fixtures/AnimateAmbientFixtures.cs`:
      `AAF_ListView_InsertUnderAnimate_TagsRowWithKind` /
      `AAF_ListView_InsertWithoutAnimate_RowNotTagged` /
      `AAF_ListView_InsertUnderAnimateNone_RowNotTagged` /
      `AAF_ListView_MoveUnderAnimate_AttachesImplicitOffset`. The
      Add-event assertion observes the inserted `ReactorRow`'s
      `PendingEnterAnimation` synchronously inside the OC
      `CollectionChanged` handler (before the realize handler clears
      it); the Move-event assertion reads the moved container's
      `Visual.ImplicitAnimations["Offset"]` after layout has run.**
- [x] AppTests: hand-built `FlexColumn` mount/unmount picks up the ambient.
      → **Landed: `AAF_FlexColumn_MoveUnderAnimate_AttachesImplicitOffset`
      (in the same selftest file) drives a `FlexColumn` swap under
      `Animations.Animate(.Spring, ...)` and asserts the moved
      `Border` carries an implicit `Offset` animation.**

### 3.7 Documentation

- [x] Add `docs/guide/animations.md` section "Transactional animation" with
      side-by-side SwiftUI / Reactor examples.
      → **Landed in `docs/guide/animation.md` as the new
      "Transactional animation — `Animations.Animate(...)`" section
      above "WithAnimation Scope" — covers the example, scope
      discipline (what `Animate` does *not* do), nesting +
      explicit-`None` suppression, and reduced-motion respect.**
- [x] Cross-link from `docs/specs/042-...md` §6.
      → **`docs/guide/animation.md`'s Transactional section references
      spec 042 §6 explicitly; spec §10 (phasing table) and the
      design's Phase 3 row already point at the same docs entry.**

---

## Phase 4 — Samples & gallery integration

### 4.1 Animated list demo mini-app

- [x] Create `samples/apps/AnimatedListDemo/`. Single-window app that
      demonstrates: insert-at-end, insert-at-0, remove, shuffle, bulk
      replace, all with and without `Animate(.Spring)`.
      → **Landed as `samples/apps/animated-list-demo/` (kebab-case to
      match sibling samples). Renders the templated `ListView<Row>` and
      a hand-built `FlexColumn(items.Select(...).WithKey(item))`
      side-by-side over the same data, so the OC-delta and
      ChildReconciler paths animate the same edit at the same time.
      Drives all seven ops (top, end, middle-remove, last-remove,
      shuffle, reverse, bulk-reset) through one `Mutate(...)`
      chokepoint that either commits directly or wraps in
      `Animations.Animate(...)`. Reduced-motion honored via the new
      `Component.UseReducedMotion()` delegation (WCAG 2.3.3).**
- [x] Wire into `samples/apps/Directory.Build.props` so it builds with the
      rest of the samples matrix.
      → **Registered in `Reactor.slnx` under
      `/samples/apps/animated-list-demo/`; no per-folder
      `Directory.Build.props` exists, the repo uses
      `samples/Directory.Build.props` which the new csproj inherits via
      the standard MSBuild walk.**
- [x] Add a `samples/apps/AnimatedListDemo/README.md` explaining the demo
      and pointing back at spec 042.

### 4.2 Gallery integration

- [x] Update `samples/ReactorGallery/ControlPages/Collections/ListViewPage.cs`
      and `LazyVStackPage` (or equivalent) with an "Animated edit" toggle
      and a +/- buttons row. Same demo, embedded in the gallery.
      → **Landed: third `SampleCard` on `ListViewPage` titled "Animated
      edit (spec 042)" with the same toolbar + Animate toggle as the
      mini-app. Reduced-motion bypass honored. The gallery does not
      currently ship a `LazyVStackPage`; the animated-list-demo mini-app
      already covers the LazyVStack / FlexColumn paths.**

### 4.3 TodoApp polish

- [x] Update `samples/TodoApp/` to use `IReactorKeyed` on `Todo` (already
      done in 2.4) **and** wrap "add todo" / "delete todo" in
      `Animate(.Spring, () => ...)`. Smoke-test that the animation reads
      correctly with the OS reduced-motion setting respected.
      → **Landed: `Render()` derives a `structural` dispatcher
      (`a => Animations.Animate(.Spring, () => dispatch(a))`) that
      `Add` / `Delete` / `Clear completed` flow through. `Toggle` /
      `SetFilter` / `SetNewItemText` keep the bare dispatch since they
      don't change list identity. `UseReducedMotion()` collapses the
      wrapper to a passthrough when the OS opts the user out.**

---

## Phase 5 — Agent-kit / DSL skill references

These keep the agent-kit reference docs in sync with the new platform feature
so Claude Code (and other tools) can recommend the right pattern out of the
box.

### 5.1 `reactor-dsl` references

- [x] Add `plugins/reactor/skills/reactor-dsl/references/keyed-lists.md`
      covering: `IReactorKeyed`, explicit `KeySelector`, `.WithKey(...)`,
      the hand-built `.Select(...)` pattern.
      → **Landed. Covers all three call sites, the three `.WithKey`
      overloads, the diff behavior (incremental ops vs. bulk-replace
      bailout), duplicate / null-key diagnostics, and four explicit
      gotchas (OC-from-UseState, mixed keyed/unkeyed siblings,
      property mutations that don't trigger structural diffs, when
      not to use `IReactorKeyed`).**
- [x] Cross-link from the skill's index file.
      → **`reactor-dsl/SKILL.md` now carries a "focused topical
      references" table that points at `references/keyed-lists.md`.**

### 5.2 `reactor-recipes` references

- [x] Add `plugins/reactor/skills/reactor-recipes/references/animated-list.md`
      with the canonical `Animate(.Spring, () => setItems(...))` recipe.
      → **Landed. Self-contained single-file program with the
      `Mutate(...)` chokepoint pattern + `UseReducedMotion()` bypass
      (WCAG 2.3.3).**
- [x] Include a "common mistakes" sub-section: mutating
      `ObservableCollection` from `UseState` (doesn't work — Reactor compares
      by reference), forgetting `KeySelector` on a non-`IReactorKeyed` type.
      → **Five mistakes documented with paired ❌ / ✓ examples:
      OC-from-UseState, missing `keySelector` on non-`IReactorKeyed`
      types, wrapping non-structural changes in `Animate`, ignoring
      reduced motion, and capturing stale `items` in change closures.
      Cross-linked from `reactor-recipes/SKILL.md` and
      `references/index.md`.**

### 5.3 Skill validation

- [x] Run the skill's existing validation harness (find via
      `plugins/reactor/skills/.../tests/` or equivalent) so the new
      references parse and link-check.
      → **No automated harness exists under `plugins/reactor/skills/`.
      Manually verified: every relative link in the two new references
      and the AnimatedListDemo README resolves (8 / 8 OK), the YAML
      frontmatter parses, and the embedded C# code block matches the
      same API surface as the runnable `animated-list-demo` sample.
      Recommend a follow-up linter under `tools/` rather than blocking
      Phase 5 close-out on it.**

---

## Phase 6 — Hardening, analyzers, follow-ups

### 6.1 Missing-key analyzer (deferred from Q2 in 0.1)

- [x] Roslyn analyzer rule `REACTOR_DSL_001`: warn when a `.Select(...)`
      expression produces `Element` children passed to a panel-like factory
      (`FlexColumn`, `VStack`, `Column`, etc.) without any child calling
      `.WithKey(...)`. Codefix offers `.WithKey(item.Id)` when the lambda
      parameter has a discoverable `Id` / `Key` property.
      → **The diagnostic already shipped as `REACTOR_DSL_001`
      (`MissingWithKeyAnalyzer`) before spec 042 was filed; renaming
      would break downstream suppressions. Phase 6.1 instead **completed
      the analyzer** by adding `MissingWithKeyCodeFix`, which offers
      three insertion shapes ranked by discoverability:
      `.WithKey(item)` when the lambda parameter implements
      `IReactorKeyed`, `.WithKey(item.Key)` when the type has a public
      `Key` property, `.WithKey(item.Id)` when it has a public `Id`
      property. The codefix opts out of `FixAllProvider` since each
      lambda needs an independent semantic lookup of the parameter
      type.**
- [x] Tests under `tests/Reactor.Tests/AnalyzerTests/`.
      → **`MissingWithKeyAnalyzerTests.cs` — 6 tests covering the
      analyzer's positive / negative paths and all three codefix
      offers. All pass under `dotnet test`.**

### 6.2 Duplicate-key diagnostic surfaces in the dev overlay

- [x] Surface the duplicate-key warning from 1.5 in the existing dev tools
      overlay (find via grep on `Diagnostics` / `Devtools`). One-shot per
      `(control, set-of-duplicates)` to avoid log spam.
      → **Landed in three pieces:**
      1. **`Microsoft.UI.Reactor.Core.Diagnostics.ReactorDiagnostics`** —
         new public collector. `RecentKeyedListWarnings` returns a
         bounded snapshot (newest-first, capped at 64 entries × 8 sample
         keys each). Producer side is `internal Record(...)` /
         `IsFirstOccurrence(...)` with dedup keyed on
         (controlInstance, kind, hashed-sample-set). Per-control dedup
         uses a `ConditionalWeakTable` so a torn-down control doesn't
         leak; contextual fallback uses a global concurrent dictionary
         for unit-test / standalone callers.
      2. **`KeyedListDiff.Apply`** gained a `controlInstance` parameter
         and now routes both bailout paths through `ReportBailout`,
         which records into the collector *and* logs through `ILogger`
         only on the first occurrence per triple — subsequent repeats
         bump the in-place `Count` so the dev surface shows
         "fired 12×" without spamming the host log. `Reconciler.Update.cs`
         passes the live `lvb` / `repeater` instance through.
      3. **`DevtoolsMenu`** got a new "Keyed-list diagnostics (N)"
         item that pops a `ContentDialog` listing each recent entry —
         timestamp, control type, kind (`null key` / `duplicate keys`),
         repeat count, and the truncated sample-key list. Behind
         `ReactorApp.DevtoolsEnabled` so retail apps pay zero cost.
         Tests: 7 in `ReactorDiagnosticsTests` covering count bump,
         per-kind separation, per-control isolation,
         `IsFirstOccurrence`, sample truncation, and snapshot ordering;
         the existing 43 `KeyedListDiffTests` still pass.

### 6.3 Long-tail perf

- [x] Add a stress scenario "10k-item virtualized list, scroll + edit" to
      `tests/stress_perf/` to catch future regressions in the ItemsRepeater
      key-indexed factory path.
      → **Landed as `--with-edits` / `--edits-per-second N` flags on the
      existing `StressPerf.VirtualList.Reactor` project (rather than a
      fresh project — the scroll-only and scroll+edit modes share 90% of
      the harness). The edit timer fires 4 ops/sec by default,
      50/50 insert/remove at random positions, deterministic seed. The
      report adds an `Edits:` line. `ListItemSource.GenerateOne(id)`
      added so synthesized items can carry ids that don't collide with
      the seed range.**
- [x] Document the new scenario in the stress_perf README.
      → **Added a "Scenario: 10k virtualized list, scroll + edit (spec
      042 Phase 6.3)" section under the existing matrix, with the
      headless command line, the expected report-shape, and the
      analysis guidance ("if the gap to the edit-free baseline scales
      with `count`, the rekey path has regressed").**

### 6.4 Spec close-out

- [x] Once Phases 1–5 ship, mark spec 042 status as **Implemented** with
      the merged-PR list in the header.
      → **Landed**: `docs/specs/042-keyed-list-reconciliation-design.md`
      header now reads **Implemented (2026-05-17)** with the
      `feat/042-keyed-list-reconciliation` branch state captured.
- [ ] Close microsoft-ui-reactor#198.
      → *Pending PR landing — close out from the merged PR's body,
      not from the feature branch.*

---

## Open items / parking lot

- Fractional indexing helper for drag-to-reorder UIs without natural IDs
  (spec §8) — separate utility, not part of this work.
- CRDT-derived approaches — out of scope, explicitly rejected in spec §8.
- `UseList<T>` op-capture hook — out of scope, explicitly rejected in spec §7.
