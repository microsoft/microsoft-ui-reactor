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
// The check runs once, after the layout pass that follows a Move, Remove or Reset, and looks
// only at the repeater child that holds focus. A focused control whose index is stale but
// which the layout still shows in the right place (another control holds its stale index) is
// left alone until it moves on screen: it only turns into the stale case once it scrolls into
// the pinned pool, so the check runs again after the next EffectiveViewportChanged on it.
public sealed partial class ElementFactory<T>
{
    private ItemsRepeater? _focusCheckRepeater;
    private EventHandler<object>? _focusCheck;
    private FrameworkElement? _focusWatchElement;
    private global::Windows.Foundation.TypedEventHandler<FrameworkElement, EffectiveViewportChangedEventArgs>? _focusWatch;

    private void ScheduleFocusedContainerCheck(ItemsRepeater repeater)
    {
        StopFocusWatch();
        if (_focusCheck is not null) return;
        _focusCheckRepeater = repeater;
        _focusCheck = (_, _) => RunFocusedContainerCheck();
        repeater.LayoutUpdated += _focusCheck;
    }

    private void StopFocusedContainerCheck()
    {
        if (_focusCheckRepeater is { } repeater && _focusCheck is { } handler)
            repeater.LayoutUpdated -= handler;
        _focusCheckRepeater = null;
        _focusCheck = null;
    }

    // Re-run the check after `element` next moves relative to the viewport.
    private void WatchFocusedContainer(ItemsRepeater repeater, FrameworkElement element)
    {
        StopFocusWatch();
        _focusWatchElement = element;
        _focusWatch = (_, _) =>
        {
            StopFocusWatch();
            ScheduleFocusedContainerCheck(repeater);
        };
        element.EffectiveViewportChanged += _focusWatch;
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

        var listState = _listState;
        if (repeater is null || listState is null || !ReferenceEquals(repeater.ItemTemplate, this)
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
    // element itself. Rows not fully inside the viewport (partly clipped, or in the repeater's cache) come last:
    // focusing one scrolls it into view, and the list would jump. Focus on a row root that
    // can't take focus itself (a stack, a component wrapper) lands on its first focusable
    // descendant (RepeaterStale_FocusedCompositeRowMovedWhileScrolledAway_NoStaleRow); a row
    // with nothing focusable refuses it, and the next row is tried.
    private void MoveFocusOffStaleContainer(ItemsRepeater repeater, UIElement stale, string key, int staleIndex)
    {
        var state = FocusManager.GetFocusedElement(repeater.XamlRoot) is Control { FocusState: not FocusState.Unfocused } focused
            ? focused.FocusState
            : FocusState.Programmatic;
        var viewport = FindViewport(repeater);

        var candidates = new List<(UIElement Control, bool OffScreen, int Distance)>();
        foreach (var (control, controlKey) in _keyByControl)
        {
            if (ReferenceEquals(control, stale)) continue;
            int controlIndex = repeater.GetElementIndex(control);
            if (controlIndex < 0) continue;
            candidates.Add((control, !IsInViewport(control, viewport),
                controlKey == key ? -1 : Math.Abs(controlIndex - staleIndex)));
        }
        candidates.Sort(static (a, b) => a.OffScreen != b.OffScreen
            ? a.OffScreen.CompareTo(b.OffScreen)
            : a.Distance.CompareTo(b.Distance));

        foreach (var (control, _, _) in candidates)
        {
            if (control.Focus(state))
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
    private static bool IsInViewport(UIElement element, FrameworkElement viewport)
    {
        if (element is not FrameworkElement fe || fe.Visibility != Visibility.Visible) return false;
        var bounds = fe.TransformToVisual(viewport).TransformBounds(new global::Windows.Foundation.Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
        return bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight
            && bounds.Left >= 0 && bounds.Right <= viewport.ActualWidth;
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
