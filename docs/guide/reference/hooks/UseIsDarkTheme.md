# UseIsDarkTheme

Convenience wrapper — returns `true` when the app-global color scheme is [`ColorScheme.Dark`](../theming/enums.md#values_1). Exactly `UseColorScheme() == ColorScheme.Dark`, so it inherits that hook's semantics: it does not observe per-element `RequestedTheme`.

```csharp
public bool UseIsDarkTheme()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Theming Tokens](../../theming-tokens.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected bool UseIsDarkTheme()
```
