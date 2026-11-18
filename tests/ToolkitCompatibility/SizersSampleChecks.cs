using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class SizersSampleChecks
{
    internal static async Task ContentAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("csz"));
        var sizer = Find<Wct.ContentSizer>();
        var target = sizer.TargetControl as Native.Border
            ?? throw new InvalidOperationException("ContentSizer must target the advertised Border, not its parent.");
        var parent = sizer.Parent as FrameworkElement
            ?? throw new InvalidOperationException("ContentSizer has no parent.");
        Program.Check(target.IsLoaded && target.Height == 140 && double.IsNaN(parent.Height),
            $"Expected loaded target height 140 and automatic parent; got {target.Height}/{parent.Height}.");
        await root.ShowAsync(TextBlock("Sizer removed"));
        Program.Check(!sizer.IsLoaded && !target.IsLoaded && sizer.TargetControl is null,
            "Removed ContentSizer retained its target or remained loaded.");
        await root.ShowAsync(WctControls.SamplePages.CreatePage("csz"));
        var remounted = Find<Wct.ContentSizer>();
        Program.Check(!ReferenceEquals(remounted, sizer) && remounted.TargetControl is Native.Border fresh &&
            !ReferenceEquals(fresh, target) && fresh.Height == 140,
            "ContentSizer remount did not bind a fresh advertised target.");
        Program.Results.Note("ContentSizer target identity, parent Auto, teardown and remount checked; no drag simulated.");
    }

    internal static async Task PropertyAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("psz"));
        var sizer = Find<Wct.PropertySizer>();
        var target = CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .OfType<Native.Border>().Single(b => b.Child is Native.TextBlock text &&
                text.Text.StartsWith("Pane", StringComparison.Ordinal));
        Program.Check(sizer.Binding == 220 && target.Width == 220, "Initial bound target is not 220.");
        sizer.Binding = 260;
        await CompatibilityRoot.WaitAsync(() => target.Width == 260, "native Binding callback updates actual target");
        sizer.Binding = 300;
        await CompatibilityRoot.WaitAsync(() => target.Width == 300, "current callback after rerender");
        Program.Check(ReferenceEquals(sizer, Find<Wct.PropertySizer>()), "Bound-value update replaced the sizer.");
        await root.ShowAsync(TextBlock("Property sizer removed"));
        Program.Check(!sizer.IsLoaded && !target.IsLoaded, "PropertySizer target did not detach.");
        await root.ShowAsync(WctControls.SamplePages.CreatePage("psz"));
        var remounted = Find<Wct.PropertySizer>();
        Program.Check(!ReferenceEquals(sizer, remounted) && remounted.Binding == 220,
            "PropertySizer remount did not reset to sample state.");
        Program.Results.Note("PropertySizer native DP notifications changed the target 220->260->300; physical drag not exercised.");
    }

    private static T Find<T>() where T : FrameworkElement =>
        CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content).OfType<T>().Single();
}
