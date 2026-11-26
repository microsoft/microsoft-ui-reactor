# UseOpenWindow

Open or reuse a secondary window keyed by `key`. Renders that pass the same `key` share the same [`ReactorWindow`](../hosting/ReactorWindow.md); if the spec changes across renders the live window is updated via [`ReactorWindow.Update`](../hosting/ReactorWindow.md#update). The returned handle is identity-stable across renders so long as the key is stable.

```csharp
public ReactorWindow? UseOpenWindow(WindowKey key, WindowSpec spec, Func<Component> factory)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`WindowKey`](../hosting/WindowKey.md) |  |
| `spec` | [`WindowSpec`](../hosting/WindowSpec.md) |  |
| `factory` | `Func<Component>` |  |

## Remarks

Unmount semantics: when the calling component unmounts, the opened window stays open. Components that want the inverse behavior must close the window explicitly — e.g. by registering a `UseEffect` cleanup that calls [`ReactorWindow.Close`](../hosting/ReactorWindow.md#close) on the returned handle. (spec 036 §4.3 / §15.6)

**Note — asymmetry with [`UseTrayIcon`](UseTrayIcon.md):** tray icons are component-scoped and close on unmount; opened windows are app-scoped and survive unmount. The asymmetry is deliberate (a window is normally expected to outlive the menu item that opened it, while a tray icon belongs to the component that declared it), so the two hooks behave inversely despite the matching naming.

Returns `null` when no UI dispatcher has been captured — happens in unit-test contexts where no `ReactorApp.Run` is in flight. In production this is unreachable.

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected ReactorWindow? UseOpenWindow(WindowKey key, WindowSpec spec, Func<Component> factory)
```
