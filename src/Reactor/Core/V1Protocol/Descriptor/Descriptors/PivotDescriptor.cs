using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (11). Descriptor variant of the
/// hand-coded <c>MountPivot</c> / <c>UpdatePivot</c> arms. Reuses the
/// <see cref="TabItemsHost{TElement,TControl,TItem}"/> strategy shape
/// landed for Port (10) with <see cref="WinUI.PivotItem"/> as the
/// container.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Items</c> via <see cref="TabItemsHost{TElement,TControl,TItem}"/>
///   — each <c>PivotItemData</c> projected to a <c>WinUI.PivotItem</c>
///   container with Header + Content set.</item>
///   <item><c>SelectedIndex</c> + <c>OnSelectedIndexChanged</c> via
///   <c>.HandCodedControlled</c> with echo suppression — Pivot reuses
///   the <see cref="FlipViewEventPayload"/> single-slot shape since
///   SelectionChanged is the only event Pivot exposes.</item>
///   <item><c>Title</c> via <c>.OneWayConditional</c> (legacy arm only
///   writes when non-null).</item>
/// </list></para>
/// </summary>
internal static class PivotDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var p = (WinUI.Pivot)s!;
        if (ChangeEchoSuppressor.ShouldSuppressEcho(p, p.SelectedIndex)) return;
        (Reconciler.GetElementTag(p) as PivotElement)?.OnSelectedIndexChanged?.Invoke(p.SelectedIndex);
    };

    public static readonly ControlDescriptor<PivotElement, WinUI.Pivot> Descriptor =
        new ControlDescriptor<PivotElement, WinUI.Pivot>
        {
            Children = new TabItemsHost<PivotElement, WinUI.Pivot, PivotItemData>(
                GetItems:        static e => e.Items,
                GetCollection:   static c => c.Items,
                GetContent:      static item => item.Content,
                CreateContainer: static (item, mounted) => new WinUI.PivotItem
                {
                    Header = item.Header,
                    Content = mounted,
                },
                UpdateContainer: static (oldItem, newItem, container) =>
                {
                    if (container is WinUI.PivotItem pi && pi.Header as string != newItem.Header)
                        pi.Header = newItem.Header;
                }),
            GetSetters = static e => e.Setters,
        }
        .OneWayConditional(
            get:         static e => e.Title ?? string.Empty,
            set:         static (c, v) => c.Title = v,
            shouldWrite: static e => e.Title is not null)
        .HandCodedControlled<FlipViewEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
            valueDiffEcho: true);
}
