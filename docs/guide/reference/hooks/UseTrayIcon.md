# UseTrayIcon

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseTrayIcon(Microsoft.UI.Reactor.TrayIconSpec)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Open (or reuse-by-`TrayIconSpec.Key`) a
system-tray icon scoped to the calling component. The icon closes
automatically on unmount — that's the only difference from
`ReactorApp.OpenTrayIcon`, which is
app-scoped and keeps the icon alive until explicit
`ReactorTrayIcon.Close`.

## Discussion

Returns `null` when no UI dispatcher has been captured (test
contexts) or when the spec change cannot be reconciled — callers
should null-check before subscribing to events. Identity-stable across
re-renders so the same handle wires through subsequent
`UseEffect` dependencies.
(spec 036 §11.4)


