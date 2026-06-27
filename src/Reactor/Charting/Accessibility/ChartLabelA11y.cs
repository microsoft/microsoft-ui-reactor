using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Charting.Accessibility;

/// <summary>
/// Hides a caller-supplied chart label / axis-tick <see cref="Core.Element"/> and its
/// <em>entire realized subtree</em> from assistive technology and keyboard focus
/// traversal, so the chart's own <see cref="IChartAccessibilityData"/> descriptor stays
/// the single accessible representation of slice / tick data.
///
/// <para>
/// Setting <c>AutomationProperties.AccessibilityView = Raw</c> on only the outer wrapper
/// the chart applies does <b>not</b> reliably remove inner peers — a caller's
/// <see cref="Core.Element"/> may be a structured composite (a Reactor
/// <see cref="Core.Component"/> with focusable children, a <see cref="TextBlock"/>, an
/// icon, …) whose interior peers still surface to UIA / focus depending on how the
/// platform composes peer trees under a <c>Raw</c>-marked parent. That produces the
/// double-announcement and stray-focus-stop problems described in issue #162. Walking
/// the subtree and force-<c>Raw</c>-ing every descendant FE (plus clearing
/// <see cref="Control"/>.<c>IsTabStop</c>) removes them from both the UIA tree and the tab order.
/// </para>
/// </summary>
internal static class ChartLabelA11y
{
    // L1 (issue #162 review): a private attached sentinel marking elements whose
    // deferred (Loaded) hide is still pending. Replaces the old IsHitTestVisible
    // identity check — which mis-fired when a recycled element happened to carry
    // IsHitTestVisible=false from an unrelated prior use. AOT/trim-safe (a plain
    // attached DependencyProperty, no reflection).
    private static readonly DependencyProperty PendingDeferredHideProperty =
        DependencyProperty.RegisterAttached(
            "PendingDeferredHide",
            typeof(bool),
            typeof(ChartLabelA11y),
            new PropertyMetadata(false));

    /// <summary>
    /// <c>OnMount</c> hook for a non-interactive custom label / tick element. Blocks
    /// pointer hit-testing and recursively hides the element's subtree from UIA and the
    /// tab order — once immediately (covers panel children already attached at mount
    /// time) and again on <see cref="FrameworkElement.Loaded"/> (covers templated-control
    /// inner peers that only realize after the element enters the live visual tree).
    /// </summary>
    internal static void HideSubtreeOnMount(FrameworkElement fe)
    {
        fe.IsHitTestVisible = false;

        // Immediate pass: panel children added to a Children collection are visual
        // children right away, even before the element is loaded.
        HideSubtree(fe);

        // Deferred pass: a templated Control (Button, etc.) only expands its template —
        // and therefore its inner peers — once it is loaded and measured. Re-walk then.
        if (fe.IsLoaded)
            return;

        // Mark the pending hide so the one-shot Loaded handler only acts when this
        // hook is still the owner of the element's hidden state (see ApplyDeferredHide).
        MarkPendingDeferredHide(fe);

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            fe.Loaded -= OnLoaded;
            ApplyDeferredHide(fe);
        }

        fe.Loaded += OnLoaded;
    }

    /// <summary>
    /// <c>OnUpdate</c> hook for a non-interactive custom label / tick element (issue #162
    /// review M1). An in-place data/content update can realize <em>new</em> focusable
    /// descendants; <see cref="HideSubtreeOnMount"/> only fires once (at mount), so those
    /// would leak back into UIA / the tab order. The element is already loaded on update,
    /// so a single synchronous re-walk suffices — no deferred <see cref="FrameworkElement.Loaded"/>
    /// arm is needed.
    /// </summary>
    internal static void HideSubtreeOnUpdate(FrameworkElement fe)
    {
        fe.IsHitTestVisible = false;
        HideSubtree(fe);
    }

    /// <summary>
    /// Marks <paramref name="fe"/> as having a deferred hide still pending its
    /// <see cref="FrameworkElement.Loaded"/> event. Internal (not private) so the
    /// deferred-hide self-test can drive the sentinel directly.
    /// </summary>
    internal static void MarkPendingDeferredHide(FrameworkElement fe) =>
        fe.SetValue(PendingDeferredHideProperty, true);

    /// <summary>
    /// Clears the deferred-hide sentinel. Wired as an <c>OnUnmount</c> hook on the
    /// non-interactive chart sites so an element unmounted <em>before it ever loaded</em>
    /// (its one-shot <see cref="FrameworkElement.Loaded"/> handler still attached) cannot
    /// later hide a recycled interactive / unrelated renter: with the sentinel cleared,
    /// the surviving handler's <see cref="ApplyDeferredHide"/> becomes a no-op. Lives on
    /// the Charting side (not <c>ElementPool</c>) to avoid a Core→Charting layering dependency.
    /// </summary>
    internal static void ClearPendingHide(FrameworkElement fe) =>
        fe.ClearValue(PendingDeferredHideProperty);

    /// <summary>
    /// Deferred (<see cref="FrameworkElement.Loaded"/>) arm of <see cref="HideSubtreeOnMount"/>,
    /// with a stale-handler guard (issue #162 review L1). If the element was unmounted and
    /// returned to the pool <em>before it ever loaded</em>, this one-shot handler can survive
    /// into a later reuse. Only apply the deferred hide when the pending-hide sentinel this
    /// hook set is still present — a recycled element that was re-rendered interactive (or for
    /// an unrelated purpose) has had the sentinel cleared on unmount, so the stale handler
    /// no-ops. The sentinel is consumed (cleared) once applied.
    /// </summary>
    internal static void ApplyDeferredHide(FrameworkElement fe)
    {
        if (!(bool)fe.GetValue(PendingDeferredHideProperty))
            return;

        fe.ClearValue(PendingDeferredHideProperty);
        HideSubtree(fe);
    }

    /// <summary>
    /// Recursively forces <c>AccessibilityView.Raw</c> on every descendant
    /// <see cref="FrameworkElement"/> (removing each inner peer from the UIA Content and
    /// Control views) and clears <see cref="Control"/>.<c>IsTabStop</c> on every descendant
    /// <see cref="Control"/> (removing inner focusable children from the keyboard tab
    /// order). Uses only the public <see cref="VisualTreeHelper"/> /
    /// <see cref="AutomationProperties"/> API — no reflection — so it stays AOT / trim safe.
    /// </summary>
    internal static void HideSubtree(DependencyObject root)
    {
        if (root is FrameworkElement fe)
        {
            AutomationProperties.SetAccessibilityView(fe, AccessibilityView.Raw);
            if (fe is Control control)
                control.IsTabStop = false;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            HideSubtree(VisualTreeHelper.GetChild(root, i));
    }
}

