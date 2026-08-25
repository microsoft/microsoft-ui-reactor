# UseReducer

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseReducer``1(``0,System.Boolean)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseReducer<T1>(T1, bool)`](#usereducert1t1-bool)
- [`UseReducer<T1, T2>(Func<T1, T2, T1>, T1, bool)`](#usereducert1-t2funct1-t2-t1-t1-bool)

## `UseReducer<T1>(T1, bool)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseReducer``1(``0,System.Boolean)`

### Summary

Declares a piece of state with a functional updater variant.
The updater receives the previous value and returns the next.
Cross-thread updater calls are auto-marshaled onto the captured UI dispatcher
(same semantics as [UseState](UseState.md) ([guide](../../hooks.md))); pass
`threadSafe`: <c>true</c> for locked in-place updates that
serialize many concurrent writers without an intervening UI tick.

## `UseReducer<T1, T2>(Func<T1, T2, T1>, T1, bool)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseReducer``2(System.Func{``0,``1,``0},``0,System.Boolean)`

### Summary

Declares a piece of state managed by a reducer function (like Redux).
The reducer takes (currentState, action) and returns the next state.
Returns (currentState, dispatch) where dispatch sends an action through the reducer.
Cross-thread dispatch calls are auto-marshaled onto the captured UI dispatcher
(same semantics as [UseState](UseState.md) ([guide](../../hooks.md))); pass
`threadSafe`: <c>true</c> for locked in-place dispatch that
serializes concurrent writers without an intervening UI tick.

## Featured in

- [Hooks](../../hooks.md)

