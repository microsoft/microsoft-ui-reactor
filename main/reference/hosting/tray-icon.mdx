---
title: "Tray icon"
description: "Types for an icon in the notification area."
---

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows)

## `ReactorTrayIcon` [#reactortrayicon]

Top-level system-tray icon — a peer of [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow). Created via [`ReactorApp.OpenTrayIcon`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#opentrayicon). Provides the icon bitmap, tooltip, and click events; reconciles a Reactor [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) as its flyout via [`ReactorTrayIcon.ShowFlyout`](#showflyout). (spec 036 §11.4)

```csharp
public sealed class ReactorTrayIcon : IDisposable
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks]

Public mutators must run on the UI thread captured by [`ReactorApp.UIDispatcher`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#uidispatcher). Disposal is idempotent — a second [`ReactorTrayIcon.Close`](#close) / [`ReactorTrayIcon.Dispose`](#dispose) call is a no-op.

The flyout-content reconciliation runs in a [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext) without an owning [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow); hooks like [`UseWindow`](/microsoft-ui-reactor/main/reference/hooks/UseWindow) return their documented fallback values (spec 036 §7.1).

### Properties [#properties]

#### `Icon` [#icon]

The icon bitmap source. Mutating triggers a shell re-apply.

```csharp
public WindowIcon Icon  // get, set
```

#### `Id` [#id]

Stable id, e.g. `"tray-1"`. Allocated monotonically per process.

```csharp
public string Id  // get
```

#### `IsVisible` [#isvisible]

Whether the icon is currently shown. Mutating triggers a shell re-apply.

```csharp
public bool IsVisible  // get, set
```

#### `Key` [#key]

Optional stable identity (from [`TrayIconSpec.Key`](#key_1)).

```csharp
public WindowKey? Key  // get
```

#### `Spec` [#spec]

Last-applied [`TrayIconSpec`](#trayiconspec) snapshot.

```csharp
public TrayIconSpec Spec  // get
```

#### `Tooltip` [#tooltip]

Tooltip text. Mutating triggers a shell re-apply.

```csharp
public string Tooltip  // get, set
```

### Methods [#methods]

#### `Close` [#close]

Remove the icon from the tray and dispose. UI-thread only. Idempotent.

```csharp
public void Close()
```

#### `Dispose` [#dispose]

Releases the resources that this object holds.

```csharp
public void Dispose()
```

#### `HideFlyout` [#hideflyout]

Dismiss the tray flyout. UI-thread only. Idempotent.

```csharp
public void HideFlyout()
```

#### `ShowFlyout` [#showflyout]

Show a Reactor [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) as the tray icon's flyout. UI-thread only. The flyout reconciles in a context without an owning window ([`UseWindow`](/microsoft-ui-reactor/main/reference/hooks/UseWindow) returns null inside). Light-dismisses automatically when the user clicks outside it or presses Escape. (spec 036 §7.1, §11.4)

```csharp
public void ShowFlyout(Element flyoutContent)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `flyoutContent` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

#### `Update` [#update]

Diff `next` against the current spec and re-apply only changed fields. UI-thread only.

```csharp
public void Update(TrayIconSpec next)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `next` | [`TrayIconSpec`](#trayiconspec) |  |

### Events [#events]

#### `Click` [#click]

Fires on the UI thread when the user left-clicks the icon.

**Security:** see [`ReactorTrayIcon.DoubleClick`](#doubleclick) remarks. Treat click handlers as triggers for reversible UI actions only.

```csharp
public event EventHandler? Click
```

#### `DoubleClick` [#doubleclick]

Fires on the UI thread when the user double-clicks the icon.

**Security — clicks are not authenticated:** the tray click callback arrives as a Win32 `WM_APP+1` message that any process at the same Integrity Level can synthesise via `PostMessage` to the hidden tray window. Use click handlers for reversible UI actions only (open a window, toggle state, show a flyout); gate any privileged or destructive operation behind a deliberate in-app confirmation step. (W-6, threat model 2026-05-08.)

```csharp
public event EventHandler? DoubleClick
```

#### `RightClick` [#rightclick]

Fires on the UI thread when the user right-clicks the icon.

**Security:** see [`ReactorTrayIcon.DoubleClick`](#doubleclick) remarks.

```csharp
public event EventHandler? RightClick
```

## `TrayIconSpec` [#trayiconspec]

Immutable (init-only) declarative description of a system-tray icon. Hand to [`ReactorApp.OpenTrayIcon`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#opentrayicon) to register; hand to [`ReactorTrayIcon.Update`](#update) to diff and re-apply only changed fields. (spec 036 §11.4)

```csharp
public sealed record TrayIconSpec(WindowIcon Icon, string Tooltip, WindowKey? Key = null, bool IsVisible = true)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors [#constructors]

#### `TrayIconSpec` [#trayiconspec_1]

Immutable (init-only) declarative description of a system-tray icon. Hand to [`ReactorApp.OpenTrayIcon`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#opentrayicon) to register; hand to [`ReactorTrayIcon.Update`](#update) to diff and re-apply only changed fields. (spec 036 §11.4)

```csharp
public TrayIconSpec(WindowIcon Icon, string Tooltip, WindowKey? Key = null, bool IsVisible = true)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Icon` | [`WindowIcon`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) |  | The icon to display in the notification area. Must be a filesystem path ([`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath)) pointing at an `.ico`, or icon data held in memory ([`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) / [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba)). Packaged resources ([`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource)) are **not** usable here: the notification area needs a raw `HICON`, which is loaded via `LoadImageW` or `CreateIconFromResourceEx`, and neither can read an `ms-appx:///` URI. A resource icon is skipped with a diagnostic — ship a sidecar `.ico` alongside the app, or hand the bytes to [`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) instead. See `ReactorTrayIcon.LoadHIcon`. |
| `Tooltip` | `string` |  | User-visible tooltip that surfaces as the icon's accessible name to Narrator / UIA. (spec 036 §0.6) |
| `Key` | `WindowKey?` | `null` | Optional stable identity for `UseTrayIcon` reuse and shell lookups. |
| `IsVisible` | `bool` | `true` | Whether the icon is currently shown. Hidden icons are still registered with the shell but suppressed via `NIS_HIDDEN`. |

### Properties [#properties_1]

#### `Icon` [#icon_1]

The icon to display in the notification area. Must be a filesystem path ([`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath)) pointing at an `.ico`, or icon data held in memory ([`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) / [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba)). Packaged resources ([`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource)) are **not** usable here: the notification area needs a raw `HICON`, which is loaded via `LoadImageW` or `CreateIconFromResourceEx`, and neither can read an `ms-appx:///` URI. A resource icon is skipped with a diagnostic — ship a sidecar `.ico` alongside the app, or hand the bytes to [`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) instead. See `ReactorTrayIcon.LoadHIcon`.

```csharp
public WindowIcon Icon  // get, init
```

#### `IsVisible` [#isvisible_1]

Whether the icon is currently shown. Hidden icons are still registered with the shell but suppressed via `NIS_HIDDEN`.

```csharp
public bool IsVisible  // get, init
```

#### `Key` [#key_1]

Optional stable identity for `UseTrayIcon` reuse and shell lookups.

```csharp
public WindowKey? Key  // get, init
```

#### `Tooltip` [#tooltip_1]

User-visible tooltip that surfaces as the icon's accessible name to Narrator / UIA. (spec 036 §0.6)

```csharp
public string Tooltip  // get, init
```
