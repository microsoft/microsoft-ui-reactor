# SemanticPanel

A lightweight WinUI Panel that provides custom automation semantics for composite Reactor components. Wraps a single child and overrides OnCreateAutomationPeer() to expose a custom role, value, and range to screen readers.

This solves the fundamental limitation where Reactor components (C# records) cannot override OnCreateAutomationPeer() on WinUI Controls.

Usage from Reactor DSL: StarRating(value: 3, max: 5) .Semantics(role: "slider", value: "3 of 5 stars", rangeValue: 3, rangeMin: 0, rangeMax: 5)

```csharp
public sealed class SemanticPanel : Panel
```

Namespace: `Microsoft.UI.Reactor.Accessibility`. Inherits from [`Panel`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.panel).

**Learn more:** [Accessibility](../../accessibility.md)

## Constructors

### `SemanticPanel`

```csharp
public SemanticPanel()
```

## Properties

### `IsReadOnly`

```csharp
public bool IsReadOnly  // get, set
```

### `RangeMaximum`

```csharp
public double RangeMaximum  // get, set
```

### `RangeMinimum`

```csharp
public double RangeMinimum  // get, set
```

### `RangeValue`

```csharp
public double RangeValue  // get, set
```

### `SemanticRole`

```csharp
public string? SemanticRole  // get, set
```

### `SemanticValue`

```csharp
public string? SemanticValue  // get, set
```

## Fields

### `IsReadOnlyProperty`

```csharp
public static readonly DependencyProperty IsReadOnlyProperty
```

### `RangeMaximumProperty`

```csharp
public static readonly DependencyProperty RangeMaximumProperty
```

### `RangeMinimumProperty`

```csharp
public static readonly DependencyProperty RangeMinimumProperty
```

### `RangeValueProperty`

```csharp
public static readonly DependencyProperty RangeValueProperty
```

### `SemanticRoleProperty`

```csharp
public static readonly DependencyProperty SemanticRoleProperty
```

### `SemanticValueProperty`

```csharp
public static readonly DependencyProperty SemanticValueProperty
```
