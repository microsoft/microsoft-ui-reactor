using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// The child-skip arms in <see cref="ChildReconciler"/> decline for children that lead to a
/// component which updated its own state; <see cref="DirtyChildIndices"/> names those children.
/// These tests pin the cost side of that contract, which is reachable headless: resolved
/// indices answer without reading a control, and when nothing in the pass self-triggered the
/// skip floor reads no control and looks up no index. Whether the update actually renders needs
/// live controls (component wrappers, the visual tree walk that builds the dirty path), so that
/// half is covered by <c>SelfTriggeredReusedChildFixtures</c> in the selftest host.
/// </summary>
public class DirtyChildIndicesTests
{
    private static readonly Action NoOp = () => { };

    /// <summary>
    /// Counts control reads and index lookups. Structural edits are not expected by any test
    /// here, so they throw.
    /// </summary>
    private sealed class CountingChildCollection(int count, bool throwOnRead) : IChildCollection
    {
        public int Reads { get; private set; }
        public int IndexLookups { get; private set; }

        public int Count => count;

        public UIElement Get(int index)
        {
            Reads++;
            if (throwOnRead)
                throw new InvalidOperationException($"Get({index}) must not be called on the skip floor.");
            return null!;
        }

        public int IndexOf(UIElement element)
        {
            IndexLookups++;
            return -1;
        }

        public void Insert(int index, UIElement element) => throw new InvalidOperationException("unexpected Insert");
        public void RemoveAt(int index) => throw new InvalidOperationException("unexpected RemoveAt");
        public void Move(int oldIndex, int newIndex) => throw new InvalidOperationException("unexpected Move");
        public void Replace(int index, UIElement element) => throw new InvalidOperationException("unexpected Replace");
    }

    private static bool Contains(DirtyChildIndices dirty, int index, IChildCollection children)
    {
        UIElement? control = null;
        return dirty.Contains(index, children, new Reconciler(), ref control);
    }

    [Fact]
    public void Default_Names_No_Child_And_Reads_Nothing()
    {
        var children = new CountingChildCollection(4, throwOnRead: true);
        var dirty = default(DirtyChildIndices);

        Assert.True(dirty.IsEmpty);
        for (int i = 0; i < 4; i++)
            Assert.False(Contains(dirty, i, children));
        Assert.Equal(0, children.Reads);
        Assert.Equal("None", dirty.ToString());
    }

    [Fact]
    public void A_Resolved_Index_Matches_Only_That_Child_Without_Reading_Controls()
    {
        var children = new CountingChildCollection(4, throwOnRead: true);
        var dirty = DirtyChildIndices.At(2);

        Assert.False(dirty.IsEmpty);
        Assert.Equal(new[] { false, false, true, false }, Enumerable.Range(0, 4).Select(i => Contains(dirty, i, children)));
        Assert.Equal(0, children.Reads);
        Assert.Equal("At(2)", dirty.ToString());
    }

    [Fact]
    public void Index_Zero_Is_A_Real_Index_Not_The_Default()
    {
        var children = new CountingChildCollection(2, throwOnRead: true);
        var dirty = DirtyChildIndices.At(0);

        Assert.False(dirty.IsEmpty);
        Assert.True(Contains(dirty, 0, children));
        Assert.False(Contains(dirty, 1, children));
    }

    [Fact]
    public void Several_Resolved_Indices_Each_Match_Without_Reading_Controls()
    {
        var children = new CountingChildCollection(5, throwOnRead: true);
        var dirty = DirtyChildIndices.AtAll(new[] { 1, 3 });

        Assert.False(dirty.IsEmpty);
        Assert.Equal(new[] { false, true, false, true, false }, Enumerable.Range(0, 5).Select(i => Contains(dirty, i, children)));
        Assert.Equal(0, children.Reads);
        Assert.Equal("AtAll(1,3)", dirty.ToString());
    }

    [Fact]
    public void The_Probe_Fallback_Reads_Each_InRange_Child_And_Nothing_Else()
    {
        // This pins the fallback's cost only. Headless code has no dirty path to put a child on,
        // so the positive half (a dirty child behind the fallback is descended into) is the
        // selftest SelfTrigReuse_InnerPanelCollectionUsesFallback.
        var children = new CountingChildCollection(3, throwOnRead: false);
        var dirty = DirtyChildIndices.ProbeEachChild;

        Assert.False(dirty.IsEmpty);
        // A fresh reconciler has no dirty path, so no control is on it.
        Assert.False(Contains(dirty, 1, children));
        Assert.Equal(1, children.Reads);
        Assert.False(Contains(dirty, 3, children));
        Assert.False(Contains(dirty, -1, children));
        Assert.Equal(1, children.Reads);
    }

    [Fact]
    public void Nothing_SelfTriggered_Resolves_To_None_Without_Touching_The_Collection()
    {
        // With no dirty path the resolution must return before it reads the collection. (Headless
        // code cannot build a UIElement to pass as the parent, so the null-parent shape stands in.)
        var children = new CountingChildCollection(4, throwOnRead: true);

        var dirty = new Reconciler().ResolveDirtyChildIndices(parentControl: null, children);

        Assert.True(dirty.IsEmpty);
        Assert.Equal(0, children.Reads);
        Assert.Equal(0, children.IndexLookups);
    }

    [Theory]
    [InlineData(false)] // positional full walk
    [InlineData(true)]  // keyed prefix
    public void Reused_Children_Still_Skip_Without_Reading_Any_Control(bool keyed)
    {
        // The shape behind the bug — the same element instances re-emitted — with nothing
        // self-triggered. The fix must leave this skip floor exactly as cheap as before: no
        // control read and no index lookup for any child.
        var oldChildren = new Element[6];
        for (int i = 0; i < oldChildren.Length; i++)
            oldChildren[i] = keyed
                ? new TextBlockElement($"cell-{i}") { Key = $"k{i}" }
                : new TextBlockElement($"cell-{i}");
        var newChildren = (Element[])oldChildren.Clone();
        var children = new CountingChildCollection(oldChildren.Length, throwOnRead: true);
        var reconciler = new Reconciler();

        ChildReconciler.Reconcile(oldChildren, newChildren, children, reconciler, NoOp);

        Assert.Equal(0, children.Reads);
        Assert.Equal(0, children.IndexLookups);
        Assert.Equal(oldChildren.Length, reconciler.DebugElementsSkipped);
    }
}
