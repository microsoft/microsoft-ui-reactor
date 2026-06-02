using System.Reflection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Windows.Foundation;
using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

public sealed class PatternAStaticLambdaTests
{
    [Theory]
    [InlineData(typeof(Win2DCanvasElement), nameof(CreateCanvas))]
    [InlineData(typeof(Win2DAnimatedCanvasElement), nameof(CreateAnimatedCanvas))]
    [InlineData(typeof(Win2DVirtualCanvasElement), nameof(CreateVirtualCanvas))]
    public void RegisteredHandlerFactory_IsStaticLambda(Type elementType, string createMethod)
    {
        typeof(PatternAStaticLambdaTests).GetMethod(createMethod, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);

        var adapterFactory = GetAdapterFactory(elementType);
        var leaf = GetLeafHandlerFactory(adapterFactory);

        // Pattern A discipline: no captured per-call state anywhere in the chain.
        // Roslyn emits `static () => new XxxHandler()` as an instance method on
        // a synthesized `<>c` closure singleton — `IsStatic` is false but the
        // target type has no instance fields (i.e. zero captures). The chain-walk
        // already drills through any captured-state closures; if the leaf's target
        // is a fields-free singleton, that satisfies "no closure allocation".
        if (leaf.Target is { } target)
        {
            var hasInstanceFields = target.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Any();
            Assert.False(hasInstanceFields,
                $"Pattern A: lambda Target must be a fields-free singleton (no captures); got {target.GetType().FullName} with instance fields.");
        }
        // Otherwise Target is null — directly static, also closure-free. Either is fine.
    }

    private static Delegate GetAdapterFactory(Type elementType)
    {
        var entriesField = typeof(ControlRegistry).GetField("s_entries", BindingFlags.NonPublic | BindingFlags.Static)!;
        var entries = entriesField.GetValue(null)!;
        var tryGetValue = entries.GetType().GetMethod("TryGetValue")!;
        var args = new object?[] { elementType, null };

        Assert.True((bool)tryGetValue.Invoke(entries, args)!);
        return (Delegate)args[1]!;
    }

    private static Delegate GetLeafHandlerFactory(Delegate del)
    {
        // The registry may store the user's factory directly (no adapter closure,
        // ideal) or wrap it in a small adapter closure. Walk through any closure
        // targets until we find a delegate field, or until there are no more.
        var current = del;
        var guard = 0;
        while (current.Target is { } target && guard++ < 8)
        {
            var delegateField = target.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => typeof(Delegate).IsAssignableFrom(f.FieldType));
            if (delegateField is null)
                break;
            if (delegateField.GetValue(target) is not Delegate inner)
                break;
            current = inner;
        }
        return current;
    }

    private static void CreateCanvas() =>
        _ = Microsoft.UI.Reactor.Advanced.Factories.Win2DCanvas(static (CanvasDrawingSession _, CanvasDrawEventArgs _) => { });

    private static void CreateAnimatedCanvas() =>
        _ = Microsoft.UI.Reactor.Advanced.Factories.Win2DAnimatedCanvas(
            static (CanvasAnimatedUpdateEventArgs _, object? _) => { },
            static (CanvasDrawingSession _, CanvasAnimatedDrawEventArgs _, object? _) => { });

    private static void CreateVirtualCanvas() =>
        _ = Microsoft.UI.Reactor.Advanced.Factories.Win2DVirtualCanvas(
            static (CanvasDrawingSession _, Rect _) => { },
            new Size(100, 100));
}
