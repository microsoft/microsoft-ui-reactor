# UseWindowDragMove

Returns a stable callback that starts a framework-managed window drag/move loop (cursor polling + `AppWindow.Move` at ~60Hz until the left mouse button is released).

```csharp
public Action UseWindowDragMove()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Remarks

The loop is framework-managed (not OS-managed) because WinUI 3 routes pointer input through a child input-site HWND, so synthesizing `WM_NCLBUTTONDOWN` against the top-level HWND silently falls back to keyboard/cursor-track Move mode rather than mouse-driven click-drag. The polling approach is reliable but trades away OS Aero Snap during the drag. See spec 054 §5.3.

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected Action UseWindowDragMove()
```
