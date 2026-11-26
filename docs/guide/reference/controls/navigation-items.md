# Navigation items

Records that describe items of navigation controls.

**Learn more:** [Controls](../../controls.md)

## `BreadcrumbBarItemData`

```csharp
public record BreadcrumbBarItemData(string Label, object? Tag = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `BreadcrumbBarItemData`

```csharp
public BreadcrumbBarItemData(string Label, object? Tag = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Label` | `string` |  |  |
| `Tag` | `object?` | `null` |  |

### Properties

#### `Label`

```csharp
public string Label  // get, init
```

#### `Tag`

```csharp
public object? Tag  // get, init
```

## `NavigationViewItemData`

```csharp
public record NavigationViewItemData(string Content, string? Icon = null, string? Tag = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `NavigationViewItemData`

```csharp
public NavigationViewItemData(string Content, string? Icon = null, string? Tag = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Content` | `string` |  |  |
| `Icon` | `string?` | `null` |  |
| `Tag` | `string?` | `null` |  |

### Properties

#### `Children`

```csharp
public NavigationViewItemData[]? Children  // get, init
```

#### `Content`

```csharp
public string Content  // get, init
```

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IconElement`

```csharp
public IconData? IconElement  // get, init
```

#### `IsHeader`

```csharp
public bool IsHeader  // get, init
```

#### `Tag`

```csharp
public string? Tag  // get, init
```

## `PivotItemData`

```csharp
public record PivotItemData(string Header, Element Content)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `PivotItemData`

```csharp
public PivotItemData(string Header, Element Content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Header` | `string` |  |
| `Content` | [`Element`](../elements/Element.md) |  |

### Properties

#### `Content`

```csharp
public Element Content  // get, init
```

#### `Header`

```csharp
public string Header  // get, init
```

## `SelectorBarItemData`

```csharp
public record SelectorBarItemData(string Text, string? Icon = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `SelectorBarItemData`

```csharp
public SelectorBarItemData(string Text, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Text` | `string` |  |  |
| `Icon` | `string?` | `null` |  |

### Properties

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `Text`

```csharp
public string Text  // get, init
```

## `TabViewItemData`

```csharp
public record TabViewItemData(string Header, Element Content)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `TabViewItemData`

```csharp
public TabViewItemData(string Header, Element Content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Header` | `string` |  |
| `Content` | [`Element`](../elements/Element.md) |  |

### Properties

#### `Content`

```csharp
public Element Content  // get, init
```

#### `Header`

```csharp
public string Header  // get, init
```

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IsClosable`

```csharp
public bool IsClosable  // get, init
```

#### `IsPinnable`

Spec 045 §2.2 — when true, the reconciler renders a small "pin" affordance in the tab header chrome alongside the close X. Used by the docking pipeline for ToolWindow tabs whose CanAutoHide=true.

```csharp
public bool IsPinnable  // get, init
```

#### `IsPinned`

Indicates whether the pin button should render as already-pinned (glyph ) or unpinned (). Only consulted when IsPinnable is true.

```csharp
public bool IsPinned  // get, init
```

#### `OnPinRequested`

Invoked when the user clicks the pin button. Caller is responsible for routing through the docking model (DockHostModel.PinToSide / Hide).

```csharp
public Action? OnPinRequested  // get, init
```

#### `PinAutomationId`

Stable AutomationId for the pin button. Selftests address the per-tab pin via this id.

```csharp
public string? PinAutomationId  // get, init
```

#### `PinAutomationName`

Tooltip + AT name applied to the pin button when IsPinnable is true. Caller supplies the localized string.

```csharp
public string? PinAutomationName  // get, init
```
