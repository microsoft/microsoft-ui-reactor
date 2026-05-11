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

// ─── Global dev flags ──────────────────────────────────────────────────────────
// Declared as static Observable<T> cells so any component can read/write without
// prop-drilling. Toggled from the Dev menu in the titlebar when the app is
// launched with `--devtools app` (and built with devtools: true).
static class AppFlags
{
    public static readonly Observable<bool> DebugUI = new(false);
    public static readonly Observable<bool> OutlineLayout = new(false);
}

// ─── Root application component ────────────────────────────────────────────────

enum Tab { Counter, TodoList, ConditionalUI, Form, DynamicList, PerfStress, Virtualization, Flyout, DataTemplate, FlexPanel, Transitions, PropertyGrid, DataSystem, DataGrid, IntegratedData, AsyncValueSamples, Context, Memo, Persisted, Slots, Navigation, Commanding, InputGestures, SpecializedEditors, LayoutCost, Windows }

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
            Tab.SpecializedEditors => ("Specialized Editors", "propertygrid"),
            Tab.LayoutCost => ("Layout Cost", "perfstress"),
            Tab.Windows => ("Windows & Tray", "navigation"),
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

        // Subscribe so toggling a flag from the Dev menu re-renders the root
        // (which rebuilds the menu's checkmarks and the conditional overlays).
        var debugUI = UseObservable(AppFlags.DebugUI).Value;
        var outline = UseObservable(AppFlags.OutlineLayout).Value;

        // The shared WinUI logo .ico is bundled at samples/Assets/WinUI.ico
        // and is also wired into the EXE PE resources via the
        // samples/Directory.Build.props default <ApplicationIcon>. Surfacing
        // it through TitleBar.Icon shows the same glyph inside the custom
        // title bar rather than only on the taskbar / Alt-Tab entry.
        return FlexColumn(
            (TitleBar("TestApp") with
            {
                Icon = new ImageIconData(new Uri(global::System.IO.Path.Combine(
                    global::System.AppContext.BaseDirectory, "Assets", "AppIcon.ico"))),
                Content = HStack(8,
                    ComboBox(TabElements, (int)currentTab, i => setTab((Tab)i)).Width(240),
                    ComboBox(Languages, langIndex, setLangIndex),
                    DevtoolsMenu(() => new MenuFlyoutItemBase[]
                    {
                        ToggleMenuItem("Debug UI",
                            debugUI,
                            v => AppFlags.DebugUI.Value = v),
                        ToggleMenuItem("Outline layout",
                            outline,
                            v => AppFlags.OutlineLayout.Value = v),
                        MenuSeparator(),
                        MenuItem("Log tab change",
                            () => Debug.WriteLine($"[devtools] current tab = {currentTab}")),
                    })
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
                    Tab.SpecializedEditors => Component<SpecializedEditorsDemo>(),
                    Tab.LayoutCost => Component<LayoutCostDemo>(),
                    Tab.Windows => Component<WindowsDemo>(),
                    _ => TextBlock("Select a tab")
                }
            )
            .Padding(24).Margin(16)
            .WithBorder(outline ? "#FF00AA" : "#00000000", outline ? 2 : 0)
            .Flex(grow: 1),

            // Debug strip — only constructed when the Debug UI flag is on.
            // The flag can only be toggled from the DevtoolsMenu above, which
            // itself only renders in devtools sessions, so in practice this
            // strip is dev-only. Retail cost is one bool check.
            debugUI ? DebugStrip(currentTab) : null
        )
        // Spec 033 §6 — Mica window backdrop on the demo shell.
        .Backdrop(BackdropKind.Mica);
    }

    static Element DebugStrip(Tab currentTab) =>
        Border(
            HStack(12,
                TextBlock("debug").Foreground("#FFAA00"),
                TextBlock($"tab: {currentTab}"),
                TextBlock($"@ {DateTime.Now:HH:mm:ss.fff}")
            ).Padding(horizontal: 8, vertical: 4)
        )
        .Background("#2B000000")
        .Flex(shrink: 0);
}
