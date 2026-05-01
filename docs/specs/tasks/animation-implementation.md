# Reactor Animation System — Implementation Plan

Execution plan for the 8 compositor-layer animation features defined in
[`docs/specs/014-animation-design.md`](../014-animation-design.md).

Phases follow the spec's priority order. Each task is independently checkable
so work can pause and resume at any point.

---

## Phase 0: Shared Foundation

### 0.1 — Curve & Easing type hierarchy
- [x] Create `Reactor\Animation\Curve.cs` with the `Curve` abstract record and
      subclasses: `SpringCurve`, `EaseCurve`, `LinearCurve`
- [x] Add `Easing` readonly record struct with preset constants (`Linear`,
      `EaseIn`, `EaseOut`, `EaseInOut`, `Accelerate`, `Decelerate`, `Standard`)
      and `CubicBezier()` factory
- [x] Unit tests: `CurveTests.cs` — verify record equality, preset values,
      factory methods produce correct types

### 0.2 — AnimationHelper compositor bridge
- [x] Create `Reactor\Animation\AnimationHelper.cs` with `SetOrAnimate()` method
      that routes a property change through `UIElement.StartAnimation()` when an
      ambient curve is present, or falls back to direct property assignment
- [x] Support creating `KeyFrameAnimation` (for `EaseCurve`/`LinearCurve`) and
      `SpringAnimation` (for `SpringCurve`) from a `Curve` instance
- [x] Implement `CompositorProvider` — static accessor for the current
      `Compositor` (from `ElementCompositionPreview.GetElementVisual()`)

---

## Phase 1: Core Animation Features

### 1.1 — `WithAnimation` scoped context (Feature 1)
- [x] Create `Reactor\Animation\AnimationScope.cs` with `[ThreadStatic]` fields
      (`_current`, `_hasScope`) and `WithAnimation(Curve?, Action)` method
- [x] Verify nesting: inner `WithAnimation` scopes override outer ones, restore
      correctly in `finally` block
- [x] Modify `Reconciler.cs:ApplyModifiers()` (~line 795) — for Opacity,
      Translation, Scale, Rotation: replace direct `fe.Property = value` with
      `AnimationHelper.SetOrAnimate(fe, property, value)` when
      `AnimationScope.Current` is non-null
- [x] Ensure `WithAnimation(null, action)` explicitly suppresses animation
      (e.g. inside an outer animated scope)
- [x] Unit tests: `AnimationScopeTests.cs`
  - [x] Verify `Current` is set/restored correctly
  - [x] Verify nesting behavior (inner wins, outer restored)
  - [x] Verify `null` curve suppresses animation
- [x] Integration test fixture (follow `LayoutAnimationFixtures.cs` pattern):
      set a property inside `WithAnimation`, verify compositor animation was
      started on the element's Visual

### 1.2 — `.Animate()` compositor property modifier (Feature 3)
- [x] Create `Reactor\Animation\AnimateProperty.cs` — `[Flags] enum` with
      `Opacity`, `Offset`, `Scale`, `Rotation`, `CenterPoint`, `All`
- [x] Add `AnimationConfig?` property to `Element` base record
      (`Reactor\Core\Element.cs`, alongside existing `LayoutAnimationConfig`)
- [x] Implement `ApplyPropertyAnimation()` in `Reconciler.cs` — creates
      `ImplicitAnimationCollection` entries on the element's composition Visual
      for the targeted properties using the specified Curve
- [x] Merge with existing layout animation `ImplicitAnimationCollection` (layout
      handles Offset/Size; property animation handles Opacity/Scale/Rotation)
      to avoid overwriting each other
- [x] Add `.Animate(Curve, AnimateProperty)` fluent extension method to
      `ElementExtensions.cs`
- [x] Priority: `WithAnimation` scope (Feature 1) > `.Animate()` declaration >
      no animation
- [x] Unit tests: `AnimateModifierTests.cs`
  - [x] Verify `ImplicitAnimationCollection` entries created for correct
        properties
  - [x] Verify Spring vs Ease curve types produce correct animation types
  - [x] Verify merging with layout animation collection
- [x] Integration test fixture: set `.Animate(Curve.Spring())` on an element,
      change Opacity, verify implicit animation present on Visual

### 1.3 — InteractionStates — zero-reconcile hover/press (Feature 8)
- [x] Create `Reactor\Animation\InteractionStates.cs` with:
  - [x] `InteractionStateValues` record (Opacity, Scale, ScaleV, Translation,
        Rotation, Background, Foreground, BorderBrush — no layout properties)
  - [x] `InteractionStatesConfig` record (PointerOver, Pressed, Focused states
        + optional Curve)
  - [x] Builder API: `states.PointerOver(...)`, `states.Pressed(...)`,
        `states.Focused(...)`
  - [x] State machine: Normal → PointerOver → Pressed, with transitions on
        pointer enter/exit/press/release/capture-lost
  - [x] Animation factory: create and cache `KeyFrameAnimation` objects for
        compositor properties per state transition
  - [x] Pressed inherits unoverridden values from PointerOver
- [x] Add `InteractionStatesConfig?` property to `Element` base record
      (`Element.cs`)
- [x] Modify `Reconciler.cs`:
  - [x] On mount/config-change: register pointer event handlers
        (`PointerEntered`, `PointerExited`, `PointerPressed`,
        `PointerReleased`, `PointerCaptureLost`)
  - [x] Create/cache `KeyFrameAnimation` objects for each state's compositor
        properties
  - [x] Event handlers call `element.StartAnimation()` for compositor
        properties, direct-set for brush properties
  - [x] During `ApplyModifiers()`: if element is in a non-Normal interaction
        state, apply that state's brush values instead of Normal values
  - [x] On unmount: unregister handlers, release animation objects
- [x] Add `.InteractionStates()` fluent modifier to `ElementExtensions.cs`
- [x] Unit tests: `InteractionStatesTests.cs`
  - [x] Verify state machine transitions (Normal → PointerOver → Pressed →
        PointerOver → Normal)
  - [x] Verify Pressed inherits from PointerOver
  - [x] Verify config record immutability
  - [x] Verify no layout properties are supported (compile-time: record only has
        visual/brush fields)
- [x] Integration test fixture: mount element with InteractionStates, simulate
      pointer enter, verify compositor animation started and correct brush
      applied

---

## Phase 2: Lifecycle & Sequencing

### 2.1 — Enter/Exit transitions (Feature 2)
- [x] Create `Reactor\Animation\Transition.cs` with the type hierarchy:
  - [x] `Transition` abstract record with `Fade`, `Slide(Edge)`, `Scale(float)`
        presets
  - [x] `CombinedTransition` — `operator +` for parallel composition
  - [x] `AsymmetricTransition` — `operator |` for enter vs exit
  - [x] `DirectionalTransition` — `Enter()` / `Exit()` factories
- [x] Add `ElementTransition?` property to `Element` base record (`Element.cs`)
- [x] Add `.Transition(Transition, Curve?)` fluent modifier to
      `ElementExtensions.cs`
- [x] Modify `Reconciler.cs` — **mount path**:
  - [x] When element has enter transition: set initial visual state (e.g.
        Opacity=0, Offset=slideDistance) then start compositor animation to
        final state
  - [x] Curve resolution: explicit `curve:` param > `AnimationScope.Current` >
        default (300ms Decelerate)
- [x] Modify `Reconciler.cs` — **unmount path** (`UnmountAndPool`):
  - [x] When element has exit transition: start exit animation within a
        `CompositionScopedBatch`
  - [x] On `batch.Completed`: remove element from parent and pool the control
  - [x] Element stays in visual tree during exit animation
  - [x] Reuse patterns from `TransitionEngine.cs:40-76`
- [x] Handle edge case: element unmounted while enter animation still playing
- [x] Handle edge case: element re-mounted while exit animation still playing
      (cancel exit, start enter)
- [x] Unit tests: `TransitionTests.cs`
  - [x] Verify type hierarchy: `Fade + Slide(Bottom)` produces
        `CombinedTransition`
  - [x] Verify asymmetric: `Enter(Fade) | Exit(Scale)` produces correct types
  - [x] Verify curve resolution priority
- [x] Integration test fixture: conditionally render an element with
      `.Transition(Transition.Fade)`, toggle visibility, verify enter animation
      starts and unmount is deferred until exit animation completes

### 2.2 — `WithAnimationAsync` choreography (Feature 7)
- [x] Add `WithAnimationAsync(Curve?, Action)` to `AnimationScope.cs`
- [x] Implementation: create `CompositionScopedBatch` before setting scope and
      running action; `batch.End()` after action; return `Task` that completes
      on `batch.Completed`
- [x] Ensure `CompositorProvider.Current` is available for batch creation
- [x] Unit tests: `AnimationAsyncTests.cs`
  - [x] Verify returned Task completes after batch completes
  - [x] Verify sequential `await` produces correct ordering
  - [x] Verify `Task.WhenAll` with multiple scopes works
- [x] Integration test: sequence two animated state changes with `await`,
      verify second animation starts only after first completes

---

## Phase 3: Advanced Capabilities

### 3.1 — Staggered children animation (Feature 4)
- [x] Add `StaggerConfig?` (record with `Delay` and optional `Curve`) to
      container element types in `Element.cs`
- [x] Add `.Stagger(TimeSpan, Curve?)` fluent modifier to
      `ElementExtensions.cs`
- [x] Modify `Reconciler.cs` — in animation setup paths (enter transitions,
      layout animations, property animations): apply `DelayTime =
      childIndex * staggerDelay` to each child's compositor animation
- [x] On layout reorder: recompute stagger delays based on new child positions
- [x] Unit tests: `StaggerTests.cs`
  - [x] Verify delay computation: child N gets `N * staggerDelay`
  - [x] Verify stagger composes with enter transitions
  - [x] Verify stagger composes with layout animations
- [x] Integration test fixture: mount a list with `.Stagger(40ms)`, verify
      each child's animation has incrementing `DelayTime`

### 3.2 — Keyframe animation builder (Feature 5)
- [x] Create `Reactor\Animation\KeyframeBuilder.cs` with:
  - [x] `KeyframeAnimationDef` record (Duration, Loop, Keyframes array)
  - [x] `KeyframeDef` record (Progress, optional Opacity/Scale/Translation/
        Rotation/Easing)
  - [x] Fluent builder: `.Duration(ms)`, `.Loop()`, `.At(progress, ...)`
- [x] Add keyframe animation storage to `Element` base record (`Element.cs`)
- [x] Add `.Keyframes(name, trigger, builder)` fluent modifier to
      `ElementExtensions.cs`
- [x] Modify `Reconciler.cs`:
  - [x] On mount/state-change: compare trigger value to previous; if changed,
        create `ScalarKeyFrameAnimation` / `Vector3KeyFrameAnimation` per
        property track via `InsertKeyFrame(progress, value, easing)`
  - [x] Group into `CompositionAnimationGroup` and start via
        `UIElement.StartAnimation()`
  - [x] Support `IterationBehavior = Forever` for `.Loop()` keyframes
- [x] Unit tests: `KeyframeBuilderTests.cs`
  - [x] Verify builder produces correct `KeyframeAnimationDef`
  - [x] Verify trigger value change detection
  - [x] Verify loop flag maps to `IterationBehavior.Forever`
- [x] Integration test fixture: mount element with `.Keyframes("pulse",
      trigger: counter, ...)`, change counter, verify keyframe animation
      started on Visual

### 3.3 — Scroll-linked expression animation (Feature 6)
- [x] Create `Reactor\Animation\ScrollAnimation.cs` with:
  - [x] Builder API: `.Parallax(factor)`, `.FadeOut(start, end)`,
        `.FadeIn(start, end)`, `.ScaleRange(start, end, from, to)`,
        `.Expression(property, expressionString)`
  - [x] Expression string generation from pre-built templates
- [x] Add scroll-linked animation storage to `Element` base record
      (`Element.cs`)
- [x] Add `.ScrollLinked(scrollViewerRef, builder)` fluent modifier to
      `ElementExtensions.cs`
- [x] Modify `Reconciler.cs`:
  - [x] On mount: get ScrollViewer's manipulation property set via
        `ElementCompositionPreview.GetScrollViewerManipulationPropertySet()`
  - [x] Create `ExpressionAnimation` per configured expression, set "scroll"
        reference parameter, start on target Visual
  - [x] On unmount: stop expression animations
- [x] Unit tests: `ScrollAnimationTests.cs`
  - [x] Verify expression string generation for each template (Parallax,
        FadeOut, FadeIn, ScaleRange)
  - [x] Verify custom expression passthrough
- [x] Integration test fixture: mount element with `.ScrollLinked()` inside a
      ScrollViewer, verify `ExpressionAnimation` started on Visual with
      correct expression and reference parameter

---

## Cross-Cutting: Demo & Documentation

### D.1 — Demo app updates
- [x] Extend `docs\apps\animation\App.cs` with interactive sections for each
      feature (WithAnimation, Animate, InteractionStates, Transitions, Stagger,
      Keyframes, ScrollLinked, Choreography)
- [x] Each section should have a toggle/button to trigger the animation so it
      can be visually verified

### D.2 — Test app updates
- [x] Add sections to `samples\Reactor.TestApp\App.cs` transitions page
      (~line 1370+) for manual verification of each feature

### D.3 — Existing animation backward compatibility
- [x] Verify `.OpacityTransition()`, `.ScaleTransition()`,
      `.RotationTransition()`, `.TranslationTransition()`,
      `.BackgroundTransition()` continue to work unchanged
- [x] Verify `.LayoutAnimation()` / `.SpringLayoutAnimation()` continue to
      work and merge correctly with new `.Animate()` implicit collections
- [x] Verify `.ConnectedAnimation()` is unaffected

---

## Cross-Cutting: Performance Verification

### P.1 — StressPerf benchmarks
- [x] Add animation-specific stress scenarios to the StressPerf suite:
  - [x] Rapid state changes inside `WithAnimation` scope
  - [x] Many elements with `.Animate()` changing simultaneously
  - [x] InteractionStates hover/unhover cycles at high frequency
  - [x] Enter/exit transitions with many elements added/removed
- [x] Verify zero managed allocations during animation playback (between start
      and completion)
- [x] Verify zero UI-thread callbacks during compositor animation playback
- [x] Profile with nettrace to confirm compositor-thread execution

---

## File Change Summary

**New files** (8):
| File | Phase |
|------|-------|
| `Reactor\Animation\Curve.cs` | 0.1 |
| `Reactor\Animation\AnimationHelper.cs` | 0.2 |
| `Reactor\Animation\AnimationScope.cs` | 1.1 |
| `Reactor\Animation\AnimateProperty.cs` | 1.2 |
| `Reactor\Animation\InteractionStates.cs` | 1.3 |
| `Reactor\Animation\Transition.cs` | 2.1 |
| `Reactor\Animation\KeyframeBuilder.cs` | 3.2 |
| `Reactor\Animation\ScrollAnimation.cs` | 3.3 |

**Modified files** (3 — touched in every phase):
| File | Phases |
|------|--------|
| `Reactor\Core\Element.cs` | 1.2, 1.3, 2.1, 3.1, 3.2, 3.3 |
| `Reactor\Core\Reconciler.cs` | 1.1, 1.2, 1.3, 2.1, 3.1, 3.2, 3.3 |
| `Reactor\Elements\ElementExtensions.cs` | 1.2, 1.3, 2.1, 3.1, 3.2, 3.3 |

**New test files** (one per feature + foundation):
| File | Phase |
|------|-------|
| `tests\...\Fixtures\CurveTests.cs` | 0.1 |
| `tests\...\Fixtures\AnimationScopeTests.cs` | 1.1 |
| `tests\...\Fixtures\AnimateModifierTests.cs` | 1.2 |
| `tests\...\Fixtures\InteractionStatesTests.cs` | 1.3 |
| `tests\...\Fixtures\TransitionTests.cs` | 2.1 |
| `tests\...\Fixtures\AnimationAsyncTests.cs` | 2.2 |
| `tests\...\Fixtures\StaggerTests.cs` | 3.1 |
| `tests\...\Fixtures\KeyframeBuilderTests.cs` | 3.2 |
| `tests\...\Fixtures\ScrollAnimationTests.cs` | 3.3 |
