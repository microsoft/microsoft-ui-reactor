# UseWindow

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindow`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Resolves the current host's `ReactorWindow`
(or <c>null</c> when called outside a window — e.g. tray-flyout content).
O(1) field read; no subscription, no re-render trigger. (spec 036 §7 / §7.1)


