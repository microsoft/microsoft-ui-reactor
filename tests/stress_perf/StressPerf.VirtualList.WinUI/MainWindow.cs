using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StressPerf.Shared;

namespace StressPerf.VirtualList.WinUI;

/// <summary>
/// Hand-authored WinUI 3 counterpart to <c>StressPerf.VirtualList.Reactor</c>.
/// Renders the same <see cref="ListItemSource"/> feed in an
/// <see cref="ItemsRepeater"/> bound directly to an
/// <c>ObservableCollection&lt;ListItem&gt;</c>; edit ops mutate the OC
/// in place (best-case WinUI — no diff layer between the model and the
/// realized containers).
///
/// Spec 042 perf gate: a paired run (same <c>--count</c>,
/// <c>--duration</c>, <c>--with-edits</c>, <c>--edits-per-second</c>,
/// <c>--seed</c>) against the Reactor variant tells us whether the
/// <see cref="Microsoft.UI.Reactor.Core.Internal.KeyedListDiff"/> +
/// <see cref="Microsoft.UI.Reactor.Core.Internal.ReactorListState"/>
/// pipeline costs anything visible against native WinUI virtualization.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string AppName = "StressPerf.VirtualList.WinUI";

    private static readonly SolidColorBrush DimText = new(global::Windows.UI.Color.FromArgb(255, 110, 110, 110));
    private static readonly SolidColorBrush AltRow = new(global::Windows.UI.Color.FromArgb(255, 245, 245, 245));
    private static readonly SolidColorBrush WhiteRow = new(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush PillBg = new(global::Windows.UI.Color.FromArgb(255, 240, 240, 240));
    private static readonly SolidColorBrush WhiteText = new(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));

    private readonly VirtualListCli _cli;
    private readonly PerfTracker _perf = new();

    // Bound directly to the ItemsRepeater — edits hit this OC in place.
    private readonly ObservableCollection<ListItemSource.ListItem> _items = new();

    private ScrollViewer? _scrollView;
    private ItemsRepeater? _repeater;
    private TextBlock? _fpsText, _p50Text, _p95Text, _p99Text, _memText, _statusText;

    // Frame-time sampling window — only active while a benchmark runs.
    private List<double>? _frameSamples;
    private long _lastFrameTicks;
    private bool _benchActive;
    private long _benchStartTicks;
    private double _benchDurationMs;
    private double _benchMaxOffset;
    private int _benchCount;

    // Edit timer state (spec 042 Phase 6.3 parity with the Reactor variant).
    private DispatcherTimer? _editTimer;
    private int _editOps;
    private Random? _editRng;

    public MainWindow(VirtualListCli cli)
    {
        _cli = cli;
        Title = AppName;
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);

        BuildUi(cli.Count);

        CompositionTarget.Rendering += OnRendering;

        if (cli.Headless)
        {
            // One tick of slack so layout has reported a viewport height —
            // mirrors VirtualListApp.UseEffect in the Reactor variant.
            var startTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            startTimer.Tick += (_, _) => { startTimer.Stop(); StartBenchmark(); };
            startTimer.Start();

            var quit = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(cli.DurationSeconds + 2),
            };
            quit.Tick += (_, _) => { quit.Stop(); Application.Current.Exit(); };
            quit.Start();
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  UI construction
    // ────────────────────────────────────────────────────────────────────

    private void BuildUi(int initialCount)
    {
        SeedItems(initialCount);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Toolbar row — matches the Reactor variant's button layout so
        // pixel-level captures of the two windows are visually comparable.
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Thickness(8),
        };

        Button SizeBtn(string label, int target)
        {
            var b = new Button { Content = label, IsEnabled = initialCount != target };
            AutomationProperties.SetName(b, $"Resize to {target}");
            b.Click += (_, _) => SeedItems(target);
            return b;
        }
        toolbar.Children.Add(SizeBtn("1k", 1000));
        toolbar.Children.Add(SizeBtn("5k", 5000));
        toolbar.Children.Add(SizeBtn("10k", 10000));

        var editsBtn = new Button { Content = _cli.WithEdits ? "Edits: on" : "Edits: off" };
        editsBtn.Click += (_, _) =>
        {
            _cli.WithEdits = !_cli.WithEdits;
            editsBtn.Content = _cli.WithEdits ? "Edits: on" : "Edits: off";
        };
        toolbar.Children.Add(editsBtn);

        var runBtn = new Button { Content = "Run benchmark" };
        AutomationProperties.SetName(runBtn, "Run benchmark");
        runBtn.Click += (_, _) => StartBenchmark();
        toolbar.Children.Add(runBtn);

        _fpsText = MakeStatusTextBlock("FPS: --", 90);
        _p50Text = MakeStatusTextBlock("P50: -- ms", 110);
        _p95Text = MakeStatusTextBlock("P95: -- ms", 110);
        _p99Text = MakeStatusTextBlock("P99: -- ms", 110);
        _memText = MakeStatusTextBlock("Mem: -- MB", 110);
        _statusText = MakeStatusTextBlock("idle", 200);
        _statusText.Foreground = DimText;

        toolbar.Children.Add(_fpsText);
        toolbar.Children.Add(_p50Text);
        toolbar.Children.Add(_p95Text);
        toolbar.Children.Add(_p99Text);
        toolbar.Children.Add(_memText);
        toolbar.Children.Add(_statusText);

        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        // Virtualizing list — ItemsRepeater + StackLayout + a hand-built
        // IElementFactory. Mirrors what Reactor's LazyVStack assembles
        // internally so the comparison is apples-to-apples (no XAML data
        // template cost on either side).
        _repeater = new ItemsRepeater
        {
            ItemsSource = _items,
            Layout = new StackLayout { Orientation = Orientation.Vertical, Spacing = 0 },
            ItemTemplate = new RowFactory(),
        };

        _scrollView = new ScrollViewer
        {
            Content = _repeater,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(_scrollView, 1);
        root.Children.Add(_scrollView);

        Content = root;
    }

    private static TextBlock MakeStatusTextBlock(string text, double width) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Width = width,
    };

    private void SeedItems(int count)
    {
        // Replace the OC contents wholesale. WinUI does not have a
        // first-class bulk-replace primitive that preserves the
        // ItemsSource reference, so we Clear() + Add(N). The realized
        // containers tear down to zero and re-realize — this is the
        // expected WinUI cost for "re-seed" and is what Reactor's
        // bulk-replace bailout devolves to in the >25% churn case.
        _items.Clear();
        for (int i = 0; i < count; i++)
            _items.Add(ListItemSource.ItemAt(i));
    }

    // ────────────────────────────────────────────────────────────────────
    //  Benchmark control
    // ────────────────────────────────────────────────────────────────────

    private void StartBenchmark()
    {
        var sv = _scrollView;
        if (sv is null) return;

        sv.ChangeView(null, 0, null, disableAnimation: true);
        _benchMaxOffset = Math.Max(
            0,
            ListItemSource.RowHeight * _items.Count - (sv.ViewportHeight > 0 ? sv.ViewportHeight : 600));
        _benchDurationMs = _cli.DurationSeconds * 1000.0;
        _frameSamples = new List<double>(capacity: 600);
        _lastFrameTicks = 0;
        _benchStartTicks = Stopwatch.GetTimestamp();
        _benchActive = true;
        _benchCount = _items.Count;
        SetStatus(_cli.WithEdits ? "running… (edits on)" : "running…");

        StartEditTimer();
    }

    private void OnRendering(object? sender, object e)
    {
        _perf.FrameRendered();
        if (!_benchActive) return;

        long now = Stopwatch.GetTimestamp();
        if (_lastFrameTicks != 0)
        {
            double dtMs = (now - _lastFrameTicks) * 1000.0 / Stopwatch.Frequency;
            _frameSamples!.Add(dtMs);
        }
        _lastFrameTicks = now;

        var sv = _scrollView;
        if (sv is not null)
        {
            double elapsedMs = (now - _benchStartTicks) * 1000.0 / Stopwatch.Frequency;
            double t = Math.Min(1.0, elapsedMs / _benchDurationMs);
            double offset = _benchMaxOffset * t;
            sv.ChangeView(null, offset, null, disableAnimation: true);
            if (t >= 1.0) FinishBenchmark();
        }
    }

    private void FinishBenchmark()
    {
        _benchActive = false;
        StopEditTimer();

        var samples = _frameSamples;
        if (samples is null || samples.Count == 0)
        {
            SetStatus("no frames captured");
            return;
        }
        samples.Sort();
        double p50 = samples[(int)(samples.Count * 0.50)];
        double p95 = samples[(int)(samples.Count * 0.95)];
        double p99 = samples[Math.Min(samples.Count - 1, (int)(samples.Count * 0.99))];
        _p50Text!.Text = $"P50: {p50:F1} ms";
        _p95Text!.Text = $"P95: {p95:F1} ms";
        _p99Text!.Text = $"P99: {p99:F1} ms";
        _fpsText!.Text = $"FPS: {_perf.CurrentFps:F0}";
        _memText!.Text = $"Mem: {_perf.CurrentMemoryMB} MB";
        SetStatus(_cli.WithEdits
            ? $"done ({samples.Count} frames, {_editOps} edits)"
            : $"done ({samples.Count} frames)");

        WriteReport(samples, _benchCount, _cli.WithEdits ? _editOps : 0);
    }

    private void SetStatus(string s)
    {
        if (_statusText is not null) _statusText.Text = s;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Edit timer — direct OC mutation, deterministic seed, 50/50 mix
    // ────────────────────────────────────────────────────────────────────

    private void StartEditTimer()
    {
        if (!_cli.WithEdits) return;
        _editOps = 0;
        _editRng = new Random(1234567);
        var period = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _cli.EditsPerSecond));
        var t = new DispatcherTimer { Interval = period };
        t.Tick += (_, _) =>
        {
            if (!_benchActive) { StopEditTimer(); return; }
            var rng = _editRng!;
            if (rng.NextDouble() < 0.5 || _items.Count < 100)
            {
                int pos = rng.Next(_items.Count + 1);
                _items.Insert(pos, ListItemSource.GenerateOne(int.MaxValue - _editOps));
            }
            else
            {
                int pos = rng.Next(_items.Count);
                _items.RemoveAt(pos);
            }
            _editOps++;
        };
        _editTimer = t;
        t.Start();
    }

    private void StopEditTimer()
    {
        var t = _editTimer;
        if (t is null) return;
        t.Stop();
        _editTimer = null;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Report — identical filename + shape as the Reactor variant
    // ────────────────────────────────────────────────────────────────────

    private static void WriteReport(List<double> sortedSamples, int count, int editOps)
    {
        double p50 = sortedSamples[(int)(sortedSamples.Count * 0.50)];
        double p95 = sortedSamples[(int)(sortedSamples.Count * 0.95)];
        double p99 = sortedSamples[Math.Min(sortedSamples.Count - 1, (int)(sortedSamples.Count * 0.99))];
        double avg = 0;
        for (int i = 0; i < sortedSamples.Count; i++) avg += sortedSamples[i];
        avg /= sortedSamples.Count;
        double totalMs = avg * sortedSamples.Count;

        // Match the Reactor variant's memory snapshot: force a GC so the
        // reported managed-heap number reflects retained state, then
        // grab WS / Private / Peak from the process handle.
        global::System.GC.Collect();
        global::System.GC.WaitForPendingFinalizers();
        global::System.GC.Collect();
        var p = global::System.Diagnostics.Process.GetCurrentProcess();
        long workingSetMB = p.WorkingSet64 / (1024 * 1024);
        long peakWorkingSetMB = p.PeakWorkingSet64 / (1024 * 1024);
        long privateMB = p.PrivateMemorySize64 / (1024 * 1024);
        long managedHeapMB = global::System.GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);

        var sb = new StringBuilder();
        sb.AppendLine($"=== {AppName} ===");
        sb.AppendLine($"Count:       {count}");
        sb.AppendLine($"Edits:       {editOps}");
        sb.AppendLine($"Frames:      {sortedSamples.Count}");
        sb.AppendLine($"WallClock:   {totalMs:F1} ms  (sum of frame deltas in the bench window)");
        sb.AppendLine($"Avg dt:      {avg:F2} ms  (~{1000.0 / avg:F1} fps)");
        sb.AppendLine($"P50 dt:      {p50:F2} ms");
        sb.AppendLine($"P95 dt:      {p95:F2} ms");
        sb.AppendLine($"P99 dt:      {p99:F2} ms");
        sb.AppendLine($"Max dt:      {sortedSamples[^1]:F2} ms");
        sb.AppendLine($"WS:          {workingSetMB} MB  (working set at bench finish)");
        sb.AppendLine($"PeakWS:      {peakWorkingSetMB} MB  (peak working set across process lifetime)");
        sb.AppendLine($"Private:     {privateMB} MB  (committed private bytes)");
        sb.AppendLine($"ManagedHeap: {managedHeapMB} MB  (after GC.Collect)");

        var dir = AppContext.BaseDirectory;
        File.WriteAllText(System.IO.Path.Combine(dir, $"{AppName}.report.txt"), sb.ToString());

        var csv = new StringBuilder("FrameIndex,DeltaMs\n");
        for (int i = 0; i < sortedSamples.Count; i++)
            csv.Append(i).Append(',').AppendLine(sortedSamples[i].ToString("F2"));
        File.WriteAllText(System.IO.Path.Combine(dir, $"{AppName}.frames.csv"), csv.ToString());
    }

    // ────────────────────────────────────────────────────────────────────
    //  Row factory — imperative tree build, no XAML DataTemplate.
    //  Mirrors the visual tree of VirtualListApp.Row in the Reactor variant
    //  so a side-by-side capture compares like-for-like cell rasterization
    //  cost. Recycled containers are repopulated in PrepareElementForItem.
    // ────────────────────────────────────────────────────────────────────

    // Vanilla recycling pool. ItemsRepeater calls RecycleElement when an
    // item scrolls out of view; we push the realized RowControl onto a
    // stack so the next GetElement reuses it instead of allocating. This
    // mirrors what Reactor's LazyVStack factory does internally (keyed
    // dict by ReactorRow.Key); the WinUI cost of "find element by index"
    // doesn't apply because RecyclingElementFactory's standard pattern
    // is index-agnostic.
    private sealed partial class RowFactory : IElementFactory
    {
        private readonly Stack<RowControl> _pool = new();

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var item = (ListItemSource.ListItem)args.Data;
            if (_pool.TryPop(out var recycled))
            {
                recycled.SetData(item);
                return recycled;
            }
            var row = new RowControl();
            row.SetData(item);
            return row;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is RowControl rc)
                _pool.Push(rc);
        }
    }

    // Border is sealed in WinUI 3, so the row is a Grid that owns the
    // background brush directly. Visual tree depth still matches the
    // Reactor variant (one Border wrap → one Grid wrap) so per-row layout
    // cost stays comparable.
    private sealed partial class RowControl : Grid
    {
        private readonly Border _avatar;
        private readonly TextBlock _initial;
        private readonly TextBlock _title;
        private readonly TextBlock _message;
        private readonly TextBlock _stamp;
        private readonly TextBlock _likes;

        public RowControl()
        {
            _initial = new TextBlock
            {
                FontSize = 18,
                Foreground = WhiteText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _avatar = new Border
            {
                Child = _initial,
                CornerRadius = new CornerRadius(6),
                Width = ListItemSource.AvatarSize,
                Height = ListItemSource.AvatarSize,
            };

            _title = new TextBlock { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            _message = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
            _stamp = new TextBlock { FontSize = 12, Foreground = DimText };

            var middle = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            middle.Children.Add(_title);
            middle.Children.Add(_message);
            middle.Children.Add(_stamp);

            _likes = new TextBlock
            {
                FontSize = 12,
                Padding = new Thickness(8, 2, 8, 2),
            };
            var likesPill = new Border
            {
                Child = _likes,
                Background = PillBg,
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Center,
            };

            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnSpacing = 12;
            Padding = new Thickness(12, 8, 12, 8);

            Grid.SetColumn(_avatar, 0);
            Grid.SetColumn(middle, 1);
            Grid.SetColumn(likesPill, 2);
            Children.Add(_avatar);
            Children.Add(middle);
            Children.Add(likesPill);

            Height = ListItemSource.RowHeight;
        }

        public void SetData(ListItemSource.ListItem item)
        {
            _initial.Text = item.Initial.ToString();
            _avatar.Background = new SolidColorBrush(HslToRgb(item.AvatarHue, 0.55, 0.45));
            _title.Text = $"{item.Name} • {item.Category}";
            _message.Text = item.Message;
            _stamp.Text = $"{item.Timestamp} • #{item.Tag}";
            _likes.Text = $"♥ {item.Likes}";
            // Alternate row tint by Id parity — visually matches the
            // Reactor variant, which keys on the realized index.
            Background = (item.Id & 1) == 0 ? WhiteRow : AltRow;

            // Accessibility — every row gets a screen-reader-friendly
            // composite name. Mirrors Reactor's auto-named TextBlocks
            // (FrameworkElement.Name flows into UIA by default).
            AutomationProperties.SetName(this,
                $"{item.Name}, {item.Category}, {item.Message}, {item.Likes} likes");
        }
    }

    private static global::Windows.UI.Color HslToRgb(int hueDeg, double s, double l)
    {
        double h = (hueDeg % 360) / 360.0;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        double r = HueToRgb(p, q, h + 1.0 / 3.0);
        double g = HueToRgb(p, q, h);
        double b = HueToRgb(p, q, h - 1.0 / 3.0);
        return global::Windows.UI.Color.FromArgb(
            255,
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
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
}
