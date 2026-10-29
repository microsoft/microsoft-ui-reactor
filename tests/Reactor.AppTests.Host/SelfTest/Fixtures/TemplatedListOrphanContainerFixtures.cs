using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting;
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
    private static Action<bool>? s_setReordered;
    private static Action<bool>? s_setPresent;
    private static Action<int>? s_selectTab;
    // Armed by a fixture: what the next cleanup of a row view in a parked container does —
    // throw, or mount a host (in s_nestedWindow) whose render fails and whose handler declines it.
    private enum ParkedCleanup { None, Throw, NestedDeclined }
    private static ParkedCleanup s_parkedCleanup;
    private static Window? s_nestedWindow;
    private static ReactorHost? s_nestedHost;
    private const string ParkedCleanupMessage = "orphan-row parked cleanup boom";
    private const string NestedDeclinedMessage = "orphan-row nested render declined";

    private sealed class Row : Component<RowProps>
    {
        public override Element Render()
        {
            var instance = UseRef(0);
            if (instance.Current == 0) instance.Current = ++s_instances;
            var (clicks, setClicks) = UseState(0);
            var root = this.UseElementRef<FrameworkElement>();
            // Before the effect below, so a throw that stopped the cleanups would skip its own.
            UseEffect(() => () =>
            {
                if (s_parkedCleanup == ParkedCleanup.None || ContainerOf(root.Current) is not { ActualOffset.X: <= -10000 })
                    return;
                var mode = s_parkedCleanup;
                s_parkedCleanup = ParkedCleanup.None;
                if (mode == ParkedCleanup.Throw)
                    throw new InvalidOperationException(ParkedCleanupMessage);
                s_nestedHost = new ReactorHost(s_nestedWindow!) { RenderErrorHandler = e => { e.Propagate(); return null; } };
                s_nestedHost.Mount(_ => throw new InvalidOperationException(NestedDeclinedMessage));
            });
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
            var (reordered, setReordered) = UseState(false);
            var (present, setPresent) = UseState(true);
            s_update = update;
            s_setVisible = setVisible;
            s_setReordered = setReordered;
            s_setPresent = setPresent;
            if (!present) return TextBlock("orphan-gone");
            // Keyed, so reordering moves the list's Border within the stack: WinUI removes it from
            // the tree and inserts it again.
            var list = Border(ListView<RowProps>(items, x => x.Key, (x, _) => Component<Row, RowProps>(x)))
                .Height(300).IsVisible(visible).WithKey("list");
            var spacer = TextBlock("orphan-spacer").WithKey("spacer");
            return reordered ? VStack(list, spacer) : VStack(spacer, list);
        }
    }

    // The list in the second tab of a TabView: while the first tab is selected, the list stays
    // mounted (and updates) but WinUI takes it out of the visual tree.
    private sealed class TabbedChurn : Component<int>
    {
        public override Element Render()
        {
            var (items, update) = UseReducer(Enumerable.Range(0, Props).Select(i => new RowProps($"c{i}")).ToList());
            var (tab, setTab) = UseState(1);
            s_update = update;
            s_selectTab = setTab;
            return TabView(tab, setTab,
                Tab("orphan-tab-a", TextBlock("orphan-other-tab")),
                Tab("orphan-tab-b", Border(ListView<RowProps>(items, x => x.Key, (x, _) => Component<Row, RowProps>(x))).Height(300)));
        }
    }

    private static void Reset()
    {
        s_live.Clear();
        s_instances = 0;
        s_parkedCleanup = ParkedCleanup.None;
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
    /// <paramref name="reinsertWithLastSwap"/> moves the list within its parent in the render of the
    /// last swap: WinUI removes and inserts it again, and lays it out before raising its
    /// <c>Loaded</c>, so a sweep must not take "not loaded" for "not laid out".
    /// <paramref name="inUnselectedTab"/> hosts the list in a TabView tab and switches to the other
    /// tab in the render of the last swap: the list leaves the tree unlaid out, and WinUI parks the
    /// container when its tab is selected again.
    /// </remarks>
    internal class MovedRowLeavesNoParkedComponent(Harness h, string name, int count, (int A, int B)[] swaps, string movedKey,
        bool collapseWithLastSwap = false, bool reinsertWithLastSwap = false, bool inUnselectedTab = false,
        bool throwingParkedCleanup = false, bool nestedDeclinedParkedCleanup = false)
        : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previousUnhandled = ReactorApplication.OnUnhandledException;
            try { await RunCoreAsync(); }
            finally
            {
                ReactorApplication.OnUnhandledException = previousUnhandled;
                s_nestedHost?.Dispose();
                s_nestedHost = null;
                s_nestedWindow?.Close();
                s_nestedWindow = null;
            }
        }

        private async Task RunCoreAsync()
        {
            Reset();
            var host = H.CreateHost();
            // Report-only handler: it would swallow any cleanup failure it is handed.
            var errors = new List<RenderError>();
            if (throwingParkedCleanup || nestedDeclinedParkedCleanup)
                host.RenderErrorHandler = e => { errors.Add(e); return null; };
            Exception? escaped = null;
            Microsoft.UI.Xaml.UnhandledExceptionEventHandler? captureEscape = null;
            if (nestedDeclinedParkedCleanup)
            {
                // The app declines the nested failure (it propagates); once it leaves the sweep,
                // WinUI surfaces it here, where the fixture captures it and keeps the host alive.
                ReactorApplication.OnUnhandledException = ex => ex.Message != NestedDeclinedMessage;
                captureEscape = (_, e) =>
                {
                    if (e.Exception?.Message != NestedDeclinedMessage) return;
                    escaped ??= e.Exception;
                    e.Handled = true;
                };
                Application.Current.UnhandledException += captureEscape;
                s_nestedWindow = new Window { Title = "Orphan rows nested declined" };
                s_nestedWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(300, 200));
                s_nestedWindow.Activate();
            }
            try { await RunChecksAsync(host, errors, () => escaped); }
            finally
            {
                if (captureEscape is not null) Application.Current.UnhandledException -= captureEscape;
            }
        }

        private async Task RunChecksAsync(ReactorHost host, List<RenderError> errors, Func<Exception?> escapedSoFar)
        {
            host.Mount(_ => inUnselectedTab ? Component<TabbedChurn, int>(count) : Component<Churn, int>(count));
            await Harness.Render(300);
            // Exactly ListView: a TabView's tab strip is a ListView subclass.
            var lv = H.FindControl<WinUI.ListView>(l => l.GetType() == typeof(WinUI.ListView));
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
                if (reinsertWithLastSwap && i == swaps.Length - 1) s_setReordered!(true);
                if (inUnselectedTab && i == swaps.Length - 1) s_selectTab!(0);
                if (throwingParkedCleanup && i == swaps.Length - 1) s_parkedCleanup = ParkedCleanup.Throw;
                if (nestedDeclinedParkedCleanup && i == swaps.Length - 1) s_parkedCleanup = ParkedCleanup.NestedDeclined;
                await Harness.Render(300);
            }
            if (collapseWithLastSwap)
            {
                s_setVisible!(true);
                await Harness.Render(300);
            }
            if (inUnselectedTab)
            {
                s_selectTab!(1);
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

            if (throwingParkedCleanup || nestedDeclinedParkedCleanup)
                H.Check($"{name}_ParkedCleanupRan", s_parkedCleanup == ParkedCleanup.None);
            if (throwingParkedCleanup)
            {
                // The sweep unmounted the parked view even though its cleanup threw, and reported it.
                H.Check($"{name}_CleanupFailureReported", errors.Any(e => e.Source == RenderErrorSource.Cleanup
                    && e.Exception.Message == ParkedCleanupMessage), $"errors={errors.Count}");
            }
            if (nestedDeclinedParkedCleanup)
            {
                // An exception the nested work already declined is not handed to this list's
                // handler (which would swallow it); it keeps going out.
                H.Check($"{name}_DeclinedNotReportedAgain", !errors.Any(e => e.Exception.Message == NestedDeclinedMessage),
                    string.Join(",", errors.Select(e => e.Exception.Message)));
                var escaped = escapedSoFar();
                H.Check($"{name}_DeclinedEscapes", escaped?.Message == NestedDeclinedMessage, escaped?.Message ?? "(nothing escaped)");
            }
            s_parkedCleanup = ParkedCleanup.None;

            var mountedParked = s_live.Values.Where(r => Unlinked(lv, r.Root.Current)).Select(r => $"{r.Key}#{r.Instance}").ToList();
            H.Check($"{name}_NoParkedRowStaysMounted", mountedParked.Count == 0, string.Join(", ", mountedParked));

            int accepted = parked.Count(t => FindWrapper(t) is { } wrapper && ReactorDiagnostics.TrySetState(wrapper, 1, "41", out _));
            H.Check($"{name}_NoWriteToParkedRow", accepted == 0, $"accepted={accepted}");

            var duplicates = s_live.Values.GroupBy(r => r.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            H.Check($"{name}_OneLiveComponentPerRow", duplicates.Count == 0, string.Join(", ", duplicates));

            // The sweep must not reach a container the list still shows: every linked container's
            // item view stays live.
            int deadLinked = lv.ItemsPanelRoot.Children.OfType<ListViewItem>().Count(item =>
                lv.IndexFromContainer(item) >= 0
                && item.ContentTemplateRoot is ContentControl { Content: not null } itemHost
                && Reconciler.GetElementTag(itemHost) is null);
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

            // The moved row, scrolled into view if it left the realized range, shows a live view:
            // the sweep unmounted only the parked container's copy.
            var movedRow = lv.Items.OfType<ReactorRow>().FirstOrDefault(r => r.Key == movedKey);
            if (movedRow is not null) lv.ScrollIntoView(movedRow);
            await Harness.Render(300);
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

    /// <summary>
    /// A list unmounted while collapsed, with a sweep still waiting for it to be shown, must
    /// drop that sweep, so no handler outlives the list (issue #1440).
    /// </summary>
    internal class UnmountWhileCollapsedDropsPendingSweep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Reset();
            var host = H.CreateHost();
            host.Mount(_ => Component<Churn, int>(30));
            await Harness.Render(300);
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("UnmountCollapsed_Mounted", lv is not null);
            if (lv is null) return;

            s_update!(list =>
            {
                var copy = list.ToList();
                (copy[0], copy[1]) = (copy[1], copy[0]);
                return copy;
            });
            s_setVisible!(false);
            await Harness.Render(300);
            var list = Reconciler.GetListState(lv);
            H.Check("UnmountCollapsed_SweepPending", list is { ShownWatch: not null } or { PostLayoutSweep: not null } or { LoadedWatch: not null });

            s_setPresent!(false);
            await Harness.Render(300);
            H.Check("UnmountCollapsed_ListUnmounted", H.FindControl<WinUI.ListView>(_ => true) is null);
            H.Check("UnmountCollapsed_SweepDropped", list is { ShownWatch: null, PostLayoutSweep: null, LoadedWatch: null });
            host.Dispose();
            Reset();
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
