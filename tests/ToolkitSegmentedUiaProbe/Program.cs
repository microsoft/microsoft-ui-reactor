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
            return 0;
        }
        if (args.Length != 2 || args[0] != "--run")
        {
            Console.Error.WriteLine("Expected --run <new-log-path>. A runtime slot is required; --help is headless.");
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
            new ProbeApp(output.WriteLine, code => exitCode = code);
        });
        return exitCode;
    }
}

internal sealed partial class ProbeApp(Action<string> note, Action<int> finish) : Application, IXamlMetadataProvider
{
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
        try
        {
            note("Diagnostic only. Native WinUI application, no Reactor reference, no synthetic peers.");
            note($"process={Environment.ProcessId}; dynamic-code={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
            foreach (string variant in new[] { "native-listview-positive-control", "native-segmented-strings", "native-segmented-items" })
            {
                ListViewBase control = variant == "native-listview-positive-control" ? new ListView() : new Wct.Segmented();
                control.Width = 400;
                control.SelectionMode = ListViewSelectionMode.Single;
                foreach (string label in new[] { "List", "Grid", "Details" })
                    control.Items.Add(variant == "native-segmented-items" ? new Wct.SegmentedItem { Content = label } : label);
                control.SelectedIndex = 0;
                AutomationProperties.SetAutomationId(control, "subject");
                AutomationProperties.SetName(control, "Compatibility label B");
                host.Child = control;
                await WaitAsync(() => control.XamlRoot is not null && control.ActualWidth > 0 && control.ActualHeight > 0,
                    $"{variant} attached and arranged");
                bool observed = await SegmentedUiaDiagnostics.CaptureAsync(control, variant, note, WaitAsync);
                if (variant == "native-listview-positive-control" && !observed)
                    throw new InvalidOperationException("ListView positive control did not expose both selection identities. Do not interpret Segmented absence.");
                host.Child = null;
            }
            note("COMPLETE native comparison capture. Exit 0 means capture completed, not Segmented accessibility passed.");
            finish(0);
        }
        catch (Exception exception)
        {
            note($"FAILED {exception}");
            finish(1);
        }
        finally
        {
            _window?.Close();
        }
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
