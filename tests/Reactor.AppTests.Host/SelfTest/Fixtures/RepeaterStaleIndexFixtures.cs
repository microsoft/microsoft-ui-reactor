using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// microsoft/microsoft-ui-xaml#12659: ItemsRepeater doesn't update its element indices for a
/// <c>Move</c>, and it keeps a focused element in its pinned pool at its old index. These
/// fixtures read what the list actually paints (rows in on-screen order, parked recycled
/// containers skipped), never the repeater's own <c>TryGetElement</c>, because the repeater's
/// indices are exactly what is wrong. Each scenario ends with a re-render that bumps every
/// row's generation suffix, so a row whose control stopped being reconciled shows up as a
/// stale <c>g0</c>.
/// </summary>
internal static class RepeaterStaleIndexFixtures
{
    private const int RowCount = 200;

    private enum ListKind { LazyVStack, ItemsView }

    private sealed record Scenario(
        string Name,
        Func<List<int>, List<int>> Change,
        ListKind Kind = ListKind.LazyVStack,
        bool CompositeRows = false,
        bool Focus = false,
        bool ChangeWhileScrolledAway = false,
        bool ScrollAwayAfterChange = false,
        bool StayScrolledAway = false,
        bool FocusAfterChange = false,
        bool ExpectEmpty = false,
        bool ExpectFocusKept = false,
        bool ChangeWhileInactive = false);

    private static Element Row(int key, int gen, bool composite)
    {
        var button = Button($"r{key} g{gen}").Height(32);
        return composite ? HStack(TextBlock("·"), button) : button;
    }

    private static Element BuildList(Scenario s, IReadOnlyList<int> keys, int gen) => s.Kind == ListKind.ItemsView
        ? ItemsView(keys, k => k.ToString(), (k, _) => ItemContainer(Row(k, gen, s.CompositeRows))).Height(300)
        : LazyVStack(keys, k => k.ToString(), (k, _) => Row(k, gen, s.CompositeRows)).Height(300);

    private static Button? FirstButton(DependencyObject node)
    {
        if (node is Button b) return b;
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
            if (FirstButton(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    private static async Task RunAsync(Harness h, Scenario s)
    {
        string Name(string check) => $"RepeaterStale_{s.Name}_{check}";

        var initial = Enumerable.Range(0, RowCount).ToList();
        var changed = s.Change(initial.ToList());

        var host = h.CreateHost();
        host.Mount(ctx =>
        {
            var (phase, setPhase) = ctx.UseState(0);
            return VStack(
                Button("Change", () => setPhase(1)),
                Button("Bump", () => setPhase(2)),
                BuildList(s, phase == 0 ? initial : changed, phase == 2 ? 1 : 0));
        });
        await Harness.Render();

        var repeater = h.FindControl<WinXC.ItemsRepeater>(_ => true);
        FrameworkElement? viewport;
        Func<double> offset, scrollable, viewportHeight;
        Action<double> scrollTo;
        if (s.Kind == ListKind.ItemsView)
        {
            var sv = h.FindControl<WinXC.ScrollView>(v => v.Content is WinXC.ItemsRepeater);
            viewport = sv;
            offset = () => sv!.VerticalOffset;
            scrollable = () => sv!.ScrollableHeight;
            viewportHeight = () => sv!.ViewportHeight;
            scrollTo = y => sv!.ScrollTo(0, y, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
        }
        else
        {
            var sv = h.FindControl<WinXC.ScrollViewer>(v => v.Content is WinXC.ItemsRepeater);
            viewport = sv;
            offset = () => sv!.VerticalOffset;
            scrollable = () => sv!.ScrollableHeight;
            viewportHeight = () => sv!.ViewportHeight;
            scrollTo = y => sv!.ChangeView(null, y, null, disableAnimation: true);
        }
        if (repeater is null || viewport is null)
        {
            h.Check(Name("Found"), false);
            return;
        }

        // Button labels of the rows painted in the viewport, top to bottom.
        List<string> Painted()
        {
            var rows = new List<(double Y, string Label)>();
            int n = VisualTreeHelper.GetChildrenCount(repeater);
            for (int i = 0; i < n; i++)
            {
                if (VisualTreeHelper.GetChild(repeater, i) is not FrameworkElement { Visibility: Visibility.Visible } row) continue;
                if (row is ItemContainer { Child: { Visibility: not Visibility.Visible } }) continue;
                if (FirstButton(row) is not { Content: string label }) continue;
                var y = row.TransformToVisual(viewport).TransformPoint(default).Y;
                if (y >= 0 && y < viewportHeight())
                    rows.Add((y, label));
            }
            return rows.OrderBy(r => r.Y).Select(r => r.Label).ToList();
        }

        async Task ScrollAsync(bool toEnd, string check)
        {
            scrollTo(toEnd ? scrollable() : 0);
            h.Check(Name(check), await Harness.WaitFor(() => toEnd ? offset() >= scrollable() - 1 : offset() <= 0));
            await Harness.Render();
        }

        Button? target = null;
        async Task FocusTargetAsync()
        {
            target = h.FindControl<Button>(b => b.Content as string == "r2 g0");
            h.Check(Name("FocusTaken"), target?.Focus(FocusState.Programmatic) == true);
            await Harness.Render();
        }
        if (s.Focus && !s.FocusAfterChange)
            await FocusTargetAsync();

        if (s.ChangeWhileScrolledAway)
        {
            await ScrollAsync(toEnd: true, "ScrolledAway");
            if (target is not null)
            {
                h.Check(Name("FocusHeldWhileScrolledAway"),
                    ReferenceEquals(FocusManager.GetFocusedElement(target.XamlRoot), target));
                h.Check(Name("FocusedRowOffScreen"), !Painted().Contains("r2 g0"));
            }
        }

        Window? otherWindow = null;
        nint otherHwnd = 0;
        if (s.ChangeWhileInactive)
        {
            otherWindow = new Window { Content = new TextBlock { Text = "other window" } };
            otherWindow.Activate();
            otherHwnd = WinRT.Interop.WindowNative.GetWindowHandle(otherWindow);
            if (!await Harness.WaitFor(() => ForegroundWindow.Get() == otherHwnd))
            {
                // Only possible while this process holds the foreground (an interactive run).
                h.Skip(Name("FocusDeferredWhileInactive"), "this process can't take the foreground here, so no window of it is active");
                otherWindow.Close();
                otherWindow = null;
            }
        }

        h.ClickButton("Change");
        await Harness.Render();

        if (otherWindow is not null && target is not null)
        {
            // Moving focus would activate the list's window, so it has to wait.
            bool otherInForeground = ForegroundWindow.Get() == otherHwnd;
            h.Check(Name("FocusDeferredWhileInactive"),
                ReferenceEquals(FocusManager.GetFocusedElement(target.XamlRoot), target) && otherInForeground,
                $"foreground is the other window: {otherInForeground}");
            otherWindow.Close();
            h.Check(Name("FocusMovedAfterActivation"),
                await Harness.WaitFor(() => !ReferenceEquals(FocusManager.GetFocusedElement(target.XamlRoot), target), maxPasses: 40, perPassMs: 25));
        }

        // Moving focus off the stale row must not scroll the list: focusing a clipped or cached
        // row scrolls it into view. A removal is excluded because the scroller's own anchoring
        // moves the offset then.
        if (s.ChangeWhileScrolledAway && changed.Count == initial.Count)
            h.Check(Name("NoScrollJump"), offset() >= scrollable() - 1, $"offset {offset():0} of {scrollable():0}");

        if (s.ExpectFocusKept && target is not null)
            h.Check(Name("FocusKeptOnCorrectRow"), ReferenceEquals(FocusManager.GetFocusedElement(target.XamlRoot), target),
                $"focused '{(FocusManager.GetFocusedElement(target.XamlRoot) as Button)?.Content}'");

        // Focusing a row the reorder shifted while nothing was focused pins it with its stale index.
        if (s.Focus && s.FocusAfterChange)
            await FocusTargetAsync();

        if (target is not null && s.ChangeWhileScrolledAway)
        {
            var focused = FocusManager.GetFocusedElement(target.XamlRoot) as DependencyObject;
            h.Check(Name("FocusLeftStaleRow"),
                !ReferenceEquals(focused, target) && IsDescendantOf(focused, repeater) != s.ExpectEmpty,
                $"focused {focused?.GetType().Name ?? "null"} '{(focused as Button)?.Content}'");
            if (s.StayScrolledAway)
                h.Check(Name("FocusFollowedItem"), focused is Button { Content: "r2 g0" } && !ReferenceEquals(focused, target));
        }

        if (s.ChangeWhileScrolledAway && !s.StayScrolledAway)
            await ScrollAsync(toEnd: false, "ScrolledBack");

        if (s.ScrollAwayAfterChange)
        {
            await ScrollAsync(toEnd: true, "ScrolledAwayAfterChange");
            await ScrollAsync(toEnd: false, "ScrolledBackAfterChange");
        }

        h.ClickButton("Bump");
        await Harness.Render();

        var painted = Painted();
        if (s.ExpectEmpty)
        {
            h.Check(Name("NoRowsPainted"), painted.Count == 0, $"painted [{string.Join(", ", painted)}]");
            return;
        }
        var expected = (s.StayScrolledAway ? changed.TakeLast(painted.Count) : changed.Take(painted.Count)).Select(k => $"r{k} g1").ToList();
        h.Check(Name("RowsPainted"), painted.Count >= 5);
        h.Check(Name("RowsMatchKeys"), painted.SequenceEqual(expected),
            $"painted [{string.Join(", ", painted)}], expected [{string.Join(", ", expected)}]");
    }

    private static List<int> Swap2And3(List<int> k) { (k[2], k[3]) = (k[3], k[2]); return k; }
    private static List<int> Move2ToTop(List<int> k) { k.RemoveAt(2); k.Insert(0, 2); return k; }
    private static List<int> Remove2(List<int> k) { k.RemoveAt(2); return k; }
    private static List<int> Move2ToEnd(List<int> k) { k.RemoveAt(2); k.Add(2); return k; }
    private static List<int> Clear(List<int> k) { k.Clear(); return k; }
    private static List<int> Move4To1(List<int> k) { var t = k[4]; k.RemoveAt(4); k.Insert(1, t); return k; }

    // Moving a row past the focused one by several positions leaves the focused row in its right
    // place with a stale stored index (no other control takes that index): focus must stay.
    internal class FocusedRowShiftedByMultiPositionMove_FocusStays(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedShiftedByMultiMove", Move4To1, Focus: true, ExpectFocusKept: true));
    }

    // The stale row's window is inactive: focus must not move (that would activate the window)
    // until the window is activated again.
    internal class FocusedRowMovedWhileWindowInactive_WaitsForActivation(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedMovedInactive", Move2ToTop, Focus: true, ChangeWhileScrolledAway: true, ChangeWhileInactive: true));
    }

    // A reorder while nothing in the list is focused leaves the shifted rows with stale
    // indices. Focusing one afterwards pins it, and it used to come back as a stale row after a
    // scroll away and back.
    internal class FocusedAfterOnScreenSwapThenScrolled_NoStaleRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedAfterSwapThenScrolled", Swap2And3, Focus: true, FocusAfterChange: true, ScrollAwayAfterChange: true));
    }

    // With no row left to take focus, focus has to leave the list: it used to stay on the
    // deleted row's control, still pinned and still invocable from the keyboard.
    internal class FocusedRowListClearedWhileScrolledAway_FocusLeavesList(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedListCleared", Clear, Focus: true, ChangeWhileScrolledAway: true, ExpectEmpty: true));
    }

    // An on-screen reorder used to leave two rows claiming the moved row's index, and the next
    // re-render then diffed one row's content into the other row's control: one row was
    // painted twice and the other froze at its old content.
    internal class OnScreenMove_ReRenderUpdatesEveryRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H, new("OnScreen", Swap2And3));
    }

    internal class ItemsView_OnScreenMove_ReRenderUpdatesEveryRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("ItemsViewOnScreen", Swap2And3, Kind: ListKind.ItemsView));
    }

    // A focused row that has scrolled out of view sits in the repeater's pinned pool. When it
    // moved, the repeater used to hand it back for its old index on the way back, so that row
    // showed the moved item and the moved item was painted a second time.
    internal class FocusedRowMovedWhileScrolledAway_NoStaleRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedMoved", Move2ToTop, Focus: true, ChangeWhileScrolledAway: true));
    }

    // Same, with the focused row deleted: it used to come back showing the deleted item.
    internal class FocusedRowRemovedWhileScrolledAway_NoStaleRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedRemoved", Remove2, Focus: true, ChangeWhileScrolledAway: true));
    }

    // The focused row moves into the rows on screen while scrolled away, so its key is realized
    // again before the stale control is recycled: focus follows the item to its new row, and
    // recycling the stale control must leave the new row's tracking alone (it used to freeze).
    internal class FocusedRowMovedIntoViewWhileScrolledAway_FocusFollows(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedMovedIntoView", Move2ToEnd, Focus: true, ChangeWhileScrolledAway: true, StayScrolledAway: true));
    }

    // Rows whose root can't take focus (a stack around the button): focus has to move to a
    // focusable descendant of another row for the stale row to be released.
    internal class FocusedCompositeRowMovedWhileScrolledAway_NoStaleRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedCompositeMoved", Move2ToTop, CompositeRows: true, Focus: true, ChangeWhileScrolledAway: true));
    }

    // A focused row reordered on screen keeps focus and is painted in the right place, but its
    // index is stale. It must not turn into a stale row once it scrolls into the pinned pool.
    internal class FocusedRowSwappedOnScreenThenScrolled_NoStaleRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RepeaterStaleIndexFixtures.RunAsync(H,
            new("FocusedSwappedThenScrolled", Swap2And3, Focus: true, ScrollAwayAfterChange: true));
    }

    // A column-first grid fills each column before moving across. A focused item the reorder
    // shifts from the bottom of one column to the top of the next is still in its right place,
    // so focus must stay (comparing positions row-first would call it misplaced).
    internal class ColumnFirstGrid_FocusedItemShiftedAcrossColumns_FocusStays(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var initial = Enumerable.Range(0, RowCount).ToList();
            List<int>? changed = null;
            var layout = new UniformGridLayout { Orientation = Orientation.Vertical, MinItemWidth = 100, MinItemHeight = 32 };

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var repeaterElement = ItemsRepeater(phase == 0 || changed is null ? initial : changed, k => k.ToString(),
                    (k, _) => Button($"r{k}").Width(100).Height(32)) with { Layout = layout };
                return VStack(
                    Button("Change", () => setPhase(1)),
                    ScrollViewer(repeaterElement).Height(300).Set(sv =>
                    {
                        sv.HorizontalScrollMode = ScrollMode.Enabled;
                        sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                        sv.VerticalScrollMode = ScrollMode.Disabled;
                        sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    }));
            });
            await Harness.Render();

            var repeater = H.FindControl<WinXC.ItemsRepeater>(_ => true);
            var first = H.FindControl<Button>(b => b.Content as string == "r0");
            if (repeater is null || first is null)
            {
                H.Check("RepeaterStale_ColumnGrid_Found", false);
                return;
            }

            // The last item of the first column: the one before the first item of the second.
            double firstColumn = first.TransformToVisual(repeater).TransformPoint(default).X;
            int targetKey = initial.FirstOrDefault(k => H.FindControl<Button>(b => b.Content as string == $"r{k}") is { } b
                && b.TransformToVisual(repeater).TransformPoint(default).X > firstColumn + 1, -1);
            H.Check("RepeaterStale_ColumnGrid_HasSecondColumn", targetKey > 1, $"first key of column two: {targetKey}");
            if (targetKey <= 1) return;
            targetKey--;

            var target = H.FindControl<Button>(b => b.Content as string == $"r{targetKey}")!;
            H.Check("RepeaterStale_ColumnGrid_FocusTaken", target.Focus(FocusState.Programmatic));
            await Harness.Render();

            // Moving a later item to the front (a single Move, so the target is only shifted) puts
            // the target at the top of column two.
            changed = initial.ToList();
            int later = targetKey + 5;
            changed.Remove(later);
            changed.Insert(0, later);
            H.ClickButton("Change");
            await Harness.Render();

            double targetColumn = target.TransformToVisual(repeater).TransformPoint(default).X;
            H.Check("RepeaterStale_ColumnGrid_TargetShiftedToSecondColumn", targetColumn > firstColumn + 1,
                $"target column x {targetColumn:0}, first column x {firstColumn:0}");
            H.Check("RepeaterStale_ColumnGrid_FocusKeptOnCorrectItem",
                ReferenceEquals(FocusManager.GetFocusedElement(target.XamlRoot), target),
                $"focused '{(FocusManager.GetFocusedElement(target.XamlRoot) as Button)?.Content}'");
        }
    }
}
