using System.Diagnostics.CodeAnalysis;

namespace Microsoft.UI.Reactor.Hosting;

internal static class ReactorFeatures
{
    /// <summary>
    /// Build-time consent gate for the devtools subsystem (MCP server, preview
    /// capture, lockfile registry, docking tools, log capture). Default off.
    ///
    /// Apps that need devtools enable the switch in their csproj:
    /// <code>
    /// &lt;RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
    ///                                 Value="true" Trim="true" /&gt;
    /// </code>
    /// With <c>Trim="true"</c> the ILC trimmer substitutes this property body
    /// with the configured constant at publish time, and every dead-arm
    /// devtools call chain (DevtoolsMcpServer, PreviewCaptureServer,
    /// LockfileRegistry, DevtoolsDockingTools, the System.Text.Json /
    /// System.Net.Http tails) gets pruned. See spec 051.
    /// </summary>
    [FeatureSwitchDefinition("Reactor.DevtoolsSupport")]
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
    [UnconditionalSuppressMessage("AOT", "IL4000", Justification = "Custom feature switch guard for devtools reachability; see spec 051.")]
    internal static bool IsDevtoolsSupported =>
        AppContext.TryGetSwitch("Reactor.DevtoolsSupport", out var on) && on;
}
