# Pending

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.PendingFactory.Pending(Microsoft.UI.Reactor.Core.Element,Microsoft.UI.Reactor.Core.Element)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Wraps <paramref name="child" /> with a fresh [PendingScope](PendingScope.md) ([guide](../../hooks.md)). Renders
<paramref name="fallback" /> instead of <paramref name="child" /> while any
<c>UseResource</c>/<c>UseInfiniteResource</c> in the subtree is in the
<c>Loading</c> state. <c>Reloading(previous)</c> does <b>not</b> trigger the
fallback — spec §10.1.

## Discussion

The child subtree is always mounted so its hooks register with the scope. The
element simply chooses which rendered tree to show — there is no unwinding
of rendering, and no reconciler involvement.


