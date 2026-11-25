using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using ToolkitCompatibility;
using Wct = CommunityToolkit.WinUI.Controls;

namespace ToolkitSegmentedUiaProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(["--help"]))
        {
            Console.WriteLine("After runtime approval: ToolkitSegmentedUiaProbe.exe --run <new-log-path>");
            Console.WriteLine("Opens one unpackaged WinUI window, assigns selection 0 then 1, closes automatically. No input injection.");
            Console.WriteLine("Use --inspect <new-log-path> to retain the controls for independent UIA inspection.");
            return 0;
        }
        if (args.Length != 2 || args[0] is not ("--run" or "--inspect"))
        {
            Console.Error.WriteLine("Expected --run or --inspect <new-log-path>. A runtime slot is required; --help is headless.");
            return 1;
        }
        using var output = new StreamWriter(new FileStream(Path.GetFullPath(args[1]), FileMode.CreateNew))
        {
            AutoFlush = true
        };
        int exitCode = 1;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new ProbeApp(output.WriteLine, code => exitCode = code, args[0] == "--inspect");
        });
        return exitCode;
    }
}

internal sealed partial class ProbeApp(Action<string> note, Action<int> finish, bool inspect) : Application, IXamlMetadataProvider
{
    private static readonly string[] Variants =
        ["native-listview-positive-control", "native-segmented-strings", "native-segmented-items"];
    private readonly IXamlMetadataProvider _toolkit = new Wct.SegmentedRns.
        CommunityToolkit_WinUI_Controls_Segmented_XamlTypeInfo.XamlMetaDataProvider();
    private readonly IXamlMetadataProvider _winui = new Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider();
    private Window? _window;

    public IXamlType GetXamlType(Type type) => _toolkit.GetXamlType(type) ?? _winui.GetXamlType(type);
    public IXamlType GetXamlType(string name) => _toolkit.GetXamlType(name) ?? _winui.GetXamlType(name);
    public XmlnsDefinition[] GetXmlnsDefinitions() =>
        [.. _toolkit.GetXmlnsDefinitions(), .. _winui.GetXmlnsDefinitions()];

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        var host = new Border { Padding = new Thickness(24) };
        _window = new Window { Title = "Native Toolkit Segmented UIA diagnostic", Content = host };
        _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 700));
        _window.Activate();
        _ = RunAsync(host);
    }

    private async Task RunAsync(Border host)
    {
        bool keepOpen = false;
        try
        {
            note("Diagnostic only. Native WinUI application, no Reactor reference, no synthetic peers.");
            note($"process={Environment.ProcessId}; dynamic-code={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
            foreach (string variant in Variants)
            {
                var control = CreateControl(variant, "subject");
                host.Child = control;
                await WaitAsync(() => control.XamlRoot is not null && control.ActualWidth > 0 && control.ActualHeight > 0,
                    $"{variant} attached and arranged");
                bool observed = await SegmentedUiaDiagnostics.CaptureAsync(control, variant, note, WaitAsync);
                if (variant == "native-listview-positive-control" && !observed)
                    throw new InvalidOperationException("ListView positive control did not expose both selection identities. Do not interpret Segmented absence.");
                host.Child = null;
            }
            note("COMPLETE native comparison capture. Exit 0 means capture completed, not Segmented accessibility passed.");
            if (inspect)
            {
                ShowInspection(host);
                keepOpen = true;
                note("INSPECTION READY. Native controls retained at index 0. Use the selection buttons, then close this window.");
            }
            finish(0);
        }
        catch (Exception exception)
        {
            note($"FAILED {exception}");
            finish(1);
        }
        finally
        {
            if (!keepOpen) _window?.Close();
        }
    }

    private static ListViewBase CreateControl(string variant, string automationId)
    {
        ListViewBase control = variant == "native-listview-positive-control" ? new ListView() : new Wct.Segmented();
        control.Width = 400;
        control.SelectionMode = ListViewSelectionMode.Single;
        foreach (string label in new[] { "List", "Grid", "Details" })
            control.Items.Add(variant == "native-segmented-items" ? new Wct.SegmentedItem { Content = label } : label);
        control.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(control, automationId);
        AutomationProperties.SetName(control, "Compatibility label B");
        return control;
    }

    private void ShowInspection(Border host)
    {
        var panel = new StackPanel { Spacing = 12 };
        var controls = new List<ListViewBase>();
        foreach (string variant in Variants)
        {
            panel.Children.Add(new TextBlock { Text = variant });
            var control = CreateControl(variant, variant);
            controls.Add(control);
            panel.Children.Add(control);
        }
        var status = new TextBlock { Text = "SelectedIndex=0 / List" };
        foreach (int index in new[] { 0, 1 })
        {
            var button = new Button { Content = index == 0 ? "Select List in all controls" : "Select Grid in all controls" };
            button.Click += (_, _) =>
            {
                foreach (var control in controls)
                {
                    control.SelectedIndex = index;
                    note($"INSPECTION id={AutomationProperties.GetAutomationId(control)}; SelectedIndex={control.SelectedIndex}");
                }
                status.Text = $"SelectedIndex={index} / {(index == 0 ? "List" : "Grid")}";
            };
            panel.Children.Add(button);
        }
        panel.Children.Add(status);
        host.Child = panel;
    }

    private static async Task WaitAsync(Func<bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(description);
            await Task.Delay(25);
        }
    }
}
