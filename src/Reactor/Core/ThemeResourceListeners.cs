namespace Microsoft.UI.Reactor.Core;

/// <summary>Implemented by Reactor hosts so <see cref="Theme.NotifyResourcesChanged"/> can reach them.</summary>
internal interface IThemeResourceListener
{
    /// <summary>
    /// Application resources changed: re-render the whole tree past component memoization,
    /// so every theme-resolved value is resolved again. Must be callable from any thread.
    /// </summary>
    void OnThemeResourcesChanged();
}

/// <summary>
/// Weak, lazily allocated list of live hosts for <see cref="Theme.NotifyResourcesChanged"/>.
/// Hosts register in their constructors and unregister on dispose; a host that is dropped
/// without being disposed ages out with the garbage collector, because the list never keeps
/// one alive.
/// </summary>
internal static class ThemeResourceListeners
{
    private static readonly object s_gate = new();
    private static List<WeakReference<IThemeResourceListener>>? s_listeners;

    internal static void Register(IThemeResourceListener listener)
    {
        lock (s_gate)
        {
            var listeners = s_listeners ??= new List<WeakReference<IThemeResourceListener>>();
            listeners.RemoveAll(static w => !w.TryGetTarget(out _));
            if (listeners.Exists(w => w.TryGetTarget(out var t) && ReferenceEquals(t, listener)))
                return; // already registered: one notification per host
            listeners.Add(new WeakReference<IThemeResourceListener>(listener));
        }
    }

    internal static void Unregister(IThemeResourceListener listener)
    {
        lock (s_gate)
        {
            s_listeners?.RemoveAll(w => !w.TryGetTarget(out var target) || ReferenceEquals(target, listener));
        }
    }

    /// <summary>Notifies every live listener. Listeners run outside the lock.</summary>
    /// <returns>How many listeners were notified.</returns>
    internal static int NotifyAll()
    {
        IThemeResourceListener[] live;
        lock (s_gate)
        {
            live = SnapshotLiveLocked();
        }

        foreach (var listener in live)
            listener.OnThemeResourcesChanged();
        return live.Length;
    }

    /// <summary>Test-only: whether <paramref name="listener"/> is currently registered.</summary>
    internal static bool IsRegisteredForTest(IThemeResourceListener listener)
    {
        lock (s_gate)
        {
            return s_listeners?.Exists(w => w.TryGetTarget(out var t) && ReferenceEquals(t, listener)) == true;
        }
    }

    /// <summary>
    /// Test-only: the live listeners, so a selftest can wait for every host a
    /// notification reached to settle before it reads the shared window back.
    /// </summary>
    internal static IThemeResourceListener[] LiveListenersForTest()
    {
        lock (s_gate)
        {
            return SnapshotLiveLocked();
        }
    }

    /// <summary>Test-only: entries in the list, dead ones included.</summary>
    internal static int EntryCountForTest()
    {
        lock (s_gate)
        {
            return s_listeners?.Count ?? 0;
        }
    }

    // Caller holds s_gate. Also compacts the list, so entries whose host was collected
    // without being disposed don't accumulate between registrations. One TryGetTarget per
    // entry: a target collected mid-snapshot is dropped, never read back as null.
    private static IThemeResourceListener[] SnapshotLiveLocked()
    {
        if (s_listeners is null) return [];
        var live = new List<IThemeResourceListener>(s_listeners.Count);
        s_listeners.RemoveAll(weak =>
        {
            if (!weak.TryGetTarget(out var listener)) return true;
            live.Add(listener);
            return false;
        });
        return live.ToArray();
    }
}
