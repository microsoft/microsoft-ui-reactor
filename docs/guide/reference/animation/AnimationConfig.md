# AnimationConfig

Configuration for the .Animate() modifier — declaration-site implicit animation on compositor Visual properties. Stored on the Element base record.

```csharp
public record AnimationConfig(Curve Curve, AnimateProperty Properties = AnimateProperty.All)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

**Learn more:** [Animation](../../animation.md)

## Constructors

### `AnimationConfig`

Configuration for the .Animate() modifier — declaration-site implicit animation on compositor Visual properties. Stored on the Element base record.

```csharp
public AnimationConfig(Curve Curve, AnimateProperty Properties = AnimateProperty.All)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Curve` | [`Curve`](curves.md#curve) |  |  |
| `Properties` | [`AnimateProperty`](enums.md#animateproperty) | `AnimateProperty.All` |  |

## Properties

### `Curve`

```csharp
public Curve Curve  // get, init
```

### `Properties`

```csharp
public AnimateProperty Properties  // get, init
```
