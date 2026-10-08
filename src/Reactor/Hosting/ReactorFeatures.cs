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

    /// <summary>
    /// The same <c>Reactor.DevtoolsSupport</c> switch, read once, for per-control hot paths
    /// (inspector diagnostics publishing in the reconciler). Under JIT the tier-1 compiler
    /// folds a static readonly value, so a Release app pays nothing per control; under
    /// ILLink/ILC the <see cref="FeatureSwitchDefinitionAttribute"/> substitutes the getter
    /// with the configured constant, so every <c>if (DevtoolsSupported &amp;&amp; …)</c> branch is
    /// removed. Guard call sites with THIS property directly, not a forwarder on another
    /// type, or that type survives trimming just to host the forwarder.
    /// </summary>
    [FeatureSwitchDefinition("Reactor.DevtoolsSupport")]
    internal static bool DevtoolsSupported { get; } =
        AppContext.TryGetSwitch("Reactor.DevtoolsSupport", out var on) && on;
}
