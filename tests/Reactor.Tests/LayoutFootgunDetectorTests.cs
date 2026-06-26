using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #345 — debug-time warning when an <c>HStack</c>/<c>VStack</c> is placed in a
/// <c>Grid</c> <c>Auto</c> track with no explicit size and no explicitly-sized children
/// (the silent 0×0 collapse footgun).
///
/// <para>These tests drive <see cref="LayoutFootgunDetector.InspectGrid"/> directly against
/// the element tree — no WinUI control mount is required, so they stay in the headless unit
/// tier.</para>
/// </summary>
[Collection("LayoutFootgunDetector")]
public sealed class LayoutFootgunDetectorTests : IDisposable
{
    private readonly List<string> _warnings = new();
    private readonly bool _originalFlag;

    public LayoutFootgunDetectorTests()
    {
        // Enable the flag explicitly rather than relying on DEBUG-always-on, so the tests behave
        // identically under Release configurations.
        _originalFlag = ReactorFeatureFlags.WarnLayoutFootguns;
        ReactorFeatureFlags.WarnLayoutFootguns = true;
        LayoutFootgunDetector.ResetForTests();
        LayoutFootgunDetector.Sink = _warnings.Add;
    }

    public void Dispose()
    {
        LayoutFootgunDetector.Sink = null;
        LayoutFootgunDetector.ResetForTests();
        ReactorFeatureFlags.WarnLayoutFootguns = _originalFlag;
    }

    // ── Should warn ────────────────────────────────────────────────────────

    [Fact]
    public void BareHStack_InAutoColumn_NoExplicitSize_Warns()
    {
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        var msg = Assert.Single(_warnings);
        Assert.Contains("HStack", msg);
        Assert.Contains("column 0 (Auto)", msg);
    }

    [Fact]
    public void BorderWrappedHStack_InAutoColumn_StillWarns()
    {
        // Wrapping in a Border does NOT fix the collapse (the Border sizes to its 0-sized child).
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            Border(HStack(TextBlock("A"), TextBlock("B"))).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        var msg = Assert.Single(_warnings);
        Assert.Contains("HStack", msg);
    }

    [Fact]
    public void VStack_InAutoRow_NoExplicitSize_Warns()
    {
        var grid = Grid(
            columns: new[] { GridSize.Star() },
            rows: new[] { GridSize.Star(), GridSize.Auto },
            VStack(TextBlock("A"), TextBlock("B")).Grid(row: 1, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        var msg = Assert.Single(_warnings);
        Assert.Contains("VStack", msg);
        Assert.Contains("row 1 (Auto)", msg);
    }

    // ── Should NOT warn ────────────────────────────────────────────────────

    [Fact]
    public void HStack_InStarColumn_DoesNotWarn()
    {
        var grid = Grid(
            columns: new[] { GridSize.Star(), GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void HStack_InAutoColumn_WithExplicitWidth_DoesNotWarn()
    {
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).Width(200).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void HStack_InAutoColumn_WithExplicitlySizedChild_DoesNotWarn()
    {
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A").Width(120), TextBlock("B")).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void HStack_InAutoColumn_WithMinWidth_DoesNotWarn()
    {
        // MinWidth clamps the Measure pass, so the stack cannot collapse to 0 — no warning.
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).MinWidth(80).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void HStack_InAutoColumn_WithChildMinWidth_DoesNotWarn()
    {
        // A child's MinWidth also prevents the stretch→0 desired-size case during Measure.
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A").MinWidth(40), TextBlock("B")).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void VStack_InAutoRow_WithMinHeight_DoesNotWarn()
    {
        var grid = Grid(
            columns: new[] { GridSize.Star() },
            rows: new[] { GridSize.Star(), GridSize.Auto },
            VStack(TextBlock("A"), TextBlock("B")).MinHeight(50).Grid(row: 1, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void VStack_InAutoRow_WithExplicitHeight_DoesNotWarn()
    {
        var grid = Grid(
            columns: new[] { GridSize.Star() },
            rows: new[] { GridSize.Star(), GridSize.Auto },
            VStack(TextBlock("A"), TextBlock("B")).Height(120).Grid(row: 1, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void VStack_InAutoRow_WithExplicitlySizedChild_DoesNotWarn()
    {
        // Mirrors the horizontal child-Width case: a child's explicit Height keeps the column from
        // collapsing, so AnyChildHasExplicitHeight must suppress the warning.
        var grid = Grid(
            columns: new[] { GridSize.Star() },
            rows: new[] { GridSize.Star(), GridSize.Auto },
            VStack(TextBlock("A").Height(60), TextBlock("B")).Grid(row: 1, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void VStack_InAutoRow_WithChildMinHeight_DoesNotWarn()
    {
        // A child's MinHeight clamps the Measure pass too — mirrors the horizontal child-MinWidth case.
        var grid = Grid(
            columns: new[] { GridSize.Star() },
            rows: new[] { GridSize.Star(), GridSize.Auto },
            VStack(TextBlock("A").MinHeight(40), TextBlock("B")).Grid(row: 1, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void BorderWrappedHStack_BorderHasExplicitWidth_DoesNotWarn()
    {
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            Border(HStack(TextBlock("A"), TextBlock("B"))).Width(200).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void HStack_InAutoRow_StarColumn_DoesNotWarn()
    {
        // A horizontal stack only collapses on its main (horizontal) axis. An Auto *row*
        // with a Star column does not trigger the HStack width-collapse footgun.
        var grid = Grid(
            columns: new[] { GridSize.Star() },
            rows: new[] { GridSize.Auto },
            HStack(TextBlock("A"), TextBlock("B")).Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void EmptyHStack_InAutoColumn_DoesNotWarn()
    {
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack().Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    [Fact]
    public void NonStackChild_InAutoColumn_DoesNotWarn()
    {
        // A non-stack element (here a bare TextBlock) placed directly in an Auto track is not the
        // footgun the detector models — InspectGrid must leave it alone (the "not a stack we model"
        // branch), so a regression that warned on every TextBlock/Button in an Auto track is caught.
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            TextBlock("A").Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Empty(_warnings);
    }

    // ── Emit-once ──────────────────────────────────────────────────────────

    [Fact]
    public void SameOffendingPlacement_WarnsOnlyOnce_AcrossRenders()
    {
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).Grid(row: 0, column: 0));

        // Simulate two render/mount passes of the same logical placement.
        LayoutFootgunDetector.InspectGrid(grid);
        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Single(_warnings);
    }

    // ── Update path (dynamic / state-driven placements) ────────────────────

    [Fact]
    public void HStack_StarColumn_ThenFlippedToAuto_WarnsAfterFlip()
    {
        // Models a state-driven `columns:` change on an already-mounted Grid: the first render is
        // safe (Star), a later render flips the track to Auto. The runtime warning's whole point
        // over a static analyzer is catching this dynamic case — the detector must fire on the
        // second pass. Goes through Inspect() so the DEBUG/flag gate + `is GridElement` filter run.
        var keyedHStack = HStack(TextBlock("A"), TextBlock("B")).WithKey("toolbar").Grid(row: 0, column: 0);

        var safe = Grid(
            columns: new[] { GridSize.Star(), GridSize.Star() },
            rows: new[] { GridSize.Star() },
            keyedHStack);
        LayoutFootgunDetector.Inspect(safe);
        Assert.Empty(_warnings);

        var collapsing = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            keyedHStack);
        LayoutFootgunDetector.Inspect(collapsing);

        var msg = Assert.Single(_warnings);
        Assert.Contains("column 0 (Auto)", msg);
    }

    [Fact]
    public void HStack_ExplicitWidth_ThenWidthRemoved_WarnsAfterRemoval()
    {
        // Models the explicit size being dropped on a re-render (e.g. a conditional .Width()).
        var sized = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).WithKey("toolbar").Width(200).Grid(row: 0, column: 0));
        LayoutFootgunDetector.Inspect(sized);
        Assert.Empty(_warnings);

        var unsized = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).WithKey("toolbar").Grid(row: 0, column: 0));
        LayoutFootgunDetector.Inspect(unsized);

        Assert.Single(_warnings);
    }

    // ── Dedup keyed on identity, not message (L1) ──────────────────────────

    [Fact]
    public void TwoDistinctOffenders_SameStackTypeAndTrackIndex_BothWarn()
    {
        // Two different HStacks both land in column 0 (Auto) but at different rows. They are
        // distinct offending placements and must each warn — dedup keys on element identity /
        // grid position, not on the (identical) message text.
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star(), GridSize.Star() },
            HStack(TextBlock("A")).Grid(row: 0, column: 0),
            HStack(TextBlock("B")).Grid(row: 1, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Equal(2, _warnings.Count);
    }

    [Fact]
    public void TwoOffenders_ReusingSameKeyAtDifferentPlacements_BothWarn()
    {
        // Element.Key uniqueness is only guaranteed among siblings, so the same key can legitimately
        // appear at distinct grid placements. Each is a distinct offender and must warn — the dedup
        // key folds in the grid placement, not just the author key.
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star(), GridSize.Star() },
            HStack(TextBlock("A")).WithKey("row").Grid(row: 0, column: 0),
            HStack(TextBlock("B")).WithKey("row").Grid(row: 1, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Equal(2, _warnings.Count);
    }

    [Fact]
    public void TwoKeyedOffenders_SameCell_DifferentKeys_BothWarn()
    {
        // Two HStacks in the SAME Auto cell (row 0, column 0) with different keys. They share stack
        // type and placement, so without the author-key discriminator in the dedup key they would
        // collapse to one warning — this pins that `.WithKey(...)` keeps distinct offenders distinct.
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A")).WithKey("first").Grid(row: 0, column: 0),
            HStack(TextBlock("B")).WithKey("second").Grid(row: 0, column: 0));

        LayoutFootgunDetector.InspectGrid(grid);

        Assert.Equal(2, _warnings.Count);
    }

    [Fact]
    public void Inspect_NonGridElement_DoesNotWarn()
    {
        // The Reconciler calls Inspect() on every mounted/updated element; only GridElement should
        // be considered. A bare HStack (not inside a Grid) must be ignored by the `is GridElement`
        // filter even though it is a stack.
        LayoutFootgunDetector.Inspect(HStack(TextBlock("A"), TextBlock("B")));

        Assert.Empty(_warnings);
    }

    [Fact]
    public void Inspect_OffendingGridElement_RoutesThroughGateAndSink()
    {
        // Exercises the exact entry point the Reconciler Mount/Update tail calls: the DEBUG/flag
        // gate is satisfied (tests run under DEBUG), the `is GridElement` filter passes, and the
        // warning reaches the Sink — pinning the hook wiring, not just InspectGrid() detection.
        var grid = Grid(
            columns: new[] { GridSize.Auto, GridSize.Star() },
            rows: new[] { GridSize.Star() },
            HStack(TextBlock("A"), TextBlock("B")).Grid(row: 0, column: 0));

        LayoutFootgunDetector.Inspect(grid);

        Assert.Single(_warnings);
    }
}
