# Fully Extensible Control Model — Phase 4 (close-out) Implementation Tasks

Derived from: `docs/specs/047-extensible-control-model.md` (§14 "Phase 4 — cleanup",
§8, §9, §11.6 / §11.7, §15.6 / §15.7) and the Phase 3 completion tracker in
[`047-extensible-control-model-implementation.md`](047-extensible-control-model-implementation.md).

> **Status:** Phase 3 complete (PR #440). Every production element type either
> routes through V1 dispatch (75 arms), is a composition primitive intentionally
> above the protocol (8 arms), or sits in the explicit reachable-but-deferred
> carve list (12 arms). A|B parity (V1 ON ≡ V1 OFF) holds across the full matrix:
> 9134 xunit + 4410 selftest, 0 failures on both flags.
>
> **Phase 4 is the final close-out.** It (a) closes the 12 reachable-but-deferred
> arms so 100% of the V1-reachable surface is registered, (b) flips
> `UseV1Protocol` ON by default and makes it the production path, (c) lands the
> §8 echo-suppressor elimination and §9 `EventHandlerState` split, (d) lands the
> §11.7 bucketed `Element` base and the §11.6 hard byte gates, (e) deletes the
> legacy `MountXxx`/`UpdateXxx` switch arms and **all A|B testing dead code**,
> (f) graduates the public author surface out of `[Experimental]` and locks it,
> and (g) closes every deferred perf/validation gate (ARM64 ratification, AOT
> publish, macro catch-up).
>
> **No deferrals inside this close-out.** Each task below ships within Phase 4.
> See "Explicitly out of scope" at the end for the two items intentionally left
> for follow-up (source generation §7, and the physical `Reactor.Controls.*`
> package split §1.1) with rationale — Phase 4 only guarantees both are
> *unblocked*, not executed.

## Conventions

- Every task is a checkbox; mark `[x]` only when its artifact (code + tests +
  doc update, or captured perf result committed under `docs/specs/047/...`) is
  landed and verified.
- **The A|B parity bar is the safety net for the whole phase.** Until §4.6
  deletes the legacy arms, every PR must keep V1 ON ≡ V1 OFF green on the full
  xunit + selftest matrix. Once a legacy arm is deleted (§4.5), its element is
  V1-only and the parity check for that element retires with it.
- Perf-gated tasks capture results on the Phase 0/2 baseline machine
  (`LAPTOP-4MEP83VI`, ARM64-native, Release, stable-AC) per the §15.5 runbook,
  committed under `docs/specs/047/phase4-results/`.
- **Order matters — and the legacy arms must die before the old machinery
  does.** The legacy `MountXxx`/`UpdateXxx` arms still call
  `ChangeEchoSuppressor` and use the monolithic `EventHandlerState`. So you
  cannot *delete* `ChangeEchoSuppressor.cs` or the `EventHandlerState` struct
  while those arms (or the V1-OFF escape path) still exist — it would fail to
  compile or force wasted migration of soon-to-be-deleted code. The required
  sequence is:
  1. **§4.0** — close the 12 reachable-but-deferred arms (100% registration).
  2. **§4.1** — flip `UseV1Protocol` ON by default.
  3. **§4.5** — delete the legacy registered arms and remove the V1-OFF path for
     them (this strands `ChangeEchoSuppressor` / `EventHandlerState` to V1-only
     consumers).
  4. **§4.2 / §4.3** — *then* replace + delete `ChangeEchoSuppressor` and split +
     delete `EventHandlerState` on the surviving V1 path. (The *new* per-control
     tolerance metadata and the per-control `ControlEventStateBox` can be built
     earlier in parallel; only the **deletions** are gated on §4.5.)
  5. **§4.4** — bucketed base + byte gates; **§4.6** — flag/A|B dead-code
     removal; **§4.7** — surface lock; **§4.8/§4.9** — docs + perf.
  Each of §4.2/§4.3/§4.4 gates on its own perf budget before its deletion step.

## Phase 4 exit gate (all must hold)

1. 100% of the V1-reachable surface (87 arms) is registered and routes through
   V1; the 8 composition primitives are the only legacy `MountXxx` arms left.
2. `UseV1Protocol` is ON by default (production path); the feature flag, the
   `registerBuiltinHandlers` internal ctor, the `REACTOR_USE_V1_PROTOCOL`
   env-var plumbing, the `StressPerf.ReactorV2` / `BlankReactorV2` A|B project
   duplicates, and the dual-flag selftest harness are deleted.
3. `ChangeEchoSuppressor.cs` is deleted; echo handling lives in per-control
   tolerance/coercion metadata + the ColorPicker shim; `WriteSuppressed` keeps
   its public signature.
4. `EventHandlerState` is split per §9.2 (`ModifierEventHandlerState` +
   per-control `ControlEventStateBox`); M10 shows the EHS-allocation drop.
5. The §11.7 bucketed `Element` base ships; the §11.6 hard byte gates pass
   (≤ Today × 0.4 on M1/M2/M3 measured per §11.6, not the stale §14 estimates).
6. The public author surface is out of `[Experimental("REACTOR_V1_PREVIEW")]`,
   documented as stable in `docs/guide/`, and KD-4 (external typed-event
   surface) is closed so a separate assembly can author a multi-event control
   without `InternalsVisibleTo`.
7. ARM64 stable-AC ratification capture lands and clears §13 Q1 / §15.6 budgets;
   AOT publish (1.17 / L13 / L14) and macro catch-up (1.18 / L2/L3/L4/L6) are
   green on the baseline machine(s).
8. Full xunit + selftest + solution build green; the §15.6 regression budgets
   hold against the `ReactorToday` baseline.

---

## 4.0 Close the 12 reachable-but-deferred dispatch arms

Source: the "Path to 100% reachable" list in the Phase 3 tracker
(`047-extensible-control-model-implementation.md` §"Quantified V1 dispatch
coverage"). These must land **before** the flip (§4.1) so turning V1 ON by
default does not silently change behavior for any element. Each sub-task keeps
A|B parity (V1 ON ≡ V1 OFF) green for the newly-registered element.

### 4.0.1 Overlay / dialog family (7 arms) — modal-lifecycle decorator strategy

`ContentDialog`, `Flyout`, `Popup`, `MenuBar`, `MenuFlyout`, `CommandBar`,
`CommandBarFlyout`. These are control-side-mounted (modal lifecycle), not
parent-tree-mounted, so they need a decorator strategy variant beyond the
`IDecoratorElementHandler` shape used for `IconElement`.

- [ ] Design + ship the modal-lifecycle decorator strategy (engine extension):
      a children/host strategy that mounts the overlay's content into the
      control-owned slot (`ContentDialog.Content`, `Flyout.Content`,
      `Popup.Child`, menu `Items`, command bar `PrimaryCommands`/
      `SecondaryCommands`) and tears it down on dismiss/unmount.
- [ ] Port `ContentDialogElement` (primary/secondary/close button content +
      `Opened`/`Closing`/`PrimaryButtonClick`/`SecondaryButtonClick` events) to a
      descriptor or hand-coded handler; register in `RegisterV1BuiltInHandlers`.
- [ ] Port `FlyoutElement`, `PopupElement` (single-content overlays;
      `Opened`/`Closed`).
- [ ] Port `MenuBarElement`, `MenuFlyoutElement` (items hosts with nested
      menu items + `Click` per item).
- [ ] Port `CommandBarElement`, `CommandBarFlyoutElement` (primary/secondary
      command collections).
- [ ] Selftest fixtures `Desc_*`/handler tests for all 7; A|B parity green V1
      ON ≡ V1 OFF; verify modal open/dismiss + descendant component-state
      preservation across re-render.

### 4.0.2 `NavigationHostElement` — cleanup-path refactor

Per-instance route/cache/transition state is intercepted in
`Reconciler.UnmountRecursive` **before** the V1 dispatch arm.

- [ ] Internal-expose `MountNavigationHost` / `UpdateNavigationHost` and wrap as
      a V1 handler (route/cache/transition state owned by the handler's
      per-control payload).
- [ ] Duplicate (or relocate) the `UnmountRecursive` cleanup logic into the V1
      handler's Unmount so the pre-dispatch intercept can be removed.
- [ ] Remove the `UnmountRecursive` intercept; register in
      `RegisterV1BuiltInHandlers`.
- [ ] Selftest: navigation push/pop/back-stack + cache eviction parity V1 ON ≡
      V1 OFF; verify no leaked state across re-mount.

### 4.0.3 `TabViewDescriptor` — gap closure

Descriptor exists but registration is carved (bisect ratified the documented
gaps are hot in the docking suite). Closing needs engine work.

- [ ] Engine: **post-children mount-hook** so `SelectionChanged` subscribes
      after children are added (avoids spurious selection echo at mount).
- [ ] Engine: `.ImperativeBridged` named-slot support for `TabStripHeader` /
      `TabStripFooter` Element slots.
- [ ] Port the spec 045 §2.4 docking drag pipeline trampolines
      (`OnTabDragStarting` / `OnTabDragCompleted`) into the descriptor.
- [ ] Port spec 045 §2.2 pinnable headers (`BuildTabHeader` / `BuildPinButton`
      / in-place `TryUpdatePinHeaderInPlace`).
- [ ] Port conditional `SelectedIndex` write + in-place `CanUpdate` for tab
      content (preserve focus/state on re-render).
- [ ] Register `TabViewDescriptor`; re-run the docking selftest suite (DockHooks
      / PixDoc / RoleAware / Composition / FloatRoot) 3× clean V1 ON; A|B parity
      green.

### 4.0.4 `GridViewDescriptor` — CCC virtualization lifecycle

Descriptor exists but the `ItemsHost<>` strategy pre-mounts every item (no
virtualization); the legacy `MountGridView` uses
`ItemsSource = Range(0..N) + ItemTemplate + ContainerContentChanging` for lazy
realization. Production memory/lifecycle would silently regress.

- [ ] Choose and ship one: a hand-coded `GridViewHandler` mirroring
      `ListViewHandler`'s CCC virtualization, **or** a reusable
      `RecyclingItemsHost<>` ChildrenStrategy that wraps the
      `ItemsSource` + `ContainerContentChanging` realization contract (preferred
      if it can also back other lazy items hosts).
- [ ] Re-point `GridViewDescriptor` at the virtualizing strategy; register in
      `RegisterV1BuiltInHandlers`.
- [ ] Selftest: a GridView-at-scale fixture (≥ a few hundred items) asserting
      lazy container realization (only realized containers mounted), to lock the
      lifecycle that the current A|B fixtures don't stress.

### 4.0.5 `XamlHostElement` / `XamlPageElement` — registration unification

`XamlHostDescriptor` / `XamlPageDescriptor` exist but stay unregistered because
`XamlInterop.Register(reconciler)` populates the external `_typeRegistry` at
startup; auto-registering V1 would clash via `EnsureRegistrableElementType`.

- [x] Decide the single ownership path: either V1 auto-registration owns the two
      interop element types (and `XamlInterop.Register` stops populating
      `_typeRegistry` for them), or `XamlInterop.Register` becomes a V1-handler
      registration. Avoid the duplicate-registration throw.
      **Decision: V1 auto-registration owns them** (`RegisterDecoratorHandler`
      for `XamlPageElement`/`XamlHostElement` in `RegisterV1BuiltInHandlers`);
      `XamlInterop.Register` is now idempotent (skips types already registered via
      new `Reconciler.IsElementTypeRegistered`), so it stays a safe public API.
- [x] Register the two interop descriptors via the chosen path; remove the
      `_typeRegistry` clash.
- [x] Selftest: XAML interop host/page mount + interop bridge parity V1 ON ≡
      V1 OFF. (`Hosting_XamlInteropRegister` green both flags; xunit
      `XamlInteropTests` + `V1OnRegistrationTests` green, +3 new V1-ON tests.)

### 4.0.6 Coverage verification

- [ ] Re-derive the dispatch-coverage table: confirm 87/87 V1-reachable arms are
      registered (75 → 87) and only the 8 composition primitives remain on the
      legacy switch.
- [ ] Full xunit + selftest matrix green V1 ON ≡ V1 OFF at 100% registration
      (this is the last A|B parity checkpoint before the flip).

---

## 4.1 Flip `UseV1Protocol` ON by default

Source: spec §14 Phase 4 ("the production swap"). Gated on §4.0 complete.

- [ ] Change the default in `Reconciler` ctor (`Reconciler.cs:287-290`) from
      `UseV1Protocol = false` to `true` when neither the explicit ctor flag nor
      the AppContext switch is set.
- [ ] Update the AppContext-switch semantics: the switch (and explicit ctor
      flag) now exists only as an **escape hatch to turn V1 OFF** during the
      legacy-deletion window (§4.5); once §4.5 deletes the legacy arms, OFF is
      no longer a valid runtime state and the flag is removed (§4.6).
- [ ] Run the full xunit + selftest suite with the new default; confirm green.
- [ ] Capture an advisory perf snapshot at the flip (production default) to
      anchor the §4.9 ratification baseline.

> Note: between §4.1 and §4.5, V1 OFF still functions (legacy arms not yet
> deleted) so a regression can be bisected by flipping the flag. After §4.5,
> the flip is permanent and the flag is gone.

---

## 4.2 §8 — eliminate `ChangeEchoSuppressor`

Source: spec §8 (Resolved §13 Q3) + the audit
`docs/specs/047/audits/begin-suppress-audit.csv` (**24 call sites**). Phase 1
KD-1 (`OnCustomEvent` drains `ChangeEchoSuppressor.ShouldSuppress`) migrates
here.

> **Ordering:** the per-control tolerance/coercion metadata + ColorPicker shim
> can be built before §4.5, but **deleting `ChangeEchoSuppressor.cs` is gated on
> §4.5** (legacy arms still call `BeginSuppress`/`ShouldSuppress`).
>
> **Counts are from the audit CSV** (24 rows): `eliminable-tight-diff` 12 +
> `defensive-redundant` 1 = **13 trivial deletions**; `coercion` 4 +
> `float-precision` 4 = **8 tolerance sites**; `items-coercion` 2; and 1
> `user-state-races-render` (ColorPicker). The spec §8 prose table cites
> `eliminable-tight-diff: 14`, which disagrees with the CSV's 12 — reconcile in
> the §4.4 spec-hygiene task; the CSV is the source of truth.

- [ ] **Trivial deletions (13 sites).** Delete the `BeginSuppress` call at the
      12 `eliminable-tight-diff` rows + the 1 `defensive-redundant` row
      (`AutoSuggestBox.Text`) per the audit CSV. Each is already covered by the
      element-prop diff / handler-side `lastFired != tag.X` check.
- [ ] **Coercion + float-precision metadata (8 sites).** Add per-control
      tolerance/coercion metadata to the descriptor/handler: NumberBox/Slider
      declare `coercedBy: [Minimum, Maximum]`; the 4 float-precision sites
      declare a numeric tolerance (match today's `AreNumberBoxValuesEquivalent`).
      Engine records "expected Y, suppress one echo for Y ± tolerance."
- [ ] **`items-coercion` (2 sites).** `CalendarView.SelectedDates` keeps a
      per-control imperative shim (diff semantics don't generalize); fold the
      existing `.CollectionDiffControlled` per-element suppression into the shim
      so it no longer depends on `ChangeEchoSuppressor`.
- [ ] **`user-state-races-render` (1 site — ColorPicker).** Replace the
      suppressor with a per-handler `expectedColor` capture + tolerance compare.
- [ ] **Re-implement `ReactorBinding<T>.WriteSuppressed` (§13 Q19).** Swap its
      body off `ChangeEchoSuppressor.BeginSuppress` onto the per-control
      tolerance/coercion mechanism. **Signature unchanged** — existing callers
      and external authors are source-compatible.
- [ ] **Migrate KD-1.** The interim `ShouldSuppress` drain inside
      `ReactorBinding<T>.OnCustomEvent` / `.HandCodedControlled` /
      `.CoercingOneWay` trampolines moves to the descriptor-declared echo shape.
- [ ] **Delete `ChangeEchoSuppressor.cs`** and the `EchoSuppressCount` field on
      `ReactorState` (§11.3 −4 bytes) — **after §4.5**. Confirm no remaining
      references.
- [ ] **Validation.** M9 (`Update_AllChanged`) + the §15.8 Q3 correctness pair
      (`Echo_Coercion_Slider`, `Echo_UserStateRacesRender`) + M13
      (`Setters_Suppression_Scope`, callback count = 0) all pass. No new echo
      regressions in the value-bearing selftest fixtures (ToggleSwitch, Slider,
      NumberBox, ColorPicker, ComboBox, PasswordBox, AutoSuggestBox, CalendarView).

## 4.3 §9 — split `EventHandlerState`

Source: spec §9 + the `EventHandlerState` field audit (Phase 0 deliverable 0.2).

> **Ordering:** the new `ModifierEventHandlerState` + per-control
> `ControlEventStateBox` can be built before §4.5, but **deleting the monolithic
> `EventHandlerState` struct is gated on §4.5** (legacy arms still use it).

- [ ] Introduce `ModifierEventHandlerState` holding only the WinUI true-routed
      event family (pointer / key / tap / focus / context / manipulation / drag);
      lives on `ReactorState`, allocated lazily (null until a routed-input
      modifier is wired).
- [ ] Move control-intrinsic (plain CLR) events out of the shared struct into
      per-control payloads stored in `ReactorState.ControlEventState`
      (`ControlEventStateBox` with `HandlerType` discriminator + `Payload`),
      per §9.2. Reuse the existing per-control payload classes in
      `ControlEventPayloads.cs` (already used by descriptors / hand-coded
      handlers) — the discriminator matches regardless of which shape authored
      the mount (§9.2.1).
- [ ] **Define + test the pool event-state lifecycle precisely.** Specify
      whether native event subscriptions are unsubscribed on return, retained
      with reset payloads, or re-wired on rent — the current pool deliberately
      preserves trampolines to avoid double-subscribe, so the §9.2 reset contract
      must not reintroduce issue #114. Implement: `Pool.Return` clears the
      `ControlEventState` payload (without re-subscribing); `Pool.Rent` asserts
      null/resets; handler `Mount` stamps a fresh box with its `HandlerType`;
      `Update` reads only after the stamp compare. Assert **no duplicate native
      event subscriptions** across rent/return cycles.
- [ ] Cover the four §9.2 hazards with tests: pool reuse (no previous-tenant
      state), handler override (stale-`HandlerType` → deterministic reset, not
      `InvalidCastException`), hot-reload type-identity change (reset across the
      version boundary), and dual-RCW idempotency (return is idempotent, no
      double-clear).
- [ ] **Verify** the `AddRawRoutedHandler` escape hatch (§9.5 / Q11) on
      `MountContext`/`UpdateContext` (already present in
      `src/Reactor/Core/V1Protocol/MountContext.cs`) survives the split and is
      covered by a `handledEventsToo` test (child Handled-marks `KeyDown`, parent
      `.OnKeyDownAny` still fires).
- [ ] **Delete the monolithic `EventHandlerState` struct** once all events route
      through the split — **after §4.5**.
- [ ] **Validation.** M10 (`EventHandlerState_Alloc`) shows the headline drop
      (≈424 B → ≈32 B per-control table; `ModifierEHS` not allocated for the
      common case). M11 (`ModifierEHS_Frequency`) confirms < 20% of elements in
      a representative 1000-element tree allocate `ModifierEventHandlerState`.
      Routed-event bubbling fixture (§9.3) green.

## 4.4 §11.7 bucketed `Element` base + §11.6 hard byte gates

Source: spec §11.6 / §11.7 + §15.6 ("§11.6 targets become hard gates at
cleanup"). The byte targets depend on §4.2 (echo) + §4.3 (EHS split) + the
bucketed base landing.

- [ ] Bucket the 14–16 cross-cutting nullable `Element` base fields
      (`Attached`, `ThemeBindings`, `ImplicitTransitions`, `ThemeTransitions`,
      `LayoutAnimation`, `AnimationConfig`, `ElementTransition`,
      `InteractionStates`, `StaggerConfig`, `KeyframeAnimations`,
      `ScrollAnimation`, `ConnectedAnimationKey`, `ResourceOverrides`,
      `ContextValues`) into a single nullable `ElementExtensions` sub-record
      (mirroring spec 034's `ElementModifiers`). In the lean case
      (`Extensions == null`) the base shrinks from ~128 B to ~16 B (only `Key`
      and `Modifiers` survive at the root).
- [ ] Migrate all readers/writers of the bucketed fields to the sub-record
      (factory methods, fluent modifiers in `ElementExtensions.cs`, reconciler
      apply pipelines). Preserve external behavior; no API break to authors.
- [ ] **Land the §11.6 hard byte gates** as merge-blocking on M1/M2/M3, measured
      per §11.6 (`Target = min(Direct + 100, ReactorToday × 0.4)` — i.e. the
      measured ≤407 / ≤1520 / ≤19200, **not** the stale §14 ≤100/≤320/≤500
      estimates).
- [ ] **Spec hygiene:** update spec §14 "Phase 4 — cleanup" to cite the measured
      §11.6 targets instead of the stale `≤100 / ≤320 / ≤500`, and fix the
      §15.6 "Phase 5 cleanup" reference to read "Phase 4" (this spec has no
      Phase 5).
- [ ] **Validation.** M1/M2/M3 pass the hard gates on the baseline machine;
      L4/L5 working-set within the §15.6 budgets; M7 (no-change update) ≤ Today.

## 4.5 Delete the legacy `MountXxx` / `UpdateXxx` switch

Source: spec §14 Phase 4 ("Delete the private switch"). Gated on §4.0 (100%
registration) + §4.1 (flip) being stable.

- [ ] Delete the legacy `MountXxx` / `UpdateXxx` arms in `Reconciler.Mount.cs` /
      `Reconciler.Update.cs` for **every element registered through V1** (the 87
      reachable arms). Keep only the 8 composition-primitive arms
      (`Component`, `Func`, `Memo`, `ErrorBoundary`, `CommandHost`,
      `Validation.FormField` / `ValidationVisualizer` / `ValidationRule`) and the
      `ModifiedElement` unwrap at the top of `Mount` (not a switch arm).
- [ ] Delete the now-unreachable dispatch fallthrough (the `else` legacy switch
      branch) once no registered element relies on it; the dispatch becomes
      V1-registry → external `_typeRegistry` → composition-primitive switch.
- [ ] Remove any internal helpers that only the deleted arms used (dead-code
      sweep — `ApplyDefaultAutomationName` variants, legacy per-control wiring
      helpers, etc., that the V1 handlers don't call).
- [ ] **Validation.** Full xunit + selftest green (V1-only now — A|B parity no
      longer applicable for deleted arms); solution build green; no orphaned
      `internal` members flagged by the analyzer / unused-symbol pass.

## 4.6 Remove A|B testing dead code

The A|B harness existed only to diff V1 ON vs V1 OFF on one binary. With V1 the
production default and legacy arms deleted, all of it is dead.

- [ ] Remove the `Reactor.UseV1Protocol` AppContext switch read, the
      `public bool UseV1Protocol` property, and the `useV1Protocol` ctor
      parameters from `Reconciler` (`Reconciler.cs:250-296, 568`). V1 is
      unconditional.
- [ ] Remove the internal `Reconciler(logger, useV1Protocol, registerBuiltinHandlers)`
      A|B ctor and the `registerBuiltinHandlers` plumbing; built-in handler
      registration is unconditional. (Verify the Phase 2 descriptor-vs-handler
      harness that used `registerBuiltinHandlers: false` is also removed — it was
      a measurement-only path.)
- [ ] Remove the `REACTOR_USE_V1_PROTOCOL` env-var mapping in
      `tests/Reactor.AppTests.Host/Program.cs:11-22`.
- [ ] Remove the dual-flag selftest harness (the runner path that executes
      fixtures under both flags) and any `Desc_`-vs-legacy A|B comparison
      scaffolding now that there is one path.
- [ ] Delete the A|B perf project duplicates: `tests/stress_perf/StressPerf.ReactorV2`
      and `tests/startup_perf/BlankReactorV2` (and `StressPerf.VirtualList.ReactorV2`
      if it landed for 1.18). Fold their scenarios back into the primary
      `StressPerf.Reactor` / `BlankReactor` — `ReactorV2` is now `Reactor`.
      Update the perf aggregator (§15.6) so it compares `Direct` /
      `ReactorToday(historical baseline)` / `Reactor(current)` without a live V2
      variant.
- [ ] Delete or repurpose the V1-flag-specific test files:
      `tests/Reactor.Tests/Spec047/V1Protocol/V1FeatureFlagTests.cs`,
      `Ports/V1OnRegistrationTests.cs` (keep behavior tests that are still
      meaningful with V1 always-on; delete the ones asserting the flag/OFF
      behavior).
- [ ] Remove the `tools/spec047-phase1-checkpoint/` A|B checkpoint runner if it
      only exercised the flag.
- [ ] **Validation.** Solution builds with zero references to `UseV1Protocol` /
      `REACTOR_USE_V1_PROTOCOL` / `ReactorV2` (grep clean); full suite green.

## 4.7 Graduate + lock the public author surface

Source: Phase 1 exit gate item 5 (surface marked provisional; lock after Phase 2
decision) — Phase 2 decided, so Phase 4 locks it. Includes KD-4.

- [ ] Remove `[Experimental("REACTOR_V1_PREVIEW")]` from the public V1 surface
      (`IElementHandler<,>`, `MountContext` / `UpdateContext`,
      `ReactorBinding<T>`, `ControlDescriptor<,>` + builder methods,
      `RegisterType` / `RegisterHandler` / `RegisterHandlerForDerivedTypes`,
      pool-policy API, `WriteSuppressed`, `AddRawRoutedHandler`). The surface is
      now stable / supported.
- [ ] **Close KD-4 — external typed-event surface.** Ship the public typed-event
      wiring so an external assembly can author a multi-event control (the
      `.HandCodedControlled` / `.HandCodedEvent` per-descriptor `TPayload`
      shape, or `OnCustomEvent` with a pool-safe deduped trampoline) **without
      `InternalsVisibleTo`** on Reactor internals. This is the last gap that
      keeps the external path below first-party quality (and the precondition
      for the §1.1 library split being unblocked).
- [ ] **Activate / retire the compile-time validation analyzers (§13 Q10).**
      `REACTOR1001` (`StringEventReferenceAnalyzer`) and `REACTOR1003`
      (`ControlledReadBackTypeAnalyzer`) are still documented no-ops "until
      Phase 2" (`src/Reactor.Compile.Analyzer/*.cs`). Q10 requires compile-time
      validation to be real, not a runtime failure. Either **activate** the rule
      bodies (flag string-form event/property typos + controlled read-back type
      mismatches as compile errors) with "should-fail" analyzer-test fixtures,
      **or** prove they are obsolete because the final descriptor API is fully
      strongly-typed (no string-form references remain) and remove the reserved
      no-op rules + their fixtures. Document the decision.
- [ ] Verify the external-assembly proof (Phase 1 gate item 2) still passes with
      the locked surface: a control hosted in a separate assembly, registered via
      public API, exercising value writes / events / modifiers / setters /
      pooling / child reconciliation, with `PublishTrimmed=true` +
      `IsAotCompatible=true` and zero new trim/AOT warnings.

## 4.8 Documentation — final author-facing surface

Source: spec §14 Phase 4 ("Document the final author-facing surface in
`docs/guide/`"). Remember the guide docs under `docs/guide/` are generated from
`docs/_pipeline/templates/*.md.dt` via `mur docs compile` — edit the templates.

- [ ] Promote `docs/guide/extensibility-preview.md` from "provisional" to the
      stable author guide (or rename to `extensibility.md`): drop the
      breaking-change warning, document V1 as the default/only path, remove the
      "enabling the V1 path / off by default" section.
- [ ] Document the final authoring decision tree (§6.1.1): descriptor
      `.OneWay` / `.Controlled` / `.HandCodedControlled` / `.HandCodedEvent` /
      the engine shapes (`.Imperative` / `.ImperativeBridged` / `.OneWayBridged`
      / `.CollectionDiffControlled`) vs. hand-coded `IElementHandler<,>`; the
      children strategies (`SingleContent` / `Panel` / `NamedSlots` /
      `ItemsHost` / `TemplatedItems(Erased)` / `TreeChildren` / `TabItemsHost` /
      `PreMountedItems` / `Imperative`); the pool policy (§13 Q18); echo handling
      via tolerance/coercion metadata (post-§4.2).
- [ ] If any edits touch generated guide pages, edit the `.md.dt` templates and
      re-run `mur docs compile`; verify the compiled output matches.
- [ ] Update `AGENTS.md` for the post-Phase-4 reality: the "Adding a new WinUI
      control requires four touch points" section (the Element-record +
      Mount/Update-switch instructions describe the deleted legacy path — replace
      with the V1 descriptor model as the primary path), the "Echo suppression
      for value controls" section (`ChangeEchoSuppressor` is deleted — describe
      the per-control tolerance/coercion metadata + `WriteSuppressed`), and any
      event-state / per-element-state conventions that referenced the monolithic
      `EventHandlerState` (now `ModifierEventHandlerState` + per-control
      `ControlEventStateBox`). Sweep for any other stale guidance pointing at the
      removed machinery.

## 4.9 Perf validation, ratification, and deferred-gate close-out

Source: spec §15.6 / §15.7 Phase 4 row, Phase 1 deferrals 1.17 / 1.18 / 1.19,
and the still-pending ARM64 stable-AC ratification gate (§14 Phase 3 finish).

- [ ] **ARM64 stable-AC ratification capture.** Run the §15.3 micro suite
      (M1–M13) on `LAPTOP-4MEP83VI` ARM64-native, Release, with **randomized /
      interleaved variant ordering**, cooldowns, and CPU-clock telemetry to
      defeat the thermal drift that made the prior attempt inconclusive. Commit
      under `docs/specs/047/phase4-results/LAPTOP-4MEP83VI/`. Must clear the §13
      Q1 thresholds and the §15.6 budgets.
- [ ] **1.17 — AOT publish + L13 / L14.** AOT publish the split-library scenario
      with `PublishTrimmed=true` + `IsAotCompatible=true`; zero new trim/AOT
      warnings. L13 (mixed-tree, ≥50% external-assembly element types ≤ +10% vs
      all-in-core) and L14 (same scenario, AOT binary) pass.
- [ ] **1.18 — macro suite catch-up.** Ship/refresh the L2 / L3 / L4 / L6
      scenarios on the (now single) production `Reactor` variant and capture on
      the baseline machine(s).
- [ ] **§15.6 regression budgets — final pass.** All metric classes within
      budget vs. the `ReactorToday` historical baseline: per-element alloc
      (M1–M3, must improve/equal), dispatch (M4–M6 ±10%), update (M7 ±5% / M8
      ≤+10%), TTFF (L1–L3 ≤+5%), working set (L4 ≤+2% / L5 ≤+5%), FPS
      (L6–L8 p95 ≤105%), GC pauses (L9 ≤ baseline), heap stability (L11 ±10%).
- [ ] Confirm KD-3 (dispatch fast-path for ported built-ins) stays closed at the
      full registration scope (advisory showed M4/M5 net negative — wins from a
      fatter handler table). Fold the M1 leading-`if` binder check into the
      pattern-switch `case` arm (the Phase 3-finish note flagged this as the
      Phase 4 perf-tuning item) if M1 is still above budget after §4.3 / §4.4.

## 4.10 Final close-out checklist

- [ ] Phase 4 exit gate (top of file) items 1–8 all satisfied.
- [ ] Update the main tracker
      (`047-extensible-control-model-implementation.md`) and spec §14 status
      line to "Phase 4 complete — migration closed; V1 is the production path."
- [ ] CI green: unit tests + selftests + full solution build (the standard PR
      gate) on `windows-latest`, .NET 10.
- [ ] Final dead-code sweep: no `UseV1Protocol`, `REACTOR_USE_V1_PROTOCOL`,
      `ReactorV2`, `registerBuiltinHandlers`, `ChangeEchoSuppressor`, or
      `EventHandlerState` (monolith) references remain.

---

## Explicitly out of scope for the close-out (with rationale)

These two items are marked "future / deferred" in the spec and are **not**
required to finish the V1 migration. Phase 4 only guarantees both are
*unblocked*. (Scope was raised with the requester; proceeding on the documented
spec defaults while awaiting any override.)

1. **Source generation (§7).** Spec §7 status + §13 Q1 reopen condition: source-
   gen is deferred with **no committed timeline**, gated on external triggers
   (WinUI→Reactor cycle-time pain, a new AOT-strict target, or compile-time
   descriptor validation need). It is a constant-factor perf enhancement on top
   of the hand-coded/descriptor model that Phase 2 already ratified; it changes
   no §13 decision and is not needed for V1 parity, cleanup, or the byte gates
   (the §11.6 hard gates are met by §9 split + bucketed base + echo elimination
   without it). **Decision: keep deferred.** When a trigger fires it plugs into
   the descriptor shape (generator emits descriptors/payload classes from
   `[ReactorControl]` attributes) and must match/beat the Phase-4 hand-coded
   numbers without regressing any settled §13 question.

2. **Physical `Reactor.Controls.*` package split (§1.1).** §1.1 is the
   *motivation* (the external path becomes the first-party path); the actual
   carving of ~half the catalog into separate packages is a large, independent
   packaging effort with its own versioning/release implications. Phase 4 makes
   it **unblocked** — the public surface is locked and stable (§4.7), KD-4 closes
   the external typed-event gap (§4.7), and L13/L14 prove a separate assembly can
   author controls with no `InternalsVisibleTo` under trim/AOT (§4.9).
   **Decision: follow-up effort.** No correctness or parity work in the migration
   depends on executing the split.

## Carry-forward known defects (status entering Phase 4)

- **KD-1** (`OnCustomEvent` drains `ChangeEchoSuppressor`) — migrated in §4.2.
- **KD-3** (dispatch fast-path for ported built-ins) — materially closed at
  registration scale (M4/M5 net negative); §4.9 confirms and folds the residual
  M1 binder-check cost into the pattern switch.
- **KD-4** (public typed-event surface for external authors) — closed in §4.7.
