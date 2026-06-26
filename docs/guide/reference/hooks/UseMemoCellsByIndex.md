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

On the steady-state path (unchanged count) the returned array reuses
the previous render's element instance for every index NOT named in
<paramref name="changedIndices" />, and publishes a positional
structural-skip hint (Spec 034 §C) keyed by reference on that array so
the reconciler can update only the changed cells and skip the
reference-equal remainder. The returned array is therefore the hook's
retained memoized state AND the key of that hint: treat it as immutable
and declare every change through a subsequent render's
<paramref name="changedIndices" /> (React-style immutability — see
AGENTS.md "Never mutate"). Mutating an unchanged slot in place both
corrupts the memo's view of the previous render and can cause the
reconciler to skip the mutated cell.

## Parameters

- **ctx** — The render context.
- **items** — Source items.
- **changedIndices** — Indices whose item differs from the
previous render. Negative indices and indices >= <c>items.Count</c>
throw `ArgumentOutOfRangeException`. Duplicate indices are a
caller-contract violation but are tolerated: they are de-duplicated
before the named cells are rebuilt, so each cell is rebuilt exactly once
and the structural-skip hint's theme tally stays exact.
- **builder** — Cell builder; same contract as
[UseMemoCells](UseMemoCells.md) ([guide](../../hooks.md)).
- **dependencies** — Trailing-<c>params</c> deps.

## Discussion

Spec 034 §C. A cell is "theme-sensitive" when it carries
ThemeBindings or a ThemeRef-backed ResourceOverride; the hook tracks how
many cells are theme-sensitive (carried forward incrementally) so the
reconciler falls back to the full walk — which re-resolves themed brushes
against the current effective theme — instead of structurally skipping
such a range.


