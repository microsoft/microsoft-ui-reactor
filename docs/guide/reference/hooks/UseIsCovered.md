# UseIsCovered

Returns a covered hint from the host window's z-order transitions and re-renders on change. The value is not pixel-accurate occlusion state.

```csharp
public bool UseIsCovered()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected bool UseIsCovered()
```
