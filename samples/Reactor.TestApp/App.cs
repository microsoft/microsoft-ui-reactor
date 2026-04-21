// Reactor Test App — A WinUI 3 application using the Microsoft.UI.Reactor functional projection.
// No XAML. No data binding. No resources. No templates. Just C#.

using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<DemoApp>("Reactor Demo", width: 1200, height: 800
#if DEBUG
    , devtools: true
#endif
);

// ─── Root application component ────────────────────────────────────────────────

enum Tab { Counter, TodoList, ConditionalUI, Form, DynamicList, PerfStress, Virtualization, Flyout, DataTemplate, FlexPanel, Transitions, PropertyGrid, DataSystem, DataGrid, IntegratedData, AsyncValueSamples, Context, Memo, Persisted, Slots, Navigation, Commanding, InputGestures }

class DemoApp : Component
{
    static readonly string[] Languages = ["English"];

    static string IconPath(string name) =>
        global::System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", $"{name}.svg");

    static readonly (string Label, string Icon)[] TabItems = Enum.GetValues<Tab>()
        .Select(tab => tab switch
        {
            Tab.Counter => ("Counter", "counter"),
            Tab.TodoList => ("Todo List", "todolist"),
            Tab.ConditionalUI => ("Conditional UI", "conditionalui"),
            Tab.Form => ("Form", "form"),
            Tab.DynamicList => ("Dynamic List", "dynamiclist"),
            Tab.PerfStress => ("Perf Stress", "perfstress"),
            Tab.Virtualization => ("Virtualization", "virtualization"),
            Tab.Flyout => ("Flyout", "flyout"),
            Tab.DataTemplate => ("DataTemplate", "datatemplate"),
            Tab.FlexPanel => ("FlexPanel", "flexpanel"),
            Tab.Transitions => ("Transitions", "transitions"),
            Tab.PropertyGrid => ("PropertyGrid", "propertygrid"),
            Tab.DataSystem => ("Data System", "datasystem"),
            Tab.DataGrid => ("DataGrid", "datagrid"),
            Tab.IntegratedData => ("Integrated Data", "integrateddata"),
            Tab.AsyncValueSamples => ("AsyncValue", "datasystem"),
            Tab.Context => ("Context", "context"),
            Tab.Memo => ("Memo", "memo"),
            Tab.Persisted => ("Persisted", "persisted"),
            Tab.Slots => ("Slots", "slots"),
            Tab.Navigation => ("Navigation", "navigation"),
            Tab.Commanding => ("Commanding", "commanding"),
            Tab.InputGestures => ("Input & Gestures", "counter"),
            _ => (tab.ToString(), "counter")
        }).ToArray();

    static readonly Element[] TabElements = TabItems
        .Select(t => HStack(8,
            Image(IconPath(t.Icon)).Width(20).Height(20),
            TextBlock(t.Label).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center)
        ) as Element).ToArray();

    public override Element Render()
    {
        var (currentTab, setTab) = UseState(Tab.Counter);
        var (langIndex, setLangIndex) = UseState(0);

        return FlexColumn(
            (TitleBar("TestApp") with
            {
                Content = HStack(8,
                    ComboBox(TabElements, (int)currentTab, i => setTab((Tab)i)).Width(240),
                    ComboBox(Languages, langIndex, setLangIndex)
                ),
            }).Flex(shrink: 0),

            // Content area — Flex(grow:1) fills remaining vertical space
            // so ScrollView inside each demo gets a bounded height and can scroll.
            Border(
                currentTab switch
                {
                    Tab.Counter => Component<CounterDemo>(),
                    Tab.TodoList => Component<TodoDemo>(),
                    Tab.ConditionalUI => Component<ConditionalDemo>(),
                    Tab.Form => Component<FormDemo>(),
                    Tab.DynamicList => Component<DynamicListDemo>(),
                    Tab.PerfStress => Component<PerfStressDemo>(),
                    Tab.Virtualization => Component<VirtualizationDemo>(),
                    Tab.Flyout => Component<FlyoutDemo>(),
                    Tab.DataTemplate => Component<DataTemplateDemo>(),
                    Tab.FlexPanel => Component<FlexPanelDemo>(),
                    Tab.Transitions => Component<TransitionsDemo>(),
                    Tab.PropertyGrid => Component<PropertyGridDemo>(),
                    Tab.DataSystem => Component<DataSystemDemo>(),
                    Tab.DataGrid => Component<DataGridDemo>(),
                    Tab.IntegratedData => Component<IntegratedDataDemo>(),
                    Tab.AsyncValueSamples => Component<AsyncValueSamplesDemo>(),
                    Tab.Context => Component<ContextDemo>(),
                    Tab.Memo => Component<MemoDemo>(),
                    Tab.Persisted => Component<PersistedDemo>(),
                    Tab.Slots => Component<SlotsDemo>(),
                    Tab.Navigation => Component<NavigationDemo>(),
                    Tab.Commanding => Component<CommandingTestDemo>(),
                    Tab.InputGestures => Component<InputGesturesDemo>(),
                    _ => TextBlock("Select a tab")
                }
            ).Padding(24).Margin(16).Flex(grow: 1)
        );
    }
}
