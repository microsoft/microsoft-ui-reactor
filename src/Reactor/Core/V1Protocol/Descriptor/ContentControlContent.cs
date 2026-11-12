using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

// Shared by the button family: a real child subtree or the existing string API.
internal static class ContentControlContent
{
    private sealed class ContentState
    {
        internal UIElement? Child;
    }

    private static readonly ConditionalWeakTable<ContentControl, ContentState> OwnedContent = new();

    internal static ControlDescriptor<TElement, TControl> WithContent<TElement, TControl>(
        this ControlDescriptor<TElement, TControl> descriptor,
        Func<TElement, Element?> getChild,
        Func<TElement, string?> getText)
        where TElement : Element
        where TControl : ContentControl, new()
    {
        // The helper owns both reconciliation and removal. Native setters run
        // between the text bridge and this existing post-setter strategy, so a
        // declared Element wins while an undeclared slot leaves setter content alone.
        descriptor.Children = new Imperative<TElement, TControl>((ctx, oldElement, newElement, control) =>
        {
            if (getChild(newElement) is not { } newChild) return;
            var hadContent = OwnedContent.TryGetValue(control, out var state);
            state ??= OwnedContent.GetValue(control, static _ => new ContentState());
            state.Child = ctx.ReconcileChild(hadContent ? getChild(oldElement) : null, newChild, state.Child);
            if (!ReferenceEquals(control.Content, state.Child)) control.Content = state.Child;
        });

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
                    UnmountOwnedContent(ctx.Reconciler, control);
                    control.Content = getText(newElement);
                }
                else if (!string.Equals(getText(oldElement), getText(newElement), StringComparison.Ordinal))
                {
                    control.Content = getText(newElement);
                }
            },
            unmount: static (ctx, control) => UnmountOwnedContent(ctx.Reconciler, control));
    }

    private static void UnmountOwnedContent(Reconciler reconciler, ContentControl control)
    {
        if (!OwnedContent.Remove(control, out var state) || state.Child is not { } child) return;
        reconciler.UnmountChild(child);
        if (ReferenceEquals(control.Content, child)) control.Content = null;
    }
}
