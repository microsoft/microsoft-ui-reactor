# UseNavigation

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseNavigation``1`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseNavigation<T1>()`](#usenavigationt1)
- [`UseNavigation<T1>(T1)`](#usenavigationt1t1)

## `UseNavigation<T1>()`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseNavigation``1`

### Summary

Child mode: retrieves an ancestor's `NavigationHandle`1`
from context. Throws if no ancestor provides one (i.e., no root <c>UseNavigation</c>
with a <c>NavigationHost</c> exists above this component in the tree).

## `UseNavigation<T1>(T1)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseNavigation``1(``0)`

### Summary

Root mode: creates a navigation stack with the given initial route.
Returns a stable `NavigationHandle`1` across re-renders.
Wire this handle to a <c>NavigationHost</c> in the DSL to render route content.
The handle is automatically provided to descendants via context so child components
can call <c>UseNavigation&lt;TRoute&gt;()</c> (parameterless) to access it.


