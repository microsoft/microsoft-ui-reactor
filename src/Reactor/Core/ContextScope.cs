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

    internal bool HasActiveValues => _stack.Count > 0;

    internal long Version => _version;

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
    /// <c>Reconciler.InProviderChangeSubtree</c>).
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
    {
        var previous = _stack;
        _stack = new List<(ContextBase Context, object? Value)>(snapshot);
        _version++;
        return previous;
    }

    internal void Restore(List<(ContextBase Context, object? Value)> previous)
    {
        _stack = previous;
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

    internal void Refresh(ContextScope scope)
    {
        if (!scope.Matches(Entries))
            Entries = scope.Snapshot();
    }

    /// <param name="scope">The live stack at an ancestor of (or at) the host.</param>
    /// <param name="inProviderChangeSubtree">
    /// True while the traversal is below an element whose provided values changed this
    /// pass. A removed provider is invisible to the prefix test, so any snapshot taken
    /// beneath such an element has to be treated as stale.
    /// </param>
    internal bool IsStale(ContextScope scope, bool inProviderChangeSubtree)
        => inProviderChangeSubtree || !scope.IsPrefixOf(Entries);
}
