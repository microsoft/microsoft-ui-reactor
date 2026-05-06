using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Microsoft.UI.Reactor.Hosting.HotReloadService))]

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Hooks into .NET Hot Reload (Visual Studio / dotnet watch).
/// When code is edited, triggers a re-render on the active ReactorHost.
/// UseState values survive because the RenderContext and its hooks list
/// remain in memory — only the Render() method body changes.
/// </summary>
static class HotReloadService
{
    /// <summary>Called by the runtime to clear any caches of metadata.</summary>
    public static void ClearCache(Type[]? updatedTypes) { }

    /// <summary>Called after the metadata update is applied. Re-renders the UI.</summary>
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        // force: true bypasses component memo (Props/deps equality) for this
        // pass — the updated Render() body would otherwise be skipped because
        // props and hook deps haven't changed.
        ReactorApp.ActiveHost?.RequestRender(force: true);
    }
}
