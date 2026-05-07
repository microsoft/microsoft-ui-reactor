using System.Reflection.Metadata;

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
    /// <summary>
    /// Set true between <see cref="UpdateApplication"/> firing and the next
    /// successful render completing. While true, the host treats a
    /// <see cref="Microsoft.UI.Reactor.Core.HookOrderException"/> as a
    /// hot-reload recovery trigger (run cleanups, drop hook state,
    /// re-render) instead of escalating to the error fallback. Cleared by
    /// the host after the recovery (or normal) render completes.
    /// </summary>
    internal static bool UpdatePending;

    /// <summary>Called by the runtime to clear any caches of metadata.</summary>
    public static void ClearCache(Type[]? updatedTypes) { }

    /// <summary>Called after the metadata update is applied. Re-renders the UI.</summary>
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        UpdatePending = true;

        // force: true bypasses component memo (Props/deps equality) for this
        // pass — the updated Render() body would otherwise be skipped because
        // props and hook deps haven't changed.
        ReactorApp.ActiveHost?.RequestRender(force: true);
    }
}
