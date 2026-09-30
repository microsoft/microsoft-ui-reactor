using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core.Diagnostics;
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
//  The bridge is keyed by the host (DockHostIdentity), paralleling
//  `DockChordBridge` and `DockHostModelBridge`, so every `DockManager`
//  instance the host rendered resolves to the host Border until unmount
//  clears it. The interop layer's mount handler registers the host Border;
//  the renderer's event paths invoke `Announce(manager, text)` to fire a
//  polite notification.
// ════════════════════════════════════════════════════════════════════════

internal static class DockHostLiveAnnouncer
{
    /// <summary>
    /// Stable operation label for the swallowed-error trace emitted when a
    /// focus hand-off fails. Kept as a constant so the emit sites and the
    /// tests that assert on them cannot drift apart.
    /// </summary>
    internal const string TryFocusOperation = "DockHostLiveAnnouncer.TryFocus";

    /// <summary>
    /// Category label on the swallowed-error payload. Matches what
    /// <c>LogCategory.Docking.ToString()</c> would produce, so the wire format
    /// is identical to every other <c>SwallowedError</c> emitter.
    /// </summary>
    private const string DockingCategory = "Docking";

    private static readonly ConditionalWeakTable<object, FrameworkElement> _table = new();

    public static void Register(DockManager element, FrameworkElement host) =>
        _table.AddOrUpdate(DockHostIdentity.KeyFor(element), host);

    public static void Clear(DockManager element) => _table.Remove(DockHostIdentity.KeyFor(element));

    public static void Clear(DockHostIdentity host) => _table.Remove(host);

    /// <summary>
    /// Fires a polite UIA notification on the host element registered for
    /// <paramref name="element"/>. Safe to call when no host is registered
    /// (silently no-ops); safe to call off-thread (queues onto the host
    /// element's dispatcher when available).
    /// </summary>
    public static void Announce(DockManager? element, string message)
    {
        if (element is null || string.IsNullOrEmpty(message)) return;
        if (!_table.TryGetValue(DockHostIdentity.KeyFor(element), out var host) || host is null) return;
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
        return _table.TryGetValue(DockHostIdentity.KeyFor(element), out var host) ? host : null;
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
        try
        {
            if (host is Microsoft.UI.Xaml.Controls.Control control)
            {
                control.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                return;
            }

            // Border / Panel — focus the first focusable element *inside* the
            // host so a tab group within still gets focus when its container
            // hands off.
            //
            // Deliberately NOT
            //   TryMoveFocusAsync(FocusNavigationDirection.Next,
            //                     new FindNextElementOptions { SearchRoot = host })
            // WinUI rejects that pairing during parameter validation —
            // "Focus navigation directions Next and Previous are not supported
            // when using FindNextElementOptions" — so the call threw an
            // ArgumentException on every hand-off and focus never moved.
            // FindNextElementOptions only accepts the four directional values.
            // Dropping the options instead would make `Next` walk the *global*
            // tab order from wherever focus happens to be, which can land
            // outside the dock host entirely; FindFirstFocusableElement keeps
            // the subtree scoping this fallback needs and takes no direction.
            var target = Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(host);
            if (target is null) return;
            ObserveFocusMove(
                Microsoft.UI.Xaml.Input.FocusManager.TryFocusAsync(
                    target, Microsoft.UI.Xaml.FocusState.Programmatic));
        }
        catch (global::System.ArgumentException ex)
        {
            // Parameter validation — the class the pre-fix
            // TryMoveFocusAsync(Next, FindNextElementOptions) pairing threw on
            // *every* hand-off (R4).
            ReportFocusFailure(ex);
        }
        catch (global::System.Runtime.InteropServices.COMException ex)
        {
            // Interop failure below the WinRT projection.
            ReportFocusFailure(ex);
        }
        catch (global::System.InvalidOperationException ex)
        {
            // Element / visual-tree state — the same class PackagedSettingsStore
            // narrows to on its WinRT surface.
            ReportFocusFailure(ex);
        }
        // Narrow, per spec 044 §6.7.2 and this site's **Narrow** verdict in
        // docs/specs/044/swallowed-error-audit.md. Anything outside those three
        // propagates on purpose: a broad catch here is precisely what hid R4,
        // and a surprise exception out of the focus stack is a bug someone needs
        // to see rather than absorb. The hand-off stays best-effort for the
        // three audited classes — an accessibility nicety must not take the app
        // down mid-pane-close, but it must not be silent either.
    }

    /// <summary>
    /// Observes the outcome of a fire-and-forget focus move. No caller gates
    /// on the result, but an unobserved fault would be invisible — route it to
    /// the same swallowed-error trace the synchronous path uses.
    /// </summary>
    private static void ObserveFocusMove(
        global::Windows.Foundation.IAsyncOperation<Microsoft.UI.Xaml.Input.FocusMovementResult> operation)
    {
        operation.AsTask().ContinueWith(
            static t => ReportFocusFailure(t.Exception?.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Reports a swallowed focus failure on the <c>Microsoft-UI-Reactor</c>
    /// provider. Mirrors <c>DiagnosticLog.SwallowedError</c>'s shape (spec 044
    /// §6.1): gated on the <c>Errors</c> keyword at <c>Warning</c>, and the
    /// exception <b>type</b> only on the payload per §6.2.1 — never the message,
    /// which can carry paths or user data.
    ///
    /// <para>
    /// Emitted straight to <see cref="ReactorEventSource"/> rather than through
    /// <c>DiagnosticLog</c> on purpose: the event source is already the reviewed
    /// core internal that <c>Reactor.Advanced</c> may touch (spec 062 §7), and
    /// docking already emits on it from <c>DockLayoutSerializer</c>. Going
    /// through the helper would have pulled two more core internals into that
    /// allowlist for no change in the emitted payload. The one thing given up is
    /// <c>DiagnosticLog</c>'s DEBUG-only <c>Debug.WriteLine</c> mirror.
    /// </para>
    /// </summary>
    private static void ReportFocusFailure(Exception? ex)
    {
        // Cost-of-disabled: skip the type-name materialization entirely when no
        // consumer has enabled the Errors keyword.
        if (!ReactorEventSource.Log.IsEnabled(
                global::System.Diagnostics.Tracing.EventLevel.Warning,
                ReactorEventSource.Keywords.Errors))
        {
            return;
        }

        ReactorEventSource.Log.SwallowedError(
            DockingCategory, TryFocusOperation, ex?.GetType().Name ?? string.Empty);
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
