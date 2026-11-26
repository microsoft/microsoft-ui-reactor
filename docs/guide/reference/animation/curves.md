# Curves and easing

Curves that shape the timing of an animation.

**Learn more:** [Animation](../../animation.md)

## `Curve`

Describes the timing/physics of an animation. Immutable, shareable, zero-allocation. Maps to either a CompositionEasingFunction or a SpringAnimation at the compositor layer.

```csharp
public abstract record Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `Curve`

```csharp
protected Curve()
```

### Methods

#### `Ease`

Cubic-bezier eased animation over a fixed duration.

```csharp
public static Curve Ease(int durationMs, Easing easing = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `durationMs` | `int` |  |  |
| `easing` | [`Easing`](#easing_1) | `default` |  |

#### `Linear`

Constant-speed animation over a fixed duration.

```csharp
public static Curve Linear(int durationMs)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `durationMs` | `int` |  |

#### `Spring`

Spring natural motion. DampingRatio 0=undamped, 1=critically damped.

```csharp
public static Curve Spring(float dampingRatio = 0.8f, float period = 0.05f)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `dampingRatio` | `float` | `0.8f` |  |
| `period` | `float` | `0.05f` |  |

## `EaseCurve`

```csharp
public sealed record EaseCurve(TimeSpan Duration, Easing Easing) : Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Curve`](#curve).

### Constructors

#### `EaseCurve`

```csharp
public EaseCurve(TimeSpan Duration, Easing Easing)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Duration` | `TimeSpan` |  |
| `Easing` | [`Easing`](#easing_1) |  |

### Properties

#### `Duration`

```csharp
public TimeSpan Duration  // get, init
```

#### `Easing`

```csharp
public Easing Easing  // get, init
```

## `Easing`

Cubic bezier easing definition. Presets match WinUI/Fluent Design motion guidelines.

```csharp
public readonly record struct Easing(float X1, float Y1, float X2, float Y2)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `Easing`

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

### Properties

#### `X1`

```csharp
public float X1  // get, init
```

#### `X2`

```csharp
public float X2  // get, init
```

#### `Y1`

```csharp
public float Y1  // get, init
```

#### `Y2`

```csharp
public float Y2  // get, init
```

### Methods

#### `CubicBezier`

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

### Fields

#### `Accelerate`

```csharp
public static readonly Easing Accelerate
```

#### `Decelerate`

```csharp
public static readonly Easing Decelerate
```

#### `EaseIn`

```csharp
public static readonly Easing EaseIn
```

#### `EaseInOut`

```csharp
public static readonly Easing EaseInOut
```

#### `EaseOut`

```csharp
public static readonly Easing EaseOut
```

#### `Linear`

```csharp
public static readonly Easing Linear
```

#### `Standard`

```csharp
public static readonly Easing Standard
```

## `LinearCurve`

```csharp
public sealed record LinearCurve(TimeSpan Duration) : Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Curve`](#curve).

### Constructors

#### `LinearCurve`

```csharp
public LinearCurve(TimeSpan Duration)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Duration` | `TimeSpan` |  |

### Properties

#### `Duration`

```csharp
public TimeSpan Duration  // get, init
```

## `SpringCurve`

```csharp
public sealed record SpringCurve(float DampingRatio, float Period) : Curve
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Curve`](#curve).

### Constructors

#### `SpringCurve`

```csharp
public SpringCurve(float DampingRatio, float Period)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `DampingRatio` | `float` |  |
| `Period` | `float` |  |

### Properties

#### `DampingRatio`

```csharp
public float DampingRatio  // get, init
```

#### `Period`

```csharp
public float Period  // get, init
```
