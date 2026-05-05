using System;
using System.Diagnostics;
using Microsoft.UI.Reactor.Core.Internal;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Per-host (per-window) persisted-state scope. Owned by a
/// <see cref="Microsoft.UI.Reactor.Hosting.ReactorHost"/> /
/// <see cref="Microsoft.UI.Reactor.Hosting.ReactorHostControl"/> and disposed
/// when the host unloads — so window-scoped state is bounded by window
/// lifetime, not process lifetime.
/// </summary>
/// <remarks>
/// Spec 033 §2. Bounded LRU (default capacity 1024). Window-scoped state is
/// the recommended default for new <see cref="RenderContext.UsePersisted{T}(string, T)"/>
/// calls because most apps want "preserved across an unmount/remount within
/// this window" rather than "preserved across every window in the process."
/// Memory-pressure registration is deliberately omitted — the lifetime is
/// already bounded by the host.
/// </remarks>
public sealed class WindowPersistedScope : IPersistedStateScope
{
    /// <summary>Default capacity for a per-window scope.</summary>
    public const int DefaultCapacity = 1024;

    private readonly LruCache<string, object?> _cache;
    private bool _disposed;

    public WindowPersistedScope() : this(DefaultCapacity) { }

    public WindowPersistedScope(int capacity)
    {
        _cache = new LruCache<string, object?>(capacity);
        // Spec 033 §7.10: log only counts/capacity. Never keys or values —
        // a host's window scope keys can be derived from user-controlled identifiers.
        Debug.WriteLine($"[Reactor] WindowPersistedScope: constructed (capacity={capacity}).");
    }

    public int Capacity => _cache.Capacity;
    public int Count => _cache.Count;

    public bool TryGet<T>(string key, out T value)
    {
        if (_disposed) { value = default!; return false; }
        ValidateKey(key);
        if (_cache.TryGet(key, out var boxed) && boxed is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    public void Set<T>(string key, T value)
    {
        if (_disposed) return;
        ValidateKey(key);
        _cache.Set(key, value);
    }

    public void Remove(string key)
    {
        if (_disposed) return;
        ValidateKey(key);
        _cache.Remove(key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var droppedCount = Count;
        _cache.Clear();
        Debug.WriteLine($"[Reactor] WindowPersistedScope: disposed (dropped={droppedCount}/{Capacity}).");
    }

    private static void ValidateKey(string key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Persisted-state key must be non-empty.", nameof(key));
        if (key.Length > 256)
            throw new ArgumentException(
                $"Persisted-state key length {key.Length} exceeds the 256-character limit.",
                nameof(key));
    }
}
