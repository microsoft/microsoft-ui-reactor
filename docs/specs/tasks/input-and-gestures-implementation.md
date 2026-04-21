# Reactor Input & Gestures — Implementation Plan

Execution plan for the input & gesture system defined in
[`docs/specs/027-input-and-gestures-design.md`](../027-input-and-gestures-design.md).

Phases follow the spec's Tier ordering so each tier is independently shippable.
Every task is individually checkable; pause/resume at any checkbox.

Test strategy follows `CONTRIBUTING.md`:
- **Unit tests** (`tests/Reactor.Tests`, xUnit) — modifier application, state
  machines, gesture math, command binding, `DragData` builder/inspector.
- **Selftest fixtures** (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`) —
  mount against real WinUI controls and assert via `VisualTreeHelper` + event
  observation (covers trampoline wiring, manipulation subscription, `IsTapEnabled`
  auto-set, `CanDrag`/`AllowDrop` auto-set, command binding propagation).
- **E2E tests** (`tests/Reactor.AppTests/Tests/`) — Appium/WinAppDriver drives
  real user input across the full stack. Two new classes:
  - `GestureTests.cs` — pan, double-tap, right-tap, long-press end-to-end.
  - `DragDropTests.cs` — typed in-process reorder + text format round-trip.

---

## Phase 1 — Tier 1: Pointer & Keyboard Modifier Completeness

Goal: fill the declarative-modifier gap so `.Set()` is never needed for common
pointer, tap, focus, or keyboard interactions. Keeps today's
attach/detach dispatch path — Tier 2 rewrites it.

### 1.1 Extend `ElementModifiers`
- [x] Add pointer lifecycle fields to `ElementModifiers` in `src/Reactor/Core/ElementModifiers.cs`:
  - [x] `Action<object, PointerRoutedEventArgs>? OnPointerEntered`
  - [x] `Action<object, PointerRoutedEventArgs>? OnPointerExited`
  - [x] `Action<object, PointerRoutedEventArgs>? OnPointerCanceled`
  - [x] `Action<object, PointerRoutedEventArgs>? OnPointerCaptureLost`
  - [x] `Action<object, PointerRoutedEventArgs>? OnPointerWheelChanged`
- [x] Add tap-family fields:
  - [x] `Action<object, DoubleTappedRoutedEventArgs>? OnDoubleTapped`
  - [x] `Action<object, RightTappedRoutedEventArgs>? OnRightTapped`
  - [x] `Action<object, HoldingRoutedEventArgs>? OnHolding`
- [x] Add keyboard fields:
  - [x] `Action<object, KeyRoutedEventArgs>? OnKeyUp`
  - [x] `Action<object, KeyRoutedEventArgs>? OnPreviewKeyDown`
  - [x] `Action<object, KeyRoutedEventArgs>? OnPreviewKeyUp`
  - [x] `Action<object, CharacterReceivedRoutedEventArgs>? OnCharacterReceived`
- [x] Add focus fields:
  - [x] `Action<object, RoutedEventArgs>? OnGotFocus`
  - [x] `Action<object, RoutedEventArgs>? OnLostFocus`
- [x] Update `ElementModifiers.Merge` / equality so new fields participate in diff

### 1.2 Add extension methods in `ElementExtensions.cs`
- [x] `.OnPointerEntered<T>(Action<object, PointerRoutedEventArgs>)`
- [x] `.OnPointerExited<T>(Action<object, PointerRoutedEventArgs>)`
- [x] `.OnPointerCanceled<T>(Action<object, PointerRoutedEventArgs>)`
- [x] `.OnPointerCaptureLost<T>(Action<object, PointerRoutedEventArgs>)`
- [x] `.OnPointerWheelChanged<T>(Action<object, PointerRoutedEventArgs>)`
- [x] `.OnDoubleTapped<T>(Action<object, DoubleTappedRoutedEventArgs>)`
- [x] `.OnRightTapped<T>(Action<object, RightTappedRoutedEventArgs>)`
- [x] `.OnHolding<T>(Action<object, HoldingRoutedEventArgs>)`
- [x] `.OnKeyUp<T>(Action<object, KeyRoutedEventArgs>)`
- [x] `.OnPreviewKeyDown<T>(Action<object, KeyRoutedEventArgs>)`
- [x] `.OnPreviewKeyUp<T>(Action<object, KeyRoutedEventArgs>)`
- [x] `.OnCharacterReceived<T>(Action<object, CharacterReceivedRoutedEventArgs>)`
- [x] `.OnGotFocus<T>(Action<object, RoutedEventArgs>)`
- [x] `.OnLostFocus<T>(Action<object, RoutedEventArgs>)`

### 1.3 Extend `EventHandlerState` and `ApplyEventHandlers`
- [x] Add matching fields to `EventHandlerState` in `Reconciler.cs` (still the
      old attach/detach shape — Tier 2 rewrites this)
- [x] Extend `ApplyEventHandlers` in `Reconciler.cs:~2242` with attach/detach
      branches for each new event
- [x] Auto-enable flag logic when modifier is present and handler non-null:
  - [x] `OnTapped` → `fe.IsTapEnabled = true`
  - [x] `OnDoubleTapped` → `fe.IsDoubleTapEnabled = true`
  - [x] `OnRightTapped` → `fe.IsRightTapEnabled = true`
  - [x] `OnHolding` → `fe.IsHoldingEnabled = true`
- [x] For `Shape` subclasses only: if any pointer event is attached and `Fill is null`,
      set `Fill = new SolidColorBrush(Colors.Transparent)` so hit testing works
- [x] Ensure detach path clears `Is*Enabled` back to default when last handler removed

### 1.4 Unit tests (`tests/Reactor.Tests/InputModifierExtensionsTests.cs`)
- [x] Each new `.On*` modifier sets the corresponding `ElementModifiers` field
- [x] Chained modifiers preserve previously-set fields (merge, don't overwrite)
- [x] `ElementModifiers` equality considers new fields
- [ ] Auto-enable: element with `.OnDoubleTapped` results in `IsDoubleTapEnabled = true`
      on the mounted control (deferred to §1.5 selftest — requires UI-thread mount)
- [ ] `Shape` with pointer handler gets transparent fill if `Fill` was null
      (deferred to §1.5 selftest — requires UI-thread mount)
- [ ] `Shape` with explicit `Fill` is not overwritten (deferred to §1.5 selftest)

### 1.5 Selftest fixtures (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/PointerModifierFixtures.cs`)
- [ ] `PointerEnteredExitedFires` — mount a `Rectangle` with `.OnPointerEntered`/`.OnPointerExited`,
      simulate pointer enter/exit via `RaiseEvent` or manual dispatch, verify counters increment
      (deferred: `PointerRoutedEventArgs` is not constructible — covered by E2E `GestureTests`)
- [x] `DoubleTappedAutoEnables` — mount with `.OnDoubleTapped`, assert the mounted
      control reports `IsDoubleTapEnabled = true` via `VisualTreeHelper`
- [x] `RightTappedAutoEnables` — same, for `IsRightTapEnabled`
- [x] `HoldingAutoEnables` — same, for `IsHoldingEnabled`
- [ ] `KeyUpFires` — mount a `TextBox`, raise `KeyUp`, verify handler fires
      (deferred: same args-not-constructible limitation — covered by E2E)
- [x] `GotFocusLostFocusFires` — mount two `TextField`s, call
      `Focus(FocusState.Programmatic)`, verify `.OnGotFocus` / `.OnLostFocus` fire
- [x] `ShapePointerHandlerAutoFillsTransparent` — mount `Rectangle` with null
      `Fill` and `.OnPointerPressed`, assert `Fill` is a transparent brush after mount
- [x] Additional: `ShapeWithExplicitFillNotOverwritten`, `AutoEnableClearsOnDetach`
- [x] Register all fixtures in `SelfTestFixtureRegistry` and wire into `SelfTestBatch`

---

## Phase 2 — Tier 2: Trampoline-Based Event Dispatch

Goal: eliminate per-render COM churn by attaching a stable trampoline once per
element and redirecting via a mutable field on every update. No API change.

### 2.1 Redesign `EventHandlerState`
- [ ] Replace per-event "subscribed delegate" pattern with two-field pattern per
      event: `Current<EventName>` (mutable user handler) + `<EventName>Trampoline`
      (stable delegate attached once)
- [ ] Cover all events added in Phase 1 plus existing (`SizeChanged`, `PointerPressed`,
      `PointerMoved`, `PointerReleased`, `Tapped`, `KeyDown`)

### 2.2 Rewrite `ApplyEventHandlers`
- [ ] Replace per-event detach/attach branches with `Ensure<EventName>Subscribed`
      helpers:
      ```csharp
      private static void EnsurePointerPressedSubscribed(
          FrameworkElement fe, EventHandlerState state,
          Action<object, PointerRoutedEventArgs>? handler) { ... }
      ```
- [ ] One helper per event type (signatures differ, can't share a generic)
- [ ] Keep the early-out `if (!HasAnyHandler(m) && !HasAnyHandler(oldM)) return;`
- [ ] Preserve first-attached-first-called dispatch ordering (trampoline fires
      in the order WinUI raises events; user handler invocation inside trampoline
      is always single-call)

### 2.3 Trampoline lifecycle
- [ ] Trampoline is attached only when handler first becomes non-null (lazy)
- [ ] Trampoline stays attached until the element is released (never detach)
- [ ] Handler becoming null → trampoline dispatches a no-op (documented behavior)
- [ ] On element release, `EventHandlerState` is discarded; WinUI element
      teardown removes the subscription naturally (verify with a memory test)

### 2.4 ETW instrumentation
- [ ] Add `reactor:event.reattach` keyword to the existing ETW provider
- [ ] Emit an event on every trampoline subscription (first-time attach only)
      so the trace shows zero detach/attach churn after the refactor
- [ ] Emit a separate `reactor:event.dispatch` event on each trampoline fire
      (guarded by keyword level to keep runtime cost zero in prod)

### 2.5 Unit tests (`tests/Reactor.Tests/TrampolineDispatchTests.cs`)
- [ ] Re-rendering the same element with a fresh closure does NOT call
      `add_PointerPressed` / `remove_PointerPressed` a second time (use a mock
      `FrameworkElement` stand-in or spy on a minimal test harness)
- [ ] A handler that becomes null → trampoline stays attached, dispatches no-op
- [ ] A handler that becomes non-null again → trampoline uses the new handler
      without re-subscribing
- [ ] First-attached-first-called ordering preserved across multiple events
- [ ] `EventHandlerState` is single-allocation per element (verify via counter)

### 2.6 Selftest fixtures (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/TrampolineFixtures.cs`)
- [ ] `ClosureChurnIsOneTimeAttach` — re-render an element 100× with a fresh
      `.OnPointerPressed` closure on each render; assert via reflection /
      `GetInvocationList` that the underlying WinUI event has exactly one
      subscription (the trampoline)
- [ ] `TrampolineRespectsLatestHandler` — re-render with handler A then handler B;
      raise `PointerPressed`; verify B fires, not A
- [ ] `NullHandlerIsNoOp` — set handler to null; raise event; assert no throw
      and no residual behavior

### 2.7 Microbenchmark (`tests/stress_perf/EventReattachBench.cs`)
- [ ] Benchmark: render 1,000-item list with fresh pointer handler per item,
      then force a no-op re-render, measure wall time
- [ ] Baseline (pre-refactor) vs. trampoline numbers recorded in
      `docs/benchmarks/` (or a README in `stress_perf`)
- [ ] Target: ≥10× reduction in re-render time; single-digit milliseconds for
      1,000 items

### 2.8 Regression check
- [ ] All existing event-handler unit tests and selftests pass unchanged
- [ ] All existing E2E event tests (`EventHandlerTests.cs`) pass unchanged

---

## Phase 3 — Tier 3 part 1: Gesture Value Types + Manipulation Wiring

Goal: ship `.OnPan`, `.OnPinch`, `.OnRotate` with value-typed gesture records,
driven by a single `ManipulationDelta` subscription per element.

### 3.1 Define gesture types (`src/Reactor/Input/Gestures.cs`)
- [ ] `GesturePhase` enum: `Began`, `Changed`, `Ended`, `Cancelled` (with XML doc)
- [ ] `PanGesture` readonly record struct: `Translation`, `Delta`, `Velocity`,
      `Position`, `StartPosition`, `Phase`, `IsInertial`
- [ ] `PinchGesture` readonly record struct: `Scale`, `ScaleDelta`, `Center`,
      `Phase`, `IsInertial`
- [ ] `RotateGesture` readonly record struct: `Angle`, `AngleDelta`, `Center`,
      `Phase`, `IsInertial`
- [ ] `PanAxis` enum: `Both`, `Horizontal`, `Vertical`

### 3.2 Gesture state storage
- [ ] Add `GestureState` class alongside `EventHandlerState` holding the
      registered gesture callbacks + per-gesture cursors (e.g. `PanStartPosition`,
      `PinchStartScale`)
- [ ] Single `ManipulationStartedTrampoline` / `ManipulationDeltaTrampoline` /
      `ManipulationCompletedTrampoline` per element (never detached, same
      pattern as Tier 2)

### 3.3 Modifiers in `ElementExtensions.cs`
- [ ] `.OnPan<T>(onChanged, onEnded?, onBegan?, onCancelled?, minimumDistance=0.0,
      axis=PanAxis.Both, withInertia=false)`
- [ ] `.OnPinch<T>(onChanged, onEnded?, onBegan?, withInertia=false)`
- [ ] `.OnRotate<T>(onChanged, onEnded?, onBegan?, withInertia=false)`

### 3.4 ManipulationMode auto-wire
- [ ] Compute `ManipulationMode` as the union of flags required by all attached
      gestures:
  - [ ] Pan horizontal → `TranslateX [| TranslateInertia]`
  - [ ] Pan vertical → `TranslateY [| TranslateInertia]`
  - [ ] Pan both → `TranslateX | TranslateY [| TranslateInertia]`
  - [ ] Pinch → `Scale [| ScaleInertia]`
  - [ ] Rotate → `Rotate [| RotateInertia]`
- [ ] Recompute whenever the set of attached gestures changes
- [ ] When no manipulation gesture is attached, leave `ManipulationMode` at its
      prior value (don't clobber user's `.Set()`-configured mode)

### 3.5 Minimum-distance gating for `.OnPan`
- [ ] Until cumulative `|e.Cumulative.Translation|` exceeds `minimumDistance`,
      suppress all callbacks
- [ ] On first crossing, emit synthetic `onBegan` with `Phase = Began`, then the
      current-delta as `Phase = Changed`
- [ ] If the manipulation completes before the threshold is crossed, never emit
      `onBegan` (and never emit `onCancelled` either — the gesture never started)

### 3.6 Coordinate space
- [ ] Use `e.Position` from manipulation event args (already element-local)
- [ ] For pointer-event-driven code (e.g. `PanStartPosition`), use
      `e.GetCurrentPoint(fe).Position`

### 3.7 Unit tests (`tests/Reactor.Tests/GestureTypesTests.cs`)
- [ ] `PanGesture` / `PinchGesture` / `RotateGesture` record-struct equality
- [ ] `ManipulationMode` union computation: `.OnPan(axis: Horizontal) + .OnPinch()`
      → `TranslateX | Scale` (no `TranslateY`)
- [ ] Inertia flags added when any gesture opts in
- [ ] Minimum-distance gating: simulated deltas below threshold produce no callbacks;
      first over-threshold delta produces both `Began` and `Changed`

### 3.8 Selftest fixtures (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/GestureFixtures.cs`)
- [ ] `OnPanSetsManipulationMode` — mount with `.OnPan(axis: Both)`, assert
      `fe.ManipulationMode == TranslateX | TranslateY`
- [ ] `OnPanWithInertiaAddsInertiaFlag` — assert `TranslateInertia` bit is set
- [ ] `OnPinchSetsScaleFlag` — assert `Scale` bit is set
- [ ] `OnRotateSetsRotateFlag` — assert `Rotate` bit is set
- [ ] `PanAndPinchCombine` — both flags present on one element
- [ ] `PanThresholdSuppressesEarlyCallbacks` — raise synthetic `ManipulationDelta`
      events; verify `onChanged` isn't called until threshold crossed
- [ ] `PanEmitsBeganBeforeFirstChanged` — on threshold crossing, `onBegan` is
      called exactly once, before `onChanged`
- [ ] `ManipulationCompletedFiresEnded` — verify phase transitions
- [ ] Register all fixtures in `SelfTestFixtureRegistry`

### 3.9 Gallery sample
- [ ] Add `GesturePanSample` to `samples/Reactor.TestApp` — a card you can
      translate via pan with inertia (uses `.OnPan(withInertia: true)` + a
      `Translation` hook)

---

## Phase 4 — Tier 3 part 2 + Tier 5: Long Press, Double-Tap, Focus, Access Keys

Goal: ship the remaining gesture conveniences plus focus/keyboard polish.

### 4.1 `LongPressGesture` + `.OnLongPress`
- [ ] `LongPressGesture` readonly record struct: `Position`, `Duration`, `Phase`
- [ ] Two `.OnLongPress` overloads (simple `Action`, and `Action<LongPressGesture>`)
- [ ] Default `minimumDuration = TimeSpan.FromMilliseconds(500)`
- [ ] Default `cancelDistance = 10.0` (device pixels)
- [ ] Touch/pen path: route through `fe.Holding` + set `IsHoldingEnabled = true`
- [ ] Mouse path: per the spec's Open Question #3, ship behind an opt-in flag
      `enableMouseEmulation` (default **false**). When true, start a
      `DispatcherTimer` on `PointerPressed`; cancel on `PointerReleased`,
      `PointerCaptureLost`, or pointer motion > `cancelDistance`
- [ ] Emit `Began` on trigger; `Ended` on release after trigger; `Cancelled`
      on early release or motion over threshold

### 4.2 `.OnDoubleTap` convenience
- [ ] Two overloads: `.OnDoubleTap(Action)` and `.OnDoubleTap(Action<Point>)`
- [ ] Built on top of `.OnDoubleTapped` (Tier 1) — just unwraps the args

### 4.3 Focus & keyboard modifiers
- [ ] `.IsTabStop<T>(bool value = true)`
- [ ] `.TabIndex<T>(int value)`
- [ ] `.TabNavigation<T>(KeyboardNavigationMode mode)`
- [ ] `.XYFocusKeyboardNavigation<T>(XYFocusKeyboardNavigationMode mode)`
- [ ] `.AccessKey<T>(string key)`
- [ ] `.AccessKeyDisplayRequested<T>(Action handler)`
- [ ] Wire each into `ElementModifiers` and the reconciler apply path
- [ ] Conflict rule: if `Command.AccessKey` and `.AccessKey(...)` are both
      set, per-site `.AccessKey(...)` wins (matches existing commanding override rule)

### 4.4 Imperative focus (`src/Reactor/Input/FocusManager.cs`)
- [ ] `public static bool Focus(ElementRef target, FocusState state = Programmatic)`
- [ ] `public static Task<bool> FocusAsync(ElementRef target, FocusState state = Programmatic)`
- [ ] `UseFocus()` hook in `Component.cs` — returns `(ElementRef Ref, Action RequestFocus)`
- [ ] `RequestFocus` schedules `TryFocusAsync` on the UI dispatcher after
      current reconcile pass completes (avoid focus-during-render)

### 4.5 Unit tests
- [ ] `LongPressGesture` record equality (`GestureTypesTests`)
- [ ] Focus modifier fields populate `ElementModifiers`
      (`ReactorElementExtensionsTests`)
- [ ] Access-key site override wins over `Command.AccessKey`

### 4.6 Selftest fixtures (`GestureFixtures.cs` + `FocusFixtures.cs`)
- [ ] `LongPressTouchFiresFromHolding` — raise a synthetic `Holding` event,
      assert `onTriggered` called with `Began` phase
- [ ] `LongPressMouseNoFallbackByDefault` — press mouse, wait >500ms, release;
      assert `onTriggered` NOT called (mouse emulation off by default)
- [ ] `LongPressMouseFallbackOptIn` — with `enableMouseEmulation: true`, same
      scenario triggers
- [ ] `LongPressCancelsOnMotion` — press, move > cancelDistance, verify
      `onTriggered` never called
- [ ] `IsTabStopFalseSkipsTabNav` — mount three `TextBox`es with middle one
      `.IsTabStop(false)`, programmatically tab, verify focus skips the middle
- [ ] `AccessKeySetsProperty` — `.AccessKey("F")` sets `fe.AccessKey`
- [ ] `UseFocusFocusesElementOnRequest` — call `RequestFocus` from an effect,
      assert `FocusState` becomes `Programmatic` on target

### 4.7 Gallery sample
- [ ] Long-press a list item in the sample app to show a context menu
- [ ] `UseFocus()` demo: input auto-focuses on mount

---

## Phase 5 — Tier 4: Commanding Coverage Extension

Goal: wire `Command` into all command-capable WinUI controls.

### 5.1 Extend `.Command(cmd)` to new controls
- [ ] `SplitButton.Command(Command)` — factory overload + modifier
- [ ] `ToggleSplitButton.Command(Command)`
- [ ] `HyperlinkButton.Command(Command)`
- [ ] `ToggleButton.Command(Command)` (fires on each toggle — Option A per spec §4.3)
- [ ] `RepeatButton.Command(Command)`
- [ ] `SwipeItem(Command)` factory in `Dsl.cs` (+ overload preserving existing `Action`)
- [ ] `ContentDialog.PrimaryCommand(Command)`, `.SecondaryCommand(Command)`,
      `.CloseCommand(Command)` modifiers

### 5.2 Shared binding plumbing
- [ ] Extract the existing Button/AppBarButton command-binding logic into a
      reusable helper so each new control calls the same code
- [ ] Helper wires: `Label` → content, `Icon` → icon slot, `Description` →
      `ToolTipService.ToolTip` + `AutomationProperties.HelpText`, `Accelerator`
      → `KeyboardAccelerators`, `AccessKey` → `fe.AccessKey`, `IsEnabled` →
      `fe.IsEnabled`, click → `Execute`/`ExecuteAsync`
- [ ] Per-site overrides continue to win (e.g. `.Label("Custom")` after `.Command(cmd)`)

### 5.3 `ContentDialog` rewiring
- [ ] Add optional `PrimaryCommand`, `SecondaryCommand`, `CloseCommand` fields
      to `ContentDialog` element
- [ ] When set, replace the existing `PrimaryButtonText`/`PrimaryButtonClick`
      wiring with the command
- [ ] When unset, existing behavior preserved (back-compat)

### 5.4 Unit tests (`tests/Reactor.Tests/CommandingCoverageTests.cs`)
- [ ] Each new control accepts `.Command(cmd)` and produces the right
      `ElementModifiers` / factory output
- [ ] `ToggleButton` with `.Command(cmd)` invokes `cmd.Execute` on each toggle
- [ ] `SwipeItem(Command)` mirrors properties onto the resulting swipe item
- [ ] `ContentDialog` with all three command slots produces three enabled
      buttons with correct labels/icons
- [ ] Per-site override: `.Command(cmd).Label("Custom")` → control content is "Custom"

### 5.5 Selftest fixtures (`CommandingCoverageFixtures.cs`)
- [ ] `SplitButtonCommandInvokesExecute` — mount, raise Click, assert counter
- [ ] `HyperlinkButtonCommandInvokesExecute`
- [ ] `ToggleButtonCommandFiresOnToggle`
- [ ] `SwipeItemCommandWiresFromFactory` — assert `Command` property flows into
      the WinUI `SwipeItem`
- [ ] `ContentDialogPrimaryCommandBindsLabel` — primary button text reflects
      `Command.Label`
- [ ] `DisabledCommandDisablesControl` — `Command with { CanExecute = false }`
      → `fe.IsEnabled = false` on each new control type

### 5.6 Sample/docs updates
- [ ] Update `CommandingDemo` sample to exercise each newly wired control
- [ ] Extend `docs/_pipeline/templates/commanding.md.dt` with a
      "command-capable controls" section
- [ ] Run `mur docs compile` to regenerate `docs/guide/commanding.md`

---

## Phase 6 — Tier 6: Drag-and-Drop with Data Transfer

Ships in three sub-phases so the 80% case lands before the full protocol.

### 6a — Typed In-Process DnD

#### 6a.1 Core types (`src/Reactor/Input/DragData.cs`, `DragOperations.cs`, `DragTargetArgs.cs`)
- [ ] `[Flags] enum DragOperations { None, Copy, Move, Link, All }`
- [ ] `DragData` class — start with typed-payload only (text/URI/HTML etc. come
      in phase 6b)
  - [ ] `DragData.Typed<T>(T)` static factory
  - [ ] `WithTypedPayload<T>(T)` instance method
  - [ ] `TryGetTypedPayload<T>(out T)` accessor
  - [ ] `HasFormat(string)` + `AvailableFormats`
- [ ] `DragUIOverrideHandle`: `Caption`, `IsCaptionVisible`, `IsContentVisible`,
      `IsGlyphVisible`
- [ ] `DragTargetArgs`: `Data`, `Position`, `AllowedOperations`, `Modifiers`,
      `AcceptedOperation { get; set; }`, `UIOverride`
- [ ] `DragEndContext(DragOperations CompletedOperation, bool WasCancelled)`
      readonly record struct

#### 6a.2 Typed-payload storage
- [ ] Custom format identifier convention: `$"reactor/typed/{typeof(T).FullName}"`
- [ ] `ConditionalWeakTable<DataPackage, object>` stores the actual object ref
      (since `DataPackage.SetData` requires serializable content)
- [ ] Hidden same-process marker format (`"reactor/proc-id"` → current
      `Process.GetCurrentProcess().Id`) added to every `DragData` so
      `OnDrop<T>` can reject cross-process forwards with a typed key collision

#### 6a.3 Source-side modifier
- [ ] `.OnDragStart<T, TPayload>(Func<TPayload> getPayload, DragOperations? allowedOperations,
      Func<TPayload, Element>? dragVisual, Action<DragEndContext>? onEnd)`
- [ ] `.DraggableWhen<T>(Func<bool> canDrag)` guard
- [ ] Reconciler: when `OnDragStart` is present, auto-set `fe.CanDrag = true`
- [ ] Subscribe once (trampoline) to `DragStarting` + `DropCompleted`

#### 6a.4 Target-side modifiers
- [ ] `.OnDrop<T, TPayload>(Action<TPayload> onDrop, DragOperations acceptedOps)`
- [ ] `.OnDragEnter<T>(Action<DragTargetArgs>)`
- [ ] `.OnDragOver<T>(Action<DragTargetArgs>)`
- [ ] `.OnDragLeave<T>(Action<DragTargetArgs>)`
- [ ] Reconciler: when any `OnDrop*`/`OnDragEnter`/`OnDragOver`/`OnDragLeave`
      is present, auto-set `fe.AllowDrop = true`
- [ ] Subscribe once (trampoline) to `DragEnter`, `DragOver`, `DragLeave`, `Drop`

#### 6a.5 Drag-visual rendering
- [ ] `dragVisual` callback → Reactor mounts the returned `Element` in a
      detached subtree, renders via `RenderTargetBitmap.RenderAsync`, converts
      to `SoftwareBitmap`, assigns to `DragStartingEventArgs.DragUI.SetContentFromSoftwareBitmap`
- [ ] Fallback when `dragVisual` is null: screenshot of source element via
      same path

#### 6a.6 Operation negotiation
- [ ] Source declares `allowedOperations` → mapped onto
      `DragStartingEventArgs.AllowedOperations`
- [ ] Target sets `args.AcceptedOperation` → mapped onto
      `DragEventArgs.AcceptedOperation`
- [ ] Modifier keys (Ctrl/Shift/Alt) read from `DragEventArgs.Modifiers` into
      `DragTargetArgs.Modifiers`
- [ ] `DropCompleted` routes the final `DragDropOperation` back into
      `DragEndContext`

#### 6a.7 Unit tests (`tests/Reactor.Tests/DragDataTests.cs`, `DragModifierTests.cs`)
- [ ] `DragData.Typed<T>(payload)` round-trips via `TryGetTypedPayload`
- [ ] `DragData` advertises the typed format in `AvailableFormats`
- [ ] Same-process marker is added automatically
- [ ] `.OnDragStart<T, TPayload>` sets `ElementModifiers` source fields
- [ ] `.OnDrop<T, TPayload>` sets `ElementModifiers` drop fields
- [ ] Operation flags negotiate: `Copy | Move` source + `Move` target → `Move`

#### 6a.8 Selftest fixtures (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/DragDropFixtures.cs`)
- [ ] `OnDragStartAutoSetsCanDrag` — mount, assert `fe.CanDrag == true`
- [ ] `OnDropAutoSetsAllowDrop` — mount, assert `fe.AllowDrop == true`
- [ ] `DraggableWhenFalseSuppressesDrag` — `DraggableWhen(() => false)` →
      `DragStarting` handler cancels the drag
- [ ] `TypedPayloadDroppedInvokesHandler` — programmatically raise
      `DragStarting` → `Drop` (using WinUI's API + our stored `DataPackage`),
      verify target handler receives the typed payload
- [ ] `DragVisualRendersElementToBitmap` — supply a `dragVisual`, raise
      `DragStarting`, assert `DragUI` bitmap content is non-null and matches
      approximate dimensions of the rendered element
- [ ] `OperationNegotiationHonoursAcceptedOperation` — source `Copy | Move`,
      target accepts `Move`, assert `DragEndContext.CompletedOperation == Move`

#### 6a.9 Gallery sample
- [ ] Add three-column kanban to `samples/Reactor.TestApp` using typed-payload
      drag reordering

---

### 6b — Cross-Process + Rich Data Transfer

#### 6b.1 Extend `DragData` with standard formats
- [ ] `WithText` / `WithUri` / `WithHtml` / `WithRtf` / `WithFiles` / `WithBitmap`
      — eager overload for each
- [ ] Lazy sync overload `Func<T>` for each
- [ ] Lazy async overload `Func<CancellationToken, Task<T>>` for each
- [ ] `WithBitmapFromElement(Func<Element>)` convenience — renders via
      `RenderTargetBitmap` only when a paint target requests the bitmap
- [ ] `WithCustomFormat(string formatId, object payload / Func<object> / Func<CT, Task<object>>)`

#### 6b.2 Target-side accessors
- [ ] Sync: `TryGetText` / `TryGetUri` / `TryGetHtml` / `TryGetRtf` /
      `TryGetFiles` / `TryGetBitmap` / `TryGetCustomFormat<T>`
- [ ] Async: `GetTextAsync` / `GetUriAsync` / `GetHtmlAsync` / `GetRtfAsync` /
      `GetFilesAsync` / `GetBitmapAsync` / `GetCustomFormatAsync<T>`
- [ ] Raw `.OnDrop<T>(Action<DragTargetArgs>)` overload for multi-format targets

#### 6b.3 `DataProviderHandler` adapter
- [ ] Every lazy `With*` overload registers via
      `DataPackage.SetDataProvider(formatId, handler)`
- [ ] Adapter: take the caller's `Func<CT, Task<T>>`, wrap in a
      `DataProviderHandler` that:
  - [ ] Calls `request.GetDeferral()`
  - [ ] Invokes the user provider on a background thread
  - [ ] Calls `request.SetData(result)` on completion
  - [ ] Completes the deferral in a `finally`
- [ ] Respect cancellation: if the target drops without requesting the format,
      the provider is never invoked (guaranteed by the WinUI contract)

#### 6b.4 `DragUIOverride` plumbing
- [ ] Apply `DragTargetArgs.UIOverride.Caption` / `IsCaptionVisible` /
      `IsContentVisible` / `IsGlyphVisible` into `DragEventArgs.DragUIOverride`
      after every `OnDragEnter` / `OnDragOver` callback returns

#### 6b.5 Unit tests (extend `DragDataTests.cs`)
- [ ] Eager text: `DragData.Text("hi")` → `TryGetText` returns `"hi"`
- [ ] Lazy text provider not invoked when only `TryGetText` called without
      request resolution
- [ ] `GetTextAsync` resolves a lazy `Func<string>` provider
- [ ] `GetTextAsync` resolves a lazy `Func<CT, Task<string>>` provider
- [ ] `WithHtml` lazy provider is not invoked when target only calls
      `GetTextAsync` (different format)
- [ ] Custom format round-trips by formatId
- [ ] `WithBitmapFromElement` registers a provider but doesn't render at
      attach time

#### 6b.6 Selftest fixtures (extend `DragDropFixtures.cs`)
- [ ] `LazyHtmlProviderNotInvokedWhenTargetWantsText` — instrument a counter
      inside the HTML provider; drop onto text-only target; assert counter = 0
- [ ] `LazyHtmlProviderInvokedOnceWhenTargetRequests` — drop onto HTML target
      (simulated via `GetHtmlAsync`); assert counter = 1; drop again; assert = 2
- [ ] `WithBitmapFromElementLazyRender` — supply a large element, drop on
      a text-only target; assert `RenderTargetBitmap` was not invoked (track via
      a side-channel flag set inside the build callback)
- [ ] `DragUIOverrideCaptionApplied` — set `args.UIOverride.Caption = "Move"`
      in `OnDragEnter`; assert the underlying WinUI `DragUIOverride.Caption` is
      set accordingly

#### 6b.7 Gallery sample
- [ ] Drop zone that accepts Explorer-dropped files and logs their paths
- [ ] Source that advertises text + lazy HTML; drop onto Notepad (text only)
      and Word (rich text); log proves HTML provider fires exactly once for the
      Word drop and never for Notepad

---

### 6c — `DropCompleted` Finalization

#### 6c.1 Source-side `onEnd` wiring
- [ ] Route `DragSource.DropCompleted` → `Action<DragEndContext>? onEnd` with
      the final operation
- [ ] Route drag cancellation (ESC / invalid target) → `onEnd` with
      `WasCancelled = true`, `CompletedOperation = None`

#### 6c.2 Move pattern documentation
- [ ] Document in `docs/_pipeline/templates/input-and-gestures.md.dt`: the source
      should **not** remove the item optimistically; wait for `Move` in `onEnd`
- [ ] Add example to the kanban sample showing the move-on-confirmation pattern

#### 6c.3 Unit + selftest coverage
- [ ] Unit test: cancelled drag → `WasCancelled = true`, `CompletedOperation = None`
- [ ] Selftest: source declares `Copy | Move`; target accepts via Ctrl modifier
      (which forces Copy); verify source's `onEnd` receives `Copy`, not `Move`,
      so the source retains the item

---

### 6d — E2E Tests for Drag-and-Drop

#### 6d.1 New test class `tests/Reactor.AppTests/Tests/DragDropTests.cs`
- [ ] `ClassInitialize` / `ClassCleanup` per existing pattern
- [ ] Host fixture: a minimal kanban (two columns, one card) wired with typed
      drag/drop, registered in `AppTests.Host` navigator
- [ ] `DragDrop_TypedReorder_MovesCard` — WinAppDriver
      `TouchAction`/`PointerInputDevice` drag from source card to target column,
      verify the card's UIA parent changes
- [ ] `DragDrop_CancelledDrag_LeavesSourceIntact` — press+drag+ESC (or drop
      outside any target), verify source column still has the card and a toast
      announces "Drop cancelled"
- [ ] `DragDrop_TextFormat_RoundTrip` — a small fixture with a text source
      (`.OnDragStart(() => DragData.Text("hello"))`) and a text target
      (`.OnDrop<T>(args => { args.Data.TryGetText(out var t); … })`); drag,
      assert the target's label becomes `"hello"`

---

## Phase 7 — Showcase Adoption (Hard Dependency — per Open Question #7)

Per the critical-review concern: features aren't "done" until a showcase app
adopts them. Each tier's work is blocked on at least one real consumer migrating
off `.Set()`.

### 7.1 Outlook clone
- [ ] List-item hover: migrate to `.OnPointerEntered` / `.OnPointerExited`
      (delete previous `.Set()` passthrough code)
- [ ] Draggable divider between message list and preview: `.OnPan(axis: Horizontal)`
- [ ] Move message to folder: `.OnDragStart<MailItem, Message>` on list items,
      `.OnDrop<FolderNode, Message>` on folder tree nodes
- [ ] Regression: existing E2E passes

### 7.2 ReactorFiles (file manager sample)
- [ ] Double-click to open: `.OnDoubleTapped`
- [ ] Right-click context menu: `.OnRightTapped`
- [ ] Reorder files: `.OnDragStart` / `.OnDrop` with typed payload
- [ ] Accept Explorer drops: `.OnDrop<T>` raw overload reading `args.Data.TryGetFiles`

### 7.3 Word-puzzle game
- [ ] Tile drag within the board: `.OnPan` (no data transfer needed)
- [ ] If racks ever move tiles between each other: typed `.OnDragStart`/`.OnDrop`

### 7.4 Success-criteria verification
- [ ] Grep showcase apps: zero `.Set(r => r.Pointer*)` / `.Set(r => r.KeyUp*)`
      occurrences remain
- [ ] Re-run critical-review rescore; input/events grade moves from C toward A-
- [ ] Microbenchmark numbers from Phase 2 recorded in repo

---

## Phase 8 — Documentation

### 8.1 Doc pipeline template
- [ ] Write `docs/_pipeline/templates/input-and-gestures.md.dt` covering:
  - [ ] The full Tier 1 modifier list (reference table)
  - [ ] Gesture examples (pan, pinch, rotate, long-press)
  - [ ] Focus / access-key guidance
  - [ ] DnD typed-payload quickstart
  - [ ] DnD cross-process patterns with lazy providers
  - [ ] Migration notes from `.Set()` passthrough
- [ ] Run `mur docs compile` to regenerate `docs/guide/input-and-gestures.md`
      (never hand-write the generated file)

### 8.2 Appendix A table
- [ ] Update the spec's Appendix A ("Field-By-Field Coverage After Phase 1")
      with actual ship status once each tier lands
