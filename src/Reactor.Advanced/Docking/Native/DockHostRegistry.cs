using System.Collections.ObjectModel;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.26 — DockManager host registry.
//
//  ConditionalWeakTable-keyed bridges (DockChordBridge, DockHostModelBridge,
//  DockHostLiveAnnouncer) resolve a host from a DockManager element ref but
//  do NOT expose the enumeration the §2.26 MCP tools (`docking.snapshot`,
//  `docking.dock`) need. This registry keeps a parallel WeakReference
//  list so live hosts can be enumerated for headless test driving and
//  devtools introspection.
//
//  Each entry pairs a weak ref to the DockManager element with a stable
//  Id assigned at registration time (so a snapshot can reference a host
//  across MCP calls). A mounted host gets one entry however many element
//  instances it renders: registering another element of the same host
//  (DockHostIdentity) points the host's entry at that element instead of
//  adding one, so apps that build `new DockManager { … }` every render
//  don't list the host once per render or change its id. Entries are
//  pruned lazily when WeakReference.Target reads null, and the native
//  host's unmount removes its entry. The registry is process-wide and
//  thread-safe behind a simple lock — the host count is small (a handful
//  per app) so the lock contention is negligible.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Per-host record in the registry. The <c>Manager</c> reference is
/// weak — the registry holds no strong root for the element.
/// </summary>
public sealed class DockHostRecord
{
    /// <summary>Stable id assigned at registration; format <c>"dh:{n}"</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The DockManager element the host rendered last, or null after the
    /// element has been GC'd.
    /// </summary>
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
    private static readonly Lock _lock = new();
    private static readonly List<DockHostRecord> _records = new();
    private static int _nextId = 1;

    /// <summary>
    /// Register a freshly-mounted host. Idempotent for the same
    /// <paramref name="manager"/> reference — returns the existing record.
    /// A later element of an already registered native host also returns
    /// that host's record, which then points at <paramref name="manager"/>.
    /// </summary>
    public static DockHostRecord Register(DockManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var host = DockHostIdentity.KeyFor(manager);
        lock (_lock)
        {
            PruneInsideLock();
            foreach (var existing in _records)
            {
                if (existing._ref.TryGetTarget(out var m) && ReferenceEquals(DockHostIdentity.KeyFor(m), host))
                {
                    existing._ref.SetTarget(manager);
                    return existing;
                }
            }
            var record = new DockHostRecord($"dh:{_nextId++}", manager);
            _records.Add(record);
            return record;
        }
    }

    /// <summary>
    /// Remove the registration for <paramref name="manager"/>'s host, if
    /// any: the record of the native host that rendered it, or else the
    /// record for the element itself.
    /// </summary>
    public static void Unregister(DockManager manager)
    {
        if (manager is null) return;
        RemoveHost(DockHostIdentity.KeyFor(manager));
    }

    /// <summary>
    /// Remove the record of the native host <paramref name="host"/>. The
    /// host's unmount uses this rather than the element it rendered last,
    /// which a new host may already have taken over.
    /// </summary>
    internal static void Unregister(DockHostIdentity host)
    {
        ArgumentNullException.ThrowIfNull(host);
        RemoveHost(host);
    }

    private static void RemoveHost(object host)
    {
        lock (_lock)
        {
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                if (_records[i]._ref.TryGetTarget(out var m) && ReferenceEquals(DockHostIdentity.KeyFor(m), host))
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
