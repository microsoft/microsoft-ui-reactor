---
title: "Charting"
description: "Charts, the D3 scales, shapes and layouts behind them, and chart accessibility. From the Microsoft.UI.Reactor.Advanced package."
sidebar:
  label: "Overview"
---

These types ship in the `Microsoft.UI.Reactor.Advanced` package. `Charts` and `D3Charts` build charts; the D3 types are a C# port of the d3 scales, shape generators and layouts you can use to draw your own.

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## Types [#types]

| Type | Description |
| --- | --- |
| [Chart accessibility](/microsoft-ui-reactor/main/reference/charting/chart-accessibility) | Descriptors, palettes and the contrast checks that make charts accessible. |
| [D3: arrays and statistics](/microsoft-ui-reactor/main/reference/charting/d3-data) | Binning, bisection, extents, grouping, ranges, ticks, statistics and random numbers. |
| [D3: color, format and interpolation](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format) | Colors, number formatting, easing and interpolators. |
| [D3: contours, Delaunay and polygons](/microsoft-ui-reactor/main/reference/charting/d3-geometry) | Contour generators, Delaunay triangulation, Voronoi diagrams and polygon helpers. |
| [D3: layouts](/microsoft-ui-reactor/main/reference/charting/d3-layouts) | Hierarchy, treemap, pack, partition, chord, Sankey and force layouts. |
| [D3: scales](/microsoft-ui-reactor/main/reference/charting/d3-scales) | Scales that map data values to positions, sizes and colors. |
| [D3: shapes](/microsoft-ui-reactor/main/reference/charting/d3-shapes) | Line, area, arc, pie, stack, link and symbol generators, curves and the path builder. |
| [Enums](/microsoft-ui-reactor/main/reference/charting/enums) | The enumerations in this category. |
| [`ChartElement<T>`](/microsoft-ui-reactor/main/reference/charting/ChartElement) |  |
| [`ChartHandle<T>`](/microsoft-ui-reactor/main/reference/charting/ChartHandle) | Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios. |
| [`ChartRange`](/microsoft-ui-reactor/main/reference/charting/ChartRange) | Represents a range of x-axis values selected via brush interaction. |
| [`Charts`](/microsoft-ui-reactor/main/reference/charting/Charts) | Static factory methods that integrate D3 charting into Reactor's declarative model. |
| [`D3Charts`](/microsoft-ui-reactor/main/reference/charting/D3Charts) | Static factory methods for declarative D3 chart drawing. |
| [`ForcedColorsTheme`](/microsoft-ui-reactor/main/reference/charting/ForcedColorsTheme) | Holds the actual system high-contrast colors queried from `UISettings.GetColorValue`. |
| [`ForceGraphElement`](/microsoft-ui-reactor/main/reference/charting/ForceGraphElement) | Force-directed graph element for Reactor's virtual tree. |
| [`ForceGraphHandle`](/microsoft-ui-reactor/main/reference/charting/ForceGraphHandle) | Exposes the live simulation and WinUI elements so callers can implement drag, animation, hover, or any other interaction outside the library. |
| [`PieChartElement<T>`](/microsoft-ui-reactor/main/reference/charting/PieChartElement) |  |
| [`PieChartHandle<T>`](/microsoft-ui-reactor/main/reference/charting/PieChartHandle) | Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios. |
| [`PieSliceLayout`](/microsoft-ui-reactor/main/reference/charting/PieSliceLayout) | Layout metadata for a single pie slice, supplied to `PieChartElement.LabelView` callbacks. |
| [`TreeChartElement<T>`](/microsoft-ui-reactor/main/reference/charting/TreeChartElement) | Tree diagram element for Reactor's virtual tree — renders as native D3 elements. |
| [`TreeChartHandle`](/microsoft-ui-reactor/main/reference/charting/TreeChartHandle) | Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios. |
