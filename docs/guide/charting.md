
# Charting

ReactorD3 brings data visualization to Reactor. The chart DSL provides high-level
`LineChart`, `BarChart`, `AreaChart`, and `PieChart` factories that produce
standard Reactor elements. You bind data, configure appearance with a fluent
API, and the chart renders as native WinUI shapes on a Canvas.

Add a reference to `ReactorD3.csproj` and import the DSL:

```csharp
using Reactor.D3.Charts;
using static Reactor.D3.Charts.Charts;
```

## Line Chart

`LineChart` draws a continuous line through your data points. Pass a
collection and accessor functions for X and Y:

```csharp
class LineChartDemo : Component
{
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 120), new(2, 180), new(3, 150),
            new(4, 220), new(5, 310), new(6, 280),
            new(7, 350), new(8, 400), new(9, 380),
            new(10, 420), new(11, 460), new(12, 510)
        };

        return VStack(12,
            SubHeading("Line Chart"),
            LineChart(data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue — Line")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .AxisLabel(ChartAxisType.X, "Month")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Stroke("#0078D4").StrokeWidth(2.5)
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
```

![Line chart](images/charting/line-chart.png)

The chart auto-scales axes to fit your data with `.Nice()` rounding. Toggle
grid lines with `.ShowGrid()` and axis labels with `.ShowAxes()`.

## Bar Chart

`BarChart` renders vertical bars. Each data point becomes a bar whose height
maps to the Y value:

```csharp
class BarChartDemo : Component
{
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 340), new(2, 420), new(3, 510), new(4, 380)
        };

        return VStack(12,
            SubHeading("Bar Chart"),
            BarChart(data, d => d.Month, d => d.Revenue)
                .Title("Quarterly Revenue — Bar")
                .SeriesName("Revenue")
                .Units("quarters", "USD")
                .AxisLabel(ChartAxisType.X, "Quarter")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Fill("#50C878")
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
```

![Bar chart](images/charting/bar-chart.png)

Bar width is calculated automatically based on the number of data points.
Use `.Fill()` to set the bar color and `.FillOpacity()` to control
transparency.

## Area Chart

`AreaChart` fills the region between the data line and the baseline. It
combines a filled area with a line overlay:

```csharp
class AreaChartDemo : Component
{
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 50), new(2, 120), new(3, 200),
            new(4, 350), new(5, 480), new(6, 600),
            new(7, 720), new(8, 850), new(9, 1020),
            new(10, 1150), new(11, 1300), new(12, 1500)
        };

        return VStack(12,
            SubHeading("Area Chart"),
            AreaChart(data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue — Area")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .AxisLabel(ChartAxisType.X, "Month")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Stroke("#9B59B6").Fill("#9B59B6")
                .FillOpacity(0.2)
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
```

![Area chart](images/charting/area-chart.png)

Use `.FillOpacity()` to control area transparency. A low value (0.15--0.3)
lets grid lines show through while still filling the shape.

## Pie Chart

`PieChart` divides data into proportional arcs. Set `.InnerRadius()` > 0
for a donut chart:

```csharp
class PieChartDemo : Component
{
    public override Element Render()
    {
        var data = new CategoryData[]
        {
            new("Engineering", 42),
            new("Marketing", 18),
            new("Sales", 25),
            new("Support", 15)
        };

        return VStack(12,
            SubHeading("Pie Chart"),
            PieChart(data, d => d.Value, d => d.Name)
                .Title("Team Distribution")
                .Description("Pie chart showing team size across Engineering, Marketing, Sales, and Support.")
                .Width(300).Height(300)
                .InnerRadius(60)
                .PadAngle(0.03)
        ).Padding(24);
    }
}
```

![Pie chart](images/charting/pie-chart.png)

Pass a label accessor to display text at each arc's centroid. Colors cycle
through the Category10 palette by default — override with `.SetColors()`.

## Chart Configuration

All chart types share a common set of builder methods:

| Method | Default | Purpose |
|--------|---------|---------|
| `.Width(n)` | 400 | Canvas width in pixels |
| `.Height(n)` | 300 | Canvas height in pixels |
| `.Margin(t, r, b, l)` | 20, 20, 30, 40 | Axis/label margins |
| `.Stroke(color)` | `#4285f4` | Line/border color |
| `.Fill(color)` | `#4285f4` | Fill color |
| `.StrokeWidth(n)` | 2 | Line thickness |
| `.FillOpacity(n)` | 0.3 | Fill transparency (0--1) |
| `.ShowAxes(bool)` | true | Show axis lines and labels |
| `.ShowGrid(bool)` | true | Show horizontal grid lines |

Colors accept any CSS-style string: `#RGB`, `#RRGGBB`, `rgb(r,g,b)`, or
named colors like `steelblue`.

## Binding Data to State

Charts are standard Reactor elements. When state changes, the chart re-renders
with the new data:

```csharp
class CombinedChartDemo : Component
{
    public override Element Render()
    {
        var (year, setYear) = UseState(0);
        var years = new[] { "2024", "2025" };

        var data2024 = new SalesPoint[]
        {
            new(1, 100), new(2, 140), new(3, 180),
            new(4, 200), new(5, 260), new(6, 300)
        };
        var data2025 = new SalesPoint[]
        {
            new(1, 160), new(2, 220), new(3, 280),
            new(4, 320), new(5, 390), new(6, 450)
        };

        var data = year == 0 ? data2024 : data2025;

        return VStack(12,
            SubHeading("Interactive Chart"),
            ComboBox(years, year, setYear),
            AreaChart(data, d => d.Month, d => d.Revenue)
                .Title("Revenue by Year")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .Interactive()
                .Width(600).Height(250)
                .Stroke("#0078D4").Fill("#0078D4")
                .FillOpacity(0.15)
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
```

![Interactive chart](images/charting/combined-chart.png)

Use [hooks](hooks.md) like `UseState` to drive data selection. The chart
rebuilds on every render — keep datasets small (under ~1000 points) for
smooth interaction.

## Dynamic Data Updates

Charts are native Reactor elements, so changing state is all you need. Use
`UseState`, `UseReducer`, or any hook to update the data — the reconciler
diffs the old and new element trees and patches only what changed:

```csharp
class DynamicDataDemo : Component
{
    public override Element Render()
    {
        var (points, updatePoints) = UseReducer(
            Enumerable.Range(1, 8)
                .Select(i => new SalesPoint(i, Random.Shared.Next(50, 500)))
                .ToList());

        return VStack(12,
            SubHeading("Dynamic Data"),
            Button("Randomize", () => updatePoints(_ =>
                Enumerable.Range(1, 8)
                    .Select(i => new SalesPoint(i, Random.Shared.Next(50, 500)))
                    .ToList())),
            BarChart<SalesPoint>(points, d => d.Month, d => d.Revenue)
                .Title("Dynamic Revenue Data")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .Width(600).Height(250)
                .Fill("#E74C3C")
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
```

![Dynamic data](images/charting/dynamic-data.png)

For high-frequency updates (60fps streaming), use `OnReady` to get a
handle that exposes the underlying `Canvas` for direct manipulation.

## Chart Accessibility

Charts are fully accessible out of the box. Add `.Title()` and
`.SeriesName()` for screen-reader identification, `.Units()` for axis
annotations, and `.Interactive()` for keyboard navigation:

```csharp
/// <summary>
/// Canonical accessible chart pattern — demonstrates all recommended accessibility
/// modifiers for both static and interactive charts. Follow this pattern to ensure
/// charts are fully accessible to screen readers, keyboard users, and users who
/// need forced-colors or reduced-motion.
/// </summary>
class AccessibleChartDemo : Component
{
    public override Element Render()
    {
        var data = new SalesPoint[]
        {
            new(1, 120), new(2, 180), new(3, 150),
            new(4, 220), new(5, 310), new(6, 280)
        };

        return VStack(12,
            SubHeading("Accessible Chart"),

            // Static accessible chart: Title + SeriesName + Units
            LineChart(data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue 2024")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .AxisLabel(ChartAxisType.X, "Month")
                .AxisLabel(ChartAxisType.Y, "Revenue (USD)")
                .Width(600).Height(250)
                .Stroke("#0078D4").StrokeWidth(2.5)
                .ShowGrid(true).ShowAxes(true),

            // Interactive accessible chart: adds keyboard nav and point invocation
            BarChart(data, d => d.Month, d => d.Revenue)
                .Title("Monthly Revenue — Interactive")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .Interactive()
                .Width(600).Height(250)
                .Fill("#50C878")
                .ShowGrid(true).ShowAxes(true)
        ).Padding(24);
    }
}
```

![Accessible chart](images/charting/accessible-chart.png)

### Accessibility Modifiers

| Method | Purpose |
|--------|---------|
| `.Title(str)` | Visible title and accessible name |
| `.Description(str)` | Override auto-generated summary |
| `.SeriesName(str)` | Name the data series |
| `.Units(x, y)` | Axis unit annotations (e.g., "months", "USD") |
| `.AxisLabel(axis, str)` | Explicit axis label |
| `.Interactive()` | Enable keyboard navigation and virtual focus |
| `.OnPointInvoke(handler)` | Callback when Enter/Space pressed on a point |
| `.AlternateView(element)` | Toggle between chart and data table (T key) |
| `.Palette(palette)` | Curated colorblind-safe palette |
| `.SeriesShapes(shapes)` | Marker shapes for double-encoding |
| `.SeriesDashes(dashes)` | Dash patterns for double-encoding |

Screen readers announce the chart type, series count, data range, and
individual point values. Keyboard users navigate with arrow keys, invoke
points with Enter, and toggle an alternate data-table view with T.

## Low-Level Drawing

For custom visualizations, ReactorD3 provides shape generators and a Canvas
DSL. Import `using static Reactor.D3.Charts.D3` for:

- **`D3Canvas(w, h, children)`** — create a drawing surface
- **`D3Rect`, `D3Circle`, `D3Line`, `D3Path`** — primitive shapes
- **`D3Text`, `D3TextRight`, `D3TextCenter`** — positioned text
- **`D3LinePath<T>`, `D3AreaPath<T>`, `D3ArcPath`** — generator helpers
- **`D3Axes`, `D3Grid`, `D3Legend`** — axis/legend composites
- **`LinearScale`, `BandScale`, `LogScale`** — map data to pixels

## Scale Types

| Scale | Purpose |
|-------|---------|
| `LinearScale` | Continuous numeric mapping with `.Nice()` and `.Ticks()` |
| `BandScale<T>` | Categorical mapping with bandwidth (bar charts) |
| `LogScale` | Logarithmic mapping for exponential data |
| `PowScale` | Power/sqrt mapping |
| `OrdinalScale<T>` | Discrete-to-discrete mapping |

## Tips

**Start with the high-level DSL.** `LineChart`, `BarChart`, `AreaChart`, and
`PieChart` cover most dashboard needs without touching scales or generators.

**Use `.ShowGrid(true)` for readability.** Grid lines make it much easier
to read values from a chart, especially line and area charts.

**Keep datasets under 1000 points.** Each data point creates WinUI shapes on
a Canvas. For larger datasets, aggregate or sample before charting.

**Just change state for live data.** Charts diff efficiently — updating
state triggers the reconciler to patch only what changed. For 60fps
escape hatches, use `OnReady` to access the underlying Canvas directly.

**Use donut charts (`InnerRadius > 0`) for proportional data.** The center
space works well for displaying a total or label.

## Next Steps

- **[Animation](animation.md)** — Previous: transitions, keyframes, interaction states
- **[Advanced Patterns](advanced.md)** — Next: error boundaries, memoization, and performance tuning
- **[Collections](collections.md)** — Bind chart data from virtualized lists and observable collections
- **[Effects and Lifecycle](effects.md)** — Load chart data asynchronously with UseEffect
- **[Styling and Theming](styling.md)** — Use theme tokens for chart colors that adapt to dark mode
