# UseMemoCellsByIndex

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions.UseMemoCellsByIndex``1(Microsoft.UI.Reactor.Core.RenderContext,System.Collections.Generic.IReadOnlyList{``0},System.Collections.Generic.IReadOnlyList{System.Int32},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Memoize cell construction when the data source already knows which
indices changed. Skips the per-cell `Equals`
scan entirely; the builder runs only for indices in
<paramref name="changedIndices" />. When the item count changes
between renders the overload falls back to a full rebuild
(<paramref name="changedIndices" /> is treated as
"rebuild everything") because the index space no longer matches
the prior render. Callers whose lists grow or shrink frequently
will get better incremental reuse from [UseMemoCells](UseMemoCells.md) ([guide](../../hooks.md))
or [UseMemoCellsByKey](UseMemoCellsByKey.md) ([guide](../../hooks.md)), both of which can
short-circuit per-cell on value or key equality across length
changes.

## Parameters

- **ctx** — The render context.
- **items** — Source items.
- **changedIndices** — Indices whose item differs from the
previous render. Negative indices and indices >= <c>items.Count</c>
throw `ArgumentOutOfRangeException`.
- **builder** — Cell builder; same contract as
[UseMemoCells](UseMemoCells.md) ([guide](../../hooks.md)).
- **dependencies** — Trailing-<c>params</c> deps.

## Discussion

Spec 034 §C.


