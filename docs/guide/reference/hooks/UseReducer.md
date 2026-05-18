# UseReducer

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseReducer``1(``0,System.Boolean)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Declares a piece of state with a functional updater variant.
The updater receives the previous value and returns the next.
Cross-thread updater calls are auto-marshaled onto the captured UI dispatcher
(same semantics as [UseState](UseState.md) ([guide](../../hooks.md))); pass
<paramref name="threadSafe" />: <c>true</c> for locked in-place updates that
serialize many concurrent writers without an intervening UI tick.

## Featured in

- [Hooks](../../hooks.md)

