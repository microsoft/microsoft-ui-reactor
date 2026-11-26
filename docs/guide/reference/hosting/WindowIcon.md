# WindowIcon

Abstraction over [`AppWindow.SetIcon`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.seticon) and [`AppWindow.SetIcon`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.seticon). Pass to [`WindowSpec.Icon`](WindowSpec.md#icon), to a tray icon, or to a taskbar overlay. (spec 036 §4.1)

```csharp
public sealed class WindowIcon
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md)

## Remarks

Three source kinds are supported: a filesystem path ([`WindowIcon.FromPath`](#frompath)) for `.ico` resources alongside an unpackaged app, an `ms-appx:///`-style packaged-app resource URI ([`WindowIcon.FromResource`](#fromresource)), and icon data held in memory ([`WindowIcon.FromBytes`](#frombytes) / [`WindowIcon.FromRgba`](#fromrgba)). Empty strings and empty buffers are rejected at construction so a malformed icon never reaches the WinUI APIs.

Consumers differ in what they can accept, because most of them need a raw Win32 `HICON` while a couple need a [`Uri`](https://learn.microsoft.com/dotnet/api/system.uri):

- [`WindowSpec.Icon`](WindowSpec.md#icon) — [`WindowIcon.FromPath`](#frompath) and [`WindowIcon.FromResource`](#fromresource). An `ms-appx:` source is mapped to a file beside the app before it reaches `AppWindow.SetIcon`, which needs a filesystem path: handed the URI itself inside a packaged app it silently applies a default icon. A binary source is **not** accepted here — see [`WindowIcon.FromBytes`](#frombytes).
- Tray icons, taskbar overlays, and thumbnail-toolbar buttons — [`WindowIcon.FromPath`](#frompath) and the binary factories. They need an `HICON`, which comes either from `LoadImageW` on a file or from `CreateIconFromResourceEx` on in-memory data; neither can read a packaged resource URI, so [`WindowIcon.FromResource`](#fromresource) is skipped with a diagnostic.
- Jump lists — [`WindowIcon.FromResource`](#fromresource) on the packaged path (the WinRT API takes the URI directly) and [`WindowIcon.FromPath`](#frompath) on the unpackaged one. Every other kind is silently skipped.

Instances are immutable and safe to share: a binary source copies the caller's buffer, so a later write to that array cannot change what the shell renders.

## Properties

### `IsBinary`

True when constructed via [`WindowIcon.FromBytes`](#frombytes) or [`WindowIcon.FromRgba`](#fromrgba).

```csharp
public bool IsBinary  // get
```

### `IsResource`

True when constructed via [`WindowIcon.FromResource`](#fromresource).

```csharp
public bool IsResource  // get
```

### `Kind`

Which factory produced this icon.

```csharp
public WindowIconKind Kind  // get
```

### `Source`

The path or resource URI this icon was constructed from, or [`String.Empty`](https://learn.microsoft.com/dotnet/api/system.string.empty) for a [`WindowIconKind.Binary`](window-options.md#values_5) icon, which has no addressable source. Test [`WindowIcon.Kind`](#kind) rather than inferring it from here.

```csharp
public string Source  // get
```

## Methods

### `FromBytes`

Create an icon from encoded icon data held in memory — an `.ico` container, a PNG, or a bare `RT_ICON` DIB blob — so an app whose icon lives in an embedded resource, a database, or a download does not have to write a temporary file first. (issue #1185)

```csharp
public static WindowIcon FromBytes(ReadOnlySpan<byte> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `ReadOnlySpan<byte>` | The encoded bytes. Copied, so the caller may reuse or overwrite its buffer. |

**Remarks**

Consumed by the tray icon, the taskbar overlay, and thumbnail-toolbar buttons, which need a raw `HICON` and get one via `CreateIconFromResourceEx`. A multi-frame `.ico` is honoured: the frame nearest the size the surface asks for is selected, exactly as `LoadImageW` would have done from a file.

**Not accepted by [`WindowSpec.Icon`](WindowSpec.md#icon).** Putting a binary icon on a window means owning the `HICON` for the window's lifetime, which `AppWindow.SetIcon(IconId)` does not do for you. A binary icon declared there is reported as not applied, so the window falls back to `Assets\AppIcon.ico` or its PE icon rather than showing nothing. Jump lists and the `TitleBar` icon default skip it for the same reason they skip a PE icon: both need a [`Uri`](https://learn.microsoft.com/dotnet/api/system.uri).

An unrecognised signature is logged via `System.Diagnostics.Debug.WriteLine` rather than throwing, mirroring [`WindowIcon.FromPath`](#frompath)'s treatment of an unrecognised extension — the loader is the authority on what it accepts, and refusing data it would have loaded is worse than letting it report the failure.

The bytes are held for this instance's lifetime. That is the point of the API, but it is worth knowing when a long-lived spec holds a large multi-resolution `.ico`.

### `FromPath`

Create an icon from a filesystem path (typically a `.ico`) for an unpackaged app. Throws on null/empty input.

An unrecognised extension or missing file is logged via `System.Diagnostics.Debug.WriteLine` so misconfiguration is diagnosable, but no exception is raised — apps that deploy assets asynchronously, or icons that exist as a sidecar to a relocated executable, still construct successfully and surface the underlying load failure on [`WindowIcon.Apply`](WindowIcon.md). (W-4 hardening.)

```csharp
public static WindowIcon FromPath(string path)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | `string` |  |

### `FromResource`

Create an icon from a packaged-app resource URI (e.g. `ms-appx:///Assets/AppIcon.ico`). Throws on null/empty input.

The URI is mapped to a file beside the app (the package install root when packaged) before it reaches `AppWindow.SetIcon`, which requires a filesystem path. Manifest visual assets (`Square44x44Logo` and friends) are named by resource identifier rather than filename and are not addressable this way.

```csharp
public static WindowIcon FromResource(string uri)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `uri` | `string` |  |

### `FromRgba`

Create an icon from a raw straight-alpha RGBA8 pixel buffer — the shape Rust's tray crates expose as `Icon::from_rgba`. (issue #1185)

```csharp
public static WindowIcon FromRgba(ReadOnlySpan<byte> pixels, int width, int height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `pixels` | `ReadOnlySpan<byte>` | Exactly `width * height * 4` bytes, top-down, one pixel as R, G, B, A. Copied (as an assembled icon image), so the caller may reuse or overwrite its buffer. |
| `width` | `int` | Icon width in pixels, between 1 and 4096. |
| `height` | `int` | Icon height in pixels, between 1 and 4096. |

**Remarks**

The buffer is converted once, at construction, into the 32-bpp icon-resource layout the platform loader wants (bottom-up BGRA plus an AND mask). See [`WindowIcon.FromBytes`](#frombytes) for which surfaces consume a binary icon.

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) | `width` or `height` is outside 1–4096. |
| [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) | `pixels` is not exactly `width * height * 4` bytes. |
