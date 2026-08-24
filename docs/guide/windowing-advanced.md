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
const int GWL_EXSTYLE = -20;
const nint WS_EX_TRANSPARENT = 0x00000020;
const nint WS_EX_TOOLWINDOW = 0x00000080;
const nint WS_EX_LAYERED = 0x00080000;
const uint LWA_ALPHA = 0x00000002;

public static void MakeClickThrough(ReactorWindow window)
{
    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window.NativeWindow);
    var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
    SetWindowLongPtr(hwnd, GWL_EXSTYLE,
        ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
    SetLayeredWindowAttributes(hwnd, 0, 160, LWA_ALPHA);
}
```

Caveats:

- `WS_EX_LAYERED` can bypass the WinUI composition path; test every build.
- Click-through means the window cannot receive normal pointer input.
- Use a separate overlay window; do not layer the main app shell.

## HUD aesthetic recipe

A HUD-style surface can stay inside the supported Reactor contract when it only
needs an aesthetic, not true transparency:

```csharp
static class HudWindow
{
    public static WindowSpec Spec { get; } = new()
    {
        Title = "HUD",
        Style = WindowStyle.None,
        IsMovableByBackground = true,
        Level = WindowLevel.Floating,
        CornerStyle = WindowCornerStyle.Rounded,
        Backdrop = BackdropChoice.Of(BackdropKind.DesktopAcrylic),
    };
}
```

Render a dark-tinted root `Border`, add a custom `TitleBar(...)`, and keep the
surface app-local with `WindowLevel.Floating`. The shipping
`samples/apps/window-styles` app is the live counterpart: it toggles every
`WindowStyle`, `WindowLevel`, `WindowCornerStyle`, and `BackdropKind` on one
window so you can see the combination before committing to it, and
`samples/apps/tool-palette` shows the same shape as a real palette window.

Caveats:

- `BackdropKind.DesktopAcrylic` is not vibrancy; it is the Windows backdrop material.
- `WindowStyle.None` removes the system menu and caption drag affordance.
- Pair borderless windows with `IsMovableByBackground` or explicit `BeginDragMove()`.

## Arbitrary corner radius via `SetWindowRgn`

DWM exposes only discrete corner preferences (`Default`, `Square`, `Rounded`,
`RoundedSmall`). You can force arbitrary regions with `SetWindowRgn`, but that is
not a platform-quality default.

```csharp
public static void ApplyRoundedRegion(nint hwnd, int widthPx, int heightPx, int radiusPx)
{
    var region = CreateRoundRectRgn(0, 0, widthPx, heightPx, radiusPx, radiusPx);
    if (region == 0) return;

    // SetWindowRgn transfers ownership of the HRGN to the system on
    // success — the region must NOT be deleted afterwards. Delete it
    // only when the call fails, or the next repaint reads freed GDI
    // memory.
    if (SetWindowRgn(hwnd, region, bRedraw: true) == 0)
        DeleteObject(region);
}
```

Tradeoffs:

- DWM shadows are often lost or clipped.
- Edges can be jagged at 100% DPI because regions are binary masks.
- The region must be recomputed during resize, which can cause redraw cascades.
- **The system takes ownership of the `HRGN` on a successful `SetWindowRgn`.**
  Deleting it afterwards — or wrapping it in a `using` / `SafeHandle` that does —
  frees GDI memory the compositor still reads. Delete the region only when
  `SetWindowRgn` fails.

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
