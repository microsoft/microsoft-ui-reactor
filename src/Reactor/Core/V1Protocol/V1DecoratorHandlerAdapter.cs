using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §14 Phase 3 completion — adapter that bridges an
/// <see cref="IDecoratorElementHandler{TElement}"/> to the type-erased
/// <see cref="IV1HandlerEntry"/> dispatch table.
///
/// <para>Decorator handlers differ from standard
/// <see cref="V1HandlerAdapter{TElement,TControl}"/> in three ways:
/// <list type="number">
///   <item>The returned <see cref="UIElement"/> identity may change on
///   update (target-wrapping decorators like FlyoutElement whose
///   returned control is the user's inner Target).</item>
///   <item>Unmount returns a <see cref="V1UnmountDisposition"/> to
///   control pool / traversal behavior — the wrapped child may need
///   the default traversal to run.</item>
///   <item>No <see cref="ChildrenStrategy{TElement,TControl}"/> dispatch —
///   children (and any decorator side-channels like attached flyouts)
///   are managed by the handler's Mount/Update bodies directly.</item>
/// </list></para>
/// </summary>
internal sealed class V1DecoratorHandlerAdapter<TElement> : IV1HandlerEntry
    where TElement : Element
{
    private readonly IDecoratorElementHandler<TElement> _handler;

    public V1DecoratorHandlerAdapter(IDecoratorElementHandler<TElement> handler)
    {
        _handler = handler;
    }

    public bool HasUnmount => true;

    public UIElement Mount(Element element, Action requestRerender, Reconciler reconciler)
    {
        var typedEl = (TElement)element;
        var ctx = new MountContext(reconciler, requestRerender);
        var control = _handler.Mount(ctx, typedEl);

        // Anchor element identity on the returned control via the
        // attached state DP so event trampolines + the unmount
        // disposition path can re-fetch the live element on each fire.
        if (control is FrameworkElement fe)
            Reconciler.SetElementTag(fe, typedEl);

        return control;
    }

    public UIElement Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler)
    {
        var typedOld = (TElement)oldEl;
        var typedNew = (TElement)newEl;
        var ctx = new UpdateContext(reconciler, requestRerender);
        var next = _handler.Update(ctx, typedOld, typedNew, control);

        // Re-tag whichever control is going to live in the slot — `next`
        // may equal `control` (in-place update) or be a different
        // instance (decorator target swap).
        if (next is FrameworkElement nextFe)
            Reconciler.SetElementTag(nextFe, typedNew);

        return next;
    }

    public V1UnmountDisposition Unmount(UIElement control, Reconciler reconciler)
    {
        // Read the element from the attached state DP — may be null if
        // the tag was already detached (defensive). Decorator handlers
        // are expected to tolerate the null case.
        TElement? typedEl = null;
        if (control is FrameworkElement fe && Reconciler.GetElementTag(fe) is TElement attached)
            typedEl = attached;

        var ctx = new UnmountContext(reconciler);
        return _handler.Unmount(ctx, typedEl, control);
    }
}
