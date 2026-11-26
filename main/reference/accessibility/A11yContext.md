---
title: "A11yContext"
description: "Rich contextual information harvested from the element tree during the scan. An AI agent uses this to generate semantically correct accessible names without needing to re-read the source code."
---

```csharp
public record A11yContext
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Accessibility](/microsoft-ui-reactor/main/guides/accessibility)

## Constructors [#constructors]

### `A11yContext` [#a11ycontext_1]

```csharp
public A11yContext()
```

## Properties [#properties]

### `ChildContent` [#childcontent]

String description of the child content (e.g., "SymbolIcon(Symbol.Delete)").

```csharp
public string? ChildContent  // get, init
```

### `ChildText` [#childtext]

Text content of the first TextBlockElement child, if any.

```csharp
public string? ChildText  // get, init
```

### `ChildTypes` [#childtypes]

Type names of immediate children.

```csharp
public string[]? ChildTypes  // get, init
```

### `Header` [#header]

Header property value (for TextBoxElement etc.).

```csharp
public string? Header  // get, init
```

### `NearestHeading` [#nearestheading]

Text and level of the nearest heading above this element.

```csharp
public string? NearestHeading  // get, init
```

### `NearestLandmark` [#nearestlandmark]

Nearest landmark type name (e.g., "Navigation", "Main").

```csharp
public string? NearestLandmark  // get, init
```

### `ParentAutomationName` [#parentautomationname]

AutomationName of the nearest ancestor that has one.

```csharp
public string? ParentAutomationName  // get, init
```

### `PlaceholderText` [#placeholdertext]

Placeholder text (for TextBoxElement etc.).

```csharp
public string? PlaceholderText  // get, init
```

### `SiblingTexts` [#siblingtexts]

Text labels of sibling elements at the same level.

```csharp
public string[]? SiblingTexts  // get, init
```
