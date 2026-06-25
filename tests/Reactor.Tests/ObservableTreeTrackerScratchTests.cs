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
/// a node dropped from the graph stops re-rendering. A final test drives the
/// re-entrancy guard itself — a side-effecting getter that fires PropertyChanged
/// mid-walk re-enters the sync, which must fall back to fresh scratch so the
/// outer walk's reused containers stay intact.
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

    // A node whose property getter raises PropertyChanged on itself while it is
    // being read during the tracker's Walk. Once the node is subscribed, that
    // synchronous raise re-enters SyncSubscriptions on the same stack — the
    // re-entrancy guard (#5) must then allocate fresh scratch instead of clearing
    // the containers the outer sync is mid-walk over.
    private sealed class ReentrantRoot : INotifyPropertyChanged
    {
        private ReentrantLeaf _leaf = new();
        public ReentrantLeaf Leaf => _leaf;

        private int _reentryBudget;
        public int RemainingReentryBudget => _reentryBudget;
        public void ArmGetterReentry(int n) => _reentryBudget = n;

        // Side-effecting getter: when armed, raises PropertyChanged on THIS node
        // as it is read during the Walk, re-entering the sync. The budget bounds
        // the recursion so the test can't spin.
        public object? Probe
        {
            get
            {
                if (_reentryBudget > 0)
                {
                    _reentryBudget--;
                    Raise(nameof(Probe));
                }
                return null;
            }
        }

        public void Poke() => Raise(nameof(Probe));
        public void ReplaceLeaf(ReentrantLeaf leaf) { _leaf = leaf; Raise(nameof(Leaf)); }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private sealed class ReentrantLeaf : INotifyPropertyChanged
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

    [Fact]
    public void ReentrantSync_FromSideEffectingGetter_KeepsSubscriptionsIntact()
    {
        var ctx = new RenderContext();
        var root = new ReentrantRoot();
        var c = new[] { 0 };

        // First two renders subscribe root + its leaf. The Probe getter is read
        // during these walks too, but it isn't armed, so nothing re-enters yet.
        Render(ctx, c, root);
        Render(ctx, c, root);

        // Arm the getter to fire ONCE during the next walk, then trigger a sync.
        // The outer SyncSubscriptions walks root, reads the armed Probe getter,
        // which raises PropertyChanged on the already-subscribed root and
        // re-enters SyncSubscriptions on the same stack (the #5 guard branch).
        root.ArmGetterReentry(1);
        int before = c[0];
        root.Poke();

        // The re-entry actually happened (the armed getter consumed its budget)
        // and the tracker re-rendered without throwing or recursing unbounded.
        Assert.Equal(0, root.RemainingReentryBudget);
        Assert.True(c[0] > before, "poke should have re-rendered");

        // The outer sync's reused scratch was not corrupted by the re-entrant
        // sync: the deep leaf is still subscribed and keeps firing.
        int afterPoke = c[0];
        root.Leaf.Value = "after-reentry";
        Assert.True(c[0] > afterPoke, "leaf must remain tracked after a re-entrant sync");

        // And a normal replacement after the re-entry still tracks/untracks
        // correctly — proving the scratch fields were left in a clean state.
        var oldLeaf = root.Leaf;
        root.ReplaceLeaf(new ReentrantLeaf());
        int afterReplace = c[0];

        oldLeaf.Value = "stale";
        Assert.Equal(afterReplace, c[0]); // old leaf untracked

        root.Leaf.Value = "fresh";
        Assert.True(c[0] > afterReplace, "replacement leaf tracked");
    }
}
