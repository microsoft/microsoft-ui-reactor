using System;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.6 — drag-drop drop-target filtering. <see cref="DockDropFilter.CanDropInto"/>
/// + <see cref="DockDropFilter.CanDockAtEdge"/> rejected combinations
/// dim the overlay button and ignore the drop; <see cref="DockHostModel.PinToSide"/>
/// throws on a side excluded by the tool window's mask.
/// </summary>
public class DockDragDropFilterTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key, DockSides allowed = DockSides.All) =>
        new() { Title = key, Key = key, AllowedSides = allowed };
    private static DockableContent Untyped(string key) => new(Title: key, Key: key);

    // ── CanDropInto ───────────────────────────────────────────────────────

    [Fact]
    public void Document_OverToolWindowStrip_Rejected()
    {
        var strip = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip);
        Assert.False(DockDropFilter.CanDropInto(strip, Doc("d1"), targetSide: null));
    }

    [Fact]
    public void Document_OverDocumentArea_Accepted()
    {
        var docArea = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        Assert.True(DockDropFilter.CanDropInto(docArea, Doc("d1"), targetSide: null));
    }

    [Fact]
    public void ToolWindow_AllowedSidesBottom_OverLeftSplit_Rejected()
    {
        // ToolWindow with Bottom-only mask, dropped on a SplitLeft target
        // (logical side Left) — filter rejects.
        var grp = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip);
        Assert.False(DockDropFilter.CanDropInto(grp, Tool("t1", DockSides.Bottom), targetSide: DockSide.Left));
    }

    [Fact]
    public void ToolWindow_AllowedSidesAll_AnySide_Accepted()
    {
        var grp = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip);
        Assert.True(DockDropFilter.CanDropInto(grp, Tool("t1", DockSides.All), targetSide: DockSide.Left));
        Assert.True(DockDropFilter.CanDropInto(grp, Tool("t1", DockSides.All), targetSide: DockSide.Bottom));
    }

    [Fact]
    public void UntypedDockableContent_AcceptedAnywhere()
    {
        var docArea = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        var strip = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip);
        var general = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General);
        Assert.True(DockDropFilter.CanDropInto(docArea, Untyped("u"), targetSide: DockSide.Left));
        Assert.True(DockDropFilter.CanDropInto(strip,   Untyped("u"), targetSide: DockSide.Top));
        Assert.True(DockDropFilter.CanDropInto(general, Untyped("u"), targetSide: null));
    }

    [Fact]
    public void Document_OverEmptyDocumentArea_CenterAccepted()
    {
        // Empty DocumentArea must still surface Center as a valid landing.
        var empty = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        Assert.True(DockDropFilter.CanDropInto(empty, Doc("d1"), targetSide: null));
    }

    [Fact]
    public void ToolWindow_OverEmptyDocumentArea_CenterRejected()
    {
        // Symmetric: empty DocumentArea rejects ToolWindow Center.
        var empty = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        Assert.False(DockDropFilter.CanDropInto(empty, Tool("t1"), targetSide: null));
    }

    // ── CanDockAtEdge — root-level Dock* edge filter ──────────────────────

    [Fact]
    public void Edge_Document_NeverFiltered()
    {
        // Documents have no AllowedSides mask; every edge accepts.
        Assert.True(DockDropFilter.CanDockAtEdge(Doc("d1"), DockSide.Left));
        Assert.True(DockDropFilter.CanDockAtEdge(Doc("d1"), DockSide.Bottom));
    }

    [Fact]
    public void Edge_ToolWindow_BottomOnly_OnlyBottomAccepted()
    {
        var tw = Tool("errors", DockSides.Bottom);
        Assert.False(DockDropFilter.CanDockAtEdge(tw, DockSide.Left));
        Assert.False(DockDropFilter.CanDockAtEdge(tw, DockSide.Top));
        Assert.False(DockDropFilter.CanDockAtEdge(tw, DockSide.Right));
        Assert.True (DockDropFilter.CanDockAtEdge(tw, DockSide.Bottom));
    }

    [Fact]
    public void Edge_ToolWindow_None_AllRejected()
    {
        var tw = Tool("floater", DockSides.None);
        Assert.False(DockDropFilter.CanDockAtEdge(tw, DockSide.Left));
        Assert.False(DockDropFilter.CanDockAtEdge(tw, DockSide.Top));
        Assert.False(DockDropFilter.CanDockAtEdge(tw, DockSide.Right));
        Assert.False(DockDropFilter.CanDockAtEdge(tw, DockSide.Bottom));
    }

    // ── SideOf — DockTarget → logical DockSide ────────────────────────────

    [Theory]
    [InlineData(DockTarget.Center, null)]
    [InlineData(DockTarget.SplitLeft, DockSide.Left)]
    [InlineData(DockTarget.SplitTop, DockSide.Top)]
    [InlineData(DockTarget.SplitRight, DockSide.Right)]
    [InlineData(DockTarget.SplitBottom, DockSide.Bottom)]
    [InlineData(DockTarget.DockLeft, DockSide.Left)]
    [InlineData(DockTarget.DockTop, DockSide.Top)]
    [InlineData(DockTarget.DockRight, DockSide.Right)]
    [InlineData(DockTarget.DockBottom, DockSide.Bottom)]
    public void SideOf_Maps(DockTarget t, DockSide? expected)
        => Assert.Equal(expected, DockDropFilter.SideOf(t));

    // ── PinToSide mask validation (spec §6.6 / §9 Q4) ─────────────────────

    [Fact]
    public void PinToSide_AllowedSidesBottom_PinningLeftThrows()
    {
        var m = new DockHostModel();
        var tw = Tool("errors", DockSides.Bottom);
        var ex = Assert.Throws<InvalidOperationException>(() => m.PinToSide(tw, DockSide.Left));
        Assert.Contains("AllowedSides", ex.Message);
        Assert.Contains("Left", ex.Message);
        // Nothing queued.
        Assert.Empty(m.Pending);
    }

    [Fact]
    public void PinToSide_AllowedSidesBottom_PinningBottomSucceeds()
    {
        var m = new DockHostModel();
        var tw = Tool("errors", DockSides.Bottom);
        m.PinToSide(tw, DockSide.Bottom);
        Assert.Single(m.Pending);
        var op = Assert.IsType<PendingMutation.PinToSideOp>(m.Pending[0]);
        Assert.Equal(DockSide.Bottom, op.Side);
    }

    [Fact]
    public void PinToSide_DefaultAll_AnySideSucceeds()
    {
        var m = new DockHostModel();
        var tw = Tool("anywhere");  // AllowedSides=All by default
        m.PinToSide(tw, DockSide.Left);
        m.PinToSide(tw, DockSide.Top);
        m.PinToSide(tw, DockSide.Right);
        m.PinToSide(tw, DockSide.Bottom);
        Assert.Equal(4, m.Pending.Count);
    }

    [Fact]
    public void PinToSide_AllowedSidesNone_EverySideThrows()
    {
        var m = new DockHostModel();
        var tw = Tool("floatonly", DockSides.None);
        Assert.Throws<InvalidOperationException>(() => m.PinToSide(tw, DockSide.Left));
        Assert.Throws<InvalidOperationException>(() => m.PinToSide(tw, DockSide.Top));
        Assert.Throws<InvalidOperationException>(() => m.PinToSide(tw, DockSide.Right));
        Assert.Throws<InvalidOperationException>(() => m.PinToSide(tw, DockSide.Bottom));
        Assert.Empty(m.Pending);
    }

    [Fact]
    public void PinToSide_ErrorMessage_IncludesTitleKeyMask()
    {
        var m = new DockHostModel();
        var tw = Tool("errors", DockSides.Bottom);
        var ex = Assert.Throws<InvalidOperationException>(() => m.PinToSide(tw, DockSide.Left));
        Assert.Contains("errors", ex.Message);  // title and key both "errors"
        Assert.Contains("Bottom", ex.Message);  // mask
    }
}
