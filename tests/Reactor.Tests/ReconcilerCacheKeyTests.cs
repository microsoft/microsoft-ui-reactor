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

    [Theory]
    [InlineData("AccentBrush", "{ThemeResource 'AccentBrush'}")]
    [InlineData("Key,With,Commas", "{ThemeResource 'Key,With,Commas'}")]
    [InlineData("Key}Brace", "{ThemeResource 'Key}Brace'}")]
    [InlineData("Key=Equals", "{ThemeResource 'Key=Equals'}")]
    [InlineData("Key With Space", "{ThemeResource 'Key With Space'}")]
    [InlineData("Key\\Slash", "{ThemeResource 'Key\\\\Slash'}")]
    [InlineData("Key'Quote", "{ThemeResource \"Key'Quote\"}")]
    [InlineData("Key\"Quote", "{ThemeResource 'Key\"Quote'}")]
    [InlineData("A&B<C>", "{ThemeResource 'A&B<C>'}")]
    public void FormatThemeResourceReference_QuotesAndEscapesKey(string key, string expectedReference)
    {
        var value = Reconciler.FormatThemeResourceReference(key);
        Assert.NotNull(value);
        // The value goes into an XML attribute: it must carry no raw markup characters,
        // and unescaping it must give back the quoted markup-extension reference.
        Assert.DoesNotContain('<', value);
        Assert.DoesNotContain('"', value);
        Assert.DoesNotContain('\'', value);
        var attribute = global::System.Xml.Linq.XElement.Parse($"<S V=\"{value}\"/>").Attribute("V")!.Value;
        Assert.Equal(expectedReference, attribute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Both'And\"Quotes")]
    [InlineData("Tab\tKey")]
    [InlineData("Line\nBreak")]
    public void FormatThemeResourceReference_RejectsUnrepresentableKey(string? key)
        => Assert.Null(Reconciler.FormatThemeResourceReference(key));

    // A lone surrogate doesn't survive InlineData serialization, so it is built here.
    [Fact]
    public void FormatThemeResourceReference_RejectsLoneSurrogate()
        => Assert.Null(Reconciler.FormatThemeResourceReference("Lone" + '\uD800' + "Surrogate"));
}
