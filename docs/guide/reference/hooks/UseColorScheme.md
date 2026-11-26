# UseColorScheme

Returns the app-global [`ColorScheme`](../theming/enums.md#colorscheme) — [`ColorScheme.Light`](../theming/enums.md#values_1) or [`ColorScheme.Dark`](../theming/enums.md#values_1), read from `Application.Current.RequestedTheme`.

The value is re-evaluated on every render — when the app theme changes, [`ReactorHost`](../hosting/ReactorHost.md) triggers a re-render so this hook naturally picks up the new value.

This hook does **not** observe per-element `RequestedTheme` overrides: it consults the application, not the calling component's position in the tree, so a component inside a `.RequestedTheme(Dark)` subtree still reports the app theme. It also never returns [`ColorScheme.HighContrast`](../theming/enums.md#values_1) in a running app — use [`UseHighContrast`](UseHighContrast.md) for forced-colors mode.

```csharp
public ColorScheme UseColorScheme()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Theming Tokens](../../theming-tokens.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected ColorScheme UseColorScheme()
```
