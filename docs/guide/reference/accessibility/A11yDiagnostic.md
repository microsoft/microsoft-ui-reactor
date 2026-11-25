# A11yDiagnostic

A single accessibility diagnostic finding from the scanner. Each finding maps to a specific WCAG 2.1 criterion and includes rich contextual information so an AI agent can generate intelligent fixes from the JSON export alone.

```csharp
public record A11yDiagnostic
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Accessibility](../../accessibility.md)

## Constructors

### `A11yDiagnostic`

```csharp
public A11yDiagnostic()
```

## Properties

### `AutomationId`

AutomationId if set on the element, null otherwise.

```csharp
public string? AutomationId  // get, init
```

### `ComponentType`

The component type name wrapping this element, if known.

```csharp
public string? ComponentType  // get, init
```

### `Context`

Contextual clues from the surrounding element tree.

```csharp
public required A11yContext Context  // get, init
```

### `ElementType`

The C# record type name of the element (e.g., "ButtonElement").

```csharp
public required string ElementType  // get, init
```

### `Fix`

What modifier to add and a suggested value.

```csharp
public required A11yFixSuggestion Fix  // get, init
```

### `Id`

Diagnostic ID (e.g., "A11Y_001").

```csharp
public required string Id  // get, init
```

### `Message`

Human-readable description of the issue.

```csharp
public required string Message  // get, init
```

### `Severity`

"warning" or "info".

```csharp
public required string Severity  // get, init
```

### `WcagCriterion`

WCAG 2.1 success criterion (e.g., "1.1.1").

```csharp
public required string WcagCriterion  // get, init
```
