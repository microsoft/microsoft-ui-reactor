# ErrorStyling

Default error styling configuration. Uses WinUI theme resource names.

```csharp
public static class ErrorStyling
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Methods

### `GetBrushKey`

Gets the theme resource key for the specified severity.

```csharp
public static string GetBrushKey(Severity severity)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `severity` | [`Severity`](enums.md#severity) |  |

### `ShouldShowErrors`

Determines whether error styling should be visible for a field, based on the field's state and the ShowWhen policy.

```csharp
public static bool ShouldShowErrors(ValidationContext ctx, string fieldName, ShowWhen showWhen, bool submitAttempted = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ctx` | [`ValidationContext`](ValidationContext.md) |  |  |
| `fieldName` | `string` |  |  |
| `showWhen` | [`ShowWhen`](enums.md#showwhen) |  |  |
| `submitAttempted` | `bool` | `false` |  |

## Fields

### `ErrorBorderThickness`

Default border thickness applied when an error is present.

```csharp
public static readonly Thickness ErrorBorderThickness
```

### `ErrorBrushKey`

WinUI theme resource key for Error-severity border brush (red). Resolved at runtime: SystemFillColorCriticalBrush

```csharp
public const string ErrorBrushKey = "SystemFillColorCriticalBrush"
```

### `InfoBrushKey`

WinUI theme resource key for Info-severity styling (blue/gray). Resolved at runtime: SystemFillColorSolidNeutralBrush

```csharp
public const string InfoBrushKey = "SystemFillColorSolidNeutralBrush"
```

### `WarningBrushKey`

WinUI theme resource key for Warning-severity border brush (yellow/orange). Resolved at runtime: SystemFillColorCautionBrush

```csharp
public const string WarningBrushKey = "SystemFillColorCautionBrush"
```
