# Spec 066 — Transform modifiers on the composition visual

> Status: **accepted** (2026-11-20), not yet implemented. Rollout in §13. Originating issue: [#1630](https://github.com/microsoft/microsoft-ui-reactor/issues/1630).
> Plans fixes for [#1623](https://github.com/microsoft/microsoft-ui-reactor/issues/1623),
> [#1624](https://github.com/microsoft/microsoft-ui-reactor/issues/1624),
> [#1625](https://github.com/microsoft/microsoft-ui-reactor/issues/1625),
> [#1626](https://github.com/microsoft/microsoft-ui-reactor/issues/1626) and
> [#773](https://github.com/microsoft/microsoft-ui-reactor/issues/773).
> Builds on #1553, #1620, #1621 and #1627 (all on `main` @ `0c5edca6b`).
> Every runtime claim below was measured by a scratch selftest spike against that commit. The spike
> was not committed, and §11 lists its results.

## Table of contents

- [§1 Problem](#1-problem)
- [§2 Goals and non-goals](#2-goals-and-non-goals)
- [§3 Channel model](#3-channel-model)
- [§4 Modifier behaviour](#4-modifier-behaviour)
- [§5 Implicit transitions, compositor only](#5-implicit-transitions-compositor-only)
- [§6 ImplicitAnimations ownership](#6-implicitanimations-ownership)
- [§7 Pooling elements whose visual was taken](#7-pooling-elements-whose-visual-was-taken)
- [§8 `.TransformOrigin(x, y)`](#8-transformoriginx-y)
- [§9 Compatibility and breaking changes](#9-compatibility-and-breaking-changes)
- [§10 AOT and trimming](#10-aot-and-trimming)
- [§11 Measurements](#11-measurements)
- [§12 Test plan](#12-test-plan)
- [§13 Rollout](#13-rollout)
- [§14 Rejected alternatives](#14-rejected-alternatives)
- [§15 Open questions](#15-open-questions)
- [§16 Other defects found by the spike](#16-other-defects-found-by-the-spike)

---

## §1 Problem

Reactor writes transforms through three WinUI channels.

| Channel | Used by today |
|---|---|
| `UIElement` facades (`Scale`, `Rotation`, `Translation`, `CenterPoint`) and the XAML implicit transitions (`OpacityTransition`, `ScaleTransition`, `RotationTransition`, `TranslationTransition`) | `.Scale()`, `.Rotation()`, `.Translation()`, `.CenterPoint()` without a curve, and the four `.XxxTransition()` modifiers |
| The handoff `Visual` from `ElementCompositionPreview.GetElementVisual` | `.Animate()`, `.InteractionStates()`, `.Transition()`, `.Keyframes()`, `.LayoutAnimation()`, `.Stagger()`, navigation transitions, and the transform modifiers once the visual has been taken (#1553) |
| `RenderTransform` | `PathElement` only (the D3 chart helpers) |

WinUI makes the first channel mutually exclusive with the other two and enforces it with
`UnauthorizedAccessException` (microsoft/microsoft-ui-xaml#2424, #4931, #9190). #1620 and #1627 work
around that at each handoff site. `AnimationHelper.GetElementVisual` moves non-identity facade values
to the visual before taking it, and the pool resets facades and refuses tainted controls. Two
channels stay alive, and every new compositor feature has to remember the migration.

Four open bugs still come from that split.

- **#1623.** Any XAML implicit transition blocks `GetElementVisual`, so `.OpacityTransition()` plus
  `.LayoutAnimation()`, `.Animate()`, `.Transition()`, `.InteractionStates()` or `.Keyframes()` throws.
- **#1624.** The pool never resets `OpacityTransition` or `BackgroundTransition`, so they leak to the next renter.
- **#1625.** `ApplyLayoutAnimation` replaces `Visual.ImplicitAnimations` and drops the transition fallback keys.
- **#1626.** Removing `.OpacityTransition()`, `.ScaleTransition()` or `.RotationTransition()` never removes the transition.

#773 asks for a size-relative transform origin. That origin is an expression on `Visual.Size`, and the
facade channel can't run one (the #773 report, E5).

## §2 Goals and non-goals

Goals:

1. One channel for every transform Reactor writes, the handoff visual, as the Windows Community
   Toolkit does with `VisualExtensions` and the Composition layer of `AnimationBuilder`.
2. One implementation of the implicit transitions on the compositor. It matches the XAML transitions'
   curve, duration, axis mask and reduced-motion behaviour (§5).
3. Implicit-animation keys that several features can share and remove independently (#1625, #1626).
4. Controls whose visual was taken can go back to the pool (#1624 and §7).
5. A `.TransformOrigin(x, y)` modifier (#773).
6. No change in rendering, hit-testing, UIA bounds or RTL for an app that uses only modifiers (§11).

Non-goals:

- `RenderTransform` stays as it is. `PathElement` keeps using it. It coexists with the visual (#1630
  measurements, P1f) and offers skew and `TransformGroup`, which the visual lacks.
- No change of the default pivot. It stays at (0,0) (§15).
- `BackgroundTransition` stays a XAML transition (§5.6).
- `ThemeTransitions` (`ChildrenTransitions` and friends) are unaffected (Q16).
- The `Curve.Linear` easing defect (§16) is tracked separately.

## §3 Channel model

### Before

```mermaid
flowchart LR
  M[".Scale/.Rotation/.Translation/.CenterPoint"] -->|no curve, visual not taken| F[UIElement facades]
  M -->|curve, or visual taken| V[Handoff Visual]
  T[".XxxTransition()"] -->|visual not taken| X[UIElement.XxxTransition]
  T -->|UnauthorizedAccess| C[ImplicitAnimations fallback]
  A[".Animate/.InteractionStates/.Transition/.Keyframes/.LayoutAnimation"] --> H["AnimationHelper.GetElementVisual (migrates facades)"] --> V
  X -.blocks.-> H
  F -.blocks unless migrated.-> H
```

### After

```mermaid
flowchart LR
  M[".Scale/.Rotation/.Translation/.CenterPoint/.TransformOrigin"] --> V[Handoff Visual]
  O[".Opacity()"] --> UO[UIElement.Opacity = Visual.Opacity]
  T[".Opacity/.Scale/.RotationTransition()"] --> K["ImplicitAnimations keys (owned, §6)"] --> V
  TT[".TranslationTransition()"] --> W["write-time animation on Translation (§5.3)"] --> V
  B[".BackgroundTransition()"] --> BX[Panel.BackgroundTransition, XAML]
  A[".Animate/.InteractionStates/.Transition/.Keyframes/.LayoutAnimation"] --> V
  P[PathElement] --> RT[RenderTransform]
```

Invariants:

1. Reactor never writes `UIElement.Scale`, `Rotation`, `CenterPoint`, `RotationAxis`,
   `TransformMatrix` or any `UIElement.*Transition` property. `Translation` is covered in §4.3.
2. Reactor takes the handoff visual lazily. The first transform write that changes the *effective*
   value, the first transition declaration, or the first compositor feature takes it, and nothing else
   does. A `.Rotation(isOpen ? 90 : 0)` that has only ever been 0 on an untouched control costs nothing
   and stays in the plain pool lane (§7). "Effective" matters. If a setter left a facade non-identity,
   for example `.Set(b => b.Scale = new(2)).Scale(1)`, the identity write still takes the visual (the
   handoff migrates the setter's value) and then overwrites it, so the modifier keeps winning as it
   does today.
3. `AnimationHelper.GetElementVisual` stays the single place that takes the visual. Its facade
   migration (#1627) stays as well, but its job becomes moving a value that *user code* wrote through
   `.Set(...)` before the handoff. Reactor no longer writes those facades itself.
4. `UIElement.Opacity` stays the opacity channel. The two opacities don't multiply (Q4d). XAML pushes
   `UIElement.Opacity` into the handoff visual's `Opacity` when it commits a change, which overwrites an
   earlier direct visual write (Q4d: UIElement 0.5 then visual 1 settles at 0.5). Writing it fires the
   visual's implicit `Opacity` animation (Q4c). So static `.Opacity()` doesn't need the visual, and
   Reactor must not keep an independent opacity value on the visual. A later `UIElement.Opacity` write
   would replace it. Q4d did not test both write orders against both getters after a second commit, so
   §12 adds that test before anything relies on a stronger equivalence.

## §4 Modifier behaviour

All four transform modifiers share one write path in `AnimationHelper`. `SetScalarDirect` and
`SetVector3Direct` lose their facade branch, and `ShouldWriteToVisual` disappears.

| Modifier | Set | Update | Unset | With a curve (`.Animate`, `WithAnimation`) |
|---|---|---|---|---|
| `.Scale(v)` | `Visual.Scale = v` | same | `Visual.Scale = One` after `StopAnimation("Scale")` | explicit animation on `Scale` (unchanged) |
| `.Rotation(deg)` | `Visual.RotationAngle = rad(deg)` | same | `RotationAngle = 0` | explicit animation on `RotationAngle`, radians (#1621, unchanged) |
| `.Translation(x,y,z)` | `SetIsTranslationEnabled(true)` once, then `Properties["Translation"] = v` | same | `Properties["Translation"] = Zero` | explicit animation on `Translation` (unchanged) |
| `.CenterPoint(v)` | static `Visual.CenterPoint = v`, or the origin expression's offset when `.TransformOrigin` is set (§8) | same | `Zero`, or offset `Zero` | explicit animation on `CenterPoint`, or on the `ReactorOriginOffset` property when an origin is set, so the expression survives (§8.2) |
| `.Opacity(o)` | `UIElement.Opacity = o` (no handoff) | same | `ClearValue(Opacity)` | explicit animation on `Opacity` (unchanged) |

Details:

- **Rotation writes `RotationAngle`, never `RotationAngleInDegrees`.** An implicit `RotationAngle` key
  doesn't fire for a `RotationAngleInDegrees` write, and the reverse is also true (Q3c, Q3e). Every
  Reactor write and key therefore uses `RotationAngle`, as the code has done since #1621.
- **Identity writes are skipped only when the effective value is already identity.** That holds when the
  visual hasn't been taken, the value is the identity (`Scale` One, `Rotation` 0, `Translation` Zero,
  `CenterPoint` Zero), *and* the matching facade still reads identity. When a user setter left the
  facade non-identity, the write takes the visual so the modifier overrides the setter (§3 invariant 2).
  Once the visual is taken, every write goes to it, including identity.
- **Rest values.** `ReactorState.TransformRest` (#1627) keeps the declared values that hover-out,
  `Transition.Scale` and `Keyframes` restore. It works unchanged, because it already reads values from
  declarations and not from the facades.
- **Decorators and keyed `Memo`.** `FindRemainingModifierOwner` and `GetRetainedModifierAnimationSource`
  keep working as they do now. Only the write target changes.
- **`ResetCompositionModifier`** loses its facade branch and its
  `catch (UnauthorizedAccessException)`. It keeps the `StopAnimation` + direct-write + suspended
  `ImplicitAnimations` sequence (P4 shows `StopAnimation` alone leaves the last animated value) and the
  loop-keyframe deferral.
- **`CompositionModifierProperties` / `PropertyFlag`.** They stay, but only to record which
  channels Reactor wrote, so the unset and pool reset know what to restore. They no longer pick a
  channel.

### §4.3 Translation

`UIElement.Translation` behaves differently from the other facades. It stays writable after the
handoff (Q8a), and it's a *separate* channel from the visual's `Translation` property, which is why the
two add up (Q8c). But once `SetIsTranslationEnabled(true)` has been applied and the element has
rendered, a later facade write is **silently ignored** (F1a, F1c). Without that flag, facade writes keep
working after the handoff (F1b).

Rules:

1. Reactor calls `SetIsTranslationEnabled(true)` only when `.Translation()` or a Translation channel
   (`Keyframes` `translation:`, `TranslationTransition`) is declared.
2. Reactor stops zeroing `UIElement.Translation` when it writes the visual
   (`AnimationHelper.SetVector3Direct`/`SetOrAnimateVector3`). A user facade value written before
   the handoff still gets migrated by `GetElementVisual`.
3. Docs warn that `fe.Translation` from user code doesn't combine with `.Translation()` (§9.3).

## §5 Implicit transitions, compositor only

`ApplyTransitions` stops writing `UIElement.OpacityTransition`, `ScaleTransition`, `RotationTransition`
and `TranslationTransition`. Its `catch (UnauthorizedAccessException)` fallback becomes the only path,
and §6 rewrites it on top of the key-ownership helper. Because the XAML properties are never set, the
handoff is never blocked, which fixes **#1623**.

### §5.1 Curve and duration parity

| | XAML transition (measured) | Fallback today | Proposed |
|---|---|---|---|
| Default duration | 300 ms (`ScalarTransition`, `Vector3Transition`) | the transition's `Duration` | the transition's `Duration` |
| Easing | cubic-bezier **(0.8, 0, 0.2, 1)**. `Q2a`, `Q2b` and `F4a` match `Q2e` sample for sample | none, so the compositor default (an ease-out, Q2c). It is *not* linear as #1630 assumed | `CubicBezierEasingFunction((0.8,0),(0.2,1))`, created once per compositor and cached |
| Start value | current presented value | `this.StartingValue` (implicit) | same |
| Fires for a write made before the element entered the tree | yes (F3) | yes (Q6f) | yes, parity |
| Fires when attached and written in the same tick | yes (F3b) | yes (F3c) | yes, parity |

### §5.2 `Components` (axis mask)

`Vector3Transition.Components` is honoured by XAML. With `Components = X`, X animates and Y jumps
(Q2f, Q2g). The fallback ignores it today. The proposal is a two-keyframe implicit animation. Keyframe
0 is `Vector3(c.X ? this.StartingValue.X : this.FinalValue.X, …)` and keyframe 1 is
`this.FinalValue` with the easing. Measured: X animates and Y jumps (F4b, F4c). When `Components` is
`X|Y|Z`, Reactor uses the single-keyframe form.

### §5.3 TranslationTransition

The XAML `TranslationTransition` animates writes to `UIElement.Translation` (Q1a) and does **not**
animate layout moves (Q1b). The fallback maps it to the visual's `Offset` key, so it animates layout
moves (Q1e) and not `.Translation()` (Q1d, Q1g). That's the wrong property.

The fix can't use an implicit key either. An implicit `"Translation"` key never fires for
`Properties.InsertVector3("Translation", …)`, neither on the first write nor on later ones (Q1c, F1d,
F1e). So `TranslationTransition` becomes a **write-time animation**. `AnimationHelper.ResolveCurve`
becomes `ResolvePolicy`, which returns a curve and a component mask. The source order is unchanged
(ambient `WithAnimation` scope, then `.Animate()`), with a declared `TranslationTransition` added last.
It yields `EaseCurve(Duration, 0.8,0,0.2,1)` with the transition's `Components`, so an explicit
`WithAnimation` or `.Animate()` still wins, as today. `SetOrAnimateVector3("Translation", …)` then
starts an explicit animation, as `.Animate()` already does. When the mask isn't `X|Y|Z`, the
animation gets a keyframe 0 of `Vector3(mask.X ? this.StartingValue.X : final.X, …)`, with `final`
passed as a parameter, so masked axes jump, as in §5.2. F4b/F4c validated the masked form only for an
implicit Scale key, so §12 adds an end-to-end Translation mask test. Retargeting mid-flight starts from
the presented value (F1g: 151 → 152 px), which matches XAML.

One difference remains. XAML animates *any* write to `UIElement.Translation`, including a write
from user code, while the write-time animation only covers Reactor's writes. That's acceptable,
because §4.3 already says user facade Translation and `.Translation()` don't mix.

### §5.4 Opacity and Rotation

- **Opacity.** An implicit `"Opacity"` key fires for both `Visual.Opacity` and `UIElement.Opacity` writes
  (Q4b, Q4c). So `.Opacity()` stays a plain `UIElement.Opacity` write and `.OpacityTransition()` installs
  the key. User code that writes `fe.Opacity` animates too, which is XAML parity.
- **Rotation.** The key is `"RotationAngle"` (Q3b) and every Reactor rotation write targets
  `RotationAngle` (§4).

### §5.5 Reduced motion

XAML implicit transitions honour the system "Animation effects" setting
(`SPI_GETCLIENTAREAANIMATION`/`UISettings.AnimationsEnabled`). With the setting off, all of them jump
(the `transitions-screen` run). With it on, they animate (`transitions-screen-anim-on`). Composition
implicit animations ignore the setting and animate either way. So a straight move would quietly break
reduced motion for every `.XxxTransition()` user.

`ApplyTransitions` therefore reads `TransitionEngine.AnimationsEnabled`, which navigation transitions
already use. When animations are off it installs no transition keys and resolves no
`TranslationTransition` curve. A change of the setting takes effect at the element's next
transition apply. Live re-application is an open question (§15). The other compositor
features (`.Animate()`, `.LayoutAnimation()`, `.Transition()`, `.Keyframes()`) already ignore the
setting today. This spec doesn't change them.

### §5.6 BackgroundTransition

`Grid/StackPanel/ContentPresenter.BackgroundTransition` doesn't block `GetElementVisual` in either order
(Q5a, Q5c, Q5d), and it still animates after the handoff (F2). A brush animation has no
equivalent on the handoff visual, so it stays a XAML property. Two changes are needed. The pool clears
it (§7), and an update that drops `.BackgroundTransition()` sets it back to null (§6.3).

## §6 ImplicitAnimations ownership

### §6.1 Rules

Measured platform facts:

- `Visual.ImplicitAnimations` returns the assigned instance (Q6a), and the collection is **live**.
  Adding a key (Q6c) or removing one (Q6b) takes effect without reassignment.
- One collection can be assigned to two visuals (Q6d). Reactor won't do that, because keys are per element.
- A `CompositionAnimationGroup` works as a key's value (Q6e).

Design: a small internal `ImplicitAnimationSlots` helper owns every write to
`Visual.ImplicitAnimations`. No Reactor code assigns the property or creates a collection anywhere
else.

```csharp
internal enum ImplicitFeature : byte { Transition = 1, Animate = 2, Layout = 3 } // higher wins
internal static class ImplicitAnimationSlots
{
    // Claim `key` for (`feature`, `declaration`). A lower-priority claim never replaces a higher one;
    // it is remembered and restored when the higher claim is released.
    public static void Claim(UIElement e, Visual v, string key, ImplicitFeature feature, Element declaration, CompositionAnimation anim);
    // Release only that claim. Restores the next effective claim, or removes the key.
    public static void Release(UIElement e, Visual v, string key, ImplicitFeature feature, Element declaration);
    public static void ReleaseAll(UIElement e, Visual v); // pool reset
}
```

The claims are kept in `ReactorState`, keyed by `(key, feature, declaration)`. The *feature*
(Transition < Animate < Layout) sets priority between features. The *declaration* is the element that
declared it. On one native control, that can be the target or a transparent decorator or keyed `Memo`
layered over it. Within one feature, the effective declaration is resolved across the shared-target
chain the same way `FindRemainingModifierOwner` resolves modifiers, with the outermost winning, and
removing it restores the inner one. The live collection is always derived from the claims. It's
created on first claim, mutated in place, and set to `null` when the last claim goes away.

The precedence keeps today's ordering. `LayoutAnimation` wins `Offset` and `Size` over `.Animate(Offset)`,
as `ApplyPropertyAnimation` does now. `.Animate()` wins `Opacity`, `Scale` and `RotationAngle` over a
transition, because today `ApplyPropertyAnimation` runs after `ApplyTransitions` and overwrites them.
When the winner leaves, the loser's animation comes back. Today it's simply lost.

### §6.2 Call sites

| Site | Today | Proposed |
|---|---|---|
| `ApplyLayoutAnimation` | assigns a fresh collection (#1625) | `Claim(Offset/Size, Layout, el)`, and `Release` for the channel that was turned off |
| `ClearLayoutAnimation` | `ImplicitAnimations = null` (#1625) | `Release(Offset, Layout, el)`, `Release(Size, Layout, el)` |
| `ApplyPropertyAnimation` / `ClearPropertyAnimation` | merge, or remove by key (removes others' keys too) | `Claim/Release(…, Animate, el)` |
| `ApplyTransitions` (Opacity/Scale/Rotation) | XAML, or the merging fallback, never removes (#1626) | `Claim/Release(…, Transition, el)` |
| `ResetCompositionModifier` | suspends the collection around the write | unchanged. It saves the instance, sets `null`, writes, then restores it |
| `ApplyStaggerDelays` | reads keys, mutates `DelayTime` | unchanged. It mutates animation objects, not the collection |

### §6.3 Removal (#1626) and ordering

`Reconciler.Update.cs` (~line 412) calls `ApplyTransitions` only when the *new* element declares
transitions. The gate becomes `oldEl.ImplicitTransitions is not null || newEl.ImplicitTransitions is not
null || newEl.ThemeTransitions is not null`, which keeps the theme-only updates that reach it today.
`ApplyTransitions(uie, old, new)` releases each channel that `old` had and `new` lacks. For Background,
that means `BackgroundTransition = null`.

**Ordering.** On update, setters and `ApplyModifiers` run *before* `ApplyTransitions`. If a reconcile
drops `.ScaleTransition()` and changes `.Scale()` together, the write would animate with the removed key,
and releasing the key afterwards can't undo an animation that already started. So on update the
transition policy runs **before** setters and modifier writes, both releases and new claims, the way
`ClearRemovedTransformModifiers` already releases departing transforms early. The same applies to the
reduced-motion gate. That also gives XAML parity for attach-and-write in one reconcile (F3b, F3c). On
mount the order stays as it is (modifiers, then transitions), so initial values don't animate in. §12
tests removal and addition together with a value change in the same reconcile.

## §7 Pooling elements whose visual was taken

### §7.1 What a taken visual leaves behind

| Fact | Measured |
|---|---|
| The handoff visual survives detach and re-add to another tree, same instance | P3 |
| The reset list below makes a reused control **pixel-identical** to a fresh one | P1: diff 0 with reset, 11,724 px without |
| After the reset, writes don't animate (implicit cleared) and transforms still work | P1b, P1c |
| `Offset` and `Size` belong to XAML layout. A direct `Offset` write is re-applied by the next arrange | P2a, P2b |
| `StopAnimation` leaves the last animated value, so each reset must write the identity after stopping | P4: 0.90 progress left |
| `SetIsTranslationEnabled(false)` works after the reset and keeps pixel identity | P1h, P1i |
| **Lasting difference:** facade `Rotation/Scale/CenterPoint/RotationAxis/TransformMatrix` writes (even identity) and every `UIElement.*Transition` write still throw | P1d, P1e, Q8a |
| `RenderTransform`, `Opacity` and facade `Translation` stay writable | P1f, P1g, Q8a |

### §7.2 Two pool lanes

After this spec, Reactor never uses the APIs that still throw, but *user code* can, through `.Set(...)`.
A plain `Border()` that rents a visual-taken control and then runs `.Set(b => b.Rotation = 10)` would
throw, while a fresh control wouldn't. So the pool keeps two lanes per type.

- **Plain lane.** Controls whose visual was never taken. This is today's pool, unchanged.
- **Visual lane.** Controls whose visual was taken and then reset with the list below.

`TryRent(type, lane)` picks the lane from the declarations of the element being mounted. That covers the
target plus any transparent decorators or keyed `Memo` resolved over it, and `RentControl` gets that
context as a new argument. The **visual lane** is eligible only when both of these hold.

- *Demand.* The element declares a non-identity transform, a transition or a compositor feature.
- *Compatibility.* No declaration in the chain carries setters (`.Set`, `.OnMount`-style callbacks) and
  the control isn't a custom or third-party handler. A setter could write a facade that throws on a
  rented visual-taken control but works on a fresh one. Example: `.Set(b => b.Rotation = 10).Scale(2)`
  succeeds on a fresh control because the handoff migrates the setter's rotation (§9 row 1).

An eligible element takes from the visual lane first and falls back to the plain lane. Every other
element, and any element whose context isn't known, **only** takes from the plain lane. So user code
never meets a control it couldn't have met fresh, and most transformed elements become poolable again.
They aren't pooled today, because `_compositorTainted` excludes them. Each lane keeps its own cap of 32.

### §7.3 Reset list (visual lane)

Applied in `CleanElement` when the control is tainted. Measured cost is in §11.

1. `StopAnimation` for `Opacity`, `Scale`, `RotationAngle`, `RotationAngleInDegrees`, `CenterPoint`,
   `Offset`, `Size`, `Orientation`, `AnchorPoint` and `TransformMatrix`. Stop `Translation` **only if the
   property exists** (`Properties.TryGetVector3("Translation")`). `StopAnimation` on a property-set entry
   that was never inserted throws `ArgumentException` (measured in the perf run). Step 1 also stops a
   `.TransformOrigin` expression and looping keyframes.
2. `ImplicitAnimationSlots.ReleaseAll` (`ImplicitAnimations = null`).
3. Identity writes. `Opacity = 1`, `Scale = One`, `RotationAngle = 0`, `RotationAxis = UnitZ`,
   `CenterPoint = Zero`, `AnchorPoint = Zero`, `Orientation = Identity`, `TransformMatrix = Identity`,
   `Clip = null`, and `Properties["Translation"] = Zero` when the property exists. Then `SetIsTranslationEnabled(false)`.
   `Offset` and `Size` are left to layout (P2).
4. Clear the `ReactorState` composition bookkeeping. That covers `CompositionModifierProperties`,
   `TransformRest`, `KeyframeChannelOwners`, `DeferredModifierResets`, the implicit-slot claims, the
   origin property set (§8), and the keyframe trigger table entries for the control.
5. The plain-lane steps stay as they are (#1620 facade resets, `RenderTransform`, `FlowDirection`, …). The facade
   writes are skipped for tainted controls, because they would throw (Q8a).

The reset doesn't cover these, so such controls still aren't pooled. A control whose exit `.Transition()` is
still running is already handled, because removal waits for the batch. Controls whose reset throws
are caught and dropped, as #1620 does.

### §7.4 #1624

Both lanes also clear `BackgroundTransition` (Grid, StackPanel, ContentPresenter). By inspection they
should clear `ChildrenTransitions`/`ChildTransitions` too, because `CleanElement` resets neither today
(§16). The XAML transform-transition check in `Return` (#1620) goes away once Reactor never sets those
properties. It only stays as a guard for user `.Set` writes, so it moves into the catch-and-drop path.

## §8 `.TransformOrigin(x, y)`

### §8.1 API

```csharp
public static T TransformOrigin<T>(this T el, float x, float y) where T : Element;  // (0.5f, 0.5f) = centre
```

- `x` and `y` are fractions of the element's laid-out size. (0,0) is the leading top corner.
- The pivot is `Size × origin + CenterPoint`. `.CenterPoint(v)` keeps its meaning (pixels), and
  combined with an origin it becomes an offset, as in CSS `calc(50% + 10px)`. One model, no precedence rules.
- Without `.TransformOrigin`, the pivot is the static `.CenterPoint` value (or 0,0) as today.

### §8.2 Implementation

On the handoff visual:

```text
visual.Properties["ReactorOrigin"]        : Vector2 (x, y)
visual.Properties["ReactorOriginOffset"]  : Vector3 (the .CenterPoint value, or Zero)
CenterPoint ← ExpressionAnimation("Vector3(t.Size.X * t.ReactorOrigin.X + t.ReactorOriginOffset.X,
                                           t.Size.Y * t.ReactorOrigin.Y + t.ReactorOriginOffset.Y,
                                           t.ReactorOriginOffset.Z)")  with t = visual
```

The expression starts once per element. Changing the origin or the offset only writes a property, so
nothing restarts. That generalises `TransitionEngine.BindCenterPointToVisualSize` (TransitionEngine.cs:818),
and navigation should switch to it.

**Ownership of `CenterPoint`.** While an origin is declared, the expression owns `Visual.CenterPoint`, and
nothing else may start an animation on that property or write it.

- A `.CenterPoint` change under a curve (`.Animate()`, `WithAnimation`) animates the
  `ReactorOriginOffset` property (`visual.Properties.StartAnimation`) instead of `CenterPoint`.
- An implicit `CenterPoint` key from `.Animate(AnimateProperty.CenterPoint)` never fires, because no
  write happens. The offset animation above replaces it.
- `ResetCompositionModifier("CenterPoint")` is origin-aware. It resets the offset, not the property.
- Navigation's `NormalizeVisual` stops `"CenterPoint"` unconditionally. After a transition it calls
  `EnsureOrigin(visual)`, which restarts the expression when the element still declares an origin
  (`ReactorState` records that).

Measured (the #773 report and this spike):

- It works when started before first layout, it tracks resizes (#773 E7a/E7c), and it centres scale and
  3D rotation (V2, V4).
- Hit-testing, `TransformToVisual` and UIA bounds all honour it. Q10 `VisualExprOrigin` shows a scale of 2
  about the centre, so (110,150) hits, (320,170) misses and `TransformToVisual(100,0)` = (300,130).
- RTL. The transform space is flow-relative (#773 E10a/V1, and Q9 here). `x = 0` is the *leading* edge,
  which matches SwiftUI's `.leading` and WinUI's own behaviour.
- **During a `.LayoutAnimation()` size animation**, the expression follows the *animating* `Visual.Size`
  while the content renders at its final layout size (Q12b: the pivot implies a 240 px width, the
  content 300 px). At rest it's exact (Q12c). This is documented, not fixed. The alternative, binding
  to a `SizeChanged`-fed property, costs a managed callback per resize and lags a frame.

Unset: `StopAnimation("CenterPoint")`, remove the two properties, then write the static `.CenterPoint`
value or Zero.

### §8.3 Seams

`VisualModifiers`/`ElementModifiers` field (`TransformOrigin: Vector2?`), equality, `Merge`, skip,
`ApplyModifiers`, unset arm, `AppliedModifierMap`, `ModifierTable` (analyzers), both `reactor.api.txt`
copies (`mur --regen-api`), `animation.md.dt` and `modifier-system.md.dt`, and the gallery samples
(hover, pulse, pinch/rotate) that currently pivot on the top-left corner.

## §9 Compatibility and breaking changes

| # | Change | Who notices | Mitigation |
|---|---|---|---|
| 1 | A user `.Set(fe => fe.Rotation / Scale / CenterPoint / RotationAxis / TransformMatrix = …)` on an element that **also** uses a Reactor transform modifier, a transition or a compositor feature. **Mount:** setters run before modifiers, and `GetElementVisual` migrates `Scale`, `Rotation`, `Translation` and `CenterPoint` (#1627), so those still work. It does **not** migrate `RotationAxis` or `TransformMatrix`, so a non-identity value there makes the handoff throw, even on a fresh control. **Update:** a setter that writes any of these facades after the handoff throws (Q8a). Today all of this works as long as no compositor feature is present | Apps mixing `.Set` facade writes with modifiers | Breaking. CHANGELOG `Changed`. Supported: the four migrated facades on mount. Rejected with a diagnostic: everything else. A `DiagnosticLog.Warning` names the modifier to use when the reconcile catches `UnauthorizedAccessException` from a setter **or from the handoff** on a Reactor-owned element. `REACTOR_ANIM_004` catches it at build time and suggests the modifier (§9.1) |
| 2 | Reading `fe.Scale`/`fe.Rotation`/`fe.CenterPoint` returns identity for values set by modifiers (Q8b) | Code reading facades back | Breaking (minor). Documented. Read `ElementCompositionPreview.GetElementVisual(fe).Scale` instead |
| 3 | `fe.Translation` from user code is ignored once `.Translation()` (or a Translation channel) enabled translation on that element and it has rendered (F1a). It still works on elements that don't use those modifiers, which covers the existing drag samples (`InputGesturesDemo`, `PointerInputPage`) | Apps mixing both | Documented as "don't mix". Reactor stops zeroing the facade (§4.3) |
| 4 | A user `.Set(fe => fe.OpacityTransition = …)`/`ScaleTransition`/… blocks the handoff (#1623's mechanism). On mount the setter runs first, so any Reactor transition, transform or compositor feature on the same element then throws at the handoff, even on a fresh control. After the handoff, the setter itself throws | Rare | Use the modifiers. `REACTOR_ANIM_004` (§9.1) catches it at build time. Same runtime diagnostic as row 1, which lands in layer 2, the first layer that makes this reachable without a compositor feature |
| 5 | The `TranslationTransition` fallback animated layout moves (`Offset`). Now `TranslationTransition` animates `.Translation()` on every path, as XAML does | Apps relying on the fallback quirk | `Fixed`. `.LayoutAnimation()` is the API for layout moves |
| 6 | The transition fallback curve goes from the compositor default (ease-out) to the XAML curve | Visual only | `Fixed` (parity) |
| 7 | Transforms no longer exclude a control from the pool. Visual-lane controls are reused | none visible (P1 pixel-identical) | — |
| 8 | Rendering, RTL, hit-testing, `TransformToVisual` and UIA bounds | none: identical (Q9, Q10, Q18) | — |
| 9 | Reduced motion keeps disabling `.XxxTransition()` | none | §5.5 |
| 10 | `PathElement.RenderTransform` | none, unchanged | — |
| 11 | Third-party handlers that write facades on Reactor-owned controls | none in the repo (grep: only `AnimationHelper` and `ElementPool` write facades) | Same diagnostic as row 1 |

### §9.1 Analyzers

The runtime diagnostic in row 1 only fires once the code runs. Two build-time diagnostics catch the
same mistakes at compile time and point to the modifier that works with the composition visual.

**`REACTOR_ANIM_004` (Warning, `Reactor.Animation`). A facade transform is written through `.Set` on an
element that uses the composition visual.**

- *Matches.* A `.Set(...)` lambda, expression-bodied or block, that does one of these:
  - assigns `Rotation`, `Scale`, `CenterPoint`, `RotationAxis`, `TransformMatrix`, `OpacityTransition`,
    `ScaleTransition`, `RotationTransition` or `TranslationTransition`;
  - calls `StartAnimation`/`StopAnimation`;
  - assigns `Translation`, but only when the chain also declares `.Translation()` (§4.3: the write would be silently ignored).

  The target must be the lambda's parameter, and the member must resolve through the semantic model to
  `Microsoft.UI.Xaml.UIElement`. A user type with its own `Rotation` property doesn't match, and neither
  does a write to some other control. Lambda inspection reuses `SetLambdaHelpers`, as
  `PoolResetSetAnalyzer` does.
- *And.* The same fluent chain declares a modifier that takes the visual. The analyzer walks the chain
  with `DuplicateAtomicModifierAnalyzer.GetReceiverInvocation` in both directions from the `.Set`. The
  trigger set is a `VisualOwningModifiers` table in `ModifierTable`, and it grows with the rollout:
  - **today:** `.Animate`, `.InteractionStates`, `.Transition`, `.Keyframes`, `.LayoutAnimation`;
  - **layer 2:** the four `.XxxTransition()`;
  - **layer 4:** `.Scale`, `.Rotation`, `.Translation`, `.CenterPoint`;
  - **layer 5:** `.TransformOrigin`.
- *Valid on `main` today.* With the current trigger set the diagnosed code already fails. A setter
  re-running on update throws after the handoff (Q8a). `RotationAxis`/`TransformMatrix` and the XAML
  transitions block the handoff at mount (#1623, §9 rows 1 and 4). So the rule can ship ahead of the
  stack (layer 0 in §13).
- *Message.* It names the modifier when one exists: `Rotation` → `.Rotation(…)`, `Scale` → `.Scale(…)`,
  `CenterPoint` → `.CenterPoint(…)`, `Translation` → `.Translation(x, y, z)`, `OpacityTransition` →
  `.OpacityTransition(duration)`, `ScaleTransition` and `TranslationTransition` → the same-named modifier,
  and `RotationTransition` → `.RotationTransition(duration)`. `RotationAxis`, `TransformMatrix` and
  `StartAnimation` have no modifier. Their message points to `.Keyframes`/`.Animate`, or to taking the
  visual with `ElementCompositionPreview.GetElementVisual` in `.OnMount`.
- *Code fix.* Mechanical rewrites only, under the same constraints as `PoolResetSetCodeFix`. The
  assignment must be the lambda's only statement, and the right-hand side must map to the modifier's
  parameters without evaluating anything twice:
  - `fe.Rotation = e` → `.Rotation(e)`, and the same for `Scale` and `CenterPoint`;
  - `fe.Translation = new Vector3(a, b, c)` → `.Translation(a, b, c)`;
  - `fe.OpacityTransition = new ScalarTransition { Duration = d }` → `.OpacityTransition(d)`, and the same for `RotationTransition`;
  - `fe.ScaleTransition = t` → `.ScaleTransition(t)`, and the same for `TranslationTransition`.

  Any other shape gets the diagnostic without a fix.
- *Accepted false negatives.* The rule misses three shapes, because none of them is visible from the
  `.Set` chain:
  - a visual-owning modifier on a wrapper (keyed `Memo`, a decorator) or in a chain split across variables;
  - facade writes through `ElementRef.Current` or in event handlers (the drag samples);
  - `.OnMount` callbacks. These could be added later if the matching proves as clean.

**`REACTOR_MOD_002` widening (Info, layer 4).** After layer 4, `.Scale/.Rotation/.CenterPoint` are
ungated writes to every element, which is the "application path verified" condition `ModifierTable`
waits for. Their rows move from `DeliberatelyExcluded` to `Properties` (non-pool-reset). Any
`.Set(fe => fe.Rotation = …)`, even on an element without compositor features, then suggests the
modifier, because only the modifier composes with transitions, animations and `.TransformOrigin`.
`Translation` stays excluded because of the signature mismatch, and `ANIM_004` covers its harmful case.
The four `*Transition` rows stay excluded too. The `.Set` write and the modifier are no longer
"different operations" after layer 2. But a plain element with only a `.Set` transition still works on
the XAML path, so `ANIM_004` (when combined with visual-owning modifiers) is the right severity, not a
blanket suggestion.

Both rules belong in `src/Reactor.Analyzers`. Neither has a `mur check` mirror, the same as the rest of
the `.Set` family (`POOL_001`, `MOD_002`). Before implementation, the `analyzer-dym` workflow runs an FP
spike. Positives are the shapes above, and the deliberate fixture writes in
`PooledFacadeTransformFixtures`, `TransformModifierFixtures` and `ItemsViewParkingFixtures` get
`#pragma warning disable` with a reason. Negatives:

- `fe.Opacity`;
- `RenderTransform` (`PathElement`);
- a user type with `Rotation`/`Scale` properties;
- a write to a captured control other than the lambda parameter;
- a `.Set` on a chain with no visual-owning modifier (today);
- a `Translation` write without `.Translation()`.

## §10 AOT and trimming

No reflection, no new generic instantiation over user types, and no `dynamic`. The expressions are
string literals, `CompositionPropertySet` writes are typed, and `ImplicitAnimationSlots` is a plain
internal class. Nothing new needs annotations. The diagnostic in §9 row 1 uses `DiagnosticLog.Warning`
behind `IsWarningEnabled`. `ReactorSourcePublisher` isn't touched, so the AOT trim proof is unaffected.

## §11 Measurements

Setup. The scratch fixtures `ZzT_*` (not committed) ran in `Reactor.AppTests.Host`, Release x64, on
`main` @ `0c5edca6b`. The target is a 100×40 red `Border` at (150,150) in a 400×400 white canvas. Two oracles
were used. `RenderTargetBitmap` sees compositor animations (Q2c/Q2d progress) but **not** XAML facade
transitions in flight. `PrintWindow(PW_RENDERFULLCONTENT)` sees both once animations are enabled. The
animation verdict samples about 0.25 s into a 1.5 s animation and again after it must have ended.
`JUMPED` means early progress ≥ 0.95, `ANIMATING` means early progress between 0.03 and 0.95 with the
final at 1, and `UNCLEAR` means early progress < 0.03 with the final at 1 (the slow start of the XAML ease
at 0.25 s). The positive controls are PC0 (no transition, `JUMPED`) and PC1/F0b (XAML transition, which
animates only when system animations are on).

The machine had "Animation effects" off (an RDP session). The XAML rows therefore ran twice, once as
found (everything jumped) and once with `SPI_SETCLIENTAREAANIMATION` turned on for that session only
and restored afterwards.

### §11.1 Answers to #1630's open questions

| Question | Answer | Evidence |
|---|---|---|
| Does `TranslationTransition` map to the right property? | No. The fallback animates `Offset` (layout moves), and XAML animates `Translation`. An implicit `"Translation"` key never fires, so use write-time animation | Q1a, Q1b, Q1d, Q1e, Q1g, Q1c, F1d, F1e, F1f |
| Is the fallback linear? | No. It uses the compositor default ease-out. XAML uses (0.8,0,0.2,1) | Q2a–Q2e, F4a |
| Does a `RotationAngle` implicit animation fire for `RotationAngleInDegrees` writes? | No, and the reverse is also no. Use `RotationAngle` everywhere | Q3b–Q3e |
| Do `UIElement.Opacity` writes reach the visual's implicit `Opacity`? | Yes. The two opacities don't multiply, and XAML pushes `UIElement.Opacity` into the visual when it commits a change | Q4b–Q4e |
| Can tainted elements be pooled? | Yes, with the §7.3 reset list. Pixel-identical. Plain renters must not get them (facade and XAML-transition APIs still throw) | P1–P4 |
| Cost of one `GetElementVisual` per transformed element | See §11.3 | Perf |
| Ordering with #1627 | #1627's helper stays as the single handoff and keeps migrating user facade writes. Its Reactor-side role ends | §3 invariant 3 |

### §11.2 New findings

| Finding | Evidence |
|---|---|
| XAML transitions honour "Animation effects", and composition implicit animations don't | transitions-screen vs -anim-on, §5.5 |
| `Components` is honoured by XAML and can be emulated on the compositor | Q2f, Q2g, F4b, F4c |
| Facade `Translation` + visual `Translation` add up before first render. After `SetIsTranslationEnabled` + render, facade writes are ignored | Q8c, F1a, F1b, F1c |
| `BackgroundTransition` coexists with the handoff in both orders and still animates | Q5a, Q5c, Q5d, F2 |
| `ImplicitAnimationCollection` is live. In-place add and remove take effect | Q6a–Q6c |
| Theme transitions (Entrance, Reposition) coexist with a visual-scaled child | Q16a–Q16d |
| Taking the visual doesn't change pixels (text, rounded border, identity writes) | Q18: diff 0, positive control 453 |
| Origin expression + `LayoutAnimation` size: the pivot follows the animated size | Q12b |
| Reactor's `Curve.Linear` is not linear (§16) | Q2h, Q2i vs Q2d |
| RTL: facade and visual give identical rotation, translation and scale | Q9 (6/6 equal) |
| UIA bounds, hit-testing and `TransformToVisual` are identical for facade and visual | Q10 |

### §11.3 Performance

Two microbenchmarks ran in the selftest host, Release x64, on the same machine. Other sessions were
idle, but it was a shared RDP desktop, so the spread is wide. Minima are given next to medians because they
are the least disturbed. The `/perf` stress workload has no transforms, so it can't measure this
change.

**Raw WinUI.** 2000 `Border`s (6×6) were added to a `Canvas` with properties set, then
`UpdateLayout()`. That's UI-thread synchronous time. There were 7 reps, the variant order was shuffled
per rep, and GC plus 1 s of quiet ran between variants (`perf4.txt`).

| Variant | Mount, ms / 2000 (median, min) | Update, ms / 2000 | Private bytes, KB / element |
|---|---|---|---|
| none | 95, **57** | 3.9 | 1.7 |
| facade `Scale` | 72, **61** | 2.3 | 1.5 |
| facade `Scale`+`Rotation`+`CenterPoint` | 95, **78** | 3.5 | 4.2 |
| `GetElementVisual` only | 379, **251** | 4.3 | 5.0 |
| visual `Scale` | 276, **251** | 0.9 | 5.1 |
| visual `RotationAngle` | 362, **247** | 1.1 | 4.2 |
| visual `Scale`+`RotationAngle`+`CenterPoint` | 403, **272** | 2.4 | 4.8 |
| the same + `.TransformOrigin` expression | 394, **324** | **47** | 8.2 |

| Micro-cost (µs / element) | Median | Min |
|---|---|---|
| First `GetElementVisual` on a laid-out element | 426 | 259 |
| Repeat `GetElementVisual` (already taken) | 1.35 | 1.0 |
| §7.3 reset list | 18.6 | 12.8 |

**Reactor.** 1000 `Border().Width(6).Height(6).Scale(1.2f).Rotation(10)` in a `VStack`, mount →
update (`Scale(1.3f).Rotation(12)`) → three remove-all/remount cycles. The facade path is `main`, and the visual
path is `main` with a scratch switch that forces `ShouldWriteToVisual` (reverted). 7 reps (`rperf.txt`).

| Mode | Mount, ms (median, min) | Update, ms | Remove + remount cycle, ms |
|---|---|---|---|
| plain (no transform) | 32, 25 | 48 | 91 |
| facade (today) | 55, 31 | 50 | 100 |
| visual (forced) | 185, 150 | 65 | **582** |

Reading:

1. **The handoff costs about 100–130 µs per element the first time** (raw: (251−57)/2000 ≈ 97 µs.
   Reactor: (185−55)/1000 ≈ 130 µs), plus about 3 KB of private memory. Writes afterwards are cheaper than facade
   writes (0.9 vs 2.3 ms per 2000). That's acceptable for the tens of transformed elements a typical
   UI has (100 elements ≈ 10–13 ms on first mount), but not free. Two rules follow, and both are
   already in the design. The handoff must stay lazy (§3 invariant 2), so identity transforms and
   untransformed elements never pay. And it must happen once per control lifetime, which the pool lanes
   provide.
2. **Pooling is the dominant term.** Today a visual-taken control can't be pooled, so every remove and
   remount pays construction plus a new handoff (582 vs 100 ms per 1000). Layer 3 (pool lanes) must
   land **before** layer 4, as §13 orders it. A recycled visual-lane control keeps its visual (P3) and pays
   only the reset list (about 15 µs). Otherwise layer 4 would be a churn regression for every transformed
   list item that is pooled today (#1620).
3. **`.TransformOrigin` costs about 25 µs more per element at mount**, and while its expression runs,
   writing other properties on the same visual costs about 20 µs per element (47 ms vs 2.4 ms per 2000).
   That's acceptable for an opt-in modifier, but it rules out putting an origin expression on every
   transformed element by default (§15 Q2).
4. The `ApplyStaggerDelays` behaviour in §16 item 3 is worth fixing on cost grounds alone. It pays the
   first-handoff cost for every child of a staggered panel.

Re-run the raw benchmark on the layer-4 PR, and `/perf` to confirm the untransformed path is unchanged.

## §12 Test plan

Every new oracle gets a positive control that fails when the mechanism is removed. Each layer
mutation-checks its gating assertion (delete or no-op the code, confirm the check fails, revert).

### §12.1 Validation strategy

The fixtures in §12.2 test each fix. This section answers three broader questions for the whole
stack.

1. **Do the animations actually run?** Every case observes motion in flight, not just the end state.
2. **Do features compound correctly?** A combinatorial matrix checks each case against an analytic
   composition model.
3. **Are we only breaking things on purpose?** A behaviour ledger captured on `main` before any
   change is diffed on every layer. Each difference must be declared and tied to a §9 row or an issue.

#### A. What each case records

| Field | Oracle |
|---|---|
| `threw` | Exception type and lifecycle step, or none |
| `pose` | RTB bounding box of the red target at rest, ±2 px, compared with the model in B |
| `motion` | Per animated channel: `JUMPED` / `ANIMATING` / `NOT-APPLIED`, plus progress at 0/50/100%. Deterministic where possible. The harness pauses the channel's `AnimationController` (`Visual.TryGetAnimationController`) and sets `Progress`, as `TransformVisualHandoffFixture` already does. Layer V checks whether implicit animations expose a controller. If not, it uses wall-clock sampling with ≥ 1.5 s durations and §11's three-way verdict |
| `curve` | For transition cases: progress at 25/50/75% within ±0.05 of (0.8,0,0.2,1) |
| `hit`, `uia`, `ttv` | Hit-test at three probe points, the UIA bounding rectangle, and `TransformToVisual` of one corner |
| `channel` | Which channel holds each value (facade getters vs visual properties). Layer 4 flips this on purpose |
| `pool` | Recycled or not, which lane, pixel diff of the rented control against a fresh one, and residual implicit keys or running animations (`TryGetAnimationController` for every channel) |
| `diag` | `DiagnosticLog` warnings emitted |

#### B. The matrix and the composition model

| Dimension | Values |
|---|---|
| Transforms | none, `Scale`, `Rotation`, `Translation`, `CenterPoint`, `Scale+Rotation+CenterPoint`, and `TransformOrigin` from layer 5 |
| Implicit transition | none, Opacity, Scale, Rotation, Translation (also `Components = X`), Background |
| Compositor feature | none, `.Animate(curve)`, `WithAnimation`, `InteractionStates` hover, `.Transition` enter and exit, `.Keyframes` (scale, opacity, translation, rotation; one-shot and looping), `.LayoutAnimation` (offset + size), `.Stagger` parent |
| Wrapper | direct, keyed `Memo`, `ModifiedElement` decorator |
| Environment | LTR / RTL, animations enabled / disabled (`TransitionEngine.OverrideAnimationsEnabled`) |
| User code | none, a `.Set` facade write (the §9 cases), a `fe.Translation` drag write |

Each case runs one scripted lifecycle. Mount, change the declared values, add a feature, remove a
feature, remove a modifier, then unmount, rent from the pool as a plain element and as a transformed
one. One case covers every step. The full product is far too large, so the generator combines two
sets. One is **all triples of Transforms × Transition × Feature**, the axis behind every bug so far
(#1613, #1623, #1625): 7 × 6 × 9 = 378 cases (Keyframes counts once, with one-shot and looping as sub-variants). The other is **pairwise coverage** of the remaining dimensions on top. The
generator is seeded and case ids are stable, so ledger diffs stay readable. Budget: about 20 minutes. It
runs in full on every layer PR (label-triggered job) and nightly. Every PR's normal CI runs a fixed
10% seeded sample plus the §12.2 fixtures.

The model writes down what "compounds properly" means, so the oracle has something to check
against:

- **Pose.** `Translation · T(pivot) · R(rotation, axis) · S(scale) · T(−pivot)`, where
  `pivot = Size × origin + CenterPoint`. The transform space is flow-relative (Q9).
- **Per-channel precedence**, highest first. A playing `.Keyframes` entry on that channel, then the active
  `InteractionStates` state, then a running `.Transition` enter or exit (scale relative to the
  declared value, #1627), then the declared modifier. When a temporary owner ends, the channel returns to
  the declared value (`TransformRest`). How the declared value moves is decided by `WithAnimation`,
  then `.Animate()`, then the implicit transition, else it jumps (§5.3).
- **Opacity** is a single channel (§3 invariant 4).
- **Implicit keys.** `LayoutAnimation` beats `.Animate` beats transitions, per key. Releasing the winner
  restores the next (§6.1).
- **Reduced motion.** Transitions jump. Other compositor features are unchanged (§5.5).

If `main` already disagrees with the model in some case, that's recorded in the ledger as a known
pre-existing difference with an issue link. The model isn't bent to fit it.

#### C. The behaviour ledger

1. **Layer V** lands first. It adds the harness and
   `tests/Reactor.AppTests.Host/SelfTest/Baselines/transforms-ledger.json`, which holds one record per
   case, captured on `main` with no product change.
2. Every later layer reruns the matrix and diffs it against the ledger. Every differing field must match
   an entry in `transforms-ledger.expected.json`
   (`{ case selector, field, before, after, reason: "§9 row N" | "fixes #NNNN" }`). An unmatched
   difference fails. So does an expectation that matches nothing, because it's stale. The PR then
   regenerates the ledger, and the ledger's own diff shows reviewers exactly which behaviours changed.
3. **Control group.** Cases with no transform, transition or compositor feature must stay
   byte-identical across every layer.
4. Expected changes per layer:
   - **L1:** the #1625 and #1626 cases go from not animating, or still animating after removal, to correct.
   - **L2:** the #1623 cases go from `threw` to passing, the fallback curve changes, and Translation-transition semantics change.
   - **L3:** `pool.recycled` flips for compositor-feature cases.
   - **L4:** `channel` flips for every transform case, and the §9 row 1 setter cases start to throw.
   - **L5:** only additions.
5. **Noise.** Poses compare within ±2 px and motion compares by verdict, not raw progress. A
   mismatch reruns three times before it fails. Quarantining a case needs an explicit ledger entry.

#### D. Validating the instruments

- Before recording, a run executes the positive and negative controls for every oracle (PC0/PC1 style
  from §11). If one fails, the run aborts instead of writing a ledger. One example is a pose oracle that
  can't see a known transform.
- **XAML transitions on `main` are invisible to RTB in flight** (§11). The baseline therefore records
  their motion as `unobservable` and compares only their end state. Layer V attaches a one-time
  PrintWindow capture, with animations enabled, of the transition cases on `main`, as evidence for the
  §5.1 parity claims that layer 2 relies on.
- **Mutation checks.** Each layer breaks its key lines, reruns the matrix, and records in the PR which
  cases turned red. Examples: skip a `Claim`, drop the easing, skip one reset step, write the facade
  instead of the visual. A mutation that no case catches gets a new case before the layer merges.

#### E. Beyond the matrix

- **Pool soak.** A seeded random churn of 500 elements with random features over 2,000
  mount/update/unmount steps. After every step it asserts: no exception, every rented control passes
  the §7.3 reset invariants (identity visual properties, no implicit keys, no running animation on any
  channel), and the lane rules of §7.2 hold.
- **Real-world pixels.** Before and after each layer, the docs pipeline captures screenshots of the
  gallery pages that use transforms and animations (Animation, Interaction states, Pointer input,
  Transitions) at rest, and diffs them pixel for pixel. Any difference needs a ledger reason.
- **Real input.** The E2E test in §12.2.
- **Perf.** §12.2.
- **Review.** Each layer gets a `pr-review` pass, plus a cross-model check where one is available.

**Exit criteria for every layer.** The matrix passes with only declared differences. The control
group is unchanged. Mutation results are recorded. The soak is clean (layers 3–5). Perf is re-measured
(layers 3–4). CHANGELOG lines cite the §9 rows they introduce.

### §12.2 Fixtures per layer

**Unit (`tests/Reactor.Tests`, headless).**
- `ImplicitAnimationSlots` claim/release precedence as a pure-managed state machine. The collection
  writes go behind an interface so the logic can be tested without WinUI. Cases: lower owner hidden
  then restored; release of a non-owner is a no-op; last release yields an empty set.
- `TransformOrigin` modifier equality, `Merge`, skip and unset (pure records).
- The `ResolveCurve` source order with `TranslationTransition` (pure).
- `ModifierTable`/analyzer parity for the new modifier.
- `REACTOR_ANIM_004` analyzer and code-fix tests (`tests/Reactor.Tests/AnalyzerTests/`): each §9.1
  positive and negative, the code fix for every mechanical shape, no fix for multi-statement lambdas
  or non-`new Vector3(...)` right-hand sides, and a parity test that `VisualOwningModifiers` names
  exactly the modifiers whose Reactor implementation calls `AnimationHelper.GetElementVisual`. That
  test reddens if a layer moves a modifier to the visual without updating the table.
- `REACTOR_MOD_002` (layer 4): `ModifierTableIntegrityTests` passes with `Scale/Rotation/CenterPoint`
  moved out of `DeliberatelyExcluded`.

**Selftests (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`).** The RTB oracle is enough because every
animation is now compositor-side.
- Extend `TransformVisualHandoffFixture` with a channel assertion (the facade getters stay identity, and
  the visual holds the value) plus #1623's matrix: each `.XxxTransition()` × LayoutAnimation, Animate,
  Transition, InteractionStates and Keyframes, in Mount, Update and FeatureFirst orders.
- Transition parity: curve samples at 25/50/75% within ±0.05 of (0.8,0,0.2,1), `Components = X`,
  `TranslationTransition` animates `.Translation()` and not a layout move, and the reduced-motion
  gate via `TransitionEngine.OverrideAnimationsEnabled(false)` gives `JUMPED`.
- #1625: `.OpacityTransition().LayoutAnimation()` animates opacity, and dropping `.LayoutAnimation()`
  keeps it animating.
- #1626: dropping each transition stops the animation, for Background too. Dropping or adding a transition
  and changing the value in the **same** reconcile honours the new policy (§6.3).
- Claim identity: a transition declared on both a keyed `Memo` and its target. Removing the outer one
  restores the inner one (§6.1).
- Theme-only `.WithTransitions()` updates still apply (§6.3 gate).
- `TranslationTransition` with `Components = X` animates X and jumps Y end to end, and `WithAnimation`
  still takes precedence over it (§5.3).
- Opacity: both write orders, both getters, after a second XAML commit (§3 invariant 4).
- `.TransformOrigin` survives `.Animate(CenterPoint)`, a `.CenterPoint` change under `WithAnimation`,
  removal of `.CenterPoint`, and a navigation transition (§8.2).
- Identity precedence: `.Set(b => b.Scale = new(2)).Scale(1)` renders at scale 1 (§4).
- Pool lanes: a transformed control returns, a transformed renter gets the same instance, and the
  result is pixel-identical to fresh with writes that don't animate. A plain renter never gets it, and
  its `.Set(b => b.Rotation = 10)` doesn't throw. A transformed renter **with** a setter never gets it
  either (§7.2 compatibility). #1624: `BackgroundTransition` and
  `ChildrenTransitions` are cleared. Reuse `PooledFacadeTransformFixtures` for the plain lane.
- `.TransformOrigin`: centre scale and rotation, resize tracking, RTL leading edge, hit-test, unset,
  and a combination with `.CenterPoint` offset.
- §9 row 1: the warning is emitted, and the reconcile error shape is unchanged.

**Packaged tier.** No identity-dependent behaviour, so no packaged fixtures.

**E2E.** One `winapp ui` test that hovers a `.TransformOrigin(0.5f,0.5f).InteractionStates(scale)`
element with real input and reads the UIA bounding rectangle. It's the only path that covers real
pointer input.

**Perf.** Run `/perf` on layer 4. The stress workload has no transforms, so it guards the untransformed
path only. The §11.3 microbenchmark covers the transformed path and should be re-run on the layer-4 PR.

## §13 Rollout

Stacked PRs. Each one passes CI alone and carries its own CHANGELOG lines (`Fixed` or `Changed`, citing
this spec and the issue). Sizes are product lines excluding tests and docs.

| # | Layer | Fixes | Size | Depends on |
|---|---|---|---|---|
| V | Validation harness (§12.1): matrix generator, observation recorder, composition model, positive controls, pool soak, and the ledger captured on `main`. Test-only, no product change | — | M (~900, tests only) | — |
| 0 | `REACTOR_ANIM_004` with today's trigger set and its code fix (§9.1). Independent, valid on `main` now | #1613/#1623-shaped user code | S (~250 incl. fix) | — |
| 1 | `ImplicitAnimationSlots`. Route LayoutAnimation, Animate and the transition fallback through it, and widen the Update gate for removals | #1625, #1626 (the fallback and XAML removal arms) | S (~200) | — |
| 2 | Compositor-only transitions. Fluent curve, `Components`, write-time `TranslationTransition` (with mask), reduced-motion gate, transition policy before setters on update, `BackgroundTransition` unset arm and pool clear, the §9 handoff diagnostic, and `.XxxTransition()` added to `VisualOwningModifiers` | #1623, #1624 (Opacity, Background) | M (~350) | 1 |
| 3 | Pool lanes and the visual reset list | #1624 (rest), restores pooling for compositor-feature elements | M (~250) | 2 |
| 4 | Transforms always on the visual. Remove the facade branches, lazy handoff with the effective-identity rule, stop zeroing `UIElement.Translation`, extend the diagnostic to setter facade writes, transform modifiers added to `VisualOwningModifiers`, and the `REACTOR_MOD_002` widening | #1630 | M (~200 net, mostly deletions) | 3 (so transformed elements never lose pooling) |
| 5 | `.TransformOrigin(x, y)`, the origin property set and `CenterPoint` ownership, navigation reuses it, api.txt ×2, docs templates, gallery samples | #773 | M (~300) | 4 |

The `Curve.Linear` fix (§16) doesn't depend on any layer (but see the ledger note below). Neither does layer 0, whose
`VisualOwningModifiers` table then grows in layers 2, 4 and 5. Layers 1–5 all depend on layer V. Its
ledger must be captured on `main` before the first behaviour change, otherwise the "only on purpose"
check has no baseline. The `Curve.Linear` fix changes curves that the ledger records, so it must land
either before layer V or with its own declared ledger entries.

## §14 Rejected alternatives

| Alternative | Why not |
|---|---|
| Keep facades for static transforms and migrate at each handoff (#1627, the status quo) | Two channels. Every new compositor feature needs the helper, and it doesn't help #1623 or #773 |
| Move to `RenderTransform`/`CompositeTransform` (the Toolkit's XAML layer) | It also blocks the facades, has no size-relative centre for compositor animations, and Storyboards run outside Reactor's compositor animations |
| Choose the channel per element (facade until a compositor feature appears) | That's today's model. It breaks whenever a feature is added on update, and the XAML transitions still block the handoff |
| Hybrid transitions (XAML when possible) | Same failure. A later compositor feature throws (#1623) |
| Implicit `"Translation"` key for `TranslationTransition` | Never fires for `InsertVector3` writes (Q1c, F1d, F1e) |
| Facade `CenterPoint` expression on `ActualSize` for #773 | Evaluated once, stale after resize, hit-testing stale (#773 E5) |
| `SizeChanged` → static `CenterPoint` for #773 | Managed callback per resize, a frame late, and still facade-bound |
| Keep an independent opacity on the visual next to `UIElement.Opacity` | XAML overwrites it with `UIElement.Opacity` on the next commit (Q4d). The values don't multiply |
| Pool tainted controls in a single lane | Plain renters would inherit APIs that throw (P1d, P1e) |

## §15 Open questions

1. **Naming and offset.** `.TransformOrigin(x, y)` plus `.CenterPoint` as the pixel offset, or a
   single `.TransformOrigin(Vector2 relative, Vector3 offset = default)`?
2. **Default pivot.** CSS, SwiftUI, Compose and Flutter default to the centre, Reactor to (0,0).
   Changing it breaks every existing `.Scale`/`.Rotation` layout, so it's out of scope here, but it's
   worth a separate discussion once `.TransformOrigin` ships.
3. **Live reduced-motion changes.** Re-apply transitions on `AnimationsEnabledChanged` (the host already
   listens, `ReactorHost.cs:1591`), or wait for the next render?
4. **Reduced motion for the other compositor features.** `.Animate()`, `.LayoutAnimation()`, `.Transition()`
   and `.Keyframes()` ignore the setting today. That's a separate decision.
5. **`AnimateProperty.Offset`** is documented as "Translation" but animates layout `Offset`. Rename,
   or add a real `Translation` flag?
6. **Analyzer scope.** §9.1 limits `REACTOR_ANIM_004` to `.Set` lambdas. Should it also cover `.OnMount`
   callbacks and `ElementRef.Current` writes, which are harder to tie to a chain?
7. **Pool lane caps.** Same 32 per lane, or a shared 32 per type?

## §16 Other defects found by the spike

These aren't fixed by this spec. They're recorded so they can be filed.

1. **`Curve.Linear` is not linear.** `AnimationHelper.CreateScalarImplicitAnimation`,
   `CreateVector3ImplicitAnimation`, `CreateScalar/Vector3TargetAnimation` and the `LinearCurve` arms
   of `SetOrAnimate`/`SetOrAnimateVector3` insert their keyframes without an easing function, so the
   compositor default ease-out applies. With `Curve.Linear(3000)`, the scale reached 65% of its travel
   at 29% of the duration (Q2h, Q2i). With an explicit `LinearEasingFunction` it reached 27% (Q2d).
   The fix is to pass `compositor.CreateLinearEasingFunction()`.
2. **`CleanElement` never resets `ChildrenTransitions`/`ChildTransitions`/`ContentTransitions`**
   (by inspection of `ElementPool.cs`). A pooled panel keeps the previous renter's theme transitions.
   That's the #1624 family, and layer 3 handles it.
3. **`ApplyStaggerDelays` takes the handoff visual of every child** of the staggered panel, even children
   with no implicit animations (`Reconciler.cs` ~6316). Today that taints them and drops them out of the
   pool. After layer 3 it only moves them to the visual lane. The cheaper fix is to check the
   children's slot claims before taking the visual.
