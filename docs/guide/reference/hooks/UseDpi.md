# UseDpi

Returns the current per-window DPI ([`ReactorWindow.Dpi`](../hosting/ReactorWindow.md#dpi)) and re-renders on DPI change. Returns the system primary-monitor DPI when called outside a window. (spec 036 §5.2)

```csharp
public uint UseDpi()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected uint UseDpi()
```
