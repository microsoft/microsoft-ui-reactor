# Reactor Animation — Design Spec

Eight compositor-layer animation features that close the gaps identified in the
[critical review](../critical-review.md) section 10, lifting Microsoft.UI.Reactor's animation
grade from C toward A. Every feature follows the same design principle as
layout-to-layout animations: simple declarative API, compositor-thread execution,
zero per-frame managed code.

---

## Status

**Proposed** — design spec complete, not yet implemented.

---

## Problem Statement

The [critical review](../critical-review.md) §10 grades Reactor's animation at
**C** — layout animations with spring physics and connected animations are solid,
but the system only covers layout motion and 5 implicit property transitions:

> "The animation system handles what the composition layer gives you for free and
> nothing more."

The nine specific gaps identified:

1. Implicit transitions limited to 5 properties, no easing control
2. No declarative value-driven animation API (`withAnimation` equivalent)
3. No enter/exit animations for individual elements
4. No keyframe or sequenced animation DSL
5. No easing function DSL
6. Layout animation limitations (hit-testing, cosmetic size)
7. Connected animations require string-key coordination
8. VSM replacement is expensive (full reconcile for hover)
9. No UseAnimation hook

SwiftUI's `withAnimation { state = newValue }` makes *any* state change
animatable. Compose's `animateAsState` does the same. Reactor cannot match this
fully because WinUI doesn't expose a general-purpose "animate this dependency
property" mechanism — but the framework can do much more than it does today
by better exposing WinUI's composition layer and adding framework-level
animation orchestration.

### Prior art: Flux.UI

Our sibling framework demonstrates proven patterns that directly inform this
design:

- **`Animation.Animate(curve, action)`** — `[ThreadStatic]` ambient curve that
  propagates through synchronous state changes. One call animates property
  changes, FLIP layout repositioning, and enter/exit transitions.
- **`Animated(value, animation)`** — declaration-site per-property animation.
  Control authors pre-bind curves; app authors just mutate state.
- **`TransitionElement`** — enter/exit transitions with `CompositionScopedBatch`
  completion tracking. Reconciler delays unmounting until exit animation finishes.
- **FLIP layout animation** — snapshot old positions, arrange, compute deltas,
  animate from delta to zero with `InsertExpressionKeyFrame` for interruption
  support.
- **`AnimateAsync`** — `CompositionScopedBatch`-backed `Task` for choreography
  via standard `await` / `Task.WhenAll`.

### WinUI APIs we underexpose

| API | What it enables | Current Reactor usage |
|-----|----------------|--------------------|
| `UIElement.StartAnimation()` | Explicit composition animation on facade properties with custom curves | Not used |
| `ImplicitAnimationCollection` | Automatic animation on *any* Visual property change | Only for layout Offset/Size |
| `CompositionScopedBatch` | Completion tracking for animation sequencing | Only in TransitionEngine |
| `ExpressionAnimation` | Input-driven animation (scroll-linked, pointer-linked) | Not used |
| `KeyFrameAnimation.InsertKeyFrame()` | Multi-step animations with per-keyframe easing | Only in TransitionEngine |
| `CompositionAnimation.DelayTime` | Staggered animation starts | Not used |

---

## Goals

1. **Compositor-thread execution** — every animation feature runs on the
   compositor thread. Zero per-frame managed-code callbacks during animation
   playback. This is the non-negotiable performance constraint.
2. **Declarative API** — animations are expressed as Element modifiers or scoped
   contexts, not imperative composition API calls via `.Set()`.
3. **Composability** — features compose with each other. `WithAnimation` scopes
   work with enter/exit transitions. Stagger works with layout animations.
   Keyframes work with the curve type system.
4. **Zero-allocation hot paths** — the `[ThreadStatic]` scope mechanism and
   record-based configuration add no GC pressure during animation.
5. **Incremental adoption** — each feature is independent. Existing code using
   `.OpacityTransition()` / `.LayoutAnimation()` continues to work unchanged.

### Non-goals

- **Animate arbitrary dependency properties** — WinUI's facade system only
  exposes Opacity, Translation, Scale, Rotation, CenterPoint, and
  TransformMatrix on the composition Visual. Animating Width, Margin, FontSize,
  etc. would require UI-thread DoubleAnimation (15-30fps, blocks layout). We
  intentionally do not wrap this because it violates goal #1.
- **Frame-driven re-rendering hooks** — a `UseAnimation` hook that re-renders
  the component every frame (like React Spring) would trigger full
  reconciliation at 60fps. This is the kind of "looks good in calling code,
  performs terribly at runtime" API we explicitly want to avoid.
- **Replace WinUI's theme transition system** — `AddDeleteThemeTransition`,
  `EntranceThemeTransition`, etc. continue to work via `.WithTransitions()`.
  The new enter/exit system (Feature 2) is complementary, not a replacement.

---

## Shared Foundation: Curve Type Hierarchy

All seven features share a common animation curve type:

```csharp
namespace Microsoft.UI.Reactor.Animation;

/// <summary>
/// Describes the timing/physics of an animation. Immutable, shareable, zero-allocation.
/// Maps to either a CompositionEasingFunction or a SpringAnimation at the compositor layer.
/// </summary>
public abstract record Curve
{
    // ── Factory methods ──

    /// <summary>Spring natural motion. DampingRatio 0=undamped, 1=critically damped.</summary>
    public static Curve Spring(float dampingRatio = 0.8f, float period = 0.05f)
        => new SpringCurve(dampingRatio, period);

    /// <summary>Cubic-bezier eased animation over a fixed duration.</summary>
    public static Curve Ease(int durationMs, Easing easing = default)
        => new EaseCurve(TimeSpan.FromMilliseconds(durationMs), easing);

    /// <summary>Constant-speed animation over a fixed duration.</summary>
    public static Curve Linear(int durationMs)
        => new LinearCurve(TimeSpan.FromMilliseconds(durationMs));
}

public sealed record SpringCurve(float DampingRatio, float Period) : Curve;
public sealed record EaseCurve(TimeSpan Duration, Easing Easing) : Curve;
public sealed record LinearCurve(TimeSpan Duration) : Curve;

/// <summary>
/// Cubic bezier easing definition. Presets match WinUI/Fluent Design motion guidelines.
/// </summary>
public readonly record struct Easing(float X1, float Y1, float X2, float Y2)
{
    public static readonly Easing Linear      = new(0f, 0f, 1f, 1f);
    public static readonly Easing EaseIn      = new(0.42f, 0f, 1f, 1f);
    public static readonly Easing EaseOut     = new(0f, 0f, 0.58f, 1f);
    public static readonly Easing EaseInOut   = new(0.42f, 0f, 0.58f, 1f);
    public static readonly Easing Accelerate  = new(0.9f, 0.1f, 1f, 0.2f);
    public static readonly Easing Decelerate  = new(0.1f, 0.9f, 0.2f, 1f);
    public static readonly Easing Standard    = new(0.8f, 0f, 0.2f, 1f);    // Fluent standard

    public static Easing CubicBezier(float x1, float y1, float x2, float y2) => new(x1, y1, x2, y2);
}
```

**Key file**: New `Reactor\Animation\Curve.cs`

---

## Feature 1: `WithAnimation` Scoped Context

**Gap closed**: No declarative value-driven animation API (review gap #2)
**Category**: Framework value-add
**Inspired by**: Flux `Animation.Animate()`, SwiftUI `withAnimation`

### API

```csharp
// Every visual property change in this state mutation animates with spring
Animation.WithAnimation(Curve.Spring(0.8f), () =>
{
    isExpanded.Value = true;    // triggers re-render; reconciler sees ambient curve
});

// Ease variant
Animation.WithAnimation(Curve.Ease(300, Easing.Decelerate), () =>
{
    selectedIndex.Value = 2;
});

// Explicit no-animation override (useful inside an animated scope)
Animation.WithAnimation(null, () => count.Value = 0);

// Nesting: inner overrides outer
Animation.WithAnimation(Curve.Ease(300), () =>
{
    opacity.Value = 0.5;  // eased

    Animation.WithAnimation(Curve.Spring(), () =>
    {
        position.Value = newPos;  // spring (inner wins)
    });
});
```

### How it works

1. `WithAnimation(curve, action)` saves/restores a `[ThreadStatic] Curve? Current`
   field and runs the action synchronously. Nesting is supported — inner scopes
   override outer ones.

2. The reconciler's `ApplyModifiers()` (currently `Reconciler.cs:809`) checks
   `AnimationScope.Current` when setting Opacity, Translation, Scale, Rotation.
   If a curve is present, it routes through `UIElement.StartAnimation()` with a
   compositor `KeyFrameAnimation` or `SpringAnimation` instead of direct property
   assignment (`fe.Opacity = value`).

3. Layout changes within the scope automatically compose with the existing
   `LayoutAnimationConfig` mechanism — if an element has `.LayoutAnimation()`,
   the ambient curve overrides its default duration/spring.

### Implementation

```csharp
namespace Microsoft.UI.Reactor.Animation;

public static class AnimationScope
{
    [ThreadStatic] private static Curve? _current;
    [ThreadStatic] private static bool _hasScope;

    public static Curve? Current => _current;
    public static bool HasScope => _hasScope;

    public static void WithAnimation(Curve? curve, Action action)
    {
        var prevCurve = _current;
        var prevScope = _hasScope;
        _current = curve;
        _hasScope = true;
        try { action(); }
        finally { _current = prevCurve; _hasScope = prevScope; }
    }
}
```

### Reconciler integration

In `Reconciler.cs:ApplyModifiers()`, the current direct-set pattern:

```csharp
if (m.Opacity.HasValue && m.Opacity != oldM?.Opacity)
    fe.Opacity = m.Opacity.Value;
```

Becomes:

```csharp
if (m.Opacity.HasValue && m.Opacity != oldM?.Opacity)
    AnimationHelper.SetOrAnimate(fe, "Opacity", (float)m.Opacity.Value);
```

Where `AnimationHelper.SetOrAnimate` checks `AnimationScope.Current` and either
sets the property directly or creates/starts a compositor animation. The helper
is shared across all animatable properties.

### Performance

- ThreadStatic save/restore is zero-allocation
- `UIElement.StartAnimation()` runs on the compositor thread
- The scope itself is synchronous — no async overhead
- Same perf characteristics as existing implicit transitions

### Key files

| File | Change |
|------|--------|
| `Reactor\Animation\AnimationScope.cs` | New — `[ThreadStatic]` scope + `WithAnimation` |
| `Reactor\Animation\AnimationHelper.cs` | New — `SetOrAnimate` / compositor animation creation |
| `Reactor\Core\Reconciler.cs` | Modify — `ApplyModifiers()` routes through `AnimationHelper` |

---

## Feature 2: Element Enter/Exit Transitions

**Gap closed**: No enter/exit animations for individual elements (review gap #3)
**Category**: Framework value-add
**Inspired by**: Flux `TransitionElement`, SwiftUI `.transition()`

### API

```csharp
// Fade + slide when element appears/disappears via conditional rendering
if (isVisible)
    Text("Hello").Transition(Transition.Fade + Transition.Slide(Edge.Bottom))

// Asymmetric: different enter and exit
Card(content)
    .Transition(Transition.Enter(Transition.Scale(0.9f) + Transition.Fade)
              | Transition.Exit(Transition.Fade))

// With explicit curve (overrides default)
Panel(content)
    .Transition(Transition.Fade, curve: Curve.Spring(0.7f))

// WithAnimation scope composes: its curve overrides the transition's default
Animation.WithAnimation(Curve.Spring(), () =>
{
    showPanel.Value = true;  // enter transition uses spring instead of default ease
});
```

### Transition type hierarchy

```csharp
namespace Microsoft.UI.Reactor.Animation;

public abstract record Transition
{
    // ── Presets ──
    public static readonly Transition Fade = new FadeTransition();
    public static Transition Slide(Edge edge) => new SlideTransition(edge);
    public static Transition Scale(float from = 0.85f) => new ScaleTransition(from);

    // ── Asymmetric factory ──
    public static Transition Enter(Transition enter) => new DirectionalTransition(enter, null);
    public static Transition Exit(Transition exit) => new DirectionalTransition(null, exit);

    // ── Combinators ──
    /// <summary>Combine two transitions to play in parallel (e.g., Fade + Slide).</summary>
    public static Transition operator +(Transition a, Transition b) => new CombinedTransition(a, b);

    /// <summary>Asymmetric: left side is enter, right side is exit.</summary>
    public static Transition operator |(Transition enter, Transition exit)
        => new AsymmetricTransition(enter, exit);
}

public sealed record FadeTransition : Transition;
public sealed record SlideTransition(Edge Edge) : Transition;
public sealed record ScaleTransition(float From) : Transition;
public sealed record CombinedTransition(Transition First, Transition Second) : Transition;
public sealed record AsymmetricTransition(Transition Enter, Transition Exit) : Transition;
public sealed record DirectionalTransition(Transition? Enter, Transition? Exit) : Transition;
```

### How it works

1. `ElementTransition` record stored on `Element` base (alongside `LayoutAnimationConfig`).

2. **On mount**: the reconciler sets the element's initial visual state based on
   the transition type (e.g., Opacity=0 for Fade, Offset=slideDistance for Slide),
   then creates and starts a compositor keyframe animation to the final state.

3. **On unmount**: the reconciler does **not** immediately remove the element.
   Instead it starts the reverse animation within a `CompositionScopedBatch`.
   The element stays in the visual tree during the exit animation. On
   `batch.Completed`, the element is actually removed and pooled.

4. **Curve resolution priority**: explicit `curve:` parameter > `AnimationScope.Current`
   (Feature 1) > default ease (300ms, Easing.Decelerate).

### Reconciler changes

The unmount path (`Reconciler.cs:UnmountAndPool`) currently removes the control
immediately. With enter/exit transitions:

```
if element has Transition with exit:
    1. Start exit animation on element's Visual (fade out, slide out, etc.)
    2. Create CompositionScopedBatch around the animations
    3. On batch.Completed: remove from parent, pool the control
    4. Return immediately (element is visually exiting but still in tree)
else:
    existing immediate removal
```

This pattern already exists in `TransitionEngine.cs:40-76` (navigation transitions)
and can be directly reused.

### Performance

- Enter/exit = 2-4 compositor keyframes on Opacity/Offset/Scale
- Runs on compositor thread, zero per-frame managed code
- Only managed callback is the batch completion that triggers DOM removal
- Exit animation keeps element in tree temporarily — same approach as Flux's
  `TransitionViewContainer` and WinUI's own theme transitions

### Key files

| File | Change |
|------|--------|
| `Reactor\Animation\Transition.cs` | New — transition type hierarchy |
| `Reactor\Core\Element.cs` | Modify — add `ElementTransition?` property to Element base |
| `Reactor\Core\Reconciler.cs` | Modify — animate on mount, delay unmount on exit |
| `Reactor\Elements\ElementExtensions.cs` | Modify — `.Transition()` fluent modifier |
| `Reactor\Core\Navigation\TransitionEngine.cs` | Reuse — compositor animation patterns |

---

## Feature 3: Compositor Property Animator (`.Animate()` modifier)

**Gap closed**: Implicit transitions limited to 5 properties with no easing (review gaps #1, #5)
**Category**: Better WinUI exposure
**Wraps**: `ImplicitAnimationCollection` on the element's composition Visual

### API

```csharp
// Spring-animate all visual property changes on this element
Border(child)
    .Opacity(isHovered ? 1.0 : 0.7)
    .Scale(isPressed ? 0.95f : 1.0f)
    .Animate(Curve.Spring(0.65f))

// Targeted: only animate specific properties
Border(child)
    .Animate(Curve.Spring(0.65f), properties: AnimateProperty.Opacity | AnimateProperty.Scale)

// Ease with custom cubic bezier
Panel(child)
    .Animate(Curve.Ease(200, Easing.CubicBezier(0.2f, 0f, 0f, 1f)))
```

### Comparison to existing implicit transitions

| Aspect | `.OpacityTransition()` | `.Animate(Curve.Spring())` |
|--------|----------------------|---------------------------|
| Curve control | Duration only (WinUI `ScalarTransition`) | Full: spring, ease with bezier, linear |
| Properties | One modifier per property | All visual properties at once |
| Mechanism | WinUI implicit transition (UIElement) | Composition `ImplicitAnimationCollection` (Visual) |
| Stacking | Can't combine with layout animation's ImplicitAnimations | Merges into same collection |

### How it works

1. `AnimationConfig` record on `Element` base, parallel to `LayoutAnimationConfig`.
2. The reconciler's new `ApplyPropertyAnimation()` method creates an
   `ImplicitAnimationCollection` on the element's composition Visual (via
   `ElementCompositionPreview.GetElementVisual()`) with entries for Opacity,
   Offset, Scale, Rotation, CenterPoint — using the specified curve.
3. This uses the **exact same mechanism** as `ApplyLayoutAnimation()` (which
   already creates `ImplicitAnimationCollection` entries for Offset and Size).
   The two collections merge: layout animations handle Offset/Size from layout
   changes, property animations handle Opacity/Scale/Rotation from modifier changes.

### Relationship to Feature 1 (WithAnimation)

- **Feature 1** is mutation-site: "animate this state change" (imperative)
- **Feature 3** is declaration-site: "this element always animates" (declarative)
- **Priority**: mutation-site `WithAnimation` scope > declaration-site `.Animate()`
  > no animation. The scope is a specific, contextual instruction that overrides
  the declaration-site default. This matches SwiftUI's model: `withAnimation(.spring)`
  overrides any `.animation()` modifier on the view. Flux uses the opposite order,
  but in practice users expect "I explicitly asked for spring on this state change"
  to win over "this element generally animates with ease."

### Performance

Identical to existing `LayoutAnimationConfig` — composition implicit animations
created once at mount/config-change. Zero per-frame managed code. Animation
objects live on the compositor and fire automatically when targeted properties change.

### Animatable properties

```csharp
[Flags]
public enum AnimateProperty
{
    Opacity     = 1 << 0,
    Offset      = 1 << 1,   // Translation
    Scale       = 1 << 2,
    Rotation    = 1 << 3,
    CenterPoint = 1 << 4,
    All         = Opacity | Offset | Scale | Rotation | CenterPoint,
}
```

### Key files

| File | Change |
|------|--------|
| `Reactor\Animation\AnimateProperty.cs` | New — flags enum |
| `Reactor\Core\Element.cs` | Modify — add `AnimationConfig?` to Element base |
| `Reactor\Core\Reconciler.cs` | Modify — `ApplyPropertyAnimation()` parallel to `ApplyLayoutAnimation()` |
| `Reactor\Elements\ElementExtensions.cs` | Modify — `.Animate()` fluent modifier |

---

## Feature 4: Staggered Children Animation

**Gap closed**: No staggered/sequenced animation for collections
**Category**: Framework value-add

### API

```csharp
// Children enter with 40ms stagger between each
VStack(
    items.Select(item => Card(item).WithKey(item))
).Stagger(TimeSpan.FromMilliseconds(40))
 .LayoutAnimation()

// Stagger with explicit curve
FlexRow(
    items.Select(item => Thumbnail(item).WithKey(item))
).Stagger(TimeSpan.FromMilliseconds(30), Curve.Spring(0.7f))

// Composes with enter/exit transitions (Feature 2)
VStack(
    items.Select(item =>
        Text(item).WithKey(item)
                  .Transition(Transition.Fade + Transition.Slide(Edge.Start)))
).Stagger(TimeSpan.FromMilliseconds(50))
```

### How it works

1. `StaggerConfig` record stored on container elements:
   ```csharp
   public record StaggerConfig(TimeSpan Delay, Curve? Curve = null);
   ```

2. On mount, the reconciler assigns each child's composition animation a
   `DelayTime` of `childIndex * staggerDelay`. This applies to:
   - Enter transitions (Feature 2) — each child's enter animation starts later
   - Layout animations — each child's implicit offset animation starts later
   - Property animations (Feature 3) — each child's visual animation starts later

3. On layout reorder, stagger delays are recomputed based on new child positions.

### Performance

Uses the `DelayTime` property on WinUI `CompositionAnimation` — the delay is
evaluated entirely on the compositor. No managed timers, no per-child dispatch.
The reconciler computes `index * delay` once at mount/reorder time.

### Key files

| File | Change |
|------|--------|
| `Reactor\Core\Element.cs` | Modify — add `StaggerConfig?` to container element types |
| `Reactor\Core\Reconciler.cs` | Modify — apply `DelayTime` in animation setup paths |
| `Reactor\Elements\ElementExtensions.cs` | Modify — `.Stagger()` fluent modifier |

---

## Feature 5: Keyframe Animation Builder

**Gap closed**: No keyframe or sequenced animation DSL (review gap #4)
**Category**: Better WinUI exposure
**Wraps**: `KeyFrameAnimation.InsertKeyFrame()`

### API

```csharp
// Attention-seeking "pulse" on a badge — re-triggers whenever count changes
Badge(count)
    .Keyframes("pulse", trigger: count, keyframes => keyframes
        .Duration(600)
        .At(0.0f, scale: Vector3.One)
        .At(0.4f, scale: new Vector3(1.3f, 1.3f, 1f), easing: Easing.Decelerate)
        .At(0.7f, scale: new Vector3(0.95f, 0.95f, 1f))
        .At(1.0f, scale: Vector3.One, easing: Easing.Accelerate))

// Entrance animation — triggers once when mountId is assigned
Card(content)
    .Keyframes("enter", trigger: mountId, keyframes => keyframes
        .Duration(400)
        .At(0.0f, opacity: 0f, translation: new Vector3(0, 20, 0))
        .At(0.6f, opacity: 1f)
        .At(1.0f, translation: Vector3.Zero))

// Looping shimmer/loading animation — re-triggers on any isLoading change (true↔false)
Placeholder()
    .Keyframes("shimmer", trigger: isLoading, keyframes => keyframes
        .Duration(1200)
        .Loop()
        .At(0.0f, opacity: 0.3f)
        .At(0.5f, opacity: 0.7f)
        .At(1.0f, opacity: 0.3f))
```

### How it works

1. The builder collects keyframe data into an immutable `KeyframeAnimationDef` record:
   ```csharp
   public record KeyframeAnimationDef
   {
       public TimeSpan Duration { get; init; }
       public bool Loop { get; init; }
       public KeyframeDef[] Keyframes { get; init; }
   }

   public record KeyframeDef(float Progress)
   {
       public float? Opacity { get; init; }
       public Vector3? Scale { get; init; }
       public Vector3? Translation { get; init; }
       public float? Rotation { get; init; }
       public Easing? Easing { get; init; }
   }
   ```

2. The reconciler maps each property track to a separate WinUI `ScalarKeyFrameAnimation`
   or `Vector3KeyFrameAnimation`, calling `InsertKeyFrame(progress, value, easing)`
   for each keyframe entry.

3. All property animations are grouped into a `CompositionAnimationGroup` and
   started together via `UIElement.StartAnimation()`.

4. The `trigger:` parameter accepts any equatable value. When the value changes
   between renders (determined by `object.Equals` comparison against the previous
   value), the animation plays. This avoids the fragility of boolean edge-detection
   — no need to maintain a separate "changed" flag. For one-shot animations, pass
   a value that only changes once (e.g., a mount ID). For repeating animations,
   pass the value that should trigger replay (e.g., a counter). Matches the
   semantics of SwiftUI's `KeyframeAnimator(trigger:)` and Compose's
   `LaunchedEffect(key)`.

### Performance

Direct 1:1 mapping to WinUI's `KeyFrameAnimation` — each `At()` call becomes one
`InsertKeyFrame()`. Zero abstraction overhead. Runs on compositor thread. Looping
uses `IterationBehavior = Forever` on the compositor, no managed timer.

### Key files

| File | Change |
|------|--------|
| `Reactor\Animation\KeyframeBuilder.cs` | New — builder API + data records |
| `Reactor\Core\Element.cs` | Modify — keyframe animation storage on Element |
| `Reactor\Core\Reconciler.cs` | Modify — trigger keyframe animations on mount/state change |
| `Reactor\Elements\ElementExtensions.cs` | Modify — `.Keyframes()` fluent modifier |

---

## Feature 6: Scroll-Linked Expression Animation

**Gap closed**: No input-driven animation capability
**Category**: Better WinUI exposure
**Wraps**: `ExpressionAnimation` + `ElementCompositionPreview.GetScrollViewerManipulationPropertySet()`

### API

```csharp
// Parallax header: translates at half scroll speed, fades out as you scroll
Image(headerUrl)
    .ScrollLinked(scrollViewerRef, scroll => scroll
        .Parallax(factor: 0.5f)                      // Offset.Y = scroll.Y * 0.5
        .FadeOut(startOffset: 0, endOffset: 200))     // Opacity lerps 1->0

// Sticky header that shrinks as you scroll
Header(title)
    .ScrollLinked(scrollViewerRef, scroll => scroll
        .ScaleRange(scrollStart: 0, scrollEnd: 400, from: 1.0f, to: 0.3f))

// Custom expression for advanced scenarios
Element(child)
    .ScrollLinked(scrollViewerRef, scroll => scroll
        .Expression("Opacity", "Clamp(1.0 + (scroll.Translation.Y / 300), 0, 1)"))
```

### Pre-built expression templates

| Helper | Expression | Use case |
|--------|-----------|----------|
| `.Parallax(factor)` | `Offset.Y = scroll.Translation.Y * factor` | Parallax backgrounds |
| `.FadeOut(start, end)` | `Opacity = Lerp(1, 0, Clamp((scroll.Y - start)/(end - start), 0, 1))` | Fade on scroll |
| `.FadeIn(start, end)` | Inverse of FadeOut | Reveal on scroll |
| `.ScaleRange(start, end, from, to)` | `Scale = Lerp(from, to, Clamp(...))` | Shrinking headers |
| `.Expression(prop, expr)` | Custom expression string | Full flexibility |

### How it works

1. On mount, the reconciler gets the ScrollViewer's manipulation property set via
   `ElementCompositionPreview.GetScrollViewerManipulationPropertySet(scrollViewer)`.

2. For each configured expression, it creates a `compositor.CreateExpressionAnimation()`
   with the expression string, sets the "scroll" reference parameter to the
   property set, and starts it on the target element's Visual.

3. The pre-built helpers (`.Parallax()`, `.FadeOut()`, etc.) generate the correct
   expression string — they're compile-time templates, not runtime overhead.

### Performance

This is the **highest-performance animation type possible** in WinUI. The
compositor evaluates a mathematical expression every frame (at display refresh
rate) with zero round-trips to managed code. This is the same mechanism WinUI
uses internally for its own parallax and reveal effects.

### Key files

| File | Change |
|------|--------|
| `Reactor\Animation\ScrollAnimation.cs` | New — builder + expression templates |
| `Reactor\Core\Element.cs` | Modify — add scroll-linked animation storage |
| `Reactor\Core\Reconciler.cs` | Modify — apply expression animations on mount/update |
| `Reactor\Elements\ElementExtensions.cs` | Modify — `.ScrollLinked()` fluent modifier |

---

## Feature 7: Animation Choreography (`WithAnimationAsync`)

**Gap closed**: No way to sequence or coordinate multi-phase animations
**Category**: Framework value-add
**Inspired by**: Flux `AnimateAsync`

### API

```csharp
// Sequential: fly out old content, then fly in new content
await Animation.WithAnimationAsync(Curve.Ease(200), () =>
{
    isOldVisible.Value = false;
});
// ^ Task completes when all compositor animations from the scope finish

await Animation.WithAnimationAsync(Curve.Spring(0.7f), () =>
{
    isNewVisible.Value = true;
});

// Parallel + sequential using standard C# Task combinators
await Task.WhenAll(
    Animation.WithAnimationAsync(Curve.Ease(150), () => opacity1.Value = 0),
    Animation.WithAnimationAsync(Curve.Ease(150), () => opacity2.Value = 0)
);
await Animation.WithAnimationAsync(Curve.Spring(0.8f), () => showResults.Value = true);
```

### How it works

1. `WithAnimationAsync()` creates a `CompositionScopedBatch` **before** setting
   the `[ThreadStatic]` scope and running the action.

2. All `StartAnimation` calls triggered by the reconciler (via `AnimationHelper.SetOrAnimate`)
   are captured by the open batch.

3. After the action completes synchronously, the batch is ended (`batch.End()`).

4. Returns a `Task` that completes on `batch.Completed`.

5. Standard C# `await` provides sequencing. `Task.WhenAll` provides parallel
   coordination. No custom choreography DSL needed.

### Implementation sketch

```csharp
public static Task WithAnimationAsync(Curve? curve, Action action)
{
    // Need a compositor reference — get from current window/thread
    var compositor = CompositorProvider.Current;
    var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
    var tcs = new TaskCompletionSource();

    WithAnimation(curve, action);

    batch.End();
    batch.Completed += (_, _) => tcs.SetResult();
    return tcs.Task;
}
```

This pattern is already proven in `TransitionEngine.cs:40-76`.

### Performance

`CompositionScopedBatch` is a lightweight WinUI compositor primitive. The only
managed callback is the single completion event. All animation runs on the
compositor thread during the batch.

### Key files

| File | Change |
|------|--------|
| `Reactor\Animation\AnimationScope.cs` | Modify — add `WithAnimationAsync()` |
| `Reactor\Core\Navigation\TransitionEngine.cs` | Reference — existing batch pattern |

---

## Feature 8: Interaction States (Zero-Reconcile Hover/Press)

**Gap closed**: VSM replacement is expensive — full reconcile for hover (review gap #8)
**Category**: Framework value-add
**Inspired by**: CSS `:hover`/`:active` pseudo-classes, SwiftUI `.hoverEffect()`

### Problem

Today, hover/pressed visual feedback in Reactor requires state variables and a
full reconcile cycle:

```csharp
var isHovered = UseState(false);
var isPressed = UseState(false);

Button("Click me")
    .Opacity(isPressed ? 0.7 : isHovered ? 0.85 : 1.0)
    .Scale(isPressed ? 0.97f : isHovered ? 1.02f : 1.0f)
    .Background(isPressed ? pressedBrush : isHovered ? hoverBrush : normalBrush)
    .OnPointerEntered(() => isHovered.Value = true)
    .OnPointerExited(() => isHovered.Value = false)
    .OnPointerPressed(() => isPressed.Value = true)
    .OnPointerReleased(() => isPressed.Value = false)
```

This is 8 lines of boilerplate for the most common interactive pattern in UI.
Every nav item, every card, every list row, every button needs some variant of
this. The reconcile cost per hover event is small (especially with
skip-unchanged-elements), but it's real work the framework shouldn't need to do
for a pre-known state machine with pre-known values.

### Design: reconciler-delegated state machine

InteractionStates is not "bypassing the reconciler." It is the **reconciler
delegating to a pre-configured state machine** — the same pattern it already uses
for compositor implicit animations. The reconciler:

1. **Sets up the rules** at mount time (Normal=these values, Hover=those values,
   Pressed=those values)
2. **Registers the event handlers** that transition between states
3. **Can restore consistency** at any time because it knows all states and their
   values
4. **Tears down everything** on unmount

The pointer handlers just pick between values the reconciler already knows about.
This is the same relationship the reconciler has with `ImplicitAnimationCollection`
— it sets the target, the compositor decides the current interpolated value, and
the reconciler doesn't track the in-between.

### Property tiers

InteractionStates supports two tiers of properties, with different mechanisms:

| Tier | Properties | Mechanism | Cost during interaction |
|------|-----------|-----------|----------------------|
| **Compositor** | Opacity, Scale, Translation, Rotation | `UIElement.StartAnimation()` with pre-built `KeyFrameAnimation` | Zero managed code |
| **Direct set** | Background, Foreground, BorderBrush | Direct property assignment on the UIElement with pre-cached brush | One UI-thread property set (~1μs) |

Both tiers share the same API and state machine. The implementation routes each
property to the appropriate mechanism.

### Design boundary: the layout line

**InteractionStates does NOT support layout-affecting properties.** No Width,
Height, Margin, Padding, CornerRadius, FontSize, or any property that triggers
a layout pass. This is the hard line.

The boundary is enforced structurally by the `InteractionStateValues` record —
only explicitly typed fields are supported. Adding a new property is a deliberate
API decision that goes through review, not a runtime escape hatch.

- **Visual properties** (opacity, scale, brush) = pre-known values, finite state
  machine, no layout impact → InteractionStates
- **Layout properties** (width, margin, padding) = triggers measure/arrange pass,
  potentially affects siblings → use normal state + reconcile
- **Structural changes** (show/hide children, change content) = inherently
  reconciler operations → use normal state

### API

```csharp
// Common hover effect — opacity, scale, and background in one declaration
Button("Click me")
    .InteractionStates(states => states
        .PointerOver(opacity: 0.85f, scale: 1.02f, background: hoverBrush)
        .Pressed(scale: 0.97f, background: pressedBrush))

// Nav item with translation shift on hover
NavItem(label)
    .InteractionStates(states => states
        .PointerOver(translation: new Vector3(4, 0, 0), foreground: accentBrush)
        .Pressed(opacity: 0.7f),
        curve: Curve.Spring(0.5f))

// Focused state for keyboard navigation accessibility
TextBox(value)
    .InteractionStates(states => states
        .PointerOver(opacity: 0.9f)
        .Focused(scale: 1.01f, borderBrush: focusBrush))

// Compositor-only (no brushes) — zero managed code during interaction
IconButton(icon)
    .InteractionStates(states => states
        .PointerOver(opacity: 0.85f, scale: 1.05f)
        .Pressed(scale: 0.95f))
```

### Comparison: before and after

**Before (state + reconcile):**
```csharp
var isHovered = UseState(false);
var isPressed = UseState(false);

Card(content)
    .Opacity(isPressed ? 0.7 : isHovered ? 0.85 : 1.0)
    .Scale(isPressed ? 0.97f : isHovered ? 1.02f : 1.0f)
    .Background(isPressed ? pressedBrush : isHovered ? hoverBrush : normalBrush)
    .OnPointerEntered(() => isHovered.Value = true)
    .OnPointerExited(() => isHovered.Value = false)
    .OnPointerPressed(() => isPressed.Value = true)
    .OnPointerReleased(() => isPressed.Value = false)
```

**After (InteractionStates):**
```csharp
Card(content)
    .InteractionStates(states => states
        .PointerOver(opacity: 0.85f, scale: 1.02f, background: hoverBrush)
        .Pressed(opacity: 0.7f, scale: 0.97f, background: pressedBrush))
```

Same visual result. No state variables, no event handler wiring, no reconcile.

### Interaction state inheritance

Pressed is a sub-state of PointerOver. Properties specified on PointerOver but
not overridden on Pressed are inherited:

```csharp
.InteractionStates(states => states
    .PointerOver(opacity: 0.85f, scale: 1.02f, background: hoverBrush)
    .Pressed(scale: 0.97f, background: pressedBrush))
// Pressed effective state: opacity = 0.85 (inherited), scale = 0.97 (overridden),
//                          background = pressedBrush (overridden)
```

This matches CSS specificity (`:active` inherits from `:hover`) and WinUI's
CommonStates group behavior.

### State machine

```
Normal ──pointer enter──▸ PointerOver ──pointer down──▸ Pressed
  ▴                           ▴                            │
  └────pointer exit───────────┘◂───pointer up──────────────┘
                              └────capture lost────────────┘
```

Each transition starts pre-built compositor animations for facade properties and
applies direct property sets for brushes. The animations are created once;
transitions are just `StartAnimation()` calls + brush assignments.

### How it works

1. `InteractionStatesConfig` record stored on Element, parallel to other animation
   configs:
   ```csharp
   public record InteractionStatesConfig(
       InteractionStateValues? PointerOver = null,
       InteractionStateValues? Pressed = null,
       InteractionStateValues? Focused = null,
       Curve? Curve = null);

   public record InteractionStateValues(
       // Compositor-accelerated (zero cost during interaction)
       float? Opacity = null,
       float? Scale = null,        // uniform scale (convenience)
       Vector3? ScaleV = null,     // non-uniform scale
       Vector3? Translation = null,
       float? Rotation = null,
       // Direct property set (pre-cached brush swap, ~1μs)
       Brush? Background = null,
       Brush? Foreground = null,
       Brush? BorderBrush = null);
       // That's it. No Width, no Margin, no CornerRadius. Ever.
       // The record IS the boundary — adding a field is an API decision.
   ```

2. On mount (or config change), the reconciler:
   - Registers `PointerEntered`, `PointerExited`, `PointerPressed`,
     `PointerReleased`, and `PointerCaptureLost` handlers on the UIElement
   - Creates `KeyFrameAnimation` objects for compositor properties using the
     specified curve, cached for reuse across state transitions
   - Captures the element's current brush values as the "Normal" state baseline

3. Event handlers (registered once, not re-registered on re-render):
   ```csharp
   void OnPointerEntered(object sender, PointerRoutedEventArgs e)
   {
       // Compositor properties — zero managed code after this call
       element.StartAnimation("Opacity", _hoverOpacityAnim);
       element.StartAnimation("Scale", _hoverScaleAnim);

       // Brush properties — direct set, pre-cached, ~1μs
       if (_hoverBackground is not null)
           element.Background = _hoverBackground;
   }
   ```
   No state update, no reconcile, no allocation.

4. **Reconciler consistency**: when the reconciler runs (because *other* state
   changed), it checks the element's current interaction state before applying
   properties that InteractionStates manages. If the element is in PointerOver
   state, the reconciler writes the PointerOver brush values, not the Normal
   values. This ensures a reconcile triggered by unrelated state doesn't flash
   the element back to Normal.

5. On unmount, handlers are unregistered and animation objects are released.

### What InteractionStates replaces

| Pattern today | With InteractionStates |
|--------------|----------------------|
| `UseState` × 2 + 4 event handlers + ternary chains for hover/pressed | Single `.InteractionStates()` modifier |
| VSM `PointerOver`/`Pressed` states in custom control templates | Same `.InteractionStates()` modifier |
| Manual `Visual.StartAnimation()` via `.Set()` for hover effects | Declarative, reconciler-managed |

### What InteractionStates does NOT replace

- **Structural changes on hover** (show tooltip, expand menu) → use normal state.
  These are inherently reconciler operations.
- **Complex multi-property state changes** (disabled + error + selected
  combinations) → use normal state. These are component mode states, not
  interaction states.
- **Layout changes on hover** (expand width, change padding) → use normal state.
  Layout = reconcile. Always.

### Performance

- **During interaction (compositor-only)**: zero managed code. Pre-built
  compositor animations start via `UIElement.StartAnimation()`.
- **During interaction (with brushes)**: one UI-thread property set per brush
  (~1μs per brush). No reconcile, no state change, no allocation. Brushes are
  pre-cached per the existing brush caching infrastructure.
- **On mount**: 3-5 event handler registrations + animation object creation. Same
  cost tier as `ApplyLayoutAnimation()`.
- **Memory**: ~6-8 cached `KeyFrameAnimation` objects per element with
  InteractionStates (compositor properties). Brush references are just pointers
  to existing cached brush objects.

### Key files

| File | Change |
|------|--------|
| `Reactor\Animation\InteractionStates.cs` | New — config records, animation factory, state machine handler |
| `Reactor\Core\Element.cs` | Modify — add `InteractionStatesConfig?` property |
| `Reactor\Core\Reconciler.cs` | Modify — register handlers, create/cache animations on mount, respect interaction state during modifier application |
| `Reactor\Elements\ElementExtensions.cs` | Modify — `.InteractionStates()` fluent modifier |

---

## Implementation Priority

| Phase | Feature | Impact | Complexity | Dependencies |
|-------|---------|--------|------------|-------------|
| 0 | Curve type hierarchy | Foundation | Low | None |
| 1 | WithAnimation scope | Highest | Medium | Curve |
| 1 | .Animate() modifier | High | Medium | Curve |
| 1 | InteractionStates | High | Medium | Curve |
| 2 | Enter/Exit transitions | High | Medium-High | Curve, reconciler unmount delay |
| 2 | WithAnimationAsync | Medium | Low | WithAnimation scope |
| 3 | Staggered children | Medium | Low | Enter/Exit or LayoutAnimation |
| 3 | Keyframe builder | Medium | Medium | Curve |
| 3 | Scroll-linked expressions | Medium | Medium | Independent |

Phase 0 ships the `Curve` and `Easing` types that all other features depend on.
Phase 1 delivers the three highest-impact features — WithAnimation, .Animate(),
and InteractionStates address the most common animation needs and the hover/pressed
performance gap. Phase 2 adds lifecycle animation and sequencing. Phase 3 rounds
out the system with advanced capabilities.

---

## New File Summary

| File | Purpose |
|------|---------|
| `Reactor\Animation\Curve.cs` | Curve/Easing type hierarchy |
| `Reactor\Animation\AnimationScope.cs` | `WithAnimation` / `WithAnimationAsync` |
| `Reactor\Animation\AnimationHelper.cs` | `SetOrAnimate` — routes property sets through compositor |
| `Reactor\Animation\Transition.cs` | Enter/exit transition type hierarchy |
| `Reactor\Animation\AnimateProperty.cs` | Flags enum for `.Animate()` property targeting |
| `Reactor\Animation\KeyframeBuilder.cs` | Keyframe builder API + data records |
| `Reactor\Animation\ScrollAnimation.cs` | Scroll-linked expression builder + templates |
| `Reactor\Animation\InteractionStates.cs` | Interaction state config records + animation factory |

## Modified File Summary

| File | Changes |
|------|---------|
| `Reactor\Core\Element.cs` | Add `ElementTransition?`, `AnimationConfig?`, `InteractionStatesConfig?`, stagger, keyframe, scroll-linked properties |
| `Reactor\Core\Reconciler.cs` | `ApplyModifiers` → animate via scope; `ApplyPropertyAnimation`; enter/exit lifecycle; stagger delays; keyframe triggers; scroll expression setup; interaction state handler registration |
| `Reactor\Elements\ElementExtensions.cs` | `.Transition()`, `.Animate()`, `.InteractionStates()`, `.Stagger()`, `.Keyframes()`, `.ScrollLinked()` fluent modifiers |

---

## Verification Plan

- **Unit tests**: Verify compositor animations are created with correct parameters.
  Follow the pattern in `tests\Reactor.AppTests.Host\SelfTest\Fixtures\LayoutAnimationFixtures.cs`
  — get the Visual's ImplicitAnimations, assert animation type and target.
- **Demo app**: Extend `docs\apps\animation\App.cs` with interactive sections for
  each feature.
- **Test app**: Add sections to `samples\Reactor.TestApp\App.cs` transitions page
  (~line 1370+).
- **Performance**: Profile with StressPerf to verify zero UI-thread work during
  animation playback. Key metric: no managed allocations or callbacks between
  animation start and completion.

---

## Competitive Comparison (Post-Implementation)

| Capability | Reactor (current) | Reactor (proposed) | SwiftUI | Compose | Flux.UI |
|-----------|---------------|----------------|---------|---------|---------|
| Animate any state change | No | **WithAnimation scope** | withAnimation | animateAsState | Animation.Animate |
| Per-element enter/exit | No | **Transition modifier** | .transition() | AnimatedVisibility | TransitionElement |
| Custom curves/springs | Layout only | **All visual properties** | Any | Any | Any |
| Hover/pressed (no re-render) | No (full reconcile) | **InteractionStates** | .hoverEffect() | Indication | VSM |
| Staggered collections | No | **Stagger modifier** | Custom | Custom | Stagger param |
| Keyframe animations | .Set() only | **Keyframe builder** | KeyframeAnimator | keyframes {} | N/A |
| Scroll-linked | No | **Expression animation** | ScrollViewReader | nestedScrollConnection | N/A |
| Animation sequencing | No | **WithAnimationAsync** | Explicit | LaunchedEffect | AnimateAsync |
| Compositor-thread | Yes | **Yes (all features)** | Core Animation | RenderThread | Yes |

Expected grade improvement: **C → A-**. The remaining gap to A is animating
non-facade properties (Width, Margin, etc.) which requires WinUI platform changes.
