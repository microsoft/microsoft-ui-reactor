---
title: "SemanticDescription"
description: "Describes the semantic role, value, and range of a composite component for assistive technology. Used with the .Semantics() modifier."
---

```csharp
public record SemanticDescription(string? Role = null, string? Value = null, double? RangeMin = null, double? RangeMax = null, double? RangeValue = null, bool IsReadOnly = true)
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Accessibility](/microsoft-ui-reactor/main/guides/accessibility)

## Constructors [#constructors]

### `SemanticDescription` [#semanticdescription_1]

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

## Properties [#properties]

### `IsReadOnly` [#isreadonly]

```csharp
public bool IsReadOnly  // get, init
```

### `RangeMax` [#rangemax]

```csharp
public double? RangeMax  // get, init
```

### `RangeMin` [#rangemin]

```csharp
public double? RangeMin  // get, init
```

### `RangeValue` [#rangevalue]

```csharp
public double? RangeValue  // get, init
```

### `Role` [#role]

```csharp
public string? Role  // get, init
```

### `Value` [#value]

```csharp
public string? Value  // get, init
```
