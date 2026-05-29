using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded RefreshContainer mount/update arms.
/// </summary>
internal static class RefreshContainerDescriptor
{
    private static readonly SingleContent<RefreshContainerElement, WinUI.RefreshContainer> ChildrenStrategy =
        new SingleContent<RefreshContainerElement, WinUI.RefreshContainer>(
            GetChild: static e => e.Content,
            SetChild: static (c, ui) => c.Content = ui)
        {
            GetCurrentChild = static c => c.Content as UIElement,
        };

    private static readonly TypedEventHandler<WinUI.RefreshContainer, WinUI.RefreshRequestedEventArgs>
        RefreshRequestedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as RefreshContainerElement)?.OnRefreshRequested?.Invoke();

    public static readonly ControlDescriptor<RefreshContainerElement, WinUI.RefreshContainer> Descriptor =
        new ControlDescriptor<RefreshContainerElement, WinUI.RefreshContainer>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.PullDirection,
            set: static (c, v) => c.PullDirection = v)
        .HandCodedEvent<RefreshContainerEventPayload,
            TypedEventHandler<WinUI.RefreshContainer, WinUI.RefreshRequestedEventArgs>>(
            subscribe:        static (c, h) => c.RefreshRequested += h,
            callbackPresent:  static e => e.OnRefreshRequested,
            trampoline:       RefreshRequestedTrampoline,
            slotIsNull:       static p => p.RefreshRequestedTrampoline is null,
            setSlot:          static (p, h) => p.RefreshRequestedTrampoline = h);
}
