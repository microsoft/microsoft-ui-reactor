# UseMemoCells

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions.UseMemoCells``1(Microsoft.UI.Reactor.Core.RenderContext,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Memoize cell construction for <paramref name="items" />. On the first
render the builder runs for every index; on subsequent renders, an
item that compares `Equals`
against the previous render's value at the same index reuses the
previous element. Any change to <paramref name="dependencies" />
invalidates the entire cache and rebuilds every cell.

## Parameters

- **ctx** — The render context.
- **items** — Source items, one cell per item.
- **builder** — Builder for a single cell. Must be a pure
function of <c>(item, index)</c> plus <paramref name="dependencies" />.
Closure captures missing from the deps list are flagged by the
<c>REACTOR_HOOKS_007</c> analyzer.
- **dependencies** — Trailing-<c>params</c> list of values
the builder closes over. Equivalent semantics to <c>UseMemo</c>:
any change invalidates the entire memo.

## Discussion

Spec 034 §C.

## Examples

<code>
var theme = ctx.UseTheme();
var children = ctx.UseMemoCells(
stocks,
(item, i) =&gt; Cell(item, theme),
theme);   // ← deps; framework invalidates on change
</code>


