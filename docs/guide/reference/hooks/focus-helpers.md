# Focus helpers

The focus manager and the modifiers that the UseFocus hooks work with.

**Learn more:** [Hooks](../../hooks.md)

## `FocusAttached`

Attached metadata for focus registration on elements.

```csharp
public sealed record FocusAttached(string FieldName, FocusManager Manager, bool AutoFocus = false)
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Constructors

#### `FocusAttached`

Attached metadata for focus registration on elements.

```csharp
public FocusAttached(string FieldName, FocusManager Manager, bool AutoFocus = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `FieldName` | `string` |  |  |
| `Manager` | [`FocusManager`](#focusmanager) |  |  |
| `AutoFocus` | `bool` | `false` |  |

### Properties

#### `AutoFocus`

```csharp
public bool AutoFocus  // get, init
```

#### `FieldName`

```csharp
public string FieldName  // get, init
```

#### `Manager`

```csharp
public FocusManager Manager  // get, init
```

## `FocusManager`

Manages focus for a set of registered form fields. Supports programmatic focus control, enter-to-advance, and field ordering.

```csharp
public sealed class FocusManager
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Constructors

#### `FocusManager`

```csharp
public FocusManager()
```

### Properties

#### `Fields`

Registered field names in order.

```csharp
public IReadOnlyList<string> Fields  // get
```

### Methods

#### `Clear`

Clears all registrations.

```csharp
public void Clear()
```

#### `FocusField`

Focuses a specific field by name.

```csharp
public void FocusField(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `FocusFirst`

Focuses the first field from the given list that exists in this manager. Useful with ValidationContext.InvalidFields.

```csharp
public void FocusFirst(IReadOnlyList<string> fieldNames)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldNames` | `IReadOnlyList<string>` |  |

#### `FocusNext`

Focuses the field after the given field in registration order. If the current field is the last one, triggers submit.

```csharp
public void FocusNext(string? currentField = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `currentField` | `string?` | `null` |  |

#### `FocusPrevious`

Focuses the field before the given field in registration order.

```csharp
public void FocusPrevious(string? currentField = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `currentField` | `string?` | `null` |  |

#### `IsFirstField`

Returns true if the given field is the first registered field.

```csharp
public bool IsFirstField(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `IsLastField`

Returns true if the given field is the last registered field.

```csharp
public bool IsLastField(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `Register`

Registers a field name in ordering. Call on every render to maintain order.

```csharp
public void Register(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `SetControl`

Associates a WinUI control with a field name. Called from .Set() during mount/update.

```csharp
public void SetControl(string fieldName, Control control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |
| `control` | `Control` |  |

#### `SetSubmitHandler`

Sets the callback for submit (Enter on last field).

```csharp
public void SetSubmitHandler(Action handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action` |  |
