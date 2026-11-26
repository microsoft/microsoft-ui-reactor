---
title: "Displays"
description: "Information about the connected displays."
---

**Learn more:** [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced), [Windows](/microsoft-ui-reactor/main/guides/windows)

## `DisplayInfo` [#displayinfo]

Snapshot of a physical display projected into Reactor's DIP-oriented windowing model.

```csharp
public sealed record DisplayInfo
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors [#constructors]

#### `DisplayInfo` [#displayinfo_1]

```csharp
public DisplayInfo()
```

### Properties [#properties]

#### `BoundsDip` [#boundsdip]

Full monitor bounds in DIPs, converted with this monitor's DPI. On mixed-DPI desktops, non-primary X/Y values are approximate.

```csharp
public Rect BoundsDip  // get, init
```

#### `Dpi` [#dpi]

Effective monitor DPI (96 at 100%, 144 at 150%, etc.).

```csharp
public uint Dpi  // get, init
```

#### `Id` [#id]

Stable monitor id from Win32 `MONITORINFOEX.szDevice` (for example, `\\.\DISPLAY1`).

```csharp
public required string Id  // get, init
```

#### `IsPrimary` [#isprimary]

Whether this display is the primary monitor.

```csharp
public bool IsPrimary  // get, init
```

#### `WorkAreaDip` [#workareadip]

Work area in DIPs, converted with this monitor's DPI. On mixed-DPI desktops, non-primary X/Y values are approximate because Windows exposes virtual-screen coordinates in physical pixels, not a single global DIP space.

```csharp
public Rect WorkAreaDip  // get, init
```

## `ReactorDisplay` [#reactordisplay]

Process-wide display enumeration helpers for Reactor windowing.

```csharp
public static class ReactorDisplay
```

Namespace: `Microsoft.UI.Reactor`.

### Properties [#properties_1]

#### `Displays` [#displays_1]

Snapshot of currently active displays. Bounds and work areas are projected into DIPs using each monitor's own DPI; on mixed-DPI desktops non-primary X/Y values are approximate because Windows has no single global DIP space.

```csharp
public static IReadOnlyList<DisplayInfo> Displays  // get
```

#### `Primary` [#primary]

The primary display in the current layout.

```csharp
public static DisplayInfo Primary  // get
```

### Methods [#methods]

#### `NearestTo` [#nearestto]

Returns the display whose DIP bounds contain `dipX` / `dipY`, or the nearest display when the point is outside all bounds.

```csharp
public static DisplayInfo NearestTo(double dipX, double dipY)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dipX` | `double` |  |
| `dipY` | `double` |  |

### Events [#events]

#### `DisplayLayoutChanged` [#displaylayoutchanged]

Fires on the UI thread when Windows reports a display layout change.

```csharp
public static event EventHandler? DisplayLayoutChanged
```
