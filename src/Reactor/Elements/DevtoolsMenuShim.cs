using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// Renders the optional in-app devtools menu when Microsoft.UI.Reactor.Devtools is referenced and active; otherwise returns <see cref="Empty" />.
    /// </summary>
    public static Element DevtoolsMenu(
        Func<IEnumerable<MenuFlyoutItemBase>>? items = null,
        string glyph = "⚡",
        string toolTip = "Devtools",
        string? automationId = null) =>
        ReactorDevtoolsBootstrap.Current?.BuildDevtoolsMenu(items, glyph, toolTip, automationId) ?? Empty();
}
