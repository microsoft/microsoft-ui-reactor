---
title: "WindowKey"
seo:
  description: "Stable identity for a top-level surface within a process. Used by ReactorApp.FindWindow, WindowSpec.Key, and the UseOpenWindow hook to dedupe re-renders that should reuse a window rather than open a new one. (spec 036 §4.4)"
---

Stable identity for a top-level surface within a process. Used by [`ReactorApp.FindWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#findwindow), [`WindowSpec.Key`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#key), and the `UseOpenWindow` hook to dedupe re-renders that should reuse a window rather than open a new one. (spec 036 §4.4)

```csharp
public readonly record struct WindowKey(string Name)
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced)

## Remarks [#remarks]

Identity is by ordinal name within the current process. Implicit conversion from `string` exists so call sites can pass a literal directly:

```csharp
ReactorApp.FindWindow("settings")
```

.

## Constructors [#constructors]

### `WindowKey` [#windowkey_1]

Stable identity for a top-level surface within a process. Used by [`ReactorApp.FindWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#findwindow), [`WindowSpec.Key`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#key), and the `UseOpenWindow` hook to dedupe re-renders that should reuse a window rather than open a new one. (spec 036 §4.4)

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

## Properties [#properties]

### `Name` [#name]

```csharp
public string Name  // get, init
```

## Methods [#methods]

### `Of` [#of]

Construct a key from a name. Throws when `name` is null or empty.

```csharp
public static WindowKey Of(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

## Operators [#operators]

### `op_Implicit` [#op_implicit]

Implicit conversion from `string` for ergonomic call sites.

```csharp
public static implicit operator WindowKey(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |
