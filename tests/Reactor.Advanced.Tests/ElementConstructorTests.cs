using System.Reflection;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

public sealed class ElementConstructorTests
{
    public static IEnumerable<object[]> ElementTypes()
    {
        yield return [typeof(Win2DCanvasElement)];
        yield return [typeof(Win2DAnimatedCanvasElement)];
        yield return [typeof(Win2DVirtualCanvasElement)];
    }

    [Theory]
    [MemberData(nameof(ElementTypes))]
    public void Win2DElementRecords_HaveNoPublicInstanceConstructors(Type elementType)
    {
        var publicCtors = elementType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicCtors);
    }

    [Theory]
    [MemberData(nameof(ElementTypes))]
    public void Win2DElementRecords_HaveInternalParameterlessConstructor(Type elementType)
    {
        var ctor = elementType.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        Assert.NotNull(ctor);
        Assert.True(ctor!.IsAssembly, $"{elementType.Name} constructor should be internal.");
    }
}
