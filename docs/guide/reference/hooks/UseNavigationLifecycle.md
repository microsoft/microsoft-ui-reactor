# UseNavigationLifecycle

Registers lifecycle callbacks that fire during navigation events.

- `onNavigatedTo` — fires after this page becomes active.
- `onNavigatingFrom` — fires before navigating away. Call `ctx.Cancel()` to block.
- `onNavigatedFrom` — fires after this page is no longer active.

Callbacks are always updated to the latest references on every render.

```csharp
public void UseNavigationLifecycle(Action<NavigatingToContext>? onNavigatingTo = null, Action<NavigatedToContext>? onNavigatedTo = null, Action<NavigatingFromContext>? onNavigatingFrom = null, Action<NavigatedFromContext>? onNavigatedFrom = null)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Navigation](../../navigation.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onNavigatingTo` | `Action<NavigatingToContext>?` | `null` |  |
| `onNavigatedTo` | `Action<NavigatedToContext>?` | `null` |  |
| `onNavigatingFrom` | `Action<NavigatingFromContext>?` | `null` |  |
| `onNavigatedFrom` | `Action<NavigatedFromContext>?` | `null` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected void UseNavigationLifecycle(Action<NavigatingToContext>? onNavigatingTo = null, Action<NavigatedToContext>? onNavigatedTo = null, Action<NavigatingFromContext>? onNavigatingFrom = null, Action<NavigatedFromContext>? onNavigatedFrom = null)
```
