# UseReducedMotion

Returns `true` when the user or system prefers reduced motion (e.g., Windows "Show animations" is off, or `SPI_GETCLIENTAREAANIMATION` returns false). Automatically re-renders the component when the preference changes.

Use this to skip entrance/exit animations, disable pan inertia, terminate force-graph simulations immediately, and keep only ≤ 150 ms opacity fades (WCAG 2.3.3).

The value is seeded during the first render and then tracked live through `UISettings.AnimationsEnabledChanged`. That event needs Windows 10 2004 (19041); on older builds the preference is re-read whenever a theme or palette change arrives instead, so it updates on the next such notification rather than immediately.

```csharp
public bool UseReducedMotion()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Accessibility](../../accessibility.md), [Animation](../../animation.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected bool UseReducedMotion()
```
