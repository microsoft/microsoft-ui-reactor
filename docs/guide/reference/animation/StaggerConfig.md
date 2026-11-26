# StaggerConfig

Stagger configuration for container children animations.

```csharp
public record StaggerConfig(TimeSpan Delay, Curve? Curve = null)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

**Learn more:** [Animation](../../animation.md)

## Constructors

### `StaggerConfig`

Stagger configuration for container children animations.

```csharp
public StaggerConfig(TimeSpan Delay, Curve? Curve = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Delay` | `TimeSpan` |  |  |
| `Curve` | [`Curve?`](curves.md#curve) | `null` |  |

## Properties

### `Curve`

```csharp
public Curve? Curve  // get, init
```

### `Delay`

```csharp
public TimeSpan Delay  // get, init
```
