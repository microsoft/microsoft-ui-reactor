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

> **Obsolete.** Constructors of types with required members are not supported in this version of your compiler.

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
public A11yContext Context  // get, init
```

### `ElementType`

The C# record type name of the element (e.g., "ButtonElement").

```csharp
public string ElementType  // get, init
```

### `Fix`

What modifier to add and a suggested value.

```csharp
public A11yFixSuggestion Fix  // get, init
```

### `Id`

Diagnostic ID (e.g., "A11Y_001").

```csharp
public string Id  // get, init
```

### `Message`

Human-readable description of the issue.

```csharp
public string Message  // get, init
```

### `Severity`

"warning" or "info".

```csharp
public string Severity  // get, init
```

### `WcagCriterion`

WCAG 2.1 success criterion (e.g., "1.1.1").

```csharp
public string WcagCriterion  // get, init
```
