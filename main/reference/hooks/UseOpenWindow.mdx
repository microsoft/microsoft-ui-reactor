---
title: "UseOpenWindow"
description: "Open or reuse a secondary window keyed by key. Renders that pass the same key share the same ReactorWindow; if the spec changes across renders the live window is updated via ReactorWindow.Update. The returned handle is identity-stable across renders so long as the key is stable."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)


## Discussion [#discussion]



Unmount semantics: when the calling component unmounts, the opened
window stays open. Components that want the inverse behavior must close
the window explicitly — e.g. by registering a `UseEffect` cleanup
that calls `ReactorWindow.Close` on the
returned handle. (spec 036 §4.3 / §15.6)



**Note — asymmetry with [UseTrayIcon](/microsoft-ui-reactor/main/reference/hooks/UseTrayIcon) ([guide](/microsoft-ui-reactor/main/guides/hooks)):** tray icons
are component-scoped and close on unmount; opened windows are app-scoped
and survive unmount. The asymmetry is deliberate (a window is normally
expected to outlive the menu item that opened it, while a tray icon
belongs to the component that declared it), so the two hooks behave
inversely despite the matching naming.



Returns `null` when no UI dispatcher has been captured —
happens in unit-test contexts where no `ReactorApp.Run` is in
flight. In production this is unreachable.
