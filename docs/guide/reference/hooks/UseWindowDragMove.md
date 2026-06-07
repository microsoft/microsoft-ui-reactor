# UseWindowDragMove

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindowDragMove`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns a stable callback that starts a framework-managed window
drag/move loop (cursor polling + <c>AppWindow.Move</c> at ~60Hz
until the left mouse button is released).

## Discussion

The loop is framework-managed (not OS-managed) because WinUI 3
routes pointer input through a child input-site HWND, so
synthesizing <c>WM_NCLBUTTONDOWN</c> against the top-level HWND
silently falls back to keyboard/cursor-track Move mode rather
than mouse-driven click-drag. The polling approach is reliable
but trades away OS Aero Snap during the drag. See spec 054 §5.3.


