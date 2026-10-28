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
            s_update = update;
            return Border(ListView<RowProps>(items, x => x.Key, (x, _) => Component<Row, RowProps>(x))).Height(300);
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

        // The component wrapper is the Border directly above the row's HStack.
        private static UIElement? FindWrapper(FrameworkElement? root) =>
            root is null ? null : VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(root)) as UIElement;
    }

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
