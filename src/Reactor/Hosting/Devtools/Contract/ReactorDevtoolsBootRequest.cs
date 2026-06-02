using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

public sealed record ReactorDevtoolsBootRequest(
    DevtoolsCliOptions Options,
    string Title,
    double Width,
    double Height,
    bool FullScreen,
    Type? HostRoot,
    Func<Component>? HostRootFactory,
    Func<RenderContext, Element>? RootRenderFunc,
    Action<ReactorHost>? Configure);
