using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §4 / §14 Phase 1 (1.6) — author-facing handler contract for
/// porting a control onto the v1 protocol.
///
/// Implementations register themselves via
/// <see cref="Reconciler.RegisterHandler{TElement,TControl}"/>; dispatch
/// is exact-type by <c>element.GetType()</c> (§13 Q17). Update returns
/// <c>void</c> — substitution is forbidden (§13 Q12); a type change
/// flows through the unmount-and-remount path on the wrapping container.
///
/// <para><b>Hot path:</b> dispatch is dictionary lookup + interface call
/// (both already paid by <see cref="V1HandlerRegistry"/>). Implementations
/// should avoid allocations in <see cref="Mount"/> / <see cref="Update"/>
/// bodies; the <c>ref struct</c> context types are allocation-free by
/// construction.</para>
/// </summary>
// <snippet:handler-contract>
[Experimental("REACTOR_V1_PREVIEW")]
public interface IElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    /// <summary>Create or rent the WinUI control, apply initial writes,
    /// and subscribe event trampolines. Returns the control the engine
    /// places into the parent's children collection.</summary>
    TControl Mount(MountContext ctx, TElement element);

    /// <summary>Diff <paramref name="oldEl"/> vs <paramref name="newEl"/>
    /// and apply minimal writes to <paramref name="control"/>. Returns void:
    /// the control identity is preserved (§13 Q12).</summary>
    void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl control);

    /// <summary>Optional override for handlers with explicit teardown
    /// (e.g. control-side IDisposable resources). Default no-op — the
    /// engine still runs the pool reset contract.</summary>
    void Unmount(UnmountContext ctx, TControl control) { }

    /// <summary>Optional override for handlers that want to drive child
    /// reconciliation manually instead of through
    /// <see cref="Children"/>. Phase 1 strategy dispatch routes through
    /// <see cref="Children"/>; this overload is retained for parity with
    /// the spec §4 surface and Phase 3 use.</summary>
    void ReconcileChildren(MountContext ctx, TElement oldEl, TElement newEl, TControl control) { }

    /// <summary>Optional children strategy. Engine dispatches through it
    /// when non-null (and not <see cref="None{TElement,TControl}"/>);
    /// otherwise the handler is a leaf for the purposes of child
    /// reconciliation.</summary>
    ChildrenStrategy<TElement, TControl>? Children => null;

    /// <summary>Spec 047 §14 Phase 3 prelude (Engine A1) — optional hook the
    /// engine invokes from <see cref="V1HandlerAdapter{TElement,TControl}"/>
    /// after the <see cref="Children"/> strategy has mounted/bound every child
    /// (and after any items-binder strategy the handler dispatched inline).
    /// Default no-op.
    ///
    /// <para>Override this to wire events whose subscription must happen
    /// strictly after children-add — e.g. <c>TabView.SelectionChanged</c>,
    /// which WinUI fires spuriously while the first tab is being added if the
    /// handler subscribes during the prop-apply phase. Subscribing here side-
    /// steps that first-add echo.</para></summary>
    void AfterChildrenMount(MountContext ctx, TElement element, TControl control) { }
}
// </snippet:handler-contract>
