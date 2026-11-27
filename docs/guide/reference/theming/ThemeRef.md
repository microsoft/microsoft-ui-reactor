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

**Remarks**

The brush is the one a `{ThemeResource}` reference gets on an element in that theme, so either theme can be resolved whatever theme the app uses. WinUI does the lookup, so its rules apply, including the `Default` fallback. While a Windows contrast theme is on, WinUI picks the `HighContrast` dictionary whatever `isDark` says, as it does for a `{ThemeResource}` reference; each host re-renders once WinUI has applied a contrast switch, so a call in a render follows it. For a key WinUI cannot resolve for that theme (one an app dictionary defines only for some themes), Reactor reads the app's Light, Dark and Default theme dictionaries, then does a plain lookup, which uses the app's theme: a key defined only for the app's theme therefore resolves to that brush for the other theme too, and a key defined only for the other theme resolves to null for the app's theme. Returns null when the key is not found or is not a [`Brush`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.brush). Call it on the UI thread.
