# UseWindow

Resolves the [`ReactorWindow`](../hosting/ReactorWindow.md) whose host renders this component — including nested components and virtualized rows realized outside the host's render pass — or `null` when it has no owning window (tray-flyout content, `ReactorHostControl` embeds). O(1) field read; no subscription, no re-render trigger. (spec 036 §7 / §7.1)

```csharp
public ReactorWindow? UseWindow()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected ReactorWindow? UseWindow()
```
