# ThemeRef

A reference to a WinUI theme resource that resolves at render time. Use via [`Theme`](Theme.md) tokens or [`Theme.Ref`](Theme.md#ref) for custom keys.

```csharp
public readonly record struct ThemeRef(string ResourceKey)
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Theming Tokens](../../theming-tokens.md)

## Constructors

### `ThemeRef`

A reference to a WinUI theme resource that resolves at render time. Use via [`Theme`](Theme.md) tokens or [`Theme.Ref`](Theme.md#ref) for custom keys.

```csharp
public ThemeRef(string ResourceKey)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ResourceKey` | `string` |  |

## Properties

### `ResourceKey`

```csharp
public string ResourceKey  // get, init
```

## Methods

### `Resolve`

Resolves a theme resource using an explicit isDark flag. Useful for resolving during Render() before controls are in the tree.

```csharp
public static Brush? Resolve(string resourceKey, bool isDark)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `resourceKey` | `string` |  |
| `isDark` | `bool` |  |
