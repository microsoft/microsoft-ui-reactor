using System.Globalization;
using System.Diagnostics;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.RowMemo;

/// <summary>
/// Issue #327 — minimal repro that ISOLATES and MEASURES the perf win of opt-in keyed row
/// memoization (<c>Memo(key, () =&gt; row)</c>) — the thing the StocksGrid <c>/perf</c> harness
/// can't show, because that harness never opts into the API (so the optimization is dormant
/// there and every metric reads "within noise").
///
/// <para>The win lives on ONE path: an ItemsRepeater recycling a container during fast scroll
/// over variable-height rows. Each recycle calls the real realize entry point
/// <see cref="ElementFactory{T}.BuildOrCache"/>. On the VirtualList int-index path the built-in
/// <c>_viewBuilderCache</c> guard (<c>ReferenceEquals(item)</c>) never hits — the index is
/// re-boxed every call — so WITHOUT memo every recycle (1) rebuilds the row's whole Element
/// subtree and (2) hands the reconciler a fresh-but-equal tree it must walk node-by-node. WITH
/// <c>Memo(i, …)</c> a recycle that re-asks for a still-cached key returns the SAME instance, so
/// (1) the factory is never re-invoked and (2) <see cref="Element.CanSkipUpdate"/> short-circuits
/// on <c>ReferenceEquals</c> at the root → the reconciler returns without descending.</para>
///
/// <para>Runs headlessly (no window needed — this touches only Element construction +
/// BuildOrCache + the skip decision). Reports, per recycle: factory rebuilds, wall-time, bytes
/// allocated, for Baseline vs Memo. Real WinUI control-patch savings stack on top and aren't
/// captured here (that was the issue's original ETL signal).</para>
/// </summary>
internal static class RowMemoBench
{
    // A realistic, non-trivial VARIABLE-HEIGHT row (mirrors the issue's DataSystem demo row):
    // Border > HStack > [ badge Border > TextBlock ] + [ VStack > title + 3 subtitle lines ].
    // 9 Element nodes — the per-row subtree the reconciler must otherwise re-walk on every
    // recycle, and the allocation the rebuild otherwise re-incurs.
    private const int RowNodeCount = 9;

    private static Element DeepRow(int i) =>
        Border(
            HStack(12,
                Border(TextBlock($"#{i}")).Width(48).Height(32),
                VStack(4,
                    TextBlock($"Row {i} title"),
                    TextBlock($"subtitle line a - row {i}"),
                    TextBlock($"subtitle line b - row {i}"),
                    TextBlock($"subtitle line c - row {i}")
                )
            )
        ).Padding(8);

    private static ElementFactory<int> Factory(IReadOnlyList<int> items, Func<int, int, Element> vb)
        => new(items, vb, new Reconciler(), requestRerender: static () => { }, pool: null);

    // The real realize entry point. keyed:false = the legacy int-index VirtualList path, where
    // each call re-boxes `i` so the built-in _viewBuilderCache cannot hit.
    private static Element Realize(ElementFactory<int> f, int i)
        => f.BuildOrCache(i.ToString(CultureInfo.InvariantCulture), i, i, keyed: false);

    private const int Window = 50;          // realized working set (rows in/near the viewport)
    private const int WarmCycles = 300;     // populate cache + JIT before timing
    private const int MeasureCycles = 20_000;
    private const int Reps = 5;             // report the best (min-time) rep to cut noise

    public static void Run(string? outPath)
    {
        var items = Enumerable.Range(0, Window).ToList();

        // ── Baseline: no memo. The viewBuilder rebuilds the row on every realize. ──
        long baselineRebuilds = 0;
        var fb = Factory(items, (i, _) => { baselineRebuilds++; return DeepRow(i); });
        Warm(fb);
        // Measure the factory rebuilds for the SAME (best) rep whose time/alloc we report, by
        // reading the live counter across that rep — a genuine measurement, not a hardcoded
        // constant. For baseline this comes out to Window*MeasureCycles (it rebuilds on every
        // realize), but reading it back proves that rather than asserting it, so a regression
        // that unexpectedly let baseline cache would show up as a smaller number.
        var (baseNs, baseBytes, baselineMeasuredRebuilds) = TimeAndAlloc(fb, () => baselineRebuilds);

        // ── Memo: Memo(i, …). A recycle that re-asks a cached key returns the same instance. ──
        long memoRebuilds = 0;
        var fm = Factory(items, (i, _) => Memo(i, () => { memoRebuilds++; return DeepRow(i); }));
        Warm(fm);
        // Same measurement, symmetric with baseline: rebuilds during the reported rep. After the
        // warm pass populated the cache, a recycle re-asking a cached key never re-invokes the
        // inner factory, so this is 0.
        var (memoNs, memoBytes, memoMeasuredRebuilds) = TimeAndAlloc(fm, () => memoRebuilds);

        // ── Reconcile-skip precondition: prove what each arm hands the reconciler on re-realize. ──
        var fbb = Factory(items, (i, _) => DeepRow(i));
        var pB = Realize(fbb, 7); var cB = Realize(fbb, 7);
        bool baseSameInstance = ReferenceEquals(pB, cB);
        bool baseSkippable = Element.CanSkipUpdate(pB, cB);

        var fmm = Factory(items, (i, _) => Memo(i, () => DeepRow(i)));
        var pM = Realize(fmm, 7); var cM = Realize(fmm, 7);
        bool memoSameInstance = ReferenceEquals(pM, cM);
        bool memoSkippable = Element.CanSkipUpdate(pM, cM);

        long recyclesPerArm = (long)Window * MeasureCycles;
        double timeX = memoNs > 0 ? baseNs / memoNs : 0;
        double allocX = memoBytes > 0 ? baseBytes / (double)memoBytes : double.PositiveInfinity;

        var inv = CultureInfo.InvariantCulture;

        // ── Human-readable report ────────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine("Issue #327 - opt-in row memoization: same-build Baseline vs Memo");
        sb.AppendLine($"Workload: a {RowNodeCount}-node variable-height row, recycled through the REAL");
        sb.AppendLine($"ElementFactory.BuildOrCache path - window={Window} rows - {MeasureCycles:N0} scroll");
        sb.AppendLine($"passes = {recyclesPerArm:N0} recycles/arm - best of {Reps} - Release.");
        sb.AppendLine();
        sb.AppendLine($"{"Arm",-22}{"ns/recycle",13}{"bytes/recycle",15}{"factory rebuilds",18}");
        sb.AppendLine(new string('-', 68));
        sb.AppendLine($"{"Baseline (no memo)",-22}{baseNs,13:0.0}{baseBytes,15:N0}{baselineMeasuredRebuilds,18:N0}");
        sb.AppendLine($"{"Memo(i, ...)",-22}{memoNs,13:0.0}{memoBytes,15:N0}{memoMeasuredRebuilds,18:N0}");
        sb.AppendLine(new string('-', 68));
        sb.AppendLine($"WIN: {timeX:0.0}x faster per recycle - {allocX:0.0}x less allocation - rebuilds eliminated after first sighting");
        sb.AppendLine();
        sb.AppendLine("Reconcile-skip precondition (what each arm hands the reconciler on re-realize):");
        sb.AppendLine($"  Baseline: sameInstance={baseSameInstance}  CanSkipUpdate={baseSkippable}  -> reconciler must WALK the {RowNodeCount}-node subtree");
        sb.AppendLine($"  Memo    : sameInstance={memoSameInstance}  CanSkipUpdate={memoSkippable}  -> reconciler returns at the ROOT (1 node), descent skipped");
        Console.WriteLine();
        Console.WriteLine(sb);

        // ── Machine-parseable key=value lines (stable contract for PerfLib.ps1) ───
        var kv = new List<string>
        {
            $"baseline_ns={baseNs.ToString("0.0##", inv)}",
            $"baseline_bytes={baseBytes.ToString(inv)}",
            $"baseline_rebuilds={baselineMeasuredRebuilds.ToString(inv)}",
            $"baseline_same_instance={(baseSameInstance ? "true" : "false")}",
            $"baseline_can_skip={(baseSkippable ? "true" : "false")}",
            $"memo_ns={memoNs.ToString("0.0##", inv)}",
            $"memo_bytes={memoBytes.ToString(inv)}",
            $"memo_rebuilds={memoMeasuredRebuilds.ToString(inv)}",
            $"memo_same_instance={(memoSameInstance ? "true" : "false")}",
            $"memo_can_skip={(memoSkippable ? "true" : "false")}",
            $"recycles_per_arm={recyclesPerArm.ToString(inv)}",
            $"row_nodes={RowNodeCount.ToString(inv)}",
            $"window={Window.ToString(inv)}",
            $"reps={Reps.ToString(inv)}",
            $"measure_cycles={MeasureCycles.ToString(inv)}",
            $"time_win_x={timeX.ToString("0.0##", inv)}",
            $"alloc_win_x={(double.IsInfinity(allocX) ? "inf" : allocX.ToString("0.0##", inv))}",
        };

        Console.WriteLine("---ROWMEMO-KV---");
        foreach (var line in kv) Console.WriteLine(line);

        if (!string.IsNullOrWhiteSpace(outPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(outPath, kv);
                Console.WriteLine($"wrote {kv.Count} key=value lines -> {outPath}");
            }
            catch (Exception ex) when (
                ex is System.IO.IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or ArgumentException
                or NotSupportedException)
            {
                // Writing the --out file is a best-effort convenience (stdout carries the
                // authoritative key=value block the harness parses). Swallow only the expected
                // path/permission/IO failures so a bad --out never fails the bench; anything
                // unexpected still propagates.
                Console.Error.WriteLine($"failed to write --out '{outPath}': {ex.Message}");
            }
        }
    }

    private static void Warm(ElementFactory<int> f)
    {
        for (int c = 0; c < WarmCycles; c++)
            for (int i = 0; i < Window; i++)
                _ = Realize(f, i);
    }

    private static (double NsPerRecycle, long BytesPerRecycle, long Rebuilds) TimeAndAlloc(
        ElementFactory<int> f, Func<long> readRebuilds)
    {
        long bestNs = long.MaxValue, alloc = 0, rebuilds = 0;
        for (int r = 0; r < Reps; r++)
        {
            // Stabilize the measurement window: drain pending finalizers and collect so a GC
            // triggered mid-run doesn't pollute the timed loop's ns or the alloc delta. This is
            // the same isolation the repo's PerfBench.Shared BenchRunner does before each rep.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            long rb0 = readRebuilds();
            var sw = Stopwatch.StartNew();
            for (int c = 0; c < MeasureCycles; c++)
                for (int i = 0; i < Window; i++)
                    _ = Realize(f, i);
            sw.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            long ns = (long)(sw.Elapsed.TotalMilliseconds * 1_000_000.0);
            // Keep the rebuild count from the SAME rep we keep the time/alloc from, so all three
            // reported numbers describe one representative measured pass.
            if (ns < bestNs) { bestNs = ns; alloc = a1 - a0; rebuilds = readRebuilds() - rb0; }
        }
        long total = (long)Window * MeasureCycles;
        return (bestNs / (double)total, alloc / total, rebuilds);
    }
}
