using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #914 — WinUI's <c>DefaultTabViewStyle</c> sets <c>VerticalAlignment="Top"</c> on
/// the TabView itself, so the control is arranged at its desired height and the <c>*</c>
/// content row of its template never receives the leftover space. Tab content therefore
/// collapses to its own height and a tab child with a background paints only a band.
///
/// <para>These fixtures pin the <c>FillContentArea</c> opt-in against a live control: the
/// WinUI default must be preserved when the flag is off, the tab body must actually grow
/// to the remaining height when it is on, the transition must work in both directions, and
/// an explicit <c>.VAlign(…)</c> must still win.</para>
/// </summary>
internal static class TabViewFillContentAreaFixtures
{
    // The host Grid is 400 px tall with an Auto button row on top, so the TabView's slot is
    // ~350 px. The tab strip eats ~40 px, leaving ~310 px of content area; an unstretched
    // body is a single line of text (~20 px). The 150 px threshold sits far outside both.
    private const double HostHeight = 400;
    private const double FilledBodyFloor = 150;

    private static Element BuildHost(
        Func<int> getPhase,
        Action advance,
        Func<int, TabViewElement, TabViewElement> configure)
    {
        var tab = configure(
            getPhase(),
            TabView(new TabViewItemData("Tab1", Border(TextBlock("body")).AutomationId("tab-body"))));

        return Grid([GridSize.Star()], [GridSize.Auto, GridSize.Star()],
                Button("Advance", advance).Grid(row: 0, column: 0),
                tab.Grid(row: 1, column: 0))
            .Width(600).Height(HostHeight);
    }

    private static Border? Body(Harness h) =>
        h.FindControl<Border>(b => AutomationProperties.GetAutomationId(b) == "tab-body");

    /// <summary>
    /// Off → on → off. Asserts the opt-out keeps WinUI's <c>Top</c> AND a content-sized
    /// body, that opting in both flips the alignment and makes the body actually fill the
    /// remaining height, and that opting back out releases the local value so the style
    /// default applies again.
    /// </summary>
    internal class ToggleFillContentArea(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return BuildHost(
                    () => phase,
                    () => set(phase + 1),
                    static (p, tab) => p == 1 ? tab.FillContentArea() : tab);
            });

            await Harness.Render();
            var tabView = H.FindControl<TabView>(_ => true);
            H.Check("TabViewFill_Mounted", tabView is not null && Body(H) is not null);
            if (tabView is null) throw new InvalidOperationException("TabView was not mounted.");

            // Phase 0 — opt-out: WinUI's style default is untouched and the body is
            // content-sized (this is the pre-fix behaviour, deliberately preserved).
            double bodyOff = Body(H)!.ActualHeight;
            H.Check("TabViewFill_OffKeepsWinUIDefault",
                tabView.VerticalAlignment == VerticalAlignment.Top);
            H.Check("TabViewFill_OffBodyIsContentSized",
                bodyOff > 0 && bodyOff < FilledBodyFloor);

            // Phase 1 — opt-in: alignment flips AND the body genuinely fills the slot. The
            // height assertions are the real oracle: deleting the descriptor entry leaves
            // bodyOn == bodyOff.
            H.ClickButton("Advance");
            await Harness.Render();
            double bodyOn = Body(H)!.ActualHeight;
            H.Check("TabViewFill_OnStretchesControl",
                tabView.VerticalAlignment == VerticalAlignment.Stretch);
            H.Check("TabViewFill_OnBodyFillsContentArea", bodyOn > FilledBodyFloor);
            H.Check("TabViewFill_OnBodyGrewOverOff", bodyOn > bodyOff * 3);
            // The TabView now fills its ~350 px slot, and the tab body reaches its bottom
            // minus only the tab strip.
            H.Check("TabViewFill_OnBodyReachesTabViewBottom",
                tabView.ActualHeight > FilledBodyFloor * 2
                && tabView.ActualHeight - bodyOn < 60);

            // Phase 2 — back off: the local Stretch is released, so WinUI's style default
            // takes over again and the body re-collapses.
            H.ClickButton("Advance");
            await Harness.Render();
            double bodyOffAgain = Body(H)!.ActualHeight;
            H.Check("TabViewFill_ToggledOffRestoresWinUIDefault",
                tabView.VerticalAlignment == VerticalAlignment.Top);
            H.Check("TabViewFill_ToggledOffBodyCollapsesAgain",
                bodyOffAgain < FilledBodyFloor && Math.Abs(bodyOffAgain - bodyOff) < 2);
        }
    }

    /// <summary>
    /// An explicit <c>.VAlign(…)</c> wins over the opt-in, on mount AND on every subsequent
    /// re-render. <c>Center</c> is deliberately chosen because it is neither WinUI's style
    /// default (<c>Top</c>) nor the value the opt-in writes (<c>Stretch</c>), so each check
    /// fails if either the modifier or the opt-in misbehaves.
    ///
    /// <para>The re-render phases are the real oracle: <c>ApplyModifiers</c> only re-writes
    /// the alignment when the modifier <em>changed</em>, so an opt-in that wrote
    /// unconditionally would clobber an unchanged explicit alignment on the second render,
    /// and the on→off release would clobber it on the third.</para>
    /// </summary>
    internal class ExplicitAlignmentWins(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return BuildHost(
                    () => phase,
                    () => set(phase + 1),
                    // Phase 0/1: opt-in ON with an explicit alignment, unchanged across the
                    // re-render. Phase 2: opt-in flipped OFF, alignment still unchanged.
                    static (p, tab) => p <= 1
                        ? tab.FillContentArea().VAlign(VerticalAlignment.Center)
                        : tab.VAlign(VerticalAlignment.Center));
            });

            await Harness.Render();
            var tabView = H.FindControl<TabView>(_ => true);
            if (tabView is null) throw new InvalidOperationException("TabView was not mounted.");

            double bodyPinned = Body(H)!.ActualHeight;
            H.Check("TabViewFill_ExplicitCenterBeatsOptIn",
                tabView.VerticalAlignment == VerticalAlignment.Center);
            H.Check("TabViewFill_ExplicitBodyStaysContentSized",
                bodyPinned > 0 && bodyPinned < FilledBodyFloor);

            // Phase 1 — re-render with the SAME explicit alignment. ApplyModifiers skips
            // unchanged values, so nothing but the opt-in could move this.
            H.ClickButton("Advance");
            await Harness.Render();
            H.Check("TabViewFill_ExplicitSurvivesRerender",
                tabView.VerticalAlignment == VerticalAlignment.Center);
            H.Check("TabViewFill_ExplicitBodyStillContentSizedAfterRerender",
                Body(H)!.ActualHeight < FilledBodyFloor);

            // Phase 2 — opt-in turned off while the explicit alignment stays put: the
            // release must not steal the author's value either.
            H.ClickButton("Advance");
            await Harness.Render();
            H.Check("TabViewFill_ExplicitSurvivesOptOut",
                tabView.VerticalAlignment == VerticalAlignment.Center);
        }
    }
}
