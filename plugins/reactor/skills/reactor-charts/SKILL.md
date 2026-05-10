---
name: reactor-charts
description: "Reactor charts and data visualization — choosing a chart type (line, bar, area, donut, `TreeChart`, `ForceGraph`), the chart DSL, `LabelView` / `XTickLabelView` / `YTickLabelView` for icon-plus-text and rich labels, and the visualization-best-practices rules. Use when adding charts or designing visualizations."
---

# Reactor Charts

The chart surface lives in `Reactor.D3.Charts` (full guide:
`docs/guide/charting.md`). This skill is the AI-agent quick reference —
chart-choice rules, the new `*View` extension points, and the a11y/accuracy
rails you should keep on by default.

## Choose the right chart first

Most "my chart looks bad" problems are wrong-chart problems, not styling
problems. Cleveland & McGill's perceptual ranking (1984) — accuracy of
graphical judgment, best to worst:

> position on a common scale > position on non-aligned scales > length /
> direction / angle > area > volume > shading / color saturation

That ordering is why bars and dots win and why pies lose. Translate it into
rules of thumb:

| Chart | Use when | Avoid when |
|---|---|---|
| **Bar** | Discrete categories, time buckets (Q1..Q4), ranked comparison. **Default when in doubt.** | (Use a different chart if the data is continuous, e.g. dense time-series — line is clearer.) |
| **Line** | Continuous data, many x-values, trends/slopes, autocorrelated series (prices, sensor readings). | Few categorical observations — use bar. |
| **Area** | Cumulative magnitude or parts-of-whole over time. | Series cross frequently — overlap occludes; switch to line. |
| **Pie** | Parts-of-a-whole **and** ≤5 slices **and** large differences **and** proportions land near recognizable fractions (½, ¼, ⅓). | Anything else. Ranking, change-over-time, negative values, precise comparison — use a sorted bar chart. |
| **Donut** (`PieChart` with `InnerRadius > 0`) | Same conditions as pie. The inner hole gives you a place for a center label (total, current selection) — useful when the chart sits inside a dashboard. | Same as pie. The hole doesn't fix pie's ranking weakness. |
| **Tree** (`TreeChart`) | Hierarchical data: org charts, file/folder structures, taxonomies, decision trees. Visualizes parent→child links. | Wide hierarchies (>~30 nodes per level) — the tree gets unreadable; consider a treemap (use the `D3.Layout.Treemap` primitive — no top-level factory yet). |
| **Force graph** (`ForceGraph`) | Relationship networks: dependency graphs, social graphs, knowledge graphs. Shows which nodes cluster. | Hierarchical data — use `TreeChart` (an explicit layout always reads better when one exists). Static print/snapshot output — force layouts converge differently each run. |

When the user asks for a pie chart, push back unless all four pie conditions
hold. A sorted bar chart usually wins. (Few/Tufte say "almost never pie";
Cairo/Wilke allow the narrow case above. Default to "no" and concede when
the conditions are met.)

**Charts not yet exposed as top-level factories:** Sankey, Treemap, Cluster
dendrograms, partition/sunburst, stratified hierarchies. The math primitives
exist in `src/Reactor/Charting/D3/Layout/` (`Sankey.cs`, `Treemap.cs`,
`Cluster.cs`, `Stratify.cs`, `TreeLayout.cs`) and can be composed with
`D3Canvas` + the d3 shape generators. If a user asks for one of these and
isn't comfortable assembling D3 primitives directly, suggest filing a
factory request rather than hand-rolling — the resulting chart will lack
the shared accessibility surface.

## The DSL — quick reference

```csharp
LineChart (data, x, y)              // continuous line
BarChart  (data, x, y)              // vertical bars (always anchored at zero — see pitfalls)
AreaChart (data, x, y)              // filled area
PieChart  (data, value)             // slices summing to 100%
PieChart  (data, value)             // donut variant: chain .InnerRadius(40+)
    .InnerRadius(60)
TreeChart (root, children)          // hierarchical (org chart, file tree, taxonomy)
ForceGraph(nodes, links)            // network graph (dependencies, relationships)
```

Common fluent knobs (chain-call any subset):

```csharp
.Title("Monthly Revenue")
.SeriesName("Revenue")
.Units(xUnits: "months", yUnits: "USD")
.AxisLabel(ChartAxisType.X, "Month")
.AxisLabel(ChartAxisType.Y, "Revenue (USD)")
.Width(600).Height(250)
.Stroke("#0078D4").StrokeWidth(2.5)
.Fill("#50C878")                                  // bars / area / pie slice baseline
.ShowGrid(true).ShowAxes(true)
.DataLabel((point, idx) => $"{point.Revenue:C}")  // string label per point
.Palette(ChartPalette.Categorical)                // pie color palette
```

`AxisLabel` text and `Title` are reserved for *labeling the chart*, not
labeling individual data points or ticks — for those, see the next two
sections.

## Make D3 do the work

Charts compose D3-style scales and generators internally. When you reach
under the DSL hood (or hand-roll Canvas drawings), follow these:

- **Scales map domain → range — never compute pixel positions by hand.** A
  `LinearScale` from `[min, max]` to `[plotLeft, plotRight]` is the boundary
  between "data" and "pixels". Mixing them is the #1 source of "off by 5px"
  bugs.
- **Pick the right scale type for the data.** Linear for quantitative,
  log for exponential, time for dates, ordinal/band for categories. Linear-
  on-exponential hides patterns; log-on-linear inflates them.
- **Always `.nice()` quantitative domains.** Axis bounds should round to
  human-friendly numbers (0/100/200, not 13.7/187.4). Pair with `.ticks(n)`
  as a *suggestion*, not a hard count — D3 picks readable intervals.
- **Reserve margins.** Long y-axis labels and multi-line tick `*View`
  elements need gutter. The Bostock margin convention
  (`{top, right, bottom, left}`) is the safe default; if you customize tick
  labels (next section), measure the longest one and grow the margin.
- **Don't reinvent the axis.** The built-in axis generator handles tick
  selection, formatting, alignment, label rotation. Custom tick code is
  where charts go to die.

## When string labels aren't enough — `*View`

Three cases the string-label APIs (`DataLabel`, `AxisLabel`, `LabelAccessor`)
can't handle:

1. The label needs an **icon** next to or inside the text.
2. The label needs **multi-line text**, wrapping, or different colors /
   weights per fragment.
3. The label needs to render a **mini sub-tree** (badge, sparkline, mini
   button, anything Reactor can build).

For these, reach for the `*View` extensions. They take a render delegate and
substitute its returned `Element` for the built-in `TextBlock` at the same
anchor position:

```csharp
// Pie slice: replace text label with arbitrary element rendered at the slice centroid
PieChart(data, d => d.Value)
    .LabelView((d, layout) => HStack(
        FontIcon(IconForCategory(d.Category)).FontSize(12),
        TextBlock($"{layout.Fraction:P0}").FontWeight(FontWeights.SemiBold)));

// Axis tick: replace numeric tick label with arbitrary element
LineChart(data, x, y)
    .XTickLabelView(t => VStack(
        TextBlock(MonthName((int)t)).FontSize(11),
        TextBlock("month").FontSize(8).Foreground(Theme.SecondaryText)))
    .YTickLabelView(t => TextBlock($"${t:N0}").FontFamily("Cascadia Mono"));
```

### `PieChartElement<T>.LabelView(Func<T, PieSliceLayout, Element>)`

The render delegate receives:

- `T data` — the slice's source data item.
- `PieSliceLayout layout` — a `readonly record struct` with everything you
  need to render a label that knows its slice geometry:
  - `Index, Value, Fraction` (0..1 of total)
  - `CentroidX, CentroidY` (absolute canvas coords — already applied via
    `CenterAt`, you don't need to apply them yourself)
  - `StartAngle, EndAngle` (radians, clockwise from 12 o'clock — d3 semantics)
  - `InnerRadius, OuterRadius`
  - `Color` — the resolved `D3Color` from the chart's palette, so your label
    can echo the slice color without a separate lookup.

The returned element is auto-anchored at the centroid via `CenterAt`. You do
**not** need a known size at construction time; the reconciler recomputes
position after layout from `ActualWidth`/`ActualHeight`.

### `ChartElement<T>.XTickLabelView(Func<double, Element>)` and `YTickLabelView`

Same pattern for axis ticks. The delegate receives the tick's domain value
(`double`); X labels are anchored horizontally centered on the tick mark, Y
labels right-anchored to the axis edge and vertically centered.

## Direct labeling vs legend vs tooltip

A label-placement decision tree (Tufte, Cleveland — direct labeling raises
the data-ink ratio and avoids legend ping-pong):

1. **Direct labels first.** Label series at the end of each line, label pie
   slices on the chart, label bars above the bar. `LabelView` /
   `*TickLabelView` exist precisely so you can do this without re-templating
   the whole chart. Eyes don't ping-pong from line to legend; print works;
   screen readers work.
2. **Use a legend** only when direct labeling would collide — many series,
   tightly-packed lines, repeating palette across small multiples. Treat it
   as a fallback, not a default.
3. **Use a tooltip** for precise values on dense data, *in addition to* —
   never *instead of* — direct labels or a legend. Tooltips fail print,
   screen readers, mobile-tap accuracy, and keyboard navigation.
4. **Pie slices**: large slices get inside-labels; small slices (<~5%) need
   outside leader lines or get rolled into "Other". Don't label tiny slices
   inline — they overlap.
5. **Backfire cases**: dense scatterplots, tightly-packed bars, many
   overlapping line endpoints. Label collisions there are worse than a
   legend; switch to legend.

## Anchor primitive (used by `*View` internally)

The `*View` methods are built on `Canvas`'s anchor extensions
(`CanvasExtensions.cs`). You'll rarely call them directly, but if you need to
position arbitrary content on a Reactor `Canvas` without knowing its size at
build time (overlay markers, custom callouts), use these:

```csharp
.Canvas(left, top, anchorX, anchorY)   // 0..1 fractions of rendered size
.CenterAt(x, y)                        // sugar for anchor (0.5, 0.5)
```

The reconciler subscribes once to `Loaded + SizeChanged` per anchored element
and recomputes `Canvas.Left/Top` as `target − anchor × ActualWidth/Height`.
Zero-anchor `(0, 0)` is the legacy fast path with no subscription overhead.

## Accessibility — beyond the framework defaults

Charts implement `IChartAccessibilityData`, which exposes axis ranges, units,
point values, and (for pie) slice descriptors via UIA. Don't disable that.
Beyond that, your responsibilities:

- **Color is never the sole channel** (WCAG 1.4.1). Pair color with shape,
  pattern, dash style, or — best — a direct text label. Useful for series
  identification when the user can't distinguish two of your colors.
- **Color-blind-safe palettes.** Use `ChartPalette.Categorical` (Reactor's
  curated set; Okabe-Ito-style). Avoid red/green pairings. Avoid rainbow
  for ordinal data — use a sequential ramp (Viridis-style) instead.
- **Contrast.** WCAG 1.4.11 — 3:1 for non-text essential graphics, 4.5:1
  for text labels. The default chart palette meets this against the
  framework's surface tokens; if you override with `.Stroke("#…")` /
  `.Fill("#…")`, check contrast against `Theme.SurfaceBackground`.
- **Screen-reader fallback.** UIA structured data is good but not enough
  for screen-reader-only users — a hidden, expandable data table next to
  the chart is the pattern Tenon and the W3C WAI accessibility guidance
  recommend. Build it from the same data the chart consumed.
- **Keyboard nav.** Data points should be focusable in reading order, and
  focus should announce category + value. The `IChartAccessibilityData`
  surface drives this; if you turn the chart `Interactive(false)` you lose
  it — keep it on unless there's a reason.
- **`*View` defaults.** Custom labels are auto-stamped with
  `IsHitTestVisible=false` and `AccessibilityView=Raw` so they don't
  duplicate the structured UIA description. **Always** keep the string
  `LabelAccessor` (PieChart) or `DataLabel` (line/bar/area) set — those
  feed UIA. Custom visuals augment the visual; they don't replace the
  accessible description.

## Common pitfalls — refuse to ship these

The most-cited visualization mistakes (Tufte, Few, Cairo, Wilke). If a user
asks for one, push back with the alternative.

- **Truncated bar baselines.** Bars MUST start at zero. A 3% gap with a
  truncated baseline visually looks like 300%. Cairo's *How Charts Lie*
  spends a chapter on this. Line charts may truncate because shape, not
  height, conveys meaning — bars never.
- **Dual y-axes.** Fabricates correlation between unrelated series. Use two
  small multiples or normalize both series to an index (e.g. `(value /
  baseline) × 100`) so they share a single axis.
- **3D / exploded pies, drop shadows, gradients on bars.** Tufte's
  "chartjunk." 3D pies are actively misleading — angle distortion changes
  the visual proportion of slices.
- **Too many series.** >5–7 lines, >7 pie slices. Group the tail into
  "Other" or split into small multiples.
- **Rainbow / unordered categorical palettes on ordinal data.** When the
  variable has order (low/medium/high, age buckets, sentiment), use a
  sequential ramp. A categorical palette implies "different kind", not
  "different magnitude".
- **Pie chart for ranking.** Pies hide rank — angle judgments are
  imprecise. A sorted bar chart shows it directly.

## When to reach for `*View` (and when not to)

Reach for it when:

- You need an **icon-plus-text** axis tick or slice label.
- You need to render the slice **percent** in the slice itself instead of a
  side legend (direct labeling, see above).
- You're embedding a chart in a dashboard whose typography contract demands
  fonts/colors the built-in `ChartAxis` style doesn't match.

Skip it when a `string` works:

- Plain numeric formatting → `DataLabel((d, i) => d.Value.ToString("C"))`.
- Custom number-to-string for ticks → built-in tick formatting (`Fmt(t)`
  handles short numbers cleanly).
- Just changing color/font of a built-in label — that's not exposed yet;
  if you need it, file an issue rather than dropping to `*View` for a
  one-property override.

## Reading list

- `docs/guide/charting.md` — full user-facing chart guide.
- `src/Reactor/Charting/Charts.cs` — `ChartElement<T>` / `PieChartElement<T>`
  fluent API. `*View` methods sit near the bottom of each class.
- `src/Reactor/Charting/Charts.Tree.cs` — `TreeChart` and `ForceGraph`
  factories + their elements.
- `src/Reactor/Charting/D3Charts.cs` — d3 primitives (`D3Pie`, `D3Axes`,
  `D3Grid`, `D3Canvas`). `D3Axes` is where the optional `xTickLabel` /
  `yTickLabel` delegates plug in.
- `src/Reactor/Charting/D3/Layout/` — composable layout algorithms
  (`Sankey.cs`, `Treemap.cs`, `Cluster.cs`, `Stratify.cs`, `TreeLayout.cs`).
  No top-level factory wrapping these yet; they're the building blocks for
  one when the time comes.
- `src/Reactor/Elements/CanvasExtensions.cs` — `CenterAt` and the anchor
  overload of `Canvas`.

External (read these once if charting is new to you):

- Cleveland & McGill (1984), *Graphical Perception* — the perceptual ranking.
- Edward Tufte, *The Visual Display of Quantitative Information* — data-ink ratio, chartjunk.
- Stephen Few, *Show Me the Numbers* — practitioner playbook for business charts.
- Claus Wilke, *Fundamentals of Data Visualization* — free online; the modern reference.
- Alberto Cairo, *How Charts Lie* — the pitfalls chapter is required reading.
- d3js.org docs on `d3-scale`, `d3-axis`, `d3-shape` — even when working through Reactor's wrappers.
