# UseEffect

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Action,System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Runs a side effect after render. The effect re-runs when any dependency changes.
Pass an empty array for "run once on mount" semantics.
Returns a cleanup action that runs before the next effect or on unmount.

## Featured in

- [Effects](../../effects.md)
- [Hooks](../../hooks.md)

