# ErrorBubbling

Helpers for filtering and processing validation messages through the visualizer hierarchy.

```csharp
public static class ErrorBubbling
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Methods

### `FilterMessages`

Filters messages based on a visualizer's severity filter. Returns (caught, uncaught) — caught messages are displayed, uncaught continue to bubble.

```csharp
public static (IReadOnlyList<ValidationMessage> Caught, IReadOnlyList<ValidationMessage> Uncaught) FilterMessages(IReadOnlyList<ValidationMessage> messages, Severity? severityFilter)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `messages` | `IReadOnlyList<ValidationMessage>` |  |
| `severityFilter` | `Severity?` |  |

### `HighestSeverity`

Gets the highest severity from a list of messages.

```csharp
public static Severity? HighestSeverity(IReadOnlyList<ValidationMessage> messages)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `messages` | `IReadOnlyList<ValidationMessage>` |  |

### `ShouldDisplay`

Determines whether a visualizer should display its content based on ShowWhen and context state.

```csharp
public static bool ShouldDisplay(IReadOnlyList<ValidationMessage> messages, ShowWhen showWhen, ValidationContext? ctx = null, bool submitAttempted = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `messages` | `IReadOnlyList<ValidationMessage>` |  |  |
| `showWhen` | [`ShowWhen`](enums.md#showwhen) |  |  |
| `ctx` | [`ValidationContext?`](ValidationContext.md) | `null` |  |
| `submitAttempted` | `bool` | `false` |  |
