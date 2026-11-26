# LayoutModifiers

Layout-related modifiers (sizing, alignment, spacing, visibility, theme, logical-direction insets). Stored as a lazy sub-record on [`ElementModifiers`](ElementModifiers.md) so that the common case of a few fields set allocates a small bucket rather than bloating the parent record. Public properties on [`ElementModifiers`](ElementModifiers.md) (Padding, Margin, Width, …) read from / write into this sub-record transparently — most callers never see this type.

```csharp
public record LayoutModifiers
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](../../modifier-system.md)

## Remarks

Spec 034 §A. The field set may grow but won't shrink — additions are always backwards-compatible.

## Constructors

### `LayoutModifiers`

```csharp
public LayoutModifiers()
```

## Properties

### `BorderInlineStart`

```csharp
public Thickness? BorderInlineStart  // get, init
```

### `Height`

```csharp
public double? Height  // get, init
```

### `HorizontalAlignment`

```csharp
public HorizontalAlignment? HorizontalAlignment  // get, init
```

### `HorizontalContentAlignment`

```csharp
public HorizontalAlignment? HorizontalContentAlignment  // get, init
```

### `IsVisible`

```csharp
public bool? IsVisible  // get, init
```

### `Margin`

```csharp
public Thickness? Margin  // get, init
```

### `MarginInlineEnd`

```csharp
public double? MarginInlineEnd  // get, init
```

### `MarginInlineStart`

```csharp
public double? MarginInlineStart  // get, init
```

### `MaxHeight`

```csharp
public double? MaxHeight  // get, init
```

### `MaxWidth`

```csharp
public double? MaxWidth  // get, init
```

### `MinHeight`

```csharp
public double? MinHeight  // get, init
```

### `MinWidth`

```csharp
public double? MinWidth  // get, init
```

### `Padding`

```csharp
public Thickness? Padding  // get, init
```

### `PaddingInlineEnd`

```csharp
public double? PaddingInlineEnd  // get, init
```

### `PaddingInlineStart`

```csharp
public double? PaddingInlineStart  // get, init
```

### `RequestedTheme`

```csharp
public ElementTheme? RequestedTheme  // get, init
```

### `VerticalAlignment`

```csharp
public VerticalAlignment? VerticalAlignment  // get, init
```

### `VerticalContentAlignment`

```csharp
public VerticalAlignment? VerticalContentAlignment  // get, init
```

### `Width`

```csharp
public double? Width  // get, init
```

## Methods

### `Merge`

Merge `other` into this record, preferring `other`'s set fields and falling back to ours. Mirrors [`ElementModifiers.Merge`](ElementModifiers.md#merge).

```csharp
public LayoutModifiers Merge(LayoutModifiers other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`LayoutModifiers`](LayoutModifiers.md) |  |

**Remarks**

Spec 034 §A.
