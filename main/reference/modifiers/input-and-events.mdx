---
title: "Modifiers: Input and events"
description: "Pointer, keyboard, gesture and drag and drop events, and whether an element takes input."
sidebar:
  label: "Input and events"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Input and gestures](/microsoft-ui-reactor/main/guides/input-and-gestures)

## `Disabled` [#disabled]

Sets Control.IsEnabled.

```csharp
T Disabled<T>(this T el, bool disabled = true) where T : Element
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsEnabled(false) — aligns with WinUI naming (see #268).

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `disabled` | `bool` | `true` |  |

## `Drag` [#drag]

Opts a subtree in or out of window drag-from-background hit testing. Use `.Drag(false)` on custom interactive regions that should not start a window move.

```csharp
T Drag<T>(this T el, bool enabled) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enabled` | `bool` |  |

## `DraggableWhen` [#draggablewhen]

Gates an attached `.OnDragStart` — when `canDrag` returns false, the drag is cancelled in `DragStarting` before any UI feedback appears. Merge with an existing [`DragSourceConfig`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#dragsourceconfig) so previously-set allowed ops / onEnd are preserved.

```csharp
T DraggableWhen<T>(this T el, Func<bool> canDrag) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `canDrag` | `Func<bool>` |  |

## `IsDragRegion` [#isdragregion]

Marks how this element participates in a window title bar's drag region (`Microsoft.UI.Xaml.Controls.TitleBar.IsDragRegion`, WinApp SDK ≥ 2.1.3): `true` = draggable, `false` = clickable, `null` = defer to the title bar (interactive controls are auto-excluded from the drag region). Only meaningful for an element inside a `TitleBar`'s content; inert elsewhere. The nullable parameter lets callers forward a `bool?` state without branching.

```csharp
T IsDragRegion<T>(this T el, bool? isDragRegion = true) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isDragRegion` | `bool?` | `true` |  |

**Example**

```csharp
Button(Icon(FontIcon("\uE713", fontSize: 16)), OnSettings)
    .AutomationName("Settings").IsDragRegion(false)
```

## `IsEnabled` [#isenabled]

Sets Control.IsEnabled.

```csharp
T IsEnabled<T>(this T el, bool enabled = true) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsHitTestVisible` [#ishittestvisible]

Sets UIElement.IsHitTestVisible — whether the element (and its subtree) can be the target of pointer input. Set `false` for decorative or overlay layers that should let pointer events pass through to whatever is underneath. Works on any element type (Panel, Control, etc.) in WinUI 3.

```csharp
T IsHitTestVisible<T>(this T el, bool isHitTestVisible = true) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isHitTestVisible` | `bool` | `true` |  |

**Example**

```csharp
Border(content).IsHitTestVisible(false)
```

## `OnCharacterReceived` [#oncharacterreceived]

```csharp
T OnCharacterReceived<T>(this T el, Action<UIElement, CharacterReceivedRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<UIElement, CharacterReceivedRoutedEventArgs>` |  |

## `OnDoubleTap` [#ondoubletap]

```csharp
T OnDoubleTap<T>(this T el, Action handler) where T : Element
T OnDoubleTap<T>(this T el, Action<Point> handler) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `OnDoubleTap<T>(T, Action)` | Zero-argument convenience overload for double-tap. Equivalent to `.OnDoubleTapped((_, _) => handler())`. |
| `OnDoubleTap<T>(T, Action<Point>)` | Position-aware convenience overload for double-tap. Hands back the tap position in element-local space. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action` or `Action<Point>` |  |

## `OnDoubleTapped` [#ondoubletapped]

```csharp
T OnDoubleTapped<T>(this T el, Action<object, DoubleTappedRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, DoubleTappedRoutedEventArgs>` |  |

## `OnDragEnter` [#ondragenter]

DragEnter callback — caller updates [`DragTargetArgs.UIOverride`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#uioverride) to customize the drop indicator, or sets [`DragTargetArgs.AcceptedOperation`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#acceptedoperation) to override default negotiation.

```csharp
T OnDragEnter<T>(this T el, Action<DragTargetArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DragTargetArgs>` |  |

## `OnDragLeave` [#ondragleave]

DragLeave callback — fires when the drag exits the target without dropping.

```csharp
T OnDragLeave<T>(this T el, Action<DragTargetArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DragTargetArgs>` |  |

## `OnDragOver` [#ondragover]

DragOver callback — fires repeatedly as the pointer moves. Use for hover highlighting that depends on position within the target.

```csharp
T OnDragOver<T>(this T el, Action<DragTargetArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DragTargetArgs>` |  |

## `OnDragStart` [#ondragstart]

```csharp
T OnDragStart<T>(this T el, Func<DragData> getData, DragOperations? allowedOperations = null, Action<DragEndContext>? onEnd = null) where T : Element
T OnDragStart<T, TPayload>(this T el, Func<TPayload> getPayload, DragOperations? allowedOperations = null, Action<DragEndContext>? onEnd = null) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `OnDragStart<T>(T, Func<DragData>, DragOperations?, Action<DragEndContext>?)` | Raw drag source — the caller builds the [`DragData`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#dragdata) directly. Useful when advertising multiple formats at once (Phase 6b) or attaching additional metadata. |
| `OnDragStart<T, TPayload>(T, Func<TPayload>, DragOperations?, Action<DragEndContext>?)` | Typed drag source. Auto-sets [`UIElement.CanDrag`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.candrag) so the element reports as draggable. `getPayload` is called each time a drag starts; the returned value is wrapped in a typed-payload [`DragData`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#dragdata) keyed by `TPayload`. Use `allowedOperations` to declare which final operations (Copy/Move/Link) the source will accept. `onEnd` fires after `DropCompleted` with the final negotiated operation (or [`DragOperations.None`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#values) on cancel). |

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `getData` | `Func<DragData>` |  |  |
| `allowedOperations` | `DragOperations?` | `null` |  |
| `onEnd` | `Action<DragEndContext>?` | `null` |  |
| `getPayload` | `Func<TPayload>` |  |  |

## `OnDrop` [#ondrop]

```csharp
T OnDrop<T>(this T el, Action<DragTargetArgs> onDrop, DragOperations acceptedOps = DragOperations.All) where T : Element
T OnDrop<T, TPayload>(this T el, Action<TPayload> onDrop, DragOperations acceptedOps = DragOperations.All) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `OnDrop<T>(T, Action<DragTargetArgs>, DragOperations)` | Raw drop handler — receives the full [`DragTargetArgs`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#dragtargetargs) so multi-format targets can inspect available formats and accept operation manually. |
| `OnDrop<T, TPayload>(T, Action<TPayload>, DragOperations)` | Typed drop target. Auto-sets [`UIElement.AllowDrop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.allowdrop). The handler is invoked when a drag with a matching typed payload is dropped on this element; the accepted operation is set to the intersection of `acceptedOps` and the source's allowed operations (preferring Move \> Copy \> Link). |

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onDrop` | `Action<DragTargetArgs>` or `Action<TPayload>` |  |  |
| `acceptedOps` | [`DragOperations`](/microsoft-ui-reactor/main/reference/input/drag-and-drop#dragoperations) | `DragOperations.All` |  |

## `OnGotFocus` [#ongotfocus]

```csharp
T OnGotFocus<T>(this T el, Action<object, RoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, RoutedEventArgs>` |  |

## `OnHolding` [#onholding]

```csharp
T OnHolding<T>(this T el, Action<object, HoldingRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, HoldingRoutedEventArgs>` |  |

## `OnKeyDown` [#onkeydown]

```csharp
T OnKeyDown<T>(this T el, Action<object, KeyRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, KeyRoutedEventArgs>` |  |

## `OnKeyUp` [#onkeyup]

```csharp
T OnKeyUp<T>(this T el, Action<object, KeyRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, KeyRoutedEventArgs>` |  |

## `OnLongPress` [#onlongpress]

```csharp
T OnLongPress<T>(this T el, Action onTriggered, TimeSpan? minimumDuration = null, double cancelDistance = 10, bool enableMouseEmulation = false) where T : Element
T OnLongPress<T>(this T el, Action<LongPressGesture> onTriggered, TimeSpan? minimumDuration = null, double cancelDistance = 10, bool enableMouseEmulation = false) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `OnLongPress<T>(T, Action, TimeSpan?, double, bool)` | Zero-argument convenience overload for long-press. Use when you don't need the gesture snapshot (Position, Duration, Phase). Fires exactly ONCE per recognized long-press, at the moment it triggers ([`GesturePhase.Began`](/microsoft-ui-reactor/main/reference/input/gestures#values)) — not again on release ([`GesturePhase.Ended`](/microsoft-ui-reactor/main/reference/input/gestures#values)) or cancel. The underlying `OnTriggered` contract fires Began then Ended/Cancelled; a phase-agnostic convenience action must filter to Began or it double-fires. (This was previously masked when a per-render capturing action went stale across a skipped render — both calls became idempotent; the skip-path closure refresh in #721 exposed it, so the filter makes the single-fire contract explicit.) |
| `OnLongPress<T>(T, Action<LongPressGesture>, TimeSpan?, double, bool)` | Attaches a long-press gesture recognizer (spec 027 Tier 3 Part 2). Touch and pen route through [`UIElement.Holding`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.holding) (`IsHoldingEnabled = true` is auto-set). Mouse input is ignored by default — WinUI's `Holding` event does not raise for mouse pointers. Pass `enableMouseEmulation` `true` to arm a dispatcher timer on [`UIElement.PointerPressed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointerpressed) that fires after `minimumDuration` and cancels on motion \> `cancelDistance`, pointer release, or capture loss. |

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onTriggered` | `Action` or `Action<LongPressGesture>` |  |  |
| `minimumDuration` | `TimeSpan?` | `null` |  |
| `cancelDistance` | `double` | `10` |  |
| `enableMouseEmulation` | `bool` | `false` |  |

**Example**

```csharp
card.OnLongPress(g => ShowContextMenu(g.Position))
```

## `OnLostFocus` [#onlostfocus]

```csharp
T OnLostFocus<T>(this T el, Action<object, RoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, RoutedEventArgs>` |  |

## `OnPan` [#onpan]

Attaches a pan (single-finger translation) gesture recognizer. The reconciler wires [`UIElement.ManipulationDelta`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationdelta) and computes [`UIElement.ManipulationMode`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationmode) based on the chosen `axis` and `withInertia` flag. `minimumDistance` gates callbacks until the cumulative translation exceeds that distance — on first crossing, `onBegan` fires once with [`GesturePhase.Began`](/microsoft-ui-reactor/main/reference/input/gestures#values), then a [`GesturePhase.Changed`](/microsoft-ui-reactor/main/reference/input/gestures#values) follows.

```csharp
T OnPan<T>(this T el, Action<PanGesture> onChanged, Action<PanGesture>? onEnded = null, Action<PanGesture>? onBegan = null, Action<PanGesture>? onCancelled = null, double minimumDistance = 0, PanAxis axis = PanAxis.Both, bool withInertia = false) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onChanged` | `Action<PanGesture>` |  |  |
| `onEnded` | `Action<PanGesture>?` | `null` |  |
| `onBegan` | `Action<PanGesture>?` | `null` |  |
| `onCancelled` | `Action<PanGesture>?` | `null` |  |
| `minimumDistance` | `double` | `0` |  |
| `axis` | [`PanAxis`](/microsoft-ui-reactor/main/reference/input/gestures#panaxis) | `PanAxis.Both` |  |
| `withInertia` | `bool` | `false` |  |

## `OnPinch` [#onpinch]

Attaches a pinch (two-finger scale) gesture recognizer.

```csharp
T OnPinch<T>(this T el, Action<PinchGesture> onChanged, Action<PinchGesture>? onEnded = null, Action<PinchGesture>? onBegan = null, Action<PinchGesture>? onCancelled = null, bool withInertia = false) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onChanged` | `Action<PinchGesture>` |  |  |
| `onEnded` | `Action<PinchGesture>?` | `null` |  |
| `onBegan` | `Action<PinchGesture>?` | `null` |  |
| `onCancelled` | `Action<PinchGesture>?` | `null` |  |
| `withInertia` | `bool` | `false` |  |

## `OnPointerCanceled` [#onpointercanceled]

```csharp
T OnPointerCanceled<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPointerCaptureLost` [#onpointercapturelost]

```csharp
T OnPointerCaptureLost<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPointerEntered` [#onpointerentered]

```csharp
T OnPointerEntered<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPointerExited` [#onpointerexited]

```csharp
T OnPointerExited<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPointerMoved` [#onpointermoved]

```csharp
T OnPointerMoved<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPointerPressed` [#onpointerpressed]

```csharp
T OnPointerPressed<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPointerReleased` [#onpointerreleased]

```csharp
T OnPointerReleased<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPointerWheelChanged` [#onpointerwheelchanged]

```csharp
T OnPointerWheelChanged<T>(this T el, Action<object, PointerRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, PointerRoutedEventArgs>` |  |

## `OnPreviewKeyDown` [#onpreviewkeydown]

```csharp
T OnPreviewKeyDown<T>(this T el, Action<object, KeyRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, KeyRoutedEventArgs>` |  |

## `OnPreviewKeyUp` [#onpreviewkeyup]

```csharp
T OnPreviewKeyUp<T>(this T el, Action<object, KeyRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, KeyRoutedEventArgs>` |  |

## `OnRightTapped` [#onrighttapped]

```csharp
T OnRightTapped<T>(this T el, Action<object, RightTappedRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, RightTappedRoutedEventArgs>` |  |

## `OnRotate` [#onrotate]

Attaches a rotate (two-finger twist) gesture recognizer.

```csharp
T OnRotate<T>(this T el, Action<RotateGesture> onChanged, Action<RotateGesture>? onEnded = null, Action<RotateGesture>? onBegan = null, Action<RotateGesture>? onCancelled = null, bool withInertia = false) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onChanged` | `Action<RotateGesture>` |  |  |
| `onEnded` | `Action<RotateGesture>?` | `null` |  |
| `onBegan` | `Action<RotateGesture>?` | `null` |  |
| `onCancelled` | `Action<RotateGesture>?` | `null` |  |
| `withInertia` | `bool` | `false` |  |

## `OnTapped` [#ontapped]

```csharp
T OnTapped<T>(this T el, Action<object, TappedRoutedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, TappedRoutedEventArgs>` |  |
