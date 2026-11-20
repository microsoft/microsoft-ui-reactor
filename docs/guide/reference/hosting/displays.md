# Displays

Information about the connected displays.

**Learn more:** [Advanced Windowing](../../windowing-advanced.md), [Windows](../../windows.md)

## `DisplayInfo`

Snapshot of a physical display projected into Reactor's DIP-oriented windowing model.

```csharp
public sealed record DisplayInfo
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors

#### `DisplayInfo`

```csharp
public DisplayInfo()
```

> **Obsolete.** Constructors of types with required members are not supported in this version of your compiler.

### Properties

#### `BoundsDip`

Full monitor bounds in DIPs, converted with this monitor's DPI. On mixed-DPI desktops, non-primary X/Y values are approximate.

```csharp
public Rect BoundsDip  // get, init
```

#### `Dpi`

Effective monitor DPI (96 at 100%, 144 at 150%, etc.).

```csharp
public uint Dpi  // get, init
```

#### `Id`

Stable monitor id from Win32 `MONITORINFOEX.szDevice` (for example, `\\.\DISPLAY1`).

```csharp
public string Id  // get, init
```

#### `IsPrimary`

Whether this display is the primary monitor.

```csharp
public bool IsPrimary  // get, init
```

#### `WorkAreaDip`

Work area in DIPs, converted with this monitor's DPI. On mixed-DPI desktops, non-primary X/Y values are approximate because Windows exposes virtual-screen coordinates in physical pixels, not a single global DIP space.

```csharp
public Rect WorkAreaDip  // get, init
```

## `ReactorDisplay`

Process-wide display enumeration helpers for Reactor windowing.

```csharp
public static class ReactorDisplay
```

Namespace: `Microsoft.UI.Reactor`.

### Properties

#### `Displays`

Snapshot of currently active displays. Bounds and work areas are projected into DIPs using each monitor's own DPI; on mixed-DPI desktops non-primary X/Y values are approximate because Windows has no single global DIP space.

```csharp
public static IReadOnlyList<DisplayInfo> Displays  // get
```

#### `Primary`

The primary display in the current layout.

```csharp
public static DisplayInfo Primary  // get
```

### Methods

#### `NearestTo`

Returns the display whose DIP bounds contain `dipX` / `dipY`, or the nearest display when the point is outside all bounds.

```csharp
public static DisplayInfo NearestTo(double dipX, double dipY)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dipX` | `double` |  |
| `dipY` | `double` |  |

### Events

#### `DisplayLayoutChanged`

Fires on the UI thread when Windows reports a display layout change.

```csharp
public static event EventHandler? DisplayLayoutChanged
```
