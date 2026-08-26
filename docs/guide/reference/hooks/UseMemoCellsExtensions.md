# UseMemoCellsExtensions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Cell-level memoization hook for high-frequency list / grid bodies.
Reuses element references for cells whose item value (and declared
dependencies) haven't changed since the previous render. The reconciler
short-circuits on `Object.ReferenceEquals`,
so reused cells skip diffing entirely.

## Discussion



Spec 034 §C. The signature deliberately matches `UseMemo` /
`UseEffect` / `UseCallback`: deps are trailing
`params`. The closure-capture correctness problem (a builder that
closes over `theme` / `selection` without listing them as
deps and silently renders stale) is caught at compile time by the
`REACTOR_HOOKS_007` Roslyn analyzer that ships with the framework.
Indirect captures through helper methods are a documented blind spot —
no static fix is available without whole-program analysis.



**When to use:** tickers, log tables, observability dashboards, file
lists, and other large readonly grids whose cell content is a pure
function of each item value plus a small set of declared
deps. **When not to use:** rows whose chrome depends on focus /
drag / selection / hover state that you aren't capturing in deps.



**gen2 trade-off:** memo trades short-lived gen0 churn for
longer-lived gen1/gen2 retention. Many memoized lists across an app
can compound gen2 pressure. Profile before deciding.




