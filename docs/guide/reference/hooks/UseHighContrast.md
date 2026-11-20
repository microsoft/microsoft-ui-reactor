# UseHighContrast

Returns `true` when the system is in a High Contrast (forced colors) theme. Automatically re-renders the component when high contrast is toggled.

Use this to conditionally override custom styling (hardcoded backgrounds, foregrounds, border colors) that would ignore forced-colors mode. WinUI controls using ThemeResource brushes adapt automatically — this hook is for Reactor components that use explicit color values.

```csharp
public bool UseHighContrast()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Accessibility](../../accessibility.md), [Theming Tokens](../../theming-tokens.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected bool UseHighContrast()
```
