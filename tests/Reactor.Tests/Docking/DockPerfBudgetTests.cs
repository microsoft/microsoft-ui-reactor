using System.Diagnostics;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Docking.Persistence;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 §2.20 — pure-function perf budgets. These tests use the
/// same allocation-counter / median-of-N pattern as spec 034 (allocation)
/// and spec 031 (frame-aligned). The xUnit thresholds are wider than
/// the spec budgets to absorb CI jitter; the spec budget is the
/// engineering target enforced via local + showcase observation.
/// </summary>
public sealed class DockPerfBudgetTests
{
    // ── Layout JSON load budget (spec §8.1 — 50 ms for 200 panes) ──────

    /// <summary>
    /// Spec §2.20 / §8.1: 200-pane layout JSON must load in ≤ 50 ms.
    /// We run 10 iterations (post a 3-iteration warm-up) and assert the
    /// *median* duration falls under a generous CI-jitter ceiling. The
    /// per-iteration assertion would be flaky on shared CI; median
    /// catches an O(n²) regression while tolerating one slow run.
    /// </summary>
    [Fact]
    public void LayoutLoad_TwoHundredPanes_MedianUnderCiCeiling()
    {
        var panes = new List<DockableContent>(200);
        for (int i = 0; i < 200; i++)
            panes.Add(new Document { Title = $"P{i}", Key = $"p{i}" });
        var json = DockLayoutSerializer.Save(new DockTabGroup(panes));

        // Warm-up — JIT + JsonSerializerContext source-gen path.
        for (int i = 0; i < 3; i++)
            _ = DockLayoutSerializer.Load(json);

        var samples = new long[10];
        for (int i = 0; i < samples.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = DockLayoutSerializer.Load(json);
            sw.Stop();
            samples[i] = sw.ElapsedMilliseconds;
            Assert.True(result.Success);
        }

        Array.Sort(samples);
        var median = samples[samples.Length / 2];

        // Spec budget: 50 ms. CI ceiling: 200 ms (4× the budget to
        // absorb shared-runner jitter without masking real regressions —
        // a true O(n²) explosion runs into seconds at this size).
        Assert.True(median < 200,
            $"200-pane load median = {median}ms (spec budget 50ms; CI ceiling 200ms). Samples: [{string.Join(",", samples)}]");
    }

    // ── Hover-state hit-test allocation budget (spec §2.20 / §8.5) ─────

    /// <summary>
    /// Spec §2.20 — drop-target hit-test is zero-allocation on the
    /// hot path. The control's <c>HitTestForTarget</c> is the function
    /// the overlay calls per pointer-move; it must run alloc-free so
    /// pointer-move latency stays under 2 ms.
    /// </summary>
    [Fact]
    public void DropTargetHitTest_HotPath_ZeroAlloc()
    {
        // Warm-up the JIT.
        for (int i = 0; i < 1000; i++)
            _ = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.Center, 800, 600);

        var before = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 100_000;
        for (int i = 0; i < iterations; i++)
        {
            // ComputePreviewBounds is the pure geometry hook the overlay
            // calls per hovered target. It must produce no allocations —
            // returns a Rect struct, no captured closures, no LINQ.
            _ = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitLeft, 800, 600);
            _ = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockBottom, 800, 600);
            _ = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.Center, 800, 600);
        }
        var delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // 1 byte per iteration cap — handles transient bookkeeping
        // (boxed enum in EventListener admin, etc.) but catches any
        // routine allocation (a captured-closure delegate is ~64 B
        // each, would blow far past).
        Assert.True(delta <= iterations,
            $"ComputePreviewBoundsForTest leaked {delta}B over {iterations} hot-path calls (cap: {iterations}).");
    }

    // ── Reconciler diff perf — measured against the mutator hot path ───

    /// <summary>
    /// Spec §2.20: a 50-pane shape change applied via
    /// <see cref="DockLayoutMutator"/> must finish in ≤ 1 ms. The
    /// mutator is the pure-function side of the reconciler diff — same
    /// algorithmic shape (visits each node once, builds a new tree).
    /// </summary>
    [Fact]
    public void Mutator_FiftyPaneShapeChange_MedianUnderCiCeiling()
    {
        var panes = new List<DockableContent>(50);
        for (int i = 0; i < 50; i++)
            panes.Add(new Document { Title = $"P{i}", Key = $"p{i}" });
        var layout = new DockTabGroup(panes);

        // Warm-up.
        for (int i = 0; i < 5; i++)
            _ = DockLayoutMutator.RemovePane(layout, panes[i]);

        var samples = new long[20];
        for (int i = 0; i < samples.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            // Worst-case for the diff path: remove + reinsert; tests
            // both the walk-and-rebuild and the insertion paths.
            var (after, _) = DockLayoutMutator.RemovePane(layout, panes[i % panes.Count]);
            _ = DockLayoutMutator.InsertPaneAtTarget(after ?? layout, panes[(i + 1) % panes.Count], DockTarget.Center);
            sw.Stop();
            samples[i] = sw.ElapsedMilliseconds;
        }

        Array.Sort(samples);
        var median = samples[samples.Length / 2];

        // Spec budget: 1 ms. CI ceiling: 25 ms — generous to absorb
        // GC tick mid-run + the OS scheduler. A real algorithmic
        // regression (O(n²) or unbounded recursion) would run into
        // hundreds of milliseconds at this size.
        Assert.True(median <= 25,
            $"50-pane mutator median = {median}ms (spec budget 1ms; CI ceiling 25ms). Samples: [{string.Join(",", samples)}]");
    }
}
