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
        // Teardown runs through the strategy, so callback-free controls stay untagged;
        // the generic ContentControl walk reaches the owned child, which is always Content.
        descriptor.Children = new Imperative<TElement, TControl>((ctx, oldElement, newElement, control) =>
        {
            // Lifecycle actions run after this strategy and may replace Content with UI the
            // helper does not own, displacing its child; an unmount action may clear it before
            // teardown. The generic walk would pool that UI and miss the child, so tag the
            // control to route teardown through Unmount, which works from the owned record.
            if (newElement.Modifiers is { OnMountAction: not null } or { OnUpdateAction: not null }
                or { OnUnmountAction: not null })
                EnsureTagged(control, newElement);
            if (getChild(newElement) is not { } newChild)
            {
                // Setter-supplied UI is not ours either, for the same reason.
                if (control.Content is UIElement)
                    EnsureTagged(control, newElement);
                return;
            }
            var hadContent = OwnedContent.TryGetValue(control, out var state);
            state ??= OwnedContent.GetValue(control, static _ => new ContentState());
            state.Child = ctx.ReconcileChild(hadContent ? getChild(oldElement) : null, newChild, state.Child);
            if (!ReferenceEquals(control.Content, state.Child)) control.Content = state.Child;
        })
        {
            Unmount = static (ctx, control) => UnmountOwnedContent(ctx.Reconciler, control),
        };

        return descriptor.ImperativeBridged(
            mount: (_, control, element) =>
            {
                // An untagged control's teardown skips the strategy, so a pooled
                // control can arrive with a record of a child already unmounted.
                OwnedContent.Remove(control);
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
            });
    }

    private static void EnsureTagged(ContentControl control, Element element)
    {
        if (!Reconciler.TryGetReactorState(control, out _))
            Reconciler.SetElementTag(control, element);
    }

    private static void UnmountOwnedContent(Reconciler reconciler, ContentControl control)
    {
        if (!OwnedContent.Remove(control, out var state) || state.Child is not { } child) return;
        reconciler.UnmountChild(child);
        if (ReferenceEquals(control.Content, child)) control.Content = null;
    }
}
