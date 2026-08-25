# Register

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.FocusManager.Register(System.String)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`Register(string)`](#registerstring)
- [`Register(object, bool)`](#registerobject-bool)

## `Register(string)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.FocusManager.Register(System.String)`

### Summary

Registers a field name in ordering. Call on every render to maintain order.

## `Register(object, bool)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.PendingScope.Register(System.Object,System.Boolean)`

### Summary

Start tracking <paramref name="token" /> with the given initial <paramref name="isLoading" />
state. A hook typically uses its own <c>this</c>-equivalent as the token.


