using Microsoft.UI.Reactor;
using Microsoft.UI.Windowing;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1297 — declarative caption theme (<c>WindowSpec.TitleBarTheme</c> /
/// <c>TitleBar(...).PreferredTheme(...)</c>). Headless coverage of the data shapes
/// and the native mapping; the live <c>AppWindow.TitleBar.PreferredTheme</c> writes
/// are proven by the <c>TitleBarTheme_*</c> selftest fixtures.
/// </summary>
public class TitleBarThemeTests
{
    [Fact]
    public void WindowSpec_TitleBarTheme_DefaultsToNull_AndRoundTrips()
    {
        Assert.Null(new WindowSpec().TitleBarTheme);

        var dark = new WindowSpec { TitleBarTheme = WindowTitleBarTheme.Dark };
        Assert.Equal(WindowTitleBarTheme.Dark, dark.TitleBarTheme);
        dark.Validate();

        Assert.Null((dark with { TitleBarTheme = null }).TitleBarTheme);
    }

    [Fact]
    public void TitleBarElement_PreferredTheme_DefaultsToNull()
    {
        Assert.Null(TitleBar("t").PreferredTheme);
    }

    [Fact]
    public void PreferredTheme_Modifier_SetsValue_AndLeavesSourceUntouched()
    {
        var plain = TitleBar("t").Subtitle("sub").Tall();
        var dark = plain.PreferredTheme(WindowTitleBarTheme.Dark);

        Assert.Equal(WindowTitleBarTheme.Dark, dark.PreferredTheme);
        Assert.Null(plain.PreferredTheme);
        // Other declarations survive the modifier.
        Assert.Equal("sub", dark.Subtitle);
        Assert.Equal(WindowTitleBarHeight.Tall, dark.HeightOption);
        // A theme change must make the element unequal, or the reconciler would skip the update.
        Assert.NotEqual(dark, plain.PreferredTheme(WindowTitleBarTheme.Light));
    }

    [Theory]
    [InlineData(WindowTitleBarTheme.Legacy, TitleBarTheme.Legacy)]
    [InlineData(WindowTitleBarTheme.UseDefaultAppMode, TitleBarTheme.UseDefaultAppMode)]
    [InlineData(WindowTitleBarTheme.Light, TitleBarTheme.Light)]
    [InlineData(WindowTitleBarTheme.Dark, TitleBarTheme.Dark)]
    public void ToNative_MapsEveryValue(WindowTitleBarTheme theme, TitleBarTheme expected) =>
        Assert.Equal(expected, ReactorWindow.ToNativeTitleBarTheme(theme));

    [Fact]
    public void ToNative_CoversEveryEnumMember()
    {
        // A member added to WindowTitleBarTheme without a mapping would throw here.
        foreach (var theme in Enum.GetValues<WindowTitleBarTheme>())
            _ = ReactorWindow.ToNativeTitleBarTheme(theme);
        Assert.Equal(Enum.GetValues<TitleBarTheme>().Length, Enum.GetValues<WindowTitleBarTheme>().Length);
    }

    [Fact]
    public void CaptionTheme_IsSkipped_OnlyForChildEmbeddedWindows()
    {
        // ApplyTitleBarTheme is gated on this predicate: a Child-embedded window is
        // parented into a host's chrome and must not have its caption themed.
        Assert.True(ReactorWindow.IsTopLevelChromeAllowed(new WindowSpec()));
        Assert.True(ReactorWindow.IsTopLevelChromeAllowed(new WindowSpec
        {
            Embed = new EmbedRequest(WindowEmbedStyle.Owner, HostPid: 1234, InitialVisibility: true),
        }));
        Assert.False(ReactorWindow.IsTopLevelChromeAllowed(new WindowSpec
        {
            TitleBarTheme = WindowTitleBarTheme.Dark,
            Embed = new EmbedRequest(WindowEmbedStyle.Child, HostPid: 1234, InitialVisibility: true),
        }));
    }
}
