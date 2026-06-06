# UseWindowDragMove

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindowDragMove`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns a stable callback that starts a framework-managed window drag/move loop.

The loop polls `GetCursorPos` and calls `AppWindow.Move` at ~60Hz until the
left mouse button is released — WinUI 3 routes pointer input through a
child input-site HWND so synthesizing `WM_NCLBUTTONDOWN` against the
top-level HWND silently falls back to keyboard/cursor-track Move mode
rather than mouse-driven click-drag. The polling approach is reliable but
trades away OS-managed niceties: there is no Aero Snap during the drag.


