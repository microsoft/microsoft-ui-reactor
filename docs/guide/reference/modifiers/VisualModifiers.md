# VisualModifiers

Visual-related modifiers (brushes, borders, transforms, opacity). Stored as a lazy sub-record on [`ElementModifiers`](ElementModifiers.md) in the same pattern as [`LayoutModifiers`](LayoutModifiers.md). Public properties on [`ElementModifiers`](ElementModifiers.md) (Foreground, Background, BorderBrush, …) shim through.

```csharp
public record VisualModifiers
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](../../modifier-system.md)

## Remarks

Spec 034 §A. The field set may grow but won't shrink — additions are always backwards-compatible.

## Constructors

### `VisualModifiers`

```csharp
public VisualModifiers()
```

## Properties

### `Background`

```csharp
public Brush? Background  // get, init
```

### `BorderBrush`

```csharp
public Brush? BorderBrush  // get, init
```

### `BorderThickness`

```csharp
public Thickness? BorderThickness  // get, init
```

### `CenterPoint`

```csharp
public Vector3? CenterPoint  // get, init
```

### `CornerRadius`

```csharp
public CornerRadius? CornerRadius  // get, init
```

### `Foreground`

```csharp
public Brush? Foreground  // get, init
```

### `Opacity`

```csharp
public double? Opacity  // get, init
```

### `Rotation`

```csharp
public float? Rotation  // get, init
```

### `Scale`

```csharp
public Vector3? Scale  // get, init
```

### `TransformOrigin`

The size-relative pivot of `.TransformOrigin(x, y)` (spec 066 §8).

```csharp
public Vector2? TransformOrigin  // get, init
```

### `Translation`

```csharp
public Vector3? Translation  // get, init
```

## Methods

### `Merge`

Merge `other` into this record, preferring `other`'s set fields and falling back to ours.

```csharp
public VisualModifiers Merge(VisualModifiers other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`VisualModifiers`](VisualModifiers.md) |  |

**Remarks**

Spec 034 §A.
