---
title: "Validate"
description: "Factory methods for built-in validators."
---

```csharp
public static class Validate
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## Methods [#methods]

### `Email` [#email]

String must be a valid email address.

```csharp
public static IValidator Email(string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `message` | `string?` | `null` |  |

### `EqualTo` [#equalto]

Value must equal another value (for cross-field equality like password confirmation).

```csharp
public static IValidator EqualTo<T>(T otherValue, string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `otherValue` | `T` |  |  |
| `message` | `string?` | `null` |  |

### `Match` [#match]

String must match the given regex pattern.

```csharp
public static IValidator Match(string regex, string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `regex` | `string` |  |  |
| `message` | `string?` | `null` |  |

### `MaxLength` [#maxlength]

String length must be \<= n.

```csharp
public static IValidator MaxLength(int n, string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `n` | `int` |  |  |
| `message` | `string?` | `null` |  |

### `MinLength` [#minlength]

String length must be \>= n.

```csharp
public static IValidator MinLength(int n, string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `n` | `int` |  |  |
| `message` | `string?` | `null` |  |

### `Must` [#must]

Custom synchronous predicate validator.

```csharp
public static IValidator Must<T>(Func<T, bool> predicate, string message)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `predicate` | `Func<T, bool>` |  |
| `message` | `string` |  |

### `MustAsync` [#mustasync]

Custom asynchronous predicate validator.

```csharp
public static IAsyncValidator MustAsync<T>(Func<T, Task<bool>> predicate, string message)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `predicate` | `Func<T, Task<bool>>` |  |
| `message` | `string` |  |

### `MustBeTrue` [#mustbetrue]

Boolean value must be true (e.g., for checkboxes).

```csharp
public static IValidator MustBeTrue(string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `message` | `string?` | `null` |  |

### `Range` [#range]

Numeric value must be within \[min, max\].

```csharp
public static IValidator Range(double min, double max, string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `min` | `double` |  |  |
| `max` | `double` |  |  |
| `message` | `string?` | `null` |  |

### `Required` [#required]

Value must be non-null, non-empty string, and non-default.

```csharp
public static IValidator Required(string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `message` | `string?` | `null` |  |

### `Url` [#url]

String must be a valid URL.

```csharp
public static IValidator Url(string? message = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `message` | `string?` | `null` |  |
