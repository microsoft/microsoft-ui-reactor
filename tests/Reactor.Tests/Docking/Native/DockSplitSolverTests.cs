using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Unit coverage for the pure split-constraint solver
/// (<see cref="DockSplitSolver"/>) — spec 045 §2.1.
///
/// These tests don't need a UI thread or the reconciler; they exercise the
/// ratio-clamping math directly. Translates the cases listed in §2.1:
///   • min &lt; proposed &lt; max clamp
///   • ratio persistence (round-trip a delta sequence; cumulative drift)
///   • multi-child layouts (three+ children, no leak into untouched panes)
///   • equal-share fallback when all ratios are zero
///   • normalize-from-persisted-JSON path
/// </summary>
public class DockSplitSolverTests
{
    private static DockSplitChild C(double ratio, double min = 0, double max = double.PositiveInfinity)
        => new(ratio, min, max);

    [Fact]
    public void EqualShare_TwoChildren_HalfEach()
    {
        var r = DockSplitSolver.EqualShare(2);
        Assert.Equal(2, r.Length);
        Assert.Equal(0.5, r[0]);
        Assert.Equal(0.5, r[1]);
    }

    [Fact]
    public void EqualShare_FiveChildren_FifthEach()
    {
        var r = DockSplitSolver.EqualShare(5);
        Assert.Equal(5, r.Length);
        foreach (var v in r) Assert.Equal(0.2, v, 10);
        Assert.Equal(1.0, r.Sum(), 9);
    }

    [Fact]
    public void Normalize_AllZero_BecomesEqualShare()
    {
        var r = DockSplitSolver.Normalize(new[] { 0.0, 0.0, 0.0 });
        Assert.Equal(3, r.Length);
        foreach (var v in r) Assert.Equal(1.0 / 3.0, v, 10);
    }

    [Fact]
    public void Normalize_NegativesAreClampedToZero()
    {
        var r = DockSplitSolver.Normalize(new[] { -2.0, 1.0, 1.0 });
        Assert.Equal(0.0, r[0]);
        Assert.Equal(0.5, r[1]);
        Assert.Equal(0.5, r[2]);
    }

    [Fact]
    public void Normalize_DriftedRatios_NormalizeToOne()
    {
        var r = DockSplitSolver.Normalize(new[] { 0.4999, 0.5002, 0.0 });
        Assert.Equal(1.0, r.Sum(), 12);
    }

    [Fact]
    public void ApplyDelta_PositiveDelta_GrowsTrailingShrinksLeading()
    {
        // Two children at 60/40 of 1000 DIPs → 600/400.
        // Apply +50 DIPs → 550/450 → ratios 0.55/0.45.
        var children = new[] { C(0.6), C(0.4) };
        var sol = DockSplitSolver.ApplyDelta(children, splitterIndex: 0, deltaDip: 50, totalDip: 1000);
        Assert.Equal(0.55, sol.Ratios[0], 6);
        Assert.Equal(0.45, sol.Ratios[1], 6);
        Assert.Equal(1.0, sol.Ratios.Sum(), 9);
    }

    [Fact]
    public void ApplyDelta_NegativeDelta_GrowsLeadingShrinksTrailing()
    {
        var children = new[] { C(0.5), C(0.5) };
        var sol = DockSplitSolver.ApplyDelta(children, 0, deltaDip: -100, totalDip: 1000);
        // 500/500 → 600/400.
        Assert.Equal(0.6, sol.Ratios[0], 6);
        Assert.Equal(0.4, sol.Ratios[1], 6);
    }

    [Fact]
    public void ApplyDelta_MinClamp_StopsShrinkingLeading()
    {
        // Leading min = 200 DIPs of 1000. At 0.6/0.4 (600/400), apply +500 should
        // clamp leading at 200 (drop 400 DIPs); trailing absorbs the rest.
        var children = new[] { C(0.6, min: 200), C(0.4) };
        var sol = DockSplitSolver.ApplyDelta(children, 0, deltaDip: 500, totalDip: 1000);
        // leading clamped to 200/1000 = 0.2.
        Assert.Equal(0.2, sol.Ratios[0], 6);
        Assert.Equal(0.8, sol.Ratios[1], 6);
    }

    [Fact]
    public void ApplyDelta_MaxClamp_StopsGrowingTrailing()
    {
        // Trailing max = 600 of 1000. At 0.5/0.5 (500/500), apply +200 should
        // try (300/700), clamp trailing to 600, push leading back to 400.
        var children = new[] { C(0.5), C(0.5, max: 600) };
        var sol = DockSplitSolver.ApplyDelta(children, 0, deltaDip: 200, totalDip: 1000);
        Assert.Equal(0.4, sol.Ratios[0], 6);
        Assert.Equal(0.6, sol.Ratios[1], 6);
    }

    [Fact]
    public void ApplyDelta_ThreeChildren_OnlyTouchesPair()
    {
        // Three columns at 1/3 each. Move the splitter between 0 and 1 by +100.
        // Index 2 (untouched) keeps its 1/3.
        var children = new[] { C(1.0 / 3.0), C(1.0 / 3.0), C(1.0 / 3.0) };
        var sol = DockSplitSolver.ApplyDelta(children, splitterIndex: 0, deltaDip: 100, totalDip: 900);
        // 300/300/300 → 200/400/300.
        Assert.Equal(200.0 / 900.0, sol.Ratios[0], 6);
        Assert.Equal(400.0 / 900.0, sol.Ratios[1], 6);
        Assert.Equal(300.0 / 900.0, sol.Ratios[2], 6);
        Assert.Equal(1.0, sol.Ratios.Sum(), 9);
    }

    [Fact]
    public void ApplyDelta_SecondSplitterInThreeChildren_MovesTrailingPair()
    {
        var children = new[] { C(0.5), C(0.25), C(0.25) };
        var sol = DockSplitSolver.ApplyDelta(children, splitterIndex: 1, deltaDip: -50, totalDip: 1000);
        // 500/250/250 → 500/300/200.
        Assert.Equal(0.5, sol.Ratios[0], 6);
        Assert.Equal(0.30, sol.Ratios[1], 6);
        Assert.Equal(0.20, sol.Ratios[2], 6);
    }

    [Fact]
    public void ApplyDelta_RoundTrip_RatiosPersistAcrossMultipleDeltas()
    {
        var children = new[] { C(0.5), C(0.5) };
        var ratios = new double[] { 0.5, 0.5 };

        // Push back and forth — ratios should return to where they started
        // (within FP tolerance).
        for (int i = 0; i < 50; i++)
        {
            ratios = DockSplitSolver.ApplyDelta(
                children: new[] { C(ratios[0]), C(ratios[1]) },
                splitterIndex: 0,
                deltaDip: 5,
                totalDip: 1000).Ratios;
        }
        for (int i = 0; i < 50; i++)
        {
            ratios = DockSplitSolver.ApplyDelta(
                children: new[] { C(ratios[0]), C(ratios[1]) },
                splitterIndex: 0,
                deltaDip: -5,
                totalDip: 1000).Ratios;
        }

        Assert.Equal(0.5, ratios[0], 4);
        Assert.Equal(0.5, ratios[1], 4);
        Assert.Equal(1.0, ratios.Sum(), 9);
    }

    [Fact]
    public void ApplyDelta_InvalidSplitterIndex_Throws()
    {
        var children = new[] { C(0.5), C(0.5) };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DockSplitSolver.ApplyDelta(children, splitterIndex: 1, deltaDip: 0, totalDip: 1000));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DockSplitSolver.ApplyDelta(children, splitterIndex: -1, deltaDip: 0, totalDip: 1000));
    }

    [Fact]
    public void ApplyDelta_SingleChild_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => DockSplitSolver.ApplyDelta(new[] { C(1.0) }, 0, 0, 100));
    }

    [Fact]
    public void ApplyDelta_NonPositiveTotal_Throws()
    {
        var children = new[] { C(0.5), C(0.5) };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DockSplitSolver.ApplyDelta(children, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DockSplitSolver.ApplyDelta(children, 0, 0, -1));
    }

    [Fact]
    public void ApplyDelta_ZeroDelta_PreservesRatios()
    {
        var children = new[] { C(0.42), C(0.58) };
        var sol = DockSplitSolver.ApplyDelta(children, 0, 0, 800);
        Assert.Equal(0.42, sol.Ratios[0], 6);
        Assert.Equal(0.58, sol.Ratios[1], 6);
    }

    [Fact]
    public void ApplyDelta_DeltaLargerThanLeading_ClampsAtMinZero()
    {
        // No explicit min — defaults to 0 — so leading collapses entirely.
        var children = new[] { C(0.3), C(0.7) };
        var sol = DockSplitSolver.ApplyDelta(children, 0, deltaDip: 10000, totalDip: 1000);
        Assert.Equal(0.0, sol.Ratios[0], 6);
        Assert.Equal(1.0, sol.Ratios[1], 6);
    }
}
