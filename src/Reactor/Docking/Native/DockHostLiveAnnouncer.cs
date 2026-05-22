using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.10 — UIA live-region for layout state transitions.
//
//  Apps + assistive tech expect a "polite" announcement when a docking
//  operation transitions state visibly: tabs torn out, panes pinned,
//  panes closed, dock targets confirmed. The Phase-2 native renderer
//  fires those announcements through this small bridge.
//
//  Why not a TextBlock with LiveSetting=Polite in the visual tree:
//  adding a region as a sibling of the dock subtree (Grid wrap) shifts
//  the host Border's direct child from the dock FlexPanel/TabView/Border
//  to a wrapper Grid, which breaks M19's `VisualTreeHelper.GetParent(b)`
//  parent-walk and the M20 SceneRerenderPreservesDockHostControls
//  identity check. WinUI's `RaiseNotificationEvent` on an existing
//  element's AutomationPeer is the supported alternative — same UIA
//  behavior, zero visual-tree changes.
//
//  The bridge is keyed by `DockManager` element instance, paralleling
//  `DockChordBridge` and `DockHostModelBridge`. The interop layer's
//  mount handler registers the host Border; the renderer's event paths
//  invoke `Announce(manager, text)` to fire a polite notification.
// ════════════════════════════════════════════════════════════════════════

internal static class DockHostLiveAnnouncer
{
    private static readonly ConditionalWeakTable<DockManager, FrameworkElement> _table = new();

    public static void Register(DockManager element, FrameworkElement host)
    {
        _table.Remove(element);
        _table.Add(element, host);
    }

    public static void Clear(DockManager element) => _table.Remove(element);

    /// <summary>
    /// Fires a polite UIA notification on the host element registered for
    /// <paramref name="element"/>. Safe to call when no host is registered
    /// (silently no-ops); safe to call off-thread (queues onto the host
    /// element's dispatcher when available).
    /// </summary>
    public static void Announce(DockManager? element, string message)
    {
        if (element is null || string.IsNullOrEmpty(message)) return;
        if (!_table.TryGetValue(element, out var host) || host is null) return;
        var dq = host.DispatcherQueue;
        if (dq is null || dq.HasThreadAccess)
        {
            RaiseNotification(host, message);
            return;
        }
        dq.TryEnqueue(() => RaiseNotification(host, message));
    }

    /// <summary>
    /// Returns the host element registered for <paramref name="element"/> or
    /// <c>null</c> when unregistered. Surface used by §2.22 focus-invariant
    /// recovery paths that need to land focus on the host when its active
    /// pane has been removed.
    /// </summary>
    public static FrameworkElement? GetHost(DockManager? element)
    {
        if (element is null) return null;
        return _table.TryGetValue(element, out var host) ? host : null;
    }

    /// <summary>
    /// Spec 045 §2.22 — programmatically focuses the host element when no
    /// pane is available to receive focus (e.g. last pane in the host
    /// just closed). Queues onto the host's dispatcher when called off
    /// the UI thread.
    /// </summary>
    public static void FocusHostFallback(DockManager? element)
    {
        var host = GetHost(element);
        if (host is null) return;
        var dq = host.DispatcherQueue;
        if (dq is null || dq.HasThreadAccess)
        {
            TryFocus(host);
            return;
        }
        dq.TryEnqueue(() => TryFocus(host));
    }

    private static void TryFocus(FrameworkElement host)
    {
        if (host is Microsoft.UI.Xaml.Controls.Control control)
        {
            control.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            return;
        }
        // Border / Panel — focus the nearest focusable child via the
        // FocusManager's automatic walk so a tab group inside still
        // gets focus when its container hands off. Fire-and-forget:
        // the focus shift completes asynchronously but its outcome
        // doesn't gate any caller — we don't need the IAsyncOp.
        _ = Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocusAsync(
            Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next,
            new Microsoft.UI.Xaml.Input.FindNextElementOptions
            {
                SearchRoot = host,
            });
    }

    private static void RaiseNotification(FrameworkElement host, string message)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(host)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(host);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            message,
            "DockingLayoutTransition");
    }
}
