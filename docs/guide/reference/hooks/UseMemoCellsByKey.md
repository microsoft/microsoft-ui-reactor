# UseMemoCellsByKey

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions.UseMemoCellsByKey``2(Microsoft.UI.Reactor.Core.Component,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,``1},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Members

> These members share a name but are declared on unrelated types. They are not overloads of one another.

- [`UseMemoCellsByKey<T1, T2>(Component, IReadOnlyList<T1>, Func<T1, T2>, Func<T1, int, Element>, object[])`](#usememocellsbykeyt1-t2component-ireadonlylistt1-funct1-t2-funct1-int-element-object) — `Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions`
- [`UseMemoCellsByKey<T1, T2>(RenderContext, IReadOnlyList<T1>, Func<T1, T2>, Func<T1, int, Element>, object[])`](#usememocellsbykeyt1-t2rendercontext-ireadonlylistt1-funct1-t2-funct1-int-element-object) — `Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions`

## `UseMemoCellsByKey<T1, T2>(Component, IReadOnlyList<T1>, Func<T1, T2>, Func<T1, int, Element>, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions.UseMemoCellsByKey``2(Microsoft.UI.Reactor.Core.Component,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,``1},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

### Summary

Component-extension shim for [UseMemoCellsByKey](UseMemoCellsByKey.md#usememocellsbykeyt1-t2rendercontext-ireadonlylistt1-funct1-t2-funct1-int-element-object) ([guide](../../hooks.md)).
Same semantics as the `RenderContext`-extension form;
dispatches against `component.Context`.

### Parameters

- **component** — The component whose render context owns the hook slot.
- **items** — Source items.
- **keySelector** — Projection from item to stable key.
- **builder** — Per-cell builder.
- **dependencies** — Additional hook dependencies.

## `UseMemoCellsByKey<T1, T2>(RenderContext, IReadOnlyList<T1>, Func<T1, T2>, Func<T1, int, Element>, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions.UseMemoCellsByKey``2(Microsoft.UI.Reactor.Core.RenderContext,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,``1},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

### Summary

Memoize cell construction keyed by `keySelector`.
Cells are reused when both the item's key and value match the
previous render. Keys that recur with mutated content rebuild that
cell only. Reordered keys reuse cells (the reconciler's keyed-
children path keeps the underlying control without unmount/remount).

### Parameters

- **ctx** — The render context.
- **items** — Source items.
- **keySelector** — Stable identity per item. Duplicate
keys collapse to last-write-wins (later items overwrite earlier
items in the lookup table).
- **builder** — Cell builder; same contract as
[UseMemoCells](UseMemoCells.md#usememocellstrendercontext-ireadonlylistt-funct-int-element-object) ([guide](../../hooks.md)).
- **dependencies** — Trailing-`params` deps.

### Discussion

Spec 034 §C.


