using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.ChartDsl;

ReactorApp.Run<ChartGallery>("Reactor Chart Gallery", width: 1200, height: 800
#if DEBUG
    , devtools: true
#endif
);

// ── Data types ──────────────────────────────────────────────────────────────

record DataPoint(double X, double Y);
record OrgNode(string Name, OrgNode[]? Reports = null);

// ── Main gallery ────────────────────────────────────────────────────────────

class ChartGallery : Component
{
    static readonly Random Rng = new(42);

    public override Element Render()
    {
        var (tab, setTab) = UseState(0);
        var (tick, setTick) = UseState(0);

        // ForceGraph still uses XamlHostElement, so it keeps a handle for drag/animation
        var forceHandle = UseRef<ForceGraphHandle?>(null);

        // Timer: every 800ms bump the tick counter → state-driven re-render
        UseEffect(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) => setTick(tick + 1);
            timer.Start();
            return () => timer.Stop();
        }, tab);

        // Data computed from tick (pure functions)
        var sineData = MakeSineData(tick);
        var barData = MakeBarData(tick);
        var pieData = MakePieData(tick);
        var orgTree = MakeTree(tick);

        var graphNodes = UseMemo(() => new ForceNode[]
        {
            new() { Label = "App", Radius = 12 }, new() { Label = "Auth", Radius = 10 },
            new() { Label = "API", Radius = 10 }, new() { Label = "DB", Radius = 10 },
            new() { Label = "Cache", Radius = 8 }, new() { Label = "Queue", Radius = 8 },
            new() { Label = "Worker", Radius = 8 }, new() { Label = "Storage", Radius = 8 },
            new() { Label = "CDN", Radius = 8 }, new() { Label = "Monitor", Radius = 8 },
            new() { Label = "Logger", Radius = 7 }, new() { Label = "Metrics", Radius = 7 },
        });
        var graphLinks = UseMemo(() => new ForceLink[]
        {
            new(0, 1), new(0, 2), new(2, 3), new(2, 4), new(2, 5), new(5, 6),
            new(3, 7), new(0, 8), new(2, 9), new(9, 10), new(9, 11),
            new(1, 3), new(6, 3), new(6, 7), new(4, 3),
        });

        string[] tabs = ["Line", "Bar", "Area", "Pie", "Tree", "Force"];

        return Grid(
            columns: ["*"], rows: ["Auto", "*"],
            (TitleBar("Reactor Chart Gallery") with
            {
                Subtitle = $"D3-powered live charts · tick {tick}",
            }).Grid(row: 0),
            VStack(8,
                SelectorBar(tabs.Select(t => SelectorBarItem(t)).ToArray(),
                    selectedIndex: tab, onSelectionChanged: setTab).Margin(24, 16, 24, 0),
            ScrollView(
                Border(
                    tab switch
                    {
                        0 => VStack(SubHeading("Line Chart — Live Sine Wave"),
                            LineChart(sineData, d => d.X, d => d.Y)
                                .Title("Live Sine Wave")
                                .Width(750).Height(350).Stroke("#4285f4")),

                        1 => VStack(SubHeading("Bar Chart — Streaming Revenue"),
                            BarChart(barData, d => d.X, d => d.Y)
                                .Title("Streaming Revenue")
                                .Width(750).Height(350).Fill("#34a853")),

                        2 => VStack(SubHeading("Area Chart — Live Signal"),
                            AreaChart(sineData, d => d.X, d => d.Y)
                                .Title("Live Signal")
                                .Width(750).Height(350).Stroke("#ea4335").Fill("#ea4335").FillOpacity(0.2)),

                        3 => VStack(SubHeading("Pie Chart — Shifting Market Share"),
                            PieChart(pieData, d => d.Y, d => ((string[])["Chrome","Safari","Firefox","Edge","Other"])[(int)d.X])
                                .Title("Market Share")
                                .Width(400).Height(400).InnerRadius(60).PadAngle(0.03)),

                        4 => VStack(SubHeading("Tree Layout — Growing Org Chart"),
                            TreeChart(orgTree, n => n.Reports, n => n.Name)
                                .Title("Organization Chart")
                                .Width(800).Height(450).NodeColor("#9467bd")),

                        5 => VStack(SubHeading("Force Graph — Drag any node!"),
                            ForceGraph(graphNodes, graphLinks)
                                .Width(800).Height(500).Charge(-200).Distance(80)
                                .OnReady(h => { forceHandle.Current = h; SetupDrag(h); })),

                        _ => TextBlock("Select a tab"),
                    }
                ).Padding(16)
            )).Padding(0).Grid(row: 1)
        );
    }

    // ── Data generators — pure functions of tick ─────────────────────────

    static DataPoint[] MakeSineData(int tick)
    {
        double phase = tick * 0.3;
        return Enumerable.Range(0, 50)
            .Select(i => new DataPoint(i * 0.2, Math.Sin(i * 0.2 + phase) * 50 + 50))
            .ToArray();
    }

    static DataPoint[] MakeBarData(int tick)
    {
        return Enumerable.Range(0, 12)
            .Select(i => new DataPoint(i, 3000 + 2000 * Math.Sin(tick * 0.2 + i * 0.5) + Rng.Next(500)))
            .ToArray();
    }

    static DataPoint[] MakePieData(int tick)
    {
        double t = tick * 0.15;
        double[] bases = [60, 18, 8, 7, 7];
        return Enumerable.Range(0, 5)
            .Select(i => new DataPoint(i, Math.Max(2, bases[i] + 8 * Math.Sin(t + i * 1.3))))
            .ToArray();
    }

    static OrgNode MakeTree(int tick)
    {
        // Every few ticks add/remove a leaf to show the tree re-laying out
        int extra = (tick / 3) % 4;
        var engKids = new List<OrgNode> { new("FE"), new("BE"), new("Infra") };
        if (extra >= 1) engKids.Add(new("Mobile"));
        if (extra >= 2) engKids.Add(new("DevOps"));
        if (extra >= 3) engKids.Add(new("QA"));

        return new OrgNode("CEO", [
            new("CTO", [
                new("Eng", engKids.ToArray()),
                new("Research", [new("ML"), new("Sec")]),
            ]),
            new("CFO", [new("Finance"), new("Acctg")]),
            new("COO", [new("Ops"), new("HR"), new("Legal")]),
        ]);
    }

    // ── Force graph drag — sample-side interaction logic ────────────────

    static void SetupDrag(ForceGraphHandle handle)
    {
        var sim = handle.Simulation;
        int dragIndex = -1;
        DispatcherTimer? animTimer = null;

        void StartAnimating()
        {
            if (animTimer != null) return;
            animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            animTimer.Tick += (_, _) =>
            {
                sim.Tick();
                handle.SyncPositions();
                if (sim.Alpha < sim.AlphaMin && dragIndex == -1)
                {
                    animTimer.Stop();
                    animTimer = null;
                }
            };
            animTimer.Start();
        }

        for (int i = 0; i < handle.NodeEllipses.Length; i++)
        {
            int idx = i;
            var ellipse = handle.NodeEllipses[idx];

            ellipse.PointerPressed += (_, e) =>
            {
                dragIndex = idx;
                var n = sim.Nodes[idx]; n.Fx = n.X; n.Fy = n.Y;
                ellipse.CapturePointer(e.Pointer);
                sim.AlphaTarget = 0.3;
                sim.Alpha = Math.Max(sim.Alpha, 0.3);
                StartAnimating();
                e.Handled = true;
            };
            ellipse.PointerMoved += (_, e) =>
            {
                if (dragIndex != idx) return;
                var pos = e.GetCurrentPoint(handle.Canvas).Position;
                sim.Nodes[idx].Fx = pos.X;
                sim.Nodes[idx].Fy = pos.Y;
                e.Handled = true;
            };
            ellipse.PointerReleased += (_, e) =>
            {
                if (dragIndex != idx) return;
                dragIndex = -1;
                sim.Nodes[idx].Fx = null;
                sim.Nodes[idx].Fy = null;
                sim.AlphaTarget = 0;
                ellipse.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            };
        }
    }
}
