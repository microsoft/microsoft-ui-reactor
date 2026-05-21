using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Windows.System;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Headless coverage for the §2.3 drop-target overlay's pure math:
/// preview-rectangle bounds for every target enum value, arrow-key
/// navigation graph, and per-target localized AT names.
///
/// The WinUI <c>DockDropTargetOverlayControl</c> itself can't be
/// instantiated without a XamlRoot — its visual-tree-affecting code lives
/// behind <c>ApplyTemplate</c>; mount/hover behavior is verified in the
/// self-host smoke fixture (NativeDocking_DropTargetOverlayShowsAndDismisses).
/// </summary>
public class DockDropTargetOverlayTests
{
    private const double HostW = 800;
    private const double HostH = 600;

    // ── Preview rectangle bounds per target ─────────────────────────────

    [Fact]
    public void PreviewBounds_Center_CoversFullHost()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.Center, HostW, HostH);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(HostW, r.Width);
        Assert.Equal(HostH, r.Height);
    }

    [Fact]
    public void PreviewBounds_SplitLeft_LeadingHalf()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitLeft, HostW, HostH);
        Assert.Equal(0, r.X);
        Assert.Equal(HostW / 2, r.Width);
        Assert.Equal(HostH, r.Height);
    }

    [Fact]
    public void PreviewBounds_SplitRight_TrailingHalf()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitRight, HostW, HostH);
        Assert.Equal(HostW / 2, r.X);
        Assert.Equal(HostW / 2, r.Width);
    }

    [Fact]
    public void PreviewBounds_SplitTop_UpperHalf()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitTop, HostW, HostH);
        Assert.Equal(0, r.Y);
        Assert.Equal(HostH / 2, r.Height);
    }

    [Fact]
    public void PreviewBounds_SplitBottom_LowerHalf()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitBottom, HostW, HostH);
        Assert.Equal(HostH / 2, r.Y);
        Assert.Equal(HostH / 2, r.Height);
    }

    [Fact]
    public void PreviewBounds_DockLeft_LeftEdgeStrip()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockLeft, HostW, HostH);
        Assert.Equal(0, r.X);
        Assert.Equal(HostW * DockDropTargetOverlayControl.EdgePreviewFraction, r.Width);
        Assert.Equal(HostH, r.Height);
    }

    [Fact]
    public void PreviewBounds_DockRight_RightEdgeStrip()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockRight, HostW, HostH);
        var w = HostW * DockDropTargetOverlayControl.EdgePreviewFraction;
        Assert.Equal(HostW - w, r.X);
        Assert.Equal(w, r.Width);
        Assert.Equal(HostH, r.Height);
    }

    [Fact]
    public void PreviewBounds_DockTop_TopEdgeStrip()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockTop, HostW, HostH);
        Assert.Equal(0, r.Y);
        Assert.Equal(HostH * DockDropTargetOverlayControl.EdgePreviewFraction, r.Height);
    }

    [Fact]
    public void PreviewBounds_DockBottom_BottomEdgeStrip()
    {
        var r = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockBottom, HostW, HostH);
        var h = HostH * DockDropTargetOverlayControl.EdgePreviewFraction;
        Assert.Equal(HostH - h, r.Y);
        Assert.Equal(h, r.Height);
    }

    [Fact]
    public void PreviewBounds_ZeroHost_ReturnsEmpty()
    {
        Assert.True(DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.Center, 0, HostH).IsEmpty);
        Assert.True(DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitLeft, HostW, 0).IsEmpty);
    }

    [Fact]
    public void PreviewBounds_AllTargets_StayInsideHost()
    {
        foreach (DockTarget t in global::System.Enum.GetValues<DockTarget>())
        {
            var r = DockDropTargetOverlayControl.ComputePreviewBounds(t, HostW, HostH);
            Assert.InRange(r.X, 0, HostW);
            Assert.InRange(r.Y, 0, HostH);
            Assert.InRange(r.X + r.Width, 0, HostW + 0.001);
            Assert.InRange(r.Y + r.Height, 0, HostH + 0.001);
        }
    }

    // ── Arrow-key focus graph ───────────────────────────────────────────
    //
    // The cluster cross + 4 edge ring is the spatial mental model. Each
    // case below verifies one step of the graph; cycling around the edge
    // ring (Up/Down/Left/Right at DockTop etc.) returns the adjacent
    // edge target.

    [Fact]
    public void Focus_Center_ArrowsLeadToClusterArms()
    {
        Assert.Equal(DockTarget.SplitLeft,   DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Left));
        Assert.Equal(DockTarget.SplitRight,  DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Right));
        Assert.Equal(DockTarget.SplitTop,    DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Up));
        Assert.Equal(DockTarget.SplitBottom, DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Down));
    }

    [Fact]
    public void Focus_SplitLeft_InwardReturnsCenter_OutwardReachesDockLeft()
    {
        Assert.Equal(DockTarget.Center,   DockDropTargetOverlayControl.NextFocus(DockTarget.SplitLeft, VirtualKey.Right));
        Assert.Equal(DockTarget.DockLeft, DockDropTargetOverlayControl.NextFocus(DockTarget.SplitLeft, VirtualKey.Left));
    }

    [Fact]
    public void Focus_DockEdges_InwardReachesMatchingClusterArm()
    {
        Assert.Equal(DockTarget.SplitLeft,   DockDropTargetOverlayControl.NextFocus(DockTarget.DockLeft, VirtualKey.Right));
        Assert.Equal(DockTarget.SplitRight,  DockDropTargetOverlayControl.NextFocus(DockTarget.DockRight, VirtualKey.Left));
        Assert.Equal(DockTarget.SplitTop,    DockDropTargetOverlayControl.NextFocus(DockTarget.DockTop, VirtualKey.Down));
        Assert.Equal(DockTarget.SplitBottom, DockDropTargetOverlayControl.NextFocus(DockTarget.DockBottom, VirtualKey.Up));
    }

    [Fact]
    public void Focus_DockEdges_SidewaysReachesAdjacentEdges()
    {
        Assert.Equal(DockTarget.DockTop,    DockDropTargetOverlayControl.NextFocus(DockTarget.DockLeft, VirtualKey.Up));
        Assert.Equal(DockTarget.DockBottom, DockDropTargetOverlayControl.NextFocus(DockTarget.DockLeft, VirtualKey.Down));
        Assert.Equal(DockTarget.DockTop,    DockDropTargetOverlayControl.NextFocus(DockTarget.DockRight, VirtualKey.Up));
        Assert.Equal(DockTarget.DockBottom, DockDropTargetOverlayControl.NextFocus(DockTarget.DockRight, VirtualKey.Down));
        Assert.Equal(DockTarget.DockLeft,   DockDropTargetOverlayControl.NextFocus(DockTarget.DockTop, VirtualKey.Left));
        Assert.Equal(DockTarget.DockRight,  DockDropTargetOverlayControl.NextFocus(DockTarget.DockTop, VirtualKey.Right));
    }

    [Fact]
    public void Focus_UnknownPair_ReturnsCurrent()
    {
        // No mapping for Center+VirtualKey.Tab → unchanged.
        Assert.Equal(DockTarget.Center, DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Tab));
    }

    // ── AT names (l10n placeholder until §2.21) ─────────────────────────

    [Fact]
    public void AtName_EveryTarget_HasNonEmptyName()
    {
        foreach (DockTarget t in global::System.Enum.GetValues<DockTarget>())
        {
            var name = DockDropTargetOverlayControl.GetLocalizedName(t);
            Assert.False(string.IsNullOrWhiteSpace(name), $"Missing name for {t}");
        }
    }

    [Fact]
    public void AtName_SplitVsDock_DistinctStrings()
    {
        Assert.NotEqual(
            DockDropTargetOverlayControl.GetLocalizedName(DockTarget.SplitLeft),
            DockDropTargetOverlayControl.GetLocalizedName(DockTarget.DockLeft));
        Assert.NotEqual(
            DockDropTargetOverlayControl.GetLocalizedName(DockTarget.SplitRight),
            DockDropTargetOverlayControl.GetLocalizedName(DockTarget.DockRight));
    }

    [Fact]
    public void AtName_Center_IsAddAsTab()
    {
        // Spec §2.3 names the central target "Add as tab" not "Dock center".
        Assert.Equal("Add as tab", DockDropTargetOverlayControl.GetLocalizedName(DockTarget.Center));
    }

    // ── Geometry sizing ────────────────────────────────────────────────

    [Fact]
    public void ButtonSize_MeetsWcag255_TouchTarget()
    {
        Assert.True(DockDropTargetOverlayControl.ButtonSizeDip >= 44.0,
            "WCAG 2.5.5 / spec §8.7 mandates ≥ 44×44 DIP touch targets.");
    }
}
