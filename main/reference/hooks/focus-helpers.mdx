---
title: "Focus helpers"
description: "The focus manager and the modifiers that the UseFocus hooks work with."
---

**Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## `FocusAttached` [#focusattached]

Attached metadata for focus registration on elements.

```csharp
public sealed record FocusAttached(string FieldName, FocusManager Manager, bool AutoFocus = false)
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Constructors [#constructors]

#### `FocusAttached` [#focusattached_1]

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

### Properties [#properties]

#### `AutoFocus` [#autofocus]

```csharp
public bool AutoFocus  // get, init
```

#### `FieldName` [#fieldname]

```csharp
public string FieldName  // get, init
```

#### `Manager` [#manager]

```csharp
public FocusManager Manager  // get, init
```

## `FocusExtensions` [#focusextensions]

Fluent extensions for attaching focus management to elements. Uses .Set() to capture the WinUI control reference during mount/update.

```csharp
public static class FocusExtensions
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Methods [#methods]

#### `GetFocus` [#getfocus]

Gets the FocusAttached metadata from an element.

```csharp
public static FocusAttached? GetFocus<T>(this T el) where T : Element
```

### Modifiers [#modifiers]

- [`Focus`](/microsoft-ui-reactor/main/reference/modifiers/input#focus)
- [`FocusMeta`](/microsoft-ui-reactor/main/reference/modifiers/focus-and-keyboard#focusmeta)

## `FocusManager` [#focusmanager]

Manages focus for a set of registered form fields. Supports programmatic focus control, enter-to-advance, and field ordering.

```csharp
public sealed class FocusManager
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Constructors [#constructors_1]

#### `FocusManager` [#focusmanager_1]

```csharp
public FocusManager()
```

### Properties [#properties_1]

#### `Fields` [#fields]

Registered field names in order.

```csharp
public IReadOnlyList<string> Fields  // get
```

### Methods [#methods_1]

#### `Clear` [#clear]

Clears all registrations.

```csharp
public void Clear()
```

#### `FocusField` [#focusfield]

Focuses a specific field by name.

```csharp
public void FocusField(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `FocusFirst` [#focusfirst]

Focuses the first field from the given list that exists in this manager. Useful with ValidationContext.InvalidFields.

```csharp
public void FocusFirst(IReadOnlyList<string> fieldNames)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldNames` | `IReadOnlyList<string>` |  |

#### `FocusNext` [#focusnext]

Focuses the field after the given field in registration order. If the current field is the last one, triggers submit.

```csharp
public void FocusNext(string? currentField = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `currentField` | `string?` | `null` |  |

#### `FocusPrevious` [#focusprevious]

Focuses the field before the given field in registration order.

```csharp
public void FocusPrevious(string? currentField = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `currentField` | `string?` | `null` |  |

#### `IsFirstField` [#isfirstfield]

Returns true if the given field is the first registered field.

```csharp
public bool IsFirstField(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `IsLastField` [#islastfield]

Returns true if the given field is the last registered field.

```csharp
public bool IsLastField(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `Register` [#register]

Registers a field name in ordering. Call on every render to maintain order.

```csharp
public void Register(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `SetControl` [#setcontrol]

Associates a WinUI control with a field name. Called from .Set() during mount/update.

```csharp
public void SetControl(string fieldName, Control control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |
| `control` | `Control` |  |

#### `SetSubmitHandler` [#setsubmithandler]

Sets the callback for submit (Enter on last field).

```csharp
public void SetSubmitHandler(Action handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action` |  |
