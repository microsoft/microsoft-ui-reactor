using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// A donut chart that smoothly animates between different datasets. Wedges
/// expand, shrink, and transition using D3Ease and D3Interpolate to interpolate
/// startAngle/endAngle. A button cycles through datasets.
///
/// Uses Func + UseState + UseEffect + UseRef for fully declarative animation:
/// the timer updates a single eased progress value, triggering a re-render
/// that produces a new D3Canvas with interpolated arc angles.
/// </summary>
public sealed class AnimatedDonutSample : GallerySample
{
    public override string Title => "Animated Donut";
    public override string Description =>
        "A donut chart that animates between datasets — wedges smoothly expand and shrink. " +
        "Uses D3Interpolate for angle interpolation and D3Ease for spring-like transitions.";
    public override string Category => "Animation";

    public override string SourceCode => """
        var (datasetIdx, setDatasetIdx) = ctx.UseState(0);
        var (animT, setAnimT) = ctx.UseState(1.0);
        var oldArcs = ctx.UseRef(ComputeArcs(0));
        var newArcs = ctx.UseRef(ComputeArcs(0));
        var progress = ctx.UseRef(1.0);

        // Timer effect — starts animation when datasetIdx changes
        ctx.UseEffect(() => {
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Tick += (_, _) => {
                progress.Current = Math.Min(progress.Current + 0.025, 1.0);
                setAnimT(D3Ease.Cubic(progress.Current));
                if (progress.Current >= 1.0) timer.Stop();
            };
            if (progress.Current < 1.0) timer.Start();
            return () => timer.Stop();
        }, datasetIdx);

        // Interpolate arcs and render declaratively
        return VStack(8,
            D3Canvas(W, H, [
                ..slices.SelectMany(i => new[] {
                    D3ArcPath(sa, ea, cx, cy, innerRadius: innerR, outerRadius: outerR,
                        fill: Brush(Palette[i]), stroke: ChartSurface, strokeWidth: 2),
                    TextCenter(cx + lx - 12, cy + ly - 8, Labels[i], ...),
                }),
                TextCenter(cx - 50, cy - 10, DatasetNames[datasetIdx], 100, 14, ChartMutedForeground),
            ]),
            Button("Next Dataset ▶", OnNext)
        )
            .AutomationName("Animated Donut")
            .FullDescription("Animated donut chart that transitions between 5 quarterly datasets with smooth arc interpolation.");
        """;

    static readonly string[] Labels = ["Q1", "Q2", "Q3", "Q4"];

    static readonly double[][] Datasets =
    [
        [40, 30, 20, 10],
        [60, 15, 15, 10],
        [20, 50, 20, 10],
        [10, 10, 60, 20],
        [25, 25, 25, 25],
    ];

    static readonly string[] DatasetNames =
        ["Default", "Q1 Surge", "Q2 Surge", "Q3 Surge", "Uniform"];

    const double W = 400, H = 400;
    const double OuterR = 150, InnerR = 80, PadAngle = 0.03;
    const int SliceCount = 4;

    public override Element Render()
    {
        return Func(ctx =>
        {
            double cx = W / 2, cy = H / 2;

            var (datasetIdx, setDatasetIdx) = ctx.UseState(0);
            var (animT, setAnimT) = ctx.UseState(1.0);

            var oldArcs = ctx.UseRef(ComputeArcs(0));
            var newArcs = ctx.UseRef(ComputeArcs(0));
            var progress = ctx.UseRef(1.0);

            // Timer effect — re-runs when datasetIdx changes
            ctx.UseEffect(() =>
            {
                if (progress.Current >= 1.0) return () => { };

                var timer = Microsoft.UI.Dispatching.DispatcherQueue
                    .GetForCurrentThread().CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(16);
                timer.Tick += (_, _) =>
                {
                    progress.Current = Math.Min(progress.Current + 0.025, 1.0);
                    setAnimT(D3Ease.Cubic(progress.Current));
                    if (progress.Current >= 1.0) timer.Stop();
                };
                timer.Start();
                return () => timer.Stop();
            }, datasetIdx);

            void OnNext()
            {
                oldArcs.Current = newArcs.Current;
                newArcs.Current = ComputeArcs((datasetIdx + 1) % Datasets.Length);
                progress.Current = 0.0;
                setAnimT(0.0); // reset before datasetIdx change so first frame shows old arcs
                setDatasetIdx((datasetIdx + 1) % Datasets.Length);
            }

            // Interpolate current arc angles
            var arcs = Enumerable.Range(0, SliceCount).Select(i => (
                Start: D3Interpolate.Number(oldArcs.Current[i].Start, newArcs.Current[i].Start)(animT),
                End: D3Interpolate.Number(oldArcs.Current[i].End, newArcs.Current[i].End)(animT)
            )).ToArray();

            return VStack(8,
                D3Canvas(W, H,
                [
                    .. arcs.SelectMany((a, i) =>
                    {
                        var (lx, ly) = ArcGenerator.Centroid(a.Start, a.End,
                            innerRadius: OuterR + 20, outerRadius: OuterR + 20);
                        return new Element[]
                        {
                            D3ArcPath(a.Start, a.End, cx, cy,
                                outerRadius: OuterR, innerRadius: InnerR, padAngle: PadAngle,
                                fill: Brush(Palette[i % Palette.Count]),
                                stroke: ChartSurface, strokeWidth: 2),
                            TextCenter(cx + lx - 12, cy + ly - 8, Labels[i], 24, 12,
                                Brush(Palette[i % Palette.Count])),
                        };
                    }),
                    TextCenter(cx - 50, cy - 10, DatasetNames[datasetIdx], 100, 14, ChartMutedForeground),
                ]),
                Button("Next Dataset \u25B6", OnNext).Center()
            ).HAlign(HorizontalAlignment.Center).Padding(16)
                .AutomationName("Animated Donut")
                .FullDescription("Animated donut chart that transitions between 5 quarterly datasets with smooth arc interpolation.");
        });
    }

    static (double Start, double End)[] ComputeArcs(int datasetIdx)
    {
        var arcs = PieGenerator.Generate(Datasets[datasetIdx], v => v, sort: false, padAngle: PadAngle);
        return arcs.Select(a => (a.StartAngle, a.EndAngle)).ToArray();
    }
}
