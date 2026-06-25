using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selection / item-click handler behaviour for the V1 <c>ListViewHandler</c>
/// and <c>GridViewHandler</c> covered by the perf PR:
/// <list type="bullet">
///   <item>#100 — the multi-select <c>OnSelectionChanged</c> snapshot is built
///     with a typed copy loop (was <c>SelectedItems.OfType&lt;int&gt;().ToList()</c>).
///     The handler binds <c>ItemsSource = [0..N-1]</c> ints, so selecting two
///     items must surface a <c>List&lt;int&gt;</c> with exactly those values, in
///     <c>SelectedItems</c> order (the copy loop preserves order), and clearing
///     the selection must surface an empty snapshot.</item>
///   <item>#110 — <c>ItemClick</c> is subscribed ONCE at Mount and only
///     <c>IsItemClickEnabled</c> is flipped in Update. This fixture guards the
///     observable Update half: toggling <c>OnItemClick</c> across record-with
///     cycles keeps <c>IsItemClickEnabled</c> tracking <c>OnItemClick is not null</c>
///     without error. (The "fires exactly once / no duplicate" half needs real
///     pointer input and is an E2E-tier concern.)</item>
/// </list>
/// </summary>
internal static class ListViewHandlerSelectionFixtures
{
    private class MultiSelectListViewComponent : Component
    {
        public static IReadOnlyList<int>? LastSnapshot;
        public static int CallbackCount;

        public static void Reset()
        {
            LastSnapshot = null;
            CallbackCount = 0;
        }

        public override Element Render() =>
            new ListViewElement(new Element[] { TextBlock("a"), TextBlock("b"), TextBlock("c") })
            {
                SelectionMode = ListViewSelectionMode.Multiple,
                OnSelectionChanged = snap => { CallbackCount++; LastSnapshot = snap; },
            }.Set(l => l.Name = "lvMultiSel");
    }

    internal class ListView_MultiSelection_TypedSnapshot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            MultiSelectListViewComponent.Reset();

            var host = H.CreateHost();
            host.Mount(new MultiSelectListViewComponent());
            await Harness.Render();

            var lv = H.FindControl<ListView>(l => l.Name == "lvMultiSel");
            H.Check("MultiSelLV_Mounted", lv is not null);
            if (lv is null) return;
            H.Check("MultiSelLV_ModeMultiple", lv.SelectionMode == ListViewSelectionMode.Multiple);

            // ItemsSource is [0,1,2]; select items 0 and 2. Each Add raises
            // SelectionChanged → the handler's typed copy loop snapshots the
            // current SelectedItems into a List<int>.
            lv.SelectedItems.Add(0);
            lv.SelectedItems.Add(2);
            await Harness.Render();

            var snap = MultiSelectListViewComponent.LastSnapshot;
            H.Check("MultiSelLV_SnapshotNotNull", snap is not null);
            H.Check("MultiSelLV_SnapshotTypedInts",
                snap is not null && snap.Count == 2 && snap.Contains(0) && snap.Contains(2));
            // #100: the typed copy loop walks SelectedItems in order, so the
            // snapshot must match SelectedItems element-for-element (independent of
            // how WinUI happens to order the selection).
            var expectedOrder = lv.SelectedItems.Cast<int>().ToList();
            H.Check("MultiSelLV_SnapshotMatchesSelectedItemsOrder",
                snap is not null && snap.SequenceEqual(expectedOrder));
            H.Check("MultiSelLV_CallbackFired", MultiSelectListViewComponent.CallbackCount >= 1);

            // Clearing the selection raises SelectionChanged; the copy loop over an
            // empty SelectedItems must surface an empty (non-null) snapshot.
            lv.SelectedItems.Clear();
            await Harness.Render();
            var cleared = MultiSelectListViewComponent.LastSnapshot;
            H.Check("MultiSelLV_EmptyAfterClear", cleared is not null && cleared.Count == 0);
        }
    }

    private class MultiSelectGridViewComponent : Component
    {
        public static IReadOnlyList<int>? LastSnapshot;
        public static int CallbackCount;

        public static void Reset()
        {
            LastSnapshot = null;
            CallbackCount = 0;
        }

        public override Element Render() =>
            new GridViewElement(new Element[] { TextBlock("a"), TextBlock("b"), TextBlock("c") })
            {
                SelectionMode = ListViewSelectionMode.Multiple,
                OnSelectionChanged = snap => { CallbackCount++; LastSnapshot = snap; },
            }.Set(g => g.Name = "gvMultiSel");
    }

    internal class GridView_MultiSelection_TypedSnapshot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            MultiSelectGridViewComponent.Reset();

            var host = H.CreateHost();
            host.Mount(new MultiSelectGridViewComponent());
            await Harness.Render();

            var gv = H.FindControl<GridView>(g => g.Name == "gvMultiSel");
            H.Check("MultiSelGV_Mounted", gv is not null);
            if (gv is null) return;
            H.Check("MultiSelGV_ModeMultiple", gv.SelectionMode == ListViewSelectionMode.Multiple);

            gv.SelectedItems.Add(1);
            gv.SelectedItems.Add(2);
            await Harness.Render();

            var snap = MultiSelectGridViewComponent.LastSnapshot;
            H.Check("MultiSelGV_SnapshotNotNull", snap is not null);
            H.Check("MultiSelGV_SnapshotTypedInts",
                snap is not null && snap.Count == 2 && snap.Contains(1) && snap.Contains(2));
            var expectedOrder = gv.SelectedItems.Cast<int>().ToList();
            H.Check("MultiSelGV_SnapshotMatchesSelectedItemsOrder",
                snap is not null && snap.SequenceEqual(expectedOrder));
            H.Check("MultiSelGV_CallbackFired", MultiSelectGridViewComponent.CallbackCount >= 1);

            gv.SelectedItems.Clear();
            await Harness.Render();
            var cleared = MultiSelectGridViewComponent.LastSnapshot;
            H.Check("MultiSelGV_EmptyAfterClear", cleared is not null && cleared.Count == 0);
        }
    }

    private class ToggleItemClickComponent : Component
    {
        public static bool Enable;

        public override Element Render()
        {
            var el = new ListViewElement(new Element[] { TextBlock("a"), TextBlock("b") })
                .Set(l => l.Name = "lvClickToggle");
            return Enable ? el with { OnItemClick = _ => { } } : el;
        }
    }

    internal class ListView_ItemClick_SubscribeOnce_TracksEnabled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ToggleItemClickComponent.Enable = false;

            var host = H.CreateHost();
            host.Mount(new ToggleItemClickComponent());
            await Harness.Render();

            var lv = H.FindControl<ListView>(l => l.Name == "lvClickToggle");
            H.Check("ItemClickToggle_Mounted", lv is not null);
            if (lv is null) return;
            H.Check("ItemClickToggle_InitiallyDisabled", !lv.IsItemClickEnabled);

            // Toggle OnItemClick on/off across several record-with re-renders. The
            // handler subscribes ItemClick once at Mount (#110); Update only flips
            // IsItemClickEnabled, which must keep tracking OnItemClick with no
            // handler accumulation or exception.
            for (int i = 0; i < 4; i++)
            {
                ToggleItemClickComponent.Enable = (i % 2 == 0);
                host.RequestRender(force: true);
                await Harness.Render();
                H.Check($"ItemClickToggle_Tracks_{i}",
                    lv.IsItemClickEnabled == ToggleItemClickComponent.Enable);
            }
        }
    }
}
