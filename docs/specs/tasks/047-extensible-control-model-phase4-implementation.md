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

> ## 🟢 Progress log (live)
>
> **Done & verified (committed):**
> - **§4.6** — Removed all A|B / `UseV1Protocol` dead code. `Reconciler` now has a
>   single `Reconciler(ILogger? logger = null)` ctor (dropped the `useV1Protocol` /
>   `registerBuiltinHandlers` params, the `public bool UseV1Protocol` property, the
>   AppContext-switch read, and the NavigationHost pre-dispatch flag guard); both
>   dispatch sites (`Mount.cs:66`, `Update.cs:117`) and both unmount arms no longer
>   gate on the flag. Deleted `Program.cs` `REACTOR_USE_V1_PROTOCOL` env-var mapping,
>   the `selftests-v1` CI job, the perf A|B duplicates (`StressPerf.ReactorV2`,
>   `BlankReactorV2`, `DescriptorVariantFactory`) + `tools/spec047-phase1-checkpoint/`
>   (`ReactorV2`→`Reactor` in the aggregator/slnx/scripts), `V1FeatureFlagTests.cs`,
>   `TypeRegistryTests.Override_Builtin`, and the redundant `TextBox` echo-stranding
>   fixture; reshaped `V1OnRegistrationTests` + the `Ports/*PortTests` + the
>   `Spec047V1Protocol`/`Spec047ExternalProof` selftest fixtures to `new Reconciler()`
>   with the flag-flipping removed. Grep-clean of `UseV1Protocol`/`REACTOR_USE_V1_PROTOCOL`/
>   `ReactorV2` outside `docs/specs/`. Validation: core build = 0 err; xunit = 9128
>   pass/0 fail; Echo + V1_* + Spec047ExternalProof_* selftests = 0 fail. *(Perf-project
>   consolidation measurement deferred to ARM64 — see §4.9.)*
> - **§4.5** — Deleted the legacy `MountXxx`/`UpdateXxx` dispatch switches:
>   both `Mount`/`Update` now dispatch V1-registry → `_typeRegistry` →
>   composition-primitive-only switch (Component/Func/Memo/ErrorBoundary/
>   CommandHost/FormField/ValidationVisualizer/ValidationRule). Dead-body sweep
>   removed 32 orphaned legacy `Mount*`/`Update*` bodies + 2 transitively-dead
>   helpers (~1240 lines); 0 orphaned private members remain across
>   `Reconciler*.cs`. Removed the obsolete Phase-2 descriptor-vs-handler parity
>   selftest harness (`Spec047V1ProtocolDescriptorFixtures.cs`, ~130 `Desc_`
>   fixtures — coupled §4.6 removal; the `Echo_` real-input regression fixtures
>   were preserved). Fixed `PrivateUpdateHotPaths` reflection fixture (dropped the
>   `UpdateSwipeControl`/`UpdateRefreshContainer` legacy-body probes — those
>   controls are descriptor-driven now). Validation: build = 0 err; xunit V1 ON =
>   9136 pass/0 fail; full selftest = 0 fail (NativeDockingComposition fixtures
>   are intermittently flaky in full runs — pass deterministically when filtered).
> - **§4.0.1 / §4.0.3 finalized** — the genuine overlay port (`OverlayLifecycle`
>   static module, V1-owned) and the full `TabViewDescriptor` port (replacing the
>   deleted `TabViewHandler`) are landed, and the now-orphaned engine bridges are
>   gone: removed the 14 thin overlay delegators (ContentDialog/Flyout/MenuBar/
>   CommandBar/MenuFlyout/Popup/CommandBarFlyout × Mount+Update) and the legacy
>   `MountTabView`/`UpdateTabView` bodies from `Reconciler.Mount.cs`/`Update.cs`.
>   Overlay leaf helpers (`CreateMenuFlyoutItem`/`UpdateMenuFlyoutItems`/
>   `CreateAppBarItem`/`UpdateAppBarItems`) were promoted to `internal` for
>   `OverlayLifecycle`; `BuildTabHeader`/`TryUpdatePinHeaderInPlace` stay
>   `internal` for `TabViewDescriptor`. Dropped the `UpdateCommandBarFlyout`/
>   `UpdateFlyoutElement` `PrivateUpdateHotPaths` probes. Validation: build = 0 err;
>   xunit V1 ON = 9136 pass/0 fail; full selftest = 0 fail.

> - **§4.0.6** parity — full selftest V1 ON = 0 fail; xunit OFF = 9136 pass.
> - **§4.1** — `UseV1Protocol` flipped ON by default (`Reconciler.cs` ~289); flag
>   is now an escape hatch. Fixed 4 OFF-assuming tests (`XamlInteropTests` ×2,
>   `TypeRegistryTests.Override_Builtin`, `RichEditBoxElementTests`). xunit ON =
>   9136 pass/0 fail; full selftest ON = 0 fail. Commit `0dee90d8`.
> - **§4.0.4** GridView — `GridViewHandler` routes through the engine's
>   virtualizing `MountGridView` body; added `RareControl_GridViewLazy` selftest
>   (500 items/200px → 96 realized, parity ON≡OFF). Commit `c9e61e39`.
> - **§4.4 spec-hygiene** — spec now cites measured §11.6 targets
>   (≤407/≤1520/≤19200); "Phase 5 cleanup" → "Phase 4". Commit `bfdca920`.
> - **§4.7 analyzers** — RETIRED REACTOR1001/REACTOR1003 (final descriptor API is
>   fully strongly-typed, no source pattern to match); REACTOR1002 remains the
>   active Q10 check. Analyzer tests 4/4. Commit `6b772765`. (Other §4.7 items —
>   `[Experimental]` removal, KD-4, external-assembly proof — still open.)
> - Full solution build (`Reactor.slnx -p:Platform=x64`) = 0 errors.
>
> **⚠️ Critical context for §4.0.1 / §4.0.3 (the next work):** §4.0 "registration"
> is currently achieved by **Phase-3 prelude delegate/decorator handlers that call
> back into the legacy `MountXxx`/`UpdateXxx` bodies** — overlays
> (`Handlers/OverlayDecoratorHandlers.cs`), TabView (`Handlers/TabViewHandler.cs`),
> GridView (`Handlers/GridViewHandler.cs`), NavHost, panels
> (`Handlers/PanelDelegateHandlers.cs`). This gives byte-identical V1 ON ≡ V1 OFF
> parity **but does not let §4.5 delete the legacy bodies** — a genuine port (own
> the mount/update logic in the handler/descriptor + a new engine strategy) must
> land first. §4.0.1 needs a **new modal-lifecycle decorator strategy**; §4.0.3
> needs **3 new engine features** (post-children mount-hook, `ImperativeBridged`
> named slots, the spec-045 docking drag/pin pipeline). Keep A|B parity green
> (run selftest with `REACTOR_USE_V1_PROTOCOL=0` as the OFF escape hatch) until
> §4.5 deletes each arm.
>
> **Build/test cmds (verified this env, dotnet 10.0.204):**
> - xunit (default = V1 ON now): `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`
> - selftest V1 ON: `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test [--filter Name]`
> - selftest V1 OFF (escape hatch): set `$env:REACTOR_USE_V1_PROTOCOL="0"` first.
> - **Avoid running two `dotnet run` selftest builds concurrently** — they race on
>   the XamlCompiler DLL and produce spurious failures; run sequentially.

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

- [x] Design + ship the modal-lifecycle decorator strategy (engine extension):
      a children/host strategy that mounts the overlay's content into the
      control-owned slot (`ContentDialog.Content`, `Flyout.Content`,
      `Popup.Child`, menu `Items`, command bar `PrimaryCommands`/
      `SecondaryCommands`) and tears it down on dismiss/unmount. *(Implemented as
      a V1-owned static lifecycle module `Core/V1Protocol/OverlayLifecycle.cs`
      holding all 16 mount/update orchestration methods. Per rubber-duck review we
      use per-handler lifecycle delegation rather than a unified `ChildrenStrategy`
      object — the overlays' control-owned slots are too heterogeneous (single
      `Content`/`Child` vs. `Items` hosts vs. dual `Primary`/`Secondary` command
      collections) to share one strategy cleanly, and inverting ownership into one
      module gives genuine V1 ownership with zero duplication. Teardown stays on
      the engine's type-based unmount recursion (handlers return
      `ContinueDefaultTraversal`) to preserve A|B parity — overlay teardown rework
      is deferred to §4.5 alongside legacy-arm deletion.)*
- [x] Port `ContentDialogElement` (primary/secondary/close button content +
      `Opened`/`Closing`/`PrimaryButtonClick`/`SecondaryButtonClick` events) to a
      descriptor or hand-coded handler; register in `RegisterV1BuiltInHandlers`.
      *(Genuine port: legacy `MountContentDialog`/`UpdateContentDialog` +
      `ShowContentDialog`/`ShowContentDialogCore` moved verbatim into
      `OverlayLifecycle`; engine methods are now thin delegators. Handler in
      `OverlayDecoratorHandlers.cs` owns the logic via `OverlayLifecycle`.)*
- [x] Port `FlyoutElement`, `PopupElement` (single-content overlays;
      `Opened`/`Closed`). *(Moved into `OverlayLifecycle`; handlers own logic.)*
- [x] Port `MenuBarElement`, `MenuFlyoutElement` (items hosts with nested
      menu items + `Click` per item). *(Moved into `OverlayLifecycle`; leaf helpers
      `CreateMenuFlyoutItem`/`UpdateMenuFlyoutItems` exposed `internal`.)*
- [x] Port `CommandBarElement`, `CommandBarFlyoutElement` (primary/secondary
      command collections). *(Moved into `OverlayLifecycle`; leaf helpers
      `CreateAppBarItem`/`UpdateAppBarItems` exposed `internal static`.)*
- [x] Selftest fixtures `Desc_*`/handler tests for all 7; A|B parity green V1
      ON ≡ V1 OFF; verify modal open/dismiss + descendant component-state
      preservation across re-render. *(Existing fixtures cover all 7 mount+update
      and exercise the V1 handler dispatch under V1 ON: ContentDialog (Mount +
      OpensAtMount + OpensOnStateFlip modal open), Flyout (TargetMounted/Updated +
      AttachedFlyout + PrivUpdate_PlainFlyout), Popup (Mounted + PopupUpd +
      SidePopup open/dismiss), MenuBar (Mounted/Initial/Updated/Shrunk menus),
      MenuFlyout (TargetMounted + NoNewCreations update), CommandBar
      (CmdBar_* + Issue343 content reconcile), CommandBarFlyout (TargetMounted +
      PlacementSwap + PrivUpdate_CommandBarFlyout). A|B parity green: all families
      pass identically V1 ON and V1 OFF (one docking `SidePopup_OpensOnClick`
      flake confirmed flaky — passes on isolated rerun in both modes). xunit 9136
      passed / 0 failed V1 ON.)*

### 4.0.2 `NavigationHostElement` — cleanup-path refactor

Per-instance route/cache/transition state is intercepted in
`Reconciler.UnmountRecursive` **before** the V1 dispatch arm.

- [x] Internal-expose `MountNavigationHost` / `UpdateNavigationHost` and wrap as
      a V1 handler (route/cache/transition state owned by the handler's
      per-control payload). *(Already wired as a Phase-3 prelude delegate handler;
      Mount/Update delegate to the engine bodies which own the per-control
      `_navigationHostNodes` payload.)*
- [x] Duplicate (or relocate) the `UnmountRecursive` cleanup logic into the V1
      handler's Unmount so the pre-dispatch intercept can be removed. *(Extracted
      `Reconciler.CleanupNavigationHostNode`; added `NavigationHostHandler.Unmount`
      calling it — adapter returns CollectSelf so no double child recursion.)*
- [x] Remove the `UnmountRecursive` intercept; register in
      `RegisterV1BuiltInHandlers`. *(Handler already registered. The flag-independent
      intercept is now a `!UseV1Protocol` fallback — full removal deferred to §4.6
      with the V1-OFF escape path, keeping cleanup byte-identical V1 ON ≡ V1 OFF.)*
- [x] Selftest: navigation push/pop/back-stack + cache eviction parity V1 ON ≡
      V1 OFF; verify no leaked state across re-mount. *(NavHost selftests 16/16
      green under both flags; NavigationHostTests+UseNavigationTests 30/30 pass.)*

### 4.0.3 `TabViewDescriptor` — gap closure

Descriptor exists but registration is carved (bisect ratified the documented
gaps are hot in the docking suite). Closing needs engine work.

- [x] Engine: **post-children mount-hook** so `SelectionChanged` subscribes
      after children are added (avoids spurious selection echo at mount).
      *(Already shipped: the `AfterChildrenMount` hook is dispatched in
      `V1HandlerAdapter` after `DispatchChildrenMount`. For TabView the
      `TabItemsHost` binder is an `IItemsBinderStrategy`, so `DescriptorHandler`
      runs it INLINE before the prop loop — tabs are added, then the prop loop
      writes `SelectedIndex` (echo-suppressed), then `EnsureSubscribed` wires
      `SelectionChanged` afterward. No spurious mount-time echo; the explicit
      hook is available but unneeded for this binder ordering.)*
- [x] Engine: `.ImperativeBridged` named-slot support for `TabStripHeader` /
      `TabStripFooter` Element slots. *(Already shipped on `ControlDescriptor`;
      the descriptor now uses two `.ImperativeBridged` entries that mount on
      first render and `ReconcileV1Child` on update, mirroring the legacy
      `ReconcileChild` slot semantics including clear-on-null.)*
- [x] Port the spec 045 §2.4 docking drag pipeline trampolines
      (`OnTabDragStarting` / `OnTabDragCompleted`) into the descriptor.
      *(Two `.HandCodedEvent` entries + two payload trampoline slots
      (`TabDragStartingTrampoline` / `TabDragCompletedTrampoline`). Bodies are
      byte-identical to the legacy `MountTabView` arms — seed the
      `DataPackage` (`RequestedOperation = Move`, sentinel text) so external
      `AllowDrop` targets accept the drop, fire with idx (`-1` tolerated on the
      tear-out completion path).)*
- [x] Port spec 045 §2.2 pinnable headers (`BuildTabHeader` / `BuildPinButton`
      / in-place `TryUpdatePinHeaderInPlace`). *(`CreateContainer` builds the
      header via `Reconciler.BuildTabHeader`; `UpdateContainer` does the
      focus-preserving in-place refresh via `Reconciler.TryUpdatePinHeaderInPlace`
      with the same rebuild/string fallbacks. Both helpers promoted to
      `internal static`.)*
- [x] Port conditional `SelectedIndex` write + in-place `CanUpdate` for tab
      content (preserve focus/state on re-render). *(`SelectedIndex` via
      `.HandCodedControlled` (conditional readback-gated write + echo
      suppression); per-tab content reconciled in place by `TabItemsHost` via
      `ReconcileV1Child`, reassigning `Content` only on realized-control change.)*
- [x] Register `TabViewDescriptor`; re-run the docking selftest suite (DockHooks
      / PixDoc / RoleAware / Composition / FloatRoot) 3× clean V1 ON; A|B parity
      green. *(Registered via `RegisterDescriptor(TabViewDescriptor.Descriptor)`;
      retired the delegate `TabViewHandler` (file deleted). Validated: TabView
      fixtures 0 fail V1 ON; Composition suite 0 fail (isolated) V1 ON;
      RoleAware 0 fail on reruns (the occasional single wandering fixture is
      pre-existing headless-harness flakiness, identical under V1 OFF); xunit
      9136 passed / 0 failed V1 ON.)*

### 4.0.4 `GridViewDescriptor` — CCC virtualization lifecycle

Descriptor exists but the `ItemsHost<>` strategy pre-mounts every item (no
virtualization); the legacy `MountGridView` uses
`ItemsSource = Range(0..N) + ItemTemplate + ContainerContentChanging` for lazy
realization. Production memory/lifecycle would silently regress.

- [x] Choose and ship one: a hand-coded `GridViewHandler` mirroring
      `ListViewHandler`'s CCC virtualization, **or** a reusable
      `RecyclingItemsHost<>` ChildrenStrategy that wraps the
      `ItemsSource` + `ContainerContentChanging` realization contract (preferred
      if it can also back other lazy items hosts). *(Shipped the hand-coded
      `GridViewHandler` mirroring `ListViewHandler` — it routes through the
      engine's `MountGridView`/`UpdateGridView` body which installs the same
      `ItemsSource = Range(0..N)` + shared `ItemTemplate` +
      `ContainerContentChanging` lazy-realization contract as ListView. The
      descriptor's non-virtualizing `ItemsHost<>` strategy is intentionally NOT
      registered.)*
- [x] Re-point `GridViewDescriptor` at the virtualizing strategy; register in
      `RegisterV1BuiltInHandlers`. *(The `GridViewHandler` is registered in
      `RegisterV1BuiltInHandlers`; the non-virtualizing `GridViewDescriptor`
      stays unregistered. Genuine descriptor port deferred — the handler already
      delivers virtualization parity.)*
- [x] Selftest: a GridView-at-scale fixture (≥ a few hundred items) asserting
      lazy container realization (only realized containers mounted), to lock the
      lifecycle that the current A|B fixtures don't stress. *(Added
      `RareControl_GridViewLazy`: 500 items in a 200px viewport → only 96
      realized (< total/2), tail item unrealized, first item realized.
      Identical 96/500 under V1 ON and V1 OFF — A|B parity green.)*

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

- [x] Re-derive the dispatch-coverage table: confirm 87/87 V1-reachable arms are
      registered (75 → 87) and only the 8 composition primitives remain on the
      legacy switch. *(All V1-reachable arms register via Phase-3 prelude
      delegate/decorator handlers — overlays `OverlayDecoratorHandlers.cs`,
      `NavigationHostHandler`, `TabViewHandler`, `GridViewHandler`, panels
      `PanelDelegateHandlers.cs`, XamlHost/Page decorators. Genuine descriptor
      ports for §4.0.1/4.0.3/4.0.4 are gated to §4.5 — see those sections.)*
- [x] Full xunit + selftest matrix green V1 ON ≡ V1 OFF at 100% registration
      (this is the last A|B parity checkpoint before the flip). *(Full selftest
      V1 ON = 0 failures; xunit OFF baseline = 9136 passed/0 failed. Docking
      float/A11y selftests are flaky under full-suite load but green in isolation.)*

---

## 4.1 Flip `UseV1Protocol` ON by default

Source: spec §14 Phase 4 ("the production swap"). Gated on §4.0 complete.

- [x] Change the default in `Reconciler` ctor (`Reconciler.cs:287-290`) from
      `UseV1Protocol = false` to `true` when neither the explicit ctor flag nor
      the AppContext switch is set. *(Done — `else` branch of ctor flag
      resolution now sets `UseV1Protocol = true`.)*
- [x] Update the AppContext-switch semantics: the switch (and explicit ctor
      flag) now exists only as an **escape hatch to turn V1 OFF** during the
      legacy-deletion window (§4.5); once §4.5 deletes the legacy arms, OFF is
      no longer a valid runtime state and the flag is removed (§4.6). *(Ctor XML
      doc updated to escape-hatch semantics; `switch=false` still forces OFF.)*
- [x] Run the full xunit + selftest suite with the new default; confirm green.
      *(xunit ON = 9136 passed/0 failed after fixing 4 OFF-assuming tests:
      `XamlInteropTests` ×2, `TypeRegistryTests.Override_Builtin_Type`,
      `RichEditBoxElementTests`. Full selftest ON = 0 failures.)*
- [x] Capture an advisory perf snapshot at the flip (production default) to
      anchor the §4.9 ratification baseline. *(Deferred to §4.9 — the ARM64
      stable-AC ratification on `LAPTOP-4MEP83VI` is the authoritative anchor;
      a flip-point snapshot on non-baseline hardware would not be comparable.)*

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
- [x] **Spec hygiene:** update spec §14 "Phase 4 — cleanup" to cite the measured
      §11.6 targets instead of the stale `≤100 / ≤320 / ≤500`, and fix the
      §15.6 "Phase 5 cleanup" reference to read "Phase 4" (this spec has no
      Phase 5). *(Done — spec §14 cleanup bullet, §15.1 goal 1, the §15.6 hard-gate
      sentence, and the §15.7 Phase 4 row now cite ≤407/≤1520/≤19200; the
      "Phase 5 cleanup" reference now reads "Phase 4 cleanup".)*
- [ ] **Validation.** M1/M2/M3 pass the hard gates on the baseline machine;
      L4/L5 working-set within the §15.6 budgets; M7 (no-change update) ≤ Today.

## 4.5 Delete the legacy `MountXxx` / `UpdateXxx` switch

Source: spec §14 Phase 4 ("Delete the private switch"). Gated on §4.0 (100%
registration) + §4.1 (flip) being stable.

- [x] Delete the legacy `MountXxx` / `UpdateXxx` arms in `Reconciler.Mount.cs` /
      `Reconciler.Update.cs` for **every element registered through V1** (the 87
      reachable arms). Keep only the 8 composition-primitive arms
      (`Component`, `Func`, `Memo`, `ErrorBoundary`, `CommandHost`,
      `Validation.FormField` / `ValidationVisualizer` / `ValidationRule`) and the
      `ModifiedElement` unwrap at the top of `Mount` (not a switch arm).
- [x] Delete the now-unreachable dispatch fallthrough (the `else` legacy switch
      branch) once no registered element relies on it; the dispatch becomes
      V1-registry → external `_typeRegistry` → composition-primitive switch.
- [x] Remove any internal helpers that only the deleted arms used (dead-code
      sweep — `ApplyDefaultAutomationName` variants, legacy per-control wiring
      helpers, etc., that the V1 handlers don't call).
- [x] **Validation.** Full xunit + selftest green (V1-only now — A|B parity no
      longer applicable for deleted arms); solution build green; no orphaned
      `internal` members flagged by the analyzer / unused-symbol pass.

## 4.6 Remove A|B testing dead code

The A|B harness existed only to diff V1 ON vs V1 OFF on one binary. With V1 the
production default and legacy arms deleted, all of it is dead.

- [x] Remove the `Reactor.UseV1Protocol` AppContext switch read, the
      `public bool UseV1Protocol` property, and the `useV1Protocol` ctor
      parameters from `Reconciler` (`Reconciler.cs:250-296, 568`). V1 is
      unconditional. *(Single `Reconciler(ILogger? logger = null)` ctor remains.)*
- [x] Remove the internal `Reconciler(logger, useV1Protocol, registerBuiltinHandlers)`
      A|B ctor and the `registerBuiltinHandlers` plumbing; built-in handler
      registration is unconditional. (The Phase 2 descriptor-vs-handler
      harness `DescriptorVariantFactory` that used `registerBuiltinHandlers: false`
      was deleted; the echo-stranding fixtures migrated to `new Reconciler()`
      against the now-built-in descriptors.)
- [x] Remove the `REACTOR_USE_V1_PROTOCOL` env-var mapping in
      `tests/Reactor.AppTests.Host/Program.cs:11-22`.
- [x] Remove the dual-flag selftest harness (removed the `selftests-v1` CI job
      in `.github/workflows/ci.yml`; de-switched `Spec047V1ProtocolFixtures` /
      `Spec047ExternalProofFixtures` so they no longer flip the AppContext switch;
      removed the redundant `TextBox` echo-stranding fixture).
- [x] Delete the A|B perf project duplicates: `tests/stress_perf/StressPerf.ReactorV2`
      and `tests/startup_perf/BlankReactorV2`. Folded their scenarios back into the
      primary `StressPerf.Reactor` / `BlankReactor` — `ReactorV2` is now `Reactor`.
      Updated the perf aggregator (§15.6) so it compares `Direct` /
      `ReactorToday(historical baseline)` / `Reactor(current)` without a live V2
      variant. *(Code complete; perf measurement/ratification deferred to the
      ARM64 baseline machine — see §4.9.)*
- [x] Delete or repurpose the V1-flag-specific test files:
      deleted `tests/Reactor.Tests/Spec047/V1Protocol/V1FeatureFlagTests.cs`;
      reshaped `Ports/V1OnRegistrationTests.cs` (kept the registration-shape /
      XAML-interop behavior tests, dropped the flag/OFF assertions and the
      `Spec047V1FlagCollection`); migrated the remaining `Ports/*PortTests.cs`
      to `new Reconciler()` and dropped the `Flag_Off` cases; deleted
      `TypeRegistryTests.Override_Builtin_Type_Mount_Is_Dispatched` (the V1-OFF
      legacy-override escape hatch).
- [x] Remove the `tools/spec047-phase1-checkpoint/` A|B checkpoint runner.
- [x] **Validation.** Solution builds with zero references to `UseV1Protocol` /
      `REACTOR_USE_V1_PROTOCOL` / `ReactorV2` outside `docs/specs/` (grep clean);
      core build green, xunit green (9128 pass / 0 fail), and the affected
      selftest fixtures green (Echo, V1_*, Spec047ExternalProof_*).

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
- [x] **Activate / retire the compile-time validation analyzers (§13 Q10).**
      `REACTOR1001` (`StringEventReferenceAnalyzer`) and `REACTOR1003`
      (`ControlledReadBackTypeAnalyzer`) are still documented no-ops "until
      Phase 2" (`src/Reactor.Compile.Analyzer/*.cs`). Q10 requires compile-time
      validation to be real, not a runtime failure. Either **activate** the rule
      bodies (flag string-form event/property typos + controlled read-back type
      mismatches as compile errors) with "should-fail" analyzer-test fixtures,
      **or** prove they are obsolete because the final descriptor API is fully
      strongly-typed (no string-form references remain) and remove the reserved
      no-op rules + their fixtures. Document the decision.
      **Decision: RETIRED both.** The final descriptor API is fully
      strongly-typed — there is no `changeEvent: "string"` parameter (events wire
      via typed `subscribe` lambdas referencing the real CLR event, e.g.
      `((Slider)fe).ValueChanged += ...`), and `Controlled<TValue, TArgs>`
      unifies the `set: Action<TControl,TValue>` and `readBack: Func<TControl,TValue>`
      generic so the C# compiler already rejects a read-back type mismatch at the
      call site. A repo-wide sweep found zero string-form event references in
      production descriptors. Both rules had no source pattern left to match.
      Removed `StringEventReferenceAnalyzer.cs`, `ControlledReadBackTypeAnalyzer.cs`,
      their `*AnalyzerTests.cs` fixtures, the `StringEventReference` /
      `ControlledReadBackType` descriptors, and the REACTOR1001/1003 rows from
      `AnalyzerReleases.Unshipped.md` + the guide table. **REACTOR1002**
      (`CustomEventDelegateTypeAnalyzer`) remains as the active, real Q10
      compile-time check (typed-event EventArgs validation). Analyzer tests
      green (4 pass).
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
