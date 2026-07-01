# UseExternalStore

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseExternalStore``1(System.Func{System.Action,System.Action},System.Func{``0},System.Collections.Generic.IEqualityComparer{``0})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Subscribes to an external store that exposes a current snapshot getter.
Re-renders only when a change notification yields a different snapshot.

## Featured in

- [Hooks](../../hooks.md)