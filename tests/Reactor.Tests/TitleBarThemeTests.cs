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
        // TryResolveCaptionTheme is the only source of ApplyTitleBarTheme's write, so a
        // Child-embedded window — parented into a host's chrome — is never themed, even
        // with a declaration and a previous write.
        var owner = new WindowSpec
        {
            Embed = new EmbedRequest(WindowEmbedStyle.Owner, HostPid: 1234, InitialVisibility: true),
        };
        var child = new WindowSpec
        {
            TitleBarTheme = WindowTitleBarTheme.Dark,
            Embed = new EmbedRequest(WindowEmbedStyle.Child, HostPid: 1234, InitialVisibility: true),
        };

        Assert.True(Resolve(new WindowSpec(), WindowTitleBarTheme.Dark, null, out _));
        Assert.True(Resolve(owner, WindowTitleBarTheme.Dark, null, out _));
        Assert.False(Resolve(child, WindowTitleBarTheme.Dark, null, out _));
        Assert.False(Resolve(child, null, WindowTitleBarTheme.Dark, out _));
    }

    [Fact]
    public void CaptionTheme_IsOptIn_AndResolvesTheWrittenValue()
    {
        var spec = new WindowSpec();
        // Nothing declared and nothing applied: an imperatively-set value stays the app's.
        Assert.False(Resolve(spec, resolved: null, applied: null, out _));

        // A declaration takes ownership and writes the mapped value.
        Assert.True(Resolve(spec, WindowTitleBarTheme.Light, applied: null, out var declared));
        Assert.Equal(TitleBarTheme.Light, declared);

        // A withdrawn declaration hands the baseline back.
        Assert.True(Resolve(spec, resolved: null, WindowTitleBarTheme.Light, out var restored));
        Assert.Equal(Baseline, restored);
    }

    // Distinct from every value the tests declare, so a restore cannot pass by coincidence.
    private const TitleBarTheme Baseline = TitleBarTheme.UseDefaultAppMode;

    private static bool Resolve(
        WindowSpec spec, WindowTitleBarTheme? resolved, WindowTitleBarTheme? applied, out TitleBarTheme target) =>
        ReactorWindow.TryResolveCaptionTheme(spec, resolved, applied, Baseline, out target);
}
