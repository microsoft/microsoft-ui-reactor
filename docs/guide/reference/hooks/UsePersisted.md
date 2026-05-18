# UsePersisted

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UsePersisted``1(System.String,``0)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Like UseState, but the value survives unmount/remount via an in-memory cache.
On first mount, uses cached value if available, otherwise uses initialValue.
Value is saved to cache on unmount.

## Discussion

Spec 033 §2. The cache is currently process-wide
(`Default`) and bounded by an LRU
policy. The two-arg form will trigger an analyzer warning in a follow-up
release; new code should use the three-arg overload to make the
intended scope explicit.

## Featured in

- [Hooks](../../hooks.md)

