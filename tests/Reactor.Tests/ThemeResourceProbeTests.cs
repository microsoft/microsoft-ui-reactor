using System.Xml.Linq;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1534 — the markup <see cref="ThemeResourceProbe"/> asks WinUI to parse. Whether WinUI
/// resolves it is covered live by <c>ThemeRefResolutionFixtures</c>; these pin the cases that
/// decide between the probe and the dictionary walk, and the XML escaping.
/// </summary>
public class ThemeResourceProbeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Both'And\"Quotes")]
    [InlineData("Tab\tKey")]
    [InlineData("Line\nKey")]
    [InlineData("Control\u0001Key")]
    public void Keys_That_Cannot_Be_Written_As_A_Reference_Are_Not_Probed(string key)
        => Assert.Null(ThemeResourceProbe.BuildStyleXaml(key));

    // Not an InlineData case: test-case serialization replaces a lone surrogate before it runs.
    [Fact]
    public void Key_With_A_Lone_Surrogate_Is_Not_Probed()
        => Assert.Null(ThemeResourceProbe.BuildStyleXaml("Lone" + '\uD800' + "Surrogate"));

    [Theory]
    [InlineData("ControlFillColorSecondaryBrush", "{ThemeResource 'ControlFillColorSecondaryBrush'}")]
    [InlineData("Key,With}Odd=Chars", "{ThemeResource 'Key,With}Odd=Chars'}")]
    [InlineData("Key'Quote", "{ThemeResource \"Key'Quote\"}")]
    [InlineData("Back\\slash", "{ThemeResource 'Back\\\\slash'}")]
    [InlineData("Lt<Amp&\"Gt>", "{ThemeResource 'Lt<Amp&\"Gt>'}")]
    public void Setter_Value_Is_A_Quoted_ThemeResource_Reference(string key, string expected)
    {
        var xaml = ThemeResourceProbe.BuildStyleXaml(key);
        Assert.NotNull(xaml);
        var setter = XDocument.Parse(xaml!).Root!.Elements().Single();
        Assert.Equal("Tag", setter.Attribute("Property")!.Value);
        Assert.Equal(expected, setter.Attribute("Value")!.Value);
    }
}
