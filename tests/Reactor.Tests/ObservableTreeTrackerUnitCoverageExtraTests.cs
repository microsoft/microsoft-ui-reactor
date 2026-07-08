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
    public void SyncSubscriptions_ValueTypeChangeOnNestedNode_RequestsRerender()
    {
        int renders = 0;
        using var tracker = new ObservableTreeTracker(() => renders++);

        var root = new Leaf();
        var child = new Leaf();
        root.Next = child;
        tracker.SyncSubscriptions(root);

        renders = 0;
        child.Count = 5; // value-type property -> rerender then early-return arm
        Assert.Equal(1, renders);
    }

    [Fact]
    public void SyncSubscriptions_EmptyPropertyName_RequestsRerenderThenReturns()
    {
        int renders = 0;
        using var tracker = new ObservableTreeTracker(() => renders++);

        var root = new Leaf();
        tracker.SyncSubscriptions(root);

        renders = 0;
        root.Raise(string.Empty); // null/empty name -> rerender, then the guard returns
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
