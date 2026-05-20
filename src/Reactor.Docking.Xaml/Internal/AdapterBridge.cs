using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WinUIDock = WinUI.Dock;

namespace Microsoft.UI.Reactor.Docking.Internal;

/// <summary>
/// Bridges upstream <see cref="WinUIDock.IDockAdapter"/> callbacks to the
/// Reactor-side <see cref="IDockAdapter"/>. The upstream surface fires when:
///   <list type="bullet">
///     <item>A <c>Document</c> is reconstituted from a <c>LoadLayout</c>
///     pass — the wrapper asks the Reactor adapter to supply the content
///     subtree for that pane.</item>
///     <item>A new <c>DocumentGroup</c> is created at the tail of a tear-out
///     drag — the Reactor adapter sees a <see cref="DockTabGroupContext"/>.</item>
///     <item>A floating window needs a custom title-bar — the Reactor adapter
///     returns a Reactor <see cref="Element"/> we host inside a
///     <c>ContentControl</c>.</item>
///   </list>
/// </summary>
internal sealed class AdapterBridge : WinUIDock.IDockAdapter
{
    private readonly IDockAdapter _adapter;
    private readonly HostState _host;
    private readonly DockingXamlInterop.PaneKeyResolver _resolveKey;

    public AdapterBridge(IDockAdapter adapter, HostState host, DockingXamlInterop.PaneKeyResolver resolveKey)
    {
        _adapter = adapter;
        _host = host;
        _resolveKey = resolveKey;
    }

    public void OnCreated(WinUIDock.Document document)
    {
        // The pane was reconstituted from layout JSON. Recover its Reactor-side
        // identity (Key + a synthesized DockableContent) and let the app supply
        // content. The wrapper hosts the returned element under the pane's
        // ContentControl.
        var key = _resolveKey(document);
        var dc = new DockableContent(
            Title: document.Title,
            Content: null,
            Key: key,
            CanClose: document.CanClose,
            CanPin: document.CanPin);

        var contentEl = _adapter.OnContentCreated(dc);

        var host = DockingXamlInterop.EnsureContentHost(document);
        var oldRealized = (UIElement?)host.Content;
        var realized = _host.Reconciler.Reconcile(
            oldElement: null,
            newElement: contentEl,
            existingControl: oldRealized,
            requestRerender: _host.RequestRerender);

        if (!ReferenceEquals(realized, oldRealized))
        {
            host.Content = realized;
        }

        if (_host.PanesByKey.TryGetValue(key ?? string.Empty, out var paneState))
        {
            paneState.ContentElement = contentEl;
            paneState.ContentControl_Realized = realized;
        }
    }

    public void OnCreated(WinUIDock.DocumentGroup group, WinUIDock.Document? draggedDocument)
    {
        DockableContent? dragged = null;
        if (draggedDocument is not null)
        {
            var key = _resolveKey(draggedDocument);
            dragged = new DockableContent(
                Title: draggedDocument.Title,
                Key: key,
                CanClose: draggedDocument.CanClose,
                CanPin: draggedDocument.CanPin);
        }

        _adapter.OnGroupCreated(new DockTabGroupContext(dragged), dragged);
    }

    public object? GetFloatingWindowTitleBar(WinUIDock.Document? draggedDocument)
    {
        DockableContent? dragged = null;
        if (draggedDocument is not null)
        {
            var key = _resolveKey(draggedDocument);
            dragged = new DockableContent(
                Title: draggedDocument.Title,
                Key: key,
                CanClose: draggedDocument.CanClose,
                CanPin: draggedDocument.CanPin);
        }

        var titleBarEl = _adapter.GetFloatingWindowTitleBar(dragged);
        if (titleBarEl is null) return null;

        var realized = _host.Reconciler.Reconcile(
            oldElement: null,
            newElement: titleBarEl,
            existingControl: null,
            requestRerender: _host.RequestRerender);

        return realized;
    }
}
