using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Unit coverage for the pure-managed reflection walk in
/// <see cref="ObservableTreeTracker"/> — candidate-property discovery, the
/// nested <c>PropertyChanged</c> dispatch arms, and the defensive catch around a
/// throwing property getter. Runs headless (no DispatcherQueue), so the tracker
/// syncs inline on the calling thread.
/// </summary>
public class ObservableTreeTrackerUnitCoverageExtraTests
{
    private sealed class Leaf : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _count;
        public int Count { get => _count; set { _count = value; Raise(nameof(Count)); } }

        private Leaf? _next;
        public Leaf? Next { get => _next; set { _next = value; Raise(nameof(Next)); } }

        public string Name { get; set; } = string.Empty;

        // Grow the graph WITHOUT raising PropertyChanged, so the tracker's subscribed
        // set only changes if a re-sync actually runs.
        public void AttachNextSilently(Leaf next) => _next = next;

        public void Raise(string? propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class ThrowingRoot : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // A reference-typed INPC candidate whose getter throws — exercises the
        // per-property try/catch inside Walk.
        public INotifyPropertyChanged Bad => throw new InvalidOperationException("boom");

        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Bad"));
    }

    [Fact]
    public void GetInpcCandidateProperties_KeepsReadableReferenceProps_DropsValueTypes_AndCaches()
    {
        var props = ObservableTreeTracker.GetInpcCandidateProperties(typeof(Leaf));
        var names = props.Select(p => p.Name).ToHashSet();

        Assert.Contains("Next", names); // reference-typed -> candidate
        Assert.Contains("Name", names); // reference-typed -> candidate
        Assert.DoesNotContain("Count", names); // value-typed -> filtered out

        // Second lookup is served from the per-type cache (same array instance).
        Assert.Same(props, ObservableTreeTracker.GetInpcCandidateProperties(typeof(Leaf)));
    }

    [Fact]
    public void SyncSubscriptions_ValueTypeChange_RerendersButDoesNotResubscribe()
    {
        int renders = 0;
        using var tracker = new ObservableTreeTracker(() => renders++);

        var root = new Leaf();
        tracker.SyncSubscriptions(root); // subscribes root only

        // Silently grow the graph: a re-sync (if one ran) would now subscribe child.
        var child = new Leaf();
        root.AttachNextSilently(child);

        renders = 0;
        root.Count = 5; // value-type change fires the unconditional rerender...
        Assert.Equal(1, renders);

        // ...but the value-type guard returns before re-syncing, so `child` was never
        // subscribed. Removing that guard would resync and subscribe child, making the
        // next mutation rerender — so this is the oracle for the guard, not the count.
        renders = 0;
        child.Count = 7;
        Assert.Equal(0, renders);
    }

    [Fact]
    public void SyncSubscriptions_EmptyPropertyName_StillFiresUnconditionalRerender()
    {
        int renders = 0;
        using var tracker = new ObservableTreeTracker(() => renders++);

        var root = new Leaf();
        tracker.SyncSubscriptions(root);

        // A PropertyChanged with no name still fires the unconditional rerender, which
        // runs before the name/type guards (removing that _requestRerender call would
        // make this fail). This covers the empty-name early-return line; the guard
        // itself is a fast-path the later null-property check also backstops, so its
        // return is not independently observable — hence no oracle is claimed for it.
        renders = 0;
        root.Raise(string.Empty);
        Assert.Equal(1, renders);
    }

    [Fact]
    public void SyncSubscriptions_ReferenceTypeChange_ResyncsGraph()
    {
        int renders = 0;
        using var tracker = new ObservableTreeTracker(() => renders++);

        var root = new Leaf();
        tracker.SyncSubscriptions(root);

        // Attach a fresh nested node, then announce the reference-typed change.
        // The handler walks past the value-type guard and re-syncs from the root,
        // which subscribes the newly-reachable node.
        var child = new Leaf();
        root.Next = child;   // raises Next; handler re-syncs and picks up child
        renders = 0;

        child.Count = 9;     // only fires because child is now subscribed
        Assert.Equal(1, renders);
    }

    [Fact]
    public void SyncSubscriptions_ThrowingPropertyGetter_IsSwallowed()
    {
        using var tracker = new ObservableTreeTracker(() => { });

        // Walk must not propagate the getter's exception.
        Assert.Null(Record.Exception(() => tracker.SyncSubscriptions(new ThrowingRoot())));
    }

    [Fact]
    public void Dispose_UnsubscribesSoLaterChangesAreIgnored()
    {
        int renders = 0;
        var tracker = new ObservableTreeTracker(() => renders++);

        var root = new Leaf();
        tracker.SyncSubscriptions(root);
        tracker.Dispose();

        renders = 0;
        root.Count = 1; // no live subscription after Dispose
        Assert.Equal(0, renders);
    }
}
