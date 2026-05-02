// PresentTracer — subscribes to the OS providers that emit composition /
// present / DWM events, filters by target PID, and reports per-(provider,
// event) counts plus interval-percentiles.
//
// Goal: ground-truth "how often did the OS actually push pixels for this
// process" so we can compare frameworks (Reactor / RN-Fabric / WPF / …)
// without trusting their in-app FPS counters. Reactor's
// CompositionTarget.Rendering and RN's requestAnimationFrame measure
// different things; this measures the same thing for every app.
//
// Captures a global VSyncDPC count regardless of filter — display refresh
// is a system-wide signal and Dynamic Refresh Rate (Win11 + battery) can
// vary it per-app based on GPU activity. We surface it as a pseudo-row
// with Provider="GLOBAL" so the harness can record it alongside per-PID
// counts.
//
// Must run elevated — kernel-mode providers (DxgKrnl) need it.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

internal static class Program
{
    private static int Main(string[] args)
    {
        int? targetPid = null;
        string? procName = null;
        int durationSec = 10;
        string? csvPath = null;
        bool allPids = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid" when i + 1 < args.Length:
                    targetPid = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--proc-name" when i + 1 < args.Length:
                    procName = args[++i];
                    break;
                case "--duration" when i + 1 < args.Length:
                    durationSec = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--csv" when i + 1 < args.Length:
                    csvPath = args[++i];
                    break;
                case "--all-pids":
                    // Diagnostic mode: don't filter, group by ProcessName so
                    // we can see whether presents for "our" content are
                    // attributed to dwm.exe vs our app PID. Used to test
                    // the Win2D / CompositionDrawingSurface attribution
                    // hypothesis.
                    allPids = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
            }
        }

        if (!allPids && targetPid is null && procName is null)
        {
            PrintUsage();
            return 2;
        }

        if (!(TraceEventSession.IsElevated() ?? false))
        {
            Console.Error.WriteLine("PresentTracer must run elevated (ETW kernel session).");
            return 3;
        }

        var sessionName = "PresentTracer-" + Guid.NewGuid().ToString("N")[..8];
        using var session = new TraceEventSession(sessionName)
        {
            // 100 MB ring is plenty for a few seconds of these providers.
            BufferSizeMB = 100,
        };

        // Enable everything that could plausibly emit per-frame / per-present
        // events. We post-filter by event-count significance.
        session.EnableProvider("Microsoft-Windows-DxgKrnl", TraceEventLevel.Informational);
        session.EnableProvider("Microsoft-Windows-DXGI", TraceEventLevel.Informational);
        session.EnableProvider("Microsoft-Windows-Dwm-Core", TraceEventLevel.Informational);
        session.EnableProvider("Microsoft-Windows-Dwm-Api", TraceEventLevel.Informational);
        session.EnableProvider("Microsoft-Windows-DirectComposition", TraceEventLevel.Informational);

        // Per-(provider,event) state.
        var counts = new ConcurrentDictionary<string, int>();
        var timestamps = new ConcurrentDictionary<string, List<double>>();
        // PID-by-event count for --all-pids mode. Resolved to ProcessName
        // at report time via Process.GetProcessById since TraceEvent's
        // data.ProcessName is unreliable for kernel events (often empty
        // for short-lived or boundary-case processes).
        var pidEventCounts = new ConcurrentDictionary<(int pid, string ev), int>();
        var pidEventTimestamps = new ConcurrentDictionary<(int pid, string ev), List<double>>();
        // Global VSync count — independent of filter so we can see Dynamic
        // Refresh Rate / battery throttling effects on the actual display
        // refresh rate during the bench.
        int globalVsyncCount = 0;
        var globalVsyncTimestamps = new List<double>(2048);
        // For "all events with a non-zero PID" so we can see what got attributed.
        long firstEventTs = -1;
        long lastEventTs = -1;
        int eventsConsidered = 0;
        int eventsKept = 0;

        bool MatchProcess(TraceEvent data)
        {
            if (targetPid is int pid)
                return data.ProcessID == pid;
            // Fallback: substring match on process name (rare; not all events
            // populate ProcessName).
            return procName is not null
                && (data.ProcessName?.Contains(procName, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        // In --all-pids mode, only capture the present-relevant events to
        // keep the dataset bounded — the firehose of Profiler/Start/Stop
        // would otherwise dominate.
        var presentEventsOfInterest = new HashSet<string>(StringComparer.Ordinal)
        {
            "Present", "PresentHistory/Start", "Render", "VSyncInterrupt", "VSyncDPC"
        };

        session.Source.Dynamic.All += (data) =>
        {
            Interlocked.Increment(ref eventsConsidered);

            // Global vsync count — captured regardless of filter, used to
            // detect Dynamic Refresh Rate behavior. Kernel events may be
            // populated with PID 0; we don't care.
            if (data.ProviderName == "Microsoft-Windows-DxgKrnl"
                && (data.EventName == "VSyncDPC" || data.EventName == "VSyncInterrupt"))
            {
                Interlocked.Increment(ref globalVsyncCount);
                globalVsyncTimestamps.Add(data.TimeStampRelativeMSec);
            }

            if (!allPids && !MatchProcess(data)) return;
            if (allPids
                && data.ProviderName != "Microsoft-Windows-DxgKrnl") return;
            if (allPids
                && !presentEventsOfInterest.Contains(data.EventName)) return;
            Interlocked.Increment(ref eventsKept);

            var ts = (long)(data.TimeStampRelativeMSec * 1000);
            if (firstEventTs < 0) firstEventTs = ts;
            lastEventTs = ts;

            if (allPids)
            {
                // Track by PID; we'll resolve names at report time. This
                // works around TraceEvent's empty ProcessName for kernel
                // events.
                var k = (pid: data.ProcessID, ev: data.ProviderName + "::" + data.EventName);
                pidEventCounts.AddOrUpdate(k, 1, (_, v) => v + 1);
                var list2 = pidEventTimestamps.GetOrAdd(k, _ => new List<double>(1024));
                list2.Add(data.TimeStampRelativeMSec);
            }
            else
            {
                string key = data.ProviderName + "::" + data.EventName;
                counts.AddOrUpdate(key, 1, (_, v) => v + 1);
                var list = timestamps.GetOrAdd(key, _ => new List<double>(1024));
                list.Add(data.TimeStampRelativeMSec);
            }
        };

        string filterDesc = allPids
            ? "ALL PIDS (DxgKrnl present-events only, grouped by ProcessName)"
            : (targetPid is int p ? $"PID={p}" : $"Name~{procName}");
        Console.Error.WriteLine($"PresentTracer running for {durationSec}s, filter={filterDesc}");

        var processTask = Task.Run(() => session.Source.Process());

        Thread.Sleep(durationSec * 1000);

        session.Stop();
        // Give the dispatcher a moment to drain any pending events.
        Thread.Sleep(750);

        // ── Resolve PIDs → names for --all-pids mode ──────────────────────
        // TraceEvent's data.ProcessName comes through empty for many kernel
        // events. We captured ProcessIDs instead and resolve them here via
        // System.Diagnostics.Process. Short-lived / exited processes fall
        // back to "?(pid)".
        Dictionary<int, string> pidNameMap = new();
        string ResolvePid(int pid)
        {
            if (pid == 0) return "system/idle";
            if (pidNameMap.TryGetValue(pid, out var n)) return n;
            try
            {
                var p = Process.GetProcessById(pid);
                pidNameMap[pid] = p.ProcessName;
                return p.ProcessName;
            }
            catch
            {
                pidNameMap[pid] = $"?({pid})";
                return pidNameMap[pid];
            }
        }

        // For --all-pids mode, fold the per-(pid,event) buckets into
        // per-(name,event) for the CSV / display.
        if (allPids)
        {
            foreach (var ((pid, ev), c) in pidEventCounts)
            {
                var name = ResolvePid(pid);
                var key = name + "::" + ev;
                counts.AddOrUpdate(key, c, (_, v) => v + c);
                if (pidEventTimestamps.TryGetValue((pid, ev), out var ts))
                {
                    var list = timestamps.GetOrAdd(key, _ => new List<double>(ts.Count));
                    list.AddRange(ts);
                }
            }
        }

        double globalVsyncPerSec = globalVsyncCount / (double)durationSec;

        Console.WriteLine();
        Console.WriteLine("=== PresentTracer summary ===");
        Console.WriteLine($"Window:          {durationSec}s");
        Console.WriteLine($"Events seen:     {eventsConsidered}");
        Console.WriteLine($"Events matched:  {eventsKept}");
        Console.WriteLine($"Elapsed (us):    {(lastEventTs - firstEventTs)}");
        Console.WriteLine($"Global VSync:    {globalVsyncCount} ({globalVsyncPerSec:F1}/s)  ◀── display refresh during the window");
        Console.WriteLine();
        Console.WriteLine("Per-(provider, event) for matched PID:");
        Console.WriteLine($"  {"Count",8}  {"P50ms",8}  {"P95ms",8}  {"P99ms",8}  {"Per-sec",8}  Provider::Event");

        var ordered = counts.OrderByDescending(kvp => kvp.Value).ToList();
        foreach (var (key, count) in ordered)
        {
            var ts = timestamps.GetValueOrDefault(key);
            string p50 = "-", p95 = "-", p99 = "-";
            if (ts is { Count: > 2 })
            {
                ts.Sort();
                var intervals = new List<double>(ts.Count - 1);
                for (int i = 1; i < ts.Count; i++) intervals.Add(ts[i] - ts[i - 1]);
                intervals.Sort();
                p50 = intervals[intervals.Count / 2].ToString("F2", CultureInfo.InvariantCulture);
                p95 = intervals[(int)(intervals.Count * 0.95)].ToString("F2", CultureInfo.InvariantCulture);
                p99 = intervals[Math.Min(intervals.Count - 1, (int)(intervals.Count * 0.99))]
                    .ToString("F2", CultureInfo.InvariantCulture);
            }
            double perSec = count / (double)durationSec;
            Console.WriteLine($"  {count,8:N0}  {p50,8}  {p95,8}  {p99,8}  {perSec,8:F1}  {key}");
        }

        if (csvPath is not null)
        {
            var sb = new StringBuilder();
            if (allPids)
                sb.AppendLine("ProcessName,Provider,Event,Count,PerSec,P50ms,P95ms,P99ms");
            else
                sb.AppendLine("Provider,Event,Count,PerSec,P50ms,P95ms,P99ms");
            // Always emit a global vsync row so the harness can read display
            // refresh rate without parsing the human-readable summary.
            // ProcessName="*GLOBAL*" in all-pids mode; column-omitted in
            // PID-filtered mode (Provider is "GLOBAL" so consumers can find it).
            if (allPids)
                sb.AppendLine($"*GLOBAL*,GLOBAL,VSync,{globalVsyncCount},{globalVsyncPerSec.ToString("F2", CultureInfo.InvariantCulture)},,,");
            else
                sb.AppendLine($"GLOBAL,VSync,{globalVsyncCount},{globalVsyncPerSec.ToString("F2", CultureInfo.InvariantCulture)},,,");
            foreach (var (key, count) in ordered)
            {
                var parts = key.Split("::", 3);
                var ts = timestamps.GetValueOrDefault(key);
                string p50 = "", p95 = "", p99 = "";
                if (ts is { Count: > 2 })
                {
                    ts.Sort();
                    var intervals = new List<double>(ts.Count - 1);
                    for (int i = 1; i < ts.Count; i++) intervals.Add(ts[i] - ts[i - 1]);
                    intervals.Sort();
                    p50 = intervals[intervals.Count / 2].ToString("F2", CultureInfo.InvariantCulture);
                    p95 = intervals[(int)(intervals.Count * 0.95)].ToString("F2", CultureInfo.InvariantCulture);
                    p99 = intervals[Math.Min(intervals.Count - 1, (int)(intervals.Count * 0.99))]
                        .ToString("F2", CultureInfo.InvariantCulture);
                }
                double perSec = count / (double)durationSec;
                if (allPids && parts.Length >= 3)
                    sb.AppendLine($"{parts[0]},{parts[1]},{parts[2]},{count},{perSec.ToString("F2", CultureInfo.InvariantCulture)},{p50},{p95},{p99}");
                else
                    sb.AppendLine($"{parts[0]},{parts[1]},{count},{perSec.ToString("F2", CultureInfo.InvariantCulture)},{p50},{p95},{p99}");
            }
            File.WriteAllText(csvPath, sb.ToString());
            Console.WriteLine($"CSV: {csvPath}");
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: PresentTracer (--pid N | --proc-name X | --all-pids) [--duration S] [--csv path]");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  --pid N        Filter events to a specific process ID.");
        Console.WriteLine("  --proc-name X  Filter events by process name (substring).");
        Console.WriteLine("  --all-pids     Diagnostic: capture present-related DxgKrnl events");
        Console.WriteLine("                 across all processes, group by ProcessName. Used to");
        Console.WriteLine("                 detect cases where 'our' presents are attributed to");
        Console.WriteLine("                 dwm.exe (Win2D / CompositionDrawingSurface case).");
        Console.WriteLine();
        Console.WriteLine("Subscribes to DxgKrnl / DXGI / Dwm-Core / Dwm-Api / DirectComposition.");
        Console.WriteLine("Reports event counts + interval percentiles per (provider, event).");
        Console.WriteLine("Must be run elevated (ETW kernel session).");
    }
}
