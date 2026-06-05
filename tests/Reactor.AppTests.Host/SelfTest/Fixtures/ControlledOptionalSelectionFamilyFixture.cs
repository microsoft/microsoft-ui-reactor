using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ControlledOptionalSelectionFamilyFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Run(ComboBoxScenario());
            await Run(ListBoxScenario());
            await Run(ListViewScenario());
            await Run(GridViewScenario());
            await Run(FlipViewScenario());
            await Run(TemplatedFlipViewScenario());
            await Run(RadioButtonsScenario());
            await Run(PivotScenario());
            await Run(TabViewScenario());
            await Run(SelectorBarScenario());
            await Run(PipsPagerScenario());
        }

        private async Task Run<TControl>(ControlledOptionalSelfTestHelpers.Scenario<TControl, int> scenario)
            where TControl : DependencyObject
        {
            const string fixture = "ControlledOptionalSelectionFamily";
            await ControlledOptionalSelfTestHelpers.RunUnsetSurvivesSiblingRerenderAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunBoundUpdatesControlAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunSnapBackAsync(H, fixture, scenario);

            // Spec 050 F1 regression: Optional.Of(-1) is the explicit
            // force-clear sentinel for selection-index controls. Only
            // ListView, GridView, and SelectorBar accept it as a real
            // "deselect" today; the rest have control-specific guards or
            // do not surface a no-selection state through SelectedIndex.
            if (scenario.Name is "ListView" or "GridView" or "SelectorBar")
                await ControlledOptionalSelfTestHelpers.RunForceClearSentinelAsync(H, fixture, scenario);
        }
    }

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.ComboBox, int> ComboBoxScenario() =>
        new(
            "ComboBox",
            (value, changed) => ComboBox(StringItems, value, changed),
            h => h.FindControl<WinUI.ComboBox>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.ListBox, int> ListBoxScenario() =>
        new(
            "ListBox",
            (value, changed) => ListBox(StringItems, value, changed),
            h => h.FindControl<WinUI.ListBox>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.ListView, int> ListViewScenario() =>
        new(
            "ListView",
            (value, changed) => ListView(value, changed, ElementItems),
            h => h.FindControl<WinUI.ListView>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.GridView, int> GridViewScenario() =>
        new(
            "GridView",
            (value, changed) => GridView(value, changed, ElementItems),
            h => h.FindControl<WinUI.GridView>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.FlipView, int> FlipViewScenario() =>
        new(
            "FlipView",
            (value, changed) => FlipView(value, changed, ElementItems),
            h => h.FindControl<WinUI.FlipView>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.FlipView, int> TemplatedFlipViewScenario() =>
        new(
            "TemplatedFlipView",
            (value, changed) => FlipView(TemplatedItems, static s => s, static (s, _) => TextBlock(s)) with
            {
                SelectedIndex = value,
                OnSelectedIndexChanged = changed,
            },
            h => h.FindControl<WinUI.FlipView>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            0,
            2,
            1);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.RadioButtons, int> RadioButtonsScenario() =>
        new(
            "RadioButtons",
            (value, changed) => RadioButtons(StringItems, value, changed),
            h => h.FindControl<WinUI.RadioButtons>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.Pivot, int> PivotScenario() =>
        new(
            "Pivot",
            (value, changed) => Pivot(value, changed, PivotItems),
            h => h.FindControl<WinUI.Pivot>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.TabView, int> TabViewScenario() =>
        new(
            "TabView",
            (value, changed) => TabView(value, changed, TabItems),
            h => h.FindControl<WinUI.TabView>(_ => true),
            c => c.SelectedIndex,
            (c, v) => c.SelectedIndex = v,
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.SelectorBar, int> SelectorBarScenario() =>
        new(
            "SelectorBar",
            (value, changed) => SelectorBar(SelectorBarItems, value, changed),
            h => h.FindControl<WinUI.SelectorBar>(_ => true),
            c => c.Items.IndexOf(c.SelectedItem),
            (c, v) => c.SelectedItem = c.Items[v],
            1,
            2,
            0);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.PipsPager, int> PipsPagerScenario() =>
        new(
            "PipsPager",
            (value, changed) => PipsPager(3, value, changed),
            h => h.FindControl<WinUI.PipsPager>(_ => true),
            c => c.SelectedPageIndex,
            (c, v) => c.SelectedPageIndex = v,
            1,
            2,
            0);

    private static readonly string[] StringItems = ["zero", "one", "two"];
    private static readonly string[] TemplatedItems = ["zero", "one", "two"];
    private static readonly Element[] ElementItems = [TextBlock("zero"), TextBlock("one"), TextBlock("two")];
    private static readonly PivotItemData[] PivotItems =
    [
        PivotItem("zero", TextBlock("zero")),
        PivotItem("one", TextBlock("one")),
        PivotItem("two", TextBlock("two")),
    ];
    private static readonly TabViewItemData[] TabItems =
    [
        Tab("zero", TextBlock("zero")),
        Tab("one", TextBlock("one")),
        Tab("two", TextBlock("two")),
    ];
    private static readonly SelectorBarItemData[] SelectorBarItems =
    [
        SelectorBarItem("zero"),
        SelectorBarItem("one"),
        SelectorBarItem("two"),
    ];
}
