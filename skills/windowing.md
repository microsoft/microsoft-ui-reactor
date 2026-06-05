---
name: reactor-windowing
description: >
  Reactor windowing cookbook: WindowSpec, OpenWindow, placement persistence,
  draggable/borderless windows, taskbar visibility, z-order, aspect ratio,
  SizeToContent, displays, taskbar integration, and picker HWND wiring.
---

# Reactor Windowing

Use this when creating or updating top-level Reactor windows. Prefer `WindowSpec`
for declarative startup shape and `ReactorWindow` methods/hooks for runtime changes.

## Minimal secondary window

```csharp
var win = ReactorApp.OpenWindow(
    new WindowSpec { Title = "Settings", Width = 520, Height = 420 },
    () => new SettingsWindow());

win.Activate();
win.Close();
```

## Sizing and resize policy

```csharp
new WindowSpec
{
    ResizeMode = WindowResizeMode.CanResize,      // CanResize | NoResize | CanMinimize
    MinWidth = 360,
    MaxWidth = 1200,
    SizeToContent = WindowSizeToContent.Manual,   // Manual | Width | Height | WidthAndHeight
};
```

Rules:

- `AspectRatio` and `SizeToContent` are mutually exclusive.
- `AspectRatio` rejects `ResizeMode.NoResize`.
- `SizeToContent` is ignored while maximized and may settle one frame after mount.

## Movement and placement

```csharp
new WindowSpec
{
    StartPosition = WindowStartPosition.CenterOnCurrent,
    IsMovableByBackground = true,
};

var pos = UseWindowPosition();
var drag = UseWindowDragMove();
Button("Move", drag);

Border(customControl).Drag(false); // opt out of background drag
```

Placement values: `Default`, `CenterOnPrimary`, `CenterOnOwner`, `CenterOnCurrent`, `Manual`.

## Persistence

```csharp
var spec = new WindowSpec { Title = "Main" }
    .WithPersistence("main", fallback: WindowStartPosition.CenterOnCurrent);

UseWindow()?.SavePlacement(); // optional immediate flush
```

`PersistenceId` alone is not enough; use `.WithPersistence(...)` or set
`PersistPlacement = true` explicitly.

## Visibility, z-order, and chrome

```csharp
new WindowSpec
{
    ShowInTaskbar = false,
    ShowInSwitcher = true,
    Level = WindowLevel.Floating,        // Normal | Floating | AlwaysOnTop
    Style = WindowStyle.ToolWindow,      // Default | None | ToolWindow
    CornerStyle = WindowCornerStyle.RoundedSmall,
};
```

Notes:

- `Floating` stays above owner and sibling Reactor app windows.
- `ToolWindow` defaults `ShowInTaskbar` to false unless explicitly set.
- `WindowStyle.None` should normally pair with `IsMovableByBackground = true`.
- `UseIsCovered()` returns a z-order hint, not pixel-accurate occlusion.

## Appearance and title bar

```csharp
VStack(
    TitleBar("My App"),
    Body());
```

`TitleBar(...)` automatically infers `ExtendsContentIntoTitleBar = true` when
`WindowSpec.ExtendsContentIntoTitleBar` is `null`. Explicit `true` or `false`
wins over inference.

```csharp
VStack(...).Backdrop(BackdropKind.Mica);
new WindowSpec { Backdrop = BackdropChoice.Of(BackdropKind.DesktopAcrylic) };
```

`BackdropKind.Transparent` falls back to no backdrop when the referenced Windows
App SDK does not expose a transparent backdrop type.

## Taskbar and display helpers

```csharp
var taskbar = UseWindow()!.TaskbarItem;
taskbar.Description = "Working";
taskbar.Progress.State = TaskbarProgressState.Normal;
taskbar.Progress.Value = 0.5;

var displays = UseDisplays();
var nearest = ReactorDisplay.NearestTo(window.Position.X, window.Position.Y);
```

## Pickers

```csharp
var file = await UseFilePickerAsync(new FilePickerOptions(
    FileTypeFilter: [".txt", ".md"]));
var folder = await UseFolderPickerAsync(new FolderPickerOptions());
```

Picker hooks run on the owning window's UI thread and automatically initialize
with that window's HWND. Do not pass arbitrary HWNDs.

## Recipe: Command Palette

PowerToys Run-style launcher. See `samples/apps/command-palette-window/`.

```csharp
new WindowSpec
{
    Style = WindowStyle.None,
    IsMovableByBackground = true,
    Level = WindowLevel.AlwaysOnTop,
    CornerStyle = WindowCornerStyle.Rounded,
    StartPosition = WindowStartPosition.CenterOnCurrent,
    ShowInTaskbar = false,
    ShowInSwitcher = false,
};
```

## Recipe: Tool Palette

Photoshop-style owned floating tool window. See `samples/apps/tool-palette/`.

```csharp
var main = ReactorApp.OpenWindow(new WindowSpec { Title = "Editor" }, () => new Editor());

ReactorApp.OpenWindow(new WindowSpec
{
    Title = "Tools",
    Owner = main,
    Style = WindowStyle.ToolWindow,
    Level = WindowLevel.Floating,
    CornerStyle = WindowCornerStyle.RoundedSmall,
}, () => new ToolPalette());
```

## Recipe: Media Player (aspect-locked)

```csharp
new WindowSpec
{
    Title = "Player",
    Width = 960,
    Height = 540,
    AspectRatio = 16.0 / 9.0,
};

// Runtime swap after media metadata loads:
UseWindow()?.SetAspectRatio(videoWidth / (double)videoHeight);
UseWindowAspectRatio(16.0 / 9.0); // lifetime-bound component variant
```
