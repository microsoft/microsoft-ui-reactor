// Spec 048 §9 / §13 Phase 2 — standalone Reg<>.Done static-field-read bench.
//
// Models the per-factory cost the Phase 3 migration will add: one read of
// `static readonly byte Done = Init();` on a closed generic type.
//
// Run:  dotnet run -c Release --project tests/aot_trim_proof/RegStaticReadBench
//
// Output (one row per measurement): mean ns/op, total ms, gen0/1/2 counts,
// alloc bytes. Compare REG_READ rows vs EMPTY_LOOP control.

using System.Diagnostics;
using System.Runtime.CompilerServices;

const int Iterations  = 100_000_000;
const int Repetitions = 7;
const int WarmupReps  = 3;

// ─── Force JIT on the closed generic Reg<> by touching it once during warm-up.
// `MethodImplOptions.NoInlining` on RegRead prevents the JIT from constant-
// folding the field read into a literal — we want the real load instruction.

Console.WriteLine($".NET runtime: {Environment.Version} | OSArch: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} | ProcArch: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"Configuration: {(IsDebug() ? "DEBUG" : "RELEASE")} | TieredCompilation: {(AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out var t) ? t.ToString() : "default(on)")}");
Console.WriteLine($"Iterations per rep: {Iterations:N0} | Repetitions: {Repetitions} (after {WarmupReps} warm-up reps)");
Console.WriteLine();
Console.WriteLine($"{"Bench",-22} | {"Mean ns/op",12} | {"Total ms",10} | {"Alloc bytes",12} | {"Gen0",6} | {"Gen1",6} | {"Gen2",6}");
Console.WriteLine(new string('-', 96));

// EMPTY_LOOP — the control. Measures empty-loop + Stopwatch + GC overhead.
Measure("EMPTY_LOOP", static () =>
{
    int acc = 0;
    for (int i = 0; i < Iterations; i++)
    {
        acc = unchecked(acc + i);
    }
    return acc;
});

// REG_READ_SINGLE — read the same closed Reg<>.Done in a tight loop.
// On the JIT's elided-cctor fast path this should compile to a single
// indirect load (or be folded into the loop if the JIT proves the read
// has no observable effect — we add it into `acc` to defeat that).
Measure("REG_READ_SINGLE", static () =>
{
    int acc = 0;
    for (int i = 0; i < Iterations; i++)
    {
        acc = unchecked(acc + RegRead<A1, B1, H1>());
    }
    return acc;
});

// REG_READ_MIXED4 — alternate across 4 distinct closed generics so the
// JIT can't fuse the loads. Models a render pass that touches 4 different
// factories (Button + TextBlock + VStack + StackPanel for example).
Measure("REG_READ_MIXED4", static () =>
{
    int acc = 0;
    for (int i = 0; i < Iterations; i++)
    {
        acc = unchecked(acc
            + RegRead<A1, B1, H1>()
            + RegRead<A2, B2, H2>()
            + RegRead<A3, B3, H3>()
            + RegRead<A4, B4, H4>());
    }
    return acc;
});

// REG_READ_SINGLE_INLINE — touches Reg<>.Done directly inside the loop
// (no NoInlining wrapper). The JIT is free to hoist the load out of the
// loop body since `Done` is `static readonly` — this measures the
// "branch disappears" lower bound the spec §9 predicts.
Measure("REG_READ_SINGLE_INLINE", static () =>
{
    int acc = 0;
    for (int i = 0; i < Iterations; i++)
    {
        acc = unchecked(acc + Reg<A1, B1, H1>.Done);
    }
    return acc;
});

// REG_READ_MIXED4_INLINE — same as MIXED4 but inline. Lower bound for
// the multi-factory render-pass scenario.
Measure("REG_READ_MIXED4_INLINE", static () =>
{
    int acc = 0;
    for (int i = 0; i < Iterations; i++)
    {
        acc = unchecked(acc
            + Reg<A1, B1, H1>.Done
            + Reg<A2, B2, H2>.Done
            + Reg<A3, B3, H3>.Done
            + Reg<A4, B4, H4>.Done);
    }
    return acc;
});

Console.WriteLine();
Console.WriteLine("Interpretation: subtract EMPTY_LOOP mean from REG_READ_SINGLE mean →");
Console.WriteLine("per-call cost of one `Reg<E,C,H>.Done` indirect load on this machine.");
Console.WriteLine("REG_READ_MIXED4 mean ÷ 4 should be within noise of REG_READ_SINGLE.");

return 0;

// ─── Bench harness ─────────────────────────────────────────────────────────

static bool IsDebug()
{
#if DEBUG
    return true;
#else
    return false;
#endif
}

static void Measure(string name, Func<int> body)
{
    // Warm-up.
    for (int w = 0; w < WarmupReps; w++)
    {
        _ = body();
    }

    double sumNs = 0;
    double sumMs = 0;
    long sumAlloc = 0;
    long sumGen0 = 0, sumGen1 = 0, sumGen2 = 0;

    for (int r = 0; r < Repetitions; r++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocStart = GC.GetAllocatedBytesForCurrentThread();
        int gen0Start = GC.CollectionCount(0);
        int gen1Start = GC.CollectionCount(1);
        int gen2Start = GC.CollectionCount(2);

        var sw = Stopwatch.StartNew();
        int acc = body();
        sw.Stop();

        // Defeat any "result is unused" optimization. The Volatile.Read keeps
        // the accumulator observable from outside the JIT's view.
        System.Threading.Volatile.Write(ref Sink._value, acc);

        long allocEnd = GC.GetAllocatedBytesForCurrentThread();
        int gen0End = GC.CollectionCount(0);
        int gen1End = GC.CollectionCount(1);
        int gen2End = GC.CollectionCount(2);

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double meanNs = (totalMs * 1_000_000.0) / Iterations;

        sumNs += meanNs;
        sumMs += totalMs;
        sumAlloc += (allocEnd - allocStart);
        sumGen0 += (gen0End - gen0Start);
        sumGen1 += (gen1End - gen1Start);
        sumGen2 += (gen2End - gen2Start);
    }

    Console.WriteLine($"{name,-22} | {sumNs / Repetitions,12:F4} | {sumMs / Repetitions,10:F2} | {sumAlloc / Repetitions,12:N0} | {sumGen0 / Repetitions,6} | {sumGen1 / Repetitions,6} | {sumGen2 / Repetitions,6}");
}

[MethodImpl(MethodImplOptions.NoInlining)]
static byte RegRead<TElement, TControl, THandler>()
    where TElement : new()
    where TControl : new()
    where THandler : new()
    => Reg<TElement, TControl, THandler>.Done;

// Global sink to defeat dead-store elimination. Field is written via
// Volatile.Write so the JIT cannot prove the accumulator dead.
internal static class Sink
{
    internal static int _value;
}

// ─── The Reg<> shape under measurement ─────────────────────────────────────

// Mirrors the spec §7 shape exactly — the explicit (empty) static
// constructor disables the C# compiler's `beforefieldinit` flag and binds
// initialization to "precise before-first-use" semantics (ECMA-335
// §I.8.9.5), so Init() runs on the first read of Done and not earlier.
// The runtime elides the cctor check on subsequent reads of any closed
// generic whose cctor has already run, so steady-state cost is one
// indirect load.
internal static class Reg<TElement, TControl, THandler>
    where TElement : new()
    where TControl : new()
    where THandler : new()
{
    static Reg() { }

    internal static readonly byte Done = Init();

    private static byte Init()
    {
        // The real Reg<> calls ControlRegistry.Register(...) and returns 1
        // (the non-zero sentinel asserted by RegTests). For the bench we
        // just return 1 to match — the cost of the static-field READ is
        // what we measure, not the one-shot Init.
        return 1;
    }
}

// Distinct element/control/handler placeholders so the bench gets four
// distinct closed-generic Reg<> instantiations.
internal sealed class A1 { }
internal sealed class A2 { }
internal sealed class A3 { }
internal sealed class A4 { }

internal sealed class B1 { }
internal sealed class B2 { }
internal sealed class B3 { }
internal sealed class B4 { }

internal sealed class H1 { }
internal sealed class H2 { }
internal sealed class H3 { }
internal sealed class H4 { }
