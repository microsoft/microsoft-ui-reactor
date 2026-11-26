---
title: "Docking strings"
description: "Resource keys and default strings for the docking UI."
---

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## `DockingStringKeys` [#dockingstringkeys]

String keys for the docking subsystem. The English defaults below are the source of truth — apps localize by wiring [`DockingStrings.Resolver`](#resolver) to consult their own catalog against these key strings.

```csharp
public static class DockingStringKeys
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields [#fields]

#### `DockHostLandmark` [#dockhostlandmark]

```csharp
public const string DockHostLandmark = "Docking.DockHost.Landmark"
```

#### `DropTargetCenter` [#droptargetcenter]

```csharp
public const string DropTargetCenter = "Docking.DropTarget.Center"
```

#### `DropTargetDockBottom` [#droptargetdockbottom]

```csharp
public const string DropTargetDockBottom = "Docking.DropTarget.DockBottom"
```

#### `DropTargetDockLeft` [#droptargetdockleft]

```csharp
public const string DropTargetDockLeft = "Docking.DropTarget.DockLeft"
```

#### `DropTargetDockRight` [#droptargetdockright]

```csharp
public const string DropTargetDockRight = "Docking.DropTarget.DockRight"
```

#### `DropTargetDockTop` [#droptargetdocktop]

```csharp
public const string DropTargetDockTop = "Docking.DropTarget.DockTop"
```

#### `DropTargetHostLandmark` [#droptargethostlandmark]

```csharp
public const string DropTargetHostLandmark = "Docking.DropTarget.HostLandmark"
```

#### `DropTargetSplitBottom` [#droptargetsplitbottom]

```csharp
public const string DropTargetSplitBottom = "Docking.DropTarget.SplitBottom"
```

#### `DropTargetSplitLeft` [#droptargetsplitleft]

```csharp
public const string DropTargetSplitLeft = "Docking.DropTarget.SplitLeft"
```

#### `DropTargetSplitRight` [#droptargetsplitright]

```csharp
public const string DropTargetSplitRight = "Docking.DropTarget.SplitRight"
```

#### `DropTargetSplitTop` [#droptargetsplittop]

```csharp
public const string DropTargetSplitTop = "Docking.DropTarget.SplitTop"
```

#### `FloatingWindowDefaultTitle` [#floatingwindowdefaulttitle]

```csharp
public const string FloatingWindowDefaultTitle = "Docking.FloatingWindow.DefaultTitle"
```

#### `LayoutRestoreFailed` [#layoutrestorefailed]

```csharp
public const string LayoutRestoreFailed = "Docking.Error.LayoutRestoreFailed"
```

#### `LiveClosed` [#liveclosed]

```csharp
public const string LiveClosed = "Docking.LiveRegion.Closed"
```

#### `LiveDocked` [#livedocked]

```csharp
public const string LiveDocked = "Docking.LiveRegion.Docked"
```

#### `LiveFloated` [#livefloated]

```csharp
public const string LiveFloated = "Docking.LiveRegion.Floated"
```

#### `LiveHidden` [#livehidden]

```csharp
public const string LiveHidden = "Docking.LiveRegion.Hidden"
```

#### `LivePinned` [#livepinned]

```csharp
public const string LivePinned = "Docking.LiveRegion.Pinned"
```

#### `LiveShown` [#liveshown]

```csharp
public const string LiveShown = "Docking.LiveRegion.Shown"
```

#### `MenuAutoHide` [#menuautohide]

```csharp
public const string MenuAutoHide = "Docking.Menu.AutoHide"
```

#### `MenuClose` [#menuclose]

```csharp
public const string MenuClose = "Docking.Menu.Close"
```

#### `MenuFloat` [#menufloat]

```csharp
public const string MenuFloat = "Docking.Menu.Float"
```

#### `MenuHide` [#menuhide]

```csharp
public const string MenuHide = "Docking.Menu.Hide"
```

#### `MenuMoveToNextGroup` [#menumovetonextgroup]

```csharp
public const string MenuMoveToNextGroup = "Docking.Menu.MoveToNextGroup"
```

#### `MenuPinToSide` [#menupintoside]

```csharp
public const string MenuPinToSide = "Docking.Menu.PinToSide"
```

#### `NavigatorHeadingActive` [#navigatorheadingactive]

```csharp
public const string NavigatorHeadingActive = "Docking.Navigator.Active"
```

#### `NavigatorHeadingDocuments` [#navigatorheadingdocuments]

```csharp
public const string NavigatorHeadingDocuments = "Docking.Navigator.Documents"
```

#### `NavigatorHeadingToolWindows` [#navigatorheadingtoolwindows]

```csharp
public const string NavigatorHeadingToolWindows = "Docking.Navigator.ToolWindows"
```

#### `SidePinTooltipPrefix` [#sidepintooltipprefix]

```csharp
public const string SidePinTooltipPrefix = "Docking.SidePin.Tooltip"
```

## `DockingStrings` [#dockingstrings]

Localization router for docking strings. Apps wire this once at startup (typically in their app-startup code that has access to the active `IntlAccessor` via context). When no resolver is installed, callers get the English default — adequate for development, English-only deployments, and headless test contexts.

```csharp
public static class DockingStrings
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks]

Spec 045 §2.21. The contract:

- `Resolver` is invoked with the key alone (e.g. `Docking.DropTarget.Center`). It returns the localized string, or null to fall back to the English default, which [`DockingStrings.Get`](#get) supplies itself.
- The resolver is called from the UI thread during render / hit-test paths — keep it allocation-light and side-effect-free.
- Apps wiring a resolver supply a stable delegate (not a fresh allocation per render); a typical pattern captures the `IntlAccessor` once and calls `accessor.Message(MessageKey)` inside.

### Properties [#properties]

#### `Resolver` [#resolver]

Optional resolver invoked for every docking string. Returns the localized string for the given key, or null to fall back to the English default.

```csharp
public static Func<string, string?>? Resolver  // get, set
```

### Methods [#methods]

#### `Get` [#get]

Resolves `key` via [`DockingStrings.Resolver`](#resolver) when installed, else returns the English default associated with the key.

```csharp
public static string Get(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` | A constant from [`DockingStringKeys`](#dockingstringkeys). |

**Remarks**

A thrown resolver is swallowed and falls back to the English default. The docking subsystem calls [`DockingStrings.Get`](#get) from hot paths (drop-target overlay hit-test, side-strip tooltip, live-region announcer) where an unhandled exception would crash the host. Resolver authors that need failure observability should log inside their resolver before throwing — matches `ReactorTrace`'s subscriber-isolation posture.

#### `LiveAnnouncement` [#liveannouncement]

Resolves a live-region announcement template via [`DockingStrings.Resolver`](#resolver) and substitutes the \`\{paneTitle\}\` placeholder. Returns empty when the key is unknown so callers can no-op silently.

```csharp
public static string LiveAnnouncement(string key, string? paneTitle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `paneTitle` | `string?` |  |

**Remarks**

Spec 045 §2.10. Keys are \`Docking.LiveRegion.\*\` constants on [`DockingStringKeys`](#dockingstringkeys).

#### `SidePinTooltip` [#sidepintooltip]

Returns the localized side-pin tooltip ("Show \{paneTitle\}"). Routes through [`DockingStrings.Resolver`](#resolver) with the [`DockingStringKeys.SidePinTooltipPrefix`](#sidepintooltipprefix) key when installed; otherwise produces the English default.

```csharp
public static string SidePinTooltip(string paneTitle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `paneTitle` | `string` |  |

**Remarks**

Resolvers that wire through ICU MessageFormat should expect the placeholder text containing `{paneTitle}`; this helper performs the substitution after resolving.
