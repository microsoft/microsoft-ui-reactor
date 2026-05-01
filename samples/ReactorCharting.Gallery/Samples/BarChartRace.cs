using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// An animated horizontal bar chart that races through yearly data —
/// bars grow, shrink, and re-sort as the year advances. Uses Func + UseState +
/// UseEffect for fully declarative animation: a timer updates eased progress,
/// triggering re-renders that produce a D3Canvas with interpolated bar positions.
/// </summary>
public sealed class BarChartRaceSample : GallerySample
{
    public override string Title => "Bar Chart Race";
    public override string Description =>
        "An animated bar chart race where horizontal bars grow, shrink, and re-sort over time. " +
        "Uses D3Ease.Cubic for smooth transitions and UseEffect for timer lifecycle.";
    public override string Category => "Animation";

    public override string SourceCode => """
        var (yearIdx, setYearIdx) = ctx.UseState(0);
        var (animT, setAnimT) = ctx.UseState(0.0);

        ctx.UseEffect(() => {
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Tick += (_, _) => {
                progress.Current += 0.03;
                if (progress.Current >= 1.0) { AdvanceYear(); }
                setAnimT(D3Ease.Cubic(progress.Current));
            };
            timer.Start();
            return () => timer.Stop();
        }, yearIdx);

        // Interpolate bar widths and Y positions, render declaratively
        return D3Canvas(W, H, [
            ..countries.SelectMany((name, ci) => new[] {
                D3Rect(left, interpY, xScale.Map(interpVal), barH),
                TextRight(0, interpY, name, left - 10),
                D3Charts.Text(left + barW + 6, interpY, $"${val:F1}T"),
            }),
            D3Charts.Text(..., "GDP by Country"),
            D3Charts.Text(..., Years[yearIdx]),  // watermark
        ])
            .AutomationName("GDP by Country")
            .FullDescription("Animated bar chart race showing GDP of 8 countries from 2018 to 2023 with bars that grow, shrink, and re-sort over time.");
        """;

    static readonly string[] Countries= ["USA", "China", "Japan", "Germany", "India", "UK", "France", "Brazil"];

    static readonly double[][] YearData =
    [
        [20.5, 14.7, 5.1, 3.8, 2.9, 2.8, 2.7, 1.9],  // 2018
        [21.4, 14.3, 5.1, 3.9, 2.9, 2.8, 2.7, 1.8],   // 2019
        [20.9, 14.7, 5.0, 3.8, 2.7, 2.7, 2.6, 1.4],   // 2020
        [23.0, 17.7, 5.0, 4.2, 3.2, 3.2, 2.9, 1.6],   // 2021
        [25.5, 18.0, 4.2, 4.1, 3.4, 3.1, 2.8, 1.9],   // 2022
        [27.4, 17.8, 4.2, 4.5, 3.7, 3.3, 3.0, 2.2],   // 2023
    ];

    static readonly string[] Years = ["2018", "2019", "2020", "2021", "2022", "2023"];

    const int N = 8; // country count
    const double W = 700, H = 420;
    const double Left = 80, Top = 40, Right = 40, Bottom = 20;

    static readonly double PlotW = W - Left - Right;
    static readonly double PlotH = H - Top - Bottom;
    static readonly double BarH = PlotH / N * 0.75;
    static readonly double BarGap = PlotH / N;
    static readonly double MaxVal = YearData.SelectMany(y => y).Max() + 2;
    static readonly LinearScale XScale = new([0, MaxVal], [0, PlotW]);

    public override Element Render()
    {
        return Func(ctx =>
        {
            var (yearIdx, setYearIdx) = ctx.UseState(0);
            var (animT, setAnimT) = ctx.UseState(0.0);

            var prevValues = ctx.UseRef(YearData[0].ToArray());
            var nextValues = ctx.UseRef(YearData[1].ToArray());
            var prevOrder = ctx.UseRef(RankOrder(0));
            var nextOrder = ctx.UseRef(RankOrder(1));
            var progress = ctx.UseRef(0.0);

            // Timer effect — auto-advances through years
            ctx.UseEffect(() =>
            {
                var timer = Microsoft.UI.Dispatching.DispatcherQueue
                    .GetForCurrentThread().CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(50);
                timer.Tick += (_, _) =>
                {
                    progress.Current += 0.03;
                    if (progress.Current >= 1.0)
                    {
                        // Advance to next year transition
                        int next = (yearIdx + 1) % Years.Length;
                        prevValues.Current = nextValues.Current;
                        prevOrder.Current = nextOrder.Current;
                        nextValues.Current = YearData[(next + 1) % Years.Length].ToArray();
                        nextOrder.Current = RankOrder((next + 1) % Years.Length);
                        progress.Current = 0.0;
                        setAnimT(0.0);
                        setYearIdx(next);
                        return;
                    }
                    setAnimT(D3Ease.Cubic(progress.Current));
                };
                timer.Start();
                return () => timer.Stop();
            }, yearIdx);

            // Compute rank → Y position maps for prev and next
            double[] prevY = new double[N], nextY = new double[N];
            for (int rank = 0; rank < N; rank++)
            {
                prevY[prevOrder.Current[rank]] = Top + rank * BarGap;
                nextY[nextOrder.Current[rank]] = Top + rank * BarGap;
            }

            // Render: iterate by country index (stable order for reconciler)
            var titleBrush = ChartForeground;
            var valueBrush = ChartMutedForeground;
            var yearBrush = ChartGrid;

            return D3Canvas(W, H,
            [
                .. Enumerable.Range(0, N).SelectMany(ci =>
                {
                    double val = D3Interpolate.Number(prevValues.Current[ci], nextValues.Current[ci])(animT);
                    double bw = XScale.Map(val);
                    double by = D3Interpolate.Number(prevY[ci], nextY[ci])(animT);

                    return new Element[]
                    {
                        D3Rect(Left, by, Math.Max(0, bw), BarH) with
                        {
                            Fill = Brush(Palette[ci % Palette.Count]),
                            RadiusX = 3, RadiusY = 3,
                        },
                        TextRight(0, by + BarH / 2 - 8, Countries[ci], Left - 10, fontSize: 11,
                            foreground: titleBrush),
                        D3Charts.Text(Left + bw + 6, by + BarH / 2 - 7, $"${val:F1}T", fontSize: 10,
                            foreground: valueBrush),
                    };
                }),
                D3Charts.Text(12, 8, "GDP by Country (Trillions USD)", fontSize: 14, foreground: titleBrush),
                D3Charts.Text(W - 160, H - 80, Years[yearIdx], fontSize: 48, foreground: yearBrush),
            ])
                .AutomationName("GDP by Country")
                .FullDescription("Animated bar chart race showing GDP of 8 countries from 2018 to 2023 with bars that grow, shrink, and re-sort over time.");
        });
    }

    /// <summary>Returns country indices sorted by descending value for the given year.</summary>
    static int[] RankOrder(int yearIdx) =>
        Enumerable.Range(0, N).OrderByDescending(i => YearData[yearIdx][i]).ToArray();
}
