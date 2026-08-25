# UseElementRef

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementRefExtensions.UseElementRef``1(Microsoft.UI.Reactor.Core.Component)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseElementRef<T>(Component)`](#useelementreftcomponent)
- [`UseElementRef<T>(RenderContext)`](#useelementreftrendercontext)

## `UseElementRef<T>(Component)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementRefExtensions.UseElementRef``1(Microsoft.UI.Reactor.Core.Component)`

### Summary

Component-extension overload of [UseElementRef](UseElementRef.md#useelementreftrendercontext).
Equivalent to calling the `RenderContext`-extension form against
<c>component.Context</c>.

### Parameters

- **component** — The component whose render context owns the hook slot.

## `UseElementRef<T>(RenderContext)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementRefExtensions.UseElementRef``1(Microsoft.UI.Reactor.Core.RenderContext)`

### Summary

Returns a stable `ElementRef` for the current component scope.


