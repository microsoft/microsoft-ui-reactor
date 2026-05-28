using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §6 / §14 Phase 1 (1.8) — children-handling strategy declared by
/// a handler. Engine dispatches through the strategy in
/// <see cref="V1HandlerAdapter{TElement,TControl}"/> after the handler's
/// Mount / Update body has returned. Phase 1 ships shape + dispatch; the
/// keyed-reconcile integration with spec-042 lands in Phase 3.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public abstract record ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

/// <summary>Leaf — no children. Engine performs no dispatch beyond the
/// handler's Mount/Update body.</summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record None<TElement, TControl>() : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

/// <summary>Single-content host (Border, ContentControl, Viewbox). The
/// engine mounts <paramref name="GetChild"/>'s result and assigns it via
/// <paramref name="SetChild"/>.
///
/// <para><b>Structural reconcile:</b> set <see cref="GetCurrentChild"/> so
/// the engine can read the existing slot value during <c>Update</c> and
/// route through <c>Reconciler.ReconcileV1Child</c> — that preserves
/// descendant component state across parent re-renders. When left null,
/// the engine remounts the child on every update (only safe for slots that
/// are reset every render anyway). All built-in handlers set it.</para></summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record SingleContent<TElement, TControl>(
    Func<TElement, Element?> GetChild,
    Action<TControl, UIElement?> SetChild) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    /// <summary>Optional: read the current child from the control. Required
    /// for structural child reconciliation; if null the engine falls back to
    /// remounting the child every Update.</summary>
    public Func<TControl, UIElement?>? GetCurrentChild { get; init; }
}

/// <summary>Panel host (StackPanel, Grid, Canvas). Engine mounts each
/// child and appends to the panel's <see cref="UIElementCollection"/>.
///
/// <para><b>Phase 1 limitation:</b> the dispatch is append-only; structural
/// diff against the previous render goes through the host's child
/// collection wholesale. Spec-042 keyed-reconcile integration is a
/// Phase 3 follow-up.</para>
///
/// <para><b>§14 Phase 3-final addition:</b>
/// <see cref="PerChildAttached"/> — optional callback invoked after each
/// child mount AND after each child Update. Receives the mounted
/// <see cref="UIElement"/> alongside the child element so the descriptor
/// can write WinUI attached DPs (e.g. <c>Grid.SetRow</c>,
/// <c>Canvas.SetLeft</c>) based on attached-prop hints carried on the
/// child element. No-op by default.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record Panel<TElement, TControl>(
    Func<TElement, IReadOnlyList<Element>> GetChildren,
    Func<TControl, UIElementCollection> GetCollection) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    /// <summary>Optional per-child attached-prop writer. Called by the
    /// engine after each child is mounted (Mount path) AND after each child
    /// is reconciled (Update path). The descriptor reads attached-prop
    /// hints off the child element (via <c>Element.GetAttached&lt;T&gt;()</c>
    /// or similar) and writes the corresponding WinUI attached DPs onto
    /// the mounted <see cref="UIElement"/>. Defaults to <c>null</c> for
    /// containers that don't carry per-child attached props
    /// (e.g. <c>StackPanel</c>).</summary>
    public Action<TControl, UIElement, Element>? PerChildAttached { get; init; }
}

/// <summary>Named-slot host (SplitView with Pane + Content, NavigationView
/// with Header + Content + PaneFooter, etc.). Each
/// <see cref="NamedSlot{TElement,TControl}"/> binds one slot.</summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record NamedSlots<TElement, TControl>(
    IReadOnlyList<NamedSlot<TElement, TControl>> Slots) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

/// <summary>One named slot on a <see cref="NamedSlots{TElement,TControl}"/>
/// host. <see cref="Name"/> is informational; binding is by lambda.</summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record NamedSlot<TElement, TControl>(
    string Name,
    Func<TElement, Element?> GetChild,
    Action<TControl, UIElement?> SetChild)
    where TElement : Element
    where TControl : UIElement
{
    /// <summary>Optional: read the current child from the control. Required
    /// for structural child reconciliation; same contract as
    /// <see cref="SingleContent{TElement,TControl}.GetCurrentChild"/>.</summary>
    public Func<TControl, UIElement?>? GetCurrentChild { get; init; }
}

/// <summary>Items host for controls whose items collection is a flat
/// <c>IList&lt;object&gt;</c> sink the descriptor populates directly —
/// <c>ListBox</c>, <c>ComboBox.Items</c>, <c>RadioButtons.Items</c>, and
/// any future control with a non-virtualizing items collection.
///
/// <para><b>§14 Phase 3-final shape:</b>
/// <list type="bullet">
///   <item><see cref="GetItems"/> projects the element's items (typically
///   <c>string[]</c> or <c>Element[]</c>) as an <c>IReadOnlyList&lt;object&gt;</c>.</item>
///   <item><see cref="GetCollection"/> resolves the control's WinUI
///   <c>ItemCollection</c> / <c>IList&lt;object&gt;</c> sink (e.g.
///   <c>cb =&gt; cb.Items</c>).</item>
///   <item><see cref="ItemEquals"/> optional per-item equality check used
///   to short-circuit Mount/Update when the items collection hasn't
///   structurally changed. Defaults to
///   <see cref="object.Equals(object,object)"/>.</item>
/// </list></para>
///
/// <para><b>Mount semantics:</b> Clear the collection and Add each item
/// once. Element items are mounted through the reconciler first; string
/// items are passed through.</para>
///
/// <para><b>Update semantics:</b> If <see cref="ItemEquals"/> reports the
/// collections are positionally equal, no-op. Otherwise rebuild positionally
/// (clear + add). Spec-042 keyed-reconcile integration for typed templated
/// lists lands separately with the
/// <c>ListView&lt;T&gt;</c>/<c>GridView&lt;T&gt;</c> ports in Batch G2.</para></summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record ItemsHost<TElement, TControl>(
    Func<TElement, IReadOnlyList<object>> GetItems,
    Func<TControl, IList<object>> GetCollection) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    /// <summary>Optional per-item equality predicate. When provided, an
    /// Update that compares equal element-by-element skips the rebuild.
    /// Default = reference + value equality via
    /// <see cref="object.Equals(object,object)"/>.</summary>
    public Func<object?, object?, bool>? ItemEquals { get; init; }
}

/// <summary>Placeholder for future ItemsHost options (virtualization mode,
/// container template etc.). Phase 1 carries no fields — Phase 3 may add
/// when more handler authors arrive. Retained for source compat with the
/// Phase 1 ItemsHost shape.</summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record ItemsHostOptions;

/// <summary>Escape hatch — the handler drives child reconciliation
/// imperatively via <see cref="Reconcile"/>. Use sparingly; the typed
/// strategies above cover the 95% case.</summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record Imperative<TElement, TControl>(
    Action<MountContext, TElement, TElement, TControl> Reconcile) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;
