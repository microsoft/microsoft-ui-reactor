using System.Reflection.Metadata;
using System.Threading;

[assembly: MetadataUpdateHandler(typeof(Microsoft.UI.Reactor.Hosting.HotReloadService))]

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Hooks into .NET Hot Reload (Visual Studio / dotnet watch).
/// When code is edited, triggers a re-render on the active ReactorHost.
/// UseState values survive because the RenderContext and its hooks list
/// remain in memory — only the Render() method body changes.
/// </summary>
internal static class HotReloadService
{
    // Underlying int storage (0 = false, 1 = true) so we can use
    // Interlocked.Exchange for atomic capture-and-clear. .NET Hot Reload
    // calls UpdateApplication from a runtime-controlled thread (typically
    // the threadpool callback that just delivered the metadata update),
    // while Render runs on the UI dispatcher. A non-atomic read-then-write
    // would race: a second UpdateApplication firing between
    // ReactorHostControl.Render's read and write of the flag could lose
    // the new pending update. Exchange = single CAS, no window.
    private static int _updatePending;

    /// <summary>
    /// True from when <see cref="UpdateApplication"/> sets it until
    /// <see cref="ConsumeUpdatePending"/> atomically clears it (called by
    /// the host at the start of each render attempt). When the consuming
    /// render observes <c>true</c>, the host treats a
    /// <see cref="Microsoft.UI.Reactor.Core.HookOrderException"/> raised
    /// during that render as a hot-reload recovery trigger (run cleanups,
    /// drop hook state, re-render) instead of escalating to the error
    /// fallback.
    /// </summary>
    internal static bool UpdatePending => Volatile.Read(ref _updatePending) == 1;

    /// <summary>
    /// Atomically reads-and-clears <see cref="UpdatePending"/>. Returns
    /// true exactly once per <see cref="UpdateApplication"/> call, even
    /// if another <c>UpdateApplication</c> fires concurrently from the
    /// hot-reload thread.
    /// </summary>
    internal static bool ConsumeUpdatePending() =>
        Interlocked.Exchange(ref _updatePending, 0) == 1;

    /// <summary>Called by the runtime to clear any caches of metadata.</summary>
    public static void ClearCache(Type[]? updatedTypes) { }

    /// <summary>Called after the metadata update is applied. Re-renders the UI.</summary>
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        Volatile.Write(ref _updatePending, 1);

        // force: true bypasses component memo (Props/deps equality) for this
        // pass — the updated Render() body would otherwise be skipped because
        // props and hook deps haven't changed.
        ReactorApp.ActiveHostInternal?.RequestRender(force: true);
    }
}
