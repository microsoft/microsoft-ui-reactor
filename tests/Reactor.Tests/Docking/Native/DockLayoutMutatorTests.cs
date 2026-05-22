using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Pure-function coverage for §2.4 layout mutation. The mutator is the
/// engine the drag pipeline applies to the immutable DockNode tree on
/// confirm; tests cover removal (with empty-container collapse) and
/// insertion at every DockTarget enum value.
/// </summary>
public class DockLayoutMutatorTests
{
    private static DockableContent Leaf(string key) => new(key, Key: key);

    // ── RemovePane ─────────────────────────────────────────────────────

    [Fact]
    public void RemovePane_LoneLeaf_ReturnsNull()
    {
        var p = Leaf("p");
        var (root, found) = DockLayoutMutator.RemovePane(p, p);
        Assert.True(found);
        Assert.Null(root);
    }

    [Fact]
    public void RemovePane_LeafNotInTree_NoOp()
    {
        var p = Leaf("p");
        var q = Leaf("q");
        var (root, found) = DockLayoutMutator.RemovePane(p, q);
        Assert.False(found);
        Assert.Same(p, root);
    }

    [Fact]
    public void RemovePane_FromTabGroup_RemovesAndPreservesGroup()
    {
        var a = Leaf("a"); var b = Leaf("b"); var c = Leaf("c");
        var grp = new DockTabGroup(new[] { a, b, c }, SelectedIndex: 1);

        var (root, found) = DockLayoutMutator.RemovePane(grp, b);
        Assert.True(found);
        var newGrp = Assert.IsType<DockTabGroup>(root);
        Assert.Equal(2, newGrp.Documents.Count);
        Assert.Same(a, newGrp.Documents[0]);
        Assert.Same(c, newGrp.Documents[1]);
    }

    [Fact]
    public void RemovePane_LastFromTabGroup_CollapsesGroup()
    {
        var a = Leaf("a");
        var grp = new DockTabGroup(new[] { a });
        var (root, found) = DockLayoutMutator.RemovePane(grp, a);
        Assert.True(found);
        Assert.Null(root);
    }

    [Fact]
    public void RemovePane_OneOfTwoFromSplit_CollapsesToSibling()
    {
        var a = Leaf("a"); var b = Leaf("b");
        var split = new DockSplit(Orientation.Horizontal, new DockNode[] { a, b });

        var (root, found) = DockLayoutMutator.RemovePane(split, a);
        Assert.True(found);
        // Single-sibling collapse: the split simplifies to b.
        Assert.Same(b, root);
    }

    [Fact]
    public void RemovePane_FromMultiSplit_PreservesSplit()
    {
        var a = Leaf("a"); var b = Leaf("b"); var c = Leaf("c");
        var split = new DockSplit(Orientation.Horizontal, new DockNode[] { a, b, c });

        var (root, found) = DockLayoutMutator.RemovePane(split, b);
        Assert.True(found);
        var newSplit = Assert.IsType<DockSplit>(root);
        Assert.Equal(2, newSplit.Children.Count);
        Assert.Same(a, newSplit.Children[0]);
        Assert.Same(c, newSplit.Children[1]);
    }

    [Fact]
    public void RemovePane_DeepNested_FindsAndCollapses()
    {
        var a = Leaf("a"); var b = Leaf("b"); var c = Leaf("c");
        var innerSplit = new DockSplit(Orientation.Vertical, new DockNode[] { b, c });
        var outerSplit = new DockSplit(Orientation.Horizontal, new DockNode[] { a, innerSplit });

        var (root, found) = DockLayoutMutator.RemovePane(outerSplit, b);
        Assert.True(found);
        var newOuter = Assert.IsType<DockSplit>(root);
        // Inner [b, c] becomes c; outer becomes [a, c].
        Assert.Same(a, newOuter.Children[0]);
        Assert.Same(c, newOuter.Children[1]);
    }

    // ── InsertPaneAtTarget ─────────────────────────────────────────────

    [Fact]
    public void Insert_Center_AddsAsTabToFirstGroup()
    {
        var existing = Leaf("existing");
        var newPane = Leaf("new");

        var result = DockLayoutMutator.InsertPaneAtTarget(existing, newPane, DockTarget.Center);

        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, grp.Documents.Count);
        Assert.Same(existing, grp.Documents[0]);
        Assert.Same(newPane, grp.Documents[1]);
    }

    [Fact]
    public void Insert_Center_IntoExistingGroup_AppendsTab()
    {
        var a = Leaf("a"); var b = Leaf("b"); var added = Leaf("added");
        var grp = new DockTabGroup(new[] { a, b });

        var result = DockLayoutMutator.InsertPaneAtTarget(grp, added, DockTarget.Center);
        var newGrp = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(3, newGrp.Documents.Count);
        Assert.Same(added, newGrp.Documents[2]);
        Assert.Equal(2, newGrp.SelectedIndex);
    }

    // Helper — split-target inserts wrap the new pane in a single-doc
    // DockTabGroup so it shows a tab strip (§2.4). Unwrap when asserting.
    private static DockableContent SoleDoc(DockNode node) =>
        ((DockTabGroup)node).Documents.Single();

    [Fact]
    public void Insert_SplitLeft_PlacesNewBefore()
    {
        var existing = Leaf("e"); var newPane = Leaf("n");
        var result = DockLayoutMutator.InsertPaneAtTarget(existing, newPane, DockTarget.SplitLeft);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(Orientation.Horizontal, split.Orientation);
        Assert.Same(newPane, SoleDoc(split.Children[0]));
        Assert.Same(existing, split.Children[1]);
    }

    [Fact]
    public void Insert_SplitRight_PlacesNewAfter()
    {
        var existing = Leaf("e"); var newPane = Leaf("n");
        var result = DockLayoutMutator.InsertPaneAtTarget(existing, newPane, DockTarget.SplitRight);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(Orientation.Horizontal, split.Orientation);
        Assert.Same(existing, split.Children[0]);
        Assert.Same(newPane, SoleDoc(split.Children[1]));
    }

    [Fact]
    public void Insert_SplitTop_PlacesNewAbove_Vertical()
    {
        var existing = Leaf("e"); var newPane = Leaf("n");
        var result = DockLayoutMutator.InsertPaneAtTarget(existing, newPane, DockTarget.SplitTop);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(Orientation.Vertical, split.Orientation);
        Assert.Same(newPane, SoleDoc(split.Children[0]));
        Assert.Same(existing, split.Children[1]);
    }

    [Fact]
    public void Insert_SplitBottom_PlacesNewBelow_Vertical()
    {
        var existing = Leaf("e"); var newPane = Leaf("n");
        var result = DockLayoutMutator.InsertPaneAtTarget(existing, newPane, DockTarget.SplitBottom);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(Orientation.Vertical, split.Orientation);
        Assert.Same(existing, split.Children[0]);
        Assert.Same(newPane, SoleDoc(split.Children[1]));
    }

    [Fact]
    public void Insert_DockEdges_SplitAtRoot()
    {
        var e = Leaf("e"); var n = Leaf("n");
        var left = (DockSplit)DockLayoutMutator.InsertPaneAtTarget(e, n, DockTarget.DockLeft);
        var right = (DockSplit)DockLayoutMutator.InsertPaneAtTarget(e, n, DockTarget.DockRight);
        var top   = (DockSplit)DockLayoutMutator.InsertPaneAtTarget(e, n, DockTarget.DockTop);
        var bot   = (DockSplit)DockLayoutMutator.InsertPaneAtTarget(e, n, DockTarget.DockBottom);

        Assert.Equal(Orientation.Horizontal, left.Orientation);
        Assert.Same(n, SoleDoc(left.Children[0]));
        Assert.Equal(Orientation.Horizontal, right.Orientation);
        Assert.Same(n, SoleDoc(right.Children[1]));
        Assert.Equal(Orientation.Vertical, top.Orientation);
        Assert.Same(n, SoleDoc(top.Children[0]));
        Assert.Equal(Orientation.Vertical, bot.Orientation);
        Assert.Same(n, SoleDoc(bot.Children[1]));
    }

    [Fact]
    public void Insert_NullRoot_ReturnsPaneWrappedInGroup()
    {
        var pane = Leaf("p");
        var result = DockLayoutMutator.InsertPaneAtTarget(null, pane, DockTarget.SplitLeft);
        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Same(pane, grp.Documents.Single());
    }

    // ── MovePaneToTarget (composite) ───────────────────────────────────

    [Fact]
    public void Move_RemovesFromSourceAndInsertsAtTarget()
    {
        var a = Leaf("a"); var b = Leaf("b");
        var initial = new DockSplit(Orientation.Horizontal, new DockNode[] { a, b });

        // Move 'a' to SplitRight at the root. The remove collapses the
        // split to b, then the insert wraps b + a back into a split with
        // a (wrapped in a single-doc DockTabGroup) on the trailing side.
        var result = DockLayoutMutator.MovePaneToTarget(initial, a, DockTarget.SplitRight);

        var split = Assert.IsType<DockSplit>(result);
        Assert.Same(b, split.Children[0]);
        Assert.Same(a, SoleDoc(split.Children[1]));
    }

    [Fact]
    public void Move_PaneNotInTree_ReturnsOriginal()
    {
        var root = Leaf("a");
        var other = Leaf("b");
        var result = DockLayoutMutator.MovePaneToTarget(root, other, DockTarget.Center);
        Assert.Same(root, result);
    }

    // ── FindContainer (§2.15 PreviousContainer support) ────────────────

    [Fact]
    public void FindContainer_LeafIsRoot_ReturnsSelf()
    {
        var p = Leaf("p");
        Assert.Same(p, DockLayoutMutator.FindContainer(p, p));
    }

    [Fact]
    public void FindContainer_TabGroupContent_ReturnsGroup()
    {
        var p = Leaf("p");
        var grp = new DockTabGroup(new[] { p });
        Assert.Same(grp, DockLayoutMutator.FindContainer(grp, p));
    }

    [Fact]
    public void FindContainer_NestedSplit_FindsGroupWithoutClimbingFurther()
    {
        var p = Leaf("p");
        var inner = new DockTabGroup(new[] { p });
        var split = new DockSplit(Orientation.Horizontal, new DockNode[] { Leaf("x"), inner });
        Assert.Same(inner, DockLayoutMutator.FindContainer(split, p));
    }

    [Fact]
    public void FindContainer_NotPresent_ReturnsNull()
    {
        var p = Leaf("p");
        var q = Leaf("q");
        var grp = new DockTabGroup(new[] { p });
        Assert.Null(DockLayoutMutator.FindContainer(grp, q));
    }

    // ── ShowFromHistory ────────────────────────────────────────────────

    [Fact]
    public void ShowFromHistory_NoHistory_FallsBackToTarget()
    {
        var a = Leaf("a");
        var grp = new DockTabGroup(new[] { a });
        var newPane = Leaf("new");

        var result = DockLayoutMutator.ShowFromHistory(grp, newPane, DockTarget.SplitRight);

        // No previous container → default insertion at SplitRight wraps
        // the root and the new pane into a split.
        var split = Assert.IsType<DockSplit>(result);
        Assert.Same(grp, split.Children[0]);
        Assert.Same(newPane, SoleDoc(split.Children[1]));
    }

    [Fact]
    public void ShowFromHistory_PreviousGroupStillInTree_FoldsAsTab()
    {
        var stayingPane = Leaf("stay");
        var rememberedGroup = new DockTabGroup(new[] { stayingPane });
        var sibling = Leaf("sibling");
        var split = new DockSplit(Orientation.Horizontal,
            new DockNode[] { rememberedGroup, sibling });

        var hiddenPane = Leaf("hidden");
        PreviousContainerTracker.Set(hiddenPane, rememberedGroup);

        var result = DockLayoutMutator.ShowFromHistory(split, hiddenPane, DockTarget.Center);

        var s = Assert.IsType<DockSplit>(result);
        var foldedGroup = Assert.IsType<DockTabGroup>(s.Children[0]);
        Assert.Equal(2, foldedGroup.Documents.Count);
        Assert.Same(stayingPane, foldedGroup.Documents[0]);
        Assert.Same(hiddenPane, foldedGroup.Documents[1]);
        // Selection lands on the newly-shown pane (VS parity).
        Assert.Equal(1, foldedGroup.SelectedIndex);
        // Sibling slot in the split is untouched.
        Assert.Same(sibling, s.Children[1]);
    }

    [Fact]
    public void ShowFromHistory_PreviousGroupTornDown_FallsBackToTarget()
    {
        // Pane remembers a group that no longer exists in the layout
        // (e.g. the group was emptied + collapsed during prior mutations).
        // The show falls back to the default target instead of crashing.
        var hiddenPane = Leaf("hidden");
        var orphanedGroup = new DockTabGroup(new[] { hiddenPane });
        PreviousContainerTracker.Set(hiddenPane, orphanedGroup);

        var liveRoot = new DockTabGroup(new[] { Leaf("a") });

        var result = DockLayoutMutator.ShowFromHistory(liveRoot, hiddenPane, DockTarget.Center);

        // Fallback Center → fold into the live root group.
        var folded = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, folded.Documents.Count);
        Assert.Same(hiddenPane, folded.Documents[1]);
    }

    // ── Key-based pane identity (§2.4 — drag survives rebuild) ─────────

    /// <summary>
    /// When a drag session captures a pane reference at drag-start, and
    /// the parent re-renders before drop (creating a fresh DockableContent
    /// record with the same Key), MovePaneToTarget must still find +
    /// remove the source pane in the new tree. Without key-based lookup
    /// the source-pane lookup fails ⇒ insert-only ⇒ duplicate tab.
    /// </summary>
    [Fact]
    public void FindContainer_MatchesByKey_WhenReferenceDiffers()
    {
        var keyed1 = new DockableContent("Items", Key: "items");
        var keyed2 = new DockableContent("Items", Key: "items"); // same key, different ref
        Assert.False(ReferenceEquals(keyed1, keyed2));

        var root = new DockTabGroup(new DockableContent[] { keyed1 });
        var container = DockLayoutMutator.FindContainer(root, keyed2);
        Assert.NotNull(container);
        Assert.Same(root, container);
    }

    [Fact]
    public void RemovePane_MatchesByKey_WhenReferenceDiffers()
    {
        var keyed1 = new DockableContent("Items", Key: "items");
        var keyed2 = new DockableContent("Items", Key: "items");
        var other = new DockableContent("Detail", Key: "detail");
        var root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { keyed1 }),
            new DockTabGroup(new[] { other }),
        });

        var (after, found) = DockLayoutMutator.RemovePane(root, keyed2);
        Assert.True(found);
        // Left group collapses (last pane removed); only right remains.
        var group = Assert.IsType<DockTabGroup>(after);
        Assert.Same(other, group.Documents[0]);
    }

    // ── Shape-only override (§2.30) ────────────────────────────────────

    [Fact]
    public void StripContent_LeafKeepsOnlyKey()
    {
        var leaf = new DockableContent(
            Title: "A",
            Content: new Microsoft.UI.Reactor.Core.TextBlockElement("body-a"),
            Key: "a",
            CanClose: true,
            CanPin: true);
        var stripped = (DockableContent)DockLayoutMutator.StripContent(leaf)!;
        Assert.Equal("a", stripped.Key);
        Assert.Equal(string.Empty, stripped.Title);
        Assert.Null(stripped.Content);
        Assert.False(stripped.CanClose);
        Assert.False(stripped.CanPin);
    }

    [Fact]
    public void StripContent_PreservesShapeStructure()
    {
        var source = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new DockableContent[]
            {
                new DockableContent("A", Content: new Microsoft.UI.Reactor.Core.TextBlockElement("body-a"), Key: "a"),
                new DockableContent("B", Content: new Microsoft.UI.Reactor.Core.TextBlockElement("body-b"), Key: "b"),
            }, SelectedIndex: 1),
            new DockTabGroup(new DockableContent[]
            {
                new DockableContent("C", Content: new Microsoft.UI.Reactor.Core.TextBlockElement("body-c"), Key: "c"),
            }),
        });
        var shape = (DockSplit)DockLayoutMutator.StripContent(source)!;
        Assert.Equal(Orientation.Horizontal, shape.Orientation);
        Assert.Equal(2, shape.Children.Count);
        var leftGrp = Assert.IsType<DockTabGroup>(shape.Children[0]);
        Assert.Equal(2, leftGrp.Documents.Count);
        Assert.Equal(1, leftGrp.SelectedIndex);
        Assert.Equal("a", leftGrp.Documents[0].Key);
        Assert.Null(leftGrp.Documents[0].Content);
        Assert.Equal("b", leftGrp.Documents[1].Key);
        var rightGrp = Assert.IsType<DockTabGroup>(shape.Children[1]);
        Assert.Equal("c", rightGrp.Documents[0].Key);
    }

    [Fact]
    public void ResolveContents_SubstitutesFullPanesByKey()
    {
        var shape = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new DockableContent[]
            {
                new DockableContent(string.Empty, Key: "a"),
            }),
            new DockTabGroup(new DockableContent[]
            {
                new DockableContent(string.Empty, Key: "b"),
            }),
        });
        var freshA = new DockableContent("Alpha", Content: new Microsoft.UI.Reactor.Core.TextBlockElement("alpha-body"), Key: "a", CanClose: true);
        var freshB = new DockableContent("Bravo", Content: new Microsoft.UI.Reactor.Core.TextBlockElement("bravo-body"), Key: "b");
        // Source order is intentionally different from shape order — the
        // resolve should match by key, not by position.
        var source = new DockSplit(Orientation.Vertical, new DockNode[]
        {
            new DockTabGroup(new DockableContent[] { freshB, freshA }),
        });

        var resolved = (DockSplit)DockLayoutMutator.ResolveContents(shape, source)!;
        // Resolved tree keeps the SHAPE (horizontal, two groups) from
        // `shape`, but each leaf has the full record from `source`.
        Assert.Equal(Orientation.Horizontal, resolved.Orientation);
        Assert.Equal(2, resolved.Children.Count);
        var leftGrp = Assert.IsType<DockTabGroup>(resolved.Children[0]);
        Assert.Same(freshA, leftGrp.Documents[0]);
        var rightGrp = Assert.IsType<DockTabGroup>(resolved.Children[1]);
        Assert.Same(freshB, rightGrp.Documents[0]);
    }

    [Fact]
    public void ResolveContents_LeafMissingFromSource_RemainsKeyOnly()
    {
        var shape = new DockTabGroup(new DockableContent[]
        {
            new DockableContent(string.Empty, Key: "missing"),
        });
        var source = new DockTabGroup(new DockableContent[]
        {
            new DockableContent("Other", Key: "other"),
        });
        var resolved = (DockTabGroup)DockLayoutMutator.ResolveContents(shape, source)!;
        // The "missing" leaf has no match in source — it stays as the
        // key-only record from the shape (orphan; app can detect and
        // remove via WithKey-remount).
        Assert.Single(resolved.Documents);
        Assert.Equal("missing", resolved.Documents[0].Key);
        Assert.Null(resolved.Documents[0].Content);
    }

    [Fact]
    public void ResolveContents_NullShape_ReturnsNull()
    {
        var source = new DockTabGroup(new DockableContent[]
        {
            new DockableContent("A", Key: "a"),
        });
        Assert.Null(DockLayoutMutator.ResolveContents(null, source));
    }

    [Fact]
    public void ResolveContents_NullSource_ReturnsShapeUnchanged()
    {
        var shape = new DockTabGroup(new DockableContent[]
        {
            new DockableContent(string.Empty, Key: "a"),
        });
        var resolved = DockLayoutMutator.ResolveContents(shape, (DockNode?)null);
        Assert.Same(shape, resolved);
    }

    [Fact]
    public void MovePaneToTarget_PreservesIdentityAcrossRebuild_NoDuplicate()
    {
        var items1 = new DockableContent("Items", Key: "items");
        var items2 = new DockableContent("Items", Key: "items"); // post-rebuild reference
        var detail = new DockableContent("Detail", Key: "detail");
        var root = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[] { items1 }),
            new DockTabGroup(new[] { detail }),
        });

        // Simulate drag-start captured items1, drop fires against items2.
        var result = DockLayoutMutator.MovePaneToTarget(root, items2, DockTarget.Center);
        Assert.NotNull(result);
        // Result should be ONE tab group containing both detail + items (the moved one).
        // No duplicate of items.
        var folded = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, folded.Documents.Count);
        Assert.Equal("detail", folded.Documents[0].Key);
        Assert.Equal("items", folded.Documents[1].Key);
    }
}
