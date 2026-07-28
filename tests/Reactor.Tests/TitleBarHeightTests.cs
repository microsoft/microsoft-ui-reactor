using System.Reflection;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #917 — declarative title-bar height. Headless coverage of the data
/// shapes and precedence inputs; the live caption/control geometry is proven by
/// the <c>TitleBarHeight_*</c> selftest fixtures (no WinUI object can be
/// constructed in this tier).
/// </summary>
public class TitleBarHeightTests
{
    [Fact]
    public void WindowSpec_TitleBarHeight_DefaultsToNull_AndRoundTrips()
    {
        Assert.Null(new WindowSpec().TitleBarHeight);

        var tall = new WindowSpec { TitleBarHeight = WindowTitleBarHeight.Tall };
        Assert.Equal(WindowTitleBarHeight.Tall, tall.TitleBarHeight);
        tall.Validate(); // a declared height must not trip cross-field validation

        Assert.Null((tall with { TitleBarHeight = null }).TitleBarHeight);
    }

    [Fact]
    public void TitleBarElement_HeightOption_DefaultsToNull_AndRoundTrips()
    {
        Assert.Null(TitleBar("t").HeightOption);
        Assert.Equal(
            WindowTitleBarHeight.Collapsed,
            (TitleBar("t") with { HeightOption = WindowTitleBarHeight.Collapsed }).HeightOption);
    }

    [Fact]
    public void Tall_Modifier_SetsTall_AndDiffersFromUnmodified()
    {
        var plain = TitleBar("t");
        var tall = plain.Tall();

        Assert.Equal(WindowTitleBarHeight.Tall, tall.HeightOption);
        // Differential isolation: a no-op modifier would leave these equal.
        Assert.NotEqual(plain.HeightOption, tall.HeightOption);
        // The element record is immutable — the source must be untouched.
        Assert.Null(plain.HeightOption);
    }

    [Fact]
    public void Tall_False_SelectsStandard_NotNull()
    {
        // Explicitly opting out must declare Standard, otherwise it could not
        // override a WindowSpec-level or previously applied Tall.
        Assert.Equal(WindowTitleBarHeight.Standard, TitleBar("t").Tall(false).HeightOption);
    }

    [Fact]
    public void HeightOption_Modifier_PreservesConcreteTypeAndOtherProps()
    {
        var el = TitleBar("t").Subtitle("s").HeightOption(WindowTitleBarHeight.Tall);
        Assert.IsType<TitleBarElement>(el);
        Assert.Equal("s", el.Subtitle);
        Assert.Equal("t", el.Title);
        Assert.Equal(WindowTitleBarHeight.Tall, el.HeightOption);
    }

    [Fact]
    public void HeightOption_DoesNotWriteTheCommonHeightModifier()
    {
        // .Tall() must not pre-bake a Height modifier: the control height is
        // applied at mount so an explicit .Height(...) can still win, and so a
        // WindowSpec override can suppress it.
        Assert.Null(TitleBar("t").Tall().Modifiers?.Height);
        Assert.Equal(64d, TitleBar("t").Tall().Height(64).Modifiers?.Height);
    }

    [Fact]
    public void TallControlHeight_MatchesTheXamlTemplateRowHeight()
    {
        var field = typeof(TitleBarElement).GetField(
            "TallTitleBarControlHeight", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(48d, (double)field!.GetRawConstantValue()!);
    }

    [Theory]
    [InlineData(WindowTitleBarHeight.Standard, 0)]
    [InlineData(WindowTitleBarHeight.Tall, 1)]
    [InlineData(WindowTitleBarHeight.Collapsed, 2)]
    public void WindowTitleBarHeight_MirrorsThePlatformEnumOrdering(WindowTitleBarHeight value, int expected)
    {
        // The mapping in ReactorWindow.ApplyTitleBarHeight is written switch-by-name,
        // but the enum is documented as mirroring Microsoft.UI.Windowing.TitleBarHeightOption.
        // Pin the members so a silent reorder/rename is caught here.
        Assert.Equal(expected, (int)value);
        Assert.Equal(
            value.ToString(),
            Enum.GetName(typeof(Microsoft.UI.Windowing.TitleBarHeightOption), expected));
    }
}
