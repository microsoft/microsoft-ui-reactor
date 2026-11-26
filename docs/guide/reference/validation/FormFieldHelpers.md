# FormFieldHelpers

Helpers for FormField rendering logic.

```csharp
public static class FormFieldHelpers
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Methods

### `DetectFieldName`

Detects the field name from ValidationAttached on the content element.

```csharp
public static string? DetectFieldName(Element content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |

### `GetAutomationName`

Gets the automation name for the child control from the label text.

```csharp
public static string? GetAutomationName(string? label)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `label` | `string?` |  |

### `GetDescriptionOrError`

Gets the text to display in the description/error area. When errors are present and should be shown, returns error text. Otherwise returns the description text.

```csharp
public static (string? Text, bool IsError) GetDescriptionOrError(ValidationContext? ctx, string? fieldName, string? description, ShowWhen showWhen, bool submitAttempted = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ctx` | [`ValidationContext?`](ValidationContext.md) |  |  |
| `fieldName` | `string?` |  |  |
| `description` | `string?` |  |  |
| `showWhen` | [`ShowWhen`](enums.md#showwhen) |  |  |
| `submitAttempted` | `bool` | `false` |  |

### `GetDisplayLabel`

Gets the display label, including the required indicator (\*) if applicable.

```csharp
public static string GetDisplayLabel(string? label, bool required)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `label` | `string?` |  |
| `required` | `bool` |  |

### `ResolveFieldName`

Resolves the effective field name from either explicit or auto-detected.

```csharp
public static string? ResolveFieldName(string? explicitFieldName, Element content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `explicitFieldName` | `string?` |  |
| `content` | [`Element`](../elements/Element.md) |  |
