using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Maintains the current context values during the reconciler's tree traversal.
/// Pushed on entering an element with ContextValues, popped on leaving.
/// Most recent entry for a given context wins (shadowing).
/// </summary>
internal sealed class ContextScope
{
    private List<(ContextBase Context, object? Value)> _stack = new();
    private long _version;

    internal void Push(IReadOnlyDictionary<ContextBase, object?> values)
    {
        foreach (var (ctx, val) in values)
            _stack.Add((ctx, val));
        if (values.Count > 0) _everHadValues = true;
        _version++;
    }

    internal void Pop(int count)
    {
        _stack.RemoveRange(_stack.Count - count, count);
        _version++;
    }

    internal T Read<T>(Context<T> context)
    {
        // Walk backward (most recent first) for shadowing
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i].Context, context))
                return (T)_stack[i].Value!;
        }
        return context.DefaultValue;
    }

    /// <summary>
    /// Non-generic read for memo change detection. Returns the boxed value
    /// or the boxed default if no provider exists.
    /// </summary>
    internal object? Read(ContextBase context)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i].Context, context))
                return _stack[i].Value;
        }
        return context.DefaultValueBoxed;
    }

    internal bool HasActiveValues => _stack.Count > 0 || (_replayDepth > 0 && _everHadValues);

    // A replayed snapshot (Replace) can be EMPTY because the host's last provider was
    // removed, while the row being realized was built under that provider. With an empty
    // stack HasActiveValues would disable every context skip check, so a reused row that
    // is shallow-equal would keep the removed value. While a replay is active, keep the
    // checks on — but only once this scope has ever seen a provider, so provider-free
    // apps pay nothing during row realization.
    private int _replayDepth;
    private bool _everHadValues;

    internal long Version => _version;

    /// <summary>
    /// Advances whenever an element's own provided values change between renders (a
    /// provider added, removed or re-valued). A host snapshot records the generation it
    /// was taken at; a mismatch means some provider changed since, and the snapshot is
    /// re-taken the next time the reconciler reaches the host. Unlike a check scoped to
    /// the pass that removed the provider, this survives until the host is reached —
    /// e.g. a nested host inside a recycled row that comes back in a later pass.
    /// </summary>
    internal long ProviderGeneration { get; private set; }

    internal void BumpProviderGeneration()
    {
        var before = ProviderGeneration++;
        if (_replayGenerations.Count == 0) return;
        // A provider changed inside a replayed subtree (e.g. a reused row dropped its own
        // .Provide): the replayed stack changed, so snapshots taken under it before the
        // change are no longer current. A replay that was current moves to the new live
        // generation; one that was already stale gets a value no live generation can
        // ever equal, so nothing captured from it is treated as current afterwards.
        int top = _replayGenerations.Count - 1;
        _replayGenerations[top] = _replayGenerations[top] == before ? ProviderGeneration : _staleReplaySeq--;
    }

    // Distinct negative generations for stale replays that saw a provider change.
    // Live generations are non-negative and ContextCapture uses long.MinValue for "none".
    private long _staleReplaySeq = -1;

    // Generations of the snapshots currently being replayed (innermost last). While a
    // replay is active the live stack IS that snapshot, so a nested host captured from it
    // must record the snapshot's generation — not the current one, which would mark
    // possibly-outdated replayed values as current.
    private readonly List<long> _replayGenerations = new();

    /// <summary>
    /// The generation the live stack's contents correspond to: the replayed snapshot's
    /// while a replay is active, otherwise <see cref="ProviderGeneration"/>.
    /// </summary>
    internal long SnapshotGeneration
        => _replayGenerations.Count > 0 ? _replayGenerations[^1] : ProviderGeneration;

    internal (ContextBase Context, object? Value)[] Snapshot()
        => _stack.Count == 0 ? [] : _stack.ToArray();

    /// <summary>
    /// True when the live stack holds exactly <paramref name="snapshot"/>: same contexts,
    /// same value instances. Lets a refresh keep the existing snapshot array instead of
    /// allocating a new one on every reconcile of the host.
    /// </summary>
    internal bool Matches((ContextBase Context, object? Value)[] snapshot)
    {
        if (_stack.Count != snapshot.Length) return false;
        for (int i = 0; i < _stack.Count; i++)
        {
            if (!ReferenceEquals(_stack[i].Context, snapshot[i].Context)
                || !ReferenceEquals(_stack[i].Value, snapshot[i].Value))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when the live stack is a positional prefix of <paramref name="snapshot"/>
    /// (same contexts, equal values). The traversal pushes providers along the path,
    /// so at any ancestor of the node that took the snapshot the live stack is a
    /// prefix of it unless a provider at or above that ancestor changed value or was
    /// added. A <em>removed</em> provider leaves a shorter stack that is still a prefix,
    /// so callers must detect removal separately (see
    /// <see cref="ProviderGeneration"/>).
    /// </summary>
    internal bool IsPrefixOf((ContextBase Context, object? Value)[] snapshot)
    {
        if (_stack.Count > snapshot.Length) return false;
        for (int i = 0; i < _stack.Count; i++)
        {
            if (!ReferenceEquals(_stack[i].Context, snapshot[i].Context)
                || !Equals(_stack[i].Value, snapshot[i].Value))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Replaces the live stack with <paramref name="snapshot"/> and returns the stack it
    /// displaced, which the caller must hand back to <see cref="Restore"/>. Replacing
    /// (rather than pushing on top) keeps a re-entrant realization from observing the
    /// providers of whatever subtree the traversal happened to be in.
    /// </summary>
    internal List<(ContextBase Context, object? Value)> Replace((ContextBase Context, object? Value)[] snapshot)
        => Replace(snapshot, SnapshotGeneration);

    /// <summary>
    /// Replaces the live stack with <paramref name="snapshot"/>, recording the provider
    /// generation it was taken at (see <see cref="SnapshotGeneration"/>), and returns the
    /// displaced stack for <see cref="Restore"/>.
    /// </summary>
    /// <param name="snapshot">The provider entries to replay.</param>
    /// <param name="generation">The <see cref="ProviderGeneration"/> <paramref name="snapshot"/> was taken at.</param>
    internal List<(ContextBase Context, object? Value)> Replace((ContextBase Context, object? Value)[] snapshot, long generation)
    {
        var previous = _stack;
        _stack = new List<(ContextBase Context, object? Value)>(snapshot);
        _replayGenerations.Add(generation);
        _replayDepth++;
        _version++;
        return previous;
    }

    internal void Restore(List<(ContextBase Context, object? Value)> previous)
    {
        _stack = previous;
        _replayGenerations.RemoveAt(_replayGenerations.Count - 1);
        _replayDepth--;
        _version++;
    }
}

/// <summary>
/// The provider stack as it stood when the reconciler last reconciled a virtualized
/// host (LazyVStack, ItemsView, ListView, TreeView, …). Those hosts realize rows from
/// WinUI callbacks — <c>IElementFactory.GetElement</c>, <c>ContainerContentChanging</c> —
/// that run during layout, outside the traversal that maintains <see cref="ContextScope"/>,
/// so the row mount replays this snapshot instead of reading whatever the live stack
/// holds at that moment.
/// </summary>
internal sealed class ContextCapture
{
    internal (ContextBase Context, object? Value)[] Entries { get; private set; } = [];

    /// <summary>
    /// Type of the element whose handler registered this capture, or null when it was
    /// registered outside element dispatch (then any element sharing the control may
    /// refresh it). Decorators and transparent wrappers that share the host's control
    /// are a different type, so they do not overwrite the snapshot at the wrong depth.
    /// </summary>
    internal Type? OwnerType { get; init; }

    internal bool IsOwnedBy(Element element) => OwnerType is null || OwnerType == element.GetType();

    /// <summary><see cref="ContextScope.SnapshotGeneration"/> when <see cref="Entries"/> was taken.</summary>
    internal long Generation { get; private set; } = long.MinValue;

    internal void Refresh(ContextScope scope)
    {
        if (!scope.Matches(Entries))
            Entries = scope.Snapshot();
        Generation = scope.SnapshotGeneration;
    }

    /// <summary>Reconcile pass in which the host was last reached (see <see cref="PassTheme"/>).</summary>
    internal long PassId { get; private set; } = -1;

    /// <summary>
    /// The subtree theme the host was reached with in pass <see cref="PassId"/>. Only
    /// meaningful while that pass is still in flight: afterwards the host's native
    /// <c>ActualTheme</c> is authoritative and is read live instead.
    /// </summary>
    internal ElementTheme PassTheme { get; private set; }

    internal void NotePass(long passId, ElementTheme theme)
    {
        PassId = passId;
        PassTheme = theme;
    }

    /// <summary>
    /// Releases the snapshot's provider values. The capture object can be aliased (an
    /// <see cref="ElementFactory{T}"/> holds the same instance), so a host leaving Reactor's
    /// ownership empties it rather than only dropping its own reference.
    /// </summary>
    internal void Clear()
    {
        Entries = [];
        Generation = long.MinValue;
    }

    /// <param name="scope">The live stack at an ancestor of (or at) the host.</param>
    internal bool IsStale(ContextScope scope)
        => Generation != scope.SnapshotGeneration || !scope.IsPrefixOf(Entries);
}
