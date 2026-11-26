# ValidationContext

Collects, queries, and manages validation messages for a form or section of a component tree. Thread-safe: all mutation and query methods are synchronized.

```csharp
public sealed class ValidationContext
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Constructors

### `ValidationContext`

```csharp
public ValidationContext()
```

## Properties

### `InvalidFields`

Returns the names of all fields that have at least one Error-severity message.

```csharp
public IReadOnlyList<string> InvalidFields  // get
```

### `RegisteredFields`

Returns all registered field names.

```csharp
public IReadOnlySet<string> RegisteredFields  // get
```

### `SubmitAttempted`

Whether [`ValidationContext.MarkAllTouched`](#markalltouched) has been called since the last reset — the context's record of a submit attempt, read by [`ShowWhen.AfterFirstSubmit`](enums.md#values_1).

```csharp
public bool SubmitAttempted  // get
```

### `Version`

Monotonically increasing version number, bumped on every mutation. Useful for change detection in hooks/memos.

```csharp
public int Version  // get
```

## Methods

### `Add`

#### `Add(ValidationMessage)`

Adds a validation message.

```csharp
public void Add(ValidationMessage message)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `message` | [`ValidationMessage`](ValidationMessage.md) |  |

#### `Add(string, string, Severity)`

Adds a validation message for the specified field.

```csharp
public void Add(string field, string text, Severity severity = Severity.Error)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `field` | `string` |  |  |
| `text` | `string` |  |  |
| `severity` | [`Severity`](enums.md#severity) | `Severity.Error` |  |

### `AddExternal`

#### `AddExternal(ValidationMessage)`

Adds an externally-sourced validation message.

```csharp
public void AddExternal(ValidationMessage message)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `message` | [`ValidationMessage`](ValidationMessage.md) |  |

#### `AddExternal(string, string, Severity)`

Adds an externally-sourced validation message (e.g., server-side error). External messages persist until explicitly cleared or the field value changes.

```csharp
public void AddExternal(string field, string text, Severity severity = Severity.Error)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `field` | `string` |  |  |
| `text` | `string` |  |  |
| `severity` | [`Severity`](enums.md#severity) | `Severity.Error` |  |

### `Clear`

Clears all validation messages (both internal and external) for the specified field.

```csharp
public void Clear(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `ClearAll`

Clears all messages (internal and external) for all fields.

```csharp
public void ClearAll()
```

### `ClearExternal`

Clears only external messages for the specified field.

```csharp
public void ClearExternal(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `GetAllMessages`

Returns all messages (internal + external) across all fields.

```csharp
public IReadOnlyList<ValidationMessage> GetAllMessages()
```

### `GetMessages`

Returns all messages (internal + external) for the specified field.

```csharp
public IReadOnlyList<ValidationMessage> GetMessages(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `HasError`

Returns true if the specified field has any Error-severity messages.

```csharp
public bool HasError(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `HasMessages`

Returns true if the specified field has any messages of any severity.

```csharp
public bool HasMessages(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `HighestSeverity`

Returns the highest severity among all messages for the specified field, or null if none.

```csharp
public Severity? HighestSeverity(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `IsDirty`

#### `IsDirty()`

Returns true if any registered field is dirty.

```csharp
public bool IsDirty()
```

#### `IsDirty(string)`

Returns true if the field's current value differs from its initial value.

```csharp
public bool IsDirty(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `IsTouched`

Returns true if the field has been focused and then blurred.

```csharp
public bool IsTouched(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `IsValid`

Returns true when there are no Error-severity messages across all fields.

```csharp
public bool IsValid()
```

### `MarkAllTouched`

Marks all registered fields as touched. Typically called on form submit.

Also records that a submit was attempted, which is what [`ShowWhen.AfterFirstSubmit`](enums.md#values_1) waits for. The framework has no other notion of "submit": this is the call the guide tells you to make on a submit attempt, so tying the two together is what makes that policy reachable on a `FormField` at all — before this it could never show an error, behaving identically to [`ShowWhen.Never`](enums.md#values_1) (issue #1262).

```csharp
public void MarkAllTouched()
```

### `MarkTouched`

Marks a field as touched (e.g., after focus+blur).

```csharp
public void MarkTouched(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `NotifyValueChanged`

Notifies the context of a field value change. Clears external messages for the field, because a server-side verdict about the old value says nothing about the new one.

The clear is conditional on the value actually having moved. It used to be unconditional, which was survivable only while validation ran rarely: now that validators run on every render, an unconditional clear here would destroy any [`ValidationContext.AddExternal`](#addexternalstring-string-severity) message on the very next repaint, before the user could read it.

An async verdict is retired for the same reason, mirroring [`ValidationContext.ApplyValidation`](ValidationContext.md). A field validated only through `.ValidateAsync(...)` never reaches that method, so without this an error computed for a value the user has already replaced would stay on screen, and a pass opened against the old value could still install its result afterwards.

```csharp
public void NotifyValueChanged(string field, object? value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |
| `value` | `object?` |  |

### `RegisterField`

Registers a field with the context. Called automatically by .Validate() or manually for fields that need dirty/touched tracking.

```csharp
public void RegisterField(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `Reset`

Resets a field to its initial value, clears touched state and all messages. Returns the initial value so the caller can update the UI.

```csharp
public object? Reset(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

### `ResetAll`

Resets all fields to their initial values, clears all touched states and messages. Returns a dictionary of field → initial value.

```csharp
public IReadOnlyDictionary<string, object?> ResetAll()
```

### `SetInitialValue`

Stores the initial value for a field. Called at field registration time.

The current value is seeded only the first time the field is seen. Re-seeding it on every call would rewind whatever the user has since typed — harmless while nothing watched the context, but with change notification it becomes a permanent repaint loop for the common `SetInitialValue(...)` + `NotifyValueChanged(...)` pair that components run on each render: the rewind and the re-notify would take turns forever. Use [`ValidationContext.Reset`](#reset) to deliberately return a field to its baseline.

```csharp
public void SetInitialValue(string field, object? value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |
| `value` | `object?` |  |

## Events

### `Changed`

Raised after any mutation that actually changed observable state — a message appearing or disappearing, a field becoming touched, a reset.

`UseValidationContext()` subscribes to this so that mutating the context from an event handler (the canonical case being [`ValidationContext.MarkAllTouched`](#markalltouched) on a submit attempt) repaints the form. Without it, an invalid submit changes nothing the user can see.

Two rules keep this from driving a render loop. First, re-running the same validators over an unchanged value is silent: results are applied with a structural diff, so an idempotent re-validation raises nothing. Second, mutations made while a render is in flight are not announced *inline* — the rendering component reads the new state later in the same pass, and notifying there would re-enter the reconciler's re-render path from inside `Render()`. They are held and delivered once the pass finishes, so other subscribers — a parent rendering `IsValid()`, say — still hear about them; a pass that ends with the state it started with is dropped rather than delivered, since there is nothing to announce.

```csharp
public event Action? Changed
```
