using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

// Root test namespace on purpose: a `Microsoft.UI.Reactor.Tests.Layout` namespace would shadow
// `Layout.Flex*` references in other test files.
namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Headless checks on the <c>Factories.FitParent</c> factory (issue #174). The factory only
/// builds a <see cref="FuncElement"/>; nothing here renders it, so no XAML object is created.
/// </summary>
public class FitParentFactoryTests
{
    [Fact]
    public void FitParent_NullRender_Throws()
    {
        var ex = Assert.Throws<global::System.ArgumentNullException>(() => FitParent(null!));
        Assert.Equal("render", ex.ParamName);
    }

    [Fact]
    public void FitParent_NullRender_WithOptions_Throws()
    {
        var ex = Assert.Throws<global::System.ArgumentNullException>(
            () => FitParent(null!, new FitParentOptions { Debounce = global::System.TimeSpan.Zero }));
        Assert.Equal("render", ex.ParamName);
    }

    [Fact]
    public void FitParent_ReturnsFuncElement_WithoutInvokingRender()
    {
        var calls = 0;
        var el = FitParent((w, h) => { calls++; return Empty(); });
        Assert.IsType<FuncElement>(el);
        Assert.Equal(0, calls);
    }
}
