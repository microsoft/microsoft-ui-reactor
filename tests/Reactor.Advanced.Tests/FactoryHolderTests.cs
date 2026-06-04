using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Windows.Foundation;
using Xunit;
using AdvancedFactories = Microsoft.UI.Reactor.Advanced.Factories;

namespace Microsoft.UI.Reactor.Advanced.Tests;

public sealed class FactoryHolderTests
{
    [Theory]
    [InlineData(typeof(Win2DCanvasElement), nameof(CreateCanvas))]
    [InlineData(typeof(Win2DAnimatedCanvasElement), nameof(CreateAnimatedCanvas))]
    [InlineData(typeof(Win2DVirtualCanvasElement), nameof(CreateVirtualCanvas))]
    public void FactoryCall_RegistersElementType(Type elementType, string createMethod)
    {
        typeof(FactoryHolderTests).GetMethod(createMethod, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);

        Assert.True(ControlRegistryContains(elementType), $"{elementType.Name} should be registered after factory use.");
    }

    [Fact]
    public void TouchingAnyFactory_RegistersAllWin2DHandlers()
    {
        // Per-library trim unit: a single static cctor on Advanced.Factories registers
        // every Win2D handler, so touching any one factory roots all three.
        _ = AdvancedFactories.Win2DCanvas(static (CanvasDrawingSession _, CanvasDrawEventArgs _) => { });

        Assert.True(ControlRegistryContains(typeof(Win2DCanvasElement)));
        Assert.True(ControlRegistryContains(typeof(Win2DAnimatedCanvasElement)));
        Assert.True(ControlRegistryContains(typeof(Win2DVirtualCanvasElement)));
    }

    private static bool ControlRegistryContains(Type elementType)
    {
        var contains = typeof(ControlRegistry).GetMethod("Contains", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)contains.Invoke(null, [elementType])!;
    }

    private static void CreateCanvas() =>
        _ = AdvancedFactories.Win2DCanvas(static (CanvasDrawingSession _, CanvasDrawEventArgs _) => { });

    private static void CreateAnimatedCanvas() =>
        _ = AdvancedFactories.Win2DAnimatedCanvas(
            static (CanvasAnimatedUpdateEventArgs _, object? _) => { },
            static (CanvasDrawingSession _, CanvasAnimatedDrawEventArgs _, object? _) => { });

    private static void CreateVirtualCanvas() =>
        _ = AdvancedFactories.Win2DVirtualCanvas(
            static (CanvasDrawingSession _, Rect _) => { },
            new Size(100, 100));
}
