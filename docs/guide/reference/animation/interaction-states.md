# Interaction states

Animations that follow pointer over, pressed and focus states.

**Learn more:** [Animation](../../animation.md)

## `InteractionStateValues`

Values for a single interaction state (PointerOver, Pressed, Focused). Only visual/brush properties — no layout properties (Width, Margin, etc.). The record IS the boundary: adding a field is a deliberate API decision.

```csharp
public record InteractionStateValues(float? Opacity = null, float? Scale = null, Vector3? ScaleV = null, Vector3? Translation = null, float? Rotation = null, Brush? Background = null, Brush? Foreground = null, Brush? BorderBrush = null)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `InteractionStateValues`

Values for a single interaction state (PointerOver, Pressed, Focused). Only visual/brush properties — no layout properties (Width, Margin, etc.). The record IS the boundary: adding a field is a deliberate API decision.

```csharp
public InteractionStateValues(float? Opacity = null, float? Scale = null, Vector3? ScaleV = null, Vector3? Translation = null, float? Rotation = null, Brush? Background = null, Brush? Foreground = null, Brush? BorderBrush = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Opacity` | `float?` | `null` |  |
| `Scale` | `float?` | `null` |  |
| `ScaleV` | `Vector3?` | `null` |  |
| `Translation` | `Vector3?` | `null` |  |
| `Rotation` | `float?` | `null` |  |
| `Background` | `Brush?` | `null` |  |
| `Foreground` | `Brush?` | `null` |  |
| `BorderBrush` | `Brush?` | `null` |  |

### Properties

#### `Background`

```csharp
public Brush? Background  // get, init
```

#### `BorderBrush`

```csharp
public Brush? BorderBrush  // get, init
```

#### `Foreground`

```csharp
public Brush? Foreground  // get, init
```

#### `Opacity`

```csharp
public float? Opacity  // get, init
```

#### `Rotation`

```csharp
public float? Rotation  // get, init
```

#### `Scale`

```csharp
public float? Scale  // get, init
```

#### `ScaleV`

```csharp
public Vector3? ScaleV  // get, init
```

#### `Translation`

```csharp
public Vector3? Translation  // get, init
```

## `InteractionStatesBuilder`

Builder for constructing InteractionStatesConfig fluently.

```csharp
public class InteractionStatesBuilder
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `InteractionStatesBuilder`

```csharp
public InteractionStatesBuilder()
```

### Methods

#### `Focused`

```csharp
public InteractionStatesBuilder Focused(float? opacity = null, float? scale = null, Vector3? scaleV = null, Vector3? translation = null, float? rotation = null, Brush? background = null, Brush? foreground = null, Brush? borderBrush = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `opacity` | `float?` | `null` |  |
| `scale` | `float?` | `null` |  |
| `scaleV` | `Vector3?` | `null` |  |
| `translation` | `Vector3?` | `null` |  |
| `rotation` | `float?` | `null` |  |
| `background` | `Brush?` | `null` |  |
| `foreground` | `Brush?` | `null` |  |
| `borderBrush` | `Brush?` | `null` |  |

#### `PointerOver`

```csharp
public InteractionStatesBuilder PointerOver(float? opacity = null, float? scale = null, Vector3? scaleV = null, Vector3? translation = null, float? rotation = null, Brush? background = null, Brush? foreground = null, Brush? borderBrush = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `opacity` | `float?` | `null` |  |
| `scale` | `float?` | `null` |  |
| `scaleV` | `Vector3?` | `null` |  |
| `translation` | `Vector3?` | `null` |  |
| `rotation` | `float?` | `null` |  |
| `background` | `Brush?` | `null` |  |
| `foreground` | `Brush?` | `null` |  |
| `borderBrush` | `Brush?` | `null` |  |

#### `Pressed`

```csharp
public InteractionStatesBuilder Pressed(float? opacity = null, float? scale = null, Vector3? scaleV = null, Vector3? translation = null, float? rotation = null, Brush? background = null, Brush? foreground = null, Brush? borderBrush = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `opacity` | `float?` | `null` |  |
| `scale` | `float?` | `null` |  |
| `scaleV` | `Vector3?` | `null` |  |
| `translation` | `Vector3?` | `null` |  |
| `rotation` | `float?` | `null` |  |
| `background` | `Brush?` | `null` |  |
| `foreground` | `Brush?` | `null` |  |
| `borderBrush` | `Brush?` | `null` |  |

## `InteractionStatesConfig`

Configuration for the .InteractionStates() modifier. Defines visual state values for PointerOver, Pressed, and Focused states, plus an optional animation curve for state transitions.

```csharp
public record InteractionStatesConfig(InteractionStateValues? PointerOver = null, InteractionStateValues? Pressed = null, InteractionStateValues? Focused = null, Curve? Curve = null)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `InteractionStatesConfig`

Configuration for the .InteractionStates() modifier. Defines visual state values for PointerOver, Pressed, and Focused states, plus an optional animation curve for state transitions.

```csharp
public InteractionStatesConfig(InteractionStateValues? PointerOver = null, InteractionStateValues? Pressed = null, InteractionStateValues? Focused = null, Curve? Curve = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `PointerOver` | [`InteractionStateValues?`](#interactionstatevalues) | `null` |  |
| `Pressed` | [`InteractionStateValues?`](#interactionstatevalues) | `null` |  |
| `Focused` | [`InteractionStateValues?`](#interactionstatevalues) | `null` |  |
| `Curve` | [`Curve?`](curves.md#curve) | `null` |  |

### Properties

#### `Curve`

```csharp
public Curve? Curve  // get, init
```

#### `Focused`

```csharp
public InteractionStateValues? Focused  // get, init
```

#### `PointerOver`

```csharp
public InteractionStateValues? PointerOver  // get, init
```

#### `Pressed`

```csharp
public InteractionStateValues? Pressed  // get, init
```
