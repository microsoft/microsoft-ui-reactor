# WindowKey

Stable identity for a top-level surface within a process. Used by [`ReactorApp.FindWindow`](ReactorApp.md#findwindow), [`WindowSpec.Key`](WindowSpec.md#key), and the `UseOpenWindow` hook to dedupe re-renders that should reuse a window rather than open a new one. (spec 036 §4.4)

```csharp
public readonly record struct WindowKey(string Name)
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md)

## Remarks

Identity is by ordinal name within the current process. Implicit conversion from `string` exists so call sites can pass a literal directly:

```csharp
ReactorApp.FindWindow("settings")
```

.

## Constructors

### `WindowKey`

Stable identity for a top-level surface within a process. Used by [`ReactorApp.FindWindow`](ReactorApp.md#findwindow), [`WindowSpec.Key`](WindowSpec.md#key), and the `UseOpenWindow` hook to dedupe re-renders that should reuse a window rather than open a new one. (spec 036 §4.4)

```csharp
public WindowKey(string Name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` |  |

**Remarks**

Identity is by ordinal name within the current process. Implicit conversion from `string` exists so call sites can pass a literal directly:

```csharp
ReactorApp.FindWindow("settings")
```

.

## Properties

### `Name`

```csharp
public string Name  // get, init
```

## Methods

### `Of`

Construct a key from a name. Throws when `name` is null or empty.

```csharp
public static WindowKey Of(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

## Operators

### `op_Implicit`

Implicit conversion from `string` for ergonomic call sites.

```csharp
public static implicit operator WindowKey(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |
