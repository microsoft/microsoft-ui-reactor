using System.Diagnostics;
using System.Text;

namespace PerfBench.Shared;

/// <summary>
/// Extended performance tracker for PerfBench experiments.
/// Adds GC metrics, input latency, frame block tracking, and mutation counting
/// on top of the base FPS / Update ms / Memory metrics.
/// </summary>
public sealed class BenchTracker
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

    // GC tracking (EXP-10)
    private int _baselineGen0;
    private int _baselineGen1;
    private int _baselineGen2;
    private readonly List<int> _gen0Samples = new();
    private readonly List<int> _gen1Samples = new();

    // Frame block tracking (EXP-7)
    private double _lastRenderingTimestamp;
    private double _longestFrameBlockMs;
    private int _animationDrops; // frames where delta > 20ms
    private readonly List<double> _frameDeltas = new();

    // Mutation count (EXP-5)
    private long _totalMutations;
    private long _frameMutations;

    // Input latency tracking (EXP-4, EXP-8)
    private readonly List<double> _inputLatencySamples = new();

    // UI thread blocked time (EXP-4)
    private readonly Stopwatch _uiBlockSw = new();
    private readonly List<double> _uiBlockSamples = new();

    // Reconcile vs apply phase (EXP-5)
    private readonly Stopwatch _reconcileSw = new();
    private readonly Stopwatch _applySw = new();
    private readonly List<double> _reconcileSamples = new();
    private readonly List<double> _applySamples = new();

    // Mount time tracking (EXP-6, EXP-9)
    private readonly Stopwatch _mountSw = new();
    private readonly List<double> _mountSamples = new();

    public double CurrentFps => _currentFps;
    public double LastUpdateMs => _lastUpdateMs;
    public long CurrentMemoryMB => Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
    public double LongestFrameBlockMs => _longestFrameBlockMs;
    public int AnimationDrops => _animationDrops;
    public double ElapsedSeconds => _wallClock.Elapsed.TotalSeconds;

    public void ResetGcBaseline()
    {
        _baselineGen0 = GC.CollectionCount(0);
        _baselineGen1 = GC.CollectionCount(1);
        _baselineGen2 = GC.CollectionCount(2);
    }

    /// <summary>
    /// Call from CompositionTarget.Rendering to count composed frames and track frame deltas.
    /// </summary>
    public void FrameRendered()
    {
        _frameCount++;
        double now = _wallClock.Elapsed.TotalSeconds;

        // Frame delta tracking for animation jank detection
        if (_lastRenderingTimestamp > 0)
        {
            double deltaMs = (now - _lastRenderingTimestamp) * 1000.0;
            _frameDeltas.Add(deltaMs);
            if (deltaMs > _longestFrameBlockMs)
                _longestFrameBlockMs = deltaMs;
            if (deltaMs > 20.0)
                _animationDrops++;
        }
        _lastRenderingTimestamp = now;

        double elapsed = now - _lastSampleTime;
        if (elapsed >= 1.0)
        {
            _currentFps = _frameCount / elapsed;
            _fpsSamples.Add(_currentFps);
            _memorySamples.Add(Process.GetCurrentProcess().WorkingSet64);
            _gen0Samples.Add(GC.CollectionCount(0) - _baselineGen0);
            _gen1Samples.Add(GC.CollectionCount(1) - _baselineGen1);
            _frameCount = 0;
            _lastSampleTime = now;
        }
    }

    public void BeginUpdate() => _updateSw.Restart();

    public void EndUpdate()
    {
        _updateSw.Stop();
        _lastUpdateMs = _updateSw.Elapsed.TotalMilliseconds;
        _updateTimeSamples.Add(_lastUpdateMs);
    }

    // UI thread blocked time
    public void BeginUiBlock() => _uiBlockSw.Restart();
    public void EndUiBlock()
    {
        _uiBlockSw.Stop();
        _uiBlockSamples.Add(_uiBlockSw.Elapsed.TotalMilliseconds);
    }

    // Reconcile phase
    public void BeginReconcile() => _reconcileSw.Restart();
    public void EndReconcile()
    {
        _reconcileSw.Stop();
        _reconcileSamples.Add(_reconcileSw.Elapsed.TotalMilliseconds);
    }

    // Apply phase
    public void BeginApply() => _applySw.Restart();
    public void EndApply()
    {
        _applySw.Stop();
        _applySamples.Add(_applySw.Elapsed.TotalMilliseconds);
    }

    // Mount time
    public void BeginMount() => _mountSw.Restart();
    public void EndMount()
    {
        _mountSw.Stop();
        _mountSamples.Add(_mountSw.Elapsed.TotalMilliseconds);
    }

    // Mutation count
    public void RecordMutations(int count)
    {
        _frameMutations = count;
        _totalMutations += count;
    }

    // Input latency
    public void RecordInputLatency(double ms) => _inputLatencySamples.Add(ms);

    public string GetReport(string appName)
    {
        if (_fpsSamples.Count == 0) return "No data collected.";

        var sb = new StringBuilder();
        sb.AppendLine($"=== {appName} ===");
        sb.AppendLine($"Duration:      {_wallClock.Elapsed.TotalSeconds:F1}s");
        // <snippet:bench-report-fields>
        sb.AppendLine($"Avg FPS:       {_fpsSamples.Average():F1}");
        sb.AppendLine($"Min FPS:       {_fpsSamples.Min():F1}");
        sb.AppendLine($"Max FPS:       {_fpsSamples.Max():F1}");

        if (_updateTimeSamples.Count > 0)
        {
            sb.AppendLine($"Avg Update:    {_updateTimeSamples.Average():F2} ms");
            sb.AppendLine($"Max Update:    {_updateTimeSamples.Max():F2} ms");
        }

        sb.AppendLine($"Avg Memory:    {_memorySamples.Average() / (1024 * 1024):F1} MB");
        sb.AppendLine($"Peak Memory:   {_memorySamples.Max() / (1024 * 1024):F1} MB");

        // GC
        int gen0Total = GC.CollectionCount(0) - _baselineGen0;
        int gen1Total = GC.CollectionCount(1) - _baselineGen1;
        int gen2Total = GC.CollectionCount(2) - _baselineGen2;
        sb.AppendLine($"GC Gen0:       {gen0Total}");
        sb.AppendLine($"GC Gen1:       {gen1Total}");
        sb.AppendLine($"GC Gen2:       {gen2Total}");

        // Frame jank
        sb.AppendLine($"Longest Block: {_longestFrameBlockMs:F1} ms");
        sb.AppendLine($"Anim Drops:    {_animationDrops}");
        // </snippet:bench-report-fields>

        if (_uiBlockSamples.Count > 0)
            sb.AppendLine($"Avg UI Block:  {_uiBlockSamples.Average():F2} ms");

        if (_reconcileSamples.Count > 0)
            sb.AppendLine($"Avg Reconcile: {_reconcileSamples.Average():F2} ms");

        if (_applySamples.Count > 0)
            sb.AppendLine($"Avg Apply:     {_applySamples.Average():F2} ms");

        if (_mountSamples.Count > 0)
            sb.AppendLine($"Avg Mount:     {_mountSamples.Average():F2} ms");

        if (_inputLatencySamples.Count > 0)
            sb.AppendLine($"Avg Input Lat: {_inputLatencySamples.Average():F2} ms");

        if (_totalMutations > 0)
            sb.AppendLine($"Total Mutations: {_totalMutations}");

        return sb.ToString();
    }

    public void WriteReportFile(string appName)
    {
        var report = GetReport(appName);
        Console.Write(report);
        var path = Path.Combine(AppContext.BaseDirectory, $"{appName}.report.txt");
        File.WriteAllText(path, report);
    }
}
