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
            if (s_listeners is null || s_listeners.Count == 0) return 0;
            var buffer = new List<IThemeResourceListener>(s_listeners.Count);
            foreach (var weak in s_listeners)
            {
                if (weak.TryGetTarget(out var listener)) buffer.Add(listener);
            }
            live = buffer.ToArray();
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
            if (s_listeners is null) return [];
            var live = new List<IThemeResourceListener>(s_listeners.Count);
            foreach (var weak in s_listeners)
            {
                if (weak.TryGetTarget(out var listener)) live.Add(listener);
            }
            return live.ToArray();
        }
    }
}
