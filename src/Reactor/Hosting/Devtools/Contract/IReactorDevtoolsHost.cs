using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

public interface IReactorDevtoolsHost
{
    bool TryHandleCommandLine(ReactorDevtoolsBootRequest request);

    Element? BuildDevtoolsMenu(
        Func<IEnumerable<MenuFlyoutItemBase>>? items,
        string glyph,
        string toolTip,
        string? automationId);
}
