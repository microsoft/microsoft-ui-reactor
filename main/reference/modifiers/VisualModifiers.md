---
title: "VisualModifiers"
seo:
  description: "Visual-related modifiers (brushes, borders, transforms, opacity). Stored as a lazy sub-record on ElementModifiers in the same pattern as LayoutModifiers. Public properties on ElementModifiers (Foreground, Background, BorderBrush, …) shim through."
---

Visual-related modifiers (brushes, borders, transforms, opacity). Stored as a lazy sub-record on [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) in the same pattern as [`LayoutModifiers`](/microsoft-ui-reactor/main/reference/modifiers/LayoutModifiers). Public properties on [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) (Foreground, Background, BorderBrush, …) shim through.

```csharp
public record VisualModifiers
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](/microsoft-ui-reactor/main/how-it-works/modifier-system)

## Remarks [#remarks]

Spec 034 §A. The field set may grow but won't shrink — additions are always backwards-compatible.

## Constructors [#constructors]

### `VisualModifiers` [#visualmodifiers_1]

```csharp
public VisualModifiers()
```

## Properties [#properties]

### `Background` [#background]

```csharp
public Brush? Background  // get, init
```

### `BorderBrush` [#borderbrush]

```csharp
public Brush? BorderBrush  // get, init
```

### `BorderThickness` [#borderthickness]

```csharp
public Thickness? BorderThickness  // get, init
```

### `CenterPoint` [#centerpoint]

```csharp
public Vector3? CenterPoint  // get, init
```

### `CornerRadius` [#cornerradius]

```csharp
public CornerRadius? CornerRadius  // get, init
```

### `Foreground` [#foreground]

```csharp
public Brush? Foreground  // get, init
```

### `Opacity` [#opacity]

```csharp
public double? Opacity  // get, init
```

### `Rotation` [#rotation]

```csharp
public float? Rotation  // get, init
```

### `Scale` [#scale]

```csharp
public Vector3? Scale  // get, init
```

### `TransformOrigin` [#transformorigin]

The size-relative pivot of `.TransformOrigin(x, y)` (spec 066 §8).

```csharp
public Vector2? TransformOrigin  // get, init
```

### `Translation` [#translation]

```csharp
public Vector3? Translation  // get, init
```

## Methods [#methods]

### `Merge` [#merge]

Merge `other` into this record, preferring `other`'s set fields and falling back to ours.

```csharp
public VisualModifiers Merge(VisualModifiers other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`VisualModifiers`](/microsoft-ui-reactor/main/reference/modifiers/VisualModifiers) |  |

**Remarks**

Spec 034 §A.
