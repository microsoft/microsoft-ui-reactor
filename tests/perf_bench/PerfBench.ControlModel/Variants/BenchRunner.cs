using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;

namespace PerfBench.ControlModel.Variants;

/// <summary>
/// Runs an <see cref="IBench"/> for N iterations × R repetitions, captures
/// time + allocation + GC counts, and emits one <see cref="MeasurementResult"/>
/// per repetition.
///
/// Repetitions exist so the comparison emitter can compute mean + 95% CI
/// (3 reps minimum per spec §15.3). Iterations exist so per-op timing is
/// well above the Stopwatch resolution floor.
///
/// Phase 0 keeps this dependency-light (no BenchmarkDotNet host) — the
/// numbers are sufficient for "is the suite running?" and feed the §15.6
/// aggregator unchanged.
/// </summary>
public sealed class BenchRunner
{
    public int Iterations { get; init; } = 10_000;
    public int Repetitions { get; init; } = 5;
    public int WarmupReps { get; init; } = 2;

    public IEnumerable<MeasurementResult> Run(IBench bench, BenchVariant variant, Func<BenchContext> contextFactory)
    {
        // Warm-up phase — discarded.
        for (int w = 0; w < WarmupReps; w++)
        {
            var ctx = contextFactory();
            for (int i = 0; i < Iterations; i++)
            {
                ctx.Iteration = i;
                bench.RunOne(variant, ctx);
            }
        }

        // Timed phase.
        for (int r = 0; r < Repetitions; r++)
        {
            var ctx = contextFactory();

            // Force collections so the measurement starts from a clean baseline.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int gen0Start = GC.CollectionCount(0);
            int gen1Start = GC.CollectionCount(1);
            int gen2Start = GC.CollectionCount(2);
            long allocStart = GC.GetAllocatedBytesForCurrentThread();
            long heapStart = GC.GetTotalMemory(forceFullCollection: false);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                ctx.Iteration = i;
                bench.RunOne(variant, ctx);
            }
            sw.Stop();

            long allocEnd = GC.GetAllocatedBytesForCurrentThread();
            long heapEnd = GC.GetTotalMemory(forceFullCollection: false);
            int gen0End = GC.CollectionCount(0);
            int gen1End = GC.CollectionCount(1);
            int gen2End = GC.CollectionCount(2);

            var totalMs = sw.Elapsed.TotalMilliseconds;
            var meanNs = (totalMs * 1_000_000.0) / Iterations;
            var allocBytes = allocEnd - allocStart;

            yield return new MeasurementResult
            {
                BenchId = bench.Id,
                BenchName = bench.Name,
                Variant = variant,
                Iterations = Iterations,
                Repetition = r,
                TotalMs = totalMs,
                MeanNs = meanNs,
                AllocBytes = allocBytes,
                Gen0 = gen0End - gen0Start,
                Gen1 = gen1End - gen1Start,
                Gen2 = gen2End - gen2Start,
                HeapDeltaBytes = heapEnd - heapStart,
                Counter = (ctx.Scratch as ICounterCarrier)?.Value ?? 0,
                CounterLabel = (ctx.Scratch as ICounterCarrier)?.Label,
                MachineSku = Env.MachineSku,
                Cpu = Env.Cpu,
                OsBuild = Env.OsBuild,
                DotnetVersion = Env.DotnetVersion,
                Architecture = Env.Architecture,
                Configuration = Env.Configuration,
            };
        }
    }
}

/// <summary>
/// Optional contract for benches that want to expose a per-run counter
/// (e.g., M11's ModifierEHS allocation count, M13's callback-fire count).
/// </summary>
public interface ICounterCarrier
{
    long Value { get; }
    string Label { get; }
}
