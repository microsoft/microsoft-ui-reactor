---
name: reactor-perf-tips
description: >
  Playbook for writing fast Reactor code. When to memoize, when to reach for
  the direct-record-initializer escape hatch, how brushes / fonts / corner
  radii should be cached, and the gen2 trade-off you're paying for memo. Read
  before optimizing — most code does not need any of this.
---

# Reactor Perf Tips

## When to care

Read this only if you actually have a perf problem. Most components are fast
enough without any of these tricks. The fluent chain, ordinary `UseState`,
and naïve `Render()` are the right shape for ordinary UI.

You should reach for this skill when:

- A list or grid renders **hundreds-plus elements per render** — tickers,
  log tables, observability dashboards, stocks grids.
- A scroll surface or animation is dropping frames.
- Profiling shows `ElementModifiers` allocations or `with`-clones dominating
  the per-tick budget.

If none of those apply, close this file and write the obvious thing.

## 1. `UseMemo` / `UseCallback`

The everyday wrappers. `UseMemo` caches the result of an expensive
computation; `UseCallback` returns a stable callback identity across renders.
Both take trailing `params object[] deps`.

```csharp
var sortedItems = ctx.UseMemo(() => Sort(items), items);
var onClick     = ctx.UseCallback(() => dispatch(action), action);
```

**Closure-capture trap.** The lambda body is keyed on the *deps array*, not
on what's syntactically captured. Forgetting a dep means stale results
between renders. The `REACTOR_HOOKS_004` analyzer warns when deps look
freshly allocated each render (`new[] { … }`, `() => …`); you still have to
list every captured value yourself.

## 2. `UseMemoCells` for list / grid bodies

Drop-in replacement for the inner cell-construction loop in a hot list. The
hook keeps the previous render's `Element[]`; cells whose item value (and
declared deps) haven't changed are reused by reference, and the reconciler
short-circuits on `ReferenceEquals`. Skipped cells allocate nothing and
diff nothing.

```csharp
// Naive — every cell rebuilds every render.
var cells = items.Select((item, i) => Cell(item, theme)).ToArray();

// Memoized — only changed cells rebuild.
var cells = ctx.UseMemoCells(items, (item, i) => Cell(item, theme), theme);
```

Three overloads:

| Overload | When to pick |
|----------|--------------|
| `UseMemoCells<T>(items, builder, …deps)` | Per-item value equality. Default. |
| `UseMemoCellsByKey<T, TKey>(items, keySelector, builder, …deps)` | Items have stable identity but mutable interior. Hashes by key, value-compares for content. Reordered keys reuse cells. |
| `UseMemoCellsByIndex<T>(items, changedIndices, builder, …deps)` | Data source already knows which indices changed. Skips the per-cell equality scan; only named indices run the builder. |

**Compile-time safety net.** `REACTOR_HOOKS_007` warns when a builder
closure captures a value that isn't declared in `deps` — the codefix
appends the missing capture. Indirect captures through helper methods are
the documented blind spot; the analyzer can't see through a method call
without whole-program analysis.

## 3. Direct record initializer in inner cell loops

When the cell count is high enough that the fluent chain's `with`-clones
show up in profiles, build the cell directly:

```csharp
// Fluent — five clones per cell.
TextBlock(label).FontSize(8).Foreground(brush).Padding(2).Grid(row, col)

// Direct — one TextBlockElement, one ElementModifiers, two bucket sub-records.
new TextBlockElement(label)
{
    FontSize = 8,
    Modifiers = new ElementModifiers
    {
        Layout = new LayoutModifiers { Padding = new Thickness(2) },
        Visual = new VisualModifiers { Foreground = brush },
    },
    Attached = new Dictionary<Type, object>(1)
    {
        [typeof(GridAttached)] = new GridAttached(row, col, 1, 1),
    },
}
```

`LayoutModifiers` and `VisualModifiers` are the public bucket types on
`ElementModifiers` (spec 034 §A). Construct them directly only in the hot
inner loop — every other call site should keep using the fluent chain.

The reference implementation lives at
`tests/stress_perf/StressPerf.ReactorOptimized/Program.cs`. The naive
sibling at `tests/stress_perf/StressPerf.Reactor/Program.cs` is the
identical workload written the obvious way; diff them to see the
translation in context.

## 4. Cache COM resources across renders

`SolidColorBrush`, `FontFamily`, `CornerRadius`, and similar COM-backed
WinUI types are not free to allocate. Constructing one per cell per render
on a 4,800-cell grid burns minutes of GC time per session.

Hold them in `static readonly` fields (or lazy `??=` singletons that
defer until the WinUI thread exists):

```csharp
private static SolidColorBrush? _greenBrush;
private static SolidColorBrush GreenBrush =>
    _greenBrush ??= new(Color.FromArgb(255, 0, 128, 0));
```

`StressPerf.Reactor` and `StressPerf.ReactorOptimized` both follow this
pattern.

## 5. The gen2 trade-off

Memoization trades short-lived gen0 allocations for longer-lived
gen1/gen2 retention. Many memoized lists across an app can compound gen2
pressure — the worst-case A/B from spec 034's investigation showed +67%
gen2 collections even when bytes/tick dropped. Profile the full GC
profile, not just bytes per tick.

## 6. Profile before optimizing

In-tree perf entry points:

- **`tests/stress_perf/`** — cross-framework workloads (Direct, Bound,
  WPF, DirectX, Reactor, ReactorOptimized, RN-Fabric). `run_stocks_grid_baseline.ps1`
  is the canonical matrix runner; needs admin for ETW.
- **`tests/stress_perf/PresentTracer/`** — ETW Present-rate sampler.
- The Reactor variants emit a `*.report.txt` with FPS, update time,
  memory, GC counts (when `STRESS_PERF_GC=1`).

Don't optimize without numbers. The fluent chain looks expensive on paper
and is invisible in most actual workloads — it's the inner loops at
hundreds of cells that move the meter.

## When NOT to

- Forms, settings dialogs, dashboards with a handful of widgets — the
  fluent chain is the right shape. Don't reach for `UseMemoCells` here;
  the memoization overhead exceeds the savings, and the analyzer-friction
  is real.
- One-shot screens that render once and stay — there's nothing to memoize.
- Animations that change every cell every frame — memo with deps that
  always change is just overhead.

If you're not in a hot list / grid / animation, declarative ergonomics
matter more than allocation count. Write the obvious code.

## See also

- [`skills/dsl-reference.md`](dsl-reference.md) — full DSL surface
- [`skills/design.md`](design.md) — Reactor's design principles
- [`docs/guide/advanced.md`](../docs/guide/advanced.md) — "Hot loops" and
  "Memoizing list cells" sections — the user-facing version of this skill,
  with worked examples
- [`docs/specs/034-element-allocation-reduction.md`](../docs/specs/034-element-allocation-reduction.md)
  — the spec that produced this skill, including the empirical A/B data
