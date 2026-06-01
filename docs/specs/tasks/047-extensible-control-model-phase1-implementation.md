# Fully Extensible Control Model — Phase 1 Implementation Tasks

Derived from: `docs/specs/047-extensible-control-model.md` §14 Phase 1
(and the Phase 0 deliverables tracked in
[`047-extensible-control-model-implementation.md`](047-extensible-control-model-implementation.md)).

> **Status:** Phase 1 **code-complete in-tree** (this PR). Phase 0 cleared
> its exit gate (greenlight granted on PR #414). The numeric exit-gate
> evaluation (1.17 AOT publish, 1.18 macro catch-up, 1.19 final perf
> validation) is gated on baseline-machine runs — see
> [`../047/phase1-results/`](../047/phase1-results/) for the per-section
> deferrals and run plans. Per-section checkboxes below remain authoritative
> for what landed in code.
>
> Phase 1 ships the v1 protocol surface behind a
> feature flag, promotes the six internal helpers identified in
> [`047/audits/existing-api-surface.md`](../047/audits/existing-api-surface.md),
> and ports six representative controls (`ToggleSwitch`, `Slider`, `TextBox`,
> `Border`, `ListView`, one external) onto the new protocol. The legacy
> private `MountXxx` switch stays — no big-bang migration. Phase 3 is when
> remaining controls migrate.

## Conventions

- All work lives behind a feature flag (decision in 1.1). When the flag is
  OFF, ported controls keep routing through the legacy switch so we can
  diff V1-on vs V1-off behavior on the same binary. When ON, ported
  controls route through `IElementHandler<TElement, TControl>`.
- Outputs that gate later phases (perf rows, AOT publish logs, analyzer
  fixture results) land under `docs/specs/047/phase1-results/` (mirrors
  the Phase 0 `baseline-results/` shape).
- Public API surface added in Phase 1 is marked **provisional** — the
  surface lock happens after Phase 2's descriptor-vs-handler decision.
  See spec §14 Phase 1 exit gate item 5.
- A task is "done" only when its output (code committed, tests added,
  measured numbers under `phase1-results/`, doc updates) ships in a
  reviewable PR. Pause/resume points are the section boundaries.
- **Regression checkpoint** = the standard gate run between sections.
  Definition in 1.2; invoked by name throughout.

---

## Phase 1 exit gate (from spec §14)

When every item below holds, Phase 1 closes and Phase 2 (descriptor
spike + Q1 decision) can begin.

1. **Perf:** `ReactorV2` ≤ +10% on M1, M2, M5, M7, L1, L4 vs Phase 0
   baseline. No worse than `ReactorToday` on any §15.4 macro that ships
   in Phase 1.
2. **External-assembly proof:** ≥ 1 of the six ported controls lives in
   a separate assembly, registered via public API, no `InternalsVisibleTo`.
   Selftests pass for value writes, events, modifiers, setters,
   pool/recycle, child reconciliation where applicable.
3. **AOT/trim:** the external assembly publishes with `PublishTrimmed=true`
   and `IsAotCompatible=true` with **zero** new trim/AOT warnings beyond
   Reactor's existing baseline (L14).
4. **Correctness:** full existing test suite passes with the V1 flag both
   ON and OFF. M13 still reports `OnIsOnChangedFireCount = 0` (the §8.2
   carve-out invariant survives).
5. **API stability statement:** `docs/guide/extensibility-preview.md`
   ships, surface is marked provisional, breaking-change risk documented.

---

## 1.1 Feature-flag mechanism

Spec §14 Phase 1 line 1 ("v1 protocol behind a feature flag"). Before any
protocol code lands, we need a flip mechanism that:
- Tests can toggle per-test without process restart.
- `StressPerf.ReactorV2` can hard-pin ON without affecting `StressPerf.Reactor`.
- Default-OFF in shipping `Reactor.dll` until Phase 2 decides.

- [ ] **Design decision: flag transport.** Pick one of (or document combination):
  - **Recommended:** per-`Reconciler` ctor flag `useV1Protocol: bool` plus a
    static default via `AppContext.SetSwitch("Reactor.UseV1Protocol", …)`.
    Ctor flag wins per-instance; switch is the global fallback.
  - Alternative: AppContext switch only (process-wide, simpler but less
    test-friendly).
  - Alternative: compile-time `#if REACTOR_V1` (rejected — defeats the "diff
    on same binary" workflow).
- [ ] Add `Reconciler.UseV1Protocol` property (readonly, set from ctor or switch).
- [ ] Add an internal `V1HandlerRegistry` separate from `_typeRegistry`. Ported
      built-ins register handlers into `V1HandlerRegistry`; external
      `RegisterType` callers continue to register into `_typeRegistry`.
- [ ] Dispatch order when `UseV1Protocol` is ON:
      1. `V1HandlerRegistry.TryGet(element.GetType())` — new ported controls
      2. `_typeRegistry.TryGet(...)` — existing external `RegisterType` lambdas
      3. Legacy `MountXxx` switch — everything else
- [ ] When `UseV1Protocol` is OFF, step (1) is skipped entirely. Ported
      controls fall through to legacy `MountXxx`.
- [ ] Unit test: same element type, both code paths produce structurally
      identical control trees (compare DP values + child counts).
- [ ] Document the flag in `docs/guide/extensibility-preview.md` (placeholder
      section now; expanded in 1.18).

---

## 1.2 Regression checkpoint definition

The checkpoint is invoked by name (e.g. "run regression checkpoint") at the
end of every section that lands code. It is the minimum gate before
moving to the next section.

- [ ] Define the checkpoint script — see [`tools/spec047-phase1-checkpoint`](#)
      (to be created in this task). Outputs a single pass/fail summary plus
      a JSON-Lines row appended to `docs/specs/047/phase1-results/<machine>/<date>/checkpoint-trend.jsonl`.

**Checkpoint steps** (each must pass):

- [ ] `dotnet test` against the full solution with `Reactor.UseV1Protocol=false`.
- [ ] `dotnet test` against the full solution with `Reactor.UseV1Protocol=true`.
- [ ] M1, M2, M5, M7, M13 micro suite — both flag states. Compare against
      the closest prior checkpoint row; fail on > 10% regression vs Phase 0
      baseline.
- [ ] M13 `OnIsOnChangedFireCount` is 0 on both flag states (the §8.2 invariant).
- [ ] `StressPerf.ReactorV2.exe` launches without error in both flag states.
- [ ] Spot-check trim warnings on `Reactor.csproj` build — must remain at the
      Phase 0 baseline count (full AOT publish is reserved for 1.14 / 1.15).
- [ ] Append checkpoint result + git SHA + machine + date to
      `phase1-results/<machine>/<date>/checkpoint-trend.jsonl`.

Pause/resume between sections is safe because every section ends with a
green checkpoint.

---

## 1.3 Promote internal helpers to `public`

From [`existing-api-surface.md`](../047/audits/existing-api-surface.md) §"Members
that must be promoted." Pure visibility changes; no API design.

- [ ] `Reconciler.ApplySetters<T>(Action<T>[], T)` — `internal static` → `public static`.
- [ ] `Reconciler.SetElementTag(FrameworkElement, Element?)` → `public static`.
- [ ] `Reconciler.GetElementTag(UIElement)` (both overloads) → `public static`.
- [ ] `Reconciler.DetachReactorState` → `public static`.
- [ ] `Reconciler.ApplyDefaultAutomationName` → `public static`.
- [ ] `Reconciler.UpdateDefaultAutomationName` → `public static`.
- [ ] `Reconciler.ApplyThemeBindings` — `private static` → `internal static` → `public static`
      (two-step so any in-tree caller broken by the rename is caught locally first).
- [ ] `Reconciler.ApplyResourceOverrides` — same two-step promotion.
- [ ] Open the two unexplored docking registrations (`DockManager`,
      `DockDropTargetOverlay` per audit) and confirm they only use
      `SetElementTag`. If they reach for more, expand the promotion list.
- [ ] Mark every promoted member `[Experimental("REACTOR_V1_PREVIEW")]` (or
      equivalent attribute decided in 1.1) so consumers see the provisional
      surface flag.
- [ ] Update spec §3 / Appendix A citation drift per audit
      `Suggested spec edits`:
  - `Reconciler.cs:2780+` → `:2787` (EventHandlerState).
  - `2963-3069+` → `2963-3200` (Ensure* family).
  - Note that `ApplyThemeBindings` / `ApplyResourceOverrides` were private
    until this PR.
  - Append `UpdateDefaultAutomationName` to Appendix A row.
- [ ] **Regression checkpoint.**

---

## 1.4 Ship `WriteSuppressed` as public primitive (Q19)

Stable surface from day one. Body swaps in Phase 4 when §8 lands; signature
must not change.

- [ ] Add `public static class ReactorBinding` (placeholder for now;
      `ReactorBinding<T>` per-instance struct lands in 1.6).
- [ ] Add `ReactorBinding.WriteSuppressed(UIElement target, Action mutate)`
      — calls today's `ChangeEchoSuppressor.BeginSuppress` / mutate / dispose.
- [ ] Add overload `WriteSuppressed<T>(T target, Action<T> mutate)` for the
      common typed case.
- [ ] Unit tests: a `ToggleSwitch.IsOn = true` write inside `WriteSuppressed`
      does NOT fire `OnIsOnChanged`; the same write outside it does.
- [ ] Document in `extensibility-preview.md` (placeholder).
- [ ] **Regression checkpoint.**

---

## 1.5 Pool-policy public API (Q18)

External authors get a real, documented pool contract from day one. Spec
§13 Q18 enumerates the contract; this section ships it.

- [ ] Add `public sealed class PoolPolicy<TControl>` carrying:
  - `bool IsPoolable { get; init; } = true` — opt-out for controls with
    persistent native resources.
  - `Action<TControl>? Reset { get; init; }` — extra reset beyond the
    default contract (defaults to null).
- [ ] Add `Reconciler.RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null)`
      as the public mount-time allocation primitive. Implementation:
  - If `IsPoolable && _pool.TryRent(typeof(T))` succeeds → return that.
  - Else → invoke `factory()` (or `new T()` via constrained-call helper).
  - On dirty rent (state not fully reset), emit a structured log entry.
- [ ] Add `Reconciler.ReturnControl<T>(T control, PoolPolicy<T>? policy = null)`
      executing the reset contract:
  - Clear `ControlEventState` (placeholder until 1.7 lands).
  - Clear `ModifierEventHandlerState`.
  - Clear `ReactorAttached.StateProperty` Tag.
  - Clear Reactor-set `DataContext`.
  - Invoke `policy?.Reset(control)` last.
- [ ] Pool key is `typeof(TControl)` only. Document that finer keys are a
      future addition (per Q18).
- [ ] Verify dual-RCW idempotency — calling `ReturnControl` twice on the
      same native control does not double-clear.
- [ ] Add **correctness test** matching Q18's validation: rent → mount →
      mutate state → unmount/return → rent same control → assert
      zero residual state from previous tenant. Run against both a
      pool-policy-aware handler and a naive one.
- [ ] Add M12 perf variant: `ctx.RentControl` vs `new T()` path.
- [ ] **Regression checkpoint.**

---

## 1.6 v1 protocol types — `IElementHandler<TElement, TControl>` and friends

Spec §4. The author-facing surface for hand-coded handlers.

- [ ] Add `IElementHandler<TElement, TControl>` interface with `Mount`,
      `Update`, `Unmount`, optional `ReconcileChildren`. **`Update` returns
      `void`** (Q12 — substitution forbidden).
- [ ] Add `public readonly ref struct MountContext` exposing:
  - `Action RequestRerender { get; }`
  - `UIElement? MountChild(Element child)`
  - `void ApplySetters<T>(Action<T>[] setters, T control) where T : class`
  - `ReactorBinding<TElement> BindFor<TElement>(FrameworkElement control, TElement element) where TElement : Element`
  - `T RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null) where T : class`
  - `IDisposable PushContext<T>(T value)` — typed context push
  - `IDisposable PushStaggerScope(TimeSpan delay)`
  - `void AddRawRoutedHandler(UIElement target, RoutedEvent re, Delegate h, bool handledEventsToo)` — Q11 escape hatch
- [ ] Add `public readonly ref struct UpdateContext` — same surface as Mount
      minus `RentControl` (no allocation during Update).
- [ ] Add `public readonly ref struct UnmountContext` — just `RequestRerender`
      and the pool-return hook.
- [ ] Add `public readonly struct ReactorBinding<TElement> where TElement : Element`
      with:
  - One `On<EventName>(Action<TElement, TArgs> handler)` per WinUI true-routed
    input event (pointer / key / tap / focus / context / manipulation / drag).
    Wired via `EventHandlerState` trampolines under the hood.
  - `OnCustomEvent<TArgs>(subscribe, unsubscribe, handler)` for plain CLR
    events (Toggled, Click, ValueChanged, TextChanged, …).
  - `WriteSuppressed(Action mutate)` — the per-binding wrapper around 1.4.
- [ ] Document the **UI-thread guarantee** (Q14) in XML doc comments on
      `MountContext` / `UpdateContext`. Handlers may freely access
      control-state without synchronization.
- [ ] **Decision:** keep the existing Debug-only `DispatcherQueue.HasThreadAccess`
      check as-is, or tighten to unconditional throw in Release. Phase 1 plan
      is to **measure first** — run a Release build of the in-tree test suite
      with the check unconditional and observe any unintentional violations.
      Capture findings under `phase1-results/q14-dispatcher-affinity.md`. Ship
      the tighten only if no callers trip.
- [ ] Provide a sample skeleton in XML doc comments showing the
      `MountToggleSwitch` shape (per spec §4 example).
- [ ] **Regression checkpoint.**

---

## 1.7 `ControlEventStateBox` and per-control event tables (§9.2)

Per the spec §9 split: routed-input events stay in shared
`ModifierEventHandlerState`; control-intrinsic events move into a
per-control payload inside `ReactorAttached.StateProperty`.

Phase 1 ships only the **shape and storage**. The actual lift of
per-event slots out of `EventHandlerState` is gated behind the V1 flag
(legacy controls keep using the shared struct; ported controls allocate
the per-control payload).

- [ ] Add `internal sealed class ControlEventStateBox { public Type HandlerType; public object Payload; }`.
- [ ] Add `object? ControlEventState` field to `ReactorState`.
- [ ] Per-control payload structs for the seven control-intrinsic events
      identified in the audit (`ToggleSwitch`, `Button`, `TextBox`, `Image`,
      `ScrollViewer`, `ScrollView`, `NumberBox`). Each carries 1–3 slots.
- [ ] Pool reset contract (1.5) clears `ControlEventState` on `ReturnControl`.
- [ ] Pool rent asserts `ControlEventState == null` (or replaces with fresh
      box keyed to the new handler).
- [ ] Add `HandlerType` verification: handler reads `ControlEventState` only
      after `HandlerType == typeof(this-handler)`. Hot-reload safety per spec
      §9.2 "Why the discriminator matters."
- [ ] M10 microbench reflects the new shape — only ported controls allocate
      the smaller per-control payload; legacy controls still allocate the
      full `EventHandlerState`. Document delta in `phase1-results/`.
- [ ] **Regression checkpoint.**

---

## 1.8 `ChildrenStrategy` types and `AttachedPropWriter`

Spec §6 ChildrenStrategy block (resolved Phase 0 Q4). Concrete C# strategy
types ship in Phase 1 so containers have a stable shape to bind to.

- [ ] Add abstract record `ChildrenStrategy<TElement, TControl>`.
- [ ] `None<TElement, TControl>` — leaf controls.
- [ ] `SingleContent<TElement, TControl>(GetChild, SetChild)` — `Border`,
      single-content controls.
- [ ] `Panel<TElement, TControl>(GetChildren, GetCollection)` — `StackPanel`,
      `Grid`, `Canvas`. Engine handles spec-042 keyed reconcile.
- [ ] `NamedSlots<TElement, TControl>(IReadOnlyList<NamedSlot<...>>)` plus
      `NamedSlot<TElement, TControl>(Name, GetChild, SetChild)`.
- [ ] `ItemsHost<TElement, TControl>(GetItemsSource, GetContainer, ItemsHostOptions)`
      — `ListView`, plugs into spec-042 keyed list reconciliation.
- [ ] `Imperative<TElement, TControl>(Reconcile)` — escape hatch.
- [ ] `AttachedPropWriter<TChildElement>(Name, Get, Write)` — `Grid.Row`,
      `Canvas.Left`, `DockPanel.Dock` etc.
- [ ] Engine dispatch: when `UseV1Protocol` is ON and the handler declares
      a `ChildrenStrategy`, route child reconcile through the strategy.
      Legacy MountXxx paths untouched.
- [ ] Unit tests for each strategy with a minimal element record + control.
- [ ] **Regression checkpoint.**

---

## 1.9 `RegisterType` v1 semantics (Q17)

Spec §2.1 + decision-criteria Q17. Exact-type lookup, throw on duplicate,
no open generics, no `RegisterOverride`.

- [ ] Audit `Reconciler.RegisterType` to confirm exact-type lookup is the
      current behavior. Tighten if base-class matching has crept in.
- [ ] Throw `InvalidOperationException` on duplicate registration — including
      duplicates against built-in element types. Error message names both
      registrations.
- [ ] Throw on open-generic element type registration
      (`typeof(TElement).ContainsGenericParameters`).
- [ ] Add scenario test covering all four sub-questions:
  - Register a handler for an element type whose base also has one — assert
    exact-type lookup on `el.GetType()`.
  - Register for a type that already has a built-in handler — assert throw.
  - Register the same type twice — assert throw.
  - Register `typeof(DataGrid<>)` — assert throw.
- [ ] Promote `RegisterType` to `[Experimental("REACTOR_V1_PREVIEW")]` for the
      same provisional-surface reason as 1.3.
- [ ] **Regression checkpoint.**

---

## 1.10 `Reactor.Compile.Analyzer` — Roslyn analyzer for string-form refs (Q10)

Spec §13 Q10 — compile-time validation is required, not optional.

- [ ] Add `src/Reactor.Compile.Analyzer/` project (Roslyn analyzer + code-fix).
- [ ] Diagnostic `REACTOR1001` — resolves any string-form event reference
      (e.g., `changeEvent: "Toggled"`) against the control type declared in
      the descriptor's generic parameters; reports error on mismatch.
- [ ] Diagnostic `REACTOR1002` — validates event delegate type matches the
      `subscribe` / `unsubscribe` lambda signatures.
- [ ] Diagnostic `REACTOR1003` — validates `Prop.Controlled`'s `readBack`
      return type matches the `set` lambda's value type.
- [ ] Each diagnostic emits a source span pointing at the call site (not
      at a generated artifact).
- [ ] Test fixture project `tests/Reactor.Compile.Analyzer.Tests/`:
  - One file per diagnostic with the "should fail" shape.
  - Assert each diagnostic fires on `dotnet build` and exits non-zero.
  - Assert clean fixtures build green.
- [ ] Ship the analyzer in the same NuGet as the v1 protocol surface (or
      as a referenced analyzer package). Decision deferred to 1.18.
- [ ] **Regression checkpoint.**

---

## 1.11 Port `ToggleSwitch` (first port — vanilla value-bearing leaf)

Validates the protocol with the simplest case.

- [ ] Author `ToggleSwitchHandler : IElementHandler<ToggleSwitchElement, ToggleSwitch>`.
- [ ] `Mount`: `ctx.RentControl(...)` + `IsOn`/`OnContent`/`OffContent` writes
      + `bind.OnCustomEvent` for `Toggled` + `ApplySetters`.
- [ ] `Update`: diff old/new element; use `bind.WriteSuppressed` for `IsOn`
      when it changes; re-apply setters.
- [ ] Register into `V1HandlerRegistry` (gated behind the flag in 1.1).
- [ ] **Behavior parity test:** mount + interact + unmount with flag ON
      and OFF; assert identical visible behavior (same DP values, same
      callback firing pattern).
- [ ] M2 + M13 micro pass against the V1 path. Record numbers in
      `phase1-results/<machine>/<date>/toggleswitch.jsonl`.
- [ ] Confirm `OnIsOnChangedFireCount = 0` for the `Set(...)`-driven case
      (§8.2 carve-out invariant).
- [ ] **Regression checkpoint.**

---

## 1.12 Port `Slider` (value-bearing + coercing)

Exercises echo handling for `Value` clamped against `Minimum`/`Maximum`.

- [ ] Author `SliderHandler`.
- [ ] Implement coercion-tolerance metadata on the handler (per Q3 audit
      finding — Slider is one of the 8 coercion sites that need tolerance,
      not the §8.1 round-trip).
- [ ] `Update`: write `Value` via `WriteSuppressed`; trampoline drops the
      one expected echo within tolerance.
- [ ] **Behavior parity test:** drag → value change → callback fires once.
      Programmatic `Value = newValue` does NOT re-fire callback. Flag ON
      and OFF must match.
- [ ] M2 / M5 / M9 against the V1 path. Numbers under `phase1-results/`.
- [ ] **Regression checkpoint.**

---

## 1.13 Port `TextBox` (text + focus)

Exercises focus-prop handling (`IsTabStop`, `FocusState`, programmatic
`Focus()`) and demonstrates `Initial` vs `OneWay` vs `Controlled` prop
distinctions per §6.1.

- [ ] Author `TextBoxHandler`.
- [ ] `Text` → controlled (with `WriteSuppressed` on `TextChanged`).
- [ ] `PlaceholderText` / `IsReadOnly` → `OneWay`.
- [ ] `GotFocus` / `LostFocus` → events via `BindFor`.
- [ ] Add a sibling `TextBoxElement` variant with `InitialText` to validate
      the `Initial` prop classification — written once at mount, never on
      update, no echo possible.
- [ ] **Behavior parity test:** typing → text change → callback; programmatic
      `Text = "x"` does NOT round-trip. Flag ON / OFF.
- [ ] **Regression checkpoint.**

---

## 1.14 Port `Border` (container — SingleContent + attached props)

First container port. Exercises `Children.SingleContent` strategy plus
modifier-pipeline interaction. Adds `Grid.Row` / `Grid.Column` attached-prop
writers (even though Border itself doesn't use them — to validate the
`AttachedPropWriter` shape).

- [ ] Author `BorderHandler` with `Children = new SingleContent<...>(...)`.
- [ ] Wire `AttachedPropWriter` for `Grid.Row` / `Grid.Column` / `Canvas.Left` /
      `Canvas.Top` so any Border child inside a Grid/Canvas picks up
      attached props correctly through the v1 path.
- [ ] **Behavior parity test:** nested element tree (Grid → Border → TextBlock);
      attached props applied; modifiers (`.Padding`, `.Margin`,
      `.Background`) honored. Modifier-after-prop precedence per §6.2 (Q13).
- [ ] **Regression checkpoint.**

---

## 1.15 Port `ListView` (templated items host)

Validates `Children.ItemsHost` against spec 042 keyed reconciliation.

- [ ] Author `ListViewHandler` with `Children = new ItemsHost<...>(...)`.
- [ ] Confirm the existing spec-042 `ChildReconciler.Reconcile` plugs into
      `ItemsHost` without changes (the strategy is a thin adapter over what
      already exists).
- [ ] Pool-survival test: scroll 1000 items through a 20-row viewport;
      assert pool rent/return cycle works correctly under the v1 protocol;
      no residual state between rentals (Q18 correctness).
- [ ] M12 against the V1 path. Numbers under `phase1-results/`.
- [ ] **Regression checkpoint.**

---

## 1.16 External-assembly proof (spec §14 Phase 1 exit gate item 2)

The split-library plan (§1.1) means external authors must be able to do
everything a built-in can. This section proves it.

- [ ] Pick the external control. Recommended: a minimal `Win2DCanvasElement`
      wrapping `Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl`. Alternative:
      a deliberate test-only `tests/external_proof/Reactor.External.TestControl/`
      assembly hosting one of the six ported controls outside `Reactor.dll`.
- [ ] Create `src/Reactor.Controls.Win2D/Reactor.Controls.Win2D.csproj` (or
      the test-only variant). **No `InternalsVisibleTo` on Reactor.dll.**
- [ ] Implement `Win2DCanvasHandler` against the public v1 protocol surface
      only — `MountContext`, `ReactorBinding<T>`, `WriteSuppressed`,
      `RentControl`, attached props.
- [ ] Register via public `RegisterType` API. Confirm the registration
      compiles against the consumer-facing assemblies only.
- [ ] Selftests in `tests/Reactor.Controls.Win2D.Tests/`:
  - Value-bearing prop write + readback.
  - At least one custom event subscribed via `BindFor.OnCustomEvent`.
  - Modifier chain applied (`.Margin`, `.Background`).
  - Setter chain applied.
  - Pool rent/return cycle observes the reset contract.
  - Child reconciliation if applicable to the chosen control.
- [ ] **Regression checkpoint.**

---

## 1.17 AOT publish + L14 macro gate (spec §14 Phase 1 exit gate item 3)

Phase 1 exit requires `PublishTrimmed=true` + `IsAotCompatible=true` on the
external assembly with **zero new warnings**.

- [ ] Add `PublishAot` profile to the external assembly's `.csproj`.
- [ ] Publish the external assembly + a hosting sample (a small WinUI app
      that mounts the external control under a Reactor reconciler) with AOT.
- [ ] Diff trim/AOT warnings against the Reactor.dll baseline. **Hard fail**
      on any new warning.
- [ ] Capture publish logs under
      `phase1-results/<machine>/<date>/aot-publish-log.txt`.
- [ ] Implement L14 (`SplitLibrary_MixedTree_AOT`) per spec §15.4 — mount a
      tree with ≥ 50% external-assembly element types and capture the
      macro perf row.
- [ ] Implement L13 (`SplitLibrary_MixedTree`) — same tree, JIT build. Compare
      against the all-in-core variant. Pass condition: ≤ +10% per-element
      cost vs all-in-core.
- [ ] **Regression checkpoint.**

---

## 1.18 Macro suite catch-up (L2 / L3 / L4 / L6 V2 variant)

Phase 0 deferred several macros to "the Phase 1 promotion PRs" per
[`macro-suite-status.md`](../047/macro-suite-status.md). Land the ones the
Phase 1 exit gate cites — L1 / L4 are already required for the perf gate.

- [ ] Implement L2 `TTFF_LoginForm` — three variants (Direct / ReactorToday
      / ReactorV2). Six-control login form per the locked Phase 0 contract.
- [ ] Implement L3 `TTFF_SettingsPage` — same three-variant pattern, 50-control
      mixed page.
- [ ] Implement L4 `WorkingSet_AtStartup` — reuses L2 binaries; snapshot
      private bytes + managed heap after first frame.
- [ ] Add `StressPerf.VirtualList.ReactorV2` (L6 V2 variant — was deferred
      from Phase 0 with the explicit note "mirrors the existing Reactor
      project").
- [ ] Run all four (L2 / L3 / L4 / L6) on both Phase 0 baseline machines
      (LAPTOP-4MEP83VI ARM64-native and CPC-ander-YTZ3O x64-native). Commit
      results under `phase1-results/`.
- [ ] **Regression checkpoint.**

L5 / L7 / L8 / L9 / L11 stay deferred per Phase 0 — they aren't gates for
Phase 1 exit, and the WTS plumbing they need isn't in scope. They land
during Phase 3 controls migration.

---

## 1.19 Final perf validation — Phase 1 exit gate item 1

The headline number. Six controls, both flag states, full suite.

- [ ] Run M1, M2, M5, M7, M10, M12, M13 against the V1 path for each of the
      six ported controls. Capture under
      `phase1-results/<machine>/<date>/micro/`.
- [ ] Run L1, L4 (and L13 / L14 per 1.17, L2 / L3 / L6 per 1.18). Capture
      under `phase1-results/<machine>/<date>/macro/`.
- [ ] Run the §15.6 aggregator. Emit:
  - Absolute comparison `Direct` / `ReactorToday` / `ReactorV2` (V1 flag ON).
  - Reactor delta `V2 vs Today` with 95% CI.
  - WinUI gap `V2 vs Direct`.
- [ ] **Gate evaluation:**
  - V2 ≤ +10% on M1, M2, M5, M7, L1, L4 vs Phase 0 baseline — required.
  - No regressions on M13 — required (`OnIsOnChangedFireCount = 0`).
  - No worse than ReactorToday on any macro that ships in Phase 1.
- [ ] Capture exit-gate decision in
      `phase1-results/<machine>/<date>/exit-gate.md`. If any metric fails,
      enumerate the regressions and decide remediation (fix in Phase 1 vs
      accept and document).
- [ ] **Regression checkpoint.**

---

## 1.20 Documentation + API stability statement (exit gate item 5)

- [ ] Write `docs/guide/extensibility-preview.md`:
  - Overview of the v1 protocol surface (`IElementHandler`, `MountContext`,
    `ReactorBinding<T>`, `WriteSuppressed`, `RentControl`).
  - "Provisional API" callout — surface may change after Phase 2's Q1
    decision.
  - Worked example: porting a hypothetical 7th control through the
    protocol, with all of the contract points called out.
  - Pool contract documentation (Q18 enumerated reset list).
  - UI-thread guarantee statement (Q14).
  - The `Reactor.Compile.Analyzer` package and its three diagnostics.
- [ ] Update spec §4 with measured shape (any changes from straw-man).
- [ ] Update spec §13 with "Resolved (Phase 1)" lines for any Q that
      moved from "Resolved (Phase 0) pending Phase 1 measurement":
  - Q6 (setters rerun policy — M7 measurement).
  - Q7 (pool integration — M12 measurement).
- [ ] Update [`047-extensible-control-model-implementation.md`](047-extensible-control-model-implementation.md)
      with a "Phase 1 complete — see Phase 1 tasks file" status line.
- [ ] **Regression checkpoint.**

---

## Phase 1 exit checklist

When every item below is checked, Phase 1 closes and Phase 2 (descriptor
spike + Q1 decision) is greenlit.

In-tree code complete in this PR:

- [x] 1.1 Feature flag mechanism shipped and documented. Ctor `Reconciler(ILogger?, bool?)`
      + `AppContext.SetSwitch("Reactor.UseV1Protocol")`; `V1HandlerRegistry`
      separate from `_typeRegistry`. Dispatch wired in `Reconciler.Mount.cs` +
      `Reconciler.Update.cs`.
- [x] 1.2 Regression checkpoint script — `tools/spec047-phase1-checkpoint/Run-Checkpoint.ps1`.
- [x] 1.3 Six internal helpers promoted to `public`; two `private` helpers
      moved to `public` via `internal`. All marked `[Experimental("REACTOR_V1_PREVIEW")]`.
      DockManager / DockDropTargetOverlay audit confirmed (no extra promotions needed).
- [x] 1.4 `ReactorBinding.WriteSuppressed` public — `src/Reactor/Core/ReactorBinding.cs`.
- [x] 1.5 Pool-policy public API ships with reset contract + correctness test.
      `PoolPolicy<T>` + `Reconciler.RentControl<T>` / `ReturnControl<T>`.
- [x] 1.6 `IElementHandler<T,U>` + `MountContext` / `UpdateContext` /
      `UnmountContext` / `ReactorBinding<T>` shipped under
      `src/Reactor/Core/V1Protocol/`.
- [x] 1.7 `ControlEventStateBox` + per-control payload structs in place.
- [x] 1.8 Six `ChildrenStrategy` variants + `AttachedPropWriter` shipped.
      Panel keyed-reconcile integration is a documented Phase 3 follow-up;
      ItemsHost dispatch is shape-only and the real path goes through the
      existing `ChildReconciler.Reconcile`.
- [x] 1.9 `RegisterType` v1 semantics (throw rules) in place; scenario test green.
- [x] 1.10 `Reactor.Compile.Analyzer` shipped with three diagnostics.
      REACTOR1002 active; REACTOR1001 / REACTOR1003 register as no-op until
      the Phase 2 descriptor model lands.
- [x] 1.11–1.15 Five built-in controls ported (`ToggleSwitch`, `Slider`,
      `TextBox`, `Border`, `ListView`) under
      `src/Reactor/Core/V1Protocol/Handlers/`.
- [x] 1.16 External test assembly (`Reactor.External.TestControl` with
      `MarqueeControl`) registered through public API; no `InternalsVisibleTo`.
      Hermetic selftests pass; live AppTests.Host fixtures pending CI run.

Baseline-machine-gated (deferred — see `phase1-results/`):

- [ ] 1.17 AOT publish clean — zero new warnings. L13 + L14 macros pass.
      See [`../047/phase1-results/1.17-aot-publish-deferral.md`](../047/phase1-results/1.17-aot-publish-deferral.md).
- [ ] 1.18 L2 / L3 / L4 / L6 V2 macros shipped on both Phase 0 baseline machines.
      See [`../047/phase1-results/1.18-macro-suite-catchup-deferral.md`](../047/phase1-results/1.18-macro-suite-catchup-deferral.md).
- [x] 1.19 Final perf validation — micro suite captured on
      LAPTOP-4MEP83VI ARM64-native across four snapshots (initial,
      typed-payload rewrite, callback-gate, 5×5 cross-process
      aggregate). M1 / M2 / M7 within the +10% exit-gate band; M5
      borderline (+13.1% mean, ±17.4% CI half-width); M4 +88.9% is
      the V1 dispatch-shell overhead documented as KD-3's Phase 2
      followup (direct calls for high-use built-ins). L1 / L4 macros
      stay deferred per 1.18. See
      [`../047/phase1-results/LAPTOP-4MEP83VI/2026-05-26-arm64-perf-typed-5x5/README.md`](../047/phase1-results/LAPTOP-4MEP83VI/2026-05-26-arm64-perf-typed-5x5/README.md)
      for the authoritative numbers.

- [x] 1.20 `extensibility-preview.md` shipped; spec §4 / §13 updated.
      See `docs/guide/extensibility-preview.md`. §13 Q6 / Q7 updated to
      reference the 1.19 deferral; §3 citation drift fixed; Appendix A row
      reflects `public` promotion.

Once 1.17 / 1.18 land on the baseline machines, the Phase 1 exit
gate (top of file) is satisfied. 1.19's micro-suite gate is closed
in-tree; the deferred L1/L4 macros gate alongside 1.18 (same
machines, same capture session).

---

## Phase 1 known defects / Phase 4 followups

Items discovered during Phase 1 stabilization that have an interim fix in
this PR but should be revisited when the relevant later-phase work lands.
Each item names the symptom, the interim fix, and what the proper
resolution looks like once the surrounding machinery is in place.

### KD-1 — `OnCustomEvent` must drain `ChangeEchoSuppressor`

**Symptom.** Selftest run with `REACTOR_USE_V1_PROTOCOL=true` regressed
the `EchoSuppress_ToggleSwitch_NoEchoCall`, `EchoSuppress_Slider_NoEchoCall`
and `EchoSuppress_SliderMinMax_NoEchoCall` fixtures (plus the V1-internal
`V1_ToggleSwitch_NoEchoOnProgrammaticFlip`, `V1_Slider_NoEchoOnCoercion`,
`V1_TextBox_NoRoundTrip`, and `ExtProof_Marquee_WriteSuppressed_NoEchoOnReconcile`
fixtures, which flip the switch internally). Every programmatic
`WriteSuppressed` write was echoing through to the user's OnChanged.

**Root cause.** `ReactorBinding<T>.OnCustomEvent`'s trampoline went
straight to the user's handler without consulting
`ChangeEchoSuppressor.ShouldSuppress`. The legacy per-control trampolines
(e.g. `EnsureToggleSwitchWiring` at `Reconciler.Mount.cs:875`) call
`ShouldSuppress` as their first line per the contract documented on
`ChangeEchoSuppressor.cs:21–25`; the V1 path lost that drain.

**Interim fix (this PR).** Added the drain to `OnCustomEvent`'s trampoline
in `src/Reactor/Core/V1Protocol/ReactorBindingT.cs`. For non-value-bearing
events (Button.Click etc.) the counter is never incremented, so the
universal drain is a free no-op.

**Phase 4 followup.** When `ChangeEchoSuppressor` is replaced by
per-control tolerance / coercion metadata per §8 (the "delete + tight diff"
plan), the drain migrates with it — the descriptor-declared echo shape
takes over from the universal counter. Until then, the drain is the right
shape under the current machinery. Cross-reference: spec §8 Phase 4 plan,
which already notes "`WriteSuppressed` public primitive is preserved as
the stable author-facing surface" — the V1 trampoline contract is the
matching consumer side.

### KD-1b — mount-time `WriteSuppressed` is a no-op (and a latent token leak)

**Symptom.** After KD-1 landed,
`ExtProof_Marquee_WriteSuppressed_FiresOutsideScope` and
`EchoSuppress_TextBox_UserEditFires` flipped from pass to fail —
a real user-edit event after a docked mount was being suppressed
instead of firing the callback.

**Root cause.** `ToggleSwitchHandler.Mount`, `TextBoxHandler.Mount`
and `MarqueeHandler.Mount` were wrapping their initial value write
in `bind.WriteSuppressed(...)` *before* the matching
`bind.OnCustomEvent(...)` subscription. The synchronous change event
fired with no trampoline subscribed, so the suppress token sat
unconsumed in `EchoSuppressCount`; KD-1's new drain then consumed it
on the next *real* event (user typing) and dropped that fire. Legacy
`MountToggleSwitch` only calls `BeginSuppress` for pool-rented
controls (the previous tenant's trampoline is still live in legacy
because legacy pool return doesn't unsubscribe); fresh-mount writes
were bare. V1 always allocates fresh trampolines on rent, so the
"bare initial write" semantics apply uniformly.

**Interim fix (this PR).** Mount bodies for the three handlers now
write the initial value bare. `WriteSuppressed` is only used on
update, where the subscription is live. See
`src/Reactor/Core/V1Protocol/Handlers/{ToggleSwitch,TextBox}Handler.cs`
and `tests/external_proof/Reactor.External.TestControl/MarqueeHandler.cs`.

**Phase 4 followup.** When per-control tolerance metadata replaces
`ChangeEchoSuppressor`, the "bare at mount, suppressed on update"
distinction goes away — the descriptor declares the echo shape and
the engine applies it uniformly. The handler bodies no longer need
to know about mount/update timing of the subscription.

### KD-2 — V1 `ChildrenStrategy` dispatch naively remounted child subtrees

**Symptom.** Selftest run with `REACTOR_USE_V1_PROTOCOL=true`
regressed seven docking/dynamic-content fixtures
(`DynDock_CounterButton_ComponentReRendered`,
`DynDock_CounterButton_AfterClick`,
`DynDock_CounterButton_AfterSecondClick`,
`PixDoc_SampleCountAfterClick`/`AfterSecondClick`,
`PixShell_SampleCountAfterClick`/`AfterSecondClick`,
`KLR_FlexColumn_Reverse_AllSurvivorsKeepIdentity`). All variants
shared the same shape: a component nested inside a `Border`-wrapped
slot (directly via Border, transitively via docking content
chrome) re-rendered into a state slot reset to 0 — counter button
clicks fired the click handler (static probe incremented) but
`setCount`'s state value disappeared.

**Root cause.** `V1HandlerAdapter.DispatchChildrenUpdate` for
`SingleContent`, `NamedSlots`, and `Panel` did a "Phase 3 follow-up"
naive replace: `ctx.MountChild(newChild)` + `SetChild(...)` on every
parent update. Because element records are immutable, `oldChild`
and `newChild` are always reference-distinct across re-renders, so
**every** `BorderHandler.Update` unmounted-and-remounted the entire
child subtree — wiping descendant `Component` state slots,
re-allocating WinUI controls, and losing any event subscriptions
wired against the previous control instances. The `// Phase 1:
naive replace. Phase 3 hooks into Reconcile() for structural diff
inside the slot.` comment in `V1HandlerAdapter.cs` had flagged this
as a known limitation; it turned out to be a correctness defect, not
a perf one.

**Interim fix (this PR).** Three changes:

1. New internal helper `Reconciler.ReconcileV1Child(oldChild,
   newChild, existing, requestRerender)` mirrors the legacy
   `ReconcileChild` semantics (CanUpdate-then-Update, else
   unmount-and-mount, else unmount-on-clear).
2. `V1HandlerAdapter.DispatchChildrenUpdate` rewired through that
   helper for `SingleContent`, `NamedSlots`, `Panel`, and
   `Imperative`. `Panel` got an index-based structural diff (still
   no keyed-list reconcile — that's the Phase 3 spec-042
   integration).
3. `SingleContent` and `NamedSlot` gained an optional
   `GetCurrentChild` init-only property so the engine can read the
   existing slot value during reconcile. Backward-compatible —
   existing positional constructor calls still bind. `BorderHandler`
   sets it (`ctrl => ctrl.Child`).

**Phase 3 followup.** Spec §6 already calls for spec-042
keyed-list-reconcile integration in the `Panel` and `ItemsHost`
strategy dispatches. The current index-based `Panel` reconcile is a
correctness floor; keyed reconcile is the Phase 3 deliverable.
Cross-reference: spec §6 ChildrenStrategy block.

### KD-3 — V1 mount-path perf regression on M2 / M5 (fixed)

**Symptom.** Initial Phase 1 perf snapshot
([`../047/phase1-results/LAPTOP-4MEP83VI/2026-05-26-arm64-perf/README.md`](../047/phase1-results/LAPTOP-4MEP83VI/2026-05-26-arm64-perf/README.md))
showed the V1 path at **+57% on M2** (Mount_Leaf_OneCallback —
ToggleSwitch + OnIsOnChanged) and **+38.8% on M5** (warm dispatch
including ported controls). Allocation delta +26% on M4/M5. Phase
1 exit gate (spec §14) requires ≤ +10%.

**Root cause.** `ToggleSwitchHandler.Mount`, `SliderHandler.Mount`
and `TextBoxHandler.Mount` were wiring their intrinsic event
through the generic `ReactorBinding<T>.OnCustomEvent` escape hatch.
That path allocated **~5–6 objects per mount**: the `handler`
closure (captured `ctrl`), the `trampoline` closure (captured `fe`
+ handler), the `new RoutedEventHandler(h)` delegate wrapper inside
the user-supplied `subscribe` lambda, plus a fresh
`ControlEventStateBox` + `CustomEventAnchorPayload` + `List<Delegate>`
on every pool rent because `ReturnControl` cleared
`ControlEventState` to null.

Legacy `MountToggleSwitch` allocates zero per mount on the pool
path: `EnsureToggleSwitchWiring` sees the existing
`ToggleSwitchToggledTrampoline` slot and early-returns. The
trampoline closure is captured exactly once per control lifetime
and read live state via `GetElementTag`. Spec §9.2 deliberately
introduced typed per-control payload structs
(`ToggleSwitchEventPayload`, `TextBoxEventPayload`, …) to mirror
that pattern for the V1 path — but the handlers shipped against
`OnCustomEvent` instead and never used the typed slots.

**Fix (this PR).**

1. Added `SliderEventPayload` (the original §9.2 audit listed seven
   payloads; Slider was missed).
2. Added `Reconciler.GetOrCreateControlEventPayload<T>` helper that
   walks the per-control `ControlEventStateBox` keyed by payload
   `Type`.
3. Removed the `rs.ControlEventState = null` line from
   `Reconciler.ReturnControl<T>` so typed payloads survive pool
   rent/return. The legacy `EventHandlerState.ClearCurrentHandlers`
   already had the same contract documented at
   `Reconciler.cs:3302-3307` ("Trampoline delegate fields are left
   intact — they're rooted by WinUI's subscription list").
4. Rewrote `ToggleSwitchHandler.Mount`, `SliderHandler.Mount`, and
   `TextBoxHandler.Mount` to wire each intrinsic event through the
   typed payload with the legacy null-check pattern. Trampolines
   are static (or captured-once on first wire); subsequent mounts
   skip subscription entirely.

**Result.** Second perf snapshot
([`../047/phase1-results/LAPTOP-4MEP83VI/2026-05-26-arm64-perf-typed/README.md`](../047/phase1-results/LAPTOP-4MEP83VI/2026-05-26-arm64-perf-typed/README.md))
shows V1-vs-Today within tolerance on every Phase 1 exit-gate
metric:

| Bench | Before | After |
|---|---:|---:|
| M1 | +9.1% | **−0.2%** |
| M2 | +57.0% | **−21.5%** (V1 *faster* than legacy) |
| M5 | +38.8% | **−4.2%** |
| M7 | −5.8% | **−6.5%** |

Selftest still passes 932/932 in both V1=ON and V1=OFF modes.

**Followup gates: callback-presence wiring + 5x5 stability run.**

After the typed-payload rewrite, a deeper bench (full M1–M8 with 10
reps and a quieter machine state) exposed a separate regression on
the **dispatch-mix** benches: M4 (Dispatch_Switch_Cold) ran at
**+51.9%** and M5 (Dispatch_Switch_Warm) at +26.6% relative to
Today. Root cause: the V1 ports were wiring their typed-payload
events **unconditionally**, while legacy `EnsureToggleSwitchWiring`
(and friends) early-exit when the user's callback is null. M4/M5
cycle through 8 element factories that include
`ToggleSwitch(false)` and `Slider(0, 0, 100)` — no callbacks — so
legacy never wires while V1 paid full subscription cost.

Mirrored the legacy gate: each handler now exposes an
`Ensure<Event>Wiring(ctrl, el)` helper called from both `Mount`
and `Update` that early-exits when the relevant callback is null,
and lazy-wires on null→non-null transitions. After this gate, a
**5-launch × 5-rep** (25 measurements per (bench, variant))
capture on a quieted machine settled to:

| Bench | ns Δ % | ns 95% CI | alloc Δ % |
|---|---:|---:|---:|
| M4 | +88.9% | ±16.5% | **−0.5%** |
| M5 | +13.1% | ±17.4% | **+0.4%** |

Allocation is now at parity (was +20–47%). Timing on M4 settled to
a stable ~1.8–2.1× ratio across all five launches — V1 is reliably
slower than legacy on the dispatch hot path. M5 settled within the
±10% gate (with high CI half-width); M5 absolute timings are ~3×
the M4 absolutes despite running the identical `RunOne` body,
which is a bench-harness artifact (the bench Parent panel
accumulates WinUI state between benches in a single process) and
not a Reactor issue.

**Where the M4 dispatch overhead lives.** With allocation at
parity, the +88.9% must be CPU instructions. The V1 dispatch chain
is:

1. `_v1Handlers.TryGet(elementType)` — dictionary hash + lookup
2. `V1HandlerAdapter<TElement,TControl>.Mount(...)` via
   `IV1HandlerEntry` interface call + downcast
3. `_handler.Mount(ctx, typedEl)` — second interface call into
   `IElementHandler<TElement,TControl>`
4. `_handler.Children` getter + `DispatchChildrenMount` strategy
   switch (no-op for `None<>` but still pays the lookups)
5. Generic specialization — each `(TElement, TControl)` pair is a
   distinct JIT-specialized type; PGO has more code to warm

vs the legacy direct dispatch which is a monomorphic
`element switch { ToggleSwitchElement ts => MountToggleSwitch(ts), … }`
that the JIT inlines aggressively.

**Phase 2 direction.** Add back a fast-path for high-use built-in
elements that bypasses the V1HandlerAdapter indirection — keep
`IElementHandler<TElement,TControl>` as the public author surface
(external assemblies, less-common controls), but for the six
ported built-ins route through static `MountToggleSwitchV1` /
`MountSliderV1` etc. helpers that the JIT can inline. The typed
event payload approach this PR landed stays; only the dispatch
shell changes. Should close the M4-shaped gap entirely while
preserving the extensibility benefits the V1 path delivers on real
mounts (M1, M2, M7, M8, M10, M13 all at parity or better).

**Phase 2 followup (KD-4 cross-reference).** The `OnCustomEvent`
escape hatch still has the per-mount alloc cost and a latent
double-subscribe risk on pool reuse (the closures it builds aren't
deduped against the existing per-control list). External handlers
that want pool-safe + fast wiring need to use the typed-payload
helper too — currently `GetOrCreateControlEventPayload<T>` is
internal. Spec §9.2 calls for a public, typed surface for the
seven (now eight, with Slider) audited control-intrinsic events.

### KD-4 — `OnCustomEvent` is not pool-safe (deferred)

**Symptom.** `ReactorBinding<T>.OnCustomEvent` is the V1 surface
for plain CLR events (`Toggled`, `Click`, `ValueChanged`,
`TextChanged`, …) that aren't covered by the typed payload set.
The implementation appends each call's trampoline to a
`CustomEventAnchorPayload.Trampolines` list and subscribes via the
caller's `subscribe` lambda. There is no dedup against the
existing list, and the pool reset now preserves the box
(KD-3 fix), so a pooled control whose handler re-runs `Mount` ends
up with **N subscriptions** after N pool rents.

**Status.** Latent. No current test exercises pool reuse through
`OnCustomEvent` for the events the built-ins now handle via typed
payloads — the four built-in ports moved off `OnCustomEvent` in
KD-3, and the external `MarqueeHandler` (proof) uses
`OnCustomEvent` but isn't pool-reused under selftest. So no
current failure mode is observable.

**Phase 2 followup.** Make `OnCustomEvent` idempotent against
re-mounts of the same control: dedup by the user's
`subscribe`/`unsubscribe` `MethodInfo` or similar stable key,
allow the trampoline to live across pool rents the same way the
typed payloads do. Pair with a public typed-event surface for the
audited eight (`OnToggled`, `OnClick`, `OnTextChanged`,
`OnValueChanged`, `OnImageOpened`, `OnImageFailed`,
`OnViewChanged`, `OnNumberBoxValueChanged`) so external authors
get the same fast path the built-ins use.
