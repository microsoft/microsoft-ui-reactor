# Transitions

Enter and exit transitions for elements.

**Learn more:** [Animation](../../animation.md)

## `AsymmetricTransition`

```csharp
public sealed record AsymmetricTransition : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors

#### `AsymmetricTransition`

```csharp
public AsymmetricTransition(Transition enter, Transition exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition`](#transition_1) |  |
| `exit` | [`Transition`](#transition_1) |  |

### Properties

#### `EnterTransition`

```csharp
public Transition EnterTransition  // get, init
```

#### `ExitTransition`

```csharp
public Transition ExitTransition  // get, init
```

## `CombinedTransition`

```csharp
public sealed record CombinedTransition(Transition First, Transition Second) : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors

#### `CombinedTransition`

```csharp
public CombinedTransition(Transition First, Transition Second)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `First` | [`Transition`](#transition_1) |  |
| `Second` | [`Transition`](#transition_1) |  |

### Properties

#### `First`

```csharp
public Transition First  // get, init
```

#### `Second`

```csharp
public Transition Second  // get, init
```

## `DirectionalTransition`

```csharp
public sealed record DirectionalTransition : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors

#### `DirectionalTransition`

```csharp
public DirectionalTransition(Transition? enter, Transition? exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition?`](#transition_1) |  |
| `exit` | [`Transition?`](#transition_1) |  |

### Properties

#### `EnterTransition`

```csharp
public Transition? EnterTransition  // get, init
```

#### `ExitTransition`

```csharp
public Transition? ExitTransition  // get, init
```

## `Edge`

Edge enum for slide direction.

```csharp
public enum Edge
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Left` | `0` |  |
| `Top` | `1` |  |
| `Right` | `2` |  |
| `Bottom` | `3` |  |

## `ElementTransition`

Stores transition configuration on an Element. Includes the transition definition and an optional curve override (default: 300ms Decelerate).

```csharp
public record ElementTransition(Transition Transition, Curve? Curve = null)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `ElementTransition`

Stores transition configuration on an Element. Includes the transition definition and an optional curve override (default: 300ms Decelerate).

```csharp
public ElementTransition(Transition Transition, Curve? Curve = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Transition` | [`Transition`](#transition_1) |  |  |
| `Curve` | [`Curve?`](curves.md#curve) | `null` |  |

### Properties

#### `Curve`

```csharp
public Curve? Curve  // get, init
```

#### `Transition`

```csharp
public Transition Transition  // get, init
```

## `FadeTransition`

```csharp
public sealed record FadeTransition : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors

#### `FadeTransition`

```csharp
public FadeTransition()
```

## `ScaleTransition`

```csharp
public sealed record ScaleTransition(float From) : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors

#### `ScaleTransition`

```csharp
public ScaleTransition(float From)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `From` | `float` |  |

### Properties

#### `From`

```csharp
public float From  // get, init
```

## `SlideTransition`

```csharp
public sealed record SlideTransition(Edge Edge) : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors

#### `SlideTransition`

```csharp
public SlideTransition(Edge Edge)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Edge` | [`Edge`](#edge) |  |

### Properties

#### `Edge`

```csharp
public Edge Edge  // get, init
```

## `Transition`

Describes enter/exit transitions for conditional rendering. Transitions are composable: Fade + Slide(Bottom) plays both in parallel. Asymmetric: MakeEnter(Fade) | MakeExit(Scale) uses different animations for enter/exit.

```csharp
public abstract record Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `Transition`

```csharp
protected Transition()
```

### Methods

#### `Enter`

```csharp
public static Transition Enter(Transition enter)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition`](#transition_1) |  |

#### `Exit`

```csharp
public static Transition Exit(Transition exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `exit` | [`Transition`](#transition_1) |  |

#### `Scale`

```csharp
public static Transition Scale(float from = 0.85f)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `from` | `float` | `0.85f` |  |

#### `Slide`

```csharp
public static Transition Slide(Edge edge = Edge.Bottom)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `edge` | [`Edge`](#edge) | `Edge.Bottom` |  |

### Fields

#### `Fade`

```csharp
public static readonly Transition Fade
```

### Operators

#### `op_Addition`

Combine two transitions to play in parallel (e.g., Fade + Slide).

```csharp
public static Transition operator +(Transition a, Transition b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | [`Transition`](#transition_1) |  |
| `b` | [`Transition`](#transition_1) |  |

#### `op_BitwiseOr`

Asymmetric: left side is enter, right side is exit.

```csharp
public static Transition operator |(Transition enter, Transition exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition`](#transition_1) |  |
| `exit` | [`Transition`](#transition_1) |  |
