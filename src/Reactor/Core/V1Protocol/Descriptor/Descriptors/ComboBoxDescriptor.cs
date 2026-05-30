using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 6) — descriptor variant of the hand-coded
/// <c>MountComboBox</c> / <c>UpdateComboBox</c> arms in
/// <see cref="Reconciler"/>. Multi-event port that mixes the controlled
/// SelectedIndex round-trip with two fire-only DropDown events.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>SelectedIndex</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with <c>SelectionChangedEventHandler</c> trampoline. ComboBox's
///   <c>SelectionChanged</c> fires synchronously inside the programmatic
///   write, so this opts into the §8 value-diff arm (<c>valueDiffEcho: true</c>):
///   the trampoline gates on <c>ChangeEchoSuppressor.ShouldSuppressEcho</c> and
///   the write is a bare <c>_set</c> + <c>ArmExpectedEcho</c> (no counter bump).
///   Contrast GridView/ListBox, which stay on the causal counter — see
///   <see cref="GridViewDescriptor"/> (PR #455 CR item #1).</item>
///   <item><c>DropDownOpened</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   with <c>EventHandler&lt;object&gt;</c> trampoline (ComboBox's
///   DropDownOpened/Closed events are plain <c>EventHandler&lt;object&gt;</c>).</item>
///   <item><c>DropDownClosed</c> — same shape as DropDownOpened.</item>
///   <item><c>Header</c>, <c>PlaceholderText</c>, <c>IsEditable</c>,
///   <c>MaxDropDownHeight</c>, <c>Description</c> — one-way / one-way
///   conditional per the legacy guards.</item>
/// </list></para>
///
/// <para><b>Items handling (Phase 3-final batch G1):</b> declared via the
/// <see cref="ItemsHost{TElement,TControl}"/> child strategy. The engine
/// populates <c>cb.Items</c> BEFORE the prop loop so <c>SelectedIndex</c>'s
/// initial write lands against a populated collection. Both string items
/// (<see cref="ComboBoxElement.Items"/>) and <see cref="Element"/> items
/// (<see cref="ComboBoxElement.ItemElements"/>) are supported — when
/// <c>ItemElements</c> is non-null it takes precedence and the engine routes
/// each child through the reconciler so descendant component state survives
/// re-renders. Reconciliation is positional (clear + add on structural
/// delta); keyed reconciliation for templated lists lands in batch G2.</para>
/// </summary>
internal static class ComboBoxDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var cb = (WinUI.ComboBox)s!;
        if (ChangeEchoSuppressor.ShouldSuppressEcho(cb, cb.SelectedIndex)) return;
        (Reconciler.GetElementTag(cb) as ComboBoxElement)
            ?.OnSelectedIndexChanged?.Invoke(cb.SelectedIndex);
    };

    private static readonly global::System.EventHandler<object> DropDownOpenedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((WinUI.ComboBox)s!) as ComboBoxElement)?.OnDropDownOpened?.Invoke();

    private static readonly global::System.EventHandler<object> DropDownClosedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((WinUI.ComboBox)s!) as ComboBoxElement)?.OnDropDownClosed?.Invoke();

    public static readonly ControlDescriptor<ComboBoxElement, WinUI.ComboBox> Descriptor =
        new ControlDescriptor<ComboBoxElement, WinUI.ComboBox>
        {
            // §14 Phase 3-final batch G1: items declared as a child strategy.
            // Engine dispatches BEFORE the prop loop so the initial
            // SelectedIndex write lands against a populated collection.
            // ItemElements takes precedence when non-null (typed Element
            // items → engine routes through Reconciler.MountChild); otherwise
            // the string[] items are used. Both casts are valid via array
            // reference covariance (reference element types).
            Children = new ItemsHost<ComboBoxElement, WinUI.ComboBox>(
                GetItems: static e => e.ItemElements is not null
                    ? (global::System.Collections.Generic.IReadOnlyList<object>)e.ItemElements
                    : (global::System.Collections.Generic.IReadOnlyList<object>)e.Items,
                GetCollection: static c => c.Items),
            GetSetters = static e => e.Setters,
        }
        .HandCodedControlled<ComboBoxEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
            valueDiffEcho: true)
        .HandCodedEvent<ComboBoxEventPayload, global::System.EventHandler<object>>(
            subscribe:        static (c, h) => c.DropDownOpened += h,
            callbackPresent:  static e => e.OnDropDownOpened,
            trampoline:       DropDownOpenedTrampoline,
            slotIsNull:       static p => p.DropDownOpenedTrampoline is null,
            setSlot:          static (p, h) => p.DropDownOpenedTrampoline = h)
        .HandCodedEvent<ComboBoxEventPayload, global::System.EventHandler<object>>(
            subscribe:        static (c, h) => c.DropDownClosed += h,
            callbackPresent:  static e => e.OnDropDownClosed,
            trampoline:       DropDownClosedTrampoline,
            slotIsNull:       static p => p.DropDownClosedTrampoline is null,
            setSlot:          static (p, h) => p.DropDownClosedTrampoline = h)
        .OneWay(
            get: static e => e.PlaceholderText ?? "",
            set: static (c, v) => c.PlaceholderText = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWay(
            get: static e => e.IsEditable,
            set: static (c, v) => c.IsEditable = v)
        .OneWayConditional(
            get:         static e => e.MaxDropDownHeight,
            set:         static (c, v) => c.MaxDropDownHeight = v,
            shouldWrite: static e => !double.IsNaN(e.MaxDropDownHeight))
        .OneWayConditional(
            get:         static e => e.Description,
            set:         static (c, v) => c.Description = v,
            shouldWrite: static e => e.Description is not null);
}
