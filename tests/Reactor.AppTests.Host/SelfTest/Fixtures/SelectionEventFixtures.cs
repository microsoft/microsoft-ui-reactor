using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Verifies that OnSelectionChanged callbacks wired by the reconciler
/// actually fire through to user code when the underlying WinUI control
/// raises its native selection event.
///
/// Each fixture mounts the control with a counter-incrementing callback,
/// waits for initial mount to settle, resets the counter (initial selection
/// wiring can fire once during mount on some controls), then drives the
/// WinUI control directly and asserts the callback ran with the expected
/// payload.
/// </summary>
internal static class SelectionEventFixtures
{
    internal class ComboBoxSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            int lastIndex = -1;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                ComboBox(
                    ["A", "B", "C"],
                    selectedIndex: -1,
                    onSelectionChanged: i => { count++; lastIndex = i; })
                    .Set(c => c.Name = "comboSel")
            ));
            await Harness.Render();

            var cb = H.FindControl<ComboBox>(c => c.Name == "comboSel");
            H.Check("ComboBoxSel_Mounted", cb is not null);

            count = 0; lastIndex = -1;
            if (cb is not null) cb.SelectedIndex = 2;

            H.Check("ComboBoxSel_CallbackFired", count >= 1);
            H.Check("ComboBoxSel_PayloadIndex", lastIndex == 2);
        }
    }

    internal class RadioButtonsSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            int lastIndex = -1;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                RadioButtons(
                    ["R1", "R2", "R3"],
                    selectedIndex: -1,
                    onSelectionChanged: i => { count++; lastIndex = i; })
                    .Set(r => r.Name = "rbsSel")
            ));
            await Harness.Render();

            var rbs = H.FindControl<RadioButtons>(r => r.Name == "rbsSel");
            H.Check("RadioButtonsSel_Mounted", rbs is not null);

            count = 0; lastIndex = -1;
            if (rbs is not null) rbs.SelectedIndex = 1;

            // RadioButtons can raise SelectionChanged via the dispatcher (unlike
            // ComboBox/ListView/etc. which fire it synchronously in the setter),
            // so pump once before asserting on the counter.
            await Harness.Render();

            H.Check("RadioButtonsSel_CallbackFired", count >= 1);
            H.Check("RadioButtonsSel_PayloadIndex", lastIndex == 1);
        }
    }

    internal class ListViewSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            int lastIndex = -1;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new ListViewElement([
                    TextBlock("LVA"),
                    TextBlock("LVB"),
                    TextBlock("LVC"),
                ])
                {
                    SelectedIndex = -1,
                    OnSelectionChanged = i => { count++; lastIndex = i; },
                }.Set(l => l.Name = "lvSel")
            ));
            await Harness.Render();

            var lv = H.FindControl<ListView>(l => l.Name == "lvSel");
            H.Check("ListViewSel_Mounted", lv is not null);

            count = 0; lastIndex = -1;
            if (lv is not null) lv.SelectedIndex = 1;

            H.Check("ListViewSel_CallbackFired", count >= 1);
            H.Check("ListViewSel_PayloadIndex", lastIndex == 1);
        }
    }

    internal class GridViewSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            int lastIndex = -1;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new GridViewElement([
                    TextBlock("GVA"),
                    TextBlock("GVB"),
                    TextBlock("GVC"),
                ])
                {
                    SelectedIndex = -1,
                    OnSelectionChanged = i => { count++; lastIndex = i; },
                }.Set(g => g.Name = "gvSel")
            ));
            await Harness.Render();

            var gv = H.FindControl<GridView>(g => g.Name == "gvSel");
            H.Check("GridViewSel_Mounted", gv is not null);

            count = 0; lastIndex = -1;
            if (gv is not null) gv.SelectedIndex = 2;

            H.Check("GridViewSel_CallbackFired", count >= 1);
            H.Check("GridViewSel_PayloadIndex", lastIndex == 2);
        }
    }

    internal class FlipViewSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            int lastIndex = -1;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new FlipViewElement([
                    TextBlock("FVA"),
                    TextBlock("FVB"),
                    TextBlock("FVC"),
                ])
                {
                    SelectedIndex = 0,
                    OnSelectionChanged = i => { count++; lastIndex = i; },
                }.Set(f => f.Name = "fvSel")
            ));
            await Harness.Render();

            var fv = H.FindControl<FlipView>(f => f.Name == "fvSel");
            H.Check("FlipViewSel_Mounted", fv is not null);

            count = 0; lastIndex = -1;
            if (fv is not null) fv.SelectedIndex = 2;

            H.Check("FlipViewSel_CallbackFired", count >= 1);
            H.Check("FlipViewSel_PayloadIndex", lastIndex == 2);
        }
    }

    internal class TabViewSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            int lastIndex = -1;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new TabViewElement([
                    Tab("T1", TextBlock("tab1")),
                    Tab("T2", TextBlock("tab2")),
                    Tab("T3", TextBlock("tab3")),
                ])
                {
                    SelectedIndex = 0,
                    OnSelectionChanged = i => { count++; lastIndex = i; },
                }.Set(t => t.Name = "tvSel")
            ));
            await Harness.Render();

            var tv = H.FindControl<TabView>(t => t.Name == "tvSel");
            H.Check("TabViewSel_Mounted", tv is not null);

            count = 0; lastIndex = -1;
            if (tv is not null) tv.SelectedIndex = 2;

            // TabView can raise SelectionChanged via the dispatcher (unlike
            // ComboBox/ListView/etc. which fire it synchronously in the setter),
            // so pump once before asserting on the counter.
            await Harness.Render();

            H.Check("TabViewSel_CallbackFired", count >= 1);
            H.Check("TabViewSel_PayloadIndex", lastIndex == 2);
        }
    }

    internal class PivotSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            int lastIndex = -1;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new PivotElement([
                    PivotItem("P1", TextBlock("p1")),
                    PivotItem("P2", TextBlock("p2")),
                    PivotItem("P3", TextBlock("p3")),
                ])
                {
                    SelectedIndex = 0,
                    OnSelectionChanged = i => { count++; lastIndex = i; },
                }.Set(p => p.Name = "pivSel")
            ));
            await Harness.Render();

            var piv = H.FindControl<Pivot>(p => p.Name == "pivSel");
            H.Check("PivotSel_Mounted", piv is not null);

            count = 0; lastIndex = -1;
            if (piv is not null) piv.SelectedIndex = 2;

            H.Check("PivotSel_CallbackFired", count >= 1);
            H.Check("PivotSel_PayloadIndex", lastIndex == 2);
        }
    }

    internal class NavigationViewSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 0;
            string? lastTag = null;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new NavigationViewElement(
                    [
                        NavItem("Home", tag: "home"),
                        NavItem("Settings", tag: "settings"),
                        NavItem("About", tag: "about"),
                    ],
                    TextBlock("nav content"))
                {
                    OnSelectionChanged = tag => { count++; lastTag = tag; },
                    IsSettingsVisible = false,
                }.Set(n => n.Name = "navSel")
            ));
            await Harness.Render();

            var nv = H.FindControl<NavigationView>(n => n.Name == "navSel");
            H.Check("NavViewSel_Mounted", nv is not null);

            count = 0; lastTag = null;

            // Find the nav item tagged "about" and select it.
            NavigationViewItem? target = null;
            if (nv is not null)
            {
                foreach (var item in nv.MenuItems.OfType<NavigationViewItem>())
                    if ((item.Tag as string) == "about") { target = item; break; }
            }
            H.Check("NavViewSel_TargetItemFound", target is not null);

            if (nv is not null && target is not null)
                nv.SelectedItem = target;

            H.Check("NavViewSel_CallbackFired", count >= 1);
            H.Check("NavViewSel_PayloadTag", lastTag == "about");
        }
    }
}
