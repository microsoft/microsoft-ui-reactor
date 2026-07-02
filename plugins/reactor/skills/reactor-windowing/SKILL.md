---
name: reactor-windowing
description: "Reactor top-level windowing cookbook: WindowSpec, OpenWindow, draggable windows, borderless/tool windows, placement persistence, taskbar visibility, z-order, aspect ratio, SizeToContent, displays, taskbar integration, and picker HWND wiring."
when_to_invoke:
  - "open a window"
  - "secondary window"
  - "make a window draggable"
  - "remember window position"
  - "restore window placement"
  - "tool palette"
  - "command palette"
  - "window aspect ratio"
  - "borderless window"
  - "always on top"
  - "taskbar progress"
  - "file picker"
---

# Reactor Windowing

Use this skill when a task involves top-level windows, placement, shell chrome,
taskbar features, displays, or native pickers.

## Core APIs

```csharp
var win = ReactorApp.OpenWindow(
    new WindowSpec { Title = "Settings", Width = 520, Height = 420 },
    () => new SettingsWindow());

win.Activate();
win.Close();
```

`WindowSpec` is immutable startup intent. `ReactorWindow` is the live handle for
runtime mutators (`SetSize`, `SetPosition`, `SetAspectRatio`, `BeginDragMove`,
`SavePlacement`, `Update`).

## Size and resize

```csharp
new WindowSpec
{
    ResizeMode = WindowResizeMode.CanMinimize,       // CanResize | NoResize | CanMinimize
    AspectRatio = 16.0 / 9.0,
    MinWidth = 480,
    SizeToContent = WindowSizeToContent.Manual,      // Manual | Width | Height | WidthAndHeight
};

UseWindow()?.SetAspectRatio(4.0 / 3.0);
UseWindowAspectRatio(1.0); // scoped; unmount clears
```

Rules:

- `AspectRatio` cannot combine with `ResizeMode.NoResize`.
- `AspectRatio` cannot combine with `SizeToContent`.
- `SizeToContent` ignores maximized windows and may settle one frame after mount.

## Movement, drag, and persistence

```csharp
new WindowSpec
{
    StartPosition = WindowStartPosition.CenterOnCurrent,
    IsMovableByBackground = true,
};

var (x, y) = UseWindowPosition();
var drag = UseWindowDragMove();
Button("Drag", drag);
Border(customEditor).Drag(false); // opt out of background drag
```

```csharp
var spec = new WindowSpec { Title = "Shell" }
    .WithPersistence("shell-main", WindowStartPosition.CenterOnCurrent);

UseWindow()?.SavePlacement();
```

Persistence requires `PersistPlacement`; use `.WithPersistence(...)` for the
common case. `PersistenceId` alone is only identity for persistence systems.

## Z-order, taskbar, and chrome

```csharp
new WindowSpec
{
    ShowInTaskbar = false,
    ShowInSwitcher = true,
    Level = WindowLevel.Floating,          // Normal | Floating | AlwaysOnTop
    Style = WindowStyle.ToolWindow,        // Default | None | ToolWindow
    CornerStyle = WindowCornerStyle.RoundedSmall,
};
```

Notes:

- `Floating` stays above owners and sibling Reactor app windows.
- `ToolWindow` hides from the taskbar by default unless `ShowInTaskbar` is explicit.
- `WindowStyle.None` should normally set `IsMovableByBackground = true`.
- `UseIsCovered()` is a z-order hint, not pixel-accurate occlusion.

## Title bar and backdrop

```csharp
VStack(
    TitleBar("My App"),
    Body());
```

A `TitleBar(...)` element infers `ExtendsContentIntoTitleBar = true` when the
spec value is `null`. Explicit `true` or `false` wins.

> **Close-safety caveat (#537):** `ExtendsContentIntoTitleBar = false` with a
> mounted `TitleBar(...)` is allowed and previously crashed on close
> (`STATUS_HEAP_CORRUPTION`); Reactor now flips the window back into
> content-extended mode just before the native close (every close/exit/dispose
> path) so it is safe. Prefer omitting `TitleBar(...)` when you want the system
> title bar.

### Custom title-bar content and drag regions

`TitleBar(...)` accepts custom `Content`. Interactive controls are excluded from
the drag region automatically (WinApp SDK ≥ 2.1.3). Override per element with
`.IsDragRegion(false)` (force clickable) or `.IsDragRegion(true)` (force draggable),
and add `.AutoRefreshDragRegions()` when the content changes across renders:

```csharp
(TitleBar("Gallery") with
{
    Content = HStack(8,
        AutoSuggestBox("", _ => {}).Width(200),
        Button("\uE713", OnSettings).IsDragRegion(false)),
}).AutoRefreshDragRegions();
```

```csharp
VStack(...).Backdrop(BackdropKind.Mica);
new WindowSpec { Backdrop = BackdropChoice.Of(BackdropKind.DesktopAcrylic) };
```

`BackdropKind.Transparent` falls back to no backdrop when unsupported by the
referenced Windows App SDK.

## Taskbar, displays, and pickers

```csharp
var taskbar = UseWindow()!.TaskbarItem;
taskbar.Description = "Exporting";
taskbar.Progress.State = TaskbarProgressState.Normal;
taskbar.Progress.Value = 0.25;

var displays = UseDisplays();
var nearest = ReactorDisplay.NearestTo(window.Position.X, window.Position.Y);

var file = await UseFilePickerAsync(new FilePickerOptions(FileTypeFilter: [".txt"]));
var folder = await UseFolderPickerAsync(new FolderPickerOptions());
```

Picker hooks must run on the owning window's UI thread and use the owning HWND;
there is no arbitrary HWND parameter.

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

Photoshop-style owned floating palette. See `samples/apps/tool-palette/`.

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

UseWindow()?.SetAspectRatio(videoWidth / (double)videoHeight);
UseWindowAspectRatio(16.0 / 9.0);
```
