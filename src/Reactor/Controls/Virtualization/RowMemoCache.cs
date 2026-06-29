using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Bounded least-recently-used cache of already-built row <see cref="Element"/>
/// instances, keyed by the author's <c>CacheRowsBy(index)</c> projection. Backs the
/// opt-in cross-recycle row memoization on <see cref="VirtualListElement.CacheRowsBy"/>
/// (issue #327).
/// </summary>
/// <remarks>
/// <para>Why this exists: on the VirtualList <c>int</c>-index realization path the
/// framework re-boxes the index on every container recycle, so
/// <c>ElementFactory.BuildOrCache</c>'s <c>ReferenceEquals(item, cached.Item)</c> guard
/// never hits — the row's Element tree is rebuilt and re-diffed on every recycle.
/// Holding the previously-built Element here and returning the <em>same reference</em>
/// on a hit lets the reconciler short-circuit on <see cref="Element.CanSkipUpdate"/>
/// (its <c>ReferenceEquals</c> fast path), collapsing both the rebuild and the per-row
/// reconcile descent to ~0.</para>
///
/// <para>The cache is intentionally simple and allocation-light: a
/// <see cref="Dictionary{TKey,TValue}"/> for O(1) lookup plus an intrusive
/// <see cref="LinkedList{T}"/> for O(1) LRU bookkeeping. No reflection — safe under the
/// core library's trim/AOT-warnings-as-errors policy. Not thread-safe: it is owned by a
/// single <see cref="VirtualListComponent"/> instance and only touched from the UI
/// thread during realization.</para>
/// </remarks>
internal sealed class RowMemoCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map;
    // Most-recently-used at the head, least-recently-used at the tail.
    private readonly LinkedList<Entry> _lru = new();

    private readonly struct Entry
    {
        public readonly string Key;
        public readonly Element Element;
        public Entry(string key, Element element) { Key = key; Element = element; }
    }

    public RowMemoCache(int capacity)
    {
        // A non-positive cap would make the cache useless (and break the eviction
        // math). Clamp to at least 1 so an honest mistake degrades to "cache the
        // single most-recent row" rather than throwing.
        _capacity = capacity < 1 ? 1 : capacity;
        _map = new Dictionary<string, LinkedListNode<Entry>>(global::System.StringComparer.Ordinal);
    }

    /// <summary>Configured upper bound on retained rows.</summary>
    public int Capacity => _capacity;

    /// <summary>Number of rows currently retained. Never exceeds <see cref="Capacity"/>.</summary>
    public int Count => _map.Count;

    /// <summary>
    /// Returns the cached Element for <paramref name="key"/> and promotes it to
    /// most-recently-used. <paramref name="element"/> is the exact instance previously
    /// passed to <see cref="Set"/> — reference-stable, which is the whole point.
    /// </summary>
    public bool TryGet(string key, out Element element)
    {
        if (_map.TryGetValue(key, out var node))
        {
            if (!ReferenceEquals(node, _lru.First))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            element = node.ValueRef.Element;
            return true;
        }
        element = null!;
        return false;
    }

    /// <summary>
    /// Stores <paramref name="element"/> under <paramref name="key"/> as
    /// most-recently-used, evicting the least-recently-used entry if the cache is over
    /// capacity. Re-storing an existing key replaces its value and promotes it.
    /// </summary>
    public void Set(string key, Element element)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            existing.ValueRef = new Entry(key, element);
            if (!ReferenceEquals(existing, _lru.First))
            {
                _lru.Remove(existing);
                _lru.AddFirst(existing);
            }
            return;
        }

        var node = _lru.AddFirst(new Entry(key, element));
        _map[key] = node;

        if (_map.Count > _capacity)
        {
            var lru = _lru.Last!;
            _lru.RemoveLast();
            _map.Remove(lru.ValueRef.Key);
        }
    }
}
