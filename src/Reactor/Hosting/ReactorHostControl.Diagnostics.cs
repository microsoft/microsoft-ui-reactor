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
        // Null when the root rendered Empty(): the control itself still anchors it, and the
        // rendered-root check below rejects pre-render, error and disposed states.
        var control = _currentControl;
        var content = Content;
        var wrapper = _overlayWiring?.WrapperRoot;
        bool contentIsOurs = ReferenceEquals(content, control) || (wrapper is not null && ReferenceEquals(content, wrapper));
        bool isAnchor = ReferenceEquals(element, this)
            || ReferenceEquals(element, control)
            || (contentIsOurs && ReferenceEquals(element, content));
        if (!isAnchor) return null;

        // The root that produced the displayed control, not the requested one (see RenderedRoot).
        var rendered = _renderedRoot;
        if (rendered.IsEmpty) return null;
        return new RootComponentSource(
            rendered.Component,
            rendered.FuncContext,
            rendered.RenderFunc,
            control,
            _currentTree,
            () => !_disposed && _renderedRoot.SameRootAs(rendered));
    }
}
