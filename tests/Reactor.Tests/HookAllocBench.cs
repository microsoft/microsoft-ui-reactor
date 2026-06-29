using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Headless allocation harness for issue #659 (hook delegate caching). Measures
/// steady-state bytes allocated per render cycle for each hook scenario by
/// wrapping N render passes in <see cref="GC.GetAllocatedBytesForCurrentThread"/>.
///
/// MEASURE-FIRST discipline: capture a BEFORE baseline, implement a finding,
/// re-measure. The "deps-unchanged" scenarios are the headline — that is the
/// per-render steady state on the StocksGrid hot path where waste concentrates.
///
/// Gated behind the REACTOR_ALLOC_BENCH env var so it is a ~instant no-op in CI;
/// run locally with:
///   $env:REACTOR_ALLOC_BENCH=1; dotnet test tests/Reactor.Tests `
///     --filter FullyQualifiedName~HookAllocBench -l "console;verbosity=detailed"
/// </summary>
public class HookAllocBench
{
    private const int Iterations = 200_000;
    private readonly ITestOutputHelper _out;

    public HookAllocBench(ITestOutputHelper outHelper) => _out = outHelper;

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("REACTOR_ALLOC_BENCH") == "1";

    private record Cell(string Content) : Element;

    /// <summary>
    /// Runs <paramref name="renderOnce"/> for <see cref="Iterations"/> steady-state
    /// passes (after a warmup that allocates the hook cells once) and reports
    /// bytes/render. The <paramref name="ctx"/> is reused so hook cells persist
    /// across passes — exactly the per-render steady state we care about.
    /// </summary>
    private void Measure(string name, Action<RenderContext> renderOnce)
    {
        var ctx = new RenderContext();

        // Warmup: allocate hook cells, JIT the paths, settle.
        for (int i = 0; i < 1000; i++)
        {
            ctx.BeginRender(static () => { });
            renderOnce(ctx);
            ctx.FlushEffects();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            ctx.BeginRender(static () => { });
            renderOnce(ctx);
            ctx.FlushEffects();
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        double perRender = (after - before) / (double)Iterations;
        _out.WriteLine($"{name,-34} {perRender,9:F2} bytes/render  ({(after - before):N0} over {Iterations:N0})");
    }

    [Fact]
    public void Baselines()
    {
        if (!Enabled)
        {
            _out.WriteLine("HookAllocBench skipped (set REACTOR_ALLOC_BENCH=1 to run).");
            return;
        }

        _out.WriteLine($"=== Hook alloc baselines ({Iterations:N0} steady-state renders each) ===");

        // --- UseState (deps-free; setter closure is the suspect) -------------
        Measure("UseState x1", static ctx =>
        {
            var (v, set) = ctx.UseState(0);
        });
        Measure("UseState x4", static ctx =>
        {
            var (a, sa) = ctx.UseState(0);
            var (b, sb) = ctx.UseState(0);
            var (c, sc) = ctx.UseState(0);
            var (d, sd) = ctx.UseState(0);
        });

        // --- UseReducer (updater + dispatch closures) ------------------------
        Measure("UseReducer<T>", static ctx =>
        {
            var (v, update) = ctx.UseReducer(0);
        });
        Measure("UseReducer<S,A>", static ctx =>
        {
            var (v, dispatch) = ctx.UseReducer<int, int>(static (s, a) => s + a, 0);
        });

        // --- UseEffect, deps UNCHANGED (params array + box on skip path) -----
        Measure("UseEffect deps-unchanged", static ctx =>
        {
            ctx.UseEffect(static () => { }, 42, "k");
        });

        // --- UseMemo / UseCallback, deps UNCHANGED ---------------------------
        Measure("UseMemo deps-unchanged", static ctx =>
        {
            var v = ctx.UseMemo(static () => 5, 42, "k");
        });
        Measure("UseCallback deps-unchanged", static ctx =>
        {
            var cb = ctx.UseCallback(static () => { }, 42, "k");
        });

        // --- UseCommand (sync passthrough — no wrapping needed) --------------
        var syncCmd = new Command { Label = "bench", Execute = static () => { } };
        Measure("UseCommand sync-passthrough", ctx =>
        {
            var c = ctx.UseCommand(syncCmd);
        });

        // --- UsePersisted (setter closure) -----------------------------------
        Measure("UsePersisted", static ctx =>
        {
            var (v, set) = ctx.UsePersisted("bench-key", 0);
        });

        // --- UseMemoCells full cache-hit (snapshots + state record) ----------
        // Reference-type items (matches the real StocksGrid row model — value
        // types would box in the positional equality scan, which is an artifact
        // of the micro, not the hot path).
        var rows = new[]
        {
            new Cell("a"), new Cell("b"), new Cell("c"), new Cell("d"),
            new Cell("e"), new Cell("f"), new Cell("g"), new Cell("h"),
        };
        Measure("UseMemoCells cache-hit (ref)", ctx =>
        {
            var children = ctx.UseMemoCells<Cell>(rows, static (it, i) => it, "deps");
        });
        var ints = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        Measure("UseMemoCells cache-hit (int)", ctx =>
        {
            var children = ctx.UseMemoCells<int>(ints, static (it, i) => new Cell($"v={it}"), "deps");
        });

        // --- A realistic per-cell mix (state + memo + effect, all unchanged) -
        Measure("MIX cell (state+memo+effect)", static ctx =>
        {
            var (sel, setSel) = ctx.UseState(false);
            var color = ctx.UseMemo(static () => 1, "theme");
            ctx.UseEffect(static () => { }, "mountonce");
        });
    }
}
