# SemanticDescription

Describes the semantic role, value, and range of a composite component for assistive technology. Used with the .Semantics() modifier.

```csharp
public record SemanticDescription(string? Role = null, string? Value = null, double? RangeMin = null, double? RangeMax = null, double? RangeValue = null, bool IsReadOnly = true)
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Accessibility](../../accessibility.md)

## Constructors

### `SemanticDescription`

Describes the semantic role, value, and range of a composite component for assistive technology. Used with the .Semantics() modifier.

```csharp
public SemanticDescription(string? Role = null, string? Value = null, double? RangeMin = null, double? RangeMax = null, double? RangeValue = null, bool IsReadOnly = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Role` | `string?` | `null` |  |
| `Value` | `string?` | `null` |  |
| `RangeMin` | `double?` | `null` |  |
| `RangeMax` | `double?` | `null` |  |
| `RangeValue` | `double?` | `null` |  |
| `IsReadOnly` | `bool` | `true` |  |

## Properties

### `IsReadOnly`

```csharp
public bool IsReadOnly  // get, init
```

### `RangeMax`

```csharp
public double? RangeMax  // get, init
```

### `RangeMin`

```csharp
public double? RangeMin  // get, init
```

### `RangeValue`

```csharp
public double? RangeValue  // get, init
```

### `Role`

```csharp
public string? Role  // get, init
```

### `Value`

```csharp
public string? Value  // get, init
```
