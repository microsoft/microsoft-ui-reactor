# Optional\<T\>

Represents either an explicit value or an unset state.

```csharp
public readonly struct Optional<T>
```

Namespace: `Microsoft.UI.Reactor`.

## Type parameters

| Name | Description |
| --- | --- |
| `T` | The type of value stored when the optional has a value. |

## Properties

### `HasValue`

Gets a value indicating whether this optional contains an explicit value.

```csharp
public bool HasValue  // get
```

### `Unset`

Gets the unset value. Equivalent to `default(Optional<T>)`.

```csharp
public static Optional<T> Unset  // get
```

### `Value`

Gets the stored value when [`Optional.HasValue`](#hasvalue) is true.

```csharp
public T Value  // get
```

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) | Thrown when this optional is unset. |

## Methods

### `Equals`

#### `Equals(Optional<T>)`

Determines whether this optional and another optional represent the same state and value.

```csharp
public bool Equals(Optional<T> other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`Optional<T>`](Optional.md) | The optional to compare with this optional. |

**Returns:** `true` when both optionals are unset or both contain equal values; otherwise `false`.

#### `Equals(object?)`

Determines whether this optional and another object represent the same state and value.

```csharp
public override bool Equals(object? obj)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `obj` | `object?` | The object to compare with this optional. |

**Returns:** `true` when `obj` is an equal optional; otherwise `false`.

### `GetHashCode`

Returns the hash code for this optional.

```csharp
public override int GetHashCode()
```

**Returns:** A hash code that distinguishes unset from an explicit default value.

### `GetValueOrDefault`

#### `GetValueOrDefault()`

Gets the stored value, or `default` when this optional is unset.

```csharp
public T GetValueOrDefault()
```

**Returns:** The stored value when set; otherwise `default`.

#### `GetValueOrDefault(T)`

Gets the stored value, or the supplied fallback when this optional is unset.

```csharp
public T GetValueOrDefault(T fallback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fallback` | `T` | The value to return when this optional is unset. |

**Returns:** The stored value when set; otherwise `fallback`.

### `Of`

Creates an optional that contains an explicit value.

```csharp
public static Optional<T> Of(T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `T` | The value to store. For reference types, `null` is an explicit value. |

**Returns:** An optional containing `value`.

### `ToString`

Returns a string representation of this optional.

```csharp
public override string ToString()
```

**Returns:** `Unset` when unset; otherwise the stored value's string representation or `null`.

## Operators

### `op_Equality`

Determines whether two optionals represent the same state and value.

```csharp
public static bool operator ==(Optional<T> left, Optional<T> right)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `left` | [`Optional<T>`](Optional.md) | The left optional. |
| `right` | [`Optional<T>`](Optional.md) | The right optional. |

**Returns:** `true` when the optionals are equal; otherwise `false`.

### `op_Implicit`

Converts a value to an optional that explicitly contains that value. For reference-type optionals, `with { Background = null }` becomes `Optional<T>.Of(null)`, not [`Optional.Unset`](#unset); use [`Optional.Unset`](#unset) to express the unset state.

```csharp
public static implicit operator Optional<T>(T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `T` | The value to store. For reference types, `null` is an explicit value. |

### `op_Inequality`

Determines whether two optionals represent different states or values.

```csharp
public static bool operator !=(Optional<T> left, Optional<T> right)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `left` | [`Optional<T>`](Optional.md) | The left optional. |
| `right` | [`Optional<T>`](Optional.md) | The right optional. |

**Returns:** `true` when the optionals are not equal; otherwise `false`.
