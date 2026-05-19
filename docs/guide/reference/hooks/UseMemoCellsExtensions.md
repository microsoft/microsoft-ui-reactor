# UseMemoCellsExtensions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Cell-level memoization hook for high-frequency list / grid bodies.
Reuses element references for cells whose item value (and declared
dependencies) haven't changed since the previous render. The reconciler
short-circuits on `ReferenceEquals`,
so reused cells skip diffing entirely.

## Discussion

<para>
Spec 034 §C. The signature deliberately matches <c>UseMemo</c> /
<c>UseEffect</c> / <c>UseCallback</c>: deps are trailing
<c>params</c>. The closure-capture correctness problem (a builder that
closes over <c>theme</c> / <c>selection</c> without listing them as
deps and silently renders stale) is caught at compile time by the
<c>REACTOR_HOOKS_007</c> Roslyn analyzer that ships with the framework.
Indirect captures through helper methods are a documented blind spot —
no static fix is available without whole-program analysis.
</para><para><b>When to use:</b> tickers, log tables, observability dashboards, file
lists, and other large readonly grids whose cell content is a pure
function of each item value plus a small set of declared
deps. <b>When not to use:</b> rows whose chrome depends on focus /
drag / selection / hover state that you aren't capturing in deps.
</para><para><b>gen2 trade-off:</b> memo trades short-lived gen0 churn for
longer-lived gen1/gen2 retention. Many memoized lists across an app
can compound gen2 pressure. Profile before deciding.
</para>


