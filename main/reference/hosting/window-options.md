---
title: "Window options"
description: "Enums that describe the style, size and position of a window."
---

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced)

## `AspectRatioBasis` [#aspectratiobasis]

Whether [`WindowSpec.AspectRatio`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#aspectratio) applies to the outer window rectangle (caption + borders included) or to the client (content) area.

```csharp
public enum AspectRatioBasis
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks]

[`AspectRatioBasis.Window`](#values) is the simpler / cheaper mode and matches the raw shape of the OS `WM_SIZING` contract — the framework just enforces a ratio on the rect the OS hands us.

[`AspectRatioBasis.Client`](#values) is what most media/game/canvas apps want (a 1:1 client area for a square video player, a 16:9 client area for a game viewport). The framework computes the chrome inset via `AdjustWindowRectExForDpi` at the current window style + DPI, so the ratio stays accurate across DPI changes and across [`WindowStyle`](#windowstyle) flips. Has no effect when [`WindowSpec.AspectRatio`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#aspectratio) is null.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Window` | `0` | Ratio applies to the outer window rect, including caption and borders. Default. |
| `Client` | `1` | Ratio applies to the client area. The framework auto-accounts for chrome. |

## `PresenterKind` [#presenterkind]

```csharp
public enum PresenterKind
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Overlapped` | `0` | Standard chrome with caption and a system menu. |
| `FullScreen` | `1` | Borderless full-screen presentation. |
| `CompactOverlay` | `2` | Compact-overlay (PIP-style) presentation. |

## `ShutdownPolicy` [#shutdownpolicy]

Process-shutdown policy. Evaluated whenever a window or tray icon closes. (spec 036 §6.2)

```csharp
public enum ShutdownPolicy
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_2]

| Name | Value | Description |
| --- | --- | --- |
| `OnPrimaryWindowClosed` | `0` | Default. Closing the primary window exits the process. |
| `OnLastSurfaceClosed` | `1` | Exit when the last window AND the last tray icon have closed. |
| `Explicit` | `2` | Surfaces close, but the process keeps running until [`ReactorApp.Exit`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#exit) is called. |

## `WindowCornerStyle` [#windowcornerstyle]

```csharp
public enum WindowCornerStyle
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_3]

| Name | Value | Description |
| --- | --- | --- |
| `Default` | `0` | Let the OS choose the default corner style. |
| `Square` | `1` | Request square window corners. |
| `Rounded` | `2` | Request rounded window corners. |
| `RoundedSmall` | `3` | Request small rounded window corners. |

## `WindowEmbedStyle` [#windowembedstyle]

```csharp
public enum WindowEmbedStyle
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_4]

| Name | Value | Description |
| --- | --- | --- |
| `Child` | `0` |  |
| `Owner` | `1` |  |

## `WindowIconKind` [#windowiconkind]

Which kind of source a [`WindowIcon`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) was constructed from. (spec 036 §4.1)

```csharp
public enum WindowIconKind
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks_1]

Consumers differ in what they can accept, because most need a raw Win32 `HICON` while a few need a [`Uri`](https://learn.microsoft.com/dotnet/api/system.uri). See the remarks on [`WindowIcon`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) for the per-surface support matrix.

### Values [#values_5]

| Name | Value | Description |
| --- | --- | --- |
| `Path` | `0` | A filesystem path, from [`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath). |
| `Resource` | `1` | An `ms-appx:///` resource URI, from [`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource). |
| `Binary` | `2` | Icon data held in memory, from [`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) or [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba). |

## `WindowLevel` [#windowlevel]

```csharp
public enum WindowLevel
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_6]

| Name | Value | Description |
| --- | --- | --- |
| `Normal` | `0` | Normal z-order behavior. |
| `Floating` | `1` | Keep above other Reactor app windows when they activate. |
| `AlwaysOnTop` | `2` | Use the Win32 topmost tier. |

## `WindowResizeMode` [#windowresizemode]

```csharp
public enum WindowResizeMode
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_7]

| Name | Value | Description |
| --- | --- | --- |
| `CanResize` | `0` | User can drag borders; minimize and maximize buttons are enabled subject to the spec masks. |
| `NoResize` | `1` | User cannot drag borders; minimize and maximize buttons are disabled subject to the spec masks. |
| `CanMinimize` | `2` | User cannot drag borders, but the minimize button stays enabled subject to the spec mask. |

## `WindowSizeToContent` [#windowsizetocontent]

```csharp
public enum WindowSizeToContent
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_8]

| Name | Value | Description |
| --- | --- | --- |
| `Manual` | `0` | Window size is controlled manually or by the OS default. |
| `Width` | `1` | Window width tracks content desired width; height stays unchanged. |
| `Height` | `2` | Window height tracks content desired height; width stays unchanged. |
| `WidthAndHeight` | `3` | Window width and height both track content desired size. |

## `WindowStartPosition` [#windowstartposition]

Initial placement strategy for a [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow) when it opens. (spec 036 §3.2 / §4.1)

```csharp
public enum WindowStartPosition
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_9]

| Name | Value | Description |
| --- | --- | --- |
| `Default` | `0` | WinUI default placement — the OS picks based on prior windows. |
| `CenterOnPrimary` | `1` | Center the window on the primary monitor. |
| `CenterOnOwner` | `2` | Center the window on its [`WindowSpec.Owner`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#owner)'s monitor. |
| `Manual` | `4` | Place at [`WindowSpec.ManualPosition`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#manualposition). Both must be set together. |
| `CenterOnCurrent` | `5` | Center on the monitor nearest the current cursor position. |

## `WindowState` [#windowstate]

Lifecycle/state of a [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow). Exposed via [`ReactorWindow.State`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#state) and the `UseWindowState` hook. (spec 036 §3.2)

```csharp
public enum WindowState
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_10]

| Name | Value | Description |
| --- | --- | --- |
| `Normal` | `0` | Not minimized, not maximized — the default. |
| `Minimized` | `1` | Minimized to the taskbar. |
| `Maximized` | `2` | Maximized to fill the work area. |
| `FullScreen` | `3` | Borderless full-screen presentation. |
| `CompactOverlay` | `4` | Compact-overlay presentation. |

## `WindowStyle` [#windowstyle]

Coarse classifier for the WinUI [`AppWindowPresenterKind`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindowpresenterkind) presenter applied to a window. (spec 036 §3.2 / §4.1)

```csharp
public enum WindowStyle
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_11]

| Name | Value | Description |
| --- | --- | --- |
| `Default` | `0` | Standard overlapped window chrome. |
| `None` | `1` | No border, caption, or system menu. |
| `ToolWindow` | `2` | Tool-window chrome; hidden from the taskbar unless explicitly overridden. |

## `WindowTitleBarHeight` [#windowtitlebarheight]

Caption height for a window whose content extends into the title bar. Mirrors `Microsoft.UI.Windowing.TitleBarHeightOption`.

```csharp
public enum WindowTitleBarHeight
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks_2]

The native option only sizes the **system caption** (caption buttons, drag region, insets). The WinUI `TitleBar` control does not follow it — see `TitleBarElement.HeightOption`, which sizes both.

### Values [#values_12]

| Name | Value | Description |
| --- | --- | --- |
| `Standard` | `0` | Standard 32 DIP caption. |
| `Tall` | `1` | Tall 48 DIP caption — the layout used when the title bar hosts navigation chrome. |
| `Collapsed` | `2` | No caption area at all. |

## `WindowTitleBarTheme` [#windowtitlebartheme]

Theme of the window's system caption (the minimize / maximize / close buttons). Mirrors `Microsoft.UI.Windowing.TitleBarTheme`. (issue #1297)

```csharp
public enum WindowTitleBarTheme
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks_3]

WinUI does not carry an element's `RequestedTheme` through to the caption, so an app that themes its content opposite to the system declares the caption theme too — see [`WindowSpec.TitleBarTheme`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#titlebartheme) and `TitleBarElement.PreferredTheme`.

### Values [#values_13]

| Name | Value | Description |
| --- | --- | --- |
| `Legacy` | `0` | Legacy caption theming. |
| `UseDefaultAppMode` | `1` | Follows the app's default mode (the system light / dark setting). |
| `Light` | `2` | Light caption buttons. |
| `Dark` | `3` | Dark caption buttons. |
