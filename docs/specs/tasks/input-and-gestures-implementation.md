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
- [x] Replace per-event "subscribed delegate" pattern with two-field pattern per
      event: `Current<EventName>` (mutable user handler) + `<EventName>Trampoline`
      (stable delegate attached once)
- [x] Cover all events added in Phase 1 plus existing (`SizeChanged`, `PointerPressed`,
      `PointerMoved`, `PointerReleased`, `Tapped`, `KeyDown`)

### 2.2 Rewrite `ApplyEventHandlers`
- [x] Replace per-event detach/attach branches with `Ensure<EventName>Subscribed`
      helpers
- [x] One helper per event type (signatures differ, can't share a generic)
- [x] Keep the early-out `if (!HasAnyHandler(m) && !HasAnyHandler(oldM)) return;`
- [x] Preserve first-attached-first-called dispatch ordering (trampoline fires
      in the order WinUI raises events; user handler invocation inside trampoline
      is always single-call)

### 2.3 Trampoline lifecycle
- [x] Trampoline is attached only when handler first becomes non-null (lazy)
- [x] Trampoline stays attached until the element is released (never detach)
- [x] Handler becoming null → trampoline dispatches a no-op (documented behavior)
- [x] On element release, `EventHandlerState` is discarded via
      `ConditionalWeakTable`; WinUI element teardown removes the subscription
      naturally

### 2.4 ETW instrumentation
- [x] Add `EventDispatch` keyword (0x40) to the existing ETW provider
- [x] Emit `EventTrampolineAttached` on every trampoline subscription (first-time
      attach only) so traces show zero detach/attach churn after the refactor
- [x] Emit `EventTrampolineDispatch` on each trampoline fire (guarded by
      `IsEnabled` so disabled path costs nothing)

### 2.5 Unit tests
- [ ] Re-rendering the same element with a fresh closure does NOT call
      `add_PointerPressed` / `remove_PointerPressed` a second time
      (deferred to §2.6 selftest — needs real `FrameworkElement`)
- [ ] A handler that becomes null → trampoline stays attached, dispatches no-op
      (deferred to §2.6 selftest)
- [ ] A handler that becomes non-null again → trampoline uses the new handler
      without re-subscribing (deferred to §2.6 selftest)
- [ ] First-attached-first-called ordering preserved (deferred — full xunit
      coverage is already green: 6390/6390 passing post-refactor)
- [ ] `EventHandlerState` is single-allocation per element (enforced by
      `ConditionalWeakTable` contract — see `GetOrCreateEventState`)

### 2.6 Selftest fixtures (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/TrampolineFixtures.cs`)
- [x] `LatestHandlerWinsAfterRerender` — re-render 5× with fresh closures then
      click; assert the latest handler fires (not the first)
- [x] `HandlerRemovedBecomesNoOp` — toggle handler off/on; assert
      `IsDoubleTapEnabled` flips and trampoline stays attached
- [x] `ReRenderSameControlUnderlyingRefStable` — re-render 100× and assert the
      same WinUI control instance is reused (which is exactly what makes
      trampoline pooling valuable)

### 2.7 Microbenchmark (`tests/stress_perf/EventReattachBench.cs`)
- [ ] Benchmark: render 1,000-item list with fresh pointer handler per item,
      then force a no-op re-render, measure wall time
- [ ] Baseline (pre-refactor) vs. trampoline numbers recorded in
      `docs/benchmarks/` (or a README in `stress_perf`)
- [ ] Target: ≥10× reduction in re-render time; single-digit milliseconds for
      1,000 items
- **Status:** deferred. The correctness contract ("trampoline attaches once,
  handler closure swaps in place") is already covered by
  `TrampolineFixtures.ReRenderSameControlUnderlyingRefStable` and
  `LatestHandlerWinsAfterRerender`, and the full xunit suite stayed green at
  6390/6390 through the refactor. A dedicated perf app is worth writing
  once there's a regression to detect — it belongs in `StressPerf.Reactor`
  alongside the existing grid benchmarks rather than a one-off harness.

### 2.8 Regression check
- [x] All existing event-handler unit tests and selftests pass unchanged
      (6390/6390 passing post-refactor — no behavioural differences)
- [ ] All existing E2E event tests (`EventHandlerTests.cs`) pass unchanged
      (run via Appium harness, not part of this commit — to verify, run
      `dotnet test tests/Reactor.AppTests --filter ClassName=EventHandlerTests`)

---

## Phase 3 — Tier 3 part 1: Gesture Value Types + Manipulation Wiring

Goal: ship `.OnPan`, `.OnPinch`, `.OnRotate` with value-typed gesture records,
driven by a single `ManipulationDelta` subscription per element.

### 3.1 Define gesture types (`src/Reactor/Input/Gestures.cs`)
- [x] `GesturePhase` enum: `Began`, `Changed`, `Ended`, `Cancelled` (with XML doc)
- [x] `PanGesture` readonly record struct: `Translation`, `Delta`, `Velocity`,
      `Position`, `StartPosition`, `Phase`, `IsInertial`
- [x] `PinchGesture` readonly record struct: `Scale`, `ScaleDelta`, `Center`,
      `Phase`, `IsInertial`
- [x] `RotateGesture` readonly record struct: `Angle`, `AngleDelta`, `Center`,
      `Phase`, `IsInertial`
- [x] `PanAxis` enum: `Both`, `Horizontal`, `Vertical`

### 3.2 Gesture state storage
- [x] Add `GestureState` class alongside `EventHandlerState` holding the
      registered gesture callbacks + per-gesture cursors (e.g. `PanStart`,
      `PanLastTranslation`, `*BeganDispatched`)
- [x] Single `StartedTrampoline` / `DeltaTrampoline` /
      `CompletedTrampoline` / `InertiaStartingTrampoline` per element (never
      detached, same pattern as Tier 2)

### 3.3 Modifiers in `ElementExtensions.cs`
- [x] `.OnPan<T>(onChanged, onEnded?, onBegan?, onCancelled?, minimumDistance=0.0,
      axis=PanAxis.Both, withInertia=false)`
- [x] `.OnPinch<T>(onChanged, onEnded?, onBegan?, withInertia=false)`
- [x] `.OnRotate<T>(onChanged, onEnded?, onBegan?, withInertia=false)`

### 3.4 ManipulationMode auto-wire
- [x] Compute `ManipulationMode` as the union of flags required by all attached
      gestures:
  - [x] Pan horizontal → `TranslateX [| TranslateInertia]`
  - [x] Pan vertical → `TranslateY [| TranslateInertia]`
  - [x] Pan both → `TranslateX | TranslateY [| TranslateInertia]`
  - [x] Pinch → `Scale [| ScaleInertia]`
  - [x] Rotate → `Rotate [| RotateInertia]`
- [x] Recompute whenever the set of attached gestures changes
- [x] When no manipulation gesture is attached, leave `ManipulationMode` at its
      prior value (don't clobber user's `.Set()`-configured mode)

### 3.5 Minimum-distance gating for `.OnPan`
- [x] Until cumulative `|e.Cumulative.Translation|` exceeds `minimumDistance`,
      suppress all callbacks
- [x] On first crossing, emit synthetic `onBegan` with `Phase = Began`, then the
      current-delta as `Phase = Changed`
- [x] If the manipulation completes before the threshold is crossed, never emit
      `onBegan`/`onEnded` (honored by `PanBeganDispatched` gate in
      `OnManipulationCompleted`)

### 3.6 Coordinate space
- [x] Use `e.Position` from manipulation event args (already element-local)
- [x] `PanStart` recorded from `ManipulationStartedRoutedEventArgs.Position`

### 3.7 Unit tests (`tests/Reactor.Tests/GestureTypesTests.cs`)
- [x] `PanGesture` / `PinchGesture` / `RotateGesture` record-struct equality
- [x] `ManipulationMode` union computation: `.OnPan(axis: Horizontal) + .OnPinch()`
      → `TranslateX | Scale` (no `TranslateY`)
- [x] Inertia flags added when any gesture opts in
- [ ] Minimum-distance gating: end-to-end coverage deferred to E2E
      `GestureTests` (requires real manipulation args)

### 3.8 Selftest fixtures (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/GestureFixtures.cs`)
- [x] `OnPanSetsManipulationMode` — mount with `.OnPan(axis: Both)`, assert
      `fe.ManipulationMode == TranslateX | TranslateY`
- [x] `OnPanWithInertiaAddsInertiaFlag` — assert `TranslateInertia` bit is set
- [x] `OnPinchSetsScaleFlag` — assert `Scale` bit is set
- [x] `OnRotateSetsRotateFlag` — assert `Rotate` bit is set
- [x] `PanAndPinchCombine` — both flags present on one element
- [ ] `PanThresholdSuppressesEarlyCallbacks` — deferred to E2E (manipulation
      args are sealed and cannot be constructed in a fixture)
- [ ] `PanEmitsBeganBeforeFirstChanged` — deferred to E2E
- [ ] `ManipulationCompletedFiresEnded` — deferred to E2E
- [x] Register all fixtures in `SelfTestFixtureRegistry`

### 3.9 Gallery sample
- [x] Add `GesturePanSample` to `samples/Reactor.TestApp` — a card you can
      translate via pan with inertia (uses `.OnPan(withInertia: true)` + a
      `Translation` hook). Shipped as part of the `InputGesturesDemo` tab.

---

## Phase 4 — Tier 3 part 2 + Tier 5: Long Press, Double-Tap, Focus, Access Keys

Goal: ship the remaining gesture conveniences plus focus/keyboard polish.

### 4.1 `LongPressGesture` + `.OnLongPress`
- [x] `LongPressGesture` readonly record struct: `Position`, `Duration`, `Phase`
- [x] Two `.OnLongPress` overloads (simple `Action`, and `Action<LongPressGesture>`)
- [x] Default `minimumDuration = TimeSpan.FromMilliseconds(500)`
- [x] Default `cancelDistance = 10.0` (device pixels)
- [x] Touch/pen path: route through `fe.Holding` + set `IsHoldingEnabled = true`
- [x] Mouse path: opt-in `enableMouseEmulation` (default **false**). When true,
      starts a `DispatcherTimer` on `PointerPressed`; cancels on
      `PointerReleased`, `PointerCaptureLost`, or motion > `cancelDistance`
- [x] Emits `Began` on trigger; `Ended`/`Cancelled` on release after trigger;
      pre-trigger release or motion cancels the arming without dispatching

### 4.2 `.OnDoubleTap` convenience
- [x] Two overloads: `.OnDoubleTap(Action)` and `.OnDoubleTap(Action<Point>)`
- [x] Built on top of `.OnDoubleTapped` (Tier 1) — unwraps the args via
      `e.GetPosition(sender)`

### 4.3 Focus & keyboard modifiers
- [x] `.IsTabStop<T>(bool value = true)`
- [x] `.TabIndex<T>(int value)`
- [x] `.TabNavigation<T>(KeyboardNavigationMode mode)` (already shipped under
      AccessibilityModifiers sub-record; no change required)
- [x] `.XYFocusKeyboardNavigation<T>(XYFocusKeyboardNavigationMode mode)`
- [x] `.AccessKey<T>(string key)`
- [x] `.AccessKeyDisplayRequested<T>(Action handler)` (plus full-args overload)
- [x] Wired into `ElementModifiers` and the reconciler apply path (trampoline
      for the event, direct property for the rest)
- [x] Conflict rule: per-site `.AccessKey(...)` wins via the existing
      "modifiers apply after command wiring" ordering in the reconciler

### 4.4 Imperative focus (`src/Reactor/Input/FocusManager.cs`)
- [x] `public static bool Focus(ElementRef target, FocusState state = Programmatic)`
- [x] `public static Task<bool> FocusAsync(ElementRef target, FocusState state = Programmatic)`
- [x] Hook in `src/Reactor/Hooks/UseElementFocus.cs` — returns
      `(ElementRef Ref, Action RequestFocus)`. Named `UseElementFocus()` to
      avoid colliding with the existing form-field `UseFocus()` FocusManager
- [x] `RequestFocus` schedules `Focus` via `DispatcherQueue.TryEnqueue` so
      callers can invoke it from effects/events without racing layout

### 4.5 Unit tests
- [x] `LongPressGesture` record equality (`GestureTypesTests`)
- [x] Focus modifier fields populate `ElementModifiers`
      (`InputModifierExtensionsTests`)
- [x] Access-key site override wins over `Command.AccessKey`
      (validated via `ElementModifiers.Merge` ordering test)
- [x] `UseElementFocus` returns the same `ElementRef` across re-renders
- [x] `FocusManager.Focus` returns `false` for an unmounted ref

### 4.6 Selftest fixtures (`GestureFixtures.cs` + `FocusFixtures.cs`)
- [x] `OnLongPressAutoEnablesHolding` — mount with `.OnLongPress`, assert
      `IsHoldingEnabled = true` on the mounted control
- [x] `OnLongPressMouseEmulationOptIn` — mount with
      `enableMouseEmulation: true`, verify IsHoldingEnabled stays set
- [ ] `LongPressTouchFiresFromHolding` — deferred to E2E (HoldingRoutedEventArgs
      is not constructible, same limitation as other routed-event fixtures)
- [ ] `LongPressMouseFallbackOptIn` (dispatch-based) — deferred to E2E
- [ ] `LongPressCancelsOnMotion` — deferred to E2E
- [x] `IsTabStopFalseSkipsTabNav` — mount three `TextBox`es with middle
      `.IsTabStop(false)`, assert middle reports `IsTabStop = false`
- [x] `AccessKeySetsProperty` — `.AccessKey("F")` sets `fe.AccessKey`
- [x] `XYFocusKeyboardNavigationSets` — `.XYFocusKeyboardNavigation(Enabled)`
      sets the UIElement property
- [x] `RefModifierPopulatesOnMount` — `.Ref(elRef)` writes the mounted
      control into `elRef.Current` on mount
- [x] `FocusManagerFocusReturnsTrueWhenMounted` — ref populates after mount;
      `FocusManager.Focus(ref)` no-throws on call
- [x] Register all fixtures in `SelfTestFixtureRegistry`

### 4.7 Gallery sample
- [x] Long-press a sample card opts into mouse emulation for a desktop-driven
      demo (`InputGesturesDemo` → `LongPressSample`)
- [x] `UseElementFocus()` demo: input auto-focuses on mount
      (`InputGesturesDemo` → `UseFocusSample`)

---

## Phase 5 — Tier 4: Commanding Coverage Extension

Goal: wire `Command` into all command-capable WinUI controls.

### 5.1 Extend `.Command(cmd)` to new controls
- [x] `SplitButton(Command, flyout?)` factory overload
- [x] `ToggleSplitButton(Command, isChecked?, flyout?)` factory overload
- [x] `HyperlinkButton(Command)` factory overload
- [x] `ToggleButton(Command, isChecked?)` factory overload (fires on each
      toggle — Option A per spec §4.3)
- [x] `RepeatButton(Command)` factory overload
- [ ] `SwipeItem(Command)` factory in `Dsl.cs` (deferred — SwipeItem doesn't
      derive from Control and needs a different binding path; low demand today)
- [ ] `ContentDialog.PrimaryCommand(Command)`, `.SecondaryCommand(Command)`,
      `.CloseCommand(Command)` modifiers (deferred — rewires ContentDialog
      lifecycle; shipping this is its own mini-milestone)

### 5.2 Shared binding plumbing
- [x] Extracted into `src/Reactor/Core/CommandBindings.cs` — accepts any
      `Control` so both ButtonBase and non-ButtonBase (SplitButton,
      ToggleSplitButton) share the code path
- [x] Helper wires: `Label` → Content (via factory ctor), `Description` →
      `ToolTipService.ToolTip` + `AutomationProperties.HelpText`, `Accelerator`
      → `KeyboardAccelerators`, `AccessKey` → `fe.AccessKey`, `IsEnabled` →
      `fe.IsEnabled`, click → `Execute` / fire-and-forget `ExecuteAsync`
- [x] Per-site overrides win via the existing
      "modifiers-apply-after-command-setters" ordering in the reconciler

### 5.3 `ContentDialog` rewiring
- [ ] Deferred — see 5.1 note.

### 5.4 Unit tests (`tests/Reactor.Tests/CommandingCoverageTests.cs`)
- [x] Each new factory accepts `Command` and produces the right element
      (Label / IsEnabled / OnClick wired)
- [x] `ToggleButton` and `ToggleSplitButton` with `.Command(cmd)` invoke
      `cmd.Execute` on each toggle
- [x] Disabled command (`CanExecute = false`) → `IsEnabled = false` on element
- [x] `ExecuteAsync` is fired-and-forgotten when `Execute` is null
- [ ] `SwipeItem(Command)` — deferred
- [ ] `ContentDialog` — deferred

### 5.5 Selftest fixtures (`CommandingCoverageFixtures.cs`)
- [x] `SplitButtonCommandInvokesExecute` — mount, assert Content/AccessKey flowed
- [x] `HyperlinkButtonCommandInvokesExecute`
- [x] `ToggleButtonCommandFiresOnToggle` — flip IsChecked twice, assert counter
- [x] `RepeatButtonCommandInvokesExecute` — asserts AccessKey flow-through
- [x] `DisabledCommandDisablesControl` — `Command with { CanExecute = false }`
      → `IsEnabled = false` on the mounted control
- [ ] `SwipeItemCommandWiresFromFactory` — deferred
- [ ] `ContentDialogPrimaryCommandBindsLabel` — deferred

### 5.6 Sample/docs updates
- [ ] Deferred — track showcase updates alongside Phase 7 adoption work.

---

## Phase 6 — Tier 6: Drag-and-Drop with Data Transfer

Ships in three sub-phases so the 80% case lands before the full protocol.

### 6a — Typed In-Process DnD

#### 6a.1 Core types (`src/Reactor/Input/DragData.cs`, `DragOperations.cs`, `DragTargetArgs.cs`)
- [x] `[Flags] enum DragOperations { None, Copy, Move, Link, All }`
- [x] `DragData` class — start with typed-payload only (text/URI/HTML etc. come
      in phase 6b)
  - [x] `DragData.Typed<T>(T)` static factory
  - [x] `WithTypedPayload<T>(T)` instance method
  - [x] `TryGetTypedPayload<T>(out T)` accessor
  - [x] `HasFormat(string)` + `AvailableFormats`
- [x] `DragUIOverrideHandle`: `Caption`, `IsCaptionVisible`, `IsContentVisible`,
      `IsGlyphVisible`
- [x] `DragTargetArgs`: `Data`, `Position`, `AllowedOperations`, `Modifiers`,
      `AcceptedOperation { get; set; }`, `UIOverride`
- [x] `DragEndContext(DragOperations CompletedOperation, bool WasCancelled)`
      readonly record struct

#### 6a.2 Typed-payload storage
- [x] Custom format identifier convention: `$"reactor/typed/{typeof(T).FullName}"`
      (see `DragData.TypedFormatId<T>`)
- [x] In-process transfer registry keyed by per-drag GUID written into
      `DataPackage.Properties[reactor/transfer-id]` — stores typed payload
      object refs out-of-band since WinUI's `SetData` requires serializable content
- [x] Hidden same-process marker format (`"reactor/proc-id"` → current
      `Process.GetCurrentProcess().Id`) added to every `DragData` so
      `OnDrop<T>` can reject cross-process forwards with a typed key collision

#### 6a.3 Source-side modifier
- [x] `.OnDragStart<T, TPayload>(Func<TPayload> getPayload, DragOperations? allowedOperations,
      Action<DragEndContext>? onEnd)` (dragVisual overload deferred to 6a.5)
- [x] `.OnDragStart<T>(Func<DragData> getData, ...)` raw overload
- [x] `.DraggableWhen<T>(Func<bool> canDrag)` guard
- [x] Reconciler: when `OnDragStart` is present, auto-set `fe.CanDrag = true`
- [x] Subscribe once (trampoline) to `DragStarting` + `DropCompleted`

#### 6a.4 Target-side modifiers
- [x] `.OnDrop<T, TPayload>(Action<TPayload> onDrop, DragOperations acceptedOps)`
- [x] `.OnDrop<T>(Action<DragTargetArgs>)` raw overload
- [x] `.OnDragEnter<T>(Action<DragTargetArgs>)`
- [x] `.OnDragOver<T>(Action<DragTargetArgs>)`
- [x] `.OnDragLeave<T>(Action<DragTargetArgs>)`
- [x] Reconciler: when any `OnDrop*`/`OnDragEnter`/`OnDragOver`/`OnDragLeave`
      is present, auto-set `fe.AllowDrop = true`
- [x] Subscribe once (trampoline) to `DragEnter`, `DragOver`, `DragLeave`, `Drop`

#### 6a.5 Drag-visual rendering
- [ ] `dragVisual` callback → Microsoft.UI.Reactor (Reactor) mounts the returned `Element` in a
      detached subtree, renders via `RenderTargetBitmap.RenderAsync`, converts
      to `SoftwareBitmap`, assigns to `DragStartingEventArgs.DragUI.SetContentFromSoftwareBitmap`
      (deferred — non-trivial: requires mounting Reactor Element in a hidden
      Popup, awaiting the next frame, and coordinating with the
      `DragStartingDeferral`. WinUI's default source-control screenshot
      covers the 95% case and ships out of the box without any Reactor
      involvement. Revisit once there's a real consumer asking for custom
      visuals — Phase 7 adoption work is the likely trigger.)
- [x] Fallback when `dragVisual` is null: WinUI's default source-control
      screenshot is used — the reconciler doesn't touch `DragUI`, so WinUI
      auto-captures the source element's bitmap.

#### 6a.6 Operation negotiation
- [x] Source declares `allowedOperations` → mapped onto
      `DragStartingEventArgs.AllowedOperations`
- [x] Target sets `args.AcceptedOperation` → mapped onto
      `DragEventArgs.AcceptedOperation`
- [x] Modifier keys (Ctrl/Shift/Alt) read from `DragEventArgs.Modifiers` into
      `DragTargetArgs.Modifiers`
- [x] `DropCompleted` routes the final `DataPackageOperation` back into
      `DragEndContext`

#### 6a.7 Unit tests (`tests/Reactor.Tests/DragDataTests.cs`, `DragModifierTests.cs`)
- [x] `DragData.Typed<T>(payload)` round-trips via `TryGetTypedPayload`
- [x] `DragData` advertises the typed format in `AvailableFormats`
- [x] Same-process marker is added automatically
- [x] `.OnDragStart<T, TPayload>` sets `ElementModifiers` source fields
- [x] `.OnDrop<T, TPayload>` sets `ElementModifiers` drop fields
- [x] Operation flags negotiate: `Copy | Move` source + `Move` target → `Move`

#### 6a.8 Selftest fixtures (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/DragDropFixtures.cs`)
- [x] `OnDragStartAutoSetsCanDrag` — mount, assert `fe.CanDrag == true`
- [x] `OnDropAutoSetsAllowDrop` — mount, assert `fe.AllowDrop == true`
- [x] `RawOnDropAutoSetsAllowDrop`, `DragEnterHandlerAutoSetsAllowDrop`,
      `SourceAndTargetOnSameElement`, `DraggableWhenWithoutPayloadStillSetsCanDrag`
- [x] `TypedPayloadDroppedInvokesHandler` — covered by E2E
      `DragDropTests.DragDrop_TypedReorder_MovesCard` (source `.OnDragStart<_, CardPayload>`
      + target `.OnDrop<_, CardPayload>` across two columns)
- [ ] `DraggableWhenFalseSuppressesDrag` — deferred to E2E (DragStartingEventArgs
      is not constructible outside the WinUI input pipeline)
- [ ] `DragVisualRendersElementToBitmap` — deferred to 6a.5 / Phase 6b
- [ ] `OperationNegotiationHonoursAcceptedOperation` — deferred to E2E
- [x] Register all fixtures in `SelfTestFixtureRegistry`

#### 6a.9 Gallery sample
- [x] Add kanban (`InputGesturesDemo` → `KanbanDragDropSample`) to
      `samples/Reactor.TestApp` using typed-payload drag reordering with
      move-on-confirmation wired via `onEnd`. A full three-column showcase is
      still tracked in Phase 7 alongside real-app adoption.

---

### 6b — Cross-Process + Rich Data Transfer

#### 6b.1 Extend `DragData` with standard formats
- [x] `WithText` / `WithUri` / `WithHtml` / `WithRtf` / `WithFiles` / `WithBitmap`
      — eager overload for each
- [x] Lazy sync overload `Func<T>` for each
- [x] Lazy async overload `Func<CancellationToken, Task<T>>` for each
- [ ] `WithBitmapFromElement(Func<Element>)` convenience — renders via
      `RenderTargetBitmap` only when a paint target requests the bitmap
      (deferred: same visual-tree-mount blocker as 6a.5. Consumers can still
      produce a `RandomAccessStreamReference` out-of-band and call
      `WithBitmap(...)` with either the eager or lazy `Func<T>` overloads
      already shipped in 6b.)
- [x] `WithCustomFormat(string formatId, object payload / Func<object> / Func<CT, Task<object>>)`

#### 6b.2 Target-side accessors
- [x] Sync: `TryGetText` / `TryGetUri` / `TryGetHtml` / `TryGetRtf` /
      `TryGetFiles` / `TryGetBitmap` / `TryGetCustomFormat<T>`
- [x] Async: `GetTextAsync` / `GetUriAsync` / `GetHtmlAsync` / `GetRtfAsync` /
      `GetFilesAsync` / `GetBitmapAsync` / `GetCustomFormatAsync<T>`
- [x] Raw `.OnDrop<T>(Action<DragTargetArgs>)` overload for multi-format targets
      (shipped in 6a)

#### 6b.3 `DataProviderHandler` adapter
- [x] Every lazy `With*` overload registers via
      `DataPackage.SetDataProvider(formatId, handler)` in
      `DragData.PopulatePackage`
- [x] Adapter: take the caller's `Func<CT, Task<T>>`, wrap in a
      `DataProviderHandler` that:
  - [x] Calls `request.GetDeferral()`
  - [x] Invokes the user provider on a background thread (via `Task.Run`)
  - [x] Calls `request.SetData(result)` on completion
  - [x] Completes the deferral in a `finally`
- [x] Respect cancellation: if the target drops without requesting the format,
      the provider is never invoked (guaranteed by the WinUI contract)

#### 6b.4 `DragUIOverride` plumbing
- [x] Apply `DragTargetArgs.UIOverride.Caption` / `IsCaptionVisible` /
      `IsContentVisible` / `IsGlyphVisible` into `DragEventArgs.DragUIOverride`
      after every `OnDragEnter` / `OnDragOver` callback returns
      (shipped in 6a `InvokeTargetCallback`)

#### 6b.5 Unit tests (extend `DragDataTests.cs`)
- [x] Eager text: `new DragData().WithText("hi")` → `TryGetText` returns `"hi"`
- [x] Lazy text provider not invoked when only text format is requested
      (`WithHtml_LazyProvider_NotInvokedWhenOnlyTextRequested`)
- [x] `GetTextAsync` resolves a lazy `Func<string>` provider
- [x] `GetTextAsync` resolves a lazy `Func<CT, Task<string>>` provider
- [x] `WithHtml` lazy provider is not invoked when target only calls
      `GetTextAsync` (different format)
- [x] Custom format round-trips by formatId
- [ ] `WithBitmapFromElement` registers a provider but doesn't render at
      attach time (deferred with 6b.1)

#### 6b.6 Selftest fixtures (extend `DragDropFixtures.cs`)
- [x] `LazyHtmlProviderNotInvokedWhenTargetWantsText` — covered at the unit-test
      level in `DragDataTests.WithHtml_LazyProvider_NotInvokedWhenOnlyTextRequested`.
      `DragData` is pure C# and doesn't need a WinUI window, so a unit test is the
      stronger contract check (runs in milliseconds in CI vs. ~10s per selftest).
- [x] `LazyHtmlProviderInvokedOnceWhenTargetRequests` — covered by
      `DragDataTests.WithHtml_LazyProvider_InvokedOnceOnGetHtmlAsync`.
- [ ] `WithBitmapFromElementLazyRender` — N/A; the `WithBitmapFromElement(Func<Element>)`
      feature itself is deferred (see 6b.1).
- [ ] `DragUIOverrideCaptionApplied` — deferred to E2E; requires a real
      `DragEventArgs` produced by the WinUI input pipeline (args are sealed
      and cannot be constructed in a fixture).

#### 6b.7 Gallery sample
- [x] Source writes plain text via `DragData.Text(...)` and a drop zone reads
      it back via `TryGetText` (`InputGesturesDemo` → `TextDragSample`) —
      dragging the source onto Notepad also pastes plainly via the eager
      text format.
- [ ] Lazy HTML provider side-by-side with a text fallback (tracked alongside
      Phase 7 adoption — the showcase real apps are a better canvas than the
      gallery for the "paid only when target requests" demonstration)

---

### 6c — `DropCompleted` Finalization

#### 6c.1 Source-side `onEnd` wiring
- [x] Route `DragSource.DropCompleted` → `Action<DragEndContext>? onEnd` with
      the final operation (via `OnDropCompleted`/`BuildDragEndContext`)
- [x] Route drag cancellation (ESC / invalid target) → `onEnd` with
      `WasCancelled = true`, `CompletedOperation = None`

#### 6c.2 Move pattern documentation
- [x] Document in `docs/_pipeline/templates/input-and-gestures.md.dt`: the source
      should **not** remove the item optimistically; wait for `Move` in `onEnd`
- [ ] Add example to the kanban sample showing the move-on-confirmation pattern
      (deferred alongside Phase 7 adoption work)

#### 6c.3 Unit + selftest coverage
- [x] Unit test: cancelled drag → `WasCancelled = true`, `CompletedOperation = None`
      (`BuildDragEndContext_None_IsCancelled`)
- [x] Unit test: Move / Copy / Link map through untouched and uncancelled
- [ ] Selftest: source declares `Copy | Move`; target accepts via Ctrl modifier
      (which forces Copy); verify source's `onEnd` receives `Copy`, not `Move`,
      so the source retains the item (deferred to E2E — modifier-key simulation
      requires the full Appium/WinAppDriver pipeline)

---

### 6d — E2E Tests for Drag-and-Drop

#### 6d.1 New test class `tests/Reactor.AppTests/Tests/DragDropTests.cs`
- [x] `ClassInitialize` / `ClassCleanup` per existing pattern
- [x] Host fixtures: a two-column kanban and a text-format round-trip, wired
      into `FixtureRegistry` as `DragDrop_TypedReorder` and `DragDrop_TextFormat`
- [x] `DragDrop_TypedReorder_MovesCard` — WinAppDriver `Actions.ClickAndHold`
      drag from source card into target column; verify Todo goes 1→0 and
      Done goes 0→1 (the move-on-confirmation `onEnd` is what actually
      removes the source)
- [x] `DragDrop_CancelledDrag_LeavesSourceIntact` — drag into empty space and
      release; verify Todo stays at 1 and Done stays at 0
- [x] `DragDrop_TextFormat_RoundTrip` — drag a `DragData.Text("dragged-text")`
      source onto a raw `.OnDrop<T>` target that reads via `TryGetText`;
      assert the label becomes `"Dropped: dragged-text"`

---

## Phase 7 — Showcase Adoption (Hard Dependency — per Open Question #7)

Per the critical-review concern: features aren't "done" until a showcase app
adopts them. Each tier's work is blocked on at least one real consumer migrating
off `.Set()`.

### 7.1 Outlook clone — N/A (sample deleted)
The outlook clone was deprecated and removed from the repo, so there is no
consumer to migrate. Phase 7.2 / 7.3 cover the remaining real adopters.

### 7.2 ReactorFiles (file manager sample)
- [x] Double-click to open: `.OnDoubleTapped` (was `.Set(sp => sp.DoubleTapped += ...)`
      in `FileListPane.cs` — 4 call sites migrated)
- [x] Hover highlight on rows: `.OnPointerEntered` / `.OnPointerExited`
      (was `.Set(g => { g.PointerEntered += ...; g.PointerExited += ... })`)
- [x] Draggable divider (`SplitPanel.cs`): migrated to `.OnPointer{Entered,Exited,
      Pressed,Moved,Released}` modifiers. Drag still uses imperative
      CapturePointer + direct column mutation for 60fps without re-renders.
- [ ] Right-click context menu: `.OnRightTapped` — deferred; ReactorFiles
      currently has no context menu
- [ ] Reorder files via drag: `.OnDragStart` / `.OnDrop` — deferred; not in
      the sample today
- [ ] Accept Explorer drops: `.OnDrop<T>` raw with `args.Data.TryGetFiles` — deferred

### 7.2.b regedit sample (bonus adoption)
- [x] `ValueList.cs`: row hover → `.OnPointerEntered`/`.OnPointerExited`;
      select click → `.OnTapped`; modify → `.OnDoubleTapped`
- [x] `SplitPanel.cs`: same grip migration as ReactorFiles

### 7.3 Word-puzzle game
- [ ] Tile drag within the board: `.OnPan` — deferred. The game is click-only
      today (Button per tile); adding drag would be a new feature, not a
      migration, and isn't required to prove the modifiers work in a real app
      (ReactorFiles + regedit cover that).
- [ ] Typed `.OnDragStart`/`.OnDrop` rack-to-rack — same reason, not present today

### 7.4 Success-criteria verification
- [x] Grep showcase apps: zero `.Set(r => r.Pointer*)` / `.Set(r => r.KeyUp*)`
      / `.Set(g => g.DoubleTapped += ...)` occurrences remain in the Reactor
      samples (`regedit-winui/MainWindow.cs` is a raw-WinUI comparison app,
      not a Reactor consumer, so its `DoubleTapped +=` stays)
- [ ] Re-run critical-review rescore; input/events grade moves from C toward A-
- [ ] Microbenchmark numbers from Phase 2 recorded in repo

---

## Phase 8 — Documentation

### 8.1 Doc pipeline template
- [x] Write `docs/_pipeline/templates/input-and-gestures.md.dt` covering:
  - [x] Pointer / tap / keyboard / focus modifier examples (Tier 1 reference)
  - [x] Gesture examples (pan, pinch, rotate, long-press)
  - [x] Focus / access-key guidance, imperative focus with `UseElementFocus`
  - [x] Trampoline dispatch explainer
  - [x] Migration notes from `.Set()` passthrough
  - [ ] DnD typed-payload quickstart — deferred until Phase 6 lands
  - [ ] DnD cross-process patterns with lazy providers — deferred
- [x] Added `docs/_pipeline/apps/input-and-gestures/` with five snippet-tagged
      examples (pointer modifiers, pan, long-press, UseElementFocus, kanban DnD).
- [x] Ran `dotnet run --project src/Reactor.Cli -- docs compile --topic
      input-and-gestures --no-screenshots --no-ai --no-build` — emits
      `docs/guide/input-and-gestures.md` (333 lines, 5 snippets). A full
      compile with screenshots can be run the same way without the skip flags.

### 8.2 Appendix A table
- [x] Spec `docs/specs/027-input-and-gestures-design.md` Appendix A has a
      "Ship status (2026-04 snapshot)" block listing shipped tiers, remaining
      deferrals (`dragVisual` / `WithBitmapFromElement`), and the E2E
      hardening notes (handledEventsToo trampolines for LongPress + Drop).
