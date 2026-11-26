# UseHighContrastScheme

Returns the high contrast scheme name (e.g., "High Contrast Black", "High Contrast White") or `null` if not in high contrast mode. Automatically re-renders the component when the scheme changes.

Must be called instead of (not in addition to) [`UseHighContrast`](UseHighContrast.md) because each consumes the same hook slots. Use one or the other.

```csharp
public string? UseHighContrastScheme()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Accessibility](../../accessibility.md), [Theming Tokens](../../theming-tokens.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected string? UseHighContrastScheme()
```
