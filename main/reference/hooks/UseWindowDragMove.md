---
title: "UseWindowDragMove"
description: "Returns a stable callback that starts a framework-managed window drag/move loop (cursor polling + AppWindow.Move at ~60Hz until the left mouse button is released)."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)


## Discussion [#discussion]

The loop is framework-managed (not OS-managed) because WinUI 3
routes pointer input through a child input-site HWND, so
synthesizing `WM_NCLBUTTONDOWN` against the top-level HWND
silently falls back to keyboard/cursor-track Move mode rather
than mouse-driven click-drag. The polling approach is reliable
but trades away OS Aero Snap during the drag. See spec 054 §5.3.
