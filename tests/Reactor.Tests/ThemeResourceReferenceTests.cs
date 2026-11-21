using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// <c>Reconciler.FormatThemeResourceReference</c>, which quotes and escapes a resource key for the
/// <c>{ThemeResource}</c> markup <see cref="ThemeResourceProbe"/> asks WinUI to parse.
/// </summary>
public class ThemeResourceReferenceTests
{
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
