using System.ComponentModel;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Correctness tests for the ObservableTreeTracker scratch-field reuse (#5) and
/// the cached per-node delegate (#6). The tracker promotes its desiredSet /
/// visiting / toRemove containers to reused instance fields (cleared before and
/// after each top-level sync, with a re-entrancy guard). These tests confirm the
/// reuse does not leak state across PropertyChanged fires: repeated fires keep
/// firing, replaced nested nodes are untracked and the replacements tracked, and
/// a node dropped from the graph stops re-rendering.
/// </summary>
public class ObservableTreeTrackerScratchTests
{
    private sealed class Leaf : INotifyPropertyChanged
    {
        private string _value = "";
        public string Value
        {
            get => _value;
            set { if (_value != value) { _value = value; Raise(nameof(Value)); } }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private sealed class Branch : INotifyPropertyChanged
    {
        private Leaf _leaf = new();
        public Leaf Leaf
        {
            get => _leaf;
            set { if (_leaf != value) { _leaf = value; Raise(nameof(Leaf)); } }
        }
        private string _label = "";
        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value; Raise(nameof(Label)); } }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private sealed class Root : INotifyPropertyChanged
    {
        private Branch _branch = new();
        public Branch Branch
        {
            get => _branch;
            set { if (_branch != value) { _branch = value; Raise(nameof(Branch)); } }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private static void Render(RenderContext ctx, int[] counter, INotifyPropertyChanged source)
    {
        ctx.BeginRender(() => counter[0]++);
        ctx.UseObservableTree(source);
        ctx.FlushEffects();
    }

    [Fact]
    public void RepeatedFires_OnDeepNode_KeepRerendering_WithReusedScratch()
    {
        var ctx = new RenderContext();
        var root = new Root(); // Branch → Leaf default-constructed
        var c = new[] { 0 };

        Render(ctx, c, root);
        Render(ctx, c, root);

        // Each fire re-runs SyncSubscriptions, which clears + reuses the same
        // scratch HashSets/List. Losing or leaking state would drop the
        // subscription and stop the re-renders.
        for (int i = 0; i < 12; i++)
        {
            int before = c[0];
            root.Branch.Leaf.Value = $"v{i}";
            Assert.True(c[0] > before, $"fire {i} should have re-rendered");
        }
    }

    [Fact]
    public void ReplacedSubtree_UntracksOld_TracksNew()
    {
        var ctx = new RenderContext();
        var root = new Root();
        var c = new[] { 0 };

        Render(ctx, c, root);
        Render(ctx, c, root);

        var oldBranch = root.Branch;
        var oldLeaf = oldBranch.Leaf;

        // Replace the whole branch subtree — the re-sync must move every old
        // node into the (reused) toRemove scratch and subscribe the new ones.
        var newBranch = new Branch { Leaf = new Leaf() };
        root.Branch = newBranch;
        Render(ctx, c, root);

        int afterReplace = c[0];

        // Old nodes are unsubscribed — no re-render.
        oldLeaf.Value = "stale";
        oldBranch.Label = "stale";
        Assert.Equal(afterReplace, c[0]);

        // New nodes are subscribed — re-render fires.
        newBranch.Label = "fresh-label";
        Assert.True(c[0] > afterReplace, "new branch should be tracked");

        int afterBranch = c[0];
        newBranch.Leaf.Value = "fresh-leaf";
        Assert.True(c[0] > afterBranch, "new leaf should be tracked");
    }

    [Fact]
    public void AlternatingReplacements_DoNotLeakStaleSubscriptions()
    {
        var ctx = new RenderContext();
        var root = new Root();
        var c = new[] { 0 };

        Render(ctx, c, root);
        Render(ctx, c, root);

        Leaf? previousLeaf = null;

        // Swap the leaf several times. After each swap the prior leaf must be
        // fully untracked (no phantom re-render) while the current one fires.
        for (int i = 0; i < 5; i++)
        {
            var freshLeaf = new Leaf();
            root.Branch.Leaf = freshLeaf;
            Render(ctx, c, root);

            int baseline = c[0];

            if (previousLeaf is not null)
            {
                previousLeaf.Value = $"stale{i}";
                Assert.Equal(baseline, c[0]); // prior leaf untracked
            }

            freshLeaf.Value = $"live{i}";
            Assert.True(c[0] > baseline, $"current leaf {i} should be tracked");

            previousLeaf = freshLeaf;
        }
    }
}
