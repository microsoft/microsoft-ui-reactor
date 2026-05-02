using System.Diagnostics;
using System.Text;

namespace StressPerf.Shared;

public sealed class PerfTracker
{
    private readonly Stopwatch _wallClock = Stopwatch.StartNew();
    private readonly Stopwatch _updateSw = new();
    private int _frameCount;
    private double _lastSampleTime;
    private double _currentFps;
    private double _lastUpdateMs;

    private readonly List<double> _fpsSamples = new();
    private readonly List<long> _memorySamples = new();
    private readonly List<double> _updateTimeSamples = new();
    private readonly List<double> _reconcileTimeSamples = new();
    private readonly List<double> _treeBuildSamples = new();
    private readonly List<double> _diffPatchSamples = new();
    private readonly List<double> _effectsSamples = new();
    // Cross-variant render counter. See METHODOLOGY.md for what this means
    // per framework. Imperative variants increment after each tick's
    // mutate-and-set-properties pass; declarative variants (Reactor)
    // increment when the reconcile completes via RecordPhases.
    private int _renderCount;

    public double CurrentFps => _currentFps;
    public double LastUpdateMs => _lastUpdateMs;
    public long CurrentMemoryMB => Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

    /// <summary>
    /// Call from CompositionTarget.Rendering to count composed frames.
    /// </summary>
    public void FrameRendered()
    {
        _frameCount++;
        double now = _wallClock.Elapsed.TotalSeconds;
        double elapsed = now - _lastSampleTime;
        if (elapsed >= 1.0)
        {
            _currentFps = _frameCount / elapsed;
            _fpsSamples.Add(_currentFps);
            _memorySamples.Add(Process.GetCurrentProcess().WorkingSet64);
            _frameCount = 0;
            _lastSampleTime = now;
        }
    }

    /// <summary>
    /// Call before updating data + UI.
    /// </summary>
    public void BeginUpdate() => _updateSw.Restart();

    /// <summary>
    /// Call after updating data + UI.
    /// </summary>
    public void EndUpdate()
    {
        _updateSw.Stop();
        _lastUpdateMs = _updateSw.Elapsed.TotalMilliseconds;
        _updateTimeSamples.Add(_lastUpdateMs);
    }

    /// <summary>
    /// Increment the cross-variant render counter. Call once per "render
    /// completed" event for the framework — for imperative variants
    /// (Direct/Bound/Wpf/DirectX) that's after the tick handler finishes
    /// patching properties; for Reactor it happens automatically when
    /// <see cref="RecordPhases"/> fires from the reconcile-complete callback.
    /// See METHODOLOGY.md.
    /// </summary>
    public void RecordRender() => _renderCount++;

    public int TotalRenders => _renderCount;

    /// <summary>
    /// Record per-phase breakdown for a render pass. Reactor only — also
    /// counts as a render via <see cref="RecordRender"/>.
    /// </summary>
    public void RecordPhases(double treeBuildMs, double diffPatchMs, double effectsMs)
    {
        _treeBuildSamples.Add(treeBuildMs);
        _diffPatchSamples.Add(diffPatchMs);
        _effectsSamples.Add(effectsMs);
        _reconcileTimeSamples.Add(treeBuildMs + diffPatchMs + effectsMs);
        RecordRender();
    }

    public double ElapsedSeconds => _wallClock.Elapsed.TotalSeconds;

    public string GetReport(string appName, double percent)
    {
        if (_fpsSamples.Count == 0) return "No data collected.";

        var sb = new StringBuilder();
        sb.AppendLine($"=== {appName} ===");
        sb.AppendLine($"Duration:    {_wallClock.Elapsed.TotalSeconds:F1}s");
        sb.AppendLine($"Percent:     {percent:F0}%");
        sb.AppendLine($"Avg FPS:     {_fpsSamples.Average():F1}");
        sb.AppendLine($"Min FPS:     {_fpsSamples.Min():F1}");
        sb.AppendLine($"Max FPS:     {_fpsSamples.Max():F1}");
        if (_updateTimeSamples.Count > 0)
        {
            sb.AppendLine($"Avg Update:  {_updateTimeSamples.Average():F1} ms");
            sb.AppendLine($"Max Update:  {_updateTimeSamples.Max():F1} ms");
        }
        // Always emit Total Renders so easy-mode (no-ETW) baselines have a
        // free cross-framework throughput proxy. See METHODOLOGY.md.
        sb.AppendLine($"Total Renders: {_renderCount}");
        if (_reconcileTimeSamples.Count > 0)
        {
            sb.AppendLine($"Avg Reconcile: {_reconcileTimeSamples.Average():F1} ms");
            sb.AppendLine($"Max Reconcile: {_reconcileTimeSamples.Max():F1} ms");
        }
        if (_treeBuildSamples.Count > 0)
        {
            sb.AppendLine($"  Avg Tree:    {_treeBuildSamples.Average():F1} ms");
            sb.AppendLine($"  Avg Diff:    {_diffPatchSamples.Average():F1} ms");
            sb.AppendLine($"  Avg Effects: {_effectsSamples.Average():F1} ms");
        }
        if (_updateTimeSamples.Count > 0 && _reconcileTimeSamples.Count > 0)
        {
            // Per-tick combined cost: total work (update + reconcile) / number of ticks.
            // This correctly handles coalescing where R renders < U ticks.
            int ticks = _updateTimeSamples.Count;
            double combinedPerTick = (_updateTimeSamples.Sum() + _reconcileTimeSamples.Sum()) / ticks;
            sb.AppendLine($"Avg Combined:  {combinedPerTick:F1} ms  (renders/tick: {(double)_reconcileTimeSamples.Count / ticks:F2})");
        }
        sb.AppendLine($"Avg Memory:  {_memorySamples.Average() / (1024 * 1024):F1} MB");
        sb.AppendLine($"Peak Memory: {_memorySamples.Max() / (1024 * 1024):F1} MB");
        return sb.ToString();
    }

    /// <summary>
    /// Write report to a file next to the executable.
    /// </summary>
    public void WriteReportFile(string appName, double percent)
    {
        var report = GetReport(appName, percent);
        var path = Path.Combine(AppContext.BaseDirectory, $"{appName}.report.txt");
        File.WriteAllText(path, report);

        var csv = new StringBuilder();
        csv.AppendLine("Second,FPS,Memory_MB");
        int n = Math.Min(_fpsSamples.Count, _memorySamples.Count);
        for (int i = 0; i < n; i++)
        {
            double mb = _memorySamples[i] / (1024.0 * 1024.0);
            csv.AppendLine($"{i + 1},{_fpsSamples[i]:F2},{mb:F1}");
        }
        var csvPath = Path.Combine(AppContext.BaseDirectory, $"{appName}.samples.csv");
        File.WriteAllText(csvPath, csv.ToString());
    }
}
