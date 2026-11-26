# Gestures

Gesture events and their options.

**Learn more:** [Input and gestures](../../input-and-gestures.md)

## `GesturePhase`

Phase of a continuous gesture (pan / pinch / rotate / long-press). The callback contract: [`GesturePhase.Began`](#values) fires exactly once at the start, [`GesturePhase.Changed`](#values) fires zero or more times as the gesture progresses, then either [`GesturePhase.Ended`](#values) (normal completion) or [`GesturePhase.Cancelled`](#values) (system aborted, pointer lost, escape key, …) fires exactly once.

```csharp
public enum GesturePhase
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Began` | `0` | Gesture has just become recognized and its parameters are stable. |
| `Changed` | `1` | Gesture is in progress; deltas are being reported. |
| `Ended` | `2` | Gesture completed normally. |
| `Cancelled` | `3` | Gesture was cancelled (pointer capture lost, window focus lost, …). |

## `LongPressGesture`

Immutable snapshot of a long-press gesture. Fires [`GesturePhase.Began`](#values) the moment the press threshold is crossed, [`GesturePhase.Ended`](#values) when the pointer is released after that point, or [`GesturePhase.Cancelled`](#values) if the pointer moves beyond the cancel distance or capture is lost.

```csharp
public readonly record struct LongPressGesture(Point Position, TimeSpan Duration, GesturePhase Phase)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `LongPressGesture`

Immutable snapshot of a long-press gesture. Fires [`GesturePhase.Began`](#values) the moment the press threshold is crossed, [`GesturePhase.Ended`](#values) when the pointer is released after that point, or [`GesturePhase.Cancelled`](#values) if the pointer moves beyond the cancel distance or capture is lost.

```csharp
public LongPressGesture(Point Position, TimeSpan Duration, GesturePhase Phase)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Position` | `Point` | Pointer position in element-local space at the moment of the event. |
| `Duration` | `TimeSpan` | Elapsed time since the initial press. |
| `Phase` | [`GesturePhase`](#gesturephase) | Current gesture phase. |

### Properties

#### `Duration`

Elapsed time since the initial press.

```csharp
public TimeSpan Duration  // get, init
```

#### `Phase`

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

#### `Position`

Pointer position in element-local space at the moment of the event.

```csharp
public Point Position  // get, init
```

## `LongPressGestureConfig`

Immutable configuration for a `.OnLongPress` gesture recognizer. Touch/pen input routes through [`UIElement.Holding`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.holding). Mouse input is opt-in via [`LongPressGestureConfig.EnableMouseEmulation`](#enablemouseemulation); when enabled, the reconciler arms a dispatcher timer on [`UIElement.PointerPressed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointerpressed) and cancels it on release, capture loss, or pointer motion beyond [`LongPressGestureConfig.CancelDistance`](#canceldistance).

```csharp
public sealed record LongPressGestureConfig(Action<LongPressGesture> OnTriggered)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `LongPressGestureConfig`

Immutable configuration for a `.OnLongPress` gesture recognizer. Touch/pen input routes through [`UIElement.Holding`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.holding). Mouse input is opt-in via [`LongPressGestureConfig.EnableMouseEmulation`](#enablemouseemulation); when enabled, the reconciler arms a dispatcher timer on [`UIElement.PointerPressed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointerpressed) and cancels it on release, capture loss, or pointer motion beyond [`LongPressGestureConfig.CancelDistance`](#canceldistance).

```csharp
public LongPressGestureConfig(Action<LongPressGesture> OnTriggered)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnTriggered` | `Action<LongPressGesture>` |  |

### Properties

#### `CancelDistance`

Cumulative pointer motion (device pixels) that cancels an in-progress press.

```csharp
public double CancelDistance  // get, init
```

#### `EnableMouseEmulation`

When true, mouse input arms a dispatcher timer in addition to touch/pen Holding.

```csharp
public bool EnableMouseEmulation  // get, init
```

#### `MinimumDuration`

Minimum press duration before the gesture triggers.

```csharp
public TimeSpan MinimumDuration  // get, init
```

#### `OnTriggered`

```csharp
public Action<LongPressGesture> OnTriggered  // get, init
```

## `PanAxis`

Axis constraint for `.OnPan`. Controls which translation axes are tracked and which flag bits end up in the computed [`ManipulationModes`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.manipulationmodes).

```csharp
public enum PanAxis
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Both` | `0` | Both horizontal and vertical translation are tracked. |
| `Horizontal` | `1` | Only horizontal (X) translation is tracked. |
| `Vertical` | `2` | Only vertical (Y) translation is tracked. |

## `PanGesture`

Immutable snapshot of a pan gesture update. All coordinates are in the element's local space.

```csharp
public readonly record struct PanGesture(Point Translation, Point Delta, Point Velocity, Point Position, Point StartPosition, GesturePhase Phase, bool IsInertial)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `PanGesture`

Immutable snapshot of a pan gesture update. All coordinates are in the element's local space.

```csharp
public PanGesture(Point Translation, Point Delta, Point Velocity, Point Position, Point StartPosition, GesturePhase Phase, bool IsInertial)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Translation` | `Point` | Total translation since [`GesturePhase.Began`](#values). |
| `Delta` | `Point` | Translation since the last [`GesturePhase.Changed`](#values) callback (zero on [`GesturePhase.Began`](#values)). |
| `Velocity` | `Point` | Instantaneous velocity in device-independent pixels per second. |
| `Position` | `Point` | Current pointer position in element-local space. |
| `StartPosition` | `Point` | Pointer position at [`GesturePhase.Began`](#values), in element-local space. |
| `Phase` | [`GesturePhase`](#gesturephase) | Current gesture phase. |
| `IsInertial` | `bool` | True once WinUI is extrapolating inertia (after the user lifts their finger). |

### Properties

#### `Delta`

Translation since the last [`GesturePhase.Changed`](#values) callback (zero on [`GesturePhase.Began`](#values)).

```csharp
public Point Delta  // get, init
```

#### `IsInertial`

True once WinUI is extrapolating inertia (after the user lifts their finger).

```csharp
public bool IsInertial  // get, init
```

#### `Phase`

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

#### `Position`

Current pointer position in element-local space.

```csharp
public Point Position  // get, init
```

#### `StartPosition`

Pointer position at [`GesturePhase.Began`](#values), in element-local space.

```csharp
public Point StartPosition  // get, init
```

#### `Translation`

Total translation since [`GesturePhase.Began`](#values).

```csharp
public Point Translation  // get, init
```

#### `Velocity`

Instantaneous velocity in device-independent pixels per second.

```csharp
public Point Velocity  // get, init
```

## `PanGestureConfig`

Immutable configuration for a `.OnPan` gesture recognizer attached to an element. Stored on [`ElementModifiers.Pan`](../modifiers/ElementModifiers.md#pan); the reconciler wires [`UIElement.ManipulationStarted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationstarted) / [`UIElement.ManipulationDelta`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationdelta) / [`UIElement.ManipulationCompleted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationcompleted) trampolines and enforces [`PanGestureConfig.MinimumDistance`](#minimumdistance) gating before dispatching callbacks.

```csharp
public sealed record PanGestureConfig(Action<PanGesture> OnChanged)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `PanGestureConfig`

Immutable configuration for a `.OnPan` gesture recognizer attached to an element. Stored on [`ElementModifiers.Pan`](../modifiers/ElementModifiers.md#pan); the reconciler wires [`UIElement.ManipulationStarted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationstarted) / [`UIElement.ManipulationDelta`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationdelta) / [`UIElement.ManipulationCompleted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationcompleted) trampolines and enforces [`PanGestureConfig.MinimumDistance`](#minimumdistance) gating before dispatching callbacks.

```csharp
public PanGestureConfig(Action<PanGesture> OnChanged)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnChanged` | `Action<PanGesture>` |  |

### Properties

#### `Axis`

Axis constraint for translation tracking.

```csharp
public PanAxis Axis  // get, init
```

#### `MinimumDistance`

Cumulative translation (device-independent pixels) required before callbacks dispatch.

```csharp
public double MinimumDistance  // get, init
```

#### `OnBegan`

Fires after [`PanGestureConfig.MinimumDistance`](#minimumdistance) is crossed, before the first Changed.

```csharp
public Action<PanGesture>? OnBegan  // get, init
```

#### `OnCancelled`

Fires when manipulation is cancelled after [`PanGestureConfig.OnBegan`](#onbegan) fired. If the threshold is never crossed, neither [`PanGestureConfig.OnBegan`](#onbegan) nor this fires.

```csharp
public Action<PanGesture>? OnCancelled  // get, init
```

#### `OnChanged`

```csharp
public Action<PanGesture> OnChanged  // get, init
```

#### `OnEnded`

Fires on normal manipulation completion (after [`PanGestureConfig.OnBegan`](#onbegan) fired).

```csharp
public Action<PanGesture>? OnEnded  // get, init
```

#### `WithInertia`

When true, include translate-inertia flags so the gesture continues to report after the pointer lifts.

```csharp
public bool WithInertia  // get, init
```

## `PinchGesture`

Immutable snapshot of a pinch (scale) gesture update.

```csharp
public readonly record struct PinchGesture(double Scale, double ScaleDelta, Point Center, GesturePhase Phase, bool IsInertial)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `PinchGesture`

Immutable snapshot of a pinch (scale) gesture update.

```csharp
public PinchGesture(double Scale, double ScaleDelta, Point Center, GesturePhase Phase, bool IsInertial)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Scale` | `double` | Cumulative scale factor relative to 1.0 at [`GesturePhase.Began`](#values). |
| `ScaleDelta` | `double` | Scale change since the last callback (ratio; 1.0 = no change). |
| `Center` | `Point` | Pinch centroid in element-local space. |
| `Phase` | [`GesturePhase`](#gesturephase) | Current gesture phase. |
| `IsInertial` | `bool` | True once inertia is being extrapolated. |

### Properties

#### `Center`

Pinch centroid in element-local space.

```csharp
public Point Center  // get, init
```

#### `IsInertial`

True once inertia is being extrapolated.

```csharp
public bool IsInertial  // get, init
```

#### `Phase`

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

#### `Scale`

Cumulative scale factor relative to 1.0 at [`GesturePhase.Began`](#values).

```csharp
public double Scale  // get, init
```

#### `ScaleDelta`

Scale change since the last callback (ratio; 1.0 = no change).

```csharp
public double ScaleDelta  // get, init
```

## `PinchGestureConfig`

Immutable configuration for a `.OnPinch` gesture recognizer.

```csharp
public sealed record PinchGestureConfig(Action<PinchGesture> OnChanged)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `PinchGestureConfig`

Immutable configuration for a `.OnPinch` gesture recognizer.

```csharp
public PinchGestureConfig(Action<PinchGesture> OnChanged)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnChanged` | `Action<PinchGesture>` |  |

### Properties

#### `OnBegan`

```csharp
public Action<PinchGesture>? OnBegan  // get, init
```

#### `OnCancelled`

```csharp
public Action<PinchGesture>? OnCancelled  // get, init
```

#### `OnChanged`

```csharp
public Action<PinchGesture> OnChanged  // get, init
```

#### `OnEnded`

```csharp
public Action<PinchGesture>? OnEnded  // get, init
```

#### `WithInertia`

```csharp
public bool WithInertia  // get, init
```

## `RotateGesture`

Immutable snapshot of a rotate gesture update.

```csharp
public readonly record struct RotateGesture(double Angle, double AngleDelta, Point Center, GesturePhase Phase, bool IsInertial)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `RotateGesture`

Immutable snapshot of a rotate gesture update.

```csharp
public RotateGesture(double Angle, double AngleDelta, Point Center, GesturePhase Phase, bool IsInertial)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Angle` | `double` | Cumulative rotation in degrees since [`GesturePhase.Began`](#values). |
| `AngleDelta` | `double` | Rotation since the last callback, in degrees. |
| `Center` | `Point` | Rotation pivot in element-local space. |
| `Phase` | [`GesturePhase`](#gesturephase) | Current gesture phase. |
| `IsInertial` | `bool` | True once inertia is being extrapolated. |

### Properties

#### `Angle`

Cumulative rotation in degrees since [`GesturePhase.Began`](#values).

```csharp
public double Angle  // get, init
```

#### `AngleDelta`

Rotation since the last callback, in degrees.

```csharp
public double AngleDelta  // get, init
```

#### `Center`

Rotation pivot in element-local space.

```csharp
public Point Center  // get, init
```

#### `IsInertial`

True once inertia is being extrapolated.

```csharp
public bool IsInertial  // get, init
```

#### `Phase`

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

## `RotateGestureConfig`

Immutable configuration for a `.OnRotate` gesture recognizer.

```csharp
public sealed record RotateGestureConfig(Action<RotateGesture> OnChanged)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `RotateGestureConfig`

Immutable configuration for a `.OnRotate` gesture recognizer.

```csharp
public RotateGestureConfig(Action<RotateGesture> OnChanged)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnChanged` | `Action<RotateGesture>` |  |

### Properties

#### `OnBegan`

```csharp
public Action<RotateGesture>? OnBegan  // get, init
```

#### `OnCancelled`

```csharp
public Action<RotateGesture>? OnCancelled  // get, init
```

#### `OnChanged`

```csharp
public Action<RotateGesture> OnChanged  // get, init
```

#### `OnEnded`

```csharp
public Action<RotateGesture>? OnEnded  // get, init
```

#### `WithInertia`

```csharp
public bool WithInertia  // get, init
```
