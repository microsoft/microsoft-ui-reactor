# CursorFromPageIndex

`property`  
_cref_: `P:Microsoft.UI.Reactor.Hooks.InfiniteHookState`2.CursorFromPageIndex`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Optional: computes the cursor for an arbitrary page index directly, bypassing the
"wait for page N-1" constraint. Used by `UseDataSource` to expose the offset
semantics of `IDataSource`'s `ContinuationToken`, so deep
scrolls fetch pages in parallel instead of walking the chain one round-trip at a time.


