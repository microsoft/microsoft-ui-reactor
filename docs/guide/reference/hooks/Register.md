# Register

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.FocusManager.Register(System.String)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Members

> These members share a name but are declared on unrelated types. They are not overloads of one another.

- [`Register(string)`](#registerstring) — `Microsoft.UI.Reactor.Hooks.FocusManager`
- [`Register(object, bool)`](#registerobject-bool) — `Microsoft.UI.Reactor.Hooks.PendingScope`

## `Register(string)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.FocusManager.Register(System.String)`

### Summary

Registers a field name in ordering. Call on every render to maintain order.

## `Register(object, bool)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.PendingScope.Register(System.Object,System.Boolean)`

### Summary

Start tracking `token` with the given initial `isLoading`
state. A hook typically uses its own `this`-equivalent as the token.


