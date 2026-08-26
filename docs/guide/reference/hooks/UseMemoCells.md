# UseMemoCells

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions.UseMemoCells``1(Microsoft.UI.Reactor.Core.Component,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Members

> These members share a name but are declared on unrelated types. They are not overloads of one another.

- [`UseMemoCells<T>(Component, IReadOnlyList<T>, Func<T, int, Element>, object[])`](#usememocellstcomponent-ireadonlylistt-funct-int-element-object) — `Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions`
- [`UseMemoCells<T>(RenderContext, IReadOnlyList<T>, Func<T, int, Element>, object[])`](#usememocellstrendercontext-ireadonlylistt-funct-int-element-object) — `Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions`

## `UseMemoCells<T>(Component, IReadOnlyList<T>, Func<T, int, Element>, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions.UseMemoCells``1(Microsoft.UI.Reactor.Core.Component,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

### Summary

Component-extension shim for [UseMemoCells](UseMemoCells.md#usememocellstrendercontext-ireadonlylistt-funct-int-element-object).
Same semantics as the `RenderContext`-extension form;
dispatches against `component.Context`.

### Parameters

- **component** — The component whose render context owns the hook slot.
- **items** — Source items.
- **builder** — Per-cell builder.
- **dependencies** — Additional hook dependencies.

## `UseMemoCells<T>(RenderContext, IReadOnlyList<T>, Func<T, int, Element>, object[])`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions.UseMemoCells``1(Microsoft.UI.Reactor.Core.RenderContext,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

### Summary

Memoize cell construction for `items`. On the first
render the builder runs for every index; on subsequent renders, an
item that compares `Object.Equals`
against the previous render's value at the same index reuses the
previous element. Any change to `dependencies`
invalidates the entire cache and rebuilds every cell.

### Parameters

- **ctx** — The render context.
- **items** — Source items, one cell per item.
- **builder** — Builder for a single cell. Must be a pure
function of `(item, index)` plus `dependencies`.
Closure captures missing from the deps list are flagged by the
`REACTOR_HOOKS_007` analyzer.
- **dependencies** — Trailing-`params` list of values
the builder closes over. Equivalent semantics to `UseMemo`:
any change invalidates the entire memo.

### Discussion

Spec 034 §C.

### Examples

```csharp
var scheme = ctx.UseColorScheme();
var children = ctx.UseMemoCells(
stocks,
(item, i) => Cell(item, scheme),
scheme);   // ← deps; framework invalidates on change
```


