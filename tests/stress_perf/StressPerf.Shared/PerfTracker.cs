using System.Diagnostics;
using System.Globalization;
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

    // Managed-allocation accounting for the render loop. The baseline is captured
    // lazily on the FIRST recorded render (see RecordRender) so app-startup
    // allocations (XAML load, first layout) are excluded and we measure the
    // steady-state per-render cost. These are process-wide cumulative counters
    // (GC.GetTotalAllocatedBytes / GC.CollectionCount) — AOT/trim-safe and
    // require no changes to the Reactor host being measured. Allocation-reduction
    // PRs move these directly, while the mean-ms / working-set metrics are largely
    // blind to them. See METHODOLOGY.md.
    private bool _allocBaselineCaptured;
    private long _startAllocBytes;
    private int _startGen0;
    private int _startGen1;
    private int _startGen2;

    // End snapshot of the cumulative GC counters, frozen exactly once — the first
    // time a report/JSON is produced or an allocation property is read — and
    // BEFORE any report-string building. Because GetTotalAllocatedBytes/
    // CollectionCount are process-wide cumulative counters, freezing them up front
    // keeps allocations from report/JSON generation itself out of the measured
    // window and guarantees report.txt and metrics.json report identical figures.
    private bool _allocEndCaptured;
    private long _endAllocBytes;
    private int _endGen0;
    private int _endGen1;
    private int _endGen2;
    private int _endRenderCount;

    // ── Startup / first-frame anchors (one-shot per process) ─────────────────
    // The from-scratch mount (build the whole element tree + create every WinUI
    // control + first layout) is EXCLUDED from the steady-state alloc/timing windows
    // by construction (RecordRender baselines on the first benchmark tick), so these
    // fields are the only place the #696-class first-render cost is captured. All ms
    // are measured from managed entry (StartupTiming.MarkEntry, called at the top of
    // Main); see StartupTiming + RecordFirstRenderIfUnset.
    private bool _firstRenderCaptured;
    private double _firstReconcileDurationMs;
    private double _entryToFirstReconcileMs;
    private double? _windowOpenToFirstReconcileMs;
    private bool _firstFrameCaptured;
    private double _entryToFirstFrameMs;

    public double CurrentFps => _currentFps;
    public double LastUpdateMs => _lastUpdateMs;
    public long CurrentMemoryMB => Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

    /// <summary>
    /// Call from CompositionTarget.Rendering to count composed frames.
    /// </summary>
    public void FrameRendered()
    {
        // First composed frame AFTER the from-scratch mount = the user-facing "first
        // frame rendered". Gated on the first render being captured so an empty
        // pre-content composition tick can't claim it; this guarantees
        // T_firstFrame >= T_firstReconcile (monotonic) for the paired CI.
        if (_firstRenderCaptured && !_firstFrameCaptured)
        {
            _firstFrameCaptured = true;
            _entryToFirstFrameMs = StartupTiming.MsSinceEntry();
        }
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
    public void RecordRender()
    {
        // Capture the allocation baseline on the first render so per-render
        // allocation figures reflect the steady-state render loop, not one-time
        // app startup. Cheap, AOT-safe, and identical for every variant.
        if (!_allocBaselineCaptured)
        {
            _allocBaselineCaptured = true;
            _startAllocBytes = GC.GetTotalAllocatedBytes();
            _startGen0 = GC.CollectionCount(0);
            _startGen1 = GC.CollectionCount(1);
            _startGen2 = GC.CollectionCount(2);
        }
        _renderCount++;
    }

    /// <summary>
    /// Freeze the end-of-measurement allocation/GC counters exactly once, before
    /// any report or JSON string is built. Idempotent: the first call wins, so
    /// every consumer (report.txt, metrics.json, direct property reads) sees the
    /// same numbers and string-building allocations never leak into the window.
    /// No-op until the first render has set the baseline.
    /// </summary>
    private void CaptureFinalSnapshot()
    {
        if (_allocEndCaptured || !_allocBaselineCaptured) return;
        _endAllocBytes = GC.GetTotalAllocatedBytes();
        _endGen0 = GC.CollectionCount(0);
        _endGen1 = GC.CollectionCount(1);
        _endGen2 = GC.CollectionCount(2);
        _endRenderCount = _renderCount;
        _allocEndCaptured = true;
    }

    // Renders fully inside the allocation window. The baseline is captured during
    // the FIRST RecordRender, so that render's own allocations sit in the baseline
    // and only renders 2..N contribute to the measured delta. Normalising the
    // delta by (N-1) keeps numerator and denominator over the same window and
    // avoids a small systematic downward bias.
    private int AllocWindowRenders => _allocEndCaptured ? Math.Max(0, _endRenderCount - 1) : 0;

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

    // ── Startup / first-frame metric ─────────────────────────────────────────
    // Captured once per process on the first from-scratch mount + first composed
    // frame, piggybacking the existing per-rep process launches. Tier 1: always-on,
    // zero extra CI. See the first-frame design note + StartupTiming.

    /// <summary>
    /// One-shot capture of the from-scratch mount on the FIRST OnRenderComplete. Call
    /// from the host's OnRenderComplete callback BEFORE any benchmark gate so the very
    /// first render — the full mount the steady-state windows exclude — is recorded.
    /// <paramref name="reconcileMs"/> is the host's reconcile/diff-patch phase
    /// duration (the 2nd OnRenderComplete arg): the Reactor-isolated first-render
    /// signal, undiluted by bootstrap (entry→first-frame is bootstrap-dominated).
    /// Idempotent — only the first call records.
    /// </summary>
    public void RecordFirstRenderIfUnset(double treeBuildMs, double reconcileMs, double effectsMs)
    {
        if (_firstRenderCaptured) return;
        _firstRenderCaptured = true;
        _firstReconcileDurationMs = reconcileMs;
        _entryToFirstReconcileMs = StartupTiming.MsSinceEntry();
        // Window-open → first reconcile only when Activated demonstrably preceded the
        // mount (monotonic). Activated-vs-mount ordering is non-deterministic across
        // launches; emit n/a (null) rather than a negative number that would poison a
        // paired CI on the consuming side.
        double? winOpen = StartupTiming.WindowOpenMsSinceEntry();
        _windowOpenToFirstReconcileMs = (winOpen.HasValue && winOpen.Value <= _entryToFirstReconcileMs)
            ? _entryToFirstReconcileMs - winOpen.Value
            : null;
    }

    /// <summary>
    /// First-render reconcile/diff-patch duration (ms) — the 2nd OnRenderComplete arg
    /// of the from-scratch mount — or null when no render has completed. Directly
    /// comparable to the steady-state <see cref="AvgDiffMs"/> (same phase); the mount
    /// value is much larger because it creates every control rather than patching a few.
    /// </summary>
    public double? FirstReconcileDurationMs => _firstRenderCaptured ? _firstReconcileDurationMs : null;

    /// <summary>Managed entry → first reconcile complete (ms), or null. Always defined once a render completes (entry precedes mount).</summary>
    public double? EntryToFirstReconcileMs => _firstRenderCaptured ? _entryToFirstReconcileMs : null;

    /// <summary>Window Activated → first reconcile (ms), or null (n/a) when the ordering was non-monotonic or the window never reported Activated.</summary>
    public double? WindowOpenToFirstReconcileMs => _firstRenderCaptured ? _windowOpenToFirstReconcileMs : null;

    /// <summary>Managed entry → first composed frame after mount (ms) — the user-facing "first frame rendered" — or null when no frame composed after the mount.</summary>
    public double? EntryToFirstFrameMs => _firstFrameCaptured ? _entryToFirstFrameMs : null;

    public double ElapsedSeconds => _wallClock.Elapsed.TotalSeconds;

    public string GetReport(string appName, double percent)
    {
        if (_fpsSamples.Count == 0) return "No data collected.";

        // Freeze the allocation/GC end-counters before building any report string
        // so report generation's own allocations never enter the measured window.
        CaptureFinalSnapshot();

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
        // Allocation accounting (steady-state render loop; baseline = first render).
        // Lower is better. The mean-ms / working-set metrics above are largely blind
        // to allocation-reduction changes, so these are the sensitive signal for them.
        sb.AppendLine($"Alloc/render: {AllocBytesPerRender:F0} bytes");
        sb.AppendLine($"GC Gen0/1/2: {Gen0Collections} / {Gen1Collections} / {Gen2Collections}");
        sb.AppendLine($"Gen0/Krender: {Gen0PerKRenders:F2}");
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

    // ── Machine-readable metrics (CI) ────────────────────────────────────────
    // The on-demand perf-comparison workflow parses these four headline numbers
    // to diff a PR against the main baseline. Renders/sec is "higher is better";
    // the three latency/memory figures are "lower is better". Kept here (rather
    // than scraped from GetReport) so CI never has to depend on the exact prose
    // layout of the human report, and so missing phase samples surface as 0
    // rather than an absent line. See .github/workflows/perf-compare.yml.

    /// <summary>Average reconcile cost (ms) across all recorded render passes, or 0.</summary>
    public double AvgReconcileMs => _reconcileTimeSamples.Count > 0 ? _reconcileTimeSamples.Average() : 0.0;

    /// <summary>Average diff/patch cost (ms) across all recorded render passes, or 0.</summary>
    public double AvgDiffMs => _diffPatchSamples.Count > 0 ? _diffPatchSamples.Average() : 0.0;

    /// <summary>Average sampled working set in MB, or 0 when no samples were taken.</summary>
    public double AvgMemoryMB => _memorySamples.Count > 0 ? _memorySamples.Average() / (1024.0 * 1024.0) : 0.0;

    /// <summary>
    /// Throughput proxy: total renders divided by measured wall-clock seconds.
    /// Mirrors the methodology's <c>Total Renders / Duration</c> (METHODOLOGY.md,
    /// "easy mode") since both use the same <see cref="ElapsedSeconds"/> clock.
    /// </summary>
    public double RendersPerSec => ElapsedSeconds > 0 ? _renderCount / ElapsedSeconds : 0.0;

    /// <summary>
    /// Mean managed bytes allocated per recorded render across the measurement
    /// window (first render → report time), or 0. Lower is better. This is the
    /// metric allocation-reduction PRs move directly; the mean-ms and working-set
    /// figures are largely insensitive to allocation churn. Reads the frozen
    /// end-snapshot and normalises by renders-since-baseline. See METHODOLOGY.md.
    /// </summary>
    public double AllocBytesPerRender
    {
        get
        {
            CaptureFinalSnapshot();
            int n = AllocWindowRenders;
            return n > 0 ? (_endAllocBytes - _startAllocBytes) / (double)n : 0.0;
        }
    }

    /// <summary>Gen0 garbage collections during the measurement window, or 0.</summary>
    public int Gen0Collections { get { CaptureFinalSnapshot(); return _allocEndCaptured ? _endGen0 - _startGen0 : 0; } }

    /// <summary>Gen1 garbage collections during the measurement window, or 0.</summary>
    public int Gen1Collections { get { CaptureFinalSnapshot(); return _allocEndCaptured ? _endGen1 - _startGen1 : 0; } }

    /// <summary>Gen2 garbage collections during the measurement window, or 0.</summary>
    public int Gen2Collections { get { CaptureFinalSnapshot(); return _allocEndCaptured ? _endGen2 - _startGen2 : 0; } }

    /// <summary>
    /// Gen0 collections per 1,000 renders (render-rate-normalised so it is
    /// comparable across runs of differing length), or 0. Lower is better.
    /// Normalised by renders-since-baseline to match the collection window.
    /// </summary>
    public double Gen0PerKRenders
    {
        get
        {
            CaptureFinalSnapshot();
            int n = AllocWindowRenders;
            return n > 0 ? Gen0Collections * 1000.0 / n : 0.0;
        }
    }

    /// <summary>
    /// Compact, single-line, culture-invariant JSON with the four headline
    /// metrics plus context. Built by hand (no serializer) to stay trivially
    /// AOT/trim-safe for this PublishAot harness.
    /// </summary>
    public string GetMetricsJson(string appName, double percent)
    {
        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        // Nullable variant for the optional startup fields: emit JSON null for an
        // un-captured anchor (e.g. window-open when Activated trailed the mount) so a
        // within-run n/a is distinguishable from a hard 0 and the PS parser reads it
        // as n/a via its existing null guard.
        static string FN(double? v) => v.HasValue ? v.Value.ToString("0.####", CultureInfo.InvariantCulture) : "null";
        // Minimal JSON string escaping for the one string field (appName) so the
        // hand-built JSON stays valid even if a name ever carries a quote /
        // backslash / control char. The numbers are culture-invariant already.
        static string J(string s)
        {
            var b = new StringBuilder(s.Length + 2);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            return b.ToString();
        }
        // Freeze the allocation/GC end-counters before building the JSON so the
        // payload's own allocations never enter the measured window (and so this
        // matches report.txt exactly when both are written).
        CaptureFinalSnapshot();
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"app\":\"").Append(J(appName)).Append("\",");
        sb.Append("\"percent\":").Append(F(percent)).Append(',');
        sb.Append("\"durationSeconds\":").Append(F(ElapsedSeconds)).Append(',');
        sb.Append("\"rendersPerSec\":").Append(F(RendersPerSec)).Append(',');
        sb.Append("\"totalRenders\":").Append(_renderCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"avgReconcileMs\":").Append(F(AvgReconcileMs)).Append(',');
        sb.Append("\"avgDiffMs\":").Append(F(AvgDiffMs)).Append(',');
        sb.Append("\"avgMemoryMB\":").Append(F(AvgMemoryMB)).Append(',');
        sb.Append("\"allocBytesPerRender\":").Append(F(AllocBytesPerRender)).Append(',');
        sb.Append("\"gen0\":").Append(Gen0Collections.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"gen1\":").Append(Gen1Collections.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"gen2\":").Append(Gen2Collections.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"gen0PerKRenders\":").Append(F(Gen0PerKRenders)).Append(',');
        // Startup / first-frame metric (optional fields; a PR head built before this
        // metric landed simply omits them and the PS side reads n/a, exactly like the
        // alloc fields above). firstReconcileDurationMs is the Reactor-isolated #696 signal.
        sb.Append("\"firstReconcileDurationMs\":").Append(FN(FirstReconcileDurationMs)).Append(',');
        sb.Append("\"entryToFirstReconcileMs\":").Append(FN(EntryToFirstReconcileMs)).Append(',');
        sb.Append("\"windowOpenToFirstReconcileMs\":").Append(FN(WindowOpenToFirstReconcileMs)).Append(',');
        sb.Append("\"entryToFirstFrameMs\":").Append(FN(EntryToFirstFrameMs)).Append(',');
        sb.Append("\"avgFps\":").Append(F(_fpsSamples.Count > 0 ? _fpsSamples.Average() : 0.0)).Append(',');
        sb.Append("\"sampleCount\":").Append(_fpsSamples.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Write the machine-readable metrics to <c>{appName}.metrics.json</c> next
    /// to the executable (alongside the human report written by
    /// <see cref="WriteReportFile"/>).
    /// </summary>
    public void WriteMetricsJsonFile(string appName, double percent)
    {
        // GetFileName() strips any directory/rooted segment, and Path.Join (not
        // Path.Combine) concatenates without rooted-path reset semantics, so a
        // stray appName can't redirect the write or drop BaseDirectory.
        var safeAppName = Path.GetFileName(appName);
        var fileName = $"{safeAppName}.metrics.json";
        var path = Path.Join(AppContext.BaseDirectory, fileName);
        File.WriteAllText(path, GetMetricsJson(appName, percent));
    }
}
