# IAsyncValidator

An asynchronous validator for operations that require I/O (e.g., server-side uniqueness checks).

```csharp
public interface IAsyncValidator
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Methods

### `ValidateAsync`

Validates the given value asynchronously. Returns a ValidationMessage on failure, or null on success.

```csharp
public Task<ValidationMessage?> ValidateAsync(object? value, string field, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | `object?` |  |  |
| `field` | `string` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |
