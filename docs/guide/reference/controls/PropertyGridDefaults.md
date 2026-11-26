# PropertyGridDefaults

Default rendering templates for PropertyGrid — compact Windows 11 layout. Uses FlexRow per property row: fixed 160px label, grow-1 editor.

```csharp
public static class PropertyGridDefaults
```

Namespace: `Microsoft.UI.Reactor.Controls`.

**Learn more:** [Controls](../../controls.md)

## Methods

### `ArrayItemTemplate`

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

### `ArrayToolbarTemplate`

```csharp
public static Element ArrayToolbarTemplate(string propertyName, int count, Func<Task>? onAdd)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `propertyName` | `string` |  |
| `count` | `int` |  |
| `onAdd` | `Func<Task>?` |  |

### `PropertyLabelTemplate`

```csharp
public static Element PropertyLabelTemplate(FieldDescriptor descriptor, int indentLevel)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `descriptor` | [`FieldDescriptor`](../data/descriptors.md#fielddescriptor) |  |
| `indentLevel` | `int` |  |

### `PropertyRowTemplate`

```csharp
public static Element PropertyRowTemplate(FieldDescriptor descriptor, Element label, Element editor, int indentLevel)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `descriptor` | [`FieldDescriptor`](../data/descriptors.md#fielddescriptor) |  |
| `label` | [`Element`](../elements/Element.md) |  |
| `editor` | [`Element`](../elements/Element.md) |  |
| `indentLevel` | `int` |  |
