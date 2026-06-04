using Microsoft.UI.Reactor.Advanced.Win2D;
using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

public sealed class Win2DAnimatedCanvasHandlerThreadingTests
{
    [Fact]
    public void Update_And_Draw_GameThread_Subscriptions_Do_Not_Use_CustomEvent_Trampoline()
    {
        _ = typeof(Win2DAnimatedCanvasHandler);
        var source = File.ReadAllText(FindHandlerSourcePath());

        Assert.DoesNotContain("OnCustomEvent<CanvasAnimatedUpdateEventArgs>", source);
        Assert.DoesNotContain("OnCustomEvent<CanvasAnimatedDrawEventArgs>", source);
        Assert.Contains("TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedUpdateEventArgs>", source);
        Assert.Contains("TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedDrawEventArgs>", source);
        Assert.Contains("Volatile.Read(ref _element)", source);
        Assert.Contains("Volatile.Write(ref _element, value)", source);
        Assert.Contains("ctrl.Update += updateHandler", source);
        Assert.Contains("ctrl.Draw += drawHandler", source);
        Assert.Contains("ctrl.Update -= updateHandler", source);
        Assert.Contains("ctrl.Draw -= drawHandler", source);
    }

    private static string FindHandlerSourcePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src",
                "Reactor.Advanced",
                "Win2D",
                "Win2DAnimatedCanvasHandler.cs");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate Win2DAnimatedCanvasHandler.cs from the test output directory.");
    }
}
