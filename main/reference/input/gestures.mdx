---
title: "Gestures"
description: "Gesture events and their options."
---

**Learn more:** [Input and gestures](/microsoft-ui-reactor/main/guides/input-and-gestures)

## `GesturePhase` [#gesturephase]

Phase of a continuous gesture (pan / pinch / rotate / long-press). The callback contract: [`GesturePhase.Began`](#values) fires exactly once at the start, [`GesturePhase.Changed`](#values) fires zero or more times as the gesture progresses, then either [`GesturePhase.Ended`](#values) (normal completion) or [`GesturePhase.Cancelled`](#values) (system aborted, pointer lost, escape key, …) fires exactly once.

```csharp
public enum GesturePhase
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Began` | `0` | Gesture has just become recognized and its parameters are stable. |
| `Changed` | `1` | Gesture is in progress; deltas are being reported. |
| `Ended` | `2` | Gesture completed normally. |
| `Cancelled` | `3` | Gesture was cancelled (pointer capture lost, window focus lost, …). |

## `LongPressGesture` [#longpressgesture]

Immutable snapshot of a long-press gesture. Fires [`GesturePhase.Began`](#values) the moment the press threshold is crossed, [`GesturePhase.Ended`](#values) when the pointer is released after that point, or [`GesturePhase.Cancelled`](#values) if the pointer moves beyond the cancel distance or capture is lost.

```csharp
public readonly record struct LongPressGesture(Point Position, TimeSpan Duration, GesturePhase Phase)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors]

#### `LongPressGesture` [#longpressgesture_1]

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

### Properties [#properties]

#### `Duration` [#duration]

Elapsed time since the initial press.

```csharp
public TimeSpan Duration  // get, init
```

#### `Phase` [#phase]

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

#### `Position` [#position]

Pointer position in element-local space at the moment of the event.

```csharp
public Point Position  // get, init
```

## `LongPressGestureConfig` [#longpressgestureconfig]

Immutable configuration for a `.OnLongPress` gesture recognizer. Touch/pen input routes through [`UIElement.Holding`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.holding). Mouse input is opt-in via [`LongPressGestureConfig.EnableMouseEmulation`](#enablemouseemulation); when enabled, the reconciler arms a dispatcher timer on [`UIElement.PointerPressed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointerpressed) and cancels it on release, capture loss, or pointer motion beyond [`LongPressGestureConfig.CancelDistance`](#canceldistance).

```csharp
public sealed record LongPressGestureConfig(Action<LongPressGesture> OnTriggered)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors_1]

#### `LongPressGestureConfig` [#longpressgestureconfig_1]

Immutable configuration for a `.OnLongPress` gesture recognizer. Touch/pen input routes through [`UIElement.Holding`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.holding). Mouse input is opt-in via [`LongPressGestureConfig.EnableMouseEmulation`](#enablemouseemulation); when enabled, the reconciler arms a dispatcher timer on [`UIElement.PointerPressed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointerpressed) and cancels it on release, capture loss, or pointer motion beyond [`LongPressGestureConfig.CancelDistance`](#canceldistance).

```csharp
public LongPressGestureConfig(Action<LongPressGesture> OnTriggered)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnTriggered` | `Action<LongPressGesture>` |  |

### Properties [#properties_1]

#### `CancelDistance` [#canceldistance]

Cumulative pointer motion (device pixels) that cancels an in-progress press.

```csharp
public double CancelDistance  // get, init
```

#### `EnableMouseEmulation` [#enablemouseemulation]

When true, mouse input arms a dispatcher timer in addition to touch/pen Holding.

```csharp
public bool EnableMouseEmulation  // get, init
```

#### `MinimumDuration` [#minimumduration]

Minimum press duration before the gesture triggers.

```csharp
public TimeSpan MinimumDuration  // get, init
```

#### `OnTriggered` [#ontriggered]

```csharp
public Action<LongPressGesture> OnTriggered  // get, init
```

## `PanAxis` [#panaxis]

Axis constraint for `.OnPan`. Controls which translation axes are tracked and which flag bits end up in the computed [`ManipulationModes`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.manipulationmodes).

```csharp
public enum PanAxis
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Both` | `0` | Both horizontal and vertical translation are tracked. |
| `Horizontal` | `1` | Only horizontal (X) translation is tracked. |
| `Vertical` | `2` | Only vertical (Y) translation is tracked. |

## `PanGesture` [#pangesture]

Immutable snapshot of a pan gesture update. All coordinates are in the element's local space.

```csharp
public readonly record struct PanGesture(Point Translation, Point Delta, Point Velocity, Point Position, Point StartPosition, GesturePhase Phase, bool IsInertial)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors_2]

#### `PanGesture` [#pangesture_1]

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

### Properties [#properties_2]

#### `Delta` [#delta]

Translation since the last [`GesturePhase.Changed`](#values) callback (zero on [`GesturePhase.Began`](#values)).

```csharp
public Point Delta  // get, init
```

#### `IsInertial` [#isinertial]

True once WinUI is extrapolating inertia (after the user lifts their finger).

```csharp
public bool IsInertial  // get, init
```

#### `Phase` [#phase_1]

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

#### `Position` [#position_1]

Current pointer position in element-local space.

```csharp
public Point Position  // get, init
```

#### `StartPosition` [#startposition]

Pointer position at [`GesturePhase.Began`](#values), in element-local space.

```csharp
public Point StartPosition  // get, init
```

#### `Translation` [#translation]

Total translation since [`GesturePhase.Began`](#values).

```csharp
public Point Translation  // get, init
```

#### `Velocity` [#velocity]

Instantaneous velocity in device-independent pixels per second.

```csharp
public Point Velocity  // get, init
```

## `PanGestureConfig` [#pangestureconfig]

Immutable configuration for a `.OnPan` gesture recognizer attached to an element. Stored on [`ElementModifiers.Pan`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers#pan); the reconciler wires [`UIElement.ManipulationStarted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationstarted) / [`UIElement.ManipulationDelta`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationdelta) / [`UIElement.ManipulationCompleted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationcompleted) trampolines and enforces [`PanGestureConfig.MinimumDistance`](#minimumdistance) gating before dispatching callbacks.

```csharp
public sealed record PanGestureConfig(Action<PanGesture> OnChanged)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors_3]

#### `PanGestureConfig` [#pangestureconfig_1]

Immutable configuration for a `.OnPan` gesture recognizer attached to an element. Stored on [`ElementModifiers.Pan`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers#pan); the reconciler wires [`UIElement.ManipulationStarted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationstarted) / [`UIElement.ManipulationDelta`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationdelta) / [`UIElement.ManipulationCompleted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.manipulationcompleted) trampolines and enforces [`PanGestureConfig.MinimumDistance`](#minimumdistance) gating before dispatching callbacks.

```csharp
public PanGestureConfig(Action<PanGesture> OnChanged)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnChanged` | `Action<PanGesture>` |  |

### Properties [#properties_3]

#### `Axis` [#axis]

Axis constraint for translation tracking.

```csharp
public PanAxis Axis  // get, init
```

#### `MinimumDistance` [#minimumdistance]

Cumulative translation (device-independent pixels) required before callbacks dispatch.

```csharp
public double MinimumDistance  // get, init
```

#### `OnBegan` [#onbegan]

Fires after [`PanGestureConfig.MinimumDistance`](#minimumdistance) is crossed, before the first Changed.

```csharp
public Action<PanGesture>? OnBegan  // get, init
```

#### `OnCancelled` [#oncancelled]

Fires when manipulation is cancelled after [`PanGestureConfig.OnBegan`](#onbegan) fired. If the threshold is never crossed, neither [`PanGestureConfig.OnBegan`](#onbegan) nor this fires.

```csharp
public Action<PanGesture>? OnCancelled  // get, init
```

#### `OnChanged` [#onchanged]

```csharp
public Action<PanGesture> OnChanged  // get, init
```

#### `OnEnded` [#onended]

Fires on normal manipulation completion (after [`PanGestureConfig.OnBegan`](#onbegan) fired).

```csharp
public Action<PanGesture>? OnEnded  // get, init
```

#### `WithInertia` [#withinertia]

When true, include translate-inertia flags so the gesture continues to report after the pointer lifts.

```csharp
public bool WithInertia  // get, init
```

## `PinchGesture` [#pinchgesture]

Immutable snapshot of a pinch (scale) gesture update.

```csharp
public readonly record struct PinchGesture(double Scale, double ScaleDelta, Point Center, GesturePhase Phase, bool IsInertial)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors_4]

#### `PinchGesture` [#pinchgesture_1]

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

### Properties [#properties_4]

#### `Center` [#center]

Pinch centroid in element-local space.

```csharp
public Point Center  // get, init
```

#### `IsInertial` [#isinertial_1]

True once inertia is being extrapolated.

```csharp
public bool IsInertial  // get, init
```

#### `Phase` [#phase_2]

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

#### `Scale` [#scale]

Cumulative scale factor relative to 1.0 at [`GesturePhase.Began`](#values).

```csharp
public double Scale  // get, init
```

#### `ScaleDelta` [#scaledelta]

Scale change since the last callback (ratio; 1.0 = no change).

```csharp
public double ScaleDelta  // get, init
```

## `PinchGestureConfig` [#pinchgestureconfig]

Immutable configuration for a `.OnPinch` gesture recognizer.

```csharp
public sealed record PinchGestureConfig(Action<PinchGesture> OnChanged)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors_5]

#### `PinchGestureConfig` [#pinchgestureconfig_1]

Immutable configuration for a `.OnPinch` gesture recognizer.

```csharp
public PinchGestureConfig(Action<PinchGesture> OnChanged)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnChanged` | `Action<PinchGesture>` |  |

### Properties [#properties_5]

#### `OnBegan` [#onbegan_1]

```csharp
public Action<PinchGesture>? OnBegan  // get, init
```

#### `OnCancelled` [#oncancelled_1]

```csharp
public Action<PinchGesture>? OnCancelled  // get, init
```

#### `OnChanged` [#onchanged_1]

```csharp
public Action<PinchGesture> OnChanged  // get, init
```

#### `OnEnded` [#onended_1]

```csharp
public Action<PinchGesture>? OnEnded  // get, init
```

#### `WithInertia` [#withinertia_1]

```csharp
public bool WithInertia  // get, init
```

## `RotateGesture` [#rotategesture]

Immutable snapshot of a rotate gesture update.

```csharp
public readonly record struct RotateGesture(double Angle, double AngleDelta, Point Center, GesturePhase Phase, bool IsInertial)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors_6]

#### `RotateGesture` [#rotategesture_1]

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

### Properties [#properties_6]

#### `Angle` [#angle]

Cumulative rotation in degrees since [`GesturePhase.Began`](#values).

```csharp
public double Angle  // get, init
```

#### `AngleDelta` [#angledelta]

Rotation since the last callback, in degrees.

```csharp
public double AngleDelta  // get, init
```

#### `Center` [#center_1]

Rotation pivot in element-local space.

```csharp
public Point Center  // get, init
```

#### `IsInertial` [#isinertial_2]

True once inertia is being extrapolated.

```csharp
public bool IsInertial  // get, init
```

#### `Phase` [#phase_3]

Current gesture phase.

```csharp
public GesturePhase Phase  // get, init
```

## `RotateGestureConfig` [#rotategestureconfig]

Immutable configuration for a `.OnRotate` gesture recognizer.

```csharp
public sealed record RotateGestureConfig(Action<RotateGesture> OnChanged)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors [#constructors_7]

#### `RotateGestureConfig` [#rotategestureconfig_1]

Immutable configuration for a `.OnRotate` gesture recognizer.

```csharp
public RotateGestureConfig(Action<RotateGesture> OnChanged)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `OnChanged` | `Action<RotateGesture>` |  |

### Properties [#properties_7]

#### `OnBegan` [#onbegan_2]

```csharp
public Action<RotateGesture>? OnBegan  // get, init
```

#### `OnCancelled` [#oncancelled_2]

```csharp
public Action<RotateGesture>? OnCancelled  // get, init
```

#### `OnChanged` [#onchanged_2]

```csharp
public Action<RotateGesture> OnChanged  // get, init
```

#### `OnEnded` [#onended_2]

```csharp
public Action<RotateGesture>? OnEnded  // get, init
```

#### `WithInertia` [#withinertia_2]

```csharp
public bool WithInertia  // get, init
```
