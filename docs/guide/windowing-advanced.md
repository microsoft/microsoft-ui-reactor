> **WinUI reference:** For the full property surface and design guidance, see [Window Features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).

# Advanced Windowing

Some shell-window ideas are possible on Windows only by leaving the safe Reactor
contract. We document the recipe so expert apps can make an informed choice, but
Reactor does not ship these primitives as first-class `WindowSpec` fields.

## FancyZones-style click-through overlay

FancyZones-like overlays need per-pixel or color-key transparency and mouse
click-through. The WinUI XAML compositor does not compose cleanly with classic
`WS_EX_LAYERED` rendering, so Reactor exposes safer building blocks (`Opacity`,
`IgnorePointerInput`, `WindowStyle.None`) and leaves true layered overlays to HWND
interop.

```csharp
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window.NativeWindow);
var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
SetWindowLongPtr(hwnd, GWL_EXSTYLE,
    ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
SetLayeredWindowAttributes(hwnd, 0, 160, LWA_ALPHA);
```

Caveats:

- `WS_EX_LAYERED` can bypass the WinUI composition path; test every build.
- Click-through means the window cannot receive normal pointer input.
- Use a separate overlay window; do not layer the main app shell.

## HUD aesthetic recipe

A HUD-style surface can stay inside the supported Reactor contract when it only
needs an aesthetic, not true transparency:

```csharp
new WindowSpec
{
    Title = "HUD",
    Style = WindowStyle.None,
    IsMovableByBackground = true,
    Level = WindowLevel.Floating,
    CornerStyle = WindowCornerStyle.Rounded,
    Backdrop = BackdropChoice.Of(BackdropKind.DesktopAcrylic),
};
```

Render a dark-tinted root `Border`, add a custom `TitleBar(...)`, and keep the
surface app-local with `WindowLevel.Floating`. A dedicated `samples/apps/hud-overlay/`
may be added in a later sample-polish phase; until then, this snippet is the
recommended shape.

Caveats:

- `BackdropKind.DesktopAcrylic` is not vibrancy; it is the Windows backdrop material.
- `WindowStyle.None` removes the system menu and caption drag affordance.
- Pair borderless windows with `IsMovableByBackground` or explicit `BeginDragMove()`.

## Arbitrary corner radius via `SetWindowRgn`

DWM exposes only discrete corner preferences (`Default`, `Square`, `Rounded`,
`RoundedSmall`). You can force arbitrary regions with `SetWindowRgn`, but that is
not a platform-quality default.

```csharp
using var region = CreateRoundRectRgn(0, 0, widthPx, heightPx, radiusPx, radiusPx);
SetWindowRgn(hwnd, region.DangerousGetHandle(), true);
```

Tradeoffs:

- DWM shadows are often lost or clipped.
- Edges can be jagged at 100% DPI because regions are binary masks.
- The region must be recomputed during resize, which can cause redraw cascades.

Use this only for highly specialized shells that accept those costs.

## Cannot deliver as Reactor primitives

| Scenario | Why it is not a first-class Reactor API |
| --- | --- |
| True transparent XAML window | WinUI XAML composition and classic layered-window rendering conflict. |
| NSWindow-style level stack | Windows has normal and topmost tiers, not an arbitrary z-level ladder. |
| Vibrancy / HUD materials beyond `SystemBackdrop` | Requires platform compositor support, not a framework shim. |
| Continuous `CornerRadius` on top-level HWNDs | DWM exposes discrete corner styles; regions lose quality. |

When you need one of these, keep the unsupported interop isolated in a small
hosting helper and keep the rest of the UI declarative Reactor components.

## Next Steps

- **[Windows](windows.md)** — supported top-level windowing APIs
- **[Docking Windows](docking.md)** — dockable panes and floating documents
- **[WinForms Interop](winforms-interop.md)** — host Reactor inside an existing desktop shell
- **[WPF Interop](wpf-interop.md)** — combine Reactor islands with WPF hosts
