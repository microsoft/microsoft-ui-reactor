using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 11) — descriptor variant of the hand-coded
/// <c>MountBreadcrumbBar</c> / <c>UpdateBreadcrumbBar</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Items</c> — one-way <c>ItemsSource</c> assignment of a label
///   list. The descriptor rebuilds the label list on each pass and assigns
///   it to <c>ItemsSource</c> (mirrors the legacy mount + update arms,
///   which both unconditionally do the same).</item>
///   <item><c>ItemClicked</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>.
///   The trampoline maps <c>args.Index</c> back to the live element's
///   <c>Items[idx]</c> data — matches the legacy hand-coded mapping.</item>
/// </list></para>
/// </summary>
internal static class BreadcrumbBarDescriptor
{
    private static readonly TypedEventHandler<WinUI.BreadcrumbBar, WinUI.BreadcrumbBarItemClickedEventArgs>
        ItemClickedTrampoline = (s, args) =>
        {
            var bar = (WinUI.BreadcrumbBar)s!;
            if (Reconciler.GetElementTag(bar) is not BreadcrumbBarElement el) return;
            if (args.Index >= 0 && args.Index < el.Items.Length)
                el.OnItemClicked?.Invoke(el.Items[args.Index]);
        };

    public static readonly ControlDescriptor<BreadcrumbBarElement, WinUI.BreadcrumbBar> Descriptor =
        new ControlDescriptor<BreadcrumbBarElement, WinUI.BreadcrumbBar>
        {
            Children = new None<BreadcrumbBarElement, WinUI.BreadcrumbBar>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay<BreadcrumbBarItemData[]>(
            get: static e => e.Items,
            set: static (c, items) => c.ItemsSource = items.Select(i => i.Label).ToList())
        .HandCodedEvent<BreadcrumbBarEventPayload,
            TypedEventHandler<WinUI.BreadcrumbBar, WinUI.BreadcrumbBarItemClickedEventArgs>>(
            subscribe:        static (c, h) => c.ItemClicked += h,
            callbackPresent:  static e => e.OnItemClicked,
            trampoline:       ItemClickedTrampoline,
            slotIsNull:       static p => p.ItemClickedTrampoline is null,
            setSlot:          static (p, h) => p.ItemClickedTrampoline = h);
}
