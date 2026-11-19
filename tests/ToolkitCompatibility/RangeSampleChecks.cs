using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class RangeSampleChecks
{
    internal static async Task RunAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("range"));
        var range = Find<Wct.RangeSelector>("Toolkit.RangeSelector");
        Program.Check(range.RangeStart == 20 && range.RangeEnd == 80, "Initial sample range is not 20/80.");
        int nativeEvents = 0;
        void Changed(object? sender, Wct.RangeChangedEventArgs args) => nativeEvents++;
        range.ValueChanged += Changed;
        try
        {
            range.RangeStart = 31;
            range.RangeEnd = 69;
            Program.Check(range.RangeStart == 31 && range.RangeEnd == 69,
                "Native drift was not established before reassertion.");
            Program.Results.Note($"native drift=31/69; native ValueChanged events={nativeEvents}; assignment is not callback evidence");
            Program.Check(nativeEvents == 0, "This silent-setter scenario requires an actual silent native assignment.");
            Invoke("Range.Rerender");
            await CompatibilityRoot.WaitAsync(() => range.RangeStart == 20 && range.RangeEnd == 80,
                "Unchanged parent values reassert after Width-only update");
            Program.Check(ReferenceEquals(range, Find<Wct.RangeSelector>("Toolkit.RangeSelector")) &&
                range.Width == 381, "Rerender did not update the same native range.");
            Program.Check(Find<Native.TextBlock>("Range.Callbacks").Text == "Endpoint callbacks: start 0, end 0",
                "Parent reassertion emitted endpoint callbacks.");
            Invoke("Range.Reset");
            await CompatibilityRoot.WaitAsync(() => range.Width == 380, "Parent reset rendered");
            Program.Check(range.RangeStart == 20 && range.RangeEnd == 80 && nativeEvents == 0 &&
                Find<Native.TextBlock>("Range.Callbacks").Text == "Endpoint callbacks: start 0, end 0",
                "Parent reset changed endpoint values/counts unexpectedly.");
        }
        finally { range.ValueChanged -= Changed; }
        await root.ShowAsync(TextBlock("Range removed"));
        Program.Check(!range.IsLoaded, "Range did not detach.");
        await root.ShowAsync(WctControls.SamplePages.CreatePage("range"));
        var remounted = Find<Wct.RangeSelector>("Toolkit.RangeSelector");
        Program.Check(!ReferenceEquals(range, remounted) && remounted.RangeStart == 20 && remounted.RangeEnd == 80 &&
            Find<Native.TextBlock>("Range.Callbacks").Text == "Endpoint callbacks: start 0, end 0",
            "Fresh range page did not start with clean state.");
    }

    private static T Find<T>(string id) where T : FrameworkElement =>
        CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content).OfType<T>()
            .Single(c => AutomationProperties.GetAutomationId(c) == id);

    private static void Invoke(string id)
    {
        var button = Find<Native.Button>(id);
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        Program.Check(peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider,
            $"Sample action {id} has no native Invoke provider.");
        ((IInvokeProvider)peer!.GetPattern(PatternInterface.Invoke)).Invoke();
    }
}
