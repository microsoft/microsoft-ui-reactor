using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PerfBench.Shared;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.Priorities.Reactor;

public class PrioritiesApp : Component
{
    public static BenchCliOptions Opts { get; set; } = new();

    private const int ItemCount = 5000;
    private static readonly string[] Words =
    {
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel",
        "india", "juliet", "kilo", "lima", "mike", "november", "oscar", "papa",
        "quebec", "romeo", "sierra", "tango", "uniform", "victor", "whiskey", "xray",
        "yankee", "zulu", "able", "baker", "cast", "dodge", "easy", "fox"
    };

    private readonly BenchTracker _tracker = new();
    private readonly Stopwatch _inputSw = new();

    // Headless typing simulation
    private const string TypeSequence = "abcdef";

    public override Element Render()
    {
        var (searchText, setSearchText) = UseState("");
        var (hudText, setHudText) = UseState("");

        // Generate all items once
        var allItems = UseMemo(() =>
        {
            var rng = new Random(42);
            var items = new string[ItemCount];
            for (int i = 0; i < ItemCount; i++)
                items[i] = $"Item {i}: {Words[rng.Next(Words.Length)]}";
            return items;
        });

        // Compute filtered items from search text
        var filteredItems = UseMemo(() =>
        {
            if (string.IsNullOrEmpty(searchText))
                return allItems;
            return allItems.Where(item =>
                item.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToArray();
        }, searchText);

        UseEffect(() =>
        {
            _tracker.ResetGcBaseline();
            CompositionTarget.Rendering += (_, _) => _tracker.FrameRendered();

            if (Opts.Headless)
            {
                int typeIndex = 0;
                var typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                typeTimer.Tick += (_, _) =>
                {
                    if (typeIndex < TypeSequence.Length)
                    {
                        typeIndex++;
                        _inputSw.Restart();
                        setSearchText(TypeSequence[..typeIndex]);
                    }
                };
                typeTimer.Start();

                var shutdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Opts.DurationSeconds) };
                shutdown.Tick += (_, _) =>
                {
                    shutdown.Stop();
                    typeTimer.Stop();
                    _tracker.WriteReportFile("EXP8_Priorities_Reactor");
                    Microsoft.UI.Xaml.Application.Current.Exit();
                };
                shutdown.Start();
            }
        });

        // Build filtered list elements
        var listElements = new Element[Math.Min(filteredItems.Length, 5000)];
        for (int i = 0; i < listElements.Length; i++)
            listElements[i] = TextBlock(filteredItems[i]).FontSize(10);

        var children = new List<Element?>
        {
            TextBox(searchText, text =>
            {
                _tracker.BeginUpdate();
                setSearchText(text);
                _tracker.EndUpdate();

                if (_inputSw.IsRunning)
                {
                    _inputSw.Stop();
                    _tracker.RecordInputLatency(_inputSw.Elapsed.TotalMilliseconds);
                }

                if (!Opts.Headless)
                    setHudText($"FPS: {_tracker.CurrentFps:F0}  Update: {_tracker.LastUpdateMs:F2}ms  Mem: {_tracker.CurrentMemoryMB}MB");
            }, placeholderText: "Search..."),
            ScrollView(VStack(listElements))
        };

        if (!Opts.Headless)
            children.Add(TextBlock(hudText).Foreground("Yellow").FontSize(14));

        return VStack(children.ToArray());
    }
}
