# UseEffect

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Action,System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseEffect(Action, object[])`](#useeffectaction-object)
- [`UseEffect(Func<Action>, object[])`](#useeffectfuncaction-object)
- [`UseEffect<T1>(Action, T1)`](#useeffectt1action-t1)
- [`UseEffect<T1>(Func<Action>, T1)`](#useeffectt1funcaction-t1)
- [`UseEffect<T1, T2>(Action, T1, T2)`](#useeffectt1-t2action-t1-t2)
- [`UseEffect<T1, T2>(Func<Action>, T1, T2)`](#useeffectt1-t2funcaction-t1-t2)
- [`UseEffect<T1, T2, T3>(Action, T1, T2, T3)`](#useeffectt1-t2-t3action-t1-t2-t3)
- [`UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)`](#useeffectt1-t2-t3funcaction-t1-t2-t3)

## `UseEffect(Action, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Action,System.Object[])`

### Summary

Runs a side effect after render. The effect re-runs when any dependency changes.
Pass an empty array for "run once on mount" semantics.
Returns a cleanup action that runs before the next effect or on unmount.

## `UseEffect(Func<Action>, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Func{System.Action},System.Object[])`

### Summary

Like UseEffect but the effect returns a cleanup function.

## `UseEffect<T1>(Action, T1)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``1(System.Action,``0)`

### Summary

Single-dependency <c>UseEffect</c> overload that avoids the
<c>params object[]</c> allocation (and value-type boxing) on the
deps-unchanged path. Semantically identical to the params overload called
with one dependency: the effect re-runs only when <paramref name="d1" />
changes.

### Discussion

If <paramref name="d1" />'s compile-time type is an array of reference types
(e.g. <c>string[]</c>), it is treated as a dependency <em>list</em> and compared
element-wise — matching the <c>params object[]</c> overload — not as a single
reference-compared value. A dependency whose static type is not an array is
always compared as one value, even if its runtime value happens to be an array.

## `UseEffect<T1>(Func<Action>, T1)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``1(System.Func{System.Action},``0)`

### Summary

Single-dependency cleanup-flavor <c>UseEffect</c> overload that avoids the
<c>params object[]</c> allocation on the deps-unchanged path. Semantically
identical to the params overload called with one dependency.

### Discussion

If <paramref name="d1" />'s compile-time type is an array of reference types
(e.g. <c>string[]</c>), it is treated as a dependency <em>list</em> and compared
element-wise — matching the <c>params object[]</c> overload — not as a single
reference-compared value. A dependency whose static type is not an array is
always compared as one value, even if its runtime value happens to be an array.

## `UseEffect<T1, T2>(Action, T1, T2)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Action,``0,``1)`

### Summary

Two-dependency <c>UseEffect</c> overload that avoids the
<c>params object[]</c> allocation on the deps-unchanged path. Re-runs when
either dependency changes.

## `UseEffect<T1, T2>(Func<Action>, T1, T2)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Func{System.Action},``0,``1)`

### Summary

Two-dependency cleanup-flavor <c>UseEffect</c> overload that avoids the
<c>params object[]</c> allocation on the deps-unchanged path.

## `UseEffect<T1, T2, T3>(Action, T1, T2, T3)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``3(System.Action,``0,``1,``2)`

### Summary

Three-dependency <c>UseEffect</c> overload that avoids the
<c>params object[]</c> allocation on the deps-unchanged path. Re-runs when
any dependency changes.

## `UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``3(System.Func{System.Action},``0,``1,``2)`

### Summary

Three-dependency cleanup-flavor <c>UseEffect</c> overload that avoids the
<c>params object[]</c> allocation on the deps-unchanged path.

## Featured in

- [Effects](../../effects.md)
- [Hooks](../../hooks.md)

