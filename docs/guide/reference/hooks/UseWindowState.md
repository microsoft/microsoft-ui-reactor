# UseWindowState

Subscribes to the host window's `StateChanged` event and re-renders on change. Returns [`WindowState.Normal`](../hosting/window-options.md#values_10) outside a window. (spec 036 §7)

```csharp
public WindowState UseWindowState()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected WindowState UseWindowState()
```
