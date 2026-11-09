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
        };

        return descriptor.OneWayBridged<(bool HasChild, string? Text)>(
            get: element => getChild(element) is null ? (false, getText(element)) : (true, null),
            set: static (control, value, reconciler, _) =>
            {
                // A declared child can render no UI (Empty or a component returning
                // null). Clear a previous string before reconciling that child.
                if (value.HasChild)
                {
                    if (control.Content is not UIElement)
                        control.Content = null;
                    return;
                }
                // Properties update before child reconciliation. Tear down an
                // owned child before replacing Content, while it is reachable.
                if (control.Content is UIElement previous)
                    reconciler.UnmountChild(previous);
                control.Content = value.Text;
            },
            shouldWrite: static _ => true);
    }
}
