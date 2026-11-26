using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the style caching infrastructure in Reconciler.
/// BuildCacheKey must produce deterministic keys regardless of dictionary enumeration order.
/// </summary>
public class StyleCacheTests
{
    [Fact]
    public void BuildCacheKey_Produces_Identical_Keys_Regardless_Of_Dictionary_Order()
    {
        // Arrange: two dictionaries with same entries in different insertion order
        var dict1 = new Dictionary<string, ThemeRef>
        {
            ["Background"] = new ThemeRef("AccentFillColorDefaultBrush"),
            ["Foreground"] = new ThemeRef("TextFillColorPrimaryBrush"),
            ["BorderBrush"] = new ThemeRef("CardStrokeColorDefaultBrush"),
        };

        var dict2 = new Dictionary<string, ThemeRef>
        {
            ["Foreground"] = new ThemeRef("TextFillColorPrimaryBrush"),
            ["BorderBrush"] = new ThemeRef("CardStrokeColorDefaultBrush"),
            ["Background"] = new ThemeRef("AccentFillColorDefaultBrush"),
        };

        // Act
        var key1 = BuildCacheKeyViaReflection("Button", dict1);
        var key2 = BuildCacheKeyViaReflection("Button", dict2);

        // Assert
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_Different_TargetTypes_Produce_Different_Keys()
    {
        var dict = new Dictionary<string, ThemeRef>
        {
            ["Background"] = new ThemeRef("AccentFillColorDefaultBrush"),
        };

        var key1 = BuildCacheKeyViaReflection("Button", dict);
        var key2 = BuildCacheKeyViaReflection("Border", dict);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_Different_ResourceKeys_Produce_Different_Keys()
    {
        var dict1 = new Dictionary<string, ThemeRef>
        {
            ["Background"] = new ThemeRef("AccentFillColorDefaultBrush"),
        };
        var dict2 = new Dictionary<string, ThemeRef>
        {
            ["Background"] = new ThemeRef("CardBackgroundFillColorDefaultBrush"),
        };

        var key1 = BuildCacheKeyViaReflection("Button", dict1);
        var key2 = BuildCacheKeyViaReflection("Button", dict2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_Single_Binding_Has_Expected_Format()
    {
        var dict = new Dictionary<string, ThemeRef>
        {
            ["Background"] = new ThemeRef("AccentFillColorDefaultBrush"),
        };

        var key = BuildCacheKeyViaReflection("Button", dict);

        Assert.Equal("Button|Background=AccentFillColorDefaultBrush", key);
    }

    [Fact]
    public void BuildCacheKey_Multiple_Bindings_Are_Sorted()
    {
        var dict = new Dictionary<string, ThemeRef>
        {
            ["Foreground"] = new ThemeRef("TextBrush"),
            ["Background"] = new ThemeRef("AccentBrush"),
        };

        var key = BuildCacheKeyViaReflection("Button", dict);

        // Background sorts before Foreground (ordinal)
        Assert.Equal("Button|Background=AccentBrush|Foreground=TextBrush", key);
    }

    [Fact]
    public void ClearStyleCache_Does_Not_Throw()
    {
        // Just ensure the public API is callable without error
        ThemeBindingApplier.ClearStyleCache();
    }

    /// <summary>
    /// Invokes the private BuildCacheKey method via reflection for testing.
    /// </summary>
    private static string BuildCacheKeyViaReflection(string targetType, IReadOnlyDictionary<string, ThemeRef> bindings)
    {
        var method = typeof(ThemeBindingApplier).GetMethod(
            "BuildCacheKey",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [targetType, bindings])!;
    }
}
