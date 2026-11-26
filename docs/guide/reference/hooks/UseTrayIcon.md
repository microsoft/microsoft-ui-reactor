# UseTrayIcon

Open (or reuse-by-[`TrayIconSpec.Key`](../hosting/tray-icon.md#key_1)) a system-tray icon scoped to the calling component. The icon closes automatically on unmount — that's the only difference from [`ReactorApp.OpenTrayIcon`](../hosting/ReactorApp.md#opentrayicon), which is app-scoped and keeps the icon alive until explicit [`ReactorTrayIcon.Close`](../hosting/tray-icon.md#close).

```csharp
public ReactorTrayIcon? UseTrayIcon(TrayIconSpec spec)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Desktop integration](../../desktop-integration.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `spec` | [`TrayIconSpec`](../hosting/tray-icon.md#trayiconspec) |  |

## Remarks

Returns `null` when no UI dispatcher has been captured (test contexts) or when the spec change cannot be reconciled — callers should null-check before subscribing to events. Identity-stable across re-renders so the same handle wires through subsequent `UseEffect` dependencies. (spec 036 §11.4)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected ReactorTrayIcon? UseTrayIcon(TrayIconSpec spec)
```
