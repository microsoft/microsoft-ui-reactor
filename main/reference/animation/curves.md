---
title: "Curves and easing"
description: "Curves that shape the timing of an animation."
---

**Learn more:** [Animation](/microsoft-ui-reactor/main/guides/animation)

## `Curve` [#curve]

Describes the timing/physics of an animation. Immutable, shareable, zero-allocation. Maps to either a CompositionEasingFunction or a SpringAnimation at the compositor layer.

```csharp
public abstract record Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors [#constructors]

#### `Curve` [#curve_1]

```csharp
protected Curve()
```

### Methods [#methods]

#### `Ease` [#ease]

Cubic-bezier eased animation over a fixed duration.

```csharp
public static Curve Ease(int durationMs, Easing easing = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `durationMs` | `int` |  |  |
| `easing` | [`Easing`](#easing_1) | `default` |  |

#### `Linear` [#linear]

Constant-speed animation over a fixed duration.

```csharp
public static Curve Linear(int durationMs)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `durationMs` | `int` |  |

#### `Spring` [#spring]

Spring natural motion. DampingRatio 0=undamped, 1=critically damped.

```csharp
public static Curve Spring(float dampingRatio = 0.8f, float period = 0.05f)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `dampingRatio` | `float` | `0.8f` |  |
| `period` | `float` | `0.05f` |  |

## `EaseCurve` [#easecurve]

```csharp
public sealed record EaseCurve(TimeSpan Duration, Easing Easing) : Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Curve`](#curve).

### Constructors [#constructors_1]

#### `EaseCurve` [#easecurve_1]

```csharp
public EaseCurve(TimeSpan Duration, Easing Easing)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Duration` | `TimeSpan` |  |
| `Easing` | [`Easing`](#easing_1) |  |

### Properties [#properties]

#### `Duration` [#duration]

```csharp
public TimeSpan Duration  // get, init
```

#### `Easing` [#easing]

```csharp
public Easing Easing  // get, init
```

## `Easing` [#easing_1]

Cubic bezier easing definition. Presets match WinUI/Fluent Design motion guidelines.

```csharp
public readonly record struct Easing(float X1, float Y1, float X2, float Y2)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors [#constructors_2]

#### `Easing` [#easing_2]

Cubic bezier easing definition. Presets match WinUI/Fluent Design motion guidelines.

```csharp
public Easing(float X1, float Y1, float X2, float Y2)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `X1` | `float` |  |
| `Y1` | `float` |  |
| `X2` | `float` |  |
| `Y2` | `float` |  |

### Properties [#properties_1]

#### `X1` [#x1]

```csharp
public float X1  // get, init
```

#### `X2` [#x2]

```csharp
public float X2  // get, init
```

#### `Y1` [#y1]

```csharp
public float Y1  // get, init
```

#### `Y2` [#y2]

```csharp
public float Y2  // get, init
```

### Methods [#methods_1]

#### `CubicBezier` [#cubicbezier]

```csharp
public static Easing CubicBezier(float x1, float y1, float x2, float y2)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `float` |  |
| `y1` | `float` |  |
| `x2` | `float` |  |
| `y2` | `float` |  |

### Fields [#fields]

#### `Accelerate` [#accelerate]

```csharp
public static readonly Easing Accelerate
```

#### `Decelerate` [#decelerate]

```csharp
public static readonly Easing Decelerate
```

#### `EaseIn` [#easein]

```csharp
public static readonly Easing EaseIn
```

#### `EaseInOut` [#easeinout]

```csharp
public static readonly Easing EaseInOut
```

#### `EaseOut` [#easeout]

```csharp
public static readonly Easing EaseOut
```

#### `Linear` [#linear_1]

```csharp
public static readonly Easing Linear
```

#### `Standard` [#standard]

```csharp
public static readonly Easing Standard
```

## `LinearCurve` [#linearcurve]

```csharp
public sealed record LinearCurve(TimeSpan Duration) : Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Curve`](#curve).

### Constructors [#constructors_3]

#### `LinearCurve` [#linearcurve_1]

```csharp
public LinearCurve(TimeSpan Duration)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Duration` | `TimeSpan` |  |

### Properties [#properties_2]

#### `Duration` [#duration_1]

```csharp
public TimeSpan Duration  // get, init
```

## `SpringCurve` [#springcurve]

```csharp
public sealed record SpringCurve(float DampingRatio, float Period) : Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Curve`](#curve).

### Constructors [#constructors_4]

#### `SpringCurve` [#springcurve_1]

```csharp
public SpringCurve(float DampingRatio, float Period)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `DampingRatio` | `float` |  |
| `Period` | `float` |  |

### Properties [#properties_3]

#### `DampingRatio` [#dampingratio]

```csharp
public float DampingRatio  // get, init
```

#### `Period` [#period]

```csharp
public float Period  // get, init
```
