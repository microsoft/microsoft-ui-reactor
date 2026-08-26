# Pending

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.PendingFactory.Pending(Microsoft.UI.Reactor.Core.Element,Microsoft.UI.Reactor.Core.Element)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Wraps `child` with a fresh [PendingScope](PendingScope.md) ([guide](../../hooks.md)). Renders
`fallback` instead of `child` while any
`UseResource`/`UseInfiniteResource` in the subtree is in the
`Loading` state. `Reloading(previous)` does **not** trigger the
fallback — spec §10.1.

## Discussion

The child subtree is always mounted so its hooks register with the scope. The
element simply chooses which rendered tree to show — there is no unwinding
of rendering, and no reconciler involvement.


