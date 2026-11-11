using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

// Shared by the button family: a real child subtree or the existing string API.
internal static class ContentControlContent
{
    internal static ControlDescriptor<TElement, TControl> WithContent<TElement, TControl>(
        this ControlDescriptor<TElement, TControl> descriptor,
        Func<TElement, Element?> getChild,
        Func<TElement, string?> getText)
        where TElement : Element
        where TControl : ContentControl, new()
    {
        descriptor.Children = new SingleContent<TElement, TControl>(
            GetChild: getChild,
            SetChild: static (control, child) =>
            {
                if (child is not null || control.Content is UIElement)
                    control.Content = child;
            })
        {
            GetCurrentChild = static control => control.Content as UIElement,
            ChildRemovalHandledByHandler = true,
        };

        return descriptor.ImperativeBridged(
            mount: (_, control, element) =>
            {
                control.Content = getChild(element) is null ? getText(element) : null;
            },
            update: (ctx, control, oldElement, newElement) =>
            {
                var oldChild = getChild(oldElement);
                var newChild = getChild(newElement);
                if (newChild is not null)
                {
                    // A declared child may render no UI. Clear the old string
                    // before reconciliation, while preserving an existing subtree.
                    if (oldChild is null && control.Content is not UIElement)
                        control.Content = null;
                    return;
                }
                if (oldChild is not null)
                {
                    // Tear down only a declared child, before text replaces the
                    // slot and setters run. Null-to-null owns no native subtree.
                    if (control.Content is UIElement previous)
                        ctx.Reconciler.UnmountChild(previous);
                    control.Content = getText(newElement);
                }
                else if (!string.Equals(getText(oldElement), getText(newElement), StringComparison.Ordinal))
                {
                    control.Content = getText(newElement);
                }
            });
    }
}
