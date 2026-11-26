# UseFocus

Creates and returns a FocusManager scoped to this component. The manager persists across re-renders.

```csharp
public static FocusManager UseFocus(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseFocusExtensions`.

**Learn more:** [Input and gestures](../../input-and-gestures.md), [Accessibility](../../accessibility.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static FocusManager UseFocus(this Component component)
```
