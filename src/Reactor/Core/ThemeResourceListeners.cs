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
    private static readonly Lock s_gate = new();
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

    /// <summary>
    /// Notifies every live listener, outside the lock. A listener can render inline (a host
    /// with no content renders synchronously on its UI thread), so one that throws must not
    /// keep the rest from being notified: every listener runs, then the failure is rethrown
    /// (an <see cref="global::System.AggregateException"/> when several failed).
    /// </summary>
    /// <returns>How many listeners were notified.</returns>
    internal static int NotifyAll()
    {
        IThemeResourceListener[] live;
        lock (s_gate)
        {
            live = SnapshotLiveLocked();
        }

        List<Exception>? errors = null;
        foreach (var listener in live)
        {
            try
            {
                listener.OnThemeResourcesChanged();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                (errors ??= []).Add(ex);
            }
        }
        if (errors is not null)
        {
            if (errors.Count == 1)
                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException(errors);
        }
        return live.Length;
    }

    /// <summary>
    /// Test-only: hides every listener except <paramref name="keep"/> until disposed, so a
    /// selftest's notification re-renders only its own hosts, never ones other fixtures left
    /// behind (some deliberately in a failing state). On dispose the hidden listeners come
    /// back, minus any kept listener that unregistered meanwhile.
    /// </summary>
    internal static IDisposable IsolateForTest(params IThemeResourceListener[] keep)
    {
        List<WeakReference<IThemeResourceListener>>? hidden;
        lock (s_gate)
        {
            hidden = s_listeners;
            s_listeners = [.. keep.Select(static k => new WeakReference<IThemeResourceListener>(k))];
        }
        return new IsolationScope(hidden, keep);
    }

    private sealed class IsolationScope(
        List<WeakReference<IThemeResourceListener>>? hidden, IThemeResourceListener[] keep) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (s_gate)
            {
                var current = s_listeners ?? [];
                var restored = new List<WeakReference<IThemeResourceListener>>();
                if (hidden is not null)
                    restored.AddRange(hidden.Where(weak => weak.TryGetTarget(out var target) && Array.IndexOf(keep, target) < 0));
                // Kept listeners still registered, and anything registered during the scope.
                restored.AddRange(current);
                s_listeners = restored;
            }
        }
    }

    /// <summary>Test-only: whether <paramref name="listener"/> is currently registered.</summary>
    internal static bool IsRegisteredForTest(IThemeResourceListener listener)
    {
        lock (s_gate)
        {
            return s_listeners?.Exists(w => w.TryGetTarget(out var t) && ReferenceEquals(t, listener)) == true;
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
