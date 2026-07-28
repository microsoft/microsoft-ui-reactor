using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression for https://github.com/microsoft/microsoft-ui-reactor/issues/845
///
/// <c>ListViewHandler.Update</c> / <c>GridViewHandler.Update</c> used to gate the
/// <c>Header</c> and <c>ItemContainerStyle</c> assignments on the <i>new</i> value
/// being non-null (<c>if (n.Header is not null) …</c> and
/// <c>… &amp;&amp; n.ItemContainerStyle is not null</c>), so a present→null transition
/// was a no-op and the stale value stuck on the live WinUI control. The fix gates
/// on <i>change</i> (<c>!ReferenceEquals(o.X, n.X)</c>) so a clear-to-null is applied.
///
/// These fixtures need a live WinUI control (the symptom is a control property), so
/// they mount, set a non-null Header + ItemContainerStyle, re-render with both null,
/// and assert the live control's properties are now null. Non-vacuity: the
/// <c>…ClearedToNull</c> checks FAIL on pre-fix code (control keeps the old value)
/// and PASS post-fix.
/// </summary>
internal static class Issue845ClearHeaderItemContainerStyleFixtures
{
    // Hoisted so the ItemContainerStyle reference is stable across the phase-0
    // renders (the Update change-gate is reference-based); phase 1 uses null.
    private static readonly Style s_listViewItemStyle =
        new() { TargetType = typeof(WinUI.ListViewItem) };

    private static readonly Style s_gridViewItemStyle =
        new() { TargetType = typeof(WinUI.GridViewItem) };

    internal class ListViewClearsHeaderAndItemContainerStyle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("Issue845ClearLV", () => set(1)),
                    ListView(TextBlock("LV_Item")) with
                    {
                        Header = phase == 0 ? "LV_HDR" : null,
                        ItemContainerStyle = phase == 0 ? s_listViewItemStyle : null,
                    }
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("Issue845_LV_HeaderSet_Initial", lv?.Header as string == "LV_HDR");
            H.Check("Issue845_LV_StyleSet_Initial", lv?.ItemContainerStyle is not null);

            H.ClickButton("Issue845ClearLV");
            await Harness.Render();

            // Same reconciled control (Update ran) — its Header/ItemContainerStyle
            // must now be cleared. Pre-fix these stay non-null and the checks fail.
            lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("Issue845_LV_HeaderClearedToNull", lv is not null && lv.Header is null);
            H.Check("Issue845_LV_StyleClearedToNull", lv is not null && lv.ItemContainerStyle is null);
        }
    }

    internal class GridViewClearsHeaderAndItemContainerStyle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("Issue845ClearGV", () => set(1)),
                    GridView(TextBlock("GV_Item")) with
                    {
                        Header = phase == 0 ? "GV_HDR" : null,
                        ItemContainerStyle = phase == 0 ? s_gridViewItemStyle : null,
                    }
                );
            });

            await Harness.Render();

            var gv = H.FindControl<WinUI.GridView>(_ => true);
            H.Check("Issue845_GV_HeaderSet_Initial", gv?.Header as string == "GV_HDR");
            H.Check("Issue845_GV_StyleSet_Initial", gv?.ItemContainerStyle is not null);

            H.ClickButton("Issue845ClearGV");
            await Harness.Render();

            gv = H.FindControl<WinUI.GridView>(_ => true);
            H.Check("Issue845_GV_HeaderClearedToNull", gv is not null && gv.Header is null);
            H.Check("Issue845_GV_StyleClearedToNull", gv is not null && gv.ItemContainerStyle is null);
        }
    }
}
