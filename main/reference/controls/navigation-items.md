---
title: "Navigation items"
description: "Records that describe items of navigation controls."
---

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls)

## `BreadcrumbBarItemData` [#breadcrumbbaritemdata]

```csharp
public record BreadcrumbBarItemData(string Label, object? Tag = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors]

#### `BreadcrumbBarItemData` [#breadcrumbbaritemdata_1]

```csharp
public BreadcrumbBarItemData(string Label, object? Tag = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Label` | `string` |  |  |
| `Tag` | `object?` | `null` |  |

### Properties [#properties]

#### `Label` [#label]

```csharp
public string Label  // get, init
```

#### `Tag` [#tag]

```csharp
public object? Tag  // get, init
```

## `NavigationViewItemData` [#navigationviewitemdata]

```csharp
public record NavigationViewItemData(string Content, string? Icon = null, string? Tag = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_1]

#### `NavigationViewItemData` [#navigationviewitemdata_1]

```csharp
public NavigationViewItemData(string Content, string? Icon = null, string? Tag = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Content` | `string` |  |  |
| `Icon` | `string?` | `null` |  |
| `Tag` | `string?` | `null` |  |

### Properties [#properties_1]

#### `Children` [#children]

```csharp
public NavigationViewItemData[]? Children  // get, init
```

#### `Content` [#content]

```csharp
public string Content  // get, init
```

#### `Icon` [#icon]

```csharp
public string? Icon  // get, init
```

#### `IconElement` [#iconelement]

```csharp
public IconData? IconElement  // get, init
```

#### `IsHeader` [#isheader]

```csharp
public bool IsHeader  // get, init
```

#### `Tag` [#tag_1]

```csharp
public string? Tag  // get, init
```

## `PivotItemData` [#pivotitemdata]

```csharp
public record PivotItemData(string Header, Element Content)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_2]

#### `PivotItemData` [#pivotitemdata_1]

```csharp
public PivotItemData(string Header, Element Content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Header` | `string` |  |
| `Content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### Properties [#properties_2]

#### `Content` [#content_1]

```csharp
public Element Content  // get, init
```

#### `Header` [#header]

```csharp
public string Header  // get, init
```

## `SelectorBarItemData` [#selectorbaritemdata]

```csharp
public record SelectorBarItemData(string Text, string? Icon = null)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_3]

#### `SelectorBarItemData` [#selectorbaritemdata_1]

```csharp
public SelectorBarItemData(string Text, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Text` | `string` |  |  |
| `Icon` | `string?` | `null` |  |

### Properties [#properties_3]

#### `Icon` [#icon_1]

```csharp
public string? Icon  // get, init
```

#### `Text` [#text]

```csharp
public string Text  // get, init
```

## `TabViewItemData` [#tabviewitemdata]

```csharp
public record TabViewItemData(string Header, Element Content)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_4]

#### `TabViewItemData` [#tabviewitemdata_1]

```csharp
public TabViewItemData(string Header, Element Content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Header` | `string` |  |
| `Content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### Properties [#properties_4]

#### `Content` [#content_2]

```csharp
public Element Content  // get, init
```

#### `Header` [#header_1]

```csharp
public string Header  // get, init
```

#### `Icon` [#icon_2]

```csharp
public string? Icon  // get, init
```

#### `IsClosable` [#isclosable]

```csharp
public bool IsClosable  // get, init
```

#### `IsPinnable` [#ispinnable]

Spec 045 §2.2 — when true, the reconciler renders a small "pin" affordance in the tab header chrome alongside the close X. Used by the docking pipeline for ToolWindow tabs whose CanAutoHide=true.

```csharp
public bool IsPinnable  // get, init
```

#### `IsPinned` [#ispinned]

Indicates whether the pin button should render as already-pinned (glyph ) or unpinned (). Only consulted when IsPinnable is true.

```csharp
public bool IsPinned  // get, init
```

#### `OnPinRequested` [#onpinrequested]

Invoked when the user clicks the pin button. Caller is responsible for routing through the docking model (DockHostModel.PinToSide / Hide).

```csharp
public Action? OnPinRequested  // get, init
```

#### `PinAutomationId` [#pinautomationid]

Stable AutomationId for the pin button. Selftests address the per-tab pin via this id.

```csharp
public string? PinAutomationId  // get, init
```

#### `PinAutomationName` [#pinautomationname]

Tooltip + AT name applied to the pin button when IsPinnable is true. Caller supplies the localized string.

```csharp
public string? PinAutomationName  // get, init
```
