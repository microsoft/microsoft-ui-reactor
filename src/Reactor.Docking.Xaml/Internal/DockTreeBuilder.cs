using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDock = WinUI.Dock;

namespace Microsoft.UI.Reactor.Docking.Internal;

/// <summary>
/// Builds upstream WinUI.Dock control instances from a Reactor
/// <see cref="DockNode"/> tree on first mount, and a "best-effort rebuild
/// while preserving keyed panes" pass on update.
///
/// <para>
/// Phase 1 reconciliation strategy (spec 045 §4.4):
/// containers (<see cref="DockSplit"/>, <see cref="DockTabGroup"/>) are
/// identified by tree position; pane leaves (<see cref="DockableContent"/>)
/// are identified by <see cref="DockableContent.Key"/> — the
/// <see cref="HostState.PanesByKey"/> map carries each pane's vendored
/// <c>WinUI.Dock.Document</c> instance across rebuilds so its content host
/// (and the realized Reactor element subtree mounted in it) survives a
/// container re-creation.
/// </para>
/// </summary>
internal static class DockTreeBuilder
{
    /// <summary>
    /// Mounts the given <see cref="DockNode"/> tree into the upstream
    /// <c>WinUI.Dock.DockManager</c>'s <c>Panel</c> slot. Panes with keys
    /// already present in <paramref name="host"/>'s pane map are reused
    /// (with content reconciled against the new <see cref="DockableContent.Content"/>);
    /// new panes are constructed and recorded.
    /// </summary>
    public static void ApplyLayout(
        WinUIDock.DockManager manager,
        HostState host,
        DockNode? layout)
    {
        var freshPaneKeys = new HashSet<object>();
        WinUIDock.LayoutPanel? panel = null;

        if (layout is not null)
        {
            // The upstream root is always a LayoutPanel. If the user supplies a
            // single DockTabGroup or DockableContent at the root, wrap it.
            panel = layout switch
            {
                DockSplit split => BuildSplit(split, host, freshPaneKeys),
                DockTabGroup group => WrapInPanel(BuildTabGroup(group, host, freshPaneKeys)),
                DockableContent leaf => WrapInPanel(WrapLeafInGroup(leaf, host, freshPaneKeys)),
                _ => null,
            };
        }

        manager.Panel = panel;

        // GC removed panes — their vendored Document instances are no longer
        // referenced from the visual tree; drop the bookkeeping entry so we
        // don't leak ContentControl hosts. The Reactor reconciler is told to
        // unmount their content subtrees by passing newElement = null.
        var staleKeys = host.PanesByKey.Keys.Where(k => !freshPaneKeys.Contains(k)).ToList();
        foreach (var key in staleKeys)
        {
            var paneState = host.PanesByKey[key];
            host.PanesByKey.Remove(key);
            // Reconcile content to null so any useEffect cleanup runs.
            host.Reconciler.Reconcile(
                oldElement: paneState.ContentElement,
                newElement: null,
                existingControl: paneState.ContentControl_Realized,
                requestRerender: host.RequestRerender);
        }

        ApplySides(manager, host, layout: null /* sides arg passed separately */, freshPaneKeys);
    }

    /// <summary>
    /// Synchronizes the manager's four side-pin collections (LeftSide,
    /// TopSide, RightSide, BottomSide) with the new ToolWindow-style content
    /// lists. Side panes also live in the pane map keyed by
    /// <see cref="DockableContent.Key"/>.
    /// </summary>
    public static void ApplySides(
        WinUIDock.DockManager manager,
        HostState host,
        IReadOnlyList<DockableContent>? leftSide,
        IReadOnlyList<DockableContent>? topSide,
        IReadOnlyList<DockableContent>? rightSide,
        IReadOnlyList<DockableContent>? bottomSide,
        HashSet<object> trackedKeys)
    {
        SyncSide(manager.LeftSide,   leftSide,   host, trackedKeys);
        SyncSide(manager.TopSide,    topSide,    host, trackedKeys);
        SyncSide(manager.RightSide,  rightSide,  host, trackedKeys);
        SyncSide(manager.BottomSide, bottomSide, host, trackedKeys);
    }

    // Internal overload retained for ApplyLayout's GC of stale keys when the
    // caller hasn't yet seen the side lists.
    private static void ApplySides(
        WinUIDock.DockManager manager,
        HostState host,
        DockNode? layout,
        HashSet<object> trackedKeys) { /* no-op — side application is driven by the public overload */ }

    private static void SyncSide(
        global::System.Collections.ObjectModel.ObservableCollection<WinUIDock.Document> sideCollection,
        IReadOnlyList<DockableContent>? newPanes,
        HostState host,
        HashSet<object> trackedKeys)
    {
        // Simple rebuild — clear and re-add. Side collections are short (a
        // handful of pinned panes); a smarter diff isn't worth the complexity
        // for Phase 1. Reuse vendored Document instances by key so content
        // hosts survive.
        sideCollection.Clear();
        if (newPanes is null) return;

        foreach (var pane in newPanes)
        {
            var doc = MountOrReuseDocument(pane, host, trackedKeys);
            sideCollection.Add(doc);
        }
    }

    // ── Containers ────────────────────────────────────────────────────────

    private static WinUIDock.LayoutPanel BuildSplit(
        DockSplit split,
        HostState host,
        HashSet<object> trackedKeys)
    {
        var panel = new WinUIDock.LayoutPanel
        {
            Orientation = split.Orientation,
        };

        ApplyDimensions(panel, split.Width, split.Height, split.MinWidth, split.MinHeight, split.MaxWidth, split.MaxHeight);

        foreach (var child in split.Children)
        {
            switch (child)
            {
                case DockSplit nested:
                    panel.Children.Add(BuildSplit(nested, host, trackedKeys));
                    break;
                case DockTabGroup grp:
                    panel.Children.Add(BuildTabGroup(grp, host, trackedKeys));
                    break;
                case DockableContent leaf:
                    panel.Children.Add(WrapLeafInGroup(leaf, host, trackedKeys));
                    break;
            }
        }

        return panel;
    }

    private static WinUIDock.LayoutPanel WrapInPanel(WinUIDock.DockContainer inner)
    {
        var panel = new WinUIDock.LayoutPanel
        {
            Orientation = Orientation.Vertical,
        };
        panel.Children.Add(inner);
        return panel;
    }

    private static WinUIDock.DocumentGroup WrapLeafInGroup(
        DockableContent leaf,
        HostState host,
        HashSet<object> trackedKeys)
    {
        var group = new WinUIDock.DocumentGroup();
        ApplyDimensions(group, leaf.Width, leaf.Height, minW: null, minH: null, maxW: null, maxH: null);
        var doc = MountOrReuseDocument(leaf, host, trackedKeys);
        group.Children.Add(doc);
        return group;
    }

    private static WinUIDock.DocumentGroup BuildTabGroup(
        DockTabGroup group,
        HostState host,
        HashSet<object> trackedKeys)
    {
        var dg = new WinUIDock.DocumentGroup
        {
            TabPosition = (WinUIDock.TabPosition)(int)group.TabPosition,
            CompactTabs = group.CompactTabs,
            ShowWhenEmpty = group.ShowWhenEmpty,
        };

        ApplyDimensions(dg, group.Width, group.Height, minW: null, minH: null, maxW: null, maxH: null);

        foreach (var pane in group.Documents)
        {
            var doc = MountOrReuseDocument(pane, host, trackedKeys);
            dg.Children.Add(doc);
        }

        if (group.SelectedIndex >= 0 && group.SelectedIndex < dg.Children.Count)
        {
            dg.SelectedIndex = group.SelectedIndex;
        }

        return dg;
    }

    // ── Pane leaf ─────────────────────────────────────────────────────────

    /// <summary>
    /// Either reuses the vendored <c>Document</c> already tracked under the
    /// pane's key (updating its DPs in place + reconciling its content
    /// subtree) or constructs a fresh one.
    /// </summary>
    private static WinUIDock.Document MountOrReuseDocument(
        DockableContent pane,
        HostState host,
        HashSet<object> trackedKeys)
    {
        var key = pane.Key ?? FallbackKey(pane);
        trackedKeys.Add(key);

        if (host.PanesByKey.TryGetValue(key, out var existing))
        {
            UpdateDocumentProps(existing.Document, pane);
            // Detach from current parent — caller will re-add.
            existing.Document.Detach(detachEmptyContainer: false);
            ReconcileContent(existing, pane.Content, host);
            return existing.Document;
        }

        var doc = new WinUIDock.Document
        {
            Title = pane.Title,
            CanClose = pane.CanClose,
            CanPin = pane.CanPin,
        };
        ApplyDimensions(doc, pane.Width, pane.Height, minW: null, minH: null, maxW: null, maxH: null);

        // ContentControl host: spec 045 §4.4 "Pane content host. Wrap each
        // Document.Content in a ReactorContentControl so the reconciler has
        // a slot host inside the XAML object graph."
        var contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        doc.Content = contentHost;

        var state = new PaneState
        {
            Document = doc,
            ContentHost = contentHost,
        };
        host.PanesByKey[key] = state;

        ReconcileContent(state, pane.Content, host);

        return doc;
    }

    private static void UpdateDocumentProps(WinUIDock.Document doc, DockableContent pane)
    {
        if (doc.Title != pane.Title) doc.Title = pane.Title;
        if (doc.CanClose != pane.CanClose) doc.CanClose = pane.CanClose;
        if (doc.CanPin != pane.CanPin) doc.CanPin = pane.CanPin;
        ApplyDimensions(doc, pane.Width, pane.Height, minW: null, minH: null, maxW: null, maxH: null);
    }

    private static void ReconcileContent(PaneState state, Element? newContent, HostState host)
    {
        var oldRealized = state.ContentControl_Realized ?? (UIElement?)state.ContentHost.Content;
        var realized = host.Reconciler.Reconcile(
            oldElement: state.ContentElement,
            newElement: newContent,
            existingControl: oldRealized,
            requestRerender: host.RequestRerender);

        if (!ReferenceEquals(realized, oldRealized))
        {
            state.ContentHost.Content = realized;
        }

        state.ContentElement = newContent;
        state.ContentControl_Realized = realized;
    }

    private static object FallbackKey(DockableContent pane)
    {
        // Phase 1 allows callers to omit Key — fall back to a synthetic
        // identity based on the title. Spec 045 §1.4 explicitly says "no
        // fallback to Title keying" for the reconciler-grade key, but we
        // still need *something* hashable for the bookkeeping map; the
        // pane will simply be treated as new on every render.
        return new SyntheticKey(pane.Title);
    }

    private sealed class SyntheticKey(string title) : global::System.IEquatable<SyntheticKey>
    {
        // Synthetic keys are deliberately reference-equal — they never match
        // across renders, so the pane is mounted fresh each time. The user
        // can supply a real Key to opt into preservation.
        public override bool Equals(object? obj) => false;
        public bool Equals(SyntheticKey? other) => false;
        public override int GetHashCode() => global::System.HashCode.Combine(title, global::System.Environment.CurrentManagedThreadId);
    }

    // ── Dimension helpers ─────────────────────────────────────────────────

    private static void ApplyDimensions(
        WinUIDock.DockModule module,
        double? width,
        double? height,
        double? minW,
        double? minH,
        double? maxW,
        double? maxH)
    {
        module.Width  = width  ?? double.NaN;
        module.Height = height ?? double.NaN;
        if (minW.HasValue) module.MinWidth  = minW.Value;
        if (minH.HasValue) module.MinHeight = minH.Value;
        if (maxW.HasValue) module.MaxWidth  = maxW.Value;
        if (maxH.HasValue) module.MaxHeight = maxH.Value;
    }
}
