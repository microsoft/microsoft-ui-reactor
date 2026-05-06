// Central registry of all gallery samples

namespace ReactorCharting.Gallery;

/// <summary>
/// Registers all gallery samples. Add new samples here to include them in the gallery.
/// </summary>
public static class SampleRegistry
{
    public static GallerySample[] All { get; } =
    [
        // ── Bars ──────────────────────────────────────────────
        new BarChartSample(),
        new HorizontalBarChartSample(),
        new StackedBarChartSample(),
        new GroupedBarChartSample(),
        new DivergingBarChartSample(),

        // ── Lines ─────────────────────────────────────────────
        new LineChart(),
        new MultiLineChart(),
        new LineChartMissingData(),
        new SlopeChart(),
        new CandlestickChart(),
        new WeeklyForecastSample(),

        // ── Areas ─────────────────────────────────────────────
        new AreaChart(),
        new StackedAreaChart(),
        new StreamgraphChart(),
        new DifferenceChart(),
        new RidgePlot(),

        // ── Radial ────────────────────────────────────────────
        new PieChartSample(),
        new DonutChartSample(),
        new BrowserSharePieSample(),

        // ── Dots ──────────────────────────────────────────────
        new ScatterplotSample(),
        new BubbleChartSample(),
        new DotPlotSample(),

        // ── Analysis ──────────────────────────────────────────
        new HistogramSample(),
        new BoxPlotSample(),

        // ── Hierarchies ───────────────────────────────────────
        new TidyTreeSample(),
        new ClusterDendrogramSample(),
        new TreemapSample(),
        new CirclePackingSample(),
        new SunburstSample(),
        new IcicleSample(),
        new IndentedTreeSample(),

        // ── Networks ──────────────────────────────────────────
        new ForceDirectedGraphSample(),
        new ChordDiagramSample(),
        new SankeyDiagramSample(),
        new ArcDiagramSample(),

        // ── Controls ──────────────────────────────────────────
        new ComponentHierarchySample(),
        new WorkflowPipelineSample(),
        new OrgChartSample(),
        new NestedListExplorerSample(),

        // ── Interactive ───────────────────────────────────────
        new DonutMixerSample(),
        new CurveExplorerSample(),

        // ── Animation ─────────────────────────────────────────
        new BarChartRaceSample(),
        new AnimatedDonutSample(),

        // ── Design ────────────────────────────────────────────────
        new ColorPageSample(),
        new ThemedChartSample(),
    ];
}
