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
// still shows in its right place (it sits between its neighbouring keys' rows) is left alone,
// and the check runs again after the next EffectiveViewportChanged on it: it only turns into
// the stale case once it scrolls into the pinned pool. Focus isn't moved while another window
// of this process is active, because that would activate the list's window; the check waits
// for the list's window to be activated.
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
    // instead of LayoutUpdated, once per burst of focus changes. The repeater can outlive this
    // factory (a replaced template), so the handler holds the factory weakly and unsubscribes
    // itself once the factory is gone or no longer the repeater's template.
    private void WatchFocusEntering(ItemsRepeater repeater)
    {
        if (ReferenceEquals(_gotFocusRepeater, repeater)) return;
        if (_gotFocusRepeater is { } previous && _gotFocus is { } previousHandler)
            previous.GotFocus -= previousHandler;
        var self = new WeakReference<ElementFactory<T>>(this);
        RoutedEventHandler handler = null!;
        handler = (_, _) =>
        {
            if (!self.TryGetTarget(out var factory) || !ReferenceEquals(repeater.ItemTemplate, factory))
            {
                repeater.GotFocus -= handler;
                return;
            }
            if (factory._focusRunQueued || factory._focusCheck is not null) return;
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (queue is null) return;
            factory._focusRunQueued = queue.TryEnqueue(() =>
            {
                if (!self.TryGetTarget(out var target)) return;
                target._focusRunQueued = false;
                target.RunFocusedContainerCheck(repeater);
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

        // A stale index alone doesn't make the row wrong: the layout keeps shifted rows in their
        // right place, it just doesn't update their stored index on a Move.
        if (IsCorrectlyPlaced(repeater, listState, child, key, expected))
        {
            if (child is FrameworkElement fe)
                WatchFocusedContainer(repeater, fe);
            return;
        }

        if (FocusWouldStealActivation(repeater))
        {
            // Moving focus would activate the window. Defer to its next activation.
            WaitForActivation(repeater);
            return;
        }

        MoveFocusOffStaleContainer(repeater, child, key, index);
    }

    // Whether `child` is shown where its key belongs: the key is still in the list, no other
    // control has been realized for it, and it sits between the controls of its neighbouring
    // keys in reading order. A stale pinned control fails this once it's left the layout (its
    // neighbours aren't next to it), and a control the repeater handed back for its old index
    // fails it because the key's own row was realized again elsewhere.
    private bool IsCorrectlyPlaced(ItemsRepeater repeater, Internal.ReactorListState listState, UIElement child, string key, int expected)
    {
        if (expected < 0) return false;
        if (_controlByKey.TryGetValue(key, out var live) && !ReferenceEquals(live, child) && repeater.GetElementIndex(live) >= 0)
            return false;

        var source = listState.Source;
        var previous = expected > 0 ? RealizedControl(repeater, source[expected - 1].Key) : null;
        var next = expected + 1 < source.Count ? RealizedControl(repeater, source[expected + 1].Key) : null;
        if (previous is null && next is null) return false;

        // Lines run down the cross axis: a column-first grid and a horizontal stack fill along X
        // first, every other layout Reactor hosts (vertical stacks, row-first grids, lined flow)
        // along Y.
        bool columnFirst = repeater.Layout is UniformGridLayout { Orientation: Orientation.Vertical }
            or StackLayout { Orientation: Orientation.Horizontal };
        var at = BoundsIn(child, repeater);
        return (previous is null || IsBefore(BoundsIn(previous, repeater), at, columnFirst))
            && (next is null || IsBefore(at, BoundsIn(next, repeater), columnFirst));
    }

    private UIElement? RealizedControl(ItemsRepeater repeater, string key)
        => _controlByKey.TryGetValue(key, out var control) && repeater.GetElementIndex(control) >= 0 ? control : null;

    private static global::Windows.Foundation.Rect BoundsIn(UIElement element, ItemsRepeater repeater)
    {
        var size = element is FrameworkElement fe ? new global::Windows.Foundation.Size(fe.ActualWidth, fe.ActualHeight) : element.RenderSize;
        return element.TransformToVisual(repeater).TransformBounds(new global::Windows.Foundation.Rect(new global::Windows.Foundation.Point(), size));
    }

    // Fill order. Two items are on the same line when their bounds overlap across it, so items of
    // one row with different margins or alignment still order along the row; otherwise the line
    // that comes first wins.
    private static bool IsBefore(global::Windows.Foundation.Rect a, global::Windows.Foundation.Rect b, bool columnFirst)
    {
        if (columnFirst)
        {
            if (a.Right <= b.Left + 0.5) return true;
            if (b.Right <= a.Left + 0.5) return false;
            return a.Top < b.Top;
        }
        if (a.Bottom <= b.Top + 0.5) return true;
        if (b.Bottom <= a.Top + 0.5) return false;
        return a.Left < b.Left;
    }

    private ReactorWindow? _activationWindow;

    // Focus moved programmatically requests activation of its window; WinUI's own recycling path
    // avoids that with an internal no-activate focus call. The request can only succeed while
    // this process holds the foreground, so the list's window is at risk exactly when another
    // window of this process is the active one. When another process is in the foreground, the
    // OS foreground lock refuses the request, and a visible background window still gets its
    // stale row released.
    private static bool FocusWouldStealActivation(ItemsRepeater repeater)
    {
        var root = repeater.XamlRoot;
        if (ReactorApp.FindWindowForXamlRoot(root) is { IsActive: true }) return false;
        if (root?.ContentIslandEnvironment is not { } environment) return false;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(environment.AppWindowId);
        var foreground = ForegroundWindow.Get();
        return hwnd != 0 && foreground != 0 && foreground != hwnd && ForegroundWindow.BelongsToThisProcess(foreground);
    }

    // A Reactor window re-runs the check when it's activated. Other hosts rely on the GotFocus
    // that activation raises when it restores focus to the row (WatchFocusEntering). The window
    // can outlive the list, so its handler holds the factory and repeater weakly: a list that is
    // unmounted, or whose factory is replaced, while the window waits isn't kept alive by it.
    private void WaitForActivation(ItemsRepeater repeater)
    {
        if (_activationWindow is not null) return;
        if (ReactorApp.FindWindowForXamlRoot(repeater.XamlRoot) is not { } window) return;
        var self = new WeakReference<ElementFactory<T>>(this);
        var list = new WeakReference<ItemsRepeater>(repeater);
        EventHandler handler = null!;
        handler = (_, _) =>
        {
            window.Activated -= handler;
            if (!self.TryGetTarget(out var factory)) return;
            factory._activationWindow = null;
            if (list.TryGetTarget(out var target) && ReferenceEquals(target.ItemTemplate, factory))
                factory.ScheduleFocusedContainerCheck(target);
        };
        _activationWindow = window;
        window.Activated += handler;
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

        // Inside a scroller, compare with the visible part of its content: the scroller's own
        // bounds also cover its padding and scroll bars, where a row is clipped.
        if (viewport is ScrollViewer { Content: UIElement content } sv)
            return Contains(fe, content, sv.HorizontalOffset, sv.VerticalOffset, sv.ViewportWidth, sv.ViewportHeight, sv.ZoomFactor);
        if (viewport is ScrollView { Content: UIElement viewContent } view)
            return Contains(fe, viewContent, view.HorizontalOffset, view.VerticalOffset, view.ViewportWidth, view.ViewportHeight, view.ZoomFactor);

        var bounds = fe.TransformToVisual(viewport).TransformBounds(new global::Windows.Foundation.Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
        return bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight
            && bounds.Left >= 0 && bounds.Right <= viewport.ActualWidth;
    }

    // Offsets and viewport sizes are in zoomed units; content coordinates aren't.
    private static bool Contains(FrameworkElement fe, UIElement content, double offsetX, double offsetY, double width, double height, float zoom)
    {
        double scale = zoom > 0 ? zoom : 1;
        var bounds = fe.TransformToVisual(content).TransformBounds(new global::Windows.Foundation.Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
        double left = offsetX / scale, top = offsetY / scale;
        return bounds.Left >= left - 0.5 && bounds.Top >= top - 0.5
            && bounds.Right <= left + width / scale + 0.5 && bounds.Bottom <= top + height / scale + 0.5;
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

internal static partial class ForegroundWindow
{
    [global::System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindow();

    [global::System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    internal static nint Get() => GetForegroundWindow();

    internal static bool BelongsToThisProcess(nint hwnd)
        => GetWindowThreadProcessId(hwnd, out var processId) != 0 && processId == (uint)global::System.Environment.ProcessId;
}
