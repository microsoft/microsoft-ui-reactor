# UseClosingGuard

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseClosingGuard(System.Func{System.Boolean})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Registers a synchronous "can the window close right now?" predicate.
Multiple guards stack — any returning <c>false</c> cancels the close.
Runs on the UI thread; for async confirmation, return <c>false</c> and
re-trigger `Close` from
the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)


