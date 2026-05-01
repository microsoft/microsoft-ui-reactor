using System;
using Microsoft.UI.Reactor.Core.Internal;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Selector for the lifetime/visibility scope of <see cref="RenderContext.UsePersisted{T}(string, T)"/>
/// values. Spec 033 §2.
/// </summary>
public enum PersistedScope
{
    /// <summary>
    /// Bound to the lifetime of the hosting <see cref="Microsoft.UI.Reactor.Hosting.ReactorHost"/>
    /// or <see cref="Microsoft.UI.Reactor.Hosting.ReactorHostControl"/>. State is
    /// dropped when the host unloads. Recommended default for new code.
    /// </summary>
    Window,

    /// <summary>
    /// Process-lifetime. State persists across windows in the same process.
    /// Replaces the legacy default (which was implicit application-scope).
    /// </summary>
    Application,
}

/// <summary>
/// A keyed object cache used by <c>UsePersisted</c>. Two implementations:
/// <see cref="ApplicationPersistedScope"/> (process-global, LRU-bounded,
/// memory-pressure-aware) and <see cref="WindowPersistedScope"/> (host-scoped,
/// LRU-bounded, disposed on host unload).
/// </summary>
/// <remarks>
/// Spec 033 §2. <c>UsePersisted</c> hooks consult these scopes at hook-entry
/// time on the UI thread; the underlying <see cref="LruCache{TKey,TValue}"/>
/// synchronizes its own mutations so background-thread invalidations (memory
/// pressure events) are safe.
/// </remarks>
public interface IPersistedStateScope : IDisposable
{
    /// <summary>The maximum number of entries this scope retains.</summary>
    int Capacity { get; }

    /// <summary>The number of entries currently held in the scope.</summary>
    int Count { get; }

    /// <summary>
    /// Try to read a previously-stored value for <paramref name="key"/>. Returns
    /// <c>false</c> when the key is absent or the stored value is not assignable
    /// to <typeparamref name="T"/>. Reads promote the entry to MRU.
    /// </summary>
    bool TryGet<T>(string key, out T value);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>. Promotes
    /// the entry to MRU. When the scope is at capacity, evicts the LRU entry
    /// before inserting (no refusal).
    /// </summary>
    void Set<T>(string key, T value);

    /// <summary>Removes the entry under <paramref name="key"/>, if any.</summary>
    void Remove(string key);
}
