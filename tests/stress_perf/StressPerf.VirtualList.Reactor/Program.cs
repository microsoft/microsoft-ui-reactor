using System.Diagnostics;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StressPerf.Shared;
using static Microsoft.UI.Reactor.Factories;

// CLI:  --headless [--count N] [--duration S]
var cli = VirtualListCli.Parse(args);
if (cli.Headless) ConsoleHelper.EnsureConsole();

VirtualListApp.Cli = cli;
ReactorApp.Run<VirtualListApp>("StressPerf.VirtualList.Reactor", fullScreen: true);

// ───────────────────────────────────────────────────────────────────────────

class VirtualListCli
{
    public bool Headless { get; set; }
    public int Count { get; set; } = 5000;
    public int DurationSeconds { get; set; } = 5;

    public static VirtualListCli Parse(string[] args)
    {
        var o = new VirtualListCli();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless": o.Headless = true; break;
                case "--count" when i + 1 < args.Length: o.Count = int.Parse(args[++i]); break;
                case "--duration" when i + 1 < args.Length: o.DurationSeconds = int.Parse(args[++i]); break;
            }
        }
        return o;
    }
}

class VirtualListApp : Component
{
    private const string AppName = "StressPerf.VirtualList.Reactor";
    public static VirtualListCli Cli { get; set; } = new();

    // Lazy brushes — SolidColorBrush ctor requires the UI thread, so a
    // static field initializer would throw at type-init time.
    private static SolidColorBrush? _dimText, _altRow, _whiteRow, _pillBg, _whiteText;
    private static SolidColorBrush DimText => _dimText ??=
        new(global::Windows.UI.Color.FromArgb(255, 110, 110, 110));
    private static SolidColorBrush AltRow => _altRow ??=
        new(global::Windows.UI.Color.FromArgb(255, 245, 245, 245));
    private static SolidColorBrush WhiteRow => _whiteRow ??=
        new(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));
    private static SolidColorBrush PillBg => _pillBg ??=
        new(global::Windows.UI.Color.FromArgb(255, 240, 240, 240));
    private static SolidColorBrush WhiteText => _whiteText ??=
        new(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));

    public override Element Render()
    {
        var (count, setCount) = UseState(Cli.Count);

        // Generate items deterministically. Cached in a ref so a re-render
        // for stat updates doesn't realloc the array.
        var itemsRef = UseRef<(int Count, ListItemSource.ListItem[] Items)>((0, []));
        if (itemsRef.Current.Count != count)
            itemsRef.Current = (count, ListItemSource.Generate(count));
        var items = itemsRef.Current.Items;

        var (fpsLabel, setFpsLabel) = UseState("FPS: --");
        var (p50Label, setP50Label) = UseState("P50: -- ms");
        var (p95Label, setP95Label) = UseState("P95: -- ms");
        var (p99Label, setP99Label) = UseState("P99: -- ms");
        var (memLabel, setMemLabel) = UseState("Mem: -- MB");
        var (status, setStatus) = UseState("idle");

        var perfRef = UseRef<PerfTracker?>(null);
        perfRef.Current ??= new PerfTracker();

        // Captured at mount via Set() — see LazyVStack below.
        var svRef = UseRef<ScrollViewer?>(null);

        // Frame-time samples for the active benchmark window.
        var frameSamplesRef = UseRef<List<double>?>(null);
        var lastFrameTicksRef = UseRef<long>(0);
        var benchActiveRef = UseRef(false);
        var benchStartTicksRef = UseRef<long>(0);
        var benchDurationMsRef = UseRef<double>(Cli.DurationSeconds * 1000.0);
        var benchMaxOffsetRef = UseRef<double>(0);

        // Hook CompositionTarget.Rendering once. Counts FPS into PerfTracker
        // continuously, drives the scroll tween while a benchmark is active.
        var renderHooked = UseRef(false);
        if (!renderHooked.Current)
        {
            renderHooked.Current = true;
            CompositionTarget.Rendering += (_, _) =>
            {
                var perf = perfRef.Current!;
                perf.FrameRendered();

                if (benchActiveRef.Current)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (lastFrameTicksRef.Current != 0)
                    {
                        double dtMs = (now - lastFrameTicksRef.Current)
                            * 1000.0 / Stopwatch.Frequency;
                        frameSamplesRef.Current!.Add(dtMs);
                    }
                    lastFrameTicksRef.Current = now;

                    // Drive the scroll position by a linear tween so the run
                    // is deterministic across machines.
                    var sv = svRef.Current;
                    if (sv is not null)
                    {
                        double elapsedMs = (now - benchStartTicksRef.Current)
                            * 1000.0 / Stopwatch.Frequency;
                        double t = Math.Min(1.0, elapsedMs / benchDurationMsRef.Current);
                        double offset = benchMaxOffsetRef.Current * t;
                        sv.ChangeView(null, offset, null, disableAnimation: true);
                        if (t >= 1.0) FinishBenchmark();
                    }
                }
            };
        }

        void FinishBenchmark()
        {
            benchActiveRef.Current = false;
            var samples = frameSamplesRef.Current!;
            if (samples.Count == 0)
            {
                setStatus("no frames captured");
                return;
            }
            samples.Sort();
            double p50 = samples[(int)(samples.Count * 0.50)];
            double p95 = samples[(int)(samples.Count * 0.95)];
            double p99 = samples[Math.Min(samples.Count - 1, (int)(samples.Count * 0.99))];
            setP50Label($"P50: {p50:F1} ms");
            setP95Label($"P95: {p95:F1} ms");
            setP99Label($"P99: {p99:F1} ms");
            setFpsLabel($"FPS: {perfRef.Current!.CurrentFps:F0}");
            setMemLabel($"Mem: {perfRef.Current!.CurrentMemoryMB} MB");
            setStatus($"done ({samples.Count} frames)");

            WriteReport(samples, count);
        }

        void StartBenchmark()
        {
            var sv = svRef.Current;
            if (sv is null) return;
            // Snap to top, then begin the tween next frame.
            sv.ChangeView(null, 0, null, disableAnimation: true);
            // Estimate the scroll range from the row metric. The internal
            // ScrollViewer's ExtentHeight is what ChangeView clamps against.
            // Compute lazily on the first benchmark frame instead — let WinUI
            // settle layout first.
            benchMaxOffsetRef.Current = Math.Max(
                0, ListItemSource.RowHeight * count - (sv.ViewportHeight > 0 ? sv.ViewportHeight : 600));
            frameSamplesRef.Current = new List<double>(capacity: 600);
            lastFrameTicksRef.Current = 0;
            benchStartTicksRef.Current = Stopwatch.GetTimestamp();
            benchActiveRef.Current = true;
            setStatus("running…");
        }

        // Headless: kick off the benchmark right after first paint, then exit.
        UseEffect(() =>
        {
            if (!Cli.Headless) return;
            // Defer one tick so layout has reported a viewport height.
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                StartBenchmark();
            };
            t.Start();

            var quit = new DispatcherTimer
            {
                // Duration + 2s slack to make sure the report is written.
                Interval = TimeSpan.FromSeconds(Cli.DurationSeconds + 2)
            };
            quit.Tick += (_, _) => { quit.Stop(); Application.Current.Exit(); };
            quit.Start();
        }, Array.Empty<object>());

        return VStack(
            HStack(8,
                Button("1k", () => setCount(1000)).Disabled(count == 1000),
                Button("5k", () => setCount(5000)).Disabled(count == 5000),
                Button("10k", () => setCount(10000)).Disabled(count == 10000),
                Button("Run benchmark", StartBenchmark),
                TextBlock(fpsLabel).VAlign(VerticalAlignment.Center).Width(90),
                TextBlock(p50Label).VAlign(VerticalAlignment.Center).Width(110),
                TextBlock(p95Label).VAlign(VerticalAlignment.Center).Width(110),
                TextBlock(p99Label).VAlign(VerticalAlignment.Center).Width(110),
                TextBlock(memLabel).VAlign(VerticalAlignment.Center).Width(110),
                TextBlock(status).VAlign(VerticalAlignment.Center).Foreground(DimText)
            ).Padding(8),
            LazyVStack<ListItemSource.ListItem>(
                items,
                item => item.Id.ToString(),
                (item, idx) => Row(item, idx)
            ).Set(sv => svRef.Current = sv)
        );
    }

    private static Element Row(ListItemSource.ListItem item, int idx)
    {
        var bg = (idx & 1) == 0 ? WhiteRow : AltRow;
        return Border(
            HStack(12,
                // Avatar
                Border(
                    TextBlock(item.Initial.ToString())
                        .FontSize(18)
                        .Foreground(WhiteText)
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center)
                )
                .Background(new SolidColorBrush(HslToRgb(item.AvatarHue, 0.55, 0.45)))
                .CornerRadius(6)
                .Width(ListItemSource.AvatarSize)
                .Height(ListItemSource.AvatarSize),
                // Center column
                VStack(2,
                    TextBlock($"{item.Name} • {item.Category}").FontSize(14).SemiBold(),
                    TextBlock(item.Message).FontSize(14)
                        .Set(tb => tb.TextTrimming = TextTrimming.CharacterEllipsis),
                    TextBlock($"{item.Timestamp} • #{item.Tag}").FontSize(12).Foreground(DimText)
                ).HAlign(HorizontalAlignment.Stretch),
                // Likes pill
                Border(TextBlock($"♥ {item.Likes}").FontSize(12).Padding(8, 2, 8, 2))
                    .Background(PillBg)
                    .CornerRadius(10)
                    .VAlign(VerticalAlignment.Center)
            ).Padding(12, 8, 12, 8)
        )
        .Background(bg)
        .Height(ListItemSource.RowHeight);
    }

    private static global::Windows.UI.Color HslToRgb(int hueDeg, double s, double l)
    {
        double h = (hueDeg % 360) / 360.0;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        double r = HueToRgb(p, q, h + 1.0 / 3.0);
        double g = HueToRgb(p, q, h);
        double b = HueToRgb(p, q, h - 1.0 / 3.0);
        return global::Windows.UI.Color.FromArgb(255,
            (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    private static void WriteReport(List<double> sortedSamples, int count)
    {
        // sortedSamples is already sorted ascending.
        double p50 = sortedSamples[(int)(sortedSamples.Count * 0.50)];
        double p95 = sortedSamples[(int)(sortedSamples.Count * 0.95)];
        double p99 = sortedSamples[Math.Min(sortedSamples.Count - 1, (int)(sortedSamples.Count * 0.99))];
        double avg = 0;
        for (int i = 0; i < sortedSamples.Count; i++) avg += sortedSamples[i];
        avg /= sortedSamples.Count;

        var sb = new StringBuilder();
        sb.AppendLine($"=== {AppName} ===");
        sb.AppendLine($"Count:       {count}");
        sb.AppendLine($"Frames:      {sortedSamples.Count}");
        sb.AppendLine($"Avg dt:      {avg:F2} ms  (~{1000.0 / avg:F1} fps)");
        sb.AppendLine($"P50 dt:      {p50:F2} ms");
        sb.AppendLine($"P95 dt:      {p95:F2} ms");
        sb.AppendLine($"P99 dt:      {p99:F2} ms");
        sb.AppendLine($"Max dt:      {sortedSamples[^1]:F2} ms");

        var dir = AppContext.BaseDirectory;
        File.WriteAllText(System.IO.Path.Combine(dir, $"{AppName}.report.txt"), sb.ToString());

        var csv = new StringBuilder("FrameIndex,DeltaMs\n");
        for (int i = 0; i < sortedSamples.Count; i++)
            csv.Append(i).Append(',').AppendLine(sortedSamples[i].ToString("F2"));
        File.WriteAllText(System.IO.Path.Combine(dir, $"{AppName}.frames.csv"), csv.ToString());
    }
}
