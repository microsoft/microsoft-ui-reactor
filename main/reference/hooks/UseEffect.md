---
title: "UseEffect"
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Overloads [#overloads]

- [`UseEffect(Action, object[])`](#useeffectaction-object)
- [`UseEffect(Func<Action>, object[])`](#useeffectfuncaction-object)
- [`UseEffect<T1>(Action, T1)`](#useeffectt1action-t1)
- [`UseEffect<T1>(Func<Action>, T1)`](#useeffectt1funcaction-t1)
- [`UseEffect<T1, T2>(Action, T1, T2)`](#useeffectt1-t2action-t1-t2)
- [`UseEffect<T1, T2>(Func<Action>, T1, T2)`](#useeffectt1-t2funcaction-t1-t2)
- [`UseEffect<T1, T2, T3>(Action, T1, T2, T3)`](#useeffectt1-t2-t3action-t1-t2-t3)
- [`UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)`](#useeffectt1-t2-t3funcaction-t1-t2-t3)

## `UseEffect(Action, object[])` [#useeffectaction-object]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Action,System.Object[])`

### Summary [#summary]

Runs a side effect after render. The effect re-runs when any dependency changes.
Pass an empty array for "run once on mount" semantics.
Returns a cleanup action that runs before the next effect or on unmount.

## `UseEffect(Func<Action>, object[])` [#useeffectfuncaction-object]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect(System.Func{System.Action},System.Object[])`

### Summary [#summary_1]

Like UseEffect but the effect returns a cleanup function.

## `UseEffect<T1>(Action, T1)` [#useeffectt1action-t1]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``1(System.Action,``0)`

### Summary [#summary_2]

Single-dependency `UseEffect` overload that avoids the
`params object[]` allocation (and value-type boxing) on the
deps-unchanged path. Semantically identical to the params overload called
with one dependency: the effect re-runs only when `d1`
changes.

### Discussion [#discussion]

If `d1`'s compile-time type is an array of reference types
(e.g. `string[]`), it is treated as a dependency <em>list</em> and compared
element-wise — matching the `params object[]` overload — not as a single
reference-compared value. A dependency whose static type is not an array is
always compared as one value, even if its runtime value happens to be an array.

## `UseEffect<T1>(Func<Action>, T1)` [#useeffectt1funcaction-t1]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``1(System.Func{System.Action},``0)`

### Summary [#summary_3]

Single-dependency cleanup-flavor `UseEffect` overload that avoids the
`params object[]` allocation on the deps-unchanged path. Semantically
identical to the params overload called with one dependency.

### Discussion [#discussion_1]

If `d1`'s compile-time type is an array of reference types
(e.g. `string[]`), it is treated as a dependency <em>list</em> and compared
element-wise — matching the `params object[]` overload — not as a single
reference-compared value. A dependency whose static type is not an array is
always compared as one value, even if its runtime value happens to be an array.

## `UseEffect<T1, T2>(Action, T1, T2)` [#useeffectt1-t2action-t1-t2]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Action,``0,``1)`

### Summary [#summary_4]

Two-dependency `UseEffect` overload that avoids the
`params object[]` allocation on the deps-unchanged path. Re-runs when
either dependency changes.

## `UseEffect<T1, T2>(Func<Action>, T1, T2)` [#useeffectt1-t2funcaction-t1-t2]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Func{System.Action},``0,``1)`

### Summary [#summary_5]

Two-dependency cleanup-flavor `UseEffect` overload that avoids the
`params object[]` allocation on the deps-unchanged path.

## `UseEffect<T1, T2, T3>(Action, T1, T2, T3)` [#useeffectt1-t2-t3action-t1-t2-t3]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``3(System.Action,``0,``1,``2)`

### Summary [#summary_6]

Three-dependency `UseEffect` overload that avoids the
`params object[]` allocation on the deps-unchanged path. Re-runs when
any dependency changes.

## `UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)` [#useeffectt1-t2-t3funcaction-t1-t2-t3]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``3(System.Func{System.Action},``0,``1,``2)`

### Summary [#summary_7]

Three-dependency cleanup-flavor `UseEffect` overload that avoids the
`params object[]` allocation on the deps-unchanged path.

## Featured in [#featured-in]

- [Effects](/microsoft-ui-reactor/main/guides/effects)
- [Hooks](/microsoft-ui-reactor/main/guides/hooks)
