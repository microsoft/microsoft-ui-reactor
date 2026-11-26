---
title: "Window events"
description: "Event arguments raised by a Reactor window."
---

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced)

## `WindowCloseReason` [#windowclosereason]

Why the window is closing. Carried on [`WindowClosingEventArgs.Reason`](#reason). (spec 036 §4.2)

```csharp
public enum WindowCloseReason
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `UserClosed` | `0` | The user closed the window via the system menu, caption button, or Alt+F4. |
| `AppClosed` | `1` | App code called [`ReactorWindow.Close`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#close) or [`ReactorApp.Exit`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#exit). |
| `OwnerClosed` | `2` | The window's owner closed and is cascading to its owned windows. |

## `WindowClosingEventArgs` [#windowclosingeventargs]

Event payload for [`ReactorWindow.Closing`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#closing). Set [`WindowClosingEventArgs.Cancel`](#cancel) to `true` to abort the close. (spec 036 §4.2)

```csharp
public sealed class WindowClosingEventArgs : EventArgs
```

Namespace: `Microsoft.UI.Reactor`. Inherits from [`EventArgs`](https://learn.microsoft.com/dotnet/api/system.eventargs).

### Constructors [#constructors]

#### `WindowClosingEventArgs` [#windowclosingeventargs_1]

Construct with the originating reason.

```csharp
public WindowClosingEventArgs(WindowCloseReason reason)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `reason` | [`WindowCloseReason`](#windowclosereason) |  |

### Properties [#properties]

#### `Cancel` [#cancel]

Set to `true` to keep the window open. Multiple subscribers may stack — once any handler sets this to true, the close is cancelled.

```csharp
public bool Cancel  // get, set
```

#### `Reason` [#reason]

Why the window is closing.

```csharp
public WindowCloseReason Reason  // get
```

## `WindowDipPositionChangedEventArgs` [#windowdippositionchangedeventargs]

Event payload for [`ReactorWindow.PositionChanged`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#positionchanged).

```csharp
public sealed class WindowDipPositionChangedEventArgs : EventArgs
```

Namespace: `Microsoft.UI.Reactor`. Inherits from [`EventArgs`](https://learn.microsoft.com/dotnet/api/system.eventargs).

### Constructors [#constructors_1]

#### `WindowDipPositionChangedEventArgs` [#windowdippositionchangedeventargs_1]

Construct from a DIP top-left position.

```csharp
public WindowDipPositionChangedEventArgs((double X, double Y) position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `position` | `(double X, double Y)` |  |

### Properties [#properties_1]

#### `Position` [#position]

New DIP top-left position of the window.

```csharp
public (double X, double Y) Position  // get
```

## `WindowDipSizeChangedEventArgs` [#windowdipsizechangedeventargs]

Event payload for [`ReactorWindow.SizeChanged`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#sizechanged). Carries the new DIP size and the underlying WinUI args for escape hatches. (spec 036 §4.2)

```csharp
public sealed class WindowDipSizeChangedEventArgs : EventArgs
```

Namespace: `Microsoft.UI.Reactor`. Inherits from [`EventArgs`](https://learn.microsoft.com/dotnet/api/system.eventargs).

### Constructors [#constructors_2]

#### `WindowDipSizeChangedEventArgs` [#windowdipsizechangedeventargs_1]

Construct from a DIP size and the originating WinUI args.

```csharp
public WindowDipSizeChangedEventArgs((double Width, double Height) size, WindowSizeChangedEventArgs? raw)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `size` | `(double Width, double Height)` |  |
| `raw` | `WindowSizeChangedEventArgs?` |  |

### Properties [#properties_2]

#### `Raw` [#raw]

Underlying WinUI args. Use only when the DIP-shaped [`WindowDipSizeChangedEventArgs.Size`](#size) is insufficient.

```csharp
public WindowSizeChangedEventArgs? Raw  // get
```

#### `Size` [#size]

New size of the window's content region in DIPs.

```csharp
public (double Width, double Height) Size  // get
```

## `WindowZOrderChangedEventArgs` [#windowzorderchangedeventargs]

Event payload for [`ReactorWindow.ZOrderChanged`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#zorderchanged).

```csharp
public sealed class WindowZOrderChangedEventArgs : EventArgs
```

Namespace: `Microsoft.UI.Reactor`. Inherits from [`EventArgs`](https://learn.microsoft.com/dotnet/api/system.eventargs).

### Constructors [#constructors_3]

#### `WindowZOrderChangedEventArgs` [#windowzorderchangedeventargs_1]

Construct from z-order hint flags.

```csharp
public WindowZOrderChangedEventArgs(bool isCovered, bool movedToTop)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `isCovered` | `bool` |  |
| `movedToTop` | `bool` |  |

### Properties [#properties_3]

#### `IsCovered` [#iscovered]

True when Win32 reported this window was inserted behind another HWND. This is a covered hint, not a pixel-accurate occlusion guarantee.

```csharp
public bool IsCovered  // get
```

#### `MovedToTop` [#movedtotop]

True when Win32 reported this window moved to the top of its z-order tier.

```csharp
public bool MovedToTop  // get
```
