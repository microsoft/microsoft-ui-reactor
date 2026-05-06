---
name: reactor-charts
description: >
  Reactor charting skill. Covers the chart DSL surface (LineChart, BarChart,
  AreaChart, PieChart) and — when ordinary string labels aren't enough — the
  *View extension points that let axis ticks and pie slices render any Reactor
  Element. Read when a user asks for chart customizations beyond color/title/
  axis-label, especially anything involving icons, multi-line tick labels,
  inline legends, or labels-positioned-on-the-chart-itself.
---

# Reactor Charts

The everyday surface lives in `Reactor.D3.Charts` and is documented in
`docs/guide/charting.md`. Quick reminder of the four factories:

```csharp
LineChart(data, x, y)       // continuous line
BarChart (data, x, y)       // vertical bars
AreaChart(data, x, y)       // filled area
PieChart (data, value)      // slices summing to 100%
```

All four return Reactor elements that you can mount anywhere a regular Element
goes. They diff efficiently — re-rendering with new data updates the existing
shapes; no full redraw.

Common fluent knobs (chain-call any subset):

```csharp
.Title("Monthly Revenue")
.SeriesName("Revenue")
.Units(xUnits: "months", yUnits: "USD")
.AxisLabel(ChartAxisType.X, "Month")
.AxisLabel(ChartAxisType.Y, "Revenue (USD)")
.Width(600).Height(250)
.Stroke("#0078D4").StrokeWidth(2.5)
.Fill("#50C878")                       // bars / area / pie slice baseline
.ShowGrid(true).ShowAxes(true)
.DataLabel((point, idx) => $"{point.Revenue:C}")  // string label per point
.Palette(ChartPalette.Categorical)     // pie color palette
```

Accessibility rides for free: every chart implements `IChartAccessibilityData`,
exposing axis ranges, units, point values, and (for pie) slice descriptors via
UIA. **Don't disable this**, even when you customize visuals — see the
"a11y rules" section below.

## When the built-in labels aren't enough

Three cases you can't handle with the string-label APIs (`DataLabel`,
`AxisLabel`, `LabelAccessor`):

1. The label needs an **icon** next to or inside the text.
2. The label needs **multi-line text**, wrapping, or different colors / weights
   per fragment.
3. The label needs to render a **mini sub-tree** (badge, sparkline, button,
   anything Reactor can build).

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

## Anchor primitive (used by `*View` internally; you'll rarely call it directly)

The `*View` methods are built on `Canvas`'s anchor extensions (`CanvasExtensions.cs`):

```csharp
.Canvas(left, top, anchorX, anchorY)   // 0..1 fractions of rendered size
.CenterAt(x, y)                        // sugar for anchor (0.5, 0.5)
```

If you need to position arbitrary content on a Reactor `Canvas` without
knowing its size at build time (overlay markers, custom callouts), use these.
The reconciler subscribes once to `Loaded` + `SizeChanged` per anchored element
and recomputes `Canvas.Left/Top` as `target − anchor × ActualWidth/Height`.
Zero-anchor `(0, 0)` is the legacy fast path with no subscription overhead.

## A11y rules — don't break the screen reader

`*View`-rendered labels are emitted with two defensive defaults applied
automatically by the reconciler:

- `IsHitTestVisible = false` — labels are visual decoration, not interactive
  surface area.
- `AccessibilityView = AccessibilityView.Raw` — labels are *hidden* from the
  UIA tree.

That's intentional. The chart's `IChartAccessibilityData` already describes
the data points; if your custom `Element` were also exposed to UIA, screen
readers would announce slice values twice (once from the chart's structured
description, once from your visible label).

So:

- **Always** set the string `LabelAccessor` (PieChart) or `DataLabel` (line/
  bar/area) when you use `LabelView` — the chart's UIA descriptor still
  reads from those. Custom visuals don't replace the accessible description,
  they augment the visual.
- If your `LabelView` element brings its own UIA peers (`HyperlinkButton`
  inside a label, etc.), those will be `AccessibilityView.Raw` too. If you
  *want* them announced, that's a different design — consider whether the
  chart is really the right home for an interactive control vs. a sibling
  legend.

## When to reach for `*View` (and when not to)

Reach for it when:

- You need an **icon-plus-text** axis tick or slice label.
- You need to render the slice **percent** in the slice itself instead of a
  side legend.
- You're embedding a chart in a dashboard whose typography contract demands
  consistent fonts/colors that the built-in `ChartAxis` style doesn't match.

Skip it when a `string` works:

- Plain numeric formatting → use `DataLabel((d, i) => d.Value.ToString("C"))`.
- Custom number-to-string for ticks → use built-in tick formatting (the
  default already calls `Fmt(t)` which handles short numbers cleanly).
- Just changing color/font of a built-in label — that's not exposed yet;
  if you need it, file an issue rather than dropping to `*View` for a
  one-property override.

## Reading list

- `docs/guide/charting.md` — full user-facing chart guide.
- `src/Reactor/Charting/Charts.cs` — `ChartElement<T>` / `PieChartElement<T>`
  fluent API. The `*View` methods live near the bottom of each.
- `src/Reactor/Charting/D3Charts.cs` — lower-level d3 primitives
  (`D3Pie`, `D3Axes`, `D3Grid`). `D3Axes` is where the optional `xTickLabel`
  / `yTickLabel` delegates plug in.
- `src/Reactor/Elements/CanvasExtensions.cs` — `CenterAt` and the anchor
  overload of `Canvas`.
