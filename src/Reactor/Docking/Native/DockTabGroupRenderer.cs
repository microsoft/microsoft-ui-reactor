using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.2 — translate a DockTabGroup node into a TabView subtree.
//
//  Decision §11 retained: tabs are rendered via WinUI TabView (existing
//  Reactor element wrapper). The renderer maps:
//    • DockTabGroup.Documents → TabViewItemData[] (Title, Content)
//    • DockTabGroup.SelectedIndex → TabViewElement.SelectedIndex
//    • DockableContent.CanClose → IsClosable per tab
//    • DockableContent.Title → Header (string; bidi via WinUI text engine)
//
//  Phase-2 affordances landing on top of TabView (per §2.2 checklist):
//    • Ctrl+W / Ctrl+F4 → close active tab when CanClose
//    • Ctrl+PageUp / Ctrl+PageDown → previous/next tab
//    • Per-tab pin button (icon + AT name + tooltip)
//
//  Those keyboard chords route through spec 027 input-and-gestures and
//  attach inside the host renderer (§2.16); the per-tab pin affordance
//  rides on the TabView's secondary-button API and lands once the drag
//  pipeline (§2.4) is in place.
// ════════════════════════════════════════════════════════════════════════

internal static class DockTabGroupRenderer
{
    /// <summary>
    /// Compose a <see cref="TabViewElement"/> for a <see cref="DockTabGroup"/>.
    /// </summary>
    /// <param name="group">The dock tab group node.</param>
    /// <param name="renderLeafContent">
    /// Renders the body element for one <see cref="DockableContent"/> child.
    /// Phase 2 passes the leaf's <c>Content</c> directly; the renderer
    /// wraps in a <c>Border</c> for AT consistency.
    /// </param>
    /// <param name="onSelectedIndexChanged">
    /// Invoked by the TabView when the user clicks a different tab (or
    /// keyboard-navigates). Caller threads the new index through model
    /// state so re-renders preserve selection.
    /// </param>
    /// <param name="onTabClosing">
    /// Invoked when a user clicks the close button on a tab whose
    /// <see cref="DockableContent.CanClose"/> is true. Caller is
    /// responsible for firing <c>OnDocumentClosing</c> / removing the
    /// pane from the model.
    /// </param>
    /// <param name="onTabDragStarting">
    /// Spec 045 §2.4. Invoked when the user starts dragging a tab; the
    /// pane reference + tab index are passed so the host can begin a
    /// <see cref="DockDragSession"/>. When null, tab tear-out is
    /// disabled (CanDragTabs = false on the underlying TabView).
    /// </param>
    /// <param name="onPinRequested">
    /// Spec 045 §2.2. Invoked when the user clicks the per-tab pin
    /// button. Only ToolWindow tabs whose <c>CanAutoHide</c> is true
    /// get the affordance; the typed <c>ToolWindow</c> is passed so
    /// the caller can route through <c>DockHostModel.PinToSide</c> or
    /// <c>Hide</c>.
    /// </param>
    /// <param name="onTabDragCompleted">
    /// Spec 045 §2.4. Invoked when a tab drag completes. The
    /// <c>wasOutside</c> flag distinguishes a drop on another TabView
    /// (false) from a drop outside any TabView (true — the tear-out
    /// signal). Caller is responsible for opening a floating window
    /// and removing the pane from the source layout on tear-out.
    /// </param>
    public static Element Render(
        DockTabGroup group,
        Func<DockableContent, Element?> renderLeafContent,
        Action<int>? onSelectedIndexChanged,
        Action<DockableContent>? onTabClosing,
        Action<DockableContent, int>? onTabDragStarting = null,
        Action<DockableContent, int, bool>? onTabDragCompleted = null,
        Action<ToolWindow>? onPinRequested = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(renderLeafContent);

        var documents = group.Documents;
        if (documents.Count == 0)
        {
            // Empty group: render a placeholder (Border) so the parent flex
            // child still has a measurable cell. ShowWhenEmpty=false on the
            // group means the caller is responsible for elision; the
            // renderer always produces *something* so the tree never has
            // a null child.
            return new BorderElement(null);
        }

        // §2.8 — apply default tab styling based on content type when the
        // user hasn't overridden it. All-ToolWindow groups switch to
        // bottom-position + compact tabs (matches Office / VS tool pane
        // convention); all-Document or mixed groups stay at the
        // top-position + full-width default. "User hasn't overridden"
        // means the group's TabPosition + CompactTabs match the record's
        // own defaults (Top + non-compact) — apps that pass explicit
        // values (even if same as defaults) on a ToolWindow group still
        // get flipped, which is the desired behavior because the typed
        // contract is that ToolWindow-only groups SHOULD look like a
        // tool pane.
        bool allToolWindow = true;
        for (int i = 0; i < documents.Count; i++)
        {
            if (documents[i] is not ToolWindow) { allToolWindow = false; break; }
        }
        // TabPosition.Bottom isn't wired through TabViewElement yet — when a
        // TabStripPlacement property lands, the all-ToolWindow flip applies
        // here. For now only the CompactTabs flag is auto-resolved.
        var atDefaults = group.TabPosition == TabPosition.Top && !group.CompactTabs;
        var resolvedCompact = allToolWindow && atDefaults ? true : group.CompactTabs;

        var tabs = new TabViewItemData[documents.Count];
        for (int i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var body = renderLeafContent(doc) ?? new BorderElement(null);
            var data = new TabViewItemData(doc.Title ?? string.Empty, body)
            {
                IsClosable = doc.CanClose,
            };
            // §2.2 — render a pin affordance on ToolWindow tabs whose
            // CanAutoHide is true. The handler receives the typed
            // ToolWindow so the caller can route through
            // DockHostModel.PinToSide / Hide. Mixed groups (a Document
            // sibling alongside a ToolWindow) still render the pin
            // only on the ToolWindow tab.
            if (doc is ToolWindow tw && tw.CanAutoHide && onPinRequested is not null)
            {
                var pinName = DockingStrings.Get(DockingStringKeys.MenuPinToSide);
                var pinAtId = DockHostNativeComponent.AutomationIdForPane(doc) is { } at
                    ? $"pin:{at[("pane:".Length)..]}"
                    : null;
                data = data with
                {
                    IsPinnable = true,
                    PinAutomationName = pinName,
                    PinAutomationId = pinAtId,
                    OnPinRequested = () => onPinRequested(tw),
                };
            }
            tabs[i] = data;
        }

        var selected = group.SelectedIndex >= 0 && group.SelectedIndex < documents.Count
            ? group.SelectedIndex
            : 0;

        var element = new TabViewElement(tabs)
        {
            SelectedIndex = selected,
            OnSelectedIndexChanged = onSelectedIndexChanged,
            OnTabCloseRequested = onTabClosing is null ? null : (int idx) =>
            {
                if (idx >= 0 && idx < documents.Count)
                    onTabClosing(documents[idx]);
            },
            // §2.2 / §2.8: configurable tab width follows the resolved
            // CompactTabs flag (Equal is the WinUI default for editor
            // groups; Compact matches the upstream DocumentGroup style
            // for tool groups). All-ToolWindow groups auto-resolve to
            // Compact unless the user set explicit non-default values.
            TabWidthMode = resolvedCompact
                ? TabViewWidthMode.Compact
                : TabViewWidthMode.Equal,
            CanReorderTabs = true,
            // Spec 045 §2.4: enable tab tear-out when a drag callback is
            // supplied so the host can begin a DockDragSession. Off by
            // default to preserve P1 behavior when the host hasn't opted in.
            CanDragTabs = onTabDragStarting is not null || onTabDragCompleted is not null,
            AllowDropTabs = false,
            OnTabDragStarting = onTabDragStarting is null ? null : (int idx) =>
            {
                if (idx >= 0 && idx < documents.Count)
                    onTabDragStarting(documents[idx], idx);
            },
            OnTabDragCompleted = onTabDragCompleted is null ? null : (int idx, bool wasOutside) =>
            {
                // The tab may already have left this group's list by the
                // time TabDragCompleted fires (tear-out path: WinUI yanks
                // the TabViewItem out before notifying). Fall back to the
                // active drag session's source pane so cleanup still runs.
                DockableContent? pane =
                    idx >= 0 && idx < documents.Count
                        ? documents[idx]
                        : DockDragSession.Current?.Source;
                if (pane is not null)
                    onTabDragCompleted(pane, idx, wasOutside);
            },
            Setters = BuildSetters(group),
        };
        return element;
    }

    /// <summary>
    /// Apply <see cref="DockTabGroup.TabChrome"/>-driven chrome to the
    /// rendered <see cref="TabView"/>. Each preset is implemented as a
    /// scoped resource-dictionary override on the <c>TabView</c> instance
    /// — the underlying control template / accessibility tree are
    /// unchanged across presets (spec 045 §4.6).
    ///
    /// <para>
    /// Pool-safety: a <c>TabView</c> may be reused across renders with a
    /// different chrome. Every preset first calls <see cref="ClearManagedKeys"/>
    /// to remove any keys a previous chrome may have written, then re-adds
    /// only what the new chrome wants. <c>Win11</c> is the no-override
    /// baseline — its setters strip prior overrides without adding anything.
    /// </para>
    ///
    /// <para>
    /// <see cref="DockTabGroup.TabPosition"/> is intentionally NOT wired
    /// here. WinUI 3's <c>TabView</c> has no <c>TabStripPlacement</c> property
    /// (microsoft-ui-xaml#7395); the upstream WinUI.Dock workaround composes
    /// <c>ScaleY = -1</c> on the outer control with counter-scales on every
    /// tab header (text + close button) AND on every tab body, requiring
    /// access to template parts of <c>TabViewItem</c> that aren't reachable
    /// without subclassing. For P2/P4 the bottom-tab variant intentionally
    /// renders as a top-tab variant: legible, no upside-down text. A true
    /// translation lands when a dedicated tab-item subclass (or a manual
    /// strip+content composition) replaces the shared
    /// <see cref="TabViewElement"/>.
    /// </para>
    /// </summary>
    internal static Action<TabView>[] BuildSetters(DockTabGroup group) => group.TabChrome switch
    {
        TabChrome.Win11    => [ClearManagedKeys],
        TabChrome.Flat     => [ClearManagedKeys, ApplyFlatChrome],
        TabChrome.TitleBar => [ClearManagedKeys, ApplyTitleBarChrome],
        _                  => [ClearManagedKeys],
    };

    /// <summary>
    /// Keys this renderer writes into <c>TabView.Resources</c>. Pool-safety
    /// requires every chrome preset to remove all of these before writing
    /// its own values; otherwise a Flat → Win11 transition would leak the
    /// sharp-corner overrides into a control reused under a new chrome.
    /// </summary>
    internal static readonly string[] ManagedResourceKeys =
    {
        "TabViewItemHeaderCornerRadius",
        "TabViewItemHeaderPadding",
        "TabViewItemHeaderBackgroundSelected",
        "TabViewItemHeaderBackgroundPointerOver",
        "TabViewBackground",
    };

    private static void ClearManagedKeys(TabView tabView)
    {
        var res = tabView.Resources;
        for (int i = 0; i < ManagedResourceKeys.Length; i++)
        {
            var key = ManagedResourceKeys[i];
            if (res.ContainsKey(key)) res.Remove(key);
        }
    }

    private static void ApplyFlatChrome(TabView tabView)
    {
        var res = tabView.Resources;
        // Zero corner radius on every header state — header rests, pointer-
        // over, selected, and pressed all share the resource. WinUI's
        // TabView template reads it through a single ThemeResource key.
        res["TabViewItemHeaderCornerRadius"] = new global::Microsoft.UI.Xaml.CornerRadius(0);
        // Tighter padding squares up the IDE / VS Code feel; matches the
        // density of a true document-tab strip rather than the looser
        // Win11 default. Numbers chosen against the WinUI 2.0.1 default
        // (8,0,8,0 horizontal) — drop top/bottom and tighten horizontals.
        res["TabViewItemHeaderPadding"] = new global::Microsoft.UI.Xaml.Thickness(6, 0, 6, 0);
    }

    private static void ApplyTitleBarChrome(TabView tabView)
    {
        // Spec 045 §4.6 — tab-strip background uses TitleBarBackgroundFillBrush
        // (when defined). Resolved against the running app's resources at
        // render time; falls back silently when the brush isn't registered
        // (e.g. test host with no theme dictionary loaded).
        var res = tabView.Resources;
        if (global::Microsoft.UI.Xaml.Application.Current?.Resources is { } appResources
            && appResources.TryGetValue("TitleBarBackgroundFillBrush", out var brush)
            && brush is global::Microsoft.UI.Xaml.Media.Brush)
        {
            res["TabViewBackground"] = brush;
        }
    }
}
