# UseMemo

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseMemo``1(System.Func{``0},System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseMemo<T1>(Func<T1>, object[])`](#usememot1funct1-object)
- [`UseMemo<T1, T2>(Func<T1>, T2)`](#usememot1-t2funct1-t2)
- [`UseMemo<T1, T2, T3>(Func<T1>, T2, T3)`](#usememot1-t2-t3funct1-t2-t3)
- [`UseMemo<T1, T2, T3, T4>(Func<T1>, T2, T3, T4)`](#usememot1-t2-t3-t4funct1-t2-t3-t4)

## `UseMemo<T1>(Func<T1>, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseMemo``1(System.Func{``0},System.Object[])`

### Summary

Memoizes a computed value, recomputing only when dependencies change.

## `UseMemo<T1, T2>(Func<T1>, T2)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseMemo``2(System.Func{``0},``1)`

### Summary

Single-dependency `UseMemo` overload that avoids the
`params object[]` allocation (and value-type boxing) on the
deps-unchanged path. Recomputes only when `d1` changes.

### Discussion

If `d1`'s compile-time type is an array of reference types
(e.g. `string[]`), it is treated as a dependency <em>list</em> and compared
element-wise — matching the `params object[]` overload — not as a single
reference-compared value. A dependency whose static type is not an array is
always compared as one value, even if its runtime value happens to be an array.

## `UseMemo<T1, T2, T3>(Func<T1>, T2, T3)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseMemo``3(System.Func{``0},``1,``2)`

### Summary

Two-dependency `UseMemo` overload that avoids the
`params object[]` allocation on the deps-unchanged path.

## `UseMemo<T1, T2, T3, T4>(Func<T1>, T2, T3, T4)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseMemo``4(System.Func{``0},``1,``2,``3)`

### Summary

Three-dependency `UseMemo` overload that avoids the
`params object[]` allocation on the deps-unchanged path.

## Featured in

- [Hooks](../../hooks.md)

