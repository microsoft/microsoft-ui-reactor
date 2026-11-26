---
title: "Optional<T>"
description: "Represents either an explicit value or an unset state."
sidebar:
  label: "Optional"
---

```csharp
public readonly struct Optional<T>
```

Namespace: `Microsoft.UI.Reactor`.

## Type parameters [#type-parameters]

| Name | Description |
| --- | --- |
| `T` | The type of value stored when the optional has a value. |

## Properties [#properties]

### `HasValue` [#hasvalue]

Gets a value indicating whether this optional contains an explicit value.

```csharp
public bool HasValue  // get
```

### `Unset` [#unset]

Gets the unset value. Equivalent to `default(Optional<T>)`.

```csharp
public static Optional<T> Unset  // get
```

### `Value` [#value]

Gets the stored value when [`Optional.HasValue`](#hasvalue) is true.

```csharp
public T Value  // get
```

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) | Thrown when this optional is unset. |

## Methods [#methods]

### `Equals` [#equals]

#### `Equals(Optional<T>)` [#equalsoptionalt]

Determines whether this optional and another optional represent the same state and value.

```csharp
public bool Equals(Optional<T> other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional) | The optional to compare with this optional. |

**Returns:** `true` when both optionals are unset or both contain equal values; otherwise `false`.

#### `Equals(object?)` [#equalsobject]

Determines whether this optional and another object represent the same state and value.

```csharp
public override bool Equals(object? obj)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `obj` | `object?` | The object to compare with this optional. |

**Returns:** `true` when `obj` is an equal optional; otherwise `false`.

### `GetHashCode` [#gethashcode]

Returns the hash code for this optional.

```csharp
public override int GetHashCode()
```

**Returns:** A hash code that distinguishes unset from an explicit default value.

### `GetValueOrDefault` [#getvalueordefault]

#### `GetValueOrDefault()` [#getvalueordefault_1]

Gets the stored value, or `default` when this optional is unset.

```csharp
public T GetValueOrDefault()
```

**Returns:** The stored value when set; otherwise `default`.

#### `GetValueOrDefault(T)` [#getvalueordefaultt]

Gets the stored value, or the supplied fallback when this optional is unset.

```csharp
public T GetValueOrDefault(T fallback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fallback` | `T` | The value to return when this optional is unset. |

**Returns:** The stored value when set; otherwise `fallback`.

### `Of` [#of]

Creates an optional that contains an explicit value.

```csharp
public static Optional<T> Of(T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `T` | The value to store. For reference types, `null` is an explicit value. |

**Returns:** An optional containing `value`.

### `ToString` [#tostring]

Returns a string representation of this optional.

```csharp
public override string ToString()
```

**Returns:** `Unset` when unset; otherwise the stored value's string representation or `null`.

## Operators [#operators]

### `op_Equality` [#op_equality]

Determines whether two optionals represent the same state and value.

```csharp
public static bool operator ==(Optional<T> left, Optional<T> right)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `left` | [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional) | The left optional. |
| `right` | [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional) | The right optional. |

**Returns:** `true` when the optionals are equal; otherwise `false`.

### `op_Implicit` [#op_implicit]

Converts a value to an optional that explicitly contains that value. For reference-type optionals, `with { Background = null }` becomes `Optional<T>.Of(null)`, not [`Optional.Unset`](#unset); use [`Optional.Unset`](#unset) to express the unset state.

```csharp
public static implicit operator Optional<T>(T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `T` | The value to store. For reference types, `null` is an explicit value. |

### `op_Inequality` [#op_inequality]

Determines whether two optionals represent different states or values.

```csharp
public static bool operator !=(Optional<T> left, Optional<T> right)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `left` | [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional) | The left optional. |
| `right` | [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional) | The right optional. |

**Returns:** `true` when the optionals are not equal; otherwise `false`.
