# A11yFixSuggestion

Tells an AI agent exactly which modifier to add and what value to use.

```csharp
public record A11yFixSuggestion
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Accessibility](../../accessibility.md)

## Constructors

### `A11yFixSuggestion`

```csharp
public A11yFixSuggestion()
```

> **Obsolete.** Constructors of types with required members are not supported in this version of your compiler.

## Properties

### `CodeSnippet`

Example code snippet (e.g., ".AutomationName(\\"...\\")").

```csharp
public string? CodeSnippet  // get, init
```

### `Modifier`

The Reactor modifier method name (e.g., "AutomationName").

```csharp
public string Modifier  // get, init
```

### `SuggestedValue`

A suggested value, or null if the agent must infer from context.

```csharp
public string? SuggestedValue  // get, init
```
