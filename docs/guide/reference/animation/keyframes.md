# Keyframes

Types for keyframe animations.

**Learn more:** [Animation](../../animation.md)

## `KeyframeAnimationDef`

Immutable definition of a keyframe animation, built by the fluent builder.

```csharp
public record KeyframeAnimationDef
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `KeyframeAnimationDef`

```csharp
public KeyframeAnimationDef()
```

### Properties

#### `Duration`

```csharp
public TimeSpan Duration  // get, init
```

#### `Keyframes`

```csharp
public KeyframeDef[] Keyframes  // get, init
```

#### `Loop`

```csharp
public bool Loop  // get, init
```

## `KeyframeBuilder`

Fluent builder for constructing keyframe animation definitions.

```csharp
public class KeyframeBuilder
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `KeyframeBuilder`

```csharp
public KeyframeBuilder()
```

### Methods

#### `At`

Adds a keyframe at `progress` (0 to 1). `rotation` is in degrees, clockwise, like `.Rotation()`.

```csharp
public KeyframeBuilder At(float progress, float? opacity = null, Vector3? scale = null, Vector3? translation = null, float? rotation = null, Easing? easing = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `progress` | `float` |  |  |
| `opacity` | `float?` | `null` |  |
| `scale` | `Vector3?` | `null` |  |
| `translation` | `Vector3?` | `null` |  |
| `rotation` | `float?` | `null` |  |
| `easing` | `Easing?` | `null` |  |

#### `Build`

```csharp
public KeyframeAnimationDef Build()
```

#### `Duration`

```csharp
public KeyframeBuilder Duration(int ms)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ms` | `int` |  |

#### `Loop`

```csharp
public KeyframeBuilder Loop()
```

## `KeyframeDef`

A single keyframe in a keyframe animation.

```csharp
public record KeyframeDef(float Progress)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `KeyframeDef`

A single keyframe in a keyframe animation.

```csharp
public KeyframeDef(float Progress)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Progress` | `float` |  |

### Properties

#### `Easing`

```csharp
public Easing? Easing  // get, init
```

#### `Opacity`

```csharp
public float? Opacity  // get, init
```

#### `Progress`

```csharp
public float Progress  // get, init
```

#### `Rotation`

Rotation in degrees, clockwise, like `.Rotation()`.

```csharp
public float? Rotation  // get, init
```

#### `Scale`

```csharp
public Vector3? Scale  // get, init
```

#### `Translation`

```csharp
public Vector3? Translation  // get, init
```

## `KeyframeEntry`

A named keyframe animation entry stored on the Element.

```csharp
public record KeyframeEntry(string Name, object? Trigger, KeyframeAnimationDef Definition)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `KeyframeEntry`

A named keyframe animation entry stored on the Element.

```csharp
public KeyframeEntry(string Name, object? Trigger, KeyframeAnimationDef Definition)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` |  |
| `Trigger` | `object?` |  |
| `Definition` | [`KeyframeAnimationDef`](#keyframeanimationdef) |  |

### Properties

#### `Definition`

```csharp
public KeyframeAnimationDef Definition  // get, init
```

#### `Name`

```csharp
public string Name  // get, init
```

#### `Trigger`

```csharp
public object? Trigger  // get, init
```
