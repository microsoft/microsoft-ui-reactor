# Charting

Charts, the D3 scales, shapes and layouts behind them, and chart accessibility. From the Microsoft.UI.Reactor.Advanced package.

These types ship in the `Microsoft.UI.Reactor.Advanced` package. `Charts` and `D3Charts` build charts; the D3 types are a C# port of the d3 scales, shape generators and layouts you can use to draw your own.

**Learn more:** [Charting](../../charting.md)

## Types

| Type | Description |
| --- | --- |
| [Chart accessibility](chart-accessibility.md) | Descriptors, palettes and the contrast checks that make charts accessible. |
| [D3: arrays and statistics](d3-data.md) | Binning, bisection, extents, grouping, ranges, ticks, statistics and random numbers. |
| [D3: color, format and interpolation](d3-color-and-format.md) | Colors, number formatting, easing and interpolators. |
| [D3: contours, Delaunay and polygons](d3-geometry.md) | Contour generators, Delaunay triangulation, Voronoi diagrams and polygon helpers. |
| [D3: layouts](d3-layouts.md) | Hierarchy, treemap, pack, partition, chord, Sankey and force layouts. |
| [D3: scales](d3-scales.md) | Scales that map data values to positions, sizes and colors. |
| [D3: shapes](d3-shapes.md) | Line, area, arc, pie, stack, link and symbol generators, curves and the path builder. |
| [Enums](enums.md) | The enumerations in this category. |
| [`ChartElement<T>`](ChartElement.md) |  |
| [`ChartHandle<T>`](ChartHandle.md) | Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios. |
| [`ChartRange`](ChartRange.md) | Represents a range of x-axis values selected via brush interaction. |
| [`Charts`](Charts.md) | Static factory methods that integrate D3 charting into Reactor's declarative model. |
| [`D3Charts`](D3Charts.md) | Static factory methods for declarative D3 chart drawing. |
| [`ForcedColorsTheme`](ForcedColorsTheme.md) | Holds the actual system high-contrast colors queried from `UISettings.GetColorValue`. |
| [`ForceGraphElement`](ForceGraphElement.md) | Force-directed graph element for Reactor's virtual tree. |
| [`ForceGraphHandle`](ForceGraphHandle.md) | Exposes the live simulation and WinUI elements so callers can implement drag, animation, hover, or any other interaction outside the library. |
| [`PieChartElement<T>`](PieChartElement.md) |  |
| [`PieChartHandle<T>`](PieChartHandle.md) | Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios. |
| [`PieSliceLayout`](PieSliceLayout.md) | Layout metadata for a single pie slice, supplied to `PieChartElement.LabelView` callbacks. |
| [`TreeChartElement<T>`](TreeChartElement.md) | Tree diagram element for Reactor's virtual tree — renders as native D3 elements. |
| [`TreeChartHandle`](TreeChartHandle.md) | Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios. |
