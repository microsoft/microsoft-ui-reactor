# ValidateExtensions

Fluent extension methods for attaching validation to Reactor elements.

```csharp
public static class ValidateExtensions
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Methods

### `RunAsyncValidators`

Runs all async validators attached to an element against a value.

```csharp
public static Task<IReadOnlyList<ValidationMessage>> RunAsyncValidators(this ValidationAttached attached, object? value, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | `object?` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### `RunValidators`

Runs all synchronous validators attached to an element against a value. Returns the list of validation messages (empty if all pass).

```csharp
public static IReadOnlyList<ValidationMessage> RunValidators(this ValidationAttached attached, object? value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `object?` |  |

## Modifiers

- [`GetValidation`](../modifiers/validation.md#getvalidation)
- [`Validate`](../modifiers/validation.md#validate)
- [`ValidateAsync`](../modifiers/validation.md#validateasync)
