using System;
using System.Linq;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Docking.Persistence;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 Phase 8 — cross-phase integration scenarios driven from the
/// mutator + serializer + model surfaces (no Reactor host needed). These
/// pin down the VS-shaped layout journey end-to-end against the public
/// API.
/// </summary>
public class Spec046IntegrationTests
{
    private static Document Doc(string key) => new() { Title = key, Key = key };
    private static ToolWindow Tool(string key, double? w = null, DockSides allowed = DockSides.All) =>
        new() { Title = key, Key = key, Width = w, AllowedSides = allowed };

    private static DockNode VSLayout()
        => new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Tool("Gallery", w: 260) },
                Width: 260, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(new[] { (DockableContent)Tool("Config", w: 320) },
                Width: 320, Role: DockGroupRole.ToolWindowStrip),
        });

    private static DockTabGroup FindGroupOfRole(DockNode root, DockGroupRole role)
    {
        return Walk(root) ?? throw new InvalidOperationException($"no group with role {role}");
        DockTabGroup? Walk(DockNode n) => n switch
        {
            DockTabGroup g when g.Role == role => g,
            DockSplit s => s.Children.Select(Walk).FirstOrDefault(g => g is not null),
            _ => null,
        };
    }

    // ── §8.1 VS-layout happy path ─────────────────────────────────────────

    [Fact]
    public void VSLayout_OpenThreeDocs_CloseAll_ReopenLandsInDocumentArea()
    {
        var layout = VSLayout();
        layout = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center);
        layout = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d2"), DockTarget.Center);
        layout = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d3"), DockTarget.Center);
        // All three should be in the DocumentArea group.
        var docArea = FindGroupOfRole(layout, DockGroupRole.DocumentArea);
        Assert.Equal(3, docArea.Documents.Count);
        Assert.Equal(new[] { "d1", "d2", "d3" }, docArea.Documents.Select(d => (string)d.Key!));

        // Close all three.
        foreach (var key in new[] { "d1", "d2", "d3" })
        {
            var pane = FindGroupOfRole(layout, DockGroupRole.DocumentArea).Documents
                .First(d => Equals(d.Key, key));
            var (after, found) = DockLayoutMutator.RemovePane(layout, pane);
            Assert.True(found);
            layout = after!;
        }
        // DocumentArea survives empty.
        var emptyDocArea = FindGroupOfRole(layout, DockGroupRole.DocumentArea);
        Assert.Empty(emptyDocArea.Documents);

        // Reopen — lands cleanly in the surviving DocumentArea.
        layout = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d4"), DockTarget.Center);
        var refilledDocArea = FindGroupOfRole(layout, DockGroupRole.DocumentArea);
        Assert.Single(refilledDocArea.Documents);
        Assert.Equal("d4", refilledDocArea.Documents[0].Key);
    }

    // ── §8.2 Multiple documents — split inside DocumentArea ──────────────

    [Fact]
    public void DocumentSplit_DragOneToRightEdge_NewSiblingIsAlsoDocumentArea()
    {
        var docArea = new DockTabGroup(new[]
        {
            (DockableContent)Doc("d1"),
            (DockableContent)Doc("d2"),
            (DockableContent)Doc("d3"),
        }, SelectedIndex: 0, Role: DockGroupRole.DocumentArea);
        DockNode root = docArea;
        // Move d3 to SplitRight against the existing DocumentArea group.
        var result = DockLayoutMutator.MovePaneToGroupTarget(root, docArea.Documents[2], docArea, DockTarget.SplitRight);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(2, split.Children.Count);
        Assert.All(split.Children, c => Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)c).Role));
        // d4 dock(Center) lands in the FIRST DocumentArea (tree order).
        var withD4 = DockLayoutMutator.InsertPaneAtTarget(result, Doc("d4"), DockTarget.Center);
        var firstDocArea = (DockTabGroup)((DockSplit)withD4).Children[0];
        Assert.Contains(firstDocArea.Documents, d => Equals(d.Key, "d4"));
    }

    // ── §8.3 Creating new tool areas when none exist ─────────────────────

    [Fact]
    public void OnlyDocumentArea_DockBottomToolWindow_CreatesToolWindowStrip()
    {
        var docArea = new DockTabGroup(new[] { (DockableContent)Doc("d1") },
            SelectedIndex: 0, Role: DockGroupRole.DocumentArea);
        DockNode root = docArea;
        var result = DockLayoutMutator.InsertPaneAtTarget(root, Tool("errors"), DockTarget.DockBottom);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[0]).Role);
        Assert.Equal(DockGroupRole.ToolWindowStrip, ((DockTabGroup)split.Children[1]).Role);
        // Then docking a second tool to Center lands in the existing strip
        // (preferred role match).
        var withTool2 = DockLayoutMutator.InsertPaneAtTarget(result, Tool("output"), DockTarget.Center);
        var strip = FindGroupOfRole(withTool2, DockGroupRole.ToolWindowStrip);
        Assert.Equal(2, strip.Documents.Count);
    }

    // ── §8.4 Floating round-trip ─────────────────────────────────────────

    [Fact]
    public void FloatingRoundTrip_DocumentTearOut_BackToHost_LandsInDocumentArea()
    {
        var layout = VSLayout();
        // Open a document in the well.
        layout = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("d1"), DockTarget.Center);
        // "Tear out" — simulate by removing from layout.
        var docArea = FindGroupOfRole(layout, DockGroupRole.DocumentArea);
        var torn = docArea.Documents[0];
        var (afterTearOut, _) = DockLayoutMutator.RemovePane(layout, torn);
        // DocumentArea survives empty.
        Assert.Equal(DockGroupRole.DocumentArea,
            FindGroupOfRole(afterTearOut!, DockGroupRole.DocumentArea).Role);
        // Re-dock at center via role-aware routing.
        var redocked = DockLayoutMutator.InsertPaneAtTarget(afterTearOut, torn, DockTarget.Center);
        var docAreaAgain = FindGroupOfRole(redocked, DockGroupRole.DocumentArea);
        Assert.Contains(docAreaAgain.Documents, d => Equals(d.Key, "d1"));
    }

    // ── §8.5 Reset layout (JSON round-trip equivalence) ──────────────────

    [Fact]
    public void ResetLayout_JsonRoundTrip_PreservesShape()
    {
        var initial = VSLayout();
        var json = DockLayoutSerializer.Save(initial);
        var reloaded = DockLayoutSerializer.Load(json).Root;
        // Structural shape preserved.
        var initialSplit = (DockSplit)initial;
        var reloadedSplit = Assert.IsType<DockSplit>(reloaded);
        Assert.Equal(initialSplit.Children.Count, reloadedSplit.Children.Count);
        for (int i = 0; i < initialSplit.Children.Count; i++)
        {
            var a = (DockTabGroup)initialSplit.Children[i];
            var b = (DockTabGroup)reloadedSplit.Children[i];
            Assert.Equal(a.Role, b.Role);
        }
    }

    // ── §8.10 Edge cases ─────────────────────────────────────────────────

    [Fact]
    public void EmptyLayout_DockCenter_WrapsAsDocumentArea()
    {
        var result = DockLayoutMutator.InsertPaneAtTarget(null, Doc("d1"), DockTarget.Center);
        var g = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(DockGroupRole.DocumentArea, g.Role);
    }

    [Fact]
    public void AllGeneralLayout_NoOptIn_BehavesAsLeftmostDescendant()
    {
        // Pre-spec-046 callers that don't opt in get today's behavior:
        // role=General accepts every category; first child wins on the
        // second-pass acceptance scan. No diagnostic emitted.
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { (DockableContent)Doc("a") }, Role: DockGroupRole.General),
            new DockTabGroup(new[] { (DockableContent)Doc("b") }, Role: DockGroupRole.General),
        });
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Doc("new"), DockTarget.Center, out var fb);
        Assert.Null(fb);
        var split = (DockSplit)result;
        var first = (DockTabGroup)split.Children[0];
        Assert.Contains(first.Documents, d => Equals(d.Key, "new"));
    }

    [Fact]
    public void SingleDocumentAreaOnly_ToolWindowCenter_FallsBackWithDiagnostic()
    {
        DockNode layout = new DockTabGroup(new[] { (DockableContent)Doc("d1") },
            Role: DockGroupRole.DocumentArea);
        var result = DockLayoutMutator.InsertPaneAtTarget(layout, Tool("t1"), DockTarget.Center, out var fb);
        Assert.NotNull(fb);
        // Tool lands in the DocumentArea as fallback (only option).
        var g = (DockTabGroup)result;
        Assert.Equal(2, g.Documents.Count);
    }

    // ── Combination: AllowedSides + role filter ──────────────────────────

    [Fact]
    public void Filter_ToolWindowWithBottomOnly_OverDocumentArea_AllSplitsRejected()
    {
        var docArea = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        var bottomOnly = Tool("errors", allowed: DockSides.Bottom);
        // Center: rejected by role (DocumentArea rejects ToolWindow).
        Assert.False(DockDropFilter.CanDropInto(docArea, bottomOnly, targetSide: null));
        // Split sides: also rejected by role (filter checks role first).
        foreach (var side in new[] { DockSide.Left, DockSide.Top, DockSide.Right, DockSide.Bottom })
            Assert.False(DockDropFilter.CanDropInto(docArea, bottomOnly, targetSide: side));
    }

    [Fact]
    public void Filter_ToolWindowWithBottomOnly_OverGeneral_OnlyBottomSplitAccepted()
    {
        var general = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General);
        var bottomOnly = Tool("errors", allowed: DockSides.Bottom);
        // Center: General accepts ToolWindow regardless of AllowedSides
        // (no targetSide passed → mask isn't consulted).
        Assert.True(DockDropFilter.CanDropInto(general, bottomOnly, targetSide: null));
        Assert.False(DockDropFilter.CanDropInto(general, bottomOnly, targetSide: DockSide.Left));
        Assert.False(DockDropFilter.CanDropInto(general, bottomOnly, targetSide: DockSide.Top));
        Assert.False(DockDropFilter.CanDropInto(general, bottomOnly, targetSide: DockSide.Right));
        Assert.True (DockDropFilter.CanDropInto(general, bottomOnly, targetSide: DockSide.Bottom));
    }
}
