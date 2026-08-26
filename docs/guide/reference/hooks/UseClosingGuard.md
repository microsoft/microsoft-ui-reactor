# UseClosingGuard

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseClosingGuard(System.Func{System.Boolean})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Registers a synchronous "can the window close right now?" predicate.
Multiple guards stack — any returning `false` cancels the close.
Runs on the UI thread; for async confirmation, return `false` and
re-trigger `ReactorWindow.Close` from
the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)


