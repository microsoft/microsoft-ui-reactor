# UseMemoCellsByKey

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions.UseMemoCellsByKey``2(Microsoft.UI.Reactor.Core.RenderContext,System.Collections.Generic.IReadOnlyList{``0},System.Func{``0,``1},System.Func{``0,System.Int32,Microsoft.UI.Reactor.Core.Element},System.Object[])`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Memoize cell construction keyed by <paramref name="keySelector" />.
Cells are reused when both the item's key and value match the
previous render. Keys that recur with mutated content rebuild that
cell only. Reordered keys reuse cells (the reconciler's keyed-
children path keeps the underlying control without unmount/remount).

## Parameters

- **ctx** — The render context.
- **items** — Source items.
- **keySelector** — Stable identity per item. Duplicate
keys collapse to last-write-wins (later items overwrite earlier
items in the lookup table).
- **builder** — Cell builder; same contract as
[UseMemoCells](UseMemoCells.md) ([guide](../../hooks.md)).
- **dependencies** — Trailing-<c>params</c> deps.

## Discussion

Spec 034 §C.


