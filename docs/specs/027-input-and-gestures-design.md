# Reactor Input & Gestures — Detailed Design

## Status

**Draft** — 2026-04-21.

---

## Problem Statement

The [critical review](../critical-review.md) §12 grades Input/Events at **C** —
the lowest non-N/A score in the scorecard:

| Area | Reactor | React | SwiftUI | Compose |
|---|---|---|---|---|
| **Input/Events** | **C** | B | A | A |

The review identifies five gaps:

1. **No gesture system.** SwiftUI has `DragGesture`, `TapGesture`,
   `LongPressGesture`, and composition operators. Compose has
   `Modifier.pointerInput { detectDragGestures {} }`. Microsoft.UI.Reactor (Reactor) has individual
   pointer events with no higher-level abstraction — users fall back to manual
   hit testing and pointer-state bookkeeping for anything richer than a single
   tap.
2. **Event handler re-attachment is wasteful.** Declarative event handlers
   re-attach on every render cycle when the delegate reference changes. In the
   React style of always-new closures, that's essentially every render — O(n)
   COM interop calls per render per element with event handlers. React
   addresses this with event delegation; SwiftUI and Compose keep dispatch
   framework-internal.
3. **Commanding integration is incomplete.** Spec 012 shipped a Reactor-native
   Command system, but it only integrates with `Button`, `AppBarButton`, and
   `MenuItem`. Other command-capable controls (`SplitButton`,
   `ToggleSplitButton`, `HyperlinkButton`, `ToggleButton`, `SwipeItem`,
   `ContentDialog` actions) still use bare `Action` callbacks.
4. **No PointerEntered/Exited modifiers.** The declarative surface covers
   pressed/moved/released/tapped but not entered/exited. Hover effects — the
   most common pointer interaction — require `.Set()` passthrough.
5. **No RightTapped, DoubleTapped, Holding, or KeyUp modifiers.** Context
   menus need right-tap. Desktop apps need double-click. Touch apps need
   long-press. All three are passthrough-only today.

Additionally, focus events (`GotFocus`, `LostFocus`) are passthrough-only,
and there are no declarative modifiers for `IsTabStop`, `TabIndex`, or
`AccessKey` beyond what the commanding system provides.

**Drag-and-drop with data transfer is entirely missing.** WinUI ships the
full OS-level drag-and-drop protocol (`CanDrag` source, `AllowDrop` target,
`DragStarting`, `DragEnter/Over/Leave/Drop`, `DropCompleted`, `DataPackage`
with text/URI/HTML/files/bitmap/custom formats, `DragUIOverride` visual
feedback). The review's "no gesture system" finding undersells this: DnD
isn't a gesture, it's a core Windows data-transfer contract, and every
capability in it requires raw `.Set()` today. Reordering cards between
kanban columns, importing files from Explorer, dragging tabs between
windows, and dragging items between apps are all core interactions a
Windows UI framework has to support declaratively.

The existing surface in `ElementExtensions.cs:178-194`:

```csharp
public static T OnSizeChanged<T>(this T el, Action<object, SizeChangedEventArgs> handler)
    where T : Element => Modify(el, new ElementModifiers { OnSizeChanged = handler });
public static T OnPointerPressed<T>(this T el, Action<object, PointerRoutedEventArgs> handler)
    where T : Element => Modify(el, new ElementModifiers { OnPointerPressed = handler });
public static T OnPointerMoved<T>(this T el, Action<object, PointerRoutedEventArgs> handler)
    where T : Element => Modify(el, new ElementModifiers { OnPointerMoved = handler });
public static T OnPointerReleased<T>(this T el, Action<object, PointerRoutedEventArgs> handler)
    where T : Element => Modify(el, new ElementModifiers { OnPointerReleased = handler });
public static T OnTapped<T>(this T el, Action<object, TappedRoutedEventArgs> handler)
    where T : Element => Modify(el, new ElementModifiers { OnTapped = handler });
public static T OnKeyDown<T>(this T el, Action<object, KeyRoutedEventArgs> handler)
    where T : Element => Modify(el, new ElementModifiers { OnKeyDown = handler });
```

Six modifiers cover a surface that WinUI exposes with 30+ routed events and
5+ manipulation events plus the drag-and-drop protocol. The gap is real.

---

## Research: Gesture Systems in Modern Frameworks

### SwiftUI

SwiftUI composes gestures via `.gesture()` with a recognizer hierarchy:

```swift
MyView()
  .gesture(
    DragGesture(minimumDistance: 10, coordinateSpace: .local)
      .onChanged { value in offset = value.translation }
      .onEnded { value in offset = .zero })
  .simultaneousGesture(
    LongPressGesture(minimumDuration: 1.0)
      .onEnded { _ in showMenu = true })
```

**Key design choices:**
- **Gestures are values**: `DragGesture` is a struct. Composition operators
  (`.simultaneously(with:)`, `.sequenced(before:)`, `.exclusively(before:)`)
  combine them into new gestures.
- **Phases collapse into callbacks**: `.onChanged` fires for Began + Changed;
  `.onEnded` fires once for Ended. Cancellation is implicit (no callback).
- **Coordinate space is explicit**: `.local` vs `.global` vs named
  coordinate space.
- **State machine is hidden**: Users never see "phase" explicitly; they see
  discrete callbacks.

### Jetpack Compose

Compose uses `Modifier.pointerInput` with recognizer functions:

```kotlin
Modifier.pointerInput(Unit) {
  detectDragGestures(
    onDragStart = { offset -> ... },
    onDrag = { change, dragAmount ->
        offset += dragAmount
        change.consume()
    },
    onDragEnd = { ... },
    onDragCancel = { ... })
}

// Or the higher-level combinedClickable:
Modifier.combinedClickable(
  onClick = { ... },
  onLongClick = { ... },
  onDoubleClick = { ... })
```

**Key design choices:**
- **Suspend functions drive state machines**: `detectDragGestures` is a
  suspend function that loops — coroutines naturally express the state
  machine without explicit phases.
- **Change consumption is explicit**: `change.consume()` tells the pointer
  pipeline to stop dispatching that event.
- **One modifier owns pointer input**: multiple `pointerInput` blocks compose,
  but each owns its own coroutine scope.

### React (DOM / React Native)

React DOM has no built-in gestures — `onMouseDown/Move/Up` + a library
(`@use-gesture/react`, `react-spring`). React Native uses Pan/Pinch/Rotation
gesture responders with a `PanResponder` state machine.

React Native Gesture Handler (the de facto standard):

```jsx
const drag = Gesture.Pan()
  .minDistance(10)
  .onUpdate(e => translateX.value = e.translationX)
  .onEnd(e => translateX.value = withSpring(0));

<GestureDetector gesture={drag}>
  <Animated.View />
</GestureDetector>
```

Same "gesture as value" pattern as SwiftUI, with a separate
`GestureDetector` component to bind it. Composition via `Gesture.Race`,
`Gesture.Simultaneous`, `Gesture.Exclusive`.

### WinUI3 native primitives

WinUI has everything needed under the hood — it's just not exposed
declaratively in Reactor:

| WinUI primitive | What it provides |
|---|---|
| Pointer routed events | Pressed, Moved, Released, Entered, Exited, Canceled, CaptureLost, WheelChanged |
| Gesture-ish routed events | Tapped, DoubleTapped, RightTapped, Holding |
| Manipulation events | Started, Delta (translation/scale/rotation), Completed, with optional inertia |
| `ManipulationMode` flags | TranslateX/Y, Rotate, Scale, TranslateInertia, ScaleInertia, RotateInertia, TranslateRailsX/Y |
| Drag-and-drop | AllowDrop + DragEnter/Over/Leave/Drop + DragStarting/Completed |
| Focus | GotFocus, LostFocus, FocusManager.TryFocusAsync |
| Pointer capture | CapturePointer/ReleasePointerCapture with PointerCaptureLost |
| Keyboard accelerators | KeyboardAccelerator with ScopeOwner (already used by the commanding system) |

The implementation path is "expose what WinUI already has cleanly" rather
than "build a gesture pipeline from scratch." Manipulation events + inertia
are a gift — SwiftUI and Compose both had to build this.

### Comparison summary

| Capability | SwiftUI | Compose | RNGH | Reactor (today) |
|---|---|---|---|---|
| Hover (enter/exit) | `onHover` | `Modifier.hoverable` | n/a (touch) | ✗ (requires `.Set()`) |
| Tap | `TapGesture` | `clickable` | `Gesture.Tap()` | ✓ `OnTapped` |
| Double tap | `TapGesture(count: 2)` | `combinedClickable(onDouble)` | `Gesture.Tap().numberOfTaps(2)` | ✗ |
| Long press | `LongPressGesture` | `combinedClickable(onLong)` | `Gesture.LongPress()` | ✗ |
| Right click | n/a (ctx menu) | n/a (ctx menu) | n/a | ✗ |
| Pan | `DragGesture` | `detectDragGestures` | `Gesture.Pan()` | ✗ |
| Pinch/Scale | `MagnificationGesture` | `detectTransformGestures` | `Gesture.Pinch()` | ✗ |
| Rotate | `RotationGesture` | `detectTransformGestures` | `Gesture.Rotation()` | ✗ |
| Inertia/fling | Manual (via animation) | Manual | Via Reanimated | ✗ (but WinUI has it free) |
| Drag-and-drop (data transfer) | `.onDrag`/`.onDrop` | `dragAndDropSource`/`Target` | Platform-specific | ✗ |
| Focus events | `.focused(...)` | `onFocusChanged` | n/a | ✗ |

---

## Research: Event Handler Re-attachment Cost

### What happens today

Every render cycle, for every element with declarative event handlers,
`ApplyEventHandlers` in `Reconciler.cs:2242` runs this pattern six times
(once per supported event):

```csharp
if (!ReferenceEquals(m.OnPointerPressed, oldM?.OnPointerPressed))
{
    if (state.PointerPressed is not null) { fe.PointerPressed -= state.PointerPressed; state.PointerPressed = null; }
    if (m.OnPointerPressed is not null)
    {
        var handler = m.OnPointerPressed;
        state.PointerPressed = (s, e) => handler(s!, e);
        fe.PointerPressed += state.PointerPressed;
    }
}
```

Both `+=` and `-=` on a routed event cross a COM boundary and mutate the
XAML event table. `ReferenceEquals` catches the case where the user memoized
the handler with `UseCallback`, but the idiomatic Reactor style is to write
fresh lambdas in `Render()`:

```csharp
return Rectangle()
    .OnPointerPressed((s, e) => setPressed(true))       // new closure each render
    .OnPointerReleased((s, e) => setPressed(false));    // new closure each render
```

So for a list of 1,000 items that each capture a pointer handler, a single
render causes ~1,000 detach + 1,000 attach COM calls on `PointerPressed`
alone. With all six events, worst case is 12,000 COM calls on an otherwise
idle re-render. React solves this with document-level delegation — one
handler dispatches to all components. SwiftUI and Compose solve it by
keeping dispatch inside the framework's own event pipeline.

### Why not event delegation?

Reactor can't copy React's delegation trick cleanly:
- WinUI's routed events bubble through the XAML tree, but they don't
  surface at a "root" in a way that lets you forward with the correct
  `OriginalSource`. Handling at the root would also swallow event
  handling for unwrapped `Set()` users.
- Gesture/manipulation semantics are per-element in WinUI (tied to
  `ManipulationMode`, `IsTapEnabled`, etc.), so a delegated root can't
  configure them.

### The trampoline pattern

The fix is simpler: **attach once, redirect many.** Store a stable
delegate ("trampoline") on the element that reads a mutable field holding
the current user handler. The WinUI-level subscription never changes; the
mutable field is a cheap swap.

Before (every render):
```csharp
fe.PointerPressed -= oldWrapper;
state.PointerPressed = (s, e) => newHandler(s!, e);
fe.PointerPressed += state.PointerPressed;
```

After (once per element, ever):
```csharp
// First time only:
state.PointerPressedTrampoline = (s, e) => state.CurrentPointerPressed?.Invoke(s!, e);
fe.PointerPressed += state.PointerPressedTrampoline;

// Every update — just a field write:
state.CurrentPointerPressed = m.OnPointerPressed;
```

The invocation overhead is one extra null-check and field read per fire,
which is noise next to the event-args allocation that WinUI already does.
The API is unchanged — this is purely an internal optimization.

A worth-measuring concern: if a handler becomes `null`, the trampoline
stays attached and dispatches a no-op. For rarely-hovered elements that
briefly had `OnPointerEntered` wired, that's tiny overhead. The alternative
(detach when null, re-attach when non-null) rebuilds the churn we're trying
to eliminate. Recommend: always attach once, never detach until element is
released.

---

## Research: WinUI Input Peculiarities

A few things to know before designing the gesture API:

### `IsTapEnabled` / `IsDoubleTapEnabled` / `IsRightTapEnabled` / `IsHoldingEnabled`

These are `UIElement` properties (default `true` on most controls, but not
on `Rectangle` and other `Shape`s). Tapped and friends don't fire on
elements that don't have them enabled. The reconciler must set these
automatically when the corresponding modifier is present — otherwise
`OnDoubleTapped` on a `Rectangle` silently does nothing.

### `ManipulationMode`

`ManipulationMode.None` by default on almost everything. Manipulation
events (`ManipulationStarted/Delta/Completed`) only fire when flags are
set. A modifier like `.OnPan(...)` must set
`ManipulationMode = ManipulationModes.TranslateX | TranslateY | TranslateInertia`
automatically.

Inertia is opt-in via the `...Inertia` flags. The inertia runs on the
compositor and delivers `ManipulationDelta` events after the user releases
— great for fling-to-dismiss and momentum scrolls.

### Pointer capture

For drag gestures, the element needs to capture the pointer in
`PointerPressed` so `PointerReleased` fires even if the pointer leaves
the element's bounds. `fe.CapturePointer(e.Pointer)` and the capture is
released automatically on `PointerReleased`, or explicitly via
`ReleasePointerCapture`. The manipulation system handles this internally;
manual drag state machines (for thresholds etc.) must do it themselves.

### Holding semantics

`Holding` fires at Started/Completed/Canceled phases. It only fires for
touch/pen input by default — mouse right-click triggers `RightTapped`
instead, not `Holding`. Long-press on mouse requires a manual timer on
`PointerPressed`.

### Keyboard accelerators vs. KeyDown

WinUI's `KeyboardAccelerator` (already wrapped by the Command system)
handles `Ctrl+S`-style shortcuts. `OnKeyDown` is the raw routed event for
per-element key handling (arrow-key navigation inside a custom widget,
Enter to submit a form, Escape to dismiss). Both have valid use cases.

---

## Scope

### In scope

- **Tier 1 — Pointer & keyboard modifier completeness.** Fill in the
  missing declarative event modifiers so `.Set()` isn't needed for
  common interactions.
- **Tier 2 — Trampoline-based event dispatch.** Eliminate the per-render
  COM churn for event re-attachment.
- **Tier 3 — Gesture system.** `.OnPan`, `.OnLongPress`, `.OnDoubleTap`,
  `.OnPinch`, `.OnRotate` with value-typed gesture records.
- **Tier 4 — Commanding coverage extension.** Wire `Command` into
  `SplitButton`, `ToggleSplitButton`, `HyperlinkButton`, `ToggleButton`,
  `SwipeItem`, and `ContentDialog` actions.
- **Tier 5 — Focus and keyboard polish.** `.OnGotFocus`, `.OnLostFocus`,
  `.IsTabStop`, `.TabIndex`, `.AccessKey`.
- **Tier 6 — Drag-and-drop with data transfer.** Source-side
  `.OnDragStart<T>()`, target-side `.OnDrop<T>()` / `.OnDragEnter/Over/Leave`,
  typed `DragData` payload supporting text/URI/HTML/files/bitmap/custom,
  drag-visual customization, Copy/Move/Link operation negotiation.

### Out of scope

- **IME / composition events** (`TextCompositionEvents`). Relevant for
  custom text input. No framework-level precedent in Reactor; revisit
  when there's a use case.
- **Pointer wheel events.** Scroll is handled by `ScrollViewer`/`ListView`
  virtualization today. Custom wheel handling is rare enough to stay
  on `.Set()`.
- **Gesture composition operators** (`.simultaneously`, `.sequenced`).
  SwiftUI's operator algebra is elegant but heavyweight. Most apps need
  "one gesture at a time" which per-modifier APIs handle naturally.
  Defer until a concrete use case exists.
- **A command palette UI.** Downstream of Tier 4, but out of scope here.
- **Command routing to focused view.** Noted in the Commanding spec as
  future work. The input-handling gap it creates (Cut/Copy/Paste in
  multi-panel apps) stays until that work ships.

---

## Goals

1. **Close the declarative-modifier gap** so `.Set()` passthrough is never
   needed for common pointer, tap, drag, focus, or keyboard interactions.
2. **Eliminate per-render event churn** via trampoline dispatch with no
   change to the public API.
3. **Ship a first-class gesture system** that matches SwiftUI/Compose in
   ergonomics while leveraging WinUI's free inertia.
4. **Extend commanding** to the full set of command-capable WinUI controls.
5. **Expose drag-and-drop declaratively**, including typed in-process
   payloads (simple case) and the full WinUI data-transfer contract
   (cross-process / multi-format).
6. **Preserve `Set()` as the escape hatch** — the goal is to make it
   unnecessary for common cases, not to remove it.
7. **Zero breaking changes** to existing modifier signatures.

### Non-goals

- Beating React's `useGesture` library ecosystem — one solid built-in
  gesture recognizer set is the target.
- Multi-touch gesture composition beyond pinch + rotate simultaneously
  (which WinUI's manipulation system gives for free).
- Cross-platform input abstraction — Reactor is WinUI3-only.

---

## Design

### Tier 1 — Pointer & Keyboard Modifier Completeness

#### 1.1 New modifiers

Add these to `ElementModifiers` and `ElementExtensions.cs`, following the
existing pattern exactly:

```csharp
// Pointer lifecycle (the hover gap)
.OnPointerEntered(Action<object, PointerRoutedEventArgs> handler)
.OnPointerExited(Action<object, PointerRoutedEventArgs> handler)
.OnPointerCanceled(Action<object, PointerRoutedEventArgs> handler)
.OnPointerCaptureLost(Action<object, PointerRoutedEventArgs> handler)
.OnPointerWheelChanged(Action<object, PointerRoutedEventArgs> handler)

// Tap-family (right-click, double-click, long-press)
.OnRightTapped(Action<object, RightTappedRoutedEventArgs> handler)
.OnDoubleTapped(Action<object, DoubleTappedRoutedEventArgs> handler)
.OnHolding(Action<object, HoldingRoutedEventArgs> handler)

// Keyboard completeness
.OnKeyUp(Action<object, KeyRoutedEventArgs> handler)
.OnPreviewKeyDown(Action<object, KeyRoutedEventArgs> handler)
.OnPreviewKeyUp(Action<object, KeyRoutedEventArgs> handler)
.OnCharacterReceived(Action<object, CharacterReceivedRoutedEventArgs> handler)

// Focus
.OnGotFocus(Action<object, RoutedEventArgs> handler)
.OnLostFocus(Action<object, RoutedEventArgs> handler)
```

#### 1.2 Auto-enable pitfalls

Certain modifiers must set related `UIElement` properties at apply time,
or they silently do nothing:

| Modifier | Must auto-set |
|---|---|
| `.OnTapped(...)` | `fe.IsTapEnabled = true` (true by default on most controls but false on `Shape`) |
| `.OnDoubleTapped(...)` | `fe.IsDoubleTapEnabled = true` |
| `.OnRightTapped(...)` | `fe.IsRightTapEnabled = true` |
| `.OnHolding(...)` | `fe.IsHoldingEnabled = true` |
| Any pointer event on a `Shape` | `shape.Fill ??= Transparent` (hit testing requires a fill) |

The reconciler already handles some of these inconsistently; the change
is to make it systematic.

#### 1.3 Example — hover state

Today:
```csharp
var (hover, setHover) = UseState(false);

return Rectangle()
    .Set(r => {
        r.PointerEntered += (_, _) => setHover(true);
        r.PointerExited += (_, _) => setHover(false);
    });  // leaks handlers on every render
```

With Tier 1:
```csharp
var (hover, setHover) = UseState(false);

return Rectangle()
    .OnPointerEntered((_, _) => setHover(true))
    .OnPointerExited((_, _) => setHover(false));
```

The leak bug is gone (the reconciler manages the subscription lifecycle),
and the DSL reads naturally.

---

### Tier 2 — Trampoline-Based Event Dispatch

#### 2.1 EventHandlerState redesign

Replace the current "swap the subscribed delegate on every change" pattern
with a stable trampoline per event type. Each trampoline is allocated once,
subscribed to WinUI once, and reads a mutable field for the current
user handler.

```csharp
internal sealed class EventHandlerState
{
    // ── Current user handlers (mutated freely, no COM churn) ────────────
    public Action<object, SizeChangedEventArgs>? CurrentSizeChanged;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerPressed;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerMoved;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerReleased;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerEntered;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerExited;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerCanceled;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerCaptureLost;
    public Action<object, PointerRoutedEventArgs>? CurrentPointerWheelChanged;
    public Action<object, TappedRoutedEventArgs>? CurrentTapped;
    public Action<object, DoubleTappedRoutedEventArgs>? CurrentDoubleTapped;
    public Action<object, RightTappedRoutedEventArgs>? CurrentRightTapped;
    public Action<object, HoldingRoutedEventArgs>? CurrentHolding;
    public Action<object, KeyRoutedEventArgs>? CurrentKeyDown;
    public Action<object, KeyRoutedEventArgs>? CurrentKeyUp;
    public Action<object, KeyRoutedEventArgs>? CurrentPreviewKeyDown;
    public Action<object, KeyRoutedEventArgs>? CurrentPreviewKeyUp;
    public Action<object, CharacterReceivedRoutedEventArgs>? CurrentCharacterReceived;
    public Action<object, RoutedEventArgs>? CurrentGotFocus;
    public Action<object, RoutedEventArgs>? CurrentLostFocus;

    // ── Trampolines (attached once, never detached) ─────────────────────
    public SizeChangedEventHandler? SizeChangedTrampoline;
    public PointerEventHandler? PointerPressedTrampoline;
    // ... one per event above
}
```

#### 2.2 ApplyEventHandlers rewrite

The new apply loop replaces ~80 lines of per-event detach/attach with a
single macro-like pattern per event:

```csharp
private static void ApplyEventHandlers(
    FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
{
    // Fast path unchanged (just extended to all event types)
    if (!HasAnyHandler(m) && !HasAnyHandler(oldM)) return;

    var state = GetOrCreateEventState(fe);

    // ── Pointer lifecycle ───────────────────────────────────────────────
    AttachOnce(fe, state, m.OnPointerPressed,
        ref state.CurrentPointerPressed,
        ref state.PointerPressedTrampoline,
        (f, h) => f.PointerPressed += h);

    AttachOnce(fe, state, m.OnPointerEntered,
        ref state.CurrentPointerEntered,
        ref state.PointerEnteredTrampoline,
        (f, h) => f.PointerEntered += h);
    // ... etc.

    // ── Tap family: also toggle the Is*Enabled flag ─────────────────────
    if (m.OnDoubleTapped is not null) fe.IsDoubleTapEnabled = true;
    AttachOnce(fe, state, m.OnDoubleTapped,
        ref state.CurrentDoubleTapped,
        ref state.DoubleTappedTrampoline,
        (f, h) => f.DoubleTapped += h);

    if (m.OnRightTapped is not null) fe.IsRightTapEnabled = true;
    AttachOnce(fe, state, m.OnRightTapped,
        ref state.CurrentRightTapped,
        ref state.RightTappedTrampoline,
        (f, h) => f.RightTapped += h);

    if (m.OnHolding is not null) fe.IsHoldingEnabled = true;
    AttachOnce(fe, state, m.OnHolding,
        ref state.CurrentHolding,
        ref state.HoldingTrampoline,
        (f, h) => f.Holding += h);
    // ... etc.
}

// AttachOnce pattern — three responsibilities:
//   1) Write the current user handler to the mutable field.
//   2) If no trampoline yet AND the handler is non-null, create & subscribe.
//   3) Never detach — trampoline stays live for the element's lifetime.
private static void AttachOnce<TArgs, TDel>(
    FrameworkElement fe,
    EventHandlerState state,
    Action<object, TArgs>? userHandler,
    ref Action<object, TArgs>? currentField,
    ref TDel? trampolineField,
    Action<FrameworkElement, TDel> subscribe)
    where TDel : Delegate
{
    currentField = userHandler;
    if (trampolineField is null && userHandler is not null)
    {
        // Build trampoline — captures state, not the user handler.
        // Declared inline for each event type by the caller.
        // (Pseudo-code — real impl uses per-event helpers because
        //  TDel isn't unifiable across event shapes.)
    }
}
```

Because the trampoline signature varies per event type (`PointerEventHandler`
vs `TappedEventHandler` etc.), the real implementation will use one
per-event helper method rather than a fully generic `AttachOnce`. The code
is still shorter than today's pattern because each helper is ~6 lines
instead of ~10, with no detach logic.

Example per-event helper:

```csharp
private static void EnsurePointerPressedSubscribed(
    FrameworkElement fe, EventHandlerState state,
    Action<object, PointerRoutedEventArgs>? handler)
{
    state.CurrentPointerPressed = handler;
    if (state.PointerPressedTrampoline is null && handler is not null)
    {
        state.PointerPressedTrampoline = (s, e) =>
            state.CurrentPointerPressed?.Invoke(s!, e);
        fe.PointerPressed += state.PointerPressedTrampoline;
    }
}
```

#### 2.3 Expected impact

For a list of N items each with one pointer handler:

| Operation | Before | After |
|---|---|---|
| First mount | N attaches | N attaches |
| Re-render with new handler (new closure) | N detaches + N attaches = 2N COM calls | N field writes |
| Re-render with same handler (memoized) | 0 (ReferenceEquals short-circuits) | N field writes |

The "same handler" case gets slightly worse (zero → N field writes), but
a field write is in the single-digit nanoseconds vs a COM call around
100ns+. The "new handler on every render" case — the common Reactor idiom
— improves by ~50-100×.

#### 2.4 Edge case: handler becomes null

If a user writes `condition ? handler : null` and `condition` flips
repeatedly, the trampoline stays attached and dispatches to a null
`CurrentPointerPressed` — it's a no-op. No visible behavior change, tiny
dispatch overhead for an infrequent case. Acceptable.

---

### Tier 3 — Gesture System

#### 3.1 Gesture value types

Gestures are value-typed records. The handler receives a gesture value
containing everything the user needs:

```csharp
public enum GesturePhase
{
    /// <summary>Fires once when the gesture first crosses its activation threshold.</summary>
    Began,
    /// <summary>Fires on every pointer update while the gesture is active.</summary>
    Changed,
    /// <summary>Fires once when the user releases (gesture completed successfully).</summary>
    Ended,
    /// <summary>Fires once if the gesture is interrupted (pointer lost, captured by parent, etc.).</summary>
    Cancelled,
}

/// <summary>
/// A pan (single-pointer translate) gesture. Translation accumulates since Began;
/// Delta is since last callback. All values are in the gesture element's local
/// coordinate space. Distinct from drag-and-drop (Tier 6).
/// </summary>
public readonly record struct PanGesture(
    Vector2 Translation,    // cumulative offset since gesture start
    Vector2 Delta,          // offset since last callback
    Vector2 Velocity,       // pixels per second, measured at last update
    Point Position,         // current pointer position in element-local coords
    Point StartPosition,    // pointer position when gesture activated
    GesturePhase Phase,
    bool IsInertial);       // true during inertia delivery after release

/// <summary>
/// A pinch/magnification gesture. Scale is 1.0 at Began, grows/shrinks as user pinches.
/// </summary>
public readonly record struct PinchGesture(
    double Scale,           // cumulative scale (1.0 = no change since Began)
    double ScaleDelta,      // multiplicative factor since last callback
    Point Center,           // midpoint between contact points, local coords
    GesturePhase Phase,
    bool IsInertial);

/// <summary>
/// A rotation gesture. Angle accumulates since Began, signed (degrees, clockwise positive).
/// </summary>
public readonly record struct RotateGesture(
    double Angle,           // cumulative rotation in degrees
    double AngleDelta,      // degrees since last callback
    Point Center,           // rotation pivot, local coords
    GesturePhase Phase,
    bool IsInertial);

/// <summary>
/// A long-press gesture. Fires once at Began after the configured hold duration;
/// a subsequent Ended or Cancelled may fire (depending on OnReleased config).
/// </summary>
public readonly record struct LongPressGesture(
    Point Position,         // where the press happened
    TimeSpan Duration,      // how long the user has held (>= MinimumDuration at Began)
    GesturePhase Phase);
```

#### 3.2 Gesture modifiers

```csharp
// ── Pan (single-pointer translate) ──────────────────────────────────────
/// <summary>
/// Tracks a pan gesture. The element is auto-configured with
/// ManipulationMode.TranslateX | TranslateY (+ TranslateInertia if withInertia).
/// Distinct from drag-and-drop (Tier 6) — pan is for in-element transforms,
/// drag-and-drop is for data transfer.
/// </summary>
public static T OnPan<T>(
    this T el,
    Action<PanGesture> onChanged,
    Action<PanGesture>? onEnded = null,
    Action<PanGesture>? onBegan = null,
    Action<PanGesture>? onCancelled = null,
    double minimumDistance = 0.0,
    PanAxis axis = PanAxis.Both,
    bool withInertia = false)
    where T : Element;

public enum PanAxis { Both, Horizontal, Vertical }

// ── Pinch / scale ───────────────────────────────────────────────────────
public static T OnPinch<T>(
    this T el,
    Action<PinchGesture> onChanged,
    Action<PinchGesture>? onEnded = null,
    Action<PinchGesture>? onBegan = null,
    bool withInertia = false)
    where T : Element;

// ── Rotation ────────────────────────────────────────────────────────────
public static T OnRotate<T>(
    this T el,
    Action<RotateGesture> onChanged,
    Action<RotateGesture>? onEnded = null,
    Action<RotateGesture>? onBegan = null,
    bool withInertia = false)
    where T : Element;

// ── Long press ──────────────────────────────────────────────────────────
/// <summary>
/// Fires after the pointer is held for the given duration without moving.
/// Uses WinUI's Holding event for touch/pen, falls back to a PointerPressed
/// timer for mouse input (WinUI does not emit Holding for mouse).
/// </summary>
public static T OnLongPress<T>(
    this T el,
    Action onTriggered,
    TimeSpan? minimumDuration = null,  // default: 500 ms
    double cancelDistance = 10.0)       // cancels if pointer moves this far
    where T : Element;

public static T OnLongPress<T>(
    this T el,
    Action<LongPressGesture> onTriggered,
    TimeSpan? minimumDuration = null,
    double cancelDistance = 10.0)
    where T : Element;

// ── Double tap (convenience) ────────────────────────────────────────────
// Distinct from .OnDoubleTapped because the handler shape is simpler — just Action.
public static T OnDoubleTap<T>(this T el, Action onTriggered) where T : Element;
public static T OnDoubleTap<T>(this T el, Action<Point> onTriggered) where T : Element;
```

#### 3.3 Example — swipe-to-dismiss

```csharp
public record SwipeableCard(string Id, string Label) : Component
{
    public override Element Render()
    {
        var (offset, setOffset) = UseState(Vector2.Zero);
        var (isPanning, setIsPanning) = UseState(false);

        return Card(
            Text(Label).Padding(16)
        )
        .TranslateX(offset.X)
        .Scale(isPanning ? 1.05 : 1.0)
        .AnimateTransform(Curve.Spring)
        .OnPan(
            onBegan: _ => setIsPanning(true),
            onChanged: g => setOffset(g.Translation),
            onEnded: g => {
                setIsPanning(false);
                setOffset(g.Translation.X > 150 ? new Vector2(1000, 0) : Vector2.Zero);
            },
            axis: PanAxis.Horizontal,
            minimumDistance: 10);
    }
}
```

#### 3.4 Example — pinch-to-zoom

```csharp
var (scale, setScale) = UseState(1.0);

return Image(uri)
    .Scale(scale)
    .OnPinch(
        onChanged: g => setScale(Math.Clamp(g.Scale, 0.5, 4.0)),
        withInertia: true);
```

#### 3.5 Implementation notes

**Pan/Pinch/Rotate use WinUI manipulations.** When one of these modifiers
is attached, the reconciler sets `ManipulationMode` to the union of flags
needed by all attached manipulation gestures and subscribes once to
`ManipulationStarted/Delta/Completed`. The handlers dispatch to the
appropriate gesture callbacks based on which deltas are non-zero.

```csharp
// Single subscription; branches to whichever gestures are configured.
fe.ManipulationDelta += (s, e) => {
    if (state.CurrentPan is not null) {
        var translation = new Vector2((float)e.Cumulative.Translation.X, (float)e.Cumulative.Translation.Y);
        state.CurrentPan.OnChanged?.Invoke(new PanGesture(
            translation, new Vector2((float)e.Delta.Translation.X, (float)e.Delta.Translation.Y),
            new Vector2((float)e.Velocities.Linear.X, (float)e.Velocities.Linear.Y),
            e.Position, state.PanStartPosition, GesturePhase.Changed, e.IsInertial));
    }
    if (state.CurrentPinch is not null) { /* ... */ }
    if (state.CurrentRotate is not null) { /* ... */ }
};
```

**Long-press uses Holding + a mouse fallback.** WinUI's `Holding` event
doesn't fire for mouse. For mouse input, the reconciler sets a
`DispatcherTimer` on `PointerPressed` that fires after `minimumDuration`
and is cancelled on `PointerReleased` or pointer motion exceeding
`cancelDistance`.

**Minimum pan distance.** WinUI manipulations fire at a small default
threshold (~5 device pixels). To enforce a larger `minimumDistance`, the
reconciler holds the Began callback until the cumulative translation
exceeds the threshold, then emits Began + the current state as Changed.

**Coordinate space.** All gesture positions are in the gesture element's
local coordinate space. Internally, the reconciler uses
`e.Position` (already element-local on manipulation events) and
`e.GetCurrentPoint(fe).Position` for pointer events.

---

### Tier 4 — Commanding Coverage Extension

#### 4.1 Controls to add

Extend the `.Command` property path or add overloaded factories for:

| Control | Current | Target |
|---|---|---|
| `Button` | ✓ (factory overload) | unchanged |
| `AppBarButton` | ✓ | unchanged |
| `MenuFlyoutItem` | ✓ | unchanged |
| `SplitButton` | bare `Action OnClick` | `.Command(Command)` |
| `ToggleSplitButton` | bare `Action OnClick` | `.Command(Command)` |
| `HyperlinkButton` | bare `Action OnClick` | `.Command(Command)` |
| `ToggleButton` | bare `Action<bool> OnToggled` | `.Command(Command)` (with toggle semantics — see 4.3) |
| `RepeatButton` | bare `Action OnClick` | `.Command(Command)` |
| `SwipeItem` | bare `Action OnInvoked` | `SwipeItem(Command command)` factory |
| `ContentDialog` | `PrimaryButtonCommand?`, `SecondaryButtonCommand?`, `CloseButtonCommand?` | `ContentDialog(...).PrimaryCommand(Command).SecondaryCommand(Command).CloseCommand(Command)` |
| `NavigationViewItem` | bare click (it's navigation though) | **skipped** — navigation, not command |

#### 4.2 Command → control binding (consistent with §12)

The binding rules already established for Button/AppBarButton carry over:

- `Command.Label` → control content (if control content is unset)
- `Command.Icon` → control icon (if the control has an icon slot)
- `Command.Description` → `ToolTipService.ToolTip` and
  `AutomationProperties.HelpText`
- `Command.Accelerator` → `KeyboardAccelerators` collection (registered
  within the focus scope, as today)
- `Command.AccessKey` → `fe.AccessKey`
- `Command.IsEnabled` → `fe.IsEnabled`
- Click / Invoked / Swiped → `Command.Execute` or `ExecuteAsync`
- Per-site overrides (`.Label("Custom")` after `.Command(cmd)`) continue
  to win, as today.

#### 4.3 ToggleButton special case

`ToggleButton`'s primary signal is `IsChecked`, not a fire-and-forget
click. For it to accept a `Command`, we need to decide what the command
represents:

**Option A — Command fires on each toggle (like a click).**
```csharp
ToggleButton("Mute", isMuted).Command(muteCmd);  // fires muteCmd.Execute on every toggle
```
Symmetric with Button. The command may toggle state internally.

**Option B — Introduce `ToggleCommand` that knows about checked state.**
Heavyweight. Rejected: MVVM `RelayCommand` users just fire the method
and toggle state in the handler; our model should match.

**Chosen: Option A.** `ToggleButton(label, isChecked).Command(cmd)` fires
on each toggle; the handler is responsible for flipping state. This
matches how Button(cmd) works today.

#### 4.4 ContentDialog

The dialog has three action buttons, each needing its own command binding:

```csharp
ContentDialog(
    title: "Delete file?",
    content: Text("This cannot be undone."))
  .PrimaryCommand(StandardCommand.Delete(onExecute: () => fs.Delete(path)))
  .CloseCommand(StandardCommand.Cancel());
```

Implementation: `ContentDialog` element gets three optional `Command?`
slots (PrimaryCommand, SecondaryCommand, CloseCommand); when set, they
replace the existing `PrimaryButtonText`/`PrimaryButtonClick` wiring.

---

### Tier 5 — Focus and Keyboard Polish

```csharp
// Focus events already listed in Tier 1 (OnGotFocus, OnLostFocus).

// ── Focus navigation ────────────────────────────────────────────────────
.IsTabStop(bool value = true)
.TabIndex(int value)
.TabNavigation(KeyboardNavigationMode mode)  // Local, Cycle, Once
.XYFocusKeyboardNavigation(XYFocusKeyboardNavigationMode mode)

// ── Access keys ────────────────────────────────────────────────────────
.AccessKey(string key)                       // "F" — activates on Alt+F
.AccessKeyDisplayRequested(Action handler)   // custom tip UI

// ── Imperative focus ───────────────────────────────────────────────────
public static class FocusManager
{
    public static bool Focus(ElementRef target, FocusState state = FocusState.Programmatic);
    public static Task<bool> FocusAsync(ElementRef target, FocusState state = FocusState.Programmatic);
}

// UseFocus hook — programmatic focus on next render
public static (ElementRef Ref, Action RequestFocus) UseFocus();
```

The imperative `FocusManager.Focus` operates on an `ElementRef` — a handle
you can obtain from `UseRef<FrameworkElement>()` combined with `.Ref(refObj)`
on any element. `UseFocus()` is a convenience hook that bundles the
ref allocation with a `RequestFocus` callback that dispatches on the
next render.

Example:
```csharp
var (inputRef, focusInput) = UseFocus();

UseEffect(() => { focusInput(); return null; }, []);  // focus on mount

return TextField()
    .Ref(inputRef)
    .Placeholder("Search...");
```

---

### Tier 6 — Drag-and-Drop with Data Transfer

Drag-and-drop (DnD) is the OS-level data-transfer protocol: files dropped
from Explorer, text dropped from a browser, items reordered between
lists. It's a core Windows interaction and the review's "no gesture
system" finding undersells how much is missing — WinUI ships the full
protocol (`DragStarting`, `DragEnter`, `DragOver`, `DragLeave`, `Drop`,
`DropCompleted`, `DragUIOverride`, `DataPackage`) and Reactor exposes
none of it declaratively.

#### 6.1 Research: WinUI3's DnD model

WinUI's model matches the OS-level protocol:

- **Source side.** `UIElement.CanDrag = true` enables drag. `DragStarting`
  fires when the user begins dragging; the handler populates the event
  args' `DataPackage` with one or more formats and sets
  `RequestedOperation` (Copy, Move, Link, or any combination). Optionally
  supply a `DragUI` (custom ghost image). `DropCompleted` fires when the
  drop resolves and tells the source which operation the target performed.
- **Target side.** `UIElement.AllowDrop = true` enables drop.
  `DragEnter`/`DragOver` fire while the pointer hovers; the target sets
  `args.AcceptedOperation` to advertise what it will do. Targets can
  customize the drag cursor via `args.DragUIOverride` (caption, glyph,
  translucent preview toggle). `DragLeave` fires when the pointer exits
  without dropping. `Drop` fires on release over an accepting target —
  the handler reads the `DataPackage` and performs the operation.
- **`DataPackageView`.** Read-only façade over `DataPackage` inside
  target events. Formats: `Text`, `Uri`, `Html`, `Rtf`, `Bitmap`,
  `StorageItems` (files), `ApplicationLink`, `WebLink`, plus arbitrary
  custom formats keyed by string (canonical form is a reverse-DNS or
  MIME-like identifier, e.g., `"application/x-reactor-task"`).
- **Format deferral (first-class in WinUI).**
  `DataPackage.SetDataProvider(format, DataProviderHandler)` lets the
  source register a callback that's only invoked when a target actually
  calls `GetDataAsync(format)` for that specific format. The handler
  receives a `DataProviderRequest` with `GetDeferral()` for async
  resolution, and the contract works cross-process — WinUI relays the
  request to the source app even when the target is another app.
  **Every format supports this, not just custom formats.** Text, URI,
  HTML, RTF, bitmap, and StorageItems can all be supplied via a
  provider instead of a pre-computed value.
- **Cross-process contract.** Text, URI, HTML, RTF, bitmap, and
  StorageItems are universally interoperable. Custom formats only
  interoperate when both ends are in the same process (same app).

#### 6.2 Design goals

1. **Typed in-process DnD is trivial.** `.OnDragStart<Task>(() => task)`
   on the source and `.OnDrop<Task>(t => ...)` on the target — no
   serialization, no format strings, no ceremony.
2. **Cross-process data transfer is first-class.** A `DragData` record
   exposes text, URI, HTML, files, and bitmap cleanly, plus custom
   formats for advanced cases.
3. **Every format can be supplied lazily.** HTML/RTF generation,
   bitmap rendering, file materialization, and custom serialization
   are often the expensive parts of DnD. Each `.With*` method has a
   `Func<T>` / `Func<CancellationToken, Task<T>>` overload that's
   only invoked when a target actually requests that format — works
   cross-process via WinUI's `DataProviderHandler` contract. The
   source-side `getData` callback itself is also invoked lazily at
   `DragStarting` time, not on every render.
4. **Drop targets are declarative.** Modifiers for `.OnDragEnter`,
   `.OnDragOver`, `.OnDragLeave`, `.OnDrop` — all typed and value-based.
5. **Drag visuals are composable.** A `dragVisual` callback returns a
   `Element` that Reactor renders to a bitmap and hands to WinUI's
   `DragUI` — no raw `SoftwareBitmap` wrangling.
6. **Operation negotiation is honest.** Source declares allowed ops;
   target accepts a single op (respecting modifier keys). `DropCompleted`
   routes back to the source so it can finalize (e.g., remove the item
   after a Move).
7. **Reordering within a single app feels natural.** In-process typed
   payloads + a `.OnDrop<T>` hook should make list reordering a
   ~15-line pattern.

#### 6.3 Core types

```csharp
/// <summary>
/// The payload for a drag-and-drop operation. Supports OS-standard formats
/// (text, URI, HTML, RTF, files, bitmap) plus typed in-process payloads and
/// arbitrary custom formats. Every format can be supplied eagerly (value
/// already computed) or lazily (provider invoked only if a target requests
/// that specific format — including cross-process targets, via WinUI's
/// DataProviderHandler contract).
///
/// Build with .With* methods on the source side; inspect with .TryGet* on
/// the target side. For each format there are three source-side overloads:
///   .WithX(TValue value)                                     — eager
///   .WithX(Func&lt;TValue&gt; provider)                           — lazy, sync
///   .WithX(Func&lt;CancellationToken, Task&lt;TValue&gt;&gt; provider)   — lazy, async
/// </summary>
public sealed class DragData
{
    // ── Static convenience factories ────────────────────────────────────
    public static DragData Text(string text) => new DragData().WithText(text);
    public static DragData Uri(Uri uri) => new DragData().WithUri(uri);
    public static DragData Files(IEnumerable<IStorageItem> files)
        => new DragData().WithFiles(files);
    public static DragData Typed<T>(T payload) where T : class
        => new DragData().WithTypedPayload(payload);

    // ── Text (eager + lazy) ─────────────────────────────────────────────
    public DragData WithText(string text);
    public DragData WithText(Func<string> provider);
    public DragData WithText(Func<CancellationToken, Task<string>> provider);

    // ── URI (eager + lazy) ──────────────────────────────────────────────
    public DragData WithUri(Uri uri);
    public DragData WithUri(Func<Uri> provider);
    public DragData WithUri(Func<CancellationToken, Task<Uri>> provider);

    // ── HTML (expensive to generate — lazy recommended) ─────────────────
    public DragData WithHtml(string html);
    public DragData WithHtml(Func<string> provider);
    public DragData WithHtml(Func<CancellationToken, Task<string>> provider);

    // ── RTF (expensive to generate — lazy recommended) ──────────────────
    public DragData WithRtf(string rtf);
    public DragData WithRtf(Func<string> provider);
    public DragData WithRtf(Func<CancellationToken, Task<string>> provider);

    // ── Bitmap (expensive — lazy-first API; eager overload for convenience) ─
    public DragData WithBitmap(SoftwareBitmap bmp);
    public DragData WithBitmap(Func<CancellationToken, Task<SoftwareBitmap>> provider);
    /// <summary>
    /// Convenience: render a Reactor Element to a bitmap lazily. Only rasterized
    /// if a target requests the bitmap format. Internally uses RenderTargetBitmap.
    /// </summary>
    public DragData WithBitmapFromElement(Func<Element> build);

    // ── Files / StorageItems ────────────────────────────────────────────
    public DragData WithFiles(IEnumerable<IStorageItem> files);
    public DragData WithFiles(Func<CancellationToken, Task<IEnumerable<IStorageItem>>> provider);

    /// <summary>
    /// Adds a typed in-process payload. Keyed by typeof(T).FullName; only
    /// resolvable by targets inside the same app instance. Eager only — the
    /// payload is an object reference, so there's nothing to defer.
    /// Combine with .WithText/.WithFiles (eager or lazy) to also provide
    /// cross-process formats.
    /// </summary>
    public DragData WithTypedPayload<T>(T payload) where T : class;

    // ── Custom formats (arbitrary identifier) ───────────────────────────
    /// <summary>
    /// Adds a custom format with a caller-chosen identifier. Use reverse-DNS
    /// or MIME-like strings (e.g., "application/x-myapp-foo") for
    /// cross-process formats; typed payloads are cleaner for in-process.
    /// </summary>
    public DragData WithCustomFormat(string formatId, object payload);
    public DragData WithCustomFormat(string formatId, Func<object> provider);
    public DragData WithCustomFormat(string formatId,
        Func<CancellationToken, Task<object>> provider);

    // ── Inspector (target side) ─────────────────────────────────────────
    // Sync accessors — only succeed if the format is eagerly present OR
    // was already resolved by an earlier request. For potentially-deferred
    // formats, prefer the async accessors below.
    public bool TryGetText([MaybeNullWhen(false)] out string text);
    public bool TryGetUri([MaybeNullWhen(false)] out Uri uri);
    public bool TryGetHtml([MaybeNullWhen(false)] out string html);
    public bool TryGetRtf([MaybeNullWhen(false)] out string rtf);
    public bool TryGetFiles([MaybeNullWhen(false)] out IReadOnlyList<IStorageItem> files);
    public bool TryGetBitmap([MaybeNullWhen(false)] out SoftwareBitmap bmp);
    public bool TryGetTypedPayload<T>([MaybeNullWhen(false)] out T payload) where T : class;
    public bool TryGetCustomFormat<T>(string formatId, [MaybeNullWhen(false)] out T payload);

    // Async accessors — resolve deferred providers on demand. These
    // force the source's provider to run (cross-process hop if needed).
    public Task<string?> GetTextAsync(CancellationToken ct = default);
    public Task<Uri?> GetUriAsync(CancellationToken ct = default);
    public Task<string?> GetHtmlAsync(CancellationToken ct = default);
    public Task<string?> GetRtfAsync(CancellationToken ct = default);
    public Task<IReadOnlyList<IStorageItem>?> GetFilesAsync(CancellationToken ct = default);
    public Task<SoftwareBitmap?> GetBitmapAsync(CancellationToken ct = default);
    public Task<T?> GetCustomFormatAsync<T>(string formatId, CancellationToken ct = default);

    /// <summary>
    /// Whether a format is advertised by the source. Cheap — doesn't trigger
    /// provider resolution. Use during OnDragEnter/Over to decide whether
    /// to accept the drop.
    /// </summary>
    public bool HasFormat(string formatId);

    /// <summary>
    /// All formats advertised by the source (resolved or deferred).
    /// </summary>
    public IReadOnlyCollection<string> AvailableFormats { get; }
}

/// <summary>
/// What the source is willing to allow. Drag-and-drop negotiates a single
/// resulting operation from this set, filtered by the user's modifier keys
/// (Ctrl = prefer Copy, Shift = prefer Move, Alt = prefer Link).
/// </summary>
[Flags]
public enum DragOperations
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    All = Copy | Move | Link,
}

/// <summary>
/// Arguments passed to target-side drag handlers. Mutable — handlers set
/// AcceptedOperation to advertise what they'll do; reading the same value
/// back in OnDrop tells you which op was negotiated.
/// </summary>
public sealed class DragTargetArgs
{
    public DragData Data { get; }
    public Point Position { get; }                // pointer in target-local coords
    public DragOperations AllowedOperations { get; }     // what source allows
    public DragOperations Modifiers { get; }             // what user's keys suggest

    /// <summary>
    /// The operation the target will perform if dropped. Defaults to None
    /// (target will reject). Set in OnDragEnter/OnDragOver.
    /// </summary>
    public DragOperations AcceptedOperation { get; set; }

    /// <summary>
    /// Customize the drag cursor: caption text, glyph visibility, preview
    /// translucency. Applied immediately.
    /// </summary>
    public DragUIOverrideHandle UIOverride { get; }
}

public sealed class DragUIOverrideHandle
{
    public string? Caption { get; set; }
    public bool IsCaptionVisible { get; set; } = true;
    public bool IsContentVisible { get; set; } = true;
    public bool IsGlyphVisible { get; set; } = true;
}

/// <summary>
/// Tells the source how a completed drag resolved. Passed to OnDragEnd if
/// the source supplied one. Use to finalize Move (delete source copy) or
/// cancel any optimistic UI.
/// </summary>
public readonly record struct DragEndContext(
    DragOperations CompletedOperation,
    bool WasCancelled);
```

#### 6.4 Source-side modifiers

```csharp
// ── Simple typed payload ────────────────────────────────────────────────
/// <summary>
/// Marks the element as a drag source with a typed in-process payload.
/// Also auto-registers the custom format so the same-app targets can
/// read it via OnDrop&lt;T&gt;.
/// </summary>
public static T OnDragStart<T, TPayload>(
    this T el,
    Func<TPayload> getPayload,
    DragOperations allowedOperations = DragOperations.Copy | DragOperations.Move,
    Func<TPayload, Element>? dragVisual = null,
    Action<DragEndContext>? onEnd = null)
    where T : Element where TPayload : class;

// ── Rich multi-format payload ───────────────────────────────────────────
/// <summary>
/// Marks the element as a drag source with a full DragData manifest.
/// Use for multi-format payloads (text + typed, files + custom, etc.)
/// or cross-process interop.
/// </summary>
public static T OnDragStart<T>(
    this T el,
    Func<DragData> getData,
    DragOperations allowedOperations = DragOperations.Copy | DragOperations.Move,
    Func<Element>? dragVisual = null,
    Action<DragEndContext>? onEnd = null)
    where T : Element;

// ── Imperative guard ────────────────────────────────────────────────────
/// <summary>
/// Rarely needed: conditionally suppress drag when the element is otherwise
/// marked draggable. The predicate is evaluated at drag-initiation time.
/// </summary>
public static T DraggableWhen<T>(this T el, Func<bool> canDrag) where T : Element;
```

#### 6.5 Target-side modifiers

```csharp
// ── Typed drop — the 80% case ───────────────────────────────────────────
/// <summary>
/// Accepts a typed in-process payload of T. Automatically:
///   - sets AllowDrop = true
///   - accepts Copy on DragEnter when the payload matches
///   - reads the typed payload on Drop and invokes the handler
/// If both OnDrop&lt;T&gt; and OnDrop (raw) are attached, OnDrop&lt;T&gt;
/// takes precedence when the payload matches.
/// </summary>
public static T OnDrop<T, TPayload>(
    this T el,
    Action<TPayload> onDrop,
    DragOperations acceptedOperations = DragOperations.Copy | DragOperations.Move)
    where T : Element where TPayload : class;

// ── Raw drop — for multi-format / cross-process ────────────────────────
public static T OnDrop<T>(this T el, Action<DragTargetArgs> onDrop) where T : Element;

// ── Drag-over lifecycle (visual feedback) ───────────────────────────────
public static T OnDragEnter<T>(this T el, Action<DragTargetArgs> handler) where T : Element;
public static T OnDragOver<T>(this T el, Action<DragTargetArgs> handler) where T : Element;
public static T OnDragLeave<T>(this T el, Action<DragTargetArgs> handler) where T : Element;
```

#### 6.6 Example — typed drag reordering

The canonical test case: drag cards between kanban columns.

```csharp
public record Column(string Id, ImmutableList<Task> Tasks) { }

public record KanbanBoard : Component
{
    public override Element Render()
    {
        var (columns, setColumns) = UseState(ImmutableList.Create<Column>(/* ... */));

        void MoveTask(Task task, string toColumnId)
        {
            setColumns(cols => cols
                .Select(c => c with { Tasks = c.Tasks.Remove(task) })
                .Select(c => c.Id == toColumnId
                    ? c with { Tasks = c.Tasks.Add(task) }
                    : c)
                .ToImmutableList());
        }

        return HStack(columns.Select(col =>
            VStack(
                Text(col.Id).FontSize(18),
                VStack(col.Tasks.Select(task =>
                    TaskCard(task)
                        .OnDragStart<TaskCard, Task>(
                            getPayload: () => task,
                            allowedOperations: DragOperations.Move,
                            dragVisual: t => TaskCard(t).Opacity(0.8),
                            onEnd: ctx => {
                                if (ctx.CompletedOperation == DragOperations.None)
                                    ShowToast("Drop cancelled");
                            })
                ).ToArray())
            )
            .Padding(16)
            .OnDragEnter<VStack>(args => args.AcceptedOperation = DragOperations.Move)
            .OnDragOver<VStack>(args => args.AcceptedOperation = DragOperations.Move)
            .OnDrop<VStack, Task>(task => MoveTask(task, col.Id))
        ).ToArray());
    }
}
```

#### 6.7 Example — accept files from Explorer

```csharp
return DropZone()
    .OnDragEnter<DropZone>(args => {
        if (args.Data.HasFormat("FileStorageItems"))
        {
            args.AcceptedOperation = DragOperations.Copy;
            args.UIOverride.Caption = "Import files";
        }
    })
    .OnDrop<DropZone>(async args => {
        if (args.Data.TryGetFiles(out var files))
        {
            foreach (var file in files.OfType<StorageFile>())
                await ImportFile(file);
        }
    });
```

#### 6.8 Example — multi-format source with lazy generation

Drag a Task that survives as plain text, HTML (for rich-text targets),
and a URI. Cheap formats are eager; expensive HTML rendering is
deferred until a target actually asks for it:

```csharp
.OnDragStart<TaskCard>(
    getData: () => DragData
        .Text(task.Title)                                 // eager — cheap
        .WithTypedPayload(task)                            // eager — object ref
        .WithUri(new Uri($"myapp://task/{task.Id}"))       // eager — cheap
        .WithHtml(() => RenderTaskAsHtml(task))            // lazy — only if a rich-text target asks
        .WithBitmapFromElement(() => TaskCard(task))       // lazy — only if a paint target asks
        .WithCustomFormat(
            "application/x-myapp-task-snapshot",
            async ct => await SerializeTaskSnapshot(task, ct)),  // lazy async
    allowedOperations: DragOperations.Copy | DragOperations.Move,
    dragVisual: () => TaskCard(task).Opacity(0.8))
```

Dragging onto Notepad triggers only `WithText`. Dragging onto Word
triggers `WithHtml` (running `RenderTaskAsHtml` once). Dragging
nowhere (cancel) triggers none of the lazy providers. The task's
HTML serialization never runs unless it's actually needed.

#### 6.9 Implementation notes

**Typed payload storage.** The custom format identifier for a typed
payload is `$"reactor/typed/{typeof(TPayload).FullName}"`. The payload
itself is stored via a `ConditionalWeakTable<DataPackage, object>`
keyed on the DataPackage, since `DataPackage.SetData` requires
serializable content for cross-process transport but we only need
in-process access. For cross-process scenarios, developers should add a
standard format (text, URI) or an explicit serializable custom format.

**Custom format naming collision.** If two apps both use the custom
format `"reactor/typed/MyApp.Task"`, cross-process drops could be
confused. This is why typed payloads are treated as in-process only in
the `OnDrop<T>` path — we check that the drag originated in the same
process (via a DragData marker) before resolving the typed payload.

**`AllowDrop` and `CanDrag`.** The reconciler sets these automatically
based on attached modifiers. An element with `OnDragStart` gets
`CanDrag = true`; an element with any `OnDrop*` modifier gets
`AllowDrop = true`.

**Drag visual rendering.** The `dragVisual` callback returns a Reactor
`Element`. The reconciler mounts it in a detached visual subtree,
invokes `RenderTargetBitmap.RenderAsync`, converts the result to a
`SoftwareBitmap`, and assigns it to `DragStartingEventArgs.DragUI`.
Falls back to a screenshot of the source element if `dragVisual` is
null. This is synchronous-enough (~5ms) for drag initiation.

**Hover auto-scroll inside a ScrollViewer.** WinUI handles this — when
the drag pointer approaches the edge of a `ScrollViewer`, the viewer
auto-scrolls. No extra work on our side, but worth documenting.

**Operation negotiation rules.**
1. Source declares `allowedOperations` (e.g., `Copy | Move`).
2. Each target's `AcceptedOperation` is the operation that would occur
   if the user released now.
3. Modifier keys filter: Ctrl prefers Copy, Shift prefers Move, Alt
   prefers Link, no modifier uses the target's preferred op.
4. `DropCompleted` reports the final operation to the source, which
   uses it to finalize (e.g., remove the source item if `Move`).

**Deferred formats — implementation.** Every `.With*` overload that
takes a `Func<T>` or `Func<CancellationToken, Task<T>>` registers via
`DataPackage.SetDataProvider(formatId, handler)`. The handler adapter:

```csharp
dataPackage.SetDataProvider(StandardDataFormats.Html, request => {
    var deferral = request.GetDeferral();
    try
    {
        var cts = new CancellationTokenSource();
        // DataProviderRequest doesn't expose cancellation directly; we tie the
        // token to the request's deadline via a WinUI background task if needed.
        var html = await userProvider(cts.Token).ConfigureAwait(false);
        request.SetData(html);
    }
    finally { deferral.Complete(); }
});
```

The provider runs on a background thread (WinUI guarantees this for
`DataProviderHandler`). For cross-process drags, the target's
`GetDataAsync` call is relayed back to the source process and the
provider runs there. Providers should be thread-safe and avoid
touching the UI thread without marshalling.

**Sync vs async accessors on the target side.** `TryGetText` succeeds
only if the format is either eager or already resolved by an earlier
call. `GetTextAsync` always resolves — awaiting the provider if
necessary. In typical `OnDrop` handlers, prefer the async accessors;
they work uniformly regardless of how the source populated the format.

**Typed payloads are never deferred.** `.WithTypedPayload<T>(T)` is
eager-only — it's an object reference inside the same process, there's
nothing to defer. Typed payloads also skip the `SetDataProvider`
machinery entirely and are stashed in a process-local
`ConditionalWeakTable<DataPackage, object>`.

---

## Implementation Plan

Phased delivery so each tier is independently valuable and testable.

### Phase 1 — Pointer modifier completeness (Tier 1)

- Add new fields to `ElementModifiers`.
- Add extension methods to `ElementExtensions.cs`.
- Extend `EventHandlerState` with new fields (keep old attach/detach
  pattern — Tier 2 rewrites it).
- Extend `ApplyEventHandlers` with new events + auto-enable flag logic.
- Unit tests in `ReactorElementExtensionsTests` covering each new modifier.
- Integration test: Outlook clone's hover state adopts `.OnPointerEntered`
  /`.OnPointerExited` instead of `.Set()`.

**Impact:** closes review critiques #4 and #5. Largest ergonomic win
for minimal code.

### Phase 2 — Trampoline dispatch (Tier 2)

- Refactor `EventHandlerState` to the Current/Trampoline pattern.
- Rewrite `ApplyEventHandlers` using per-event `EnsureXxxSubscribed`
  helpers.
- Microbenchmark the new pattern against the old — target 10×+ reduction
  in re-render time for event-heavy lists.
- Regression test: all existing event-handler tests pass unchanged.
- ETW trace enrichment: add a `reactor:event.reattach` keyword so
  re-attachment overhead can be profiled (and ideally stay near zero
  after the change).

**Impact:** closes review critique #2. No API surface change.

### Phase 3 — Gesture value types + manipulation wiring (Tier 3 part 1)

- Define `PanGesture`, `PinchGesture`, `RotateGesture`, `LongPressGesture`
  record structs.
- Add `.OnPan`, `.OnPinch`, `.OnRotate` modifiers.
- Wire `ManipulationMode` computation based on which gesture modifiers
  are attached.
- Single `ManipulationDelta` trampoline that dispatches to each gesture.
- Minimum-distance gating logic for `.OnPan`.
- Gallery sample: pan-to-translate a card with inertia.

### Phase 4 — Long press, double-tap, access keys (Tier 3 part 2 + Tier 5)

- `.OnLongPress` with Holding-event path + mouse timer fallback.
- `.OnDoubleTap` convenience (on top of `.OnDoubleTapped` from Phase 1).
- Focus / tab / access-key modifiers.
- `UseFocus()` hook.
- Gallery sample: long-press a list item to show a context menu.

### Phase 5 — Commanding coverage extension (Tier 4)

- `.Command(cmd)` on SplitButton, ToggleSplitButton, HyperlinkButton,
  ToggleButton, RepeatButton.
- `SwipeItem(Command)` factory.
- `ContentDialog.PrimaryCommand/SecondaryCommand/CloseCommand`.
- Update CommandingDemo to exercise each.
- Documentation: extend the commanding doc-pipeline template with a
  "command-capable controls" section.

### Phase 6 — Drag-and-drop with data transfer (Tier 6)

Ships in two sub-phases so the 80% case lands before the full protocol.

**6a — Typed in-process DnD.**
- `DragData` record with builder + inspector APIs.
- `DragOperations` flags; `DragTargetArgs`; `DragEndContext`.
- Source modifier: `.OnDragStart<T, TPayload>(...)`.
- Target modifiers: `.OnDrop<T, TPayload>(...)`, `.OnDragEnter/Over/Leave`.
- Reconciler wiring: auto-set `CanDrag`/`AllowDrop`; subscribe once per
  event (using the Tier 2 trampoline pattern so DnD doesn't regress the
  re-attachment fix).
- Custom format identifier convention (`"reactor/typed/<T.FullName>"`)
  + same-process verification marker.
- Gallery sample: three-column kanban with typed-payload drag reordering.

**6b — Cross-process + rich data transfer.**
- Text / URI / HTML / RTF / files / bitmap source-side support —
  each format with both eager and lazy (`Func<T>` +
  `Func<CancellationToken, Task<T>>`) overloads.
- `DataProviderHandler` adapter that converts Reactor's `Func<...>`
  providers into WinUI deferrals (including thread marshalling and
  deferral completion).
- `WithBitmapFromElement(Func<Element>)` convenience — renders via
  `RenderTargetBitmap` only when a paint target requests the bitmap
  format.
- Raw `.OnDrop<T>(Action<DragTargetArgs>)` overload for multi-format
  targets.
- Async target accessors (`GetTextAsync`, `GetHtmlAsync`,
  `GetCustomFormatAsync<T>`, etc.) that resolve deferred providers.
- `DragUIOverrideHandle` with caption / glyph / content-visibility.
- `dragVisual` → `SoftwareBitmap` rendering via `RenderTargetBitmap`
  for the drag preview (distinct from `WithBitmapFromElement`, which
  is the payload itself).
- Gallery sample: file-import drop zone accepting Explorer drops; text
  drop from an external browser; a source that advertises expensive
  HTML lazily and verifies (via logging) that the provider only fires
  when the drop target is a rich-text consumer.

**6c — `DropCompleted` finalization.**
- Wire `onEnd: Action<DragEndContext>?` on source modifiers to
  `DragSource.DropCompleted` and `DragStarting`'s cancellation.
- Document the Move pattern: source doesn't remove the item
  optimistically; it waits for `DragEndContext.CompletedOperation == Move`.
- Regression test: Move that's converted to Copy at the target (via
  Ctrl) doesn't remove from source.

### Phase 7 — Showcase adoption

Per the critical review's recurring concern ("features work in isolation
but showcase apps don't adopt them"):

- Outlook clone: `.OnPointerEntered/Exited` for list-item hover, `.OnPan`
  for the draggable message preview pane divider, `.OnDragStart`/`.OnDrop`
  for moving messages between folders.
- ReactorFiles: `.OnDoubleTapped` to open, `.OnRightTapped` for context
  menu, `.OnDragStart`/`.OnDrop` for file reorder and folder moves,
  accepting Explorer file drops.
- Word-puzzle game: `.OnPan` for tile drag within the board (no data
  transfer), `.OnDragStart`/`.OnDrop` if tiles ever move between word
  racks.

---

## Migration and Compatibility

- **All existing modifiers keep their signatures.** `OnPointerPressed`,
  `OnPointerMoved`, `OnPointerReleased`, `OnTapped`, `OnKeyDown`,
  `OnSizeChanged` are untouched.
- **No semantic changes to dispatch order.** Trampolines preserve
  the current first-attached-first-called ordering.
- **`.Set()` continues to work.** Users who already wired `PointerEntered`
  via `.Set()` see no change. The new modifiers are purely additive.
- **Command-bound controls degrade gracefully.** Adding `.Command(cmd)`
  to a `SplitButton` that previously used `OnClick` is opt-in; bare
  `OnClick` users are unaffected.
- **`EventHandlerState` is internal.** The refactor is invisible to
  consumers.

---

## Open Questions

1. ~~**Gesture naming — `OnDrag` vs `OnPan`?**~~ **Resolved.** The
   single-pointer translate gesture is `OnPan` / `PanGesture` (matches
   Flutter and React Native Gesture Handler). `OnDragStart` / `OnDrop`
   are reserved for drag-and-drop data transfer (Tier 6). Distinct
   names avoid the SwiftUI ambiguity where `.onDrag` has two meanings.

2. **Should `OnPan` live on `ScrollViewer` content?** WinUI's scroll
   viewers consume manipulations. If a user attaches `.OnPan` inside a
   `ScrollViewer`, the scroll viewer will typically win. This is
   platform-correct behavior but surprising. Options: (a) document as
   a known limitation, (b) auto-set `ScrollViewer.IsHorizontalRailEnabled`
   / `IsVerticalRailEnabled` based on `PanAxis`. Recommend (a).

3. **LongPress mouse fallback — should it exist at all?** WinUI
   deliberately maps mouse right-click to `RightTapped` rather than
   `Holding`. The fallback timer might produce surprising results on
   mouse (fires on any press that happens to linger). Options: (a)
   mouse fallback as shown, (b) no fallback — `.OnLongPress` is
   touch/pen only, (c) a flag to opt into mouse emulation. Recommend
   (c): default false, developers opt in if they want the behavior.

4. **Should `Command` bind to `ToggleButton.IsChecked`?** The chosen
   design fires the command on each toggle but leaves the `IsChecked`
   binding to the user. Alternative: Command has an optional
   `IsOn: bool` field that drives `IsChecked`. Deferred — `ToggleButton`
   is rare enough not to warrant a breaking Command shape change.

5. **Gesture composition operators?** SwiftUI's
   `.simultaneously`/`.sequenced`/`.exclusively` are elegant but a
   substantial design surface. Current stance is out-of-scope — per-modifier
   APIs cover ~95% of real use cases. Revisit if a real use case emerges.

6. **Should pointer-wheel events stay `.Set()`-only?** The review
   doesn't call it out specifically. It's included in Tier 1 for
   completeness but could be dropped. Recommend keeping — zero marginal
   cost once the trampoline pattern is in place, and custom scrollable
   widgets (timeline zoom, canvas pan) benefit.

7. **Does Phase 7 showcase work block earlier phases?** Past specs
   shipped features without refactoring showcase apps, which the
   critical review repeatedly flags as a red flag. Recommend: treat
   Phase 7 as a hard dependency — Tiers 1–6 aren't "done" until at
   least one original showcase app adopts the new surface.

8. **Typed-payload same-process check — how strict?** The design
   verifies that a typed payload came from the same process before
   resolving `OnDrop<T>`. The check uses a hidden marker format
   injected into every `DragData`. Alternative stricter check: use
   `Process.GetCurrentProcess().Id` in the marker. Alternative looser
   check: allow typed payloads to cross processes if `T` is marked
   `[Serializable]` and we JSON-encode the payload. Recommend starting
   strict (same-process-only, no serialization), and adding opt-in
   cross-process serialization later if a real use case emerges.

9. **Should `OnDrop<T>` also accept an `acceptedOperation` callback?**
   Current design takes a static `DragOperations` flags. A callback
   (`Func<T, DragOperations>`) would allow "accept Move for open tasks,
   Copy for archived" semantics. Recommend: defer. The static flag
   covers 90%+ of cases; the raw `OnDrop` overload is the escape hatch.

---

## Success Criteria

The input/events score moves from **C** toward **A-** (SwiftUI / Compose
tier) on the next critical-review rescore, specifically:

- No pointer interaction in any showcase app requires `.Set()`.
- The five review critiques are all addressed:
  - ✓ Gesture system exists (Tier 3).
  - ✓ Event handlers don't re-attach on every render (Tier 2).
  - ✓ Commanding covers all command-capable controls (Tier 4).
  - ✓ PointerEntered/Exited modifiers exist (Tier 1).
  - ✓ RightTapped/DoubleTapped/Holding modifiers exist (Tier 1).
- Drag-and-drop with data transfer is first-class (Tier 6): typed
  in-process reordering, file drops from Explorer, and multi-format
  sources all work declaratively.
- Lazy format generation is verified — the DnD gallery sample
  advertises an expensive HTML payload and logs prove the provider
  fires only when the target actually requests HTML (and never when
  the drop is cancelled or routes to a text-only target).
- Microbenchmark: re-rendering a 1,000-item list with fresh pointer
  handlers completes in single-digit milliseconds vs today's hundreds.
- At least one original showcase app uses `OnPan`, one uses
  `OnDragStart`/`OnDrop`, one uses `OnDoubleTapped`, and one uses
  `OnLongPress` / `OnRightTapped`.

---

## Appendix A — Field-By-Field Coverage After Phase 1

For each WinUI routed event, the status after Phase 1:

| WinUI event | Today | Post-spec |
|---|---|---|
| SizeChanged | ✓ | ✓ |
| PointerPressed | ✓ | ✓ |
| PointerMoved | ✓ | ✓ |
| PointerReleased | ✓ | ✓ |
| PointerEntered | ✗ | ✓ |
| PointerExited | ✗ | ✓ |
| PointerCanceled | ✗ | ✓ |
| PointerCaptureLost | ✗ | ✓ |
| PointerWheelChanged | ✗ | ✓ |
| Tapped | ✓ | ✓ |
| DoubleTapped | ✗ | ✓ |
| RightTapped | ✗ | ✓ |
| Holding | ✗ | ✓ |
| KeyDown | ✓ | ✓ |
| KeyUp | ✗ | ✓ |
| PreviewKeyDown | ✗ | ✓ |
| PreviewKeyUp | ✗ | ✓ |
| CharacterReceived | ✗ | ✓ |
| GotFocus | ✗ | ✓ |
| LostFocus | ✗ | ✓ |
| ManipulationStarting | ✗ | internal (drives Tier 3) |
| ManipulationStarted | ✗ | internal |
| ManipulationDelta | ✗ | internal |
| ManipulationCompleted | ✗ | internal |
| DragEnter | ✗ | ✓ (Tier 6) |
| DragOver | ✗ | ✓ (Tier 6) |
| DragLeave | ✗ | ✓ (Tier 6) |
| Drop | ✗ | ✓ (Tier 6) |
| DragStarting | ✗ | ✓ (Tier 6, source side) |
| DropCompleted | ✗ | ✓ (Tier 6, via onEnd callback) |
| AccessKey events | ✗ | ✓ (Tier 5) |

### Ship status (2026-04 snapshot)

All "Post-spec ✓" rows in the table above have shipped as of this snapshot:

- Pointer / tap / keyboard / focus routed events (Tier 1) — `ElementModifiers`
  plus matching `.On*` extension methods in `ElementExtensions.cs`.
- Manipulation pipeline (Tier 3) — driven by `Reconciler.Gestures.cs`; exposed
  via `.OnPan` / `.OnPinch` / `.OnRotate` / `.OnLongPress` / `.OnDoubleTap`.
- DnD surface (Tier 6) — `DragData`, `DragSourceConfig`, `DropTargetConfig`,
  reconciler wiring in `Reconciler.DragDrop.cs`. Cross-process rich formats
  (6b) ship alongside the typed-payload fast path (6a) and `DropCompleted`
  finalization (6c). Remaining deferrals: custom `dragVisual` rendering via
  `RenderTargetBitmap` (6a.5) and the paired `WithBitmapFromElement` lazy
  provider (6b.1) — WinUI's default source-element screenshot covers the
  common case without Reactor involvement.
- Access-key events (Tier 5) — `.AccessKey` / `.AccessKeyDisplayRequested`
  modifiers, with per-site override beating `Command.AccessKey` by the
  modifier-after-command ordering rule.

E2E parity: `GestureTests` and `DragDropTests` exercise pan / double-tap /
right-tap / long-press and typed reorder / cancelled drag / text-format
round-trip end-to-end via Appium/WinAppDriver — all 7 green as of this
snapshot. Unit + selftest tiers already covered the declarative surface
before E2E landed. Two reconciler hardening notes shipped alongside the
E2E work: `.OnLongPress` now subscribes to `PointerPressed`/`Released`/
`CaptureLost`/`Moved` via `AddHandler(handledEventsToo: true)` so mouse
emulation still arms on Controls (like Button) that mark the press
handled for their own Click logic, and drop-target trampolines
(`DragEnter`/`DragOver`/`DragLeave`/`Drop`) do the same so `.OnDrop` fires
even when the target Control consumes the routed drag event internally.

Showcase adoption: `ReactorFiles` (file list + split panel grip) and
`regedit` (value list + split panel) have migrated off
`.Set(ctrl => ctrl.X += ...)` onto the Tier 1 declarative modifiers; the
outlook clone was deprecated and deleted.

---

## Appendix B — Why Not Copy Compose's `pointerInput`?

Compose wraps its entire gesture system in a single suspend-function
modifier: `Modifier.pointerInput(Unit) { detectDragGestures(...) }`. The
suspend-function machinery (coroutines, CoroutineScope, cancellation) is
how the state machine is expressed.

C# could approximate this with `async` methods, but the ergonomics
degrade:

- Every gesture block becomes an `async Task` in the component, which
  isn't where async normally lives in React-style rendering.
- Cancellation is manual (`CancellationToken` threading vs. Kotlin's
  structured concurrency).
- There's no equivalent to Kotlin's `awaitPointerEvent()` primitive —
  it'd need a whole channel-based pump.

The per-modifier approach (`.OnPan`, `.OnPinch`, etc.) fits Reactor's
existing idiom and doesn't introduce a new concurrency model. The loss
is gesture composition operators — which, as noted in "Out of scope,"
are deferred.

---

## Appendix C — Handler Allocation Analysis

Even with trampolines, every render still allocates closures for each
handler the user writes:

```csharp
.OnPan(
    onChanged: g => setOffset(g.Translation),    // allocates on each render
    onEnded:   _ => setOffset(Vector2.Zero));    // allocates on each render
```

The allocation happens regardless of the dispatch path. For event-heavy
lists, this can still contribute GC pressure. Two potential mitigations,
neither in scope for this spec:

- **`UseCallback` adoption guidance.** Document a pattern for memoizing
  handlers when allocation is a measured concern.
- **Source generator.** Auto-wrap `Render()` lambdas into per-component
  stable delegates. Large effort, speculative benefit.

Neither is proposed now; flagging for a future perf pass if allocation
shows up in profiling after Tier 2 ships.
