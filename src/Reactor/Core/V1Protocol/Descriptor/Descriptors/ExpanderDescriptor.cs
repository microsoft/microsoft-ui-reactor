using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 7) — descriptor variant of the hand-coded
/// <c>MountExpander</c> / <c>UpdateExpander</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Content</c> — <see cref="SingleContent{TElement,TControl}"/>
///   for the expandable content slot.</item>
///   <item><c>IsExpanded</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   round-trip against <c>OnIsExpandedChanged(bool)</c>. The legacy arm
///   wires both <c>Expanding</c> and <c>Collapsed</c>; the descriptor
///   subscribes to both via a single <see cref="ExpanderEventPayload"/> with
///   two trampoline slots so each fires the same callback with the right
///   bool.</item>
///   <item><c>ExpandDirection</c> — plain one-way enum write.</item>
///   <item><c>Header</c> (string slot) — plain one-way write.</item>
/// </list></para>
///
/// <para><b>§14 Phase 3 finish addition:</b> <c>HeaderTemplate</c>
/// ports via the Engine (2) <c>.ImperativeBridged</c> entry shape — the
/// entry's Update lambda calls
/// <c>ctx.Reconciler.ReconcileV1Child(o.HeaderTemplate, n.HeaderTemplate, ...)</c>
/// to preserve descendant component state across re-renders. The string
/// <c>Header</c> entry is gated on <c>HeaderTemplate is null</c>, so the
/// Element header wins when both are set — mirrors the legacy
/// "Element header overrides string slot" semantics in
/// <c>UpdateExpander</c>.</para>
///
/// <para><b>Known gap vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><c>ContentTransitions</c> is not surfaced (writing a
///   <c>TransitionCollection</c> with the legacy guard is declarative-
///   compatible but the test fixture exercises only the common case;
///   escape-hatched via setter when needed).</item>
/// </list></para>
/// </summary>
internal static class ExpanderDescriptor
{
    private static readonly SingleContent<ExpanderElement, WinUI.Expander> ChildrenStrategy =
        new SingleContent<ExpanderElement, WinUI.Expander>(
            GetChild: static el => el.Content,
            SetChild: static (ctrl, ui) => ctrl.Content = ui)
        {
            GetCurrentChild = static ctrl => ctrl.Content as UIElement,
        };

    // Static trampolines — captured-free. Read the live element via
    // Reconciler.GetElementTag on every fire so the same trampoline serves
    // any element identity change without re-subscription.
    private static readonly TypedEventHandler<WinUI.Expander, WinUI.ExpanderExpandingEventArgs>
        ExpandingTrampoline = (s, _) =>
        {
            if (ChangeEchoSuppressor.ShouldSuppress(s)) return;
            (Reconciler.GetElementTag(s) as ExpanderElement)
                ?.OnIsExpandedChanged?.Invoke(true);
        };

    private static readonly TypedEventHandler<WinUI.Expander, WinUI.ExpanderCollapsedEventArgs>
        CollapsedTrampoline = (s, _) =>
        {
            if (ChangeEchoSuppressor.ShouldSuppress(s)) return;
            (Reconciler.GetElementTag(s) as ExpanderElement)
                ?.OnIsExpandedChanged?.Invoke(false);
        };

    public static readonly ControlDescriptor<ExpanderElement, WinUI.Expander> Descriptor =
        new ControlDescriptor<ExpanderElement, WinUI.Expander>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.ExpandDirection,
            set: static (c, v) => c.ExpandDirection = v)
        // §14 Phase 3 finish — Engine (2) port. HeaderTemplate (Element)
        // takes precedence over the string slot. ImperativeBridged so the
        // Update path can call ReconcileV1Child to preserve descendant
        // component state across re-renders. The sibling string-Header
        // entry below is gated on HeaderTemplate-null so this entry's
        // write is never overwritten.
        .ImperativeBridged(
            mount: static (ctx, c, e) =>
            {
                if (e.HeaderTemplate is null) return;
                var mounted = ctx.MountChild(e.HeaderTemplate);
                if (mounted is not null) c.Header = mounted;
            },
            update: static (ctx, c, oldEl, newEl) =>
            {
                if (oldEl.HeaderTemplate is null && newEl.HeaderTemplate is null) return;
                var existing = c.Header as UIElement;
                var next = ctx.Reconciler.ReconcileV1Child(
                    oldEl.HeaderTemplate, newEl.HeaderTemplate, existing, ctx.RequestRerender);
                if (!ReferenceEquals(existing, next))
                    c.Header = next; // null clears the slot so the string-Header gate can write.
            })
        .OneWayConditional(
            get:         static e => e.Header ?? string.Empty,
            set:         static (c, v) => c.Header = v,
            // Element header wins — skip when HeaderTemplate is set.
            shouldWrite: static e => e.HeaderTemplate is null)
        .HandCodedControlled<ExpanderEventPayload, bool,
            TypedEventHandler<WinUI.Expander, WinUI.ExpanderExpandingEventArgs>>(
            get:         static e => e.IsExpanded,
            set:         static (c, v) => c.IsExpanded = v,
            readBack:    static c => c.IsExpanded,
            subscribe:   static (c, h) => c.Expanding += h,
            callback:    static e => e.OnIsExpandedChanged,
            trampoline:  ExpandingTrampoline,
            slotIsNull:  static p => p.ExpandingTrampoline is null,
            setSlot:     static (p, h) => p.ExpandingTrampoline = h)
        .HandCodedEvent<ExpanderEventPayload,
            TypedEventHandler<WinUI.Expander, WinUI.ExpanderCollapsedEventArgs>>(
            subscribe:        static (c, h) => c.Collapsed += h,
            callbackPresent:  static e => e.OnIsExpandedChanged,
            trampoline:       CollapsedTrampoline,
            slotIsNull:       static p => p.CollapsedTrampoline is null,
            setSlot:          static (p, h) => p.CollapsedTrampoline = h);
}
