---
title: "Transitions"
description: "Enter and exit transitions for elements."
---

**Learn more:** [Animation](/microsoft-ui-reactor/main/guides/animation)

## `AsymmetricTransition` [#asymmetrictransition]

```csharp
public sealed record AsymmetricTransition : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors [#constructors]

#### `AsymmetricTransition` [#asymmetrictransition_1]

```csharp
public AsymmetricTransition(Transition enter, Transition exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition`](#transition_1) |  |
| `exit` | [`Transition`](#transition_1) |  |

### Properties [#properties]

#### `EnterTransition` [#entertransition]

```csharp
public Transition EnterTransition  // get, init
```

#### `ExitTransition` [#exittransition]

```csharp
public Transition ExitTransition  // get, init
```

## `CombinedTransition` [#combinedtransition]

```csharp
public sealed record CombinedTransition(Transition First, Transition Second) : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors [#constructors_1]

#### `CombinedTransition` [#combinedtransition_1]

```csharp
public CombinedTransition(Transition First, Transition Second)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `First` | [`Transition`](#transition_1) |  |
| `Second` | [`Transition`](#transition_1) |  |

### Properties [#properties_1]

#### `First` [#first]

```csharp
public Transition First  // get, init
```

#### `Second` [#second]

```csharp
public Transition Second  // get, init
```

## `DirectionalTransition` [#directionaltransition]

```csharp
public sealed record DirectionalTransition : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors [#constructors_2]

#### `DirectionalTransition` [#directionaltransition_1]

```csharp
public DirectionalTransition(Transition? enter, Transition? exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition?`](#transition_1) |  |
| `exit` | [`Transition?`](#transition_1) |  |

### Properties [#properties_2]

#### `EnterTransition` [#entertransition_1]

```csharp
public Transition? EnterTransition  // get, init
```

#### `ExitTransition` [#exittransition_1]

```csharp
public Transition? ExitTransition  // get, init
```

## `Edge` [#edge]

Edge enum for slide direction.

```csharp
public enum Edge
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Left` | `0` |  |
| `Top` | `1` |  |
| `Right` | `2` |  |
| `Bottom` | `3` |  |

## `ElementTransition` [#elementtransition]

Stores transition configuration on an Element. Includes the transition definition and an optional curve override (default: 300ms Decelerate).

```csharp
public record ElementTransition(Transition Transition, Curve? Curve = null)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors [#constructors_3]

#### `ElementTransition` [#elementtransition_1]

Stores transition configuration on an Element. Includes the transition definition and an optional curve override (default: 300ms Decelerate).

```csharp
public ElementTransition(Transition Transition, Curve? Curve = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Transition` | [`Transition`](#transition_1) |  |  |
| `Curve` | [`Curve?`](/microsoft-ui-reactor/main/reference/animation/curves#curve) | `null` |  |

### Properties [#properties_3]

#### `Curve` [#curve]

```csharp
public Curve? Curve  // get, init
```

#### `Transition` [#transition]

```csharp
public Transition Transition  // get, init
```

## `FadeTransition` [#fadetransition]

```csharp
public sealed record FadeTransition : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors [#constructors_4]

#### `FadeTransition` [#fadetransition_1]

```csharp
public FadeTransition()
```

## `ScaleTransition` [#scaletransition]

```csharp
public sealed record ScaleTransition(float From) : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors [#constructors_5]

#### `ScaleTransition` [#scaletransition_1]

```csharp
public ScaleTransition(float From)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `From` | `float` |  |

### Properties [#properties_4]

#### `From` [#from]

```csharp
public float From  // get, init
```

## `SlideTransition` [#slidetransition]

```csharp
public sealed record SlideTransition(Edge Edge) : Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`. Inherits from [`Transition`](#transition_1).

### Constructors [#constructors_6]

#### `SlideTransition` [#slidetransition_1]

```csharp
public SlideTransition(Edge Edge)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Edge` | [`Edge`](#edge) |  |

### Properties [#properties_5]

#### `Edge` [#edge_1]

```csharp
public Edge Edge  // get, init
```

## `Transition` [#transition_1]

Describes enter/exit transitions for conditional rendering. Transitions are composable: Fade + Slide(Bottom) plays both in parallel. Asymmetric: MakeEnter(Fade) | MakeExit(Scale) uses different animations for enter/exit.

```csharp
public abstract record Transition
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors [#constructors_7]

#### `Transition` [#transition_2]

```csharp
protected Transition()
```

### Methods [#methods]

#### `Enter` [#enter]

```csharp
public static Transition Enter(Transition enter)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition`](#transition_1) |  |

#### `Exit` [#exit]

```csharp
public static Transition Exit(Transition exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `exit` | [`Transition`](#transition_1) |  |

#### `Scale` [#scale]

```csharp
public static Transition Scale(float from = 0.85f)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `from` | `float` | `0.85f` |  |

#### `Slide` [#slide]

```csharp
public static Transition Slide(Edge edge = Edge.Bottom)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `edge` | [`Edge`](#edge) | `Edge.Bottom` |  |

### Fields [#fields]

#### `Fade` [#fade]

```csharp
public static readonly Transition Fade
```

### Operators [#operators]

#### `op_Addition` [#op_addition]

Combine two transitions to play in parallel (e.g., Fade + Slide).

```csharp
public static Transition operator +(Transition a, Transition b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | [`Transition`](#transition_1) |  |
| `b` | [`Transition`](#transition_1) |  |

#### `op_BitwiseOr` [#op_bitwiseor]

Asymmetric: left side is enter, right side is exit.

```csharp
public static Transition operator |(Transition enter, Transition exit)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enter` | [`Transition`](#transition_1) |  |
| `exit` | [`Transition`](#transition_1) |  |
