using System;
using System.Diagnostics;
using Microsoft.UI.Reactor.Core.Internal;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Process-lifetime persisted-state scope (spec 033 §2). Replaces the previous
/// <c>ConcurrentDictionary</c>-based reject-on-full implementation with an
/// LRU-bounded scope that:
/// </summary>
/// <list type="bullet">
///   <item><description>Evicts least-recently-used entries when at capacity (instead of refusing new keys).</description></item>
///   <item><description>Registers for OS memory-pressure notifications and shrinks to 25% of capacity when the host signals over-limit.</description></item>
///   <item><description>Exposes a stable public surface (<see cref="IPersistedStateScope"/>) so component code can target Window-or-Application scope explicitly.</description></item>
/// </list>
/// <remarks>
/// The legacy internal-static <c>PersistedStateCache</c> shim is preserved for
/// callers inside this assembly, delegating to <see cref="Default"/>.
/// </remarks>
public sealed class ApplicationPersistedScope : IPersistedStateScope
{
    /// <summary>Default capacity for the singleton process-wide scope.</summary>
    public const int DefaultCapacity = 4096;

    /// <summary>Process-wide singleton instance.</summary>
    public static ApplicationPersistedScope Default { get; } = new(DefaultCapacity);

    private readonly LruCache<string, object?> _cache;
    private readonly object _memPressureSync = new();
    private bool _memPressureRegistered;
    private bool _disposed;

    public ApplicationPersistedScope(int capacity)
    {
        _cache = new LruCache<string, object?>(capacity);
        TryRegisterMemoryPressureHandler();
        // Spec 033 §7.10: log only counts/capacity. Never keys or values —
        // keys can be derived from user-controlled identifiers in apps.
        Debug.WriteLine($"[Reactor] ApplicationPersistedScope: constructed (capacity={capacity}).");
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
        TryUnregisterMemoryPressureHandler();
        _cache.Clear();
        Debug.WriteLine($"[Reactor] ApplicationPersistedScope: disposed (dropped={droppedCount}/{Capacity}).");
    }

    /// <summary>
    /// Spec 033 §2 — tighten the key surface. Keys are developer-controlled
    /// (not user-input) but a pathologically long key would let one component
    /// dominate the cache budget. 256 chars is the documented ceiling.
    /// </summary>
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

    // ── Memory-pressure escape valve ────────────────────────────────────────

    private void TryRegisterMemoryPressureHandler()
    {
        lock (_memPressureSync)
        {
            if (_memPressureRegistered) return;
            try
            {
                global::Windows.System.MemoryManager.AppMemoryUsageIncreased += OnAppMemoryUsageIncreased;
                _memPressureRegistered = true;
            }
            catch (Exception ex)
            {
                // Some hosting models don't expose AppMemoryUsageIncreased
                // (unpackaged apps, headless tests, etc.). Spec: log + carry on.
                Debug.WriteLine($"[Reactor] ApplicationPersistedScope: memory-pressure registration failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void TryUnregisterMemoryPressureHandler()
    {
        lock (_memPressureSync)
        {
            if (!_memPressureRegistered) return;
            try { global::Windows.System.MemoryManager.AppMemoryUsageIncreased -= OnAppMemoryUsageIncreased; }
            catch { /* best effort */ }
            _memPressureRegistered = false;
        }
    }

    private void OnAppMemoryUsageIncreased(object? sender, object e)
    {
        // Race: the event can fire after Dispose has unregistered (if a callback
        // is already in flight) or while disposing. Match the WindowPersistedScope
        // pattern and become inert post-dispose.
        if (_disposed) return;

        // The event arg type is opaque-ish; what we care about is the current
        // app memory usage level. Trim aggressively when over-limit.
        try
        {
            var level = global::Windows.System.MemoryManager.AppMemoryUsageLevel;
            if (level == global::Windows.System.AppMemoryUsageLevel.OverLimit ||
                level == global::Windows.System.AppMemoryUsageLevel.High)
            {
                ApplyMemoryPressureTrim();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] ApplicationPersistedScope: memory-pressure handler threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Public for tests: trim to 25% of capacity. Production callers use the
    /// memory-pressure event; tests invoke this directly to exercise the
    /// shrink path without depending on the OS callback.
    /// </summary>
    public int ApplyMemoryPressureTrim()
    {
        var target = Math.Max(0, Capacity / 4);
        var removed = _cache.Trim(target);
        Debug.WriteLine($"[Reactor] ApplicationPersistedScope: memory-pressure trim removed {removed}; count={Count}/{Capacity}");
        return removed;
    }

    /// <summary>
    /// Trims to the given target count. Exposed publicly so tests and callers
    /// that need a clean slate (e.g. test harnesses between cases) can drive
    /// the cache below its 25%-of-capacity memory-pressure floor without
    /// disposing the singleton.
    /// </summary>
    public int Trim(int targetCount) => _cache.Trim(targetCount);
}

/// <summary>
/// Internal shim preserving the legacy call sites that referenced the
/// static <c>PersistedStateCache</c>. Routes through
/// <see cref="ApplicationPersistedScope.Default"/> so the LRU policy and
/// memory-pressure handler take effect everywhere.
/// </summary>
internal static class PersistedStateCache
{
    internal static bool TryGet<T>(string key, out T value)
        => ApplicationPersistedScope.Default.TryGet(key, out value);

    internal static void Set<T>(string key, T value)
        => ApplicationPersistedScope.Default.Set(key, value);

    internal static void Remove(string key)
        => ApplicationPersistedScope.Default.Remove(key);

    internal static void Clear()
    {
        // Trim to zero — used by long-running tests that need a clean slate
        // between cases without rebuilding the singleton.
        ApplicationPersistedScope.Default.Trim(0);
    }
}
