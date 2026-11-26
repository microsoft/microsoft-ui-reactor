---
title: "PropertyGridDefaults"
description: "Default rendering templates for PropertyGrid — compact Windows 11 layout. Uses FlexRow per property row: fixed 160px label, grow-1 editor."
---

```csharp
public static class PropertyGridDefaults
```

Namespace: `Microsoft.UI.Reactor.Controls`.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls)

## Methods [#methods]

### `ArrayItemTemplate` [#arrayitemtemplate]

```csharp
public static Element ArrayItemTemplate(int index, string summary, bool isExpanded, Action<bool> onExpandedChanged, Action? onMoveUp, Action? onMoveDown, Action? onRemove)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |
| `summary` | `string` |  |
| `isExpanded` | `bool` |  |
| `onExpandedChanged` | `Action<bool>` |  |
| `onMoveUp` | `Action?` |  |
| `onMoveDown` | `Action?` |  |
| `onRemove` | `Action?` |  |

### `ArrayToolbarTemplate` [#arraytoolbartemplate]

```csharp
public static Element ArrayToolbarTemplate(string propertyName, int count, Func<Task>? onAdd)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `propertyName` | `string` |  |
| `count` | `int` |  |
| `onAdd` | `Func<Task>?` |  |

### `PropertyLabelTemplate` [#propertylabeltemplate]

```csharp
public static Element PropertyLabelTemplate(FieldDescriptor descriptor, int indentLevel)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `descriptor` | [`FieldDescriptor`](/microsoft-ui-reactor/main/reference/data/descriptors#fielddescriptor) |  |
| `indentLevel` | `int` |  |

### `PropertyRowTemplate` [#propertyrowtemplate]

```csharp
public static Element PropertyRowTemplate(FieldDescriptor descriptor, Element label, Element editor, int indentLevel)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `descriptor` | [`FieldDescriptor`](/microsoft-ui-reactor/main/reference/data/descriptors#fielddescriptor) |  |
| `label` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `editor` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `indentLevel` | `int` |  |
