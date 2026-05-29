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
// <snippet:children-strategies>
public abstract record ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

/// <summary>Leaf — no children. Engine performs no dispatch beyond the
/// handler's Mount/Update body.</summary>
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
// </snippet:children-strategies>

/// <summary>Named-slot host (SplitView with Pane + Content, NavigationView
/// with Header + Content + PaneFooter, etc.). Each
/// <see cref="NamedSlot{TElement,TControl}"/> binds one slot.</summary>
public sealed record NamedSlots<TElement, TControl>(
    IReadOnlyList<NamedSlot<TElement, TControl>> Slots) : ChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : UIElement;

/// <summary>One named slot on a <see cref="NamedSlots{TElement,TControl}"/>
/// host. <see cref="Name"/> is informational; binding is by lambda.</summary>
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
    /// <summary>
    /// §14 Phase 3 completion — extended to accept the previous element
    /// alongside the new one. <paramref name="oldElement"/> is null on
    /// Mount and set on Update (where the v1 engine has it cheaply at
    /// hand). Keyed-state binders (TemplatedItems / TemplatedItemsErased /
    /// TreeChildren / TabItemsHost) ignore it and continue to read prior
    /// state from the control. Positional binders (PreMountedItems) read
    /// it to drive per-slot Element-aware reconcile via
    /// <see cref="Reconciler.ReconcileV1Child"/>.
    /// </summary>
    void Bind(FrameworkElement control, Element? oldElement, Element element, Reconciler reconciler, Action requestRerender, bool isMount);
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
public sealed record TemplatedItemsErased<TElement, TControl>(
    Func<TElement, Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource> GetSource)
    : ChildrenStrategy<TElement, TControl>, IErasedTemplatedItemsStrategy
    where TElement : Element
    where TControl : FrameworkElement
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element? oldElement, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        _ = oldElement; // keyed-state binder reads prior state from the control
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
public sealed record TemplatedItems<TItem, TElement, TControl>(
    Func<TElement, IReadOnlyList<TItem>> GetItems,
    Func<TItem, int, string> KeySelector,
    Func<TItem, int, Element> BuildItemView)
    : ChildrenStrategy<TElement, TControl>, ITemplatedItemsStrategy
    where TElement : Element
    where TControl : FrameworkElement
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element? oldElement, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        _ = oldElement; // keyed-state binder reads prior state from the control
        var typedEl = (TElement)element;
        var typedCtrl = (TControl)control;
        var items = GetItems(typedEl);
        reconciler.BindKeyedItemsSource(typedCtrl, items, KeySelector, BuildItemView, requestRerender, isMount);
    }
}

/// <summary>Escape hatch — the handler drives child reconciliation
/// imperatively via <see cref="Reconcile"/>. Use sparingly; the typed
/// strategies above cover the 95% case.</summary>
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
// FlipView in the simple (Element[]) case does NOT need a new strategy —
// its flat IList<object> sink shape is already covered by ItemsHost<>,
// with each Element item pre-mounted by the existing ItemsHost dispatch
// body. The simple FlipView descriptor (Port (9)) uses ItemsHost<>
// directly.
//
// The TEMPLATED FlipView (TemplatedFlipViewElement<T>) uses
// PreMountedItems<> (added in Phase 3 completion at the end of this
// file) because items come through IItemViewSource (int ItemCount;
// Element BuildItemView(int)) rather than as a materialized
// IReadOnlyList<object>, and the legacy MountTemplatedFlipView /
// UpdateTemplatedFlipView pair runs positional Element-aware reconcile
// per slot via CanUpdate.

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
public sealed record TreeChildren<TElement, TControl>(
    Func<TElement, IReadOnlyList<TreeViewNodeData>> GetNodes)
    : ChildrenStrategy<TElement, TControl>, IItemsBinderStrategy
    where TElement : Element
    where TControl : WinUI.TreeView
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element? oldElement, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        _ = oldElement; // positional rebuild reads only the new tree
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
/// <para><b>Update semantics:</b> in-place positional reconcile against the
/// previous element. For each shared index the existing container is kept
/// and its <c>Content</c> reconciled through
/// <see cref="Reconciler.ReconcileV1Child"/> (CanUpdate-or-mount-or-unmount);
/// the container's own metadata (header / icon / closable) is refreshed via
/// the optional <see cref="UpdateContainer"/> callback. Excess containers
/// are unmounted + removed; surplus new items are created + appended.
/// Containers (and the mounted Content subtree underneath them) therefore
/// survive renders that don't change the tab/pivot set — re-adding every
/// <c>TabViewItem</c> / <c>PivotItem</c> would re-trigger the tab-strip
/// entrance animation and steal focus from descendant controls. Mirrors the
/// legacy <c>UpdateTabView</c> / <c>UpdatePivot</c> in-place arms.</para>
///
/// <para><b>Not covered (still legacy-only):</b> keyed reorder, pinnable tab
/// headers, and the docking drag pipeline. Index-keyed pairing assumes the
/// item at index <c>i</c> in the old list corresponds to index <c>i</c> in
/// the new list.</para></summary>
public sealed record TabItemsHost<TElement, TControl, TItem>(
    Func<TElement, IReadOnlyList<TItem>> GetItems,
    Func<TControl, IList<object>> GetCollection,
    Func<TItem, Element> GetContent,
    Func<TItem, UIElement?, object> CreateContainer,
    Action<TItem, TItem, object>? UpdateContainer = null)
    : ChildrenStrategy<TElement, TControl>, IItemsBinderStrategy
    where TElement : Element
    where TControl : FrameworkElement
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element? oldElement, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        var typedCtrl = (TControl)control;
        var typedEl = (TElement)element;
        var items = GetItems(typedEl);
        var collection = GetCollection(typedCtrl);

        if (isMount)
        {
            for (int i = 0; i < items.Count; i++)
                collection.Add(CreateContainer(items[i], MountContent(items[i], reconciler, requestRerender)));
            return;
        }

        // Update path. Positional reconcile keyed by index against the
        // previous items list so containers are preserved in place.
        var oldItems = oldElement is TElement oldTyped ? GetItems(oldTyped) : null;

        // Engine-invariant break (no previous element) or collection drift
        // from the previous items count — fall back to a full rebuild rather
        // than index into a mismatched collection.
        if (oldItems is null || collection.Count != oldItems.Count)
        {
            RebuildAll(items, collection, reconciler, requestRerender);
            return;
        }

        int shared = global::System.Math.Min(oldItems.Count, items.Count);
        for (int i = 0; i < shared; i++)
        {
            var oldItem = oldItems[i];
            var newItem = items[i];
            var container = collection[i];

            // Reconcile the per-item Content in place. Only reassign when the
            // realized control actually changed — re-assigning the same
            // UIElement triggers WinUI's logical-tree detach→reattach on the
            // whole subtree, which drops setState queued by descendant
            // handlers before it lands.
            if (container is WinUI.ContentControl cc)
            {
                var existing = cc.Content as UIElement;
                var next = reconciler.ReconcileV1Child(GetContent(oldItem), GetContent(newItem), existing, requestRerender);
                if (!ReferenceEquals(existing, next))
                    cc.Content = next;
            }

            UpdateContainer?.Invoke(oldItem, newItem, container);
        }

        // Remove excess containers (highest index first so removals don't shift).
        for (int i = collection.Count - 1; i >= shared; i--)
        {
            if (collection[i] is WinUI.ContentControl cc && cc.Content is UIElement ui)
                reconciler.UnmountChild(ui);
            collection.RemoveAt(i);
        }

        // Append surplus new items.
        for (int i = shared; i < items.Count; i++)
            collection.Add(CreateContainer(items[i], MountContent(items[i], reconciler, requestRerender)));
    }

    private UIElement? MountContent(TItem item, Reconciler reconciler, Action requestRerender)
    {
        var content = GetContent(item);
        return content is null ? null : reconciler.Mount(content, requestRerender);
    }

    private void RebuildAll(IReadOnlyList<TItem> items, IList<object> collection, Reconciler reconciler, Action requestRerender)
    {
        // Tear down each existing container's mounted content. Both
        // TabViewItem and PivotItem inherit ContentControl, so the engine
        // can reach the previously-mounted UIElement reflection-free.
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (collection[i] is WinUI.ContentControl cc && cc.Content is UIElement ui)
                reconciler.UnmountChild(ui);
            collection.RemoveAt(i);
        }
        for (int i = 0; i < items.Count; i++)
            collection.Add(CreateContainer(items[i], MountContent(items[i], reconciler, requestRerender)));
    }
}

// ════════════════════════════════════════════════════════════════════════
//  §14 Phase 3 completion — PreMountedItems (TemplatedFlipView)
// ════════════════════════════════════════════════════════════════════════

/// <summary>§14 Phase 3 completion — pre-mounted, Element-aware
/// positional items host for templated controls whose host has no
/// <c>ContainerContentChanging</c> (i.e. <see cref="WinUI.FlipView"/>
/// templated peer). Items come from the element's
/// <see cref="Microsoft.UI.Reactor.Core.Internal.IItemViewSource"/>
/// implementation — <c>ItemCount</c> + <c>BuildItemView(int)</c> — and
/// are pre-mounted up-front into the control's flat
/// <c>IList&lt;object&gt;</c> sink (e.g. <c>FlipView.Items</c>). Closes
/// the carry-forward from §14 Phase 3 finish where
/// <c>TemplatedFlipViewElement&lt;T&gt;</c> stayed legacy because the
/// FlipView host can't share the
/// <see cref="TemplatedItemsErased{TElement,TControl}"/> realization
/// pipeline used by ListView / GridView (those route through
/// <see cref="Reconciler.BindKeyedItemsSource"/> which assumes
/// <c>ContainerContentChanging</c>).
///
/// <para><b>Update semantics:</b> positional reconcile against the
/// previous element. For each shared index, the engine helper
/// <see cref="Reconciler.ReconcileV1Child"/> runs the standard
/// CanUpdate-or-Mount-or-Unmount slot decision. Excess old slots are
/// <c>UnmountChild</c>-ed and removed; surplus new slots are mounted
/// and appended. Mirrors the legacy <c>UpdateTemplatedFlipView</c> arm
/// 1:1.</para>
///
/// <para><b>Invariants asserted in Debug:</b> the dispatched old element
/// is the same closed type as the new (the engine's <c>CanUpdate</c>
/// gate guarantees this for Update arms); <c>items.Count</c> tracks
/// the previous source's <c>ItemCount</c>. Release builds full-rebuild
/// when an invariant is violated rather than indexing into a stale
/// collection.</para></summary>
public sealed record PreMountedItems<TElement, TControl>(
    Func<TElement, Microsoft.UI.Reactor.Core.Internal.IItemViewSource> GetSource,
    Func<TControl, IList<object>> GetCollection)
    : ChildrenStrategy<TElement, TControl>, IItemsBinderStrategy
    where TElement : Element
    where TControl : FrameworkElement
{
    void IItemsBinderStrategy.Bind(FrameworkElement control, Element? oldElement, Element element, Reconciler reconciler, Action requestRerender, bool isMount)
    {
        var typedCtrl = (TControl)control;
        var newSource = GetSource((TElement)element);
        var items = GetCollection(typedCtrl);

        if (isMount)
        {
            for (int i = 0; i < newSource.ItemCount; i++)
            {
                var itemElement = newSource.BuildItemView(i);
                var ctrl = reconciler.Mount(itemElement, requestRerender);
                if (ctrl is null)
                    throw new global::System.InvalidOperationException(
                        "PreMountedItems<>: item Element at index " + i + " mounted to a null UIElement. "
                        + "Templated FlipView items must produce a visible control.");
                items.Add(ctrl);
            }
            return;
        }

        // Update path — engine invariant: matching closed-T old element.
        // CanUpdate gates Update arms on identical concrete types, so
        // this Debug assert catches dispatcher / registry bugs only.
        global::System.Diagnostics.Debug.Assert(
            oldElement is TElement,
            "PreMountedItems<>: oldElement is not the closed-T leaf; engine dispatcher invariant broken.");
        if (oldElement is not TElement oldLeaf)
        {
            // Release fallback — full rebuild without assuming positional
            // pairing with the existing UIElements.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] is UIElement orphan) reconciler.UnmountChild(orphan);
                items.RemoveAt(i);
            }
            for (int i = 0; i < newSource.ItemCount; i++)
            {
                var fresh = reconciler.Mount(newSource.BuildItemView(i), requestRerender);
                if (fresh is null)
                    throw new global::System.InvalidOperationException(
                        "PreMountedItems<>: item Element at index " + i + " mounted to a null UIElement.");
                items.Add(fresh);
            }
            return;
        }

        var oldSource = GetSource(oldLeaf);
        int oldCount = oldSource.ItemCount;
        int newCount = newSource.ItemCount;

        // Collection / source-count drift would corrupt positional pairing.
        // Debug-assert; release falls through to the same rebuild path as
        // the type-mismatch case via the count clamp below.
        global::System.Diagnostics.Debug.Assert(
            items.Count == oldCount,
            "PreMountedItems<>: control items collection (" + items.Count
            + ") drifted from previous source ItemCount (" + oldCount + ").");
        if (items.Count != oldCount)
        {
            // Release fallback for count drift — same shape as the
            // type-mismatch rebuild above. Cannot continue positional
            // reconciliation safely: if items.Count > oldSource.ItemCount
            // we'd index past oldSource bounds; if smaller, stale source
            // items would be skipped without element-aware teardown.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] is UIElement orphan) reconciler.UnmountChild(orphan);
                items.RemoveAt(i);
            }
            for (int i = 0; i < newCount; i++)
            {
                var fresh = reconciler.Mount(newSource.BuildItemView(i), requestRerender);
                if (fresh is null)
                    throw new global::System.InvalidOperationException(
                        "PreMountedItems<>: item Element at index " + i + " mounted to a null UIElement.");
                items.Add(fresh);
            }
            return;
        }

        int shared = global::System.Math.Min(oldCount, newCount);
        for (int i = 0; i < shared; i++)
        {
            var oldItem = oldSource.BuildItemView(i);
            var newItem = newSource.BuildItemView(i);
            var existing = items[i] as UIElement;
            var next = reconciler.ReconcileV1Child(oldItem, newItem, existing, requestRerender);
            if (next is null)
                throw new global::System.InvalidOperationException(
                    "PreMountedItems<>: item Element at index " + i + " reconciled to null. "
                    + "Templated FlipView items must produce a visible control.");
            if (!ReferenceEquals(existing, next))
                items[i] = next;
        }

        // Truncate excess (highest-index first so removals don't shift).
        for (int i = oldCount - 1; i >= shared; i--)
        {
            if (items[i] is UIElement old) reconciler.UnmountChild(old);
            items.RemoveAt(i);
        }

        // Append new tail.
        for (int i = shared; i < newCount; i++)
        {
            var ctrl = reconciler.Mount(newSource.BuildItemView(i), requestRerender);
            if (ctrl is null)
                throw new global::System.InvalidOperationException(
                    "PreMountedItems<>: item Element at index " + i + " mounted to a null UIElement.");
            items.Add(ctrl);
        }
    }
}
