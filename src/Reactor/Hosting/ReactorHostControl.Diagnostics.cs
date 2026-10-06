using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting;

public sealed partial class ReactorHostControl
{
    /// <summary>
    /// <see cref="Reconciler.DiagnosticsRootResolver"/> for this control. The root anchor
    /// is the control itself, the rendered root control, or its current content while that
    /// content is still Reactor's (the rendered root, or the dev-overlay wrapper around it) —
    /// <c>Content</c> is public, so an element a consumer swapped in is never attributed.
    /// </summary>
    private RootComponentSource? ResolveDiagnosticsRoot(UIElement element)
    {
        if (_disposed) return null;
        var control = _currentControl;
        if (control is null) return null;
        var content = Content;
        var wrapper = _overlayWiring?.WrapperRoot;
        bool contentIsOurs = ReferenceEquals(content, control) || (wrapper is not null && ReferenceEquals(content, wrapper));
        bool isAnchor = ReferenceEquals(element, this)
            || ReferenceEquals(element, control)
            || (contentIsOurs && ReferenceEquals(element, content));
        if (!isAnchor) return null;

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
