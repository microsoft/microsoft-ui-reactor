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
    public static Element Render(
        DockTabGroup group,
        Func<DockableContent, Element?> renderLeafContent,
        Action<int>? onSelectedIndexChanged,
        Action<DockableContent>? onTabClosing)
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

        var tabs = new TabViewItemData[documents.Count];
        for (int i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var body = renderLeafContent(doc) ?? new BorderElement(null);
            tabs[i] = new TabViewItemData(doc.Title ?? string.Empty, body)
            {
                IsClosable = doc.CanClose,
            };
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
            // §2.2: configurable tab width follows the model's CompactTabs
            // flag — Equal is the WinUI default for editor groups; Compact
            // matches the upstream DocumentGroup style for tool groups.
            TabWidthMode = group.CompactTabs
                ? TabViewWidthMode.Compact
                : TabViewWidthMode.Equal,
            CanReorderTabs = true,
            CanDragTabs = false, // tab-tearout lands with §2.4 drag pipeline
            AllowDropTabs = false,
            Setters = BuildSetters(group),
        };
        return element;
    }

    /// <summary>
    /// Hook for applying <see cref="DockTabGroup.TabPosition"/>-driven
    /// chrome to the rendered <see cref="TabView"/>. WinUI's
    /// <see cref="TabView"/> has no native bottom-tab mode; the upstream
    /// WinUI.Dock workaround composes <c>ScaleY = -1</c> on the outer
    /// control with counter-scales on every tab header (text + close
    /// button) AND on every tab body — requiring access to template
    /// parts of <c>TabViewItem</c> that aren't reachable without
    /// subclassing.
    ///
    /// For P2 the bottom-tab variant intentionally renders as a
    /// top-tab variant: legible, no upside-down text, but visually it
    /// doesn't match upstream's bottom-strip placement. A true
    /// translation lands when a dedicated tab-item subclass (or a
    /// manual strip+content composition) replaces the shared
    /// <see cref="TabViewElement"/>.
    /// </summary>
    private static Action<TabView>[] BuildSetters(DockTabGroup group)
    {
        _ = group;
        return Array.Empty<Action<TabView>>();
    }
}
