using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Reconciler.BuildCacheKey — a pure function that generates deterministic
/// cache keys for themed WinUI styles. Keys must be order-independent and stable.
/// </summary>
public class ReconcilerCacheKeyTests
{
    [Fact]
    public void BuildCacheKey_SingleBinding()
    {
        var bindings = new Dictionary<string, ThemeRef>
        {
            { "Background", new ThemeRef("AccentBrush") }
        };
        var key = Reconciler.BuildCacheKey("Button", bindings);
        Assert.Equal("Button|Background=AccentBrush", key);
    }

    [Fact]
    public void BuildCacheKey_MultipleBindings_Sorted()
    {
        var bindings = new Dictionary<string, ThemeRef>
        {
            { "Foreground", new ThemeRef("TextPrimary") },
            { "Background", new ThemeRef("AccentBrush") }
        };
        var key = Reconciler.BuildCacheKey("Button", bindings);
        // Properties sorted ordinally: Background before Foreground
        Assert.Equal("Button|Background=AccentBrush|Foreground=TextPrimary", key);
    }

    [Fact]
    public void BuildCacheKey_OrderIndependent()
    {
        var bindings1 = new Dictionary<string, ThemeRef>
        {
            { "A", new ThemeRef("Val1") },
            { "B", new ThemeRef("Val2") },
            { "C", new ThemeRef("Val3") }
        };
        var bindings2 = new Dictionary<string, ThemeRef>
        {
            { "C", new ThemeRef("Val3") },
            { "A", new ThemeRef("Val1") },
            { "B", new ThemeRef("Val2") }
        };
        Assert.Equal(
            Reconciler.BuildCacheKey("Grid", bindings1),
            Reconciler.BuildCacheKey("Grid", bindings2));
    }

    [Fact]
    public void BuildCacheKey_EmptyBindings()
    {
        var bindings = new Dictionary<string, ThemeRef>();
        var key = Reconciler.BuildCacheKey("TextBlock", bindings);
        Assert.Equal("TextBlock", key);
    }

    [Fact]
    public void BuildCacheKey_DifferentTargetType_DifferentKey()
    {
        var bindings = new Dictionary<string, ThemeRef>
        {
            { "Background", new ThemeRef("Accent") }
        };
        var key1 = Reconciler.BuildCacheKey("Button", bindings);
        var key2 = Reconciler.BuildCacheKey("TextBlock", bindings);
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_DifferentValues_DifferentKey()
    {
        var b1 = new Dictionary<string, ThemeRef> { { "Background", new ThemeRef("Accent") } };
        var b2 = new Dictionary<string, ThemeRef> { { "Background", new ThemeRef("Secondary") } };
        Assert.NotEqual(
            Reconciler.BuildCacheKey("Button", b1),
            Reconciler.BuildCacheKey("Button", b2));
    }

    [Fact]
    public void ClearStyleCache_DoesNotThrow()
    {
        // Just verifying the static method is callable
        Reconciler.ClearStyleCache();
    }
}
