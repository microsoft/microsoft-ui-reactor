# UseCallback

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCallback(System.Action,System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseCallback(Action, object[])`](#usecallbackaction-object)
- [`UseCallback<T1>(Action, T1)`](#usecallbackt1action-t1)
- [`UseCallback<T1, T2>(Action, T1, T2)`](#usecallbackt1-t2action-t1-t2)
- [`UseCallback<T1, T2, T3>(Action, T1, T2, T3)`](#usecallbackt1-t2-t3action-t1-t2-t3)

## `UseCallback(Action, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCallback(System.Action,System.Object[])`

### Summary

Returns a stable callback reference that doesn't change between renders.

## `UseCallback<T1>(Action, T1)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCallback``1(System.Action,``0)`

### Summary

Single-dependency `UseCallback` overload that avoids the
`params object[]` allocation (and value-type boxing) on the
deps-unchanged path. Returns a stable reference until `d1`
changes.

### Discussion

If `d1`'s compile-time type is an array of reference types
(e.g. `string[]`), it is treated as a dependency <em>list</em> and compared
element-wise — matching the `params object[]` overload — not as a single
reference-compared value. A dependency whose static type is not an array is
always compared as one value, even if its runtime value happens to be an array.

## `UseCallback<T1, T2>(Action, T1, T2)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCallback``2(System.Action,``0,``1)`

### Summary

Two-dependency `UseCallback` overload that avoids the
`params object[]` allocation on the deps-unchanged path.

## `UseCallback<T1, T2, T3>(Action, T1, T2, T3)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseCallback``3(System.Action,``0,``1,``2)`

### Summary

Three-dependency `UseCallback` overload that avoids the
`params object[]` allocation on the deps-unchanged path.

## Featured in

- [Hooks](../../hooks.md)

