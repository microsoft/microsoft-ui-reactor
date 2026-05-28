using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

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
///
/// <para><b>§14 Phase 3 close-out addition:</b>
/// <see cref="PerChildAttachedAfterAll"/> — optional two-pass callback
/// invoked once after every child has been mounted (Mount path) or
/// reconciled (Update path). Receives the full <c>(UIElement, Element)</c>
/// pair list in collection order so the descriptor can apply attached
/// DPs that reference OTHER children by name (e.g.
/// <c>RelativePanel.SetRightOf(b, a)</c>). Distinct from
/// <see cref="PerChildAttached"/>, which fires per-child mid-pass and
/// cannot see siblings that haven't mounted yet. Most descriptors set
/// only one of the two; <c>RelativePanel</c> is the canonical consumer
/// of the after-all shape.</para>
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

    /// <summary>Optional two-pass callback fired once after every child has
    /// been mounted/reconciled, receiving the full ordered list of
    /// <c>(UIElement, Element)</c> pairs. Use for attached DPs that
    /// reference siblings by name — e.g. <c>RelativePanel.SetRightOf</c>.
    /// Defaults to <c>null</c>; only RelativePanel-shaped descriptors set
    /// it.</summary>
    public Action<TControl, IReadOnlyList<(UIElement Mounted, Element ChildElement)>>? PerChildAttachedAfterAll { get; init; }
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

/// <summary>§14 Phase 3 finish — single dispatch marker for every
/// items-binder strategy variant. The engine probes this base interface
/// ONCE at the top of <c>V1HandlerAdapter.DispatchChildrenMount</c> /
/// <c>DispatchChildrenUpdate</c> and <c>DescriptorHandler.Mount</c> /
/// <c>Update</c> instead of running an <c>is</c>-check per concrete
/// strategy type. Keeps the M1 dispatch cost constant as new strategies
/// (templated/erased today; tree/tab/pivot to come) implement the base.
///
/// <para>The two existing variants <see cref="ITemplatedItemsStrategy"/>
/// and <see cref="IErasedTemplatedItemsStrategy"/> stay as named handles
/// for the implementing strategy types (so the type system still tells
/// the closed-TItem vs erased-TItem story at the strategy declaration
/// site), but the dispatch path only walks the base.</para></summary>
internal interface IItemsBinderStrategy
{
    void Bind(FrameworkElement control, Element element, Reconciler reconciler, Action requestRerender, bool isMount);
}

/// <summary>Non-generic marker the engine dispatcher uses to reach an
/// open-<c>TItem</c> templated-items strategy from the closed
/// <c>(TElement, TControl)</c> adapter. Implemented by every
/// <see cref="TemplatedItems{TItem,TElement,TControl}"/> instance.</summary>
internal interface ITemplatedItemsStrategy : IItemsBinderStrategy { }

/// <summary>§14 Phase 3 close-out — non-generic marker for the
/// T-erased templated-items strategy. <see cref="TemplatedItemsErased{TElement,TControl}"/>
/// projects items through the element's <see cref="Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource"/>
/// implementation rather than carrying TItem at the strategy level —
/// matching the legacy <see cref="TemplatedListElementBase"/> erasure
/// model so a single descriptor registration on a non-generic base
/// catches every closed-T variant.</summary>
internal interface IErasedTemplatedItemsStrategy : IItemsBinderStrategy { }

/// <summary>§14 Phase 3 close-out — keyed templated-items host that
/// erases the per-item type at the strategy level by reading items
/// through the element's <see cref="Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource"/>
/// implementation. The element is the carrier of T; the descriptor is
/// non-generic in TItem.
///
/// <para>This is the shape used to port the existing
/// <c>TemplatedListViewElement&lt;T&gt;</c> / <c>TemplatedGridViewElement&lt;T&gt;</c>
/// family — registered once on a non-generic intermediate base
/// (<see cref="TemplatedListViewElementBase"/> / <see cref="TemplatedGridViewElementBase"/>)
/// via <see cref="Reconciler.RegisterHandlerForDerivedTypes"/>; the
/// engine's base-derived registry walk routes every closed-T variant to
/// the same descriptor. Same realization plumbing as
/// <see cref="TemplatedItems{TItem,TElement,TControl}"/>:
/// <see cref="Reconciler.BindKeyedItemsSource"/> owns the
/// <c>ReactorListState</c> + <c>KeyedListDiff</c> + container-realization
/// pipeline.</para></summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record TemplatedItemsErased<TElement, TControl>(
    Func<TElement, Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource> GetSource)
    : ChildrenStrategy<TElement, TControl>, IErasedTemplatedItemsStrategy
    where TElement : Element
    where TControl : FrameworkElement
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        var typedEl = (TElement)element;
        var typedCtrl = (TControl)control;
        var source = GetSource(typedEl);
        reconciler.BindErasedKeyedItemsSource(typedCtrl, source, requestRerender, isMount);
    }
}

/// <summary>Keyed templated-items host for descriptor-driven typed lists
/// (<c>ListView&lt;T&gt;</c>, <c>GridView&lt;T&gt;</c>, future
/// <c>LazyVStack&lt;T&gt;</c> / <c>LazyHStack&lt;T&gt;</c> /
/// <c>ItemsRepeater&lt;T&gt;</c>). The strategy declares only the data
/// shape; the engine partial <see cref="Reconciler.BindKeyedItemsSource"/>
/// owns the realization plumbing (spec-042 <c>ReactorListState</c> +
/// <c>KeyedListDiff</c>, the shared <c>ContainerContentChanging</c>
/// handler, the per-control <c>ItemsSource</c> binding).
///
/// <para><b>Lifecycle:</b> Mount runs once on first render; Update runs on
/// every subsequent render with the same control instance. Both go
/// through <see cref="Reconciler.BindKeyedItemsSource"/>; the boolean
/// <c>isMount</c> parameter selects between fresh-state construction
/// (Mount) and keyed-diff application (Update).</para>
///
/// <para><b>Key contract:</b> <see cref="KeySelector"/> must produce
/// stable, non-null, non-duplicate strings across renders for any given
/// user item. Null / duplicate keys trigger the same <see cref="P:KeyedListDiff.DiffStats.Bailout"/>
/// path the legacy element-based binder uses — correctness preserved,
/// animation degraded for that diff.</para>
///
/// <para><b>Per-item Element:</b> <see cref="BuildItemView"/> is called
/// lazily by the realization machinery as containers materialize, so
/// large lists never realize all items up front. The returned
/// <see cref="Element"/> is reconciled into the container's
/// <c>ContentControl</c>.</para></summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record TemplatedItems<TItem, TElement, TControl>(
    Func<TElement, IReadOnlyList<TItem>> GetItems,
    Func<TItem, int, string> KeySelector,
    Func<TItem, int, Element> BuildItemView)
    : ChildrenStrategy<TElement, TControl>, ITemplatedItemsStrategy
    where TElement : Element
    where TControl : FrameworkElement
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        var typedEl = (TElement)element;
        var typedCtrl = (TControl)control;
        var items = GetItems(typedEl);
        reconciler.BindKeyedItemsSource(typedCtrl, items, KeySelector, BuildItemView, requestRerender, isMount);
    }
}

/// <summary>Escape hatch — the handler drives child reconciliation
/// imperatively via <see cref="Reconcile"/>. Use sparingly; the typed
/// strategies above cover the 95% case.</summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record Imperative<TElement, TControl>(
    Action<MountContext, TElement, TElement, TControl> Reconcile) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

// ════════════════════════════════════════════════════════════════════════
//  §14 Phase 3 finish — G3 children strategies (TreeView, TabView, Pivot)
// ════════════════════════════════════════════════════════════════════════
//
// All three implement IItemsBinderStrategy so dispatch goes through the
// single consolidated arm in V1HandlerAdapter / DescriptorHandler — same
// shape as TemplatedItems / TemplatedItemsErased.
//
// FlipView does NOT need a new strategy — its flat IList<object> sink
// shape is already covered by ItemsHost<>, with each Element item
// pre-mounted by the existing ItemsHost dispatch body. FlipView
// descriptor (Port (9)) uses ItemsHost<> directly.

/// <summary>§14 Phase 3 finish — Port (8). Hierarchical children for
/// <see cref="WinUI.TreeView"/>. The strategy declares only the data
/// shape (a <see cref="TreeViewNodeData"/> tree); the engine builds a
/// matching <see cref="WinUI.TreeViewNode"/> tree on
/// <see cref="WinUI.TreeView.RootNodes"/>, mounting per-node
/// <c>ContentElement</c> through the reconciler when any node uses one.
///
/// <para><b>MVP scope:</b> positional rebuild on Update — old
/// <c>ContentElement</c> subtrees are unmounted and the WinUI tree is
/// reconstructed. No keyed reconcile (descendant component state
/// inside ContentElement nodes is lost across renders that touch the
/// tree). Same correctness contract as the legacy
/// <c>UpdateTreeView</c> arm.</para></summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record TreeChildren<TElement, TControl>(
    Func<TElement, IReadOnlyList<TreeViewNodeData>> GetNodes)
    : ChildrenStrategy<TElement, TControl>, IItemsBinderStrategy
    where TElement : Element
    where TControl : WinUI.TreeView
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        var tree = (TControl)control;
        var nodes = GetNodes((TElement)element);
        bool hasContentElements = HasAnyContentElement(nodes);

        if (isMount)
        {
            // Pick the ItemTemplate once at mount based on whether any
            // node uses ContentElement — mirrors MountTreeView's choice
            // between the text-bound template and the ContentControl shell.
            tree.ItemTemplate = hasContentElements
                ? Reconciler.SharedContentControlTemplate.Value
                : Reconciler.TreeViewTextItemTemplate.Value;
        }
        else
        {
            // Update — tear down any previously mounted ContentElement UI
            // subtrees before clearing the WinUI tree, so descendant
            // unmount hooks fire. Same teardown the legacy arm performs.
            UnmountTreeContent(tree.RootNodes, reconciler);
            tree.RootNodes.Clear();
            // ItemTemplate may flip if the new tree gained / lost any
            // ContentElement. Assigning the same Lazy<DataTemplate> is
            // a no-op identity write.
            tree.ItemTemplate = hasContentElements
                ? Reconciler.SharedContentControlTemplate.Value
                : Reconciler.TreeViewTextItemTemplate.Value;
        }

        for (int i = 0; i < nodes.Count; i++)
            tree.RootNodes.Add(CreateTreeNode(nodes[i], hasContentElements, reconciler, requestRerender));
    }

    private static bool HasAnyContentElement(IReadOnlyList<TreeViewNodeData> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.ContentElement is not null) return true;
            if (n.Children is not null && HasAnyContentElement(n.Children)) return true;
        }
        return false;
    }

    private static WinUI.TreeViewNode CreateTreeNode(TreeViewNodeData data, bool mountElements, Reconciler reconciler, Action requestRerender)
    {
        var node = new WinUI.TreeViewNode { IsExpanded = data.IsExpanded };
        if (mountElements && data.ContentElement is not null)
            node.Content = reconciler.Mount(data.ContentElement, requestRerender);
        else
            node.Content = data;
        if (data.Children is not null)
            for (int i = 0; i < data.Children.Length; i++)
                node.Children.Add(CreateTreeNode(data.Children[i], mountElements, reconciler, requestRerender));
        return node;
    }

    private static void UnmountTreeContent(IList<WinUI.TreeViewNode> nodes, Reconciler reconciler)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Content is UIElement ui) reconciler.UnmountChild(ui);
            UnmountTreeContent(node.Children, reconciler);
        }
    }
}

/// <summary>§14 Phase 3 finish — Ports (10) + (11). Heterogeneous
/// items host for <see cref="WinUI.TabView"/> and <see cref="WinUI.Pivot"/>.
/// Each item declares a header + an Element content + (TabView-only)
/// an optional IsClosable / icon hint; the strategy mounts the content,
/// builds the per-control container (e.g. <c>WinUI.TabViewItem</c>),
/// and adds it to the host's items sink.
///
/// <para><b>MVP scope:</b> positional rebuild on Update — every container
/// is unmounted + remounted. No keyed reconcile (descendant component
/// state inside the per-item Content is lost across renders that touch
/// the tab set). Matches the legacy <c>UpdateTabView</c> / <c>UpdatePivot</c>
/// rebuild semantics for the common case.</para></summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed record TabItemsHost<TElement, TControl, TItem>(
    Func<TElement, IReadOnlyList<TItem>> GetItems,
    Func<TControl, IList<object>> GetCollection,
    Func<TItem, Element> GetContent,
    Func<TItem, UIElement?, object> CreateContainer)
    : ChildrenStrategy<TElement, TControl>, IItemsBinderStrategy
    where TElement : Element
    where TControl : FrameworkElement
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        var typedCtrl = (TControl)control;
        var typedEl = (TElement)element;
        var items = GetItems(typedEl);
        var collection = GetCollection(typedCtrl);

        if (!isMount && collection.Count > 0)
        {
            // Tear down each existing container's mounted content. Each
            // container is a per-host concrete WinUI type (TabViewItem /
            // PivotItem) — the engine doesn't know its shape, but both
            // have a Content property holding the previously-mounted
            // UIElement. Reflection-free walk: cast to ContentControl
            // (both inherit from it) and read Content.
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i] is WinUI.ContentControl cc && cc.Content is UIElement ui)
                    reconciler.UnmountChild(ui);
            }
            collection.Clear();
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var content = GetContent(item);
            var mounted = content is null ? null : reconciler.Mount(content, requestRerender);
            var container = CreateContainer(item, mounted);
            collection.Add(container);
        }
    }
}
