using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// Bounded LRU cache, accessed under a lock. Used by
/// <see cref="ApplicationPersistedScope"/> and
/// <see cref="WindowPersistedScope"/> as their backing storage; spec 033 §2
/// replaces the old refuse-on-full <see cref="PersistedStateCache"/> policy
/// with eviction-on-full so later, hotter keys aren't starved by the first
/// 4096 keys ever recorded.
/// </summary>
/// <remarks>
/// <para>
/// Mutations and reads are protected by a single lock object. The cache is
/// not on the rendering hot path — hooks consult it at hook-entry time — so
/// the lock contention is acceptable. Touch-on-access (TryGet promotes the
/// node to MRU) is part of LRU semantics and is the source of the "read also
/// mutates" property; callers must therefore hold no other locks across a
/// <see cref="TryGet"/> call to avoid deadlocks.
/// </para>
/// <para>
/// The memory-pressure trim path (<see cref="Trim"/>) is callable from any
/// thread; the lock guarantees consistency.
/// </para>
/// </remarks>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly object _sync = new();
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order;
    private int _capacity;

    public LruCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be > 0.");
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
        _order = new LinkedList<KeyValuePair<TKey, TValue>>();
    }

    public int Capacity
    {
        get { lock (_sync) return _capacity; }
    }

    public int Count
    {
        get { lock (_sync) return _map.Count; }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Promote to MRU.
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Update in place + promote to MRU.
                _order.Remove(node);
                node.Value = new KeyValuePair<TKey, TValue>(key, value);
                _order.AddFirst(node);
                return;
            }

            // Evict-on-full BEFORE insert so we never exceed capacity.
            while (_map.Count >= _capacity)
            {
                var lru = _order.Last;
                if (lru is null) break;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
            }

            var newNode = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                new KeyValuePair<TKey, TValue>(key, value));
            _order.AddFirst(newNode);
            _map[key] = newNode;
        }
    }

    public bool Remove(TKey key)
    {
        lock (_sync)
        {
            if (!_map.TryGetValue(key, out var node)) return false;
            _order.Remove(node);
            _map.Remove(key);
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _map.Clear();
            _order.Clear();
        }
    }

    /// <summary>
    /// Trims the cache so its size does not exceed <paramref name="targetCount"/>,
    /// evicting LRU entries first. Used by the memory-pressure handler on
    /// <see cref="ApplicationPersistedScope"/> when the OS reports a pressure
    /// level above-limit.
    /// </summary>
    public int Trim(int targetCount)
    {
        if (targetCount < 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
        int removed = 0;
        lock (_sync)
        {
            while (_map.Count > targetCount)
            {
                var lru = _order.Last;
                if (lru is null) break;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
                removed++;
            }
        }
        return removed;
    }
}
