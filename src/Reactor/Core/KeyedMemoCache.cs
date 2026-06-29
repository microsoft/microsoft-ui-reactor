namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Issue #327 (Option A) — a bounded, factory-scoped LRU that memoizes the inner
/// <see cref="Element"/> produced by a <see cref="KeyedMemoElement.Factory"/>, keyed by the
/// author-supplied <see cref="KeyedMemoElement.MemoKey"/>.
///
/// <para>Owned by <see cref="ElementFactory{T}"/>. On the variable-height VirtualList recycle
/// path the framework rebuilds each row's element tree on every container recycle
/// (<see cref="ElementFactory{T}.GetElement"/>) — the existing <c>_viewBuilderCache</c> guards on
/// <c>ReferenceEquals(item)</c> and never hits on the int-index path because each access re-boxes
/// the index. This cache keys on the author's <see cref="KeyedMemoElement.MemoKey"/> with value
/// equality instead, so a recycle that re-asks for a key still inside the LRU window returns the
/// <em>same</em> inner instance — letting <see cref="Element.ShallowEquals"/> short-circuit on
/// <c>ReferenceEquals</c> and collapsing the per-row reconcile to a single sub-µs skip.</para>
///
/// <para><b>Bound.</b> Capacity defaults to <see cref="DefaultCapacity"/> (a few× a typical
/// realized window). Inserting past capacity evicts the least-recently-resolved entry. The cache
/// is therefore never unbounded regardless of list length.</para>
///
/// <para><b>Invalidation.</b> <see cref="Clear"/> is called from
/// <see cref="ElementFactory{T}.UpdateInPlace"/> alongside the existing
/// <c>_viewBuilderCache.Clear()</c>: a new <c>viewBuilder</c> closure may capture different
/// external state, so any inner instance built by the previous closure must not be served.</para>
///
/// <para>Keys are compared with the default <c>EqualityComparer&lt;object&gt;</c>
/// (i.e. <see cref="object.Equals(object)"/> / <see cref="object.GetHashCode"/>),
/// so boxed value-type keys (ints, value tuples, records) dedupe by value.</para>
/// </summary>
internal sealed class KeyedMemoCache
{
    /// <summary>
    /// Default LRU capacity. Sized to comfortably exceed a realized window (typically a few
    /// dozen rows) plus the off-screen buffer the ItemsRepeater keeps warm, so steady-state
    /// scrolling stays in-cache while the working set stays bounded.
    /// </summary>
    internal const int DefaultCapacity = 128;

    private sealed class Entry
    {
        public readonly object Key;
        public Element Value;
        public Entry(object key, Element value) { Key = key; Value = value; }
    }

    private readonly int _capacity;
    private readonly Dictionary<object, LinkedListNode<Entry>> _map;
    // First node = most-recently-used, Last node = least-recently-used (eviction target).
    private readonly LinkedList<Entry> _lru = new();

    /// <summary>
    /// Number of times a <see cref="KeyedMemoElement.Factory"/> was actually invoked (cache
    /// MISS / rebuild count). The effectiveness metric: with the cache working, this rises only
    /// while keys are first seen (or after eviction/invalidation) and stays flat across recycles
    /// of already-cached keys. Exposed for the headless effectiveness fixture.
    /// </summary>
    internal long FactoryInvocations { get; private set; }

    internal KeyedMemoCache(int capacity = DefaultCapacity)
    {
        _capacity = capacity < 1 ? 1 : capacity;
        _map = new Dictionary<object, LinkedListNode<Entry>>(_capacity);
    }

    /// <summary>Current number of cached entries (≤ capacity). Test/diagnostic accessor.</summary>
    internal int Count => _map.Count;

    /// <summary>Configured LRU capacity. Test/diagnostic accessor.</summary>
    internal int Capacity => _capacity;

    /// <summary>
    /// Resolve <paramref name="memo"/> to a stable inner <see cref="Element"/> instance.
    /// HIT: promote the key to most-recently-used and return the cached instance unchanged
    /// (so the caller observes <c>ReferenceEquals</c> across recycles). MISS: invoke
    /// <see cref="KeyedMemoElement.Factory"/> exactly once, optionally stamp the per-item
    /// identity <paramref name="identityKey"/> onto it (mirrors
    /// <see cref="ElementFactory{T}.ApplyItemIdentityKey"/>), cache and return it, evicting the
    /// least-recently-used entry if at capacity.
    /// </summary>
    /// <param name="memo">The opt-in keyed memo wrapper to resolve.</param>
    /// <param name="identityKey">
    /// When non-null, the stable per-item key to stamp onto a freshly-built inner element whose
    /// own <see cref="Element.Key"/> is still null — preserves the issue #326 recycle-on-reuse
    /// remount semantics on the keyed <c>ReactorRow</c> path. Pass null on the legacy int-index
    /// path where stamping a key would force a control swap on every scroll.
    /// </param>
    internal Element Resolve(KeyedMemoElement memo, string? identityKey)
    {
        var key = memo.MemoKey;
        if (_map.TryGetValue(key, out var existing))
        {
            // HIT — move to MRU front, return the previously-built instance unchanged.
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return existing.Value.Value;
        }

        // MISS — invoke the (author-asserted-pure) factory exactly once.
        FactoryInvocations++;
        var inner = memo.Factory() ?? EmptyElement.Instance;

        // Mirror ElementFactory<T>.ApplyItemIdentityKey on the inner: an explicit author key
        // inside the factory always wins; otherwise stamp the per-item identity so recycle-reuse
        // for a different logical item flips Reconciler.CanUpdate → fresh mount (issue #326).
        if (identityKey is not null && inner.Key is null)
            inner = inner with { Key = identityKey };

        var node = new LinkedListNode<Entry>(new Entry(key, inner));
        _lru.AddFirst(node);
        _map[key] = node;

        if (_map.Count > _capacity)
        {
            var evict = _lru.Last;
            if (evict is not null)
            {
                _lru.RemoveLast();
                _map.Remove(evict.Value.Key);
            }
        }

        return inner;
    }

    /// <summary>
    /// Drop all cached entries. Called on the items-reference / <c>UpdateInPlace</c> boundary so a
    /// new <c>viewBuilder</c> closure can never serve an instance built by the previous one.
    /// Resets neither <see cref="FactoryInvocations"/> (a monotonic lifetime counter) — clearing
    /// only the entries forces the next resolve of each key to rebuild.
    /// </summary>
    internal void Clear()
    {
        _map.Clear();
        _lru.Clear();
    }
}
