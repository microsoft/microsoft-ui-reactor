namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Implemented by the <c>Microsoft.UI.Reactor.Devtools</c> package: the in-app preview host
/// that the VS Code live preview and the Visual Studio embedded preview launch with
/// <c>--devtools run</c>.
/// </summary>
public interface IReactorDevtoolsHost
{
    bool TryHandleCommandLine(ReactorDevtoolsBootRequest request);
}
