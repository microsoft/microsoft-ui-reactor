using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 §2.4 / §2.30 — pure algebra tests for <see cref="DockLayoutMutator"/>.
/// The mutator is a set of static functions over the immutable DockNode
/// algebra; these tests exercise the algorithmic branches without spinning
/// up a host.
/// </summary>
/// <remarks>
/// Joins the <c>DockingGlobals</c> collection because several tests below
/// mutate process-global state in <see cref="PreviousContainerTracker"/>
/// via <c>Set</c> / <c>ClearAll</c>; running concurrently with other docking
/// suites would cross-contaminate the tracker.
/// </remarks>
[Collection("DockingGlobals")]
public sealed class DockLayoutMutatorTests
{
    private static Document Doc(string key, string title = "T") =>
        new() { Title = title, Key = key };

    // ── StripContent ────────────────────────────────────────────────────

    [Fact]
    public void StripContent_NullInput_ReturnsNull()
    {
        Assert.Null(DockLayoutMutator.StripContent(null));
    }

    [Fact]
    public void StripContent_LeafWithoutKey_Throws()
    {
        var bareLeaf = new DockableContent(string.Empty);
        var ex = Assert.Throws<InvalidOperationException>(
            () => DockLayoutMutator.StripContent(bareLeaf));
        Assert.Contains("Key", ex.Message);
    }

    [Fact]
    public void StripContent_Leaf_KeepsKey_DropsTitleAndContent()
    {
        var leaf = new Document { Title = "Original", Key = "k1" };
        var stripped = (DockableContent)DockLayoutMutator.StripContent(leaf)!;
        Assert.Equal("k1", stripped.Key);
        Assert.Equal(string.Empty, stripped.Title);
    }

    [Fact]
    public void StripContent_TabGroup_StripsEveryDocument()
    {
        var grp = new DockTabGroup(new DockableContent[]
        {
            Doc("a", "Alpha"),
            Doc("b", "Beta"),
        }, SelectedIndex: 1);
        var stripped = (DockTabGroup)DockLayoutMutator.StripContent(grp)!;
        Assert.Equal(2, stripped.Documents.Count);
        Assert.All(stripped.Documents, d => Assert.Equal(string.Empty, d.Title));
        // SelectedIndex is preserved (it's structural, not content).
        Assert.Equal(1, stripped.SelectedIndex);
    }

    [Fact]
    public void StripContent_Split_StripsRecursively()
    {
        var split = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            Doc("a"),
            new DockTabGroup(new DockableContent[] { Doc("b"), Doc("c") }),
        });
        var stripped = (DockSplit)DockLayoutMutator.StripContent(split)!;
        Assert.Equal(2, stripped.Children.Count);
        Assert.IsType<DockableContent>(stripped.Children[0]);
        Assert.IsType<DockTabGroup>(stripped.Children[1]);
    }

    // ── ResolveContents ─────────────────────────────────────────────────

    [Fact]
    public void ResolveContents_NullShape_ReturnsNull()
    {
        Assert.Null(DockLayoutMutator.ResolveContents(null, Doc("a")));
    }

    [Fact]
    public void ResolveContents_NullSource_ReturnsShapeUnchanged()
    {
        var shape = Doc("only");
        Assert.Same(shape, DockLayoutMutator.ResolveContents(shape, source: null));
    }

    [Fact]
    public void ResolveContents_LeafMatch_SubstitutesFromSource()
    {
        var freshContent = new Document { Title = "Fresh", Key = "k", Content = null! };
        var shape = new DockableContent(string.Empty, Key: "k");
        var resolved = (DockableContent)DockLayoutMutator.ResolveContents(shape, freshContent)!;
        Assert.Same(freshContent, resolved);
        Assert.Equal("Fresh", resolved.Title);
    }

    [Fact]
    public void ResolveContents_LeafMiss_KeepsShapeLeaf()
    {
        var shape = new DockableContent(string.Empty, Key: "orphan");
        var source = Doc("different");
        var resolved = (DockableContent)DockLayoutMutator.ResolveContents(shape, source)!;
        Assert.Equal("orphan", resolved.Key);
        Assert.Equal(string.Empty, resolved.Title); // still the shape's empty title
    }

    [Fact]
    public void ResolveContents_NestedTree_PreservesSplitOrientation()
    {
        var freshA = new Document { Title = "A!", Key = "a" };
        var freshB = new Document { Title = "B!", Key = "b" };
        var sourceTree = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            freshA, freshB,
        });
        var shape = new DockSplit(Orientation.Vertical, new DockNode[]
        {
            new DockableContent(string.Empty, Key: "a"),
            new DockableContent(string.Empty, Key: "b"),
        });
        var resolved = (DockSplit)DockLayoutMutator.ResolveContents(shape, sourceTree)!;
        // Shape's orientation wins (the user's drag is a structural override).
        Assert.Equal(Orientation.Vertical, resolved.Orientation);
        // But leaves are substituted from the source.
        Assert.Same(freshA, resolved.Children[0]);
        Assert.Same(freshB, resolved.Children[1]);
    }

    [Fact]
    public void ResolveContents_WithPrebuiltIndex_UsesIndexLookup()
    {
        var freshA = new Document { Title = "A!", Key = "a" };
        var index = new Dictionary<object, DockableContent> { ["a"] = freshA };
        var shape = new DockableContent(string.Empty, Key: "a");
        var resolved = (DockableContent)DockLayoutMutator.ResolveContents(shape, index)!;
        Assert.Same(freshA, resolved);
    }

    // ── IndexLeavesInto ─────────────────────────────────────────────────

    [Fact]
    public void IndexLeavesInto_NullTree_NoOp()
    {
        var idx = new Dictionary<object, DockableContent>();
        DockLayoutMutator.IndexLeavesInto(null, idx);
        Assert.Empty(idx);
    }

    [Fact]
    public void IndexLeavesInto_LeavesWithNullKey_AreSkipped()
    {
        var idx = new Dictionary<object, DockableContent>();
        var grp = new DockTabGroup(new DockableContent[]
        {
            new DockableContent("A"), // no Key
            Doc("b"),
        });
        DockLayoutMutator.IndexLeavesInto(grp, idx);
        Assert.Single(idx);
        Assert.True(idx.ContainsKey("b"));
    }

    [Fact]
    public void IndexLeavesInto_NestedSplits_RecursesIntoEverything()
    {
        var idx = new Dictionary<object, DockableContent>();
        var tree = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            Doc("a"),
            new DockTabGroup(new DockableContent[] { Doc("b"), Doc("c") }),
            new DockSplit(Orientation.Vertical, new DockNode[] { Doc("d") }),
        });
        DockLayoutMutator.IndexLeavesInto(tree, idx);
        Assert.Equal(new[] { "a", "b", "c", "d" }, idx.Keys.OrderBy(k => (string)k));
    }

    // ── RemovePane + InsertPaneAtTarget + MovePaneToTarget ──────────────

    [Fact]
    public void RemovePane_LeafIsRoot_ReturnsNull()
    {
        var leaf = Doc("a");
        var (root, found) = DockLayoutMutator.RemovePane(leaf, leaf);
        Assert.True(found);
        Assert.Null(root);
    }

    [Fact]
    public void RemovePane_NotFound_ReturnsOriginal()
    {
        var leaf = Doc("a");
        var (root, found) = DockLayoutMutator.RemovePane(leaf, Doc("b"));
        Assert.False(found);
        Assert.Same(leaf, root);
    }

    [Fact]
    public void RemovePane_TabGroup_ShrinksAndClampsSelectedIndex()
    {
        var a = Doc("a");
        var b = Doc("b");
        var c = Doc("c");
        var grp = new DockTabGroup(new DockableContent[] { a, b, c }, SelectedIndex: 2);
        var (root, found) = DockLayoutMutator.RemovePane(grp, c);
        Assert.True(found);
        var newGrp = Assert.IsType<DockTabGroup>(root);
        Assert.Equal(2, newGrp.Documents.Count);
        Assert.Equal(1, newGrp.SelectedIndex); // clamped to last
    }

    [Fact]
    public void RemovePane_LastDocInTabGroup_CollapsesGroup()
    {
        var a = Doc("a");
        var grp = new DockTabGroup(new DockableContent[] { a });
        var (root, found) = DockLayoutMutator.RemovePane(grp, a);
        Assert.True(found);
        Assert.Null(root);
    }

    [Fact]
    public void RemovePane_Split_CollapsingToOneChild_UnwrapsSplit()
    {
        var a = Doc("a");
        var b = Doc("b");
        var split = new DockSplit(Orientation.Horizontal, new DockNode[] { a, b });
        var (root, found) = DockLayoutMutator.RemovePane(split, b);
        Assert.True(found);
        Assert.Same(a, root); // split unwrapped to bare leaf
    }

    [Fact]
    public void InsertPaneAtTarget_NullRoot_ReturnsTabGroupWrapper()
    {
        var p = Doc("p");
        var result = DockLayoutMutator.InsertPaneAtTarget(null, p, DockTarget.Center);
        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Same(p, grp.Documents[0]);
    }

    [Theory]
    [InlineData(DockTarget.SplitLeft, Orientation.Horizontal, true)]
    [InlineData(DockTarget.SplitRight, Orientation.Horizontal, false)]
    [InlineData(DockTarget.SplitTop, Orientation.Vertical, true)]
    [InlineData(DockTarget.SplitBottom, Orientation.Vertical, false)]
    [InlineData(DockTarget.DockLeft, Orientation.Horizontal, true)]
    [InlineData(DockTarget.DockRight, Orientation.Horizontal, false)]
    [InlineData(DockTarget.DockTop, Orientation.Vertical, true)]
    [InlineData(DockTarget.DockBottom, Orientation.Vertical, false)]
    public void InsertPaneAtTarget_SplitOrEdge_WrapsRootInNewSplit(
        DockTarget target, Orientation expectedOrientation, bool insertedFirst)
    {
        var rootDoc = Doc("root");
        var newDoc = Doc("new");
        var result = DockLayoutMutator.InsertPaneAtTarget(rootDoc, newDoc, target);
        var split = Assert.IsType<DockSplit>(result);
        Assert.Equal(expectedOrientation, split.Orientation);
        Assert.Equal(2, split.Children.Count);
        // Inserted-first means the new pane's tab-group precedes the original.
        if (insertedFirst)
        {
            var grp = Assert.IsType<DockTabGroup>(split.Children[0]);
            Assert.Same(newDoc, grp.Documents[0]);
        }
        else
        {
            var grp = Assert.IsType<DockTabGroup>(split.Children[1]);
            Assert.Same(newDoc, grp.Documents[0]);
        }
    }

    [Fact]
    public void InsertPaneAtTarget_Center_FoldsIntoExistingTabGroup()
    {
        var a = Doc("a");
        var b = Doc("b");
        var grp = new DockTabGroup(new DockableContent[] { a });
        var result = DockLayoutMutator.InsertPaneAtTarget(grp, b, DockTarget.Center);
        var newGrp = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, newGrp.Documents.Count);
        Assert.Same(b, newGrp.Documents[1]);
        Assert.Equal(1, newGrp.SelectedIndex);
    }

    [Fact]
    public void InsertPaneAtTarget_CenterOnLeaf_PromotesLeafIntoTabGroup()
    {
        var a = Doc("a");
        var b = Doc("b");
        var result = DockLayoutMutator.InsertPaneAtTarget(a, b, DockTarget.Center);
        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, grp.Documents.Count);
        Assert.Same(a, grp.Documents[0]);
        Assert.Same(b, grp.Documents[1]);
    }

    [Fact]
    public void InsertPaneAtTarget_CenterOnSplit_FoldsIntoLeftmostBranch()
    {
        var a = Doc("a");
        var b = Doc("b");
        var c = Doc("c");
        var split = new DockSplit(Orientation.Horizontal,
            new DockNode[] { new DockTabGroup(new DockableContent[] { a }), b });
        var result = DockLayoutMutator.InsertPaneAtTarget(split, c, DockTarget.Center);
        var newSplit = Assert.IsType<DockSplit>(result);
        var newGroup = Assert.IsType<DockTabGroup>(newSplit.Children[0]);
        Assert.Equal(2, newGroup.Documents.Count);
        Assert.Same(c, newGroup.Documents[1]);
    }

    [Fact]
    public void MovePaneToTarget_NotFound_ReturnsOriginalRoot()
    {
        var grp = new DockTabGroup(new DockableContent[] { Doc("a") });
        var stranger = Doc("stranger");
        var result = DockLayoutMutator.MovePaneToTarget(grp, stranger, DockTarget.Center);
        Assert.Same(grp, result);
    }

    [Fact]
    public void MovePaneToTarget_FoundAndMoved_PaneAppearsOnce()
    {
        var a = Doc("a");
        var b = Doc("b");
        var split = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new DockableContent[] { a }),
            new DockTabGroup(new DockableContent[] { b }),
        });
        // Move a to the bottom of the root.
        var moved = DockLayoutMutator.MovePaneToTarget(split, a, DockTarget.SplitBottom);
        var newSplit = Assert.IsType<DockSplit>(moved);
        Assert.Equal(Orientation.Vertical, newSplit.Orientation);
        // Walk every leaf in the moved tree and assert pane 'a' appears
        // exactly once — guarding against the "removed from one branch but
        // also appended elsewhere → duplicated" regression. A flat List
        // (NOT a dict) is required because dict keys would silently
        // deduplicate the very case we're trying to detect.
        var leaves = new List<DockableContent>();
        Walk(moved!, leaves);
        Assert.Equal(1, leaves.Count(l => (string?)l.Key == "a"));
        Assert.Equal(1, leaves.Count(l => (string?)l.Key == "b"));

        static void Walk(DockNode node, List<DockableContent> acc)
        {
            switch (node)
            {
                case DockableContent leaf: acc.Add(leaf); break;
                case DockTabGroup g: foreach (var d in g.Documents) acc.Add(d); break;
                case DockSplit s: foreach (var c in s.Children) Walk(c, acc); break;
            }
        }
    }

    // ── FindContainer ───────────────────────────────────────────────────

    [Fact]
    public void FindContainer_LeafRoot_ReturnsLeaf()
    {
        var a = Doc("a");
        Assert.Same(a, DockLayoutMutator.FindContainer(a, a));
    }

    [Fact]
    public void FindContainer_NotFound_ReturnsNull()
    {
        var a = Doc("a");
        var b = Doc("b");
        var grp = new DockTabGroup(new DockableContent[] { a });
        Assert.Null(DockLayoutMutator.FindContainer(grp, b));
    }

    [Fact]
    public void FindContainer_InsideTabGroup_ReturnsGroup()
    {
        var a = Doc("a");
        var b = Doc("b");
        var grp = new DockTabGroup(new DockableContent[] { a, b });
        Assert.Same(grp, DockLayoutMutator.FindContainer(grp, b));
    }

    [Fact]
    public void FindContainer_NullRoot_ReturnsNull()
    {
        Assert.Null(DockLayoutMutator.FindContainer(null, Doc("a")));
    }

    // ── InsertPaneIntoGroup / InsertPaneRelativeToGroup ────────────────

    [Fact]
    public void InsertPaneIntoGroup_NullRoot_WrapsPane()
    {
        var p = Doc("p");
        var target = new DockTabGroup(new DockableContent[] { Doc("ignored") });
        var result = DockLayoutMutator.InsertPaneIntoGroup(null, p, target);
        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Same(p, grp.Documents[0]);
    }

    [Fact]
    public void InsertPaneIntoGroup_UnresolvableTarget_FallsBackToCenter()
    {
        // root has 'a'; we point the target at a brand-new group that
        // shares no content. The mutator falls back to root-level Center.
        var rootDoc = Doc("a");
        var foreignGroup = new DockTabGroup(new DockableContent[] { Doc("z") });
        var p = Doc("p");
        var result = DockLayoutMutator.InsertPaneIntoGroup(rootDoc, p, foreignGroup);
        // Fallback wraps root as a tab group with both panes.
        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, grp.Documents.Count);
    }

    [Fact]
    public void InsertPaneIntoGroup_ResolvableTarget_AppendsPane()
    {
        var a = Doc("a");
        var targetGroup = new DockTabGroup(new DockableContent[] { a });
        var p = Doc("p");
        var result = DockLayoutMutator.InsertPaneIntoGroup(targetGroup, p, targetGroup);
        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, grp.Documents.Count);
        Assert.Same(p, grp.Documents[1]);
    }

    // ── ShowFromHistory ─────────────────────────────────────────────────

    [Fact]
    public void ShowFromHistory_NoHistory_FallsBackToTarget()
    {
        PreviousContainerTracker.ClearAll();
        var root = new DockTabGroup(new DockableContent[] { Doc("root") });
        var p = Doc("p");
        var result = DockLayoutMutator.ShowFromHistory(root, p, fallbackTarget: DockTarget.SplitRight);
        Assert.IsType<DockSplit>(result);
    }

    [Fact]
    public void ShowFromHistory_RememberedGroupGone_FallsBack()
    {
        PreviousContainerTracker.ClearAll();
        var pane = Doc("pane");
        // Record a brand-new group instance as the "previous container".
        var disconnected = new DockTabGroup(new DockableContent[] { Doc("foreign") });
        PreviousContainerTracker.Set(pane, disconnected);

        // Pass an unrelated root — the remembered group isn't in it.
        var root = new DockTabGroup(new DockableContent[] { Doc("root") });
        var result = DockLayoutMutator.ShowFromHistory(root, pane, fallbackTarget: DockTarget.Center);
        // Fallback Center folds the pane into the root group.
        var grp = Assert.IsType<DockTabGroup>(result);
        Assert.Equal(2, grp.Documents.Count);
        PreviousContainerTracker.ClearAll();
    }

    // ── PreviousContainerTracker ────────────────────────────────────────

    [Fact]
    public void PreviousContainerTracker_SetAndGet_RoundTrips()
    {
        PreviousContainerTracker.ClearAll();
        var pane = Doc("pane");
        var container = new DockTabGroup(new DockableContent[] { pane });
        PreviousContainerTracker.Set(pane, container);
        Assert.Same(container, PreviousContainerTracker.GetPrevious(pane));

        // Re-Set replaces.
        var newContainer = new DockTabGroup(new DockableContent[] { pane });
        PreviousContainerTracker.Set(pane, newContainer);
        Assert.Same(newContainer, PreviousContainerTracker.GetPrevious(pane));

        // Clear.
        PreviousContainerTracker.Clear(pane);
        Assert.Null(PreviousContainerTracker.GetPrevious(pane));
        PreviousContainerTracker.ClearAll();
    }

    [Fact]
    public void PreviousContainerTracker_GetPrevious_NeverSet_ReturnsNull()
    {
        PreviousContainerTracker.ClearAll();
        var pane = Doc("never");
        Assert.Null(PreviousContainerTracker.GetPrevious(pane));
    }

    [Fact]
    public void PreviousContainerTracker_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.Set(null!, new DockTabGroup(Array.Empty<DockableContent>())));
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.Set(Doc("a"), null!));
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.GetPrevious(null!));
        Assert.Throws<ArgumentNullException>(() => PreviousContainerTracker.Clear(null!));
    }
}
