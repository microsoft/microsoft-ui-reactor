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
/// shows. Every mounted row component must belong to a row the list still holds.
/// </summary>
internal static class TemplatedListOrphanContainerFixtures
{
    private sealed record RowProps(string Key);

    private static readonly Dictionary<object, Microsoft.UI.Reactor.Input.ElementRef<FrameworkElement>> s_live = new(ReferenceEqualityComparer.Instance);
    private static Action<Func<List<RowProps>, List<RowProps>>>? s_update;

    private sealed class Row : Component<RowProps>
    {
        public override Element Render()
        {
            var (clicks, _) = UseState(0);
            var root = this.UseElementRef<FrameworkElement>();
            UseEffect(() =>
            {
                s_live[this] = root;
                return () => s_live.Remove(this);
            });
            return TextBlock($"orphan-row {Props.Key} local {clicks}").Ref(root);
        }
    }

    private sealed class Churn : Component
    {
        public override Element Render()
        {
            var (items, update) = UseReducer(Enumerable.Range(0, 30).Select(i => new RowProps($"c{i}")).ToList());
            s_update = update;
            return Border(ListView<RowProps>(items, x => x.Key, (x, _) => Component<Row, RowProps>(x))).Height(300);
        }
    }

    private static ListViewItem? ContainerOf(DependencyObject? node)
    {
        while (node is not null and not ListViewItem) node = VisualTreeHelper.GetParent(node);
        return node as ListViewItem;
    }

    internal class ChurnLeavesNoOrphanedRows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_live.Clear();
            var host = H.CreateHost();
            host.Mount(new Churn());
            await Harness.Render(200);
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("OrphanRows_Mounted", lv is not null && s_live.Count > 0);
            if (lv is null) return;

            // The tester's "Fast (16 ms)" mode, then shrink to 10 rows.
            var rng = new Random(7);
            int nextId = 30;
            for (int step = 0; step < 340; step++)
            {
                bool shrink = step >= 300;
                int op = shrink ? 0 : rng.Next(3);
                s_update!(list =>
                {
                    var copy = list.ToList();
                    switch (op)
                    {
                        case 0 when copy.Count > (shrink ? 10 : 5): copy.RemoveAt(rng.Next(copy.Count)); break;
                        case 0: break;
                        case 1 when copy.Count < 60: copy.Insert(rng.Next(copy.Count + 1), new RowProps($"c{nextId++}")); break;
                        default:
                            var a = rng.Next(copy.Count);
                            var b = rng.Next(copy.Count);
                            (copy[a], copy[b]) = (copy[b], copy[a]);
                            break;
                    }
                    return copy;
                });
                await Task.Delay(16);
            }
            await Harness.Render(1500);

            H.Check("OrphanRows_LiveRowsMatchItems", s_live.Count == lv.Items.Count,
                $"live={s_live.Count} items={lv.Items.Count}");

            int unlinked = 0, accepted = 0;
            foreach (var root in s_live.Values.Select(r => r.Current).ToList())
            {
                var container = ContainerOf(root);
                if (container is not null && lv.IndexFromContainer(container) >= 0) continue;
                unlinked++;
                if (VisualTreeHelper.GetParent(root) is UIElement wrapper
                    && ReactorDiagnostics.TrySetState(wrapper, 0, "41", out _))
                    accepted++;
            }
            H.Check("OrphanRows_NoUnlinkedRowStaysMounted", unlinked == 0, $"unlinked={unlinked}");
            H.Check("OrphanRows_NoWriteToAnUnlinkedRow", accepted == 0, $"accepted={accepted}");

            int wrong = 0;
            string? wrongAt = null;
            foreach (var container in H.FindAllControls<ListViewItem>(_ => true))
            {
                int index = lv.IndexFromContainer(container);
                if (index < 0) continue;
                var key = (lv.Items[index] as ReactorRow)?.Key;
                var shown = container.ContentTemplateRoot is ContentControl { Content: Border { Child: TextBlock text } } ? text.Text : "(none)";
                if (!shown.StartsWith($"orphan-row {key} ", StringComparison.Ordinal))
                {
                    wrong++;
                    wrongAt ??= $"index={index} key={key} shown='{shown}'";
                }
            }
            H.Check("OrphanRows_RealizedRowsShowTheirItem", wrong == 0, wrongAt ?? "");
            host.Dispose();
            s_live.Clear();
        }
    }
}
