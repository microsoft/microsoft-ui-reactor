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

    // True for the duration of a single hot-reload-flagged render pass on the
    // UI dispatcher thread. Where UpdatePending/ConsumeUpdatePending is the
    // one-shot *trigger* (atomically consumed once at the top of the render),
    // WithinUpdatePass is the *wider* signal that the whole-tree re-render is
    // in flight. The reconciler reads it from anywhere on the render thread to
    // decide whether a HookOrderException raised by a non-root child is a
    // hot-reload recovery trigger (reset + retry) rather than a hard error.
    //
    // [ThreadStatic] is correct here: Render runs on the UI dispatcher and all
    // child Render() calls run synchronously on that same thread within the
    // pass, so no other thread should observe the flag.
    [ThreadStatic] private static bool _withinUpdatePass;

    /// <summary>
    /// True while a hot-reload-flagged render pass is executing on the current
    /// (UI dispatcher) thread. See <see cref="BeginUpdatePass"/>.
    /// </summary>
    internal static bool WithinUpdatePass => _withinUpdatePass;

    /// <summary>
    /// The set of types the runtime reported as metadata-updated for the most
    /// recent <see cref="UpdateApplication"/>. Read by the state-migration path
    /// (<see cref="Microsoft.UI.Reactor.Core.RenderContext.MigrateHooksForHotReload"/>)
    /// during the pass to decide which hook-cell values to value-swap (spec 049
    /// §6). Null when the runtime supplied no type list (whole-assembly reload)
    /// or outside a pass. Published before <see cref="_updatePending"/> so the
    /// consuming UI-thread render observes it, and cleared when the pass scope
    /// disposes so a stale set can't leak into a later non-HR render.
    /// </summary>
    internal static IReadOnlySet<Type>? UpdatedTypes { get; private set; }

    /// <summary>
    /// True only when .NET Hot Reload is actually available at runtime *and* a
    /// hot-reload pass is in flight on this thread. The reflection-bearing
    /// Phase 2/3 migration branches route through this so that under NativeAOT
    /// (<see cref="MetadataUpdater.IsSupported"/> == false) the whole subsystem
    /// is statically dead and trims away — see spec 049 §8.
    /// </summary>
    internal static bool IsHotReloadLive => MetadataUpdater.IsSupported && _withinUpdatePass;

    /// <summary>
    /// Opens a hot-reload update pass on the current thread. The returned
    /// scope clears <see cref="WithinUpdatePass"/> on dispose, so the flag is
    /// reset on every exit path (normal return, early return, exception).
    /// </summary>
    internal static IDisposable BeginUpdatePass()
    {
        bool previous = _withinUpdatePass;
        _withinUpdatePass = true;
        return new UpdatePassScope(previous);
    }

    private sealed class UpdatePassScope(bool previous) : IDisposable
    {
        // Restore (not unconditionally clear) so a nested pass on the same
        // thread cannot reset an outer pass's flag when the inner one disposes.
        public void Dispose()
        {
            _withinUpdatePass = previous;
            // Clear the migrated-type set only when the outermost pass closes
            // (previous == false). A nested pass disposing must not drop the
            // set the outer pass is still migrating against.
            if (!previous) UpdatedTypes = null;
        }
    }

    /// <summary>Called by the runtime to clear any caches of metadata.</summary>
    public static void ClearCache(Type[]? updatedTypes) { }

    /// <summary>
    /// Escape hatch (spec 049 §10): forcibly drops <em>all</em> live hook state
    /// on the active host and forces a clean re-render — the "lose everything,
    /// remount fresh" reload. Use this when an in-place migration (spec §6 value
    /// swap or §7 subtree migration) leaves a component in a bad state after an
    /// unusual edit: it runs every context's pending cleanups, clears its hook
    /// list, and re-renders so the next render re-mounts hooks from scratch.
    /// Unlike a normal hot-reload pass this preserves nothing, so reach for it
    /// only when targeted migration misbehaves.
    /// </summary>
    internal static void ResetAllContexts()
    {
        var host = ReactorApp.ActiveHostInternal;
        if (host is null) return;
        host.Reconciler.ForEachLiveContext(ctx => ctx.ResetForHotReload());
        host.RequestRender(force: true);
    }

    /// <summary>Called after the metadata update is applied. Re-renders the UI.</summary>
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        // Publish the updated-type set BEFORE flipping the pending flag so the
        // UI-thread render (which gates on _updatePending via Volatile.Read)
        // is guaranteed to observe a non-stale UpdatedTypes. The set is read
        // during the pass for value-migration (spec 049 §6) and cleared when
        // the pass scope disposes.
        UpdatedTypes = updatedTypes is null || updatedTypes.Length == 0
            ? null
            : new HashSet<Type>(updatedTypes);

        Volatile.Write(ref _updatePending, 1);

        // force: true bypasses component memo (Props/deps equality) for this
        // pass — the updated Render() body would otherwise be skipped because
        // props and hook deps haven't changed.
        ReactorApp.ActiveHostInternal?.RequestRender(force: true);
    }
}
