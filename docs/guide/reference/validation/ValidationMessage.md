# ValidationMessage

An immutable validation message associated with a specific field.

```csharp
public sealed record ValidationMessage(string Field, string Text, Severity Severity = Severity.Error, string? Code = null)
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Constructors

### `ValidationMessage`

An immutable validation message associated with a specific field.

```csharp
public ValidationMessage(string Field, string Text, Severity Severity = Severity.Error, string? Code = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Field` | `string` |  | The field name this message applies to. |
| `Text` | `string` |  | Human-readable message text. |
| `Severity` | [`Severity`](enums.md#severity) | `Severity.Error` | Message severity (default: Error). |
| `Code` | `string?` | `null` | Optional machine-readable code for i18n/dedup. |

## Properties

### `Code`

Optional machine-readable code for i18n/dedup.

```csharp
public string? Code  // get, init
```

### `Field`

The field name this message applies to.

```csharp
public string Field  // get, init
```

### `Severity`

Message severity (default: Error).

```csharp
public Severity Severity  // get, init
```

### `Text`

Human-readable message text.

```csharp
public string Text  // get, init
```
