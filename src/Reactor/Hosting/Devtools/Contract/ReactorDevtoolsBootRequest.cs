using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

public sealed record ReactorDevtoolsBootRequest(
    DevtoolsCliOptions Options,
    string Title,
    double? Width,
    double? Height,
    bool FullScreen,
    Type? HostRoot,
    Func<Component>? HostRootFactory,
    Func<RenderContext, Element>? RootRenderFunc,
    Action<ReactorHost>? Configure)
{
    /// <summary>
    /// The source-mapped <c>ReactorApp.Run</c> call that requested the boot, so a preview
    /// that mounts the app's own root still reports it as that root's mount site.
    /// </summary>
    internal SourceLocation? RootMountSite { get; init; }
}
