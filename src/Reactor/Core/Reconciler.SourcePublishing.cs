using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: ReactorDiagnostics.SourceProperty publishing and keyed item identity (diagnostics mode).
public sealed partial class Reconciler
{
    // ── ReactorDiagnostics.SourceProperty publishing (diagnostics mode only) ─────
    // Every touch of these is behind ReactorFeatures.DevtoolsSupported && IsEnabled, so
    // a build without Reactor.DevtoolsSupport trims it and a Release JIT app folds it away.

    /// <summary>The component whose render output is being mounted / reconciled right now.</summary>
    private string? _diagOwner;

    /// <summary>
    /// Owner name for the host's root render output (the root component, or
    /// <c>FuncElement</c> for a root render function). Set by the host for the duration of
    /// its render pass only, and cleared after it.
    /// </summary>
    internal string? DiagnosticRootOwner { get; set; }

    /// <summary>
    /// Owner for elements realized now: the component being rendered, else the root during
    /// the host's render pass. Outside it (an ItemsRepeater realizing or reusing a row during
    /// layout, which also runs <c>Reconcile</c>) the owner is unknown and the field is
    /// omitted rather than guessed.
    /// </summary>
    private string? CurrentDiagnosticOwner => _diagOwner ?? DiagnosticRootOwner;

    /// <summary>
    /// The component whose render output is being reconciled, for diagnostics that name it
    /// (keyed-list warnings, menu / command-bar item identity).
    /// Known only while sources are published (an inspector is attached); <c>null</c> otherwise.
    /// </summary>
    internal string? DiagnosticComponent =>
        global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported && Diagnostics.ReactorSourcePublisher.IsEnabled
            ? CurrentDiagnosticOwner
            : null;

    /// <summary>The component and C# call site a keyed-list diagnostic for <paramref name="list"/> names.</summary>
    internal Internal.KeyedListAttribution KeyedListAttributionFor(Element? list)
        => new(DiagnosticComponent, Internal.KeyedListAttribution.CallSiteOf(list));

    /// <summary>
    /// Builds the row view at <paramref name="index"/> of a templated list (<c>ListView&lt;T&gt;</c>,
    /// <c>GridView&lt;T&gt;</c>, <c>FlipView&lt;T&gt;</c>) and stamps the item's <c>KeySelector</c>
    /// key onto it, as <c>VirtualList</c> / <c>LazyVStack</c> rows already are (issue #326), so
    /// the realized row carries its key for diagnostics (<c>key=</c> in its published
    /// <c>ReactorSource</c>, <see cref="Element.Key"/> on its element tag). A ListView / GridView
    /// container follows its item through the keyed collection diff, so the stamped key matches
    /// across updates. An author's own <c>.WithKey(...)</c> on the row wins.
    /// </summary>
    internal static Element BuildKeyedItemView(Internal.IItemViewSource source, int index)
        => WithItemKey(source.BuildItemView(index), source, index);

    /// <summary>
    /// <paramref name="built"/> (the row view at <paramref name="index"/>) with the item's
    /// <c>KeySelector</c> key, unless the author keyed it already. See <see cref="BuildKeyedItemView"/>.
    /// </summary>
    internal static Element WithItemKey(Element built, Internal.IItemViewSource source, int index)
    {
        if (built.Key is not null || source is not Internal.IKeyedItemSource keyed) return built;
        // A legacy ModifiedElement mounts (and tags) its inner element: an author's key there wins.
        if (built is ModifiedElement && InnermostOf(built).Key is not null) return built;
        var key = source is TemplatedListElementBase templated ? templated.GetItemIdentityKey(index) : keyed.GetKeyAt(index);
        return key is not null ? WithKeyThrough(built, key) : built;
    }

    private static Element InnermostOf(Element element)
    {
        while (element is ModifiedElement modified) element = modified.Inner;
        return element;
    }

    // The key on the element the parent diffs (the outermost) and on the one Mount tags and
    // publishes (the innermost of a ModifiedElement chain).
    private static Element WithKeyThrough(Element element, string key)
        => element is ModifiedElement modified
            ? modified with { Key = key, Inner = WithKeyThrough(modified.Inner, key) }
            : element with { Key = key };

    private void PublishSource(UIElement control, Element element)
    {
        var kind = element;
        while (kind is ModifiedElement modified) kind = modified.Inner;
        // A KeyedMemoElement realizes its factory output, which published itself.
        if (kind is KeyedMemoElement) return;
        Component? component = kind is ComponentElement && _componentNodes.TryGetValue(control, out var node)
            ? node.Component
            : null;
        var value = Diagnostics.ReactorSourcePublisher.Publish(control, element, CurrentDiagnosticOwner, component);
        KeepTagIfSourceAmbiguous(control, element, value);
        MirrorOntoLiveDialog(control, value);
    }

    /// <summary>
    /// A ContentDialog element realizes a collapsed placeholder in the tree and shows a separate
    /// WinUI ContentDialog; the visible dialog carries the placeholder's published value too.
    /// Only a ContentDialog placeholder has a live dialog, so no element check is needed (a
    /// keyed-memo root's realized output is described by its value, not by re-running it).
    /// </summary>
    private static void MirrorOntoLiveDialog(UIElement control, string value)
    {
        if (control is FrameworkElement placeholder
            && V1Protocol.OverlayLifecycle.PeekLiveContentDialog(placeholder) is { } dialog
            && !string.Equals(dialog.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) as string, value, StringComparison.Ordinal))
            dialog.SetValue(Diagnostics.ReactorDiagnostics.SourceProperty, value);
    }

    /// <summary>
    /// <see cref="SkipsCallSiteOnlyTags"/> mode resolves <c>GetSource</c> from the published
    /// value. When that value's <c>at=</c> text is shared by different call sites it cannot,
    /// so such a control is tagged after all — unless it already has an element (a
    /// decorator's own tag, which must not be replaced).
    /// </summary>
    private static void KeepTagIfSourceAmbiguous(UIElement control, Element element, string value)
    {
        if (!Diagnostics.ReactorSourcePublisher.SkipsCallSiteOnlyTags
            || !Diagnostics.ReactorSourcePublisher.IsAmbiguous(value)
            || control is not FrameworkElement fe)
            return;
        if (TryGetReactorState(fe, out var state))
        {
            if (state.DecoratorElement is null) state.Element ??= element;
        }
        else
            SetElementTag(fe, element);
    }

    /// <summary>
    /// Shallow-skip refresh: the element was skipped, so only its call site can have moved.
    /// The skip happens inside its parent's reconcile, under the same owner as an update.
    /// </summary>
    internal void PublishSourceOnSkip(UIElement control, Element newEl) => PublishSource(control, newEl);

    /// <summary>
    /// The key a templated-list item's realized element carries: the stamped item key, or an
    /// author's own key on a wrapped (<see cref="ModifiedElement"/>) inner element, which
    /// <see cref="WithItemKey"/> leaves in place.
    /// </summary>
    internal static string? ItemKeyOf(Element itemElement) => itemElement.Key ?? InnermostOf(itemElement).Key;

    /// <summary>
    /// After a templated-list item view was mounted or updated into <paramref name="control"/>:
    /// makes the control's identity (tag and, in diagnostics mode, published value) carry the
    /// item's key when it does not. A forwarding <c>RegisterType</c> row keeps its child's tag,
    /// which the item key never reached, and an update the reconciler skipped (a different item
    /// with identical content) may refresh the tag without republishing — so
    /// <paramref name="priorKey"/>, the key the control showed before the update, forces a
    /// refresh whenever the item key changed.
    /// </summary>
    internal void EnsureItemIdentity(UIElement? control, Element itemElement, string? priorKey = null, bool updated = false)
    {
        if (control is not FrameworkElement fe) return;
        var key = ItemKeyOf(itemElement);
        if (key is null && !updated) return;
        if (!string.Equals(GetMountedElement(fe)?.Key, key, StringComparison.Ordinal)
            || (updated && !string.Equals(priorKey, key, StringComparison.Ordinal))
            || (PublishesItemIdentity && fe.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) is string published
                && !Diagnostics.ReactorSourcePublisher.HasKey(published, key)))
            RefreshItemIdentityOnSkip(fe, itemElement);
    }

    private static bool PublishesItemIdentity
        => global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported && Diagnostics.ReactorSourcePublisher.IsEnabled;

    /// <summary>
    /// Publishes <paramref name="element"/> with the item <paramref name="key"/>, unless the
    /// control's value describes another element: a forwarding <c>RegisterType</c> row's child,
    /// which in Native AOT tag-skip mode is untagged (so the outer element holds the tag) while
    /// its value is still its own. That value is re-keyed in place, keeping the child's kind and
    /// call site.
    /// </summary>
    private void PublishItemIdentity(FrameworkElement control, Element element, string? key)
    {
        if (control.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) is string current
            && ForwardsAnotherElement(control, element))
        {
            var rekeyed = Diagnostics.ReactorSourcePublisher.WithKey(current, key);
            if (!string.Equals(rekeyed, current, StringComparison.Ordinal))
                control.SetValue(Diagnostics.ReactorDiagnostics.SourceProperty, rekeyed);
            return;
        }
        PublishSource(control, element);
    }

    /// <summary>
    /// Refreshes a reused control's identity — element tag and, in diagnostics mode, its
    /// published value — to the item key of <paramref name="newEl"/> (see <see cref="ItemKeyOf"/>).
    /// </summary>
    internal void RefreshItemIdentityOnSkip(FrameworkElement control, Element newEl)
    {
        var kind = InnermostOf(newEl);
        var key = ItemKeyOf(newEl);
        bool publish = PublishesItemIdentity;
        // The control can realize an element other than the item's: a keyed Memo's factory
        // output, or the child a forwarding RegisterType callback handed back (whose tag
        // TypeRegistration.TagControl keeps). The control's trampolines and unmount read that
        // element, so re-key it and describe it; the item element never replaces it.
        if (TryGetReactorState(control, out var state) && (state.DecoratorElement ?? state.Element) is { } realized
            && (kind is KeyedMemoElement || realized.GetType() != kind.GetType()))
        {
            if (!string.Equals(realized.Key, key, StringComparison.Ordinal))
            {
                realized = realized with { Key = key };
                if (state.DecoratorElement is not null)
                    state.DecoratorElement = realized;
                else state.Element = realized;
            }
            if (publish) PublishItemIdentity(control, realized, key);
            return;
        }
        // An untagged keyed Memo output has nothing to re-key, and its wrapper is never the tag.
        if (kind is KeyedMemoElement) return;
        // A legacy ModifiedElement mounts (and tags) its inner element, so that element is
        // re-tagged with the item key.
        var target = string.Equals(kind.Key, key, StringComparison.Ordinal) ? kind : kind with { Key = key };
        RefreshElementTagOnSkip(control, state?.DecoratorElement ?? state?.Element, target, tagOnly: true);
        if (publish) PublishItemIdentity(control, target, key);
    }

    /// <summary>
    /// ItemsRepeater adoption (<c>TryAdoptRealizedReplacement</c>) moves a fresh component
    /// subtree into the still-realized wrapper, but not the wrapper's own attached values.
    /// The replacement wrapper was published when it mounted; carry that value over so the
    /// live wrapper describes the element it now hosts (its new key, for one).
    /// </summary>
    internal static void AdoptPublishedSource(UIElement adopted, UIElement replacement)
    {
        if (replacement.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) is string value)
            adopted.SetValue(Diagnostics.ReactorDiagnostics.SourceProperty, value);
    }

    /// <summary>The root name of the last <see cref="PublishRootSource"/> (a string; no control is retained).</summary>
    private string? _publishedRootName;

    /// <summary>Host hook: describes the root content control, naming the host's root component.</summary>
    internal void PublishRootSource(UIElement? control, Element tree, string rootName, string? rootHooks)
    {
        if (control is null) return;
        RefreshStaleFacts(control);
        if (_publishedRootName is { } previousRoot && !string.Equals(previousRoot, rootName, StringComparison.Ordinal))
            RenameRootOwner(control, previousRoot, rootName);
        _publishedRootName = rootName;
        // A root wrapped in modifiers (ModifiedElement) is still the kind it wraps.
        var kind = tree;
        while (kind is ModifiedElement modified) kind = modified.Inner;
        Component? component = kind is ComponentElement && _componentNodes.TryGetValue(control, out var node)
            ? node.Component
            : null;
        // The control's current value is the baseline: the update path may have rewritten the
        // plain value, so the root value is written whenever it differs. No reference to the
        // root is kept, so a disposed host's previous subtree is not retained.
        var current = control.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) as string;
        // Keyed-memo roots and forwarding RegisterType roots realize a control that describes
        // another element (the factory output; the forwarded child): only the host-root fields
        // are added to its value, never a re-description of the root element.
        if (kind is KeyedMemoElement || IsForwardingRegisteredRoot(control, kind))
        {
            var withRoot = Diagnostics.ReactorSourcePublisher.WithRoot(
                current, rootName, rootHooks, _hostAddedRootHooks, out var addedHooks);
            _hostAddedRootHooks = addedHooks ? rootHooks : null;
            if (withRoot is null) return;
            if (!string.Equals(withRoot, current, StringComparison.Ordinal))
                control.SetValue(Diagnostics.ReactorDiagnostics.SourceProperty, withRoot);
            MirrorOntoLiveDialog(control, withRoot);
            return;
        }
        _hostAddedRootHooks = null;
        var value = Diagnostics.ReactorSourcePublisher.Publish(control, tree, rootName, component, rootName, rootHooks, current);
        KeepTagIfSourceAmbiguous(control, tree, value);
        MirrorOntoLiveDialog(control, value);
    }

    /// <summary>
    /// The <c>hooks=</c> value <see cref="PublishRootSource"/> added to a keyed-memo root's
    /// value on its last pass (the realized output had none of its own), so a remounted root
    /// replaces or removes it instead of keeping the previous root's hooks.
    /// </summary>
    private string? _hostAddedRootHooks;

    /// <summary>The <c>ReactorSourceMap.StaticFactsRevision</c> this host's published values reflect.</summary>
    private int _seenFactsRevision;

    /// <summary>
    /// Static facts (names, hooks) already published may have gone stale since the last pass: a
    /// late source-mapped assembly made a file unattributable, or a hot-reload update made them
    /// all unknown. Controls whose call site, key and kind did not change, and skipped
    /// subtrees, are not re-published, so the whole tree is walked once per change (rare) and
    /// the stale fields are dropped.
    /// </summary>
    /// <summary>
    /// Host hook: a root rendered null and kept its content, which nothing else re-publishes,
    /// so the content's stale static facts are dropped here (see <see cref="RefreshStaleFacts"/>).
    /// </summary>
    internal void RefreshRetainedContentFacts(UIElement? control)
    {
        if (control is not null) RefreshStaleFacts(control);
    }

    private void RefreshStaleFacts(UIElement control)
    {
        int revision = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.StaticFactsRevision;
        if (revision == _seenFactsRevision) return;
        _seenFactsRevision = revision;
        // Each control's exact call site (its tag where it has one), not the first site recorded
        // for its at= text: two files can share that text, and only one may have gone stale.
        WalkPublished(control, stopAtBoundaries: false, static (node, value) =>
            Diagnostics.ReactorSourcePublisher.WithoutStaleFacts(value,
                node is UIElement ui ? global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(ui) : null));
    }

    /// <summary>
    /// Host hook: the root threw and the app's <c>RenderErrorHandler</c> supplied a fallback
    /// tree, which now stands in for the root's content. Its controls describe the fallback
    /// (published as it mounted); the content root additionally gets the host-root fields, so
    /// an inspector still sees which root failed. Same shape as a keyed-memo root: only
    /// <c>root=</c> (and the root's <c>hooks=</c>, when the value has none) are added.
    /// </summary>
    internal void PublishFallbackRootSource(UIElement? control, string rootName, string? rootHooks)
    {
        if (control is null) return;
        RefreshStaleFacts(control);
        _publishedRootName = rootName;
        var current = control.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) as string;
        var withRoot = Diagnostics.ReactorSourcePublisher.WithRoot(
            current, rootName, rootHooks, _hostAddedRootHooks, out var addedHooks);
        _hostAddedRootHooks = addedHooks ? rootHooks : null;
        if (withRoot is null) return;
        if (!string.Equals(withRoot, current, StringComparison.Ordinal))
            control.SetValue(Diagnostics.ReactorDiagnostics.SourceProperty, withRoot);
        MirrorOntoLiveDialog(control, withRoot);
    }

    /// <summary>
    /// Host hook: the host mounted a different root over content it kept (an in-place update).
    /// Root-owned controls whose call site, key and kind did not change were not re-published,
    /// so they still name the previous root. Rewrites <c>owner=</c> on the content's root-owned
    /// controls: the visual tree plus the Reactor subtrees hung off it outside the visual tree
    /// (flyout content and pass-through element, menu and command items, popup child, a
    /// showing dialog's content, cached navigation pages). A component wrapper (<c>mounts=</c>) is renamed if the root
    /// owns it, but not entered: its subtree is owned by that component. Rare (only on a root
    /// swap), so a walk.
    /// </summary>
    internal void RenameRootOwner(UIElement? root, string previousOwner, string owner)
        => WalkPublished(root, stopAtBoundaries: true,
            (_, value) => Diagnostics.ReactorSourcePublisher.WithOwner(value, previousOwner, owner));

    /// <summary>
    /// Visits every control under <paramref name="root"/> that carries a published value,
    /// through the visual tree and the Reactor subtrees outside it (see
    /// <see cref="RenameRootOwner"/>), writing back what <paramref name="rewrite"/> returns
    /// (<c>null</c> = unchanged). With <paramref name="stopAtBoundaries"/>, component wrappers
    /// and embedded hosts are not entered (their subtrees have other owners).
    /// </summary>
    private void WalkPublished(UIElement? root, bool stopAtBoundaries, Func<DependencyObject, string, string?> rewrite)
    {
        if (root is null) return;
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<DependencyObject>();
        void Push(DependencyObject? d)
        {
            if (d is not null && seen.Add(d)) pending.Push(d);
        }
        void PushCommandItems(WinPrim.FlyoutBase? flyoutBase)
        {
            switch (flyoutBase)
            {
                case WinUI.MenuFlyout menu:
                    foreach (var item in menu.Items) Push(item);
                    break;
                case WinUI.CommandBarFlyout commandFlyout:
                    foreach (var command in commandFlyout.PrimaryCommands) Push(command as DependencyObject);
                    foreach (var command in commandFlyout.SecondaryCommands) Push(command as DependencyObject);
                    break;
            }
        }

        Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            bool componentWrapper = false;
            if (node.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) is string value)
            {
                if (rewrite(node, value) is { } rewritten && !string.Equals(rewritten, value, StringComparison.Ordinal))
                    node.SetValue(Diagnostics.ReactorDiagnostics.SourceProperty, Diagnostics.ReactorSourcePublisher.Retained(rewritten));
                componentWrapper = Diagnostics.ReactorSourcePublisher.IsComponentWrapper(value);
            }

            // Overlay edges first: a decorator hangs its flyout on its target's control, which
            // can be a component wrapper (Flyout(Component<T>(), body)) or an embedded host
            // whose own content is not this root's, while the flyout body is the decorator
            // owner's.
            if (node is FrameworkElement fe)
            {
                var onControl = GetFlyoutOnControl(fe);
                if (onControl is WinUI.Flyout flyout)
                {
                    Push(flyout.Content);
                    Push(flyout.OverlayInputPassThroughElement);
                }
                // .WithContextFlyout / .AttachedFlyout / .ToolTip modifiers hang Reactor content
                // off the control too, outside every child walk until opened.
                if (fe.ContextFlyout is WinUI.Flyout contextFlyout) Push(contextFlyout.Content);
                var attached = WinPrim.FlyoutBase.GetAttachedFlyout(fe);
                if (attached is WinUI.Flyout attachedFlyout) Push(attachedFlyout.Content);
                Push(WinUI.ToolTipService.GetToolTip(fe) as UIElement);
                Push(V1Protocol.OverlayLifecycle.PeekLiveContentDialog(fe));
                // Menu and command items carry their own published values, and a closed menu's
                // items (a flyout's, a submenu's, a menu-bar menu's) are in no visual or logical
                // child walk.
                PushCommandItems(onControl);
                PushCommandItems(fe.ContextFlyout);
                PushCommandItems(attached);
                switch (fe)
                {
                    case WinUI.MenuFlyoutSubItem subItem:
                        foreach (var item in subItem.Items) Push(item);
                        break;
                    case WinUI.MenuBarItem menuBarItem:
                        foreach (var item in menuBarItem.Items) Push(item);
                        break;
                    // A bar whose template is not applied yet (collapsed, never shown) has its
                    // items in no visual child walk.
                    case WinUI.MenuBar menuBar:
                        foreach (var item in menuBar.Items) Push(item);
                        break;
                    case WinUI.CommandBar commandBar:
                        foreach (var command in commandBar.PrimaryCommands) Push(command as DependencyObject);
                        foreach (var command in commandBar.SecondaryCommands) Push(command as DependencyObject);
                        break;
                }
            }
            // A component wrapper's subtree is that component's. An embedded ReactorHostControl
            // is another host, even when it is this root's own content (XamlHost(() => island)):
            // its island names its own root. The node itself was renamed above if this root owns it.
            if (stopAtBoundaries && (componentWrapper || node is global::Microsoft.UI.Reactor.Hosting.ReactorHostControl)) continue;

            if (node is WinPrim.Popup popup) Push(popup.Child);
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));

            if (node is not UIElement ui) continue;
            // Logical children too: content a control template has not realized yet (a closed
            // flyout's Button content, a collapsed pane slot) is not in the visual tree.
            ForEachReactorChildControl(ui, child => Push(child));
            // Inline UI in a RichTextBlock document lives in its blocks, not its visual tree.
            if (ui is WinUI.RichTextBlock richText)
                VisitInlineUIChildren(richText, child => Push(child));
            // A target-wrapping decorator (Flyout(NavigationView(...), ...)) shares
            // its target's control; the control's children are still the target's.
            if (ui is FrameworkElement tagged && GetMountedElement(tagged) is Element taggedElement
                && global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.UnwrapDecorators(taggedElement) is { } hostElement
                && _v1Handlers.TryGet(hostElement.GetType(), out var entry) && entry is IV1ChildEnumerator children)
                children.VisitLiveChildren(ui, child => Push(child));
            if (ui is WinUI.ItemsControl items)
            {
                foreach (var item in items.Items)
                    Push(item as UIElement);
            }
            else if (ui is WinUI.TabView tabs)
            {
                foreach (var tab in tabs.TabItems)
                    Push(tab as UIElement);
            }
            else if (ui is WinUI.TreeView tree)
            {
                // Node content (legacy TreeViewNodeData.ContentElement) is mounted for every
                // node, collapsed ones included, and is in no visual or logical child walk.
                var nodes = new Stack<WinUI.TreeViewNode>(tree.RootNodes);
                while (nodes.Count > 0)
                {
                    var treeNode = nodes.Pop();
                    Push(treeNode.Content as UIElement);
                    foreach (var child in treeNode.Children) nodes.Push(child);
                }
            }
            if (_navigationHostNodes.TryGetValue(ui, out var navNode) && navNode.Cache is { } cache)
            {
                foreach (var page in cache.SnapshotControls())
                    Push(page);
            }
        }
    }

    /// <summary>
    /// A root that dispatched to a per-host <c>RegisterType</c> registration (a V1 handler for
    /// the same type takes precedence, as in Mount) whose control describes another element.
    /// </summary>
    private bool IsForwardingRegisteredRoot(UIElement control, Element kind)
        => !_v1Handlers.TryGet(kind.GetType(), out _)
            && _typeRegistry.ContainsKey(kind.GetType())
            && ForwardsAnotherElement(control, kind);

    /// <summary>
    /// A per-host <c>RegisterType</c> callback can return the control the reconciler mounted for
    /// a child element; the published value then describes whatever the JIT tag (and so
    /// <c>GetSource</c>) describes. <c>TypeRegistration.TagControl</c> keeps the child's tag when
    /// the child is tagged (a stamped child always is, outside the Native AOT skip, so a value
    /// with a call site counts) and otherwise tags the control with the registration. Decided
    /// from the value and the tag, so it holds where no tag exists.
    /// </summary>
    private static bool ForwardsAnotherElement(UIElement control, Element element)
    {
        if (control.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) is not string current
            || !Diagnostics.ReactorSourcePublisher.DescribesAnotherElement(current, element))
            return false;
        return Diagnostics.ReactorSourcePublisher.AtKey(current) is not null
            || (control is FrameworkElement fe && GetMountedElement(fe) is { } tag && tag.GetType() != element.GetType());
    }
}
