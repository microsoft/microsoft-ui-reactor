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
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record Panel<TElement, TControl>(
    Func<TElement, IReadOnlyList<Element>> GetChildren,
    Func<TControl, UIElementCollection> GetCollection) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

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

/// <summary>Templated items host (ListView, GridView, ItemsRepeater).
/// Phase 1 ships shape only; the actual reconciliation flows through the
/// existing <c>ChildReconciler</c> path (spec 042). The strategy is
/// retained as a shape contract — the LISTVIEW port in 1.15 will refine
/// what its lambdas hand back to the engine.</summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record ItemsHost<TElement, TControl>(
    Func<TElement, global::System.Collections.IEnumerable> GetItemsSource,
    Func<TControl, object> GetContainer,
    ItemsHostOptions Options) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

/// <summary>Placeholder for future ItemsHost options (virtualization mode,
/// container template etc.). Phase 1 carries no fields — Phase 3 may add
/// when more handler authors arrive.</summary>
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
