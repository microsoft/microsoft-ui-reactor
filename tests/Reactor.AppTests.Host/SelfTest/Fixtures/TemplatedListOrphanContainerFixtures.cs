using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Under fast insert / remove / swap churn a <c>ListView&lt;T&gt;</c> can park a container
/// offscreen in its items panel, unlinked from any item, without ever raising the recycle
/// <c>ContainerContentChanging</c> for it. Its row's components then stayed mounted: their
/// effects never cleaned up, and a <see cref="ReactorDiagnostics.TrySetState"/> on one was
/// accepted but never rendered, because no pass reaches a container the list no longer
/// shows. Every mounted row component must belong to a row the list still holds, and a row the
/// list still holds must stay live.
/// </summary>
internal static class TemplatedListOrphanContainerFixtures
{
    private sealed record RowProps(string Key);

    private sealed class RowInfo
    {
        public Microsoft.UI.Reactor.Input.ElementRef<FrameworkElement> Root = null!;
        public string Key = "";
        public int Instance;
    }

    private static readonly Dictionary<object, RowInfo> s_live = new(ReferenceEqualityComparer.Instance);
    private static int s_instances;
    private static Action<Func<List<RowProps>, List<RowProps>>>? s_update;
    private static Action<bool>? s_setVisible;

    private sealed class Row : Component<RowProps>
    {
        public override Element Render()
        {
            var instance = UseRef(0);
            if (instance.Current == 0) instance.Current = ++s_instances;
            var (clicks, setClicks) = UseState(0);
            var root = this.UseElementRef<FrameworkElement>();
            UseEffect(() =>
            {
                var info = new RowInfo { Root = root, Key = Props.Key, Instance = instance.Current };
                s_live[this] = info;
                return () => s_live.Remove(this);
            });
            return HStack(
                TextBlock($"orphan-row {Props.Key} #{instance.Current} local {clicks}").Ref(root),
                Button($"orphan+{Props.Key}", () => setClicks(clicks + 1)));
        }
    }

    private sealed class Churn : Component<int>
    {
        public override Element Render()
        {
            var (items, update) = UseReducer(Enumerable.Range(0, Props).Select(i => new RowProps($"c{i}")).ToList());
            var (visible, setVisible) = UseState(true);
            s_update = update;
            s_setVisible = setVisible;
            return Border(ListView<RowProps>(items, x => x.Key, (x, _) => Component<Row, RowProps>(x))).Height(300).Visible(visible);
        }
    }

    private static void Reset()
    {
        s_live.Clear();
        s_instances = 0;
    }

    private static ListViewItem? ContainerOf(DependencyObject? node)
    {
        while (node is not null and not ListViewItem) node = VisualTreeHelper.GetParent(node);
        return node as ListViewItem;
    }

    private static bool Unlinked(WinUI.ListView lv, FrameworkElement? root) =>
        ContainerOf(root) is not { } container || lv.IndexFromContainer(container) < 0;

    private static string? RowText(Harness h, string key) =>
        h.FindControl<TextBlock>(t => t.Text.StartsWith($"orphan-row {key} ", StringComparison.Ordinal))?.Text;

    internal class ChurnLeavesNoOrphanedRows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Reset();
            var host = H.CreateHost();
            host.Mount(_ => Component<Churn, int>(30));
            await Harness.Render(200);
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("OrphanRows_Mounted", lv is not null && s_live.Count > 0);
            if (lv is null) return;

            // The tester's "Fast (16 ms)" mode, then remove rows down to 10 and stop: the
            // last pass is a structural one with no update after it.
            var rng = new Random(7);
            int nextId = 30, count = 30, unlinkedSeen = 0;
            for (int step = 0; step < 300 || count > 10; step++)
            {
                int op = step >= 300 ? 0 : rng.Next(3);
                if (op == 0 && count <= 5) op = 2;
                if (op == 1 && count >= 60) op = 2;
                int a = rng.Next(count), b = rng.Next(count), at = rng.Next(count + 1), id = nextId;
                if (op == 0) count--;
                if (op == 1) { count++; nextId++; }
                s_update!(list =>
                {
                    var copy = list.ToList();
                    switch (op)
                    {
                        case 0: copy.RemoveAt(a % copy.Count); break;
                        case 1: copy.Insert(Math.Min(at, copy.Count), new RowProps($"c{id}")); break;
                        default: (copy[a % copy.Count], copy[b % copy.Count]) = (copy[b % copy.Count], copy[a % copy.Count]); break;
                    }
                    return copy;
                });
                await Task.Delay(16);
                unlinkedSeen += s_live.Values.Count(r => Unlinked(lv, r.Root.Current));
            }
            await Harness.Render(1500);
            // Positive control: the run must actually have unlinked some row's container, or the
            // checks below pass whether or not the sweep works.
            if (unlinkedSeen > 0)
                H.Check("OrphanRows_ChurnUnlinkedRows", true, $"observations={unlinkedSeen}");
            else
                H.Skip("OrphanRows_ChurnUnlinkedRows", "WinUI unlinked no row container during this run; the orphan checks are inconclusive");

            H.Check("OrphanRows_LiveRowsMatchItems", s_live.Count == lv.Items.Count,
                $"live={s_live.Count} items={lv.Items.Count}");

            int unlinked = 0, accepted = 0;
            foreach (var info in s_live.Values.Where(info => Unlinked(lv, info.Root.Current)).ToList())
            {
                unlinked++;
                if (FindWrapper(info.Root.Current) is { } wrapper && ReactorDiagnostics.TrySetState(wrapper, 1, "41", out _))
                    accepted++;
            }
            H.Check("OrphanRows_NoUnlinkedRowStaysMounted", unlinked == 0, $"unlinked={unlinked}");
            H.Check("OrphanRows_NoWriteToAnUnlinkedRow", accepted == 0, $"accepted={accepted}");

            int wrong = 0;
            string? wrongAt = null;
            var realized = H.FindAllControls<TextBlock>(t => t.Text.StartsWith("orphan-row ", StringComparison.Ordinal))
                .Select(text => (text, index: ContainerOf(text) is { } container ? lv.IndexFromContainer(container) : -1))
                .Where(row => row.index >= 0);
            foreach (var (text, index) in realized)
            {
                var key = (lv.Items[index] as ReactorRow)?.Key;
                if (!text.Text.StartsWith($"orphan-row {key} ", StringComparison.Ordinal))
                {
                    wrong++;
                    wrongAt ??= $"index={index} key={key} shown='{text.Text}'";
                }
            }
            H.Check("OrphanRows_RealizedRowsShowTheirItem", wrong == 0, wrongAt ?? "");

            host.Dispose();
            Reset();
        }
    }

    /// <summary>
    /// Moves a row with swaps and, with nothing rendering afterwards, checks the container WinUI
    /// parked for it. WinUI unlinks and parks that container offscreen in the layout pass after
    /// the update, without the recycle <c>ContainerContentChanging</c>, so neither the realize
    /// nor the recycle handler sees it; the row's old component stayed mounted, and a
    /// <see cref="ReactorDiagnostics.TrySetState"/> on it was accepted and never rendered
    /// (issue #1440).
    /// </summary>
    /// <remarks>
    /// <paramref name="collapseWithLastSwap"/> collapses the list in the render of the last swap
    /// and shows it again afterwards: WinUI lays the panel out (and parks the container) only once
    /// the list is shown, so a sweep spent on an earlier layout pass of the window would miss it.
    /// </remarks>
    internal class MovedRowLeavesNoParkedComponent(Harness h, string name, int count, (int A, int B)[] swaps, string movedKey,
        bool movedRowRealized, bool collapseWithLastSwap = false)
        : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Reset();
            var host = H.CreateHost();
            host.Mount(_ => Component<Churn, int>(count));
            await Harness.Render(300);
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check($"{name}_Mounted", lv is not null && s_live.Count > 0);
            if (lv is null) return;

            for (int i = 0; i < swaps.Length; i++)
            {
                var (a, b) = swaps[i];
                s_update!(list =>
                {
                    var copy = list.ToList();
                    (copy[a], copy[b]) = (copy[b], copy[a]);
                    return copy;
                });
                if (collapseWithLastSwap && i == swaps.Length - 1) s_setVisible!(false);
                await Harness.Render(300);
            }
            if (collapseWithLastSwap)
            {
                s_setVisible!(true);
                await Harness.Render(300);
            }

            // Positive control: WinUI parked a container still showing the moved row, or the
            // checks below pass whether or not the parked view is unmounted.
            var parked = H.FindAllControls<TextBlock>(t => t.Text.StartsWith($"orphan-row {movedKey} ", StringComparison.Ordinal))
                .Where(t => Unlinked(lv, t)).ToList();
            if (parked.Count > 0)
                H.Check($"{name}_ParkedContainer", true, $"parked={parked.Count}");
            else
                H.Skip($"{name}_ParkedContainer", "WinUI parked no container for the moved row during this run; the checks are inconclusive");

            var mountedParked = s_live.Values.Where(r => Unlinked(lv, r.Root.Current)).Select(r => $"{r.Key}#{r.Instance}").ToList();
            H.Check($"{name}_NoParkedRowStaysMounted", mountedParked.Count == 0, string.Join(", ", mountedParked));

            int accepted = parked.Count(t => FindWrapper(t) is { } wrapper && ReactorDiagnostics.TrySetState(wrapper, 1, "41", out _));
            H.Check($"{name}_NoWriteToParkedRow", accepted == 0, $"accepted={accepted}");

            var duplicates = s_live.Values.GroupBy(r => r.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            H.Check($"{name}_OneLiveComponentPerRow", duplicates.Count == 0, string.Join(", ", duplicates));

            // The sweep must not reach a container the list still shows: every linked container's
            // item view stays live.
            int deadLinked = 0;
            foreach (var child in lv.ItemsPanelRoot.Children)
                if (child is ListViewItem item && lv.IndexFromContainer(item) >= 0
                    && item.ContentTemplateRoot is ContentControl { Content: not null } itemHost
                    && Reconciler.GetElementTag(itemHost) is null)
                    deadLinked++;
            H.Check($"{name}_LinkedRowsStayLive", deadLinked == 0, $"deadLinked={deadLinked}");

            // One sweep per structural change, and it unsubscribes: later layout passes run none.
            int sweeps = Reconciler.ParkedContainerSweepCount;
            for (int i = 0; i < 3; i++)
            {
                lv.InvalidateMeasure();
                await Harness.Render(50);
            }
            H.Check($"{name}_SweepUnsubscribes", Reconciler.ParkedContainerSweepCount == sweeps,
                $"before={sweeps} after={Reconciler.ParkedContainerSweepCount}");

            if (movedRowRealized)
            {
                // The row's live view, in the container that shows it, must still respond.
                var shown = H.FindAllControls<TextBlock>(t => t.Text.StartsWith($"orphan-row {movedKey} ", StringComparison.Ordinal))
                    .FirstOrDefault(t => !Unlinked(lv, t));
                var button = shown is null ? null : FindDescendant<Button>(VisualTreeHelper.GetParent(shown));
                if (button is not null)
                {
                    var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
                    ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(
                        Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    await Harness.Render();
                }
                H.Check($"{name}_MovedRowResponds", shown?.Text.EndsWith(" local 1", StringComparison.Ordinal) == true,
                    shown?.Text ?? "(moved row not shown)");
            }

            host.Dispose();
            Reset();
        }

        private static T? FindDescendant<T>(DependencyObject? node) where T : DependencyObject
        {
            if (node is null) return null;
            if (node is T match) return match;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (FindDescendant<T>(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            return null;
        }
    }

    // The component wrapper is the Border directly above the row's HStack.
    private static UIElement? FindWrapper(FrameworkElement? root) =>
        root is null ? null : VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(root)) as UIElement;

    internal class MoveKeepsRowComponent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Reset();
            var host = H.CreateHost();
            host.Mount(_ => Component<Churn, int>(5));
            await Harness.Render(200);
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("MoveRow_Mounted", lv is not null);
            if (lv is null) return;

            for (int i = 0; i < 3; i++)
            {
                H.ClickButton("orphan+c2");
                await Harness.Render();
            }
            var before = RowText(H, "c2");
            H.Check("MoveRow_Setup", before?.EndsWith(" local 3", StringComparison.Ordinal) == true, before ?? "(none)");

            // One swap is a single Move in the list's item source, and nothing renders after it.
            s_update!(list =>
            {
                var copy = list.ToList();
                (copy[2], copy[3]) = (copy[3], copy[2]);
                return copy;
            });
            await Harness.Render(300);

            // WinUI re-realizes a moved row's container, so whether the row keeps its component
            // is up to the list (it currently doesn't). What the sweep must never do is leave the
            // row showing a view it already unmounted: that view would no longer respond.
            var after = RowText(H, "c2");
            Console.WriteLine($"# moved row kept its component: {after == before} ('{before}' -> '{after}')");
            int shown = int.Parse(after?[(after.LastIndexOf(' ') + 1)..] ?? "-1");
            H.ClickButton("orphan+c2");
            await Harness.Render();
            var clicked = RowText(H, "c2");
            H.Check("MoveRow_StillResponds", clicked?.EndsWith($" local {shown + 1}", StringComparison.Ordinal) == true,
                $"after='{after}' clicked='{clicked}'");
            H.Check("MoveRow_NoExtraRows", s_live.Count == lv.Items.Count, $"live={s_live.Count} items={lv.Items.Count}");
            host.Dispose();
            Reset();
        }
    }
}
