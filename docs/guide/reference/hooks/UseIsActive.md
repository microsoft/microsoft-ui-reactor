# UseIsActive

Subscribes to the host window's activation events and re-renders on change. Returns `true` outside a window (the surface is "active" while shown). (spec 036 §7)

```csharp
public bool UseIsActive()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected bool UseIsActive()
```
