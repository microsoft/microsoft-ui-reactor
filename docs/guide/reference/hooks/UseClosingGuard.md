# UseClosingGuard

Registers a synchronous "can the window close right now?" predicate. Multiple guards stack — any returning `false` cancels the close. Runs on the UI thread; for async confirmation, return `false` and re-trigger [`ReactorWindow.Close`](../hosting/ReactorWindow.md#close) from the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)

```csharp
public void UseClosingGuard(Func<bool> canClose)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `canClose` | `Func<bool>` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected void UseClosingGuard(Func<bool> canClose)
```
