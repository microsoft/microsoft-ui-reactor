---
title: "LayoutModifiers"
seo:
  description: "Layout-related modifiers (sizing, alignment, spacing, visibility, theme, logical-direction insets). Stored as a lazy sub-record on ElementModifiers so that the common case of a few fields set allocates a small bucket rather than bloating the parent record. Public properties on ElementModifiers (Padding, Margin, Width, …) read from / write into this sub-record transparently — most callers never see this type."
---

Layout-related modifiers (sizing, alignment, spacing, visibility, theme, logical-direction insets). Stored as a lazy sub-record on [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) so that the common case of a few fields set allocates a small bucket rather than bloating the parent record. Public properties on [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) (Padding, Margin, Width, …) read from / write into this sub-record transparently — most callers never see this type.

```csharp
public record LayoutModifiers
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](/microsoft-ui-reactor/main/how-it-works/modifier-system)

## Remarks [#remarks]

Spec 034 §A. The field set may grow but won't shrink — additions are always backwards-compatible.

## Constructors [#constructors]

### `LayoutModifiers` [#layoutmodifiers_1]

```csharp
public LayoutModifiers()
```

## Properties [#properties]

### `BorderInlineStart` [#borderinlinestart]

```csharp
public Thickness? BorderInlineStart  // get, init
```

### `Height` [#height]

```csharp
public double? Height  // get, init
```

### `HorizontalAlignment` [#horizontalalignment]

```csharp
public HorizontalAlignment? HorizontalAlignment  // get, init
```

### `HorizontalContentAlignment` [#horizontalcontentalignment]

```csharp
public HorizontalAlignment? HorizontalContentAlignment  // get, init
```

### `IsVisible` [#isvisible]

```csharp
public bool? IsVisible  // get, init
```

### `Margin` [#margin]

```csharp
public Thickness? Margin  // get, init
```

### `MarginInlineEnd` [#margininlineend]

```csharp
public double? MarginInlineEnd  // get, init
```

### `MarginInlineStart` [#margininlinestart]

```csharp
public double? MarginInlineStart  // get, init
```

### `MaxHeight` [#maxheight]

```csharp
public double? MaxHeight  // get, init
```

### `MaxWidth` [#maxwidth]

```csharp
public double? MaxWidth  // get, init
```

### `MinHeight` [#minheight]

```csharp
public double? MinHeight  // get, init
```

### `MinWidth` [#minwidth]

```csharp
public double? MinWidth  // get, init
```

### `Padding` [#padding]

```csharp
public Thickness? Padding  // get, init
```

### `PaddingInlineEnd` [#paddinginlineend]

```csharp
public double? PaddingInlineEnd  // get, init
```

### `PaddingInlineStart` [#paddinginlinestart]

```csharp
public double? PaddingInlineStart  // get, init
```

### `RequestedTheme` [#requestedtheme]

```csharp
public ElementTheme? RequestedTheme  // get, init
```

### `VerticalAlignment` [#verticalalignment]

```csharp
public VerticalAlignment? VerticalAlignment  // get, init
```

### `VerticalContentAlignment` [#verticalcontentalignment]

```csharp
public VerticalAlignment? VerticalContentAlignment  // get, init
```

### `Width` [#width]

```csharp
public double? Width  // get, init
```

## Methods [#methods]

### `Merge` [#merge]

Merge `other` into this record, preferring `other`'s set fields and falling back to ours. Mirrors [`ElementModifiers.Merge`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers#merge).

```csharp
public LayoutModifiers Merge(LayoutModifiers other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`LayoutModifiers`](/microsoft-ui-reactor/main/reference/modifiers/LayoutModifiers) |  |

**Remarks**

Spec 034 §A.
