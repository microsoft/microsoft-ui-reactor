using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting;

public sealed partial class ReactorHostControl
{
    /// <summary>
    /// <see cref="Reconciler.DiagnosticsRootResolver"/> for this control. The root anchor
    /// is the control itself, its current content (which is a dev-overlay wrapper while
    /// one is installed), or the rendered root control.
    /// </summary>
    private RootComponentSource? ResolveDiagnosticsRoot(UIElement element)
    {
        if (_disposed) return null;
        var control = _currentControl;
        bool isAnchor = ReferenceEquals(element, this)
            || ReferenceEquals(element, control)
            || ReferenceEquals(element, Content);
        if (!isAnchor || control is null) return null;

        var component = _rootComponent;
        var funcContext = _funcContext;
        if (component is null && funcContext is null) return null;
        return new RootComponentSource(
            component,
            component is null ? funcContext : null,
            component is null ? _rootRenderFunc : null,
            control,
            _currentTree,
            () => !_disposed && ReferenceEquals(_rootComponent, component) && ReferenceEquals(_funcContext, funcContext));
    }
}
