using System.Collections.ObjectModel;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.26 — DockManager host registry.
//
//  ConditionalWeakTable-keyed bridges (DockChordBridge, DockHostModelBridge,
//  DockHostLiveAnnouncer) hash on DockManager element refs but do NOT
//  expose the enumeration the §2.26 MCP tools (`docking.snapshot`,
//  `docking.dock`) need. This registry keeps a parallel WeakReference
//  list so live hosts can be enumerated for headless test driving and
//  devtools introspection.
//
//  Each entry pairs a weak ref to the DockManager element with a stable
//  Id assigned at registration time (so a snapshot can reference a host
//  across MCP calls). Entries are pruned lazily when WeakReference.Target
//  reads null. The registry is process-wide and thread-safe behind a
//  simple lock — the host count is small (a handful per app) so the
//  lock contention is negligible.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Per-host record in the registry. The <c>Manager</c> reference is
/// weak — the registry holds no strong root for the element.
/// </summary>
public sealed class DockHostRecord
{
    /// <summary>Stable id assigned at registration; format <c>"dh:{n}"</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The live DockManager element ref, or null after the element has been GC'd.</summary>
    public DockManager? Manager => _ref.TryGetTarget(out var m) ? m : null;

    internal WeakReference<DockManager> _ref;

    internal DockHostRecord(string id, DockManager manager)
    {
        Id = id;
        _ref = new WeakReference<DockManager>(manager);
    }
}

/// <summary>
/// Process-wide registry of mounted <see cref="DockManager"/> elements.
/// Used by the §2.26 MCP tools and the corresponding devtools surface
/// to enumerate hosts without holding strong refs.
/// </summary>
public static class DockHostRegistry
{
    private static readonly object _lock = new();
    private static readonly List<DockHostRecord> _records = new();
    private static int _nextId = 1;

    /// <summary>
    /// Register a freshly-mounted host. Idempotent for the same
    /// <paramref name="manager"/> reference — returns the existing record.
    /// </summary>
    public static DockHostRecord Register(DockManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        lock (_lock)
        {
            PruneInsideLock();
            foreach (var existing in _records)
            {
                if (existing._ref.TryGetTarget(out var m) && ReferenceEquals(m, manager))
                    return existing;
            }
            var record = new DockHostRecord($"dh:{_nextId++}", manager);
            _records.Add(record);
            return record;
        }
    }

    /// <summary>Remove the registration for <paramref name="manager"/>, if any.</summary>
    public static void Unregister(DockManager manager)
    {
        if (manager is null) return;
        lock (_lock)
        {
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                if (_records[i]._ref.TryGetTarget(out var m) && ReferenceEquals(m, manager))
                    _records.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Snapshot the current set of live hosts. Records whose underlying
    /// element ref has been GC'd are filtered out before returning.
    /// </summary>
    public static IReadOnlyList<DockHostRecord> Snapshot()
    {
        lock (_lock)
        {
            PruneInsideLock();
            return new ReadOnlyCollection<DockHostRecord>(_records.ToArray());
        }
    }

    /// <summary>
    /// Resolve a record by its stable <see cref="DockHostRecord.Id"/>.
    /// Returns null when no live host carries that id.
    /// </summary>
    public static DockHostRecord? Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock)
        {
            PruneInsideLock();
            foreach (var r in _records)
            {
                if (string.Equals(r.Id, id, StringComparison.Ordinal)) return r;
            }
            return null;
        }
    }

    /// <summary>Clear all registrations. Test isolation only.</summary>
    internal static void ResetForTest()
    {
        lock (_lock)
        {
            _records.Clear();
            _nextId = 1;
        }
    }

    private static void PruneInsideLock()
    {
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            if (!_records[i]._ref.TryGetTarget(out _))
                _records.RemoveAt(i);
        }
    }
}
