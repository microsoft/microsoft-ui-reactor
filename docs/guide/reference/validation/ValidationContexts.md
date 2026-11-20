# ValidationContexts

Provides the Context definition for propagating ValidationContext through the tree.

```csharp
public static class ValidationContexts
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Fields

### `Current`

The Context definition used to propagate ValidationContext through the tree. Default value is null (no validation context provided by any ancestor).

```csharp
public static readonly Context<ValidationContext?> Current
```
