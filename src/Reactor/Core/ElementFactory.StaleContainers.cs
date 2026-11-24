using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

// ── Stale focused containers (microsoft/microsoft-ui-xaml#12659) ─────────
//
// ItemsRepeater doesn't update its element indices for a Move, and it keeps the element
// that holds keyboard focus pinned. Once that element sits in the pinned pool (scrolled out
// of view, or cleared by the Move itself) the repeater hands it back for its old index
// without asking the factory, so the row shows the wrong item and the moved item is
// realized a second time. A Remove or Reset doesn't evict it from the pinned pool either.
//
// The pin is focus: the repeater drops it when focus leaves the element, and the next
// measure then recycles the element and realizes the row through GetElement. So when the
// focused row's control is serving an index that belongs to another row, focus moves off it.
//
// The check looks only at the repeater child that holds focus. It runs after the layout pass
// that follows a Move, Remove or Reset, and, once the list has had one, whenever focus moves
// onto a row, because a row that was shifted while unfocused keeps its stale index and becomes
// pinned the moment it's focused. A focused control whose index is stale but which the layout
// still shows in the right place (another control holds its stale index) is left alone until
// it moves on screen: it only turns into the stale case once it scrolls into the pinned pool,
// so the check runs again after the next EffectiveViewportChanged on it.
public sealed partial class ElementFactory<T>
{
    private ItemsRepeater? _focusCheckRepeater;
    private EventHandler<object>? _focusCheck;
    private FrameworkElement? _focusWatchElement;
    private global::Windows.Foundation.TypedEventHandler<FrameworkElement, EffectiveViewportChangedEventArgs>? _focusWatch;
    private ItemsRepeater? _gotFocusRepeater;
    private RoutedEventHandler? _gotFocus;
    private bool _focusRunQueued;

    private void ScheduleFocusedContainerCheck(ItemsRepeater repeater)
    {
        StopFocusWatch();
        WatchFocusEntering(repeater);
        if (_focusCheck is not null) return;
        EventHandler<object> handler = (_, _) => RunFocusedContainerCheck();
        _focusCheckRepeater = repeater;
        _focusCheck = handler;
        repeater.LayoutUpdated += handler;
    }

    private void StopFocusedContainerCheck()
    {
        if (_focusCheckRepeater is { } repeater && _focusCheck is { } handler)
            repeater.LayoutUpdated -= handler;
        _focusCheckRepeater = null;
        _focusCheck = null;
    }

    // From the first Move, Remove or Reset on, check whenever focus moves onto a row. Focus
    // changes don't necessarily lay the list out, so the check runs from the dispatcher
    // instead of LayoutUpdated, once per burst of focus changes.
    private void WatchFocusEntering(ItemsRepeater repeater)
    {
        if (ReferenceEquals(_gotFocusRepeater, repeater)) return;
        if (_gotFocusRepeater is { } previous && _gotFocus is { } previousHandler)
            previous.GotFocus -= previousHandler;
        RoutedEventHandler handler = (_, _) =>
        {
            if (_focusRunQueued || _focusCheck is not null) return;
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (queue is null) return;
            _focusRunQueued = queue.TryEnqueue(() =>
            {
                _focusRunQueued = false;
                RunFocusedContainerCheck(repeater);
            });
        };
        _gotFocusRepeater = repeater;
        _gotFocus = handler;
        repeater.GotFocus += handler;
    }

    // Re-run the check after `element` next moves relative to the viewport.
    private void WatchFocusedContainer(ItemsRepeater repeater, FrameworkElement element)
    {
        StopFocusWatch();
        global::Windows.Foundation.TypedEventHandler<FrameworkElement, EffectiveViewportChangedEventArgs> handler = (_, _) =>
        {
            StopFocusWatch();
            ScheduleFocusedContainerCheck(repeater);
        };
        _focusWatchElement = element;
        _focusWatch = handler;
        element.EffectiveViewportChanged += handler;
    }

    private void StopFocusWatch()
    {
        if (_focusWatchElement is { } element && _focusWatch is { } handler)
            element.EffectiveViewportChanged -= handler;
        _focusWatchElement = null;
        _focusWatch = null;
    }

    private void RunFocusedContainerCheck()
    {
        var repeater = _focusCheckRepeater;
        StopFocusedContainerCheck();
        if (repeater is not null)
            RunFocusedContainerCheck(repeater);
    }

    private void RunFocusedContainerCheck(ItemsRepeater repeater)
    {
        var listState = _listState;
        if (listState is null || !ReferenceEquals(repeater.ItemTemplate, this)
            || FindFocusedChild(repeater) is not { } child
            || !_keyByControl.TryGetValue(child, out var key))
            return;

        int index = repeater.GetElementIndex(child);
        int expected = listState.ByKey.TryGetValue(key, out var row) ? row.Index : -1;
        if (index < 0 || index == expected)
            return;

        // Another control holds the stale index: the layout shows this one in its right place.
        foreach (var control in _keyByControl.Keys)
        {
            if (!ReferenceEquals(control, child) && repeater.GetElementIndex(control) == index)
            {
                if (child is FrameworkElement fe)
                    WatchFocusedContainer(repeater, fe);
                return;
            }
        }

        MoveFocusOffStaleContainer(repeater, child, key, index);
    }

    // Focus follows the item when its row is on screen; otherwise it goes to the on-screen row
    // nearest the stale index, which is where WinUI sends focus when it recycles a focused
    // element itself. Only rows fully inside the viewport qualify: focusing a clipped or
    // cached row scrolls it into view, and the list would jump. Focus on a row root that can't
    // take focus itself (a stack, a component wrapper) lands on its first focusable descendant
    // (RepeaterStale_FocusedCompositeRowMovedWhileScrolledAway_NoStaleRow); a row with nothing
    // focusable refuses it, and the next row is tried. When no row takes it (the list was
    // cleared, or no visible row is focusable), focus leaves the list for the first or last
    // focusable element outside it, so the stale control is still released.
    private void MoveFocusOffStaleContainer(ItemsRepeater repeater, UIElement stale, string key, int staleIndex)
    {
        var root = repeater.XamlRoot;
        var state = FocusManager.GetFocusedElement(root) is Control { FocusState: not FocusState.Unfocused } focused
            ? focused.FocusState
            : FocusState.Programmatic;
        var viewport = FindViewport(repeater);

        var candidates = new List<(UIElement Control, int Distance)>();
        foreach (var (control, controlKey) in _keyByControl)
        {
            if (ReferenceEquals(control, stale)) continue;
            if (repeater.GetElementIndex(control) is var controlIndex && controlIndex < 0) continue;
            if (!IsFullyInViewport(control, viewport)) continue;
            candidates.Add((control, controlKey == key ? -1 : Math.Abs(controlIndex - staleIndex)));
        }
        candidates.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        foreach (var (control, _) in candidates)
        {
            if (control.Focus(state))
                return;
        }

        if (root?.Content is not { } content) return;
        foreach (var outside in new[] { FocusManager.FindFirstFocusableElement(content), FocusManager.FindLastFocusableElement(content) })
        {
            if (outside is UIElement element
                && !IsDescendantOf(element, repeater)
                && (!IsDescendantOf(element, viewport) || IsFullyInViewport(element, viewport))
                && element.Focus(state))
                return;
        }
    }

    // The nearest scrolling ancestor, or the repeater itself when it doesn't scroll.
    private static FrameworkElement FindViewport(ItemsRepeater repeater)
    {
        for (var node = VisualTreeHelper.GetParent(repeater); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer or ScrollView)
                return (FrameworkElement)node;
        }
        return repeater;
    }

    // Fully inside: focusing a partly clipped row would scroll it into view too.
    private static bool IsFullyInViewport(UIElement element, FrameworkElement viewport)
    {
        if (element is not FrameworkElement fe || fe.Visibility != Visibility.Visible) return false;
        var bounds = fe.TransformToVisual(viewport).TransformBounds(new global::Windows.Foundation.Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
        return bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight
            && bounds.Left >= 0 && bounds.Right <= viewport.ActualWidth;
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor)) return true;
        }
        return false;
    }

    // The repeater's direct child that contains the focused element, if any.
    private static UIElement? FindFocusedChild(ItemsRepeater repeater)
    {
        if (repeater.XamlRoot is not { } root || FocusManager.GetFocusedElement(root) is not DependencyObject node)
            return null;
        for (var parent = VisualTreeHelper.GetParent(node); parent is not null; node = parent, parent = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(parent, repeater))
                return node as UIElement;
        }
        return null;
    }
}
