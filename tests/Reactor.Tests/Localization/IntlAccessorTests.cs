using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

public class IntlAccessorTests
{
    private static IntlAccessor CreateAccessor(string locale, InMemoryResourceProvider provider)
    {
        return new IntlAccessor(locale, provider, new MessageCache(), "en-US");
    }

    // ── Message() ──────────────────────────────────────────────────

    [Fact]
    public void Message_SimpleString_ReturnsValue()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Common", "Save", "Save");

        var t = CreateAccessor("en-US", provider);
        Assert.Equal("Save", t.Message(new MessageKey("Common", "Save")));
    }

    [Fact]
    public void Message_WithInterpolation_FormatsArgs()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Settings", "LoggedInAs", "Logged in as {name}");

        var t = CreateAccessor("en-US", provider);
        var result = t.Message(new MessageKey("Settings", "LoggedInAs"), new { name = "Alice" });
        Assert.Equal("Logged in as Alice", result);
    }

    [Fact]
    public void Message_IcuPlurals_SelectsCorrectForm()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Cart", "ItemCount",
                "{count, plural, =0 {Your cart is empty} one {# item in cart} other {# items in cart}}");

        var t = CreateAccessor("en-US", provider);

        Assert.Equal("Your cart is empty", t.Message(new MessageKey("Cart", "ItemCount"), new { count = 0 }));
        Assert.Equal("1 item in cart", t.Message(new MessageKey("Cart", "ItemCount"), new { count = 1 }));
        Assert.Equal("5 items in cart", t.Message(new MessageKey("Cart", "ItemCount"), new { count = 5 }));
    }

    [Fact]
    public void Message_IcuSelect_SelectsCorrectBranch()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Profile", "Greeting",
                "{gender, select, male {He joined} female {She joined} other {They joined}}");

        var t = CreateAccessor("en-US", provider);

        Assert.Equal("He joined", t.Message(new MessageKey("Profile", "Greeting"), new { gender = "male" }));
        Assert.Equal("She joined", t.Message(new MessageKey("Profile", "Greeting"), new { gender = "female" }));
        Assert.Equal("They joined", t.Message(new MessageKey("Profile", "Greeting"), new { gender = "other" }));
    }

    [Fact]
    public void Message_MissingKey_ReturnsMissingMarker()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.Message(new MessageKey("Common", "Missing"));
        Assert.Equal("[?? Common.Missing ??]", result);
    }

    [Fact]
    public void Message_MissingInCurrentLocale_FallsBackToDefault()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Common", "Save", "Save");
        // fr-FR doesn't have the key

        var t = CreateAccessor("fr-FR", provider);
        Assert.Equal("Save", t.Message(new MessageKey("Common", "Save")));
    }

    // ── FormatNumber() ─────────────────────────────────────────────

    [Fact]
    public void FormatNumber_Default_UsesLocaleFormatting()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.FormatNumber(1234.56,
            new NumberFormatOptions { MinimumFractionDigits = 2, MaximumFractionDigits = 2 });
        Assert.Equal("1,234.56", result);
    }

    [Fact]
    public void FormatNumber_Currency_FormatsCurrency()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.FormatNumber(42.50, new NumberFormatOptions { Style = NumberStyle.Currency });
        Assert.Equal("$42.50", result);
    }

    [Fact]
    public void FormatNumber_Percent_FormatsPercent()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.FormatNumber(0.75, new NumberFormatOptions
        {
            Style = NumberStyle.Percent,
            MinimumFractionDigits = 2,
            MaximumFractionDigits = 2,
        });
        Assert.Equal("75.00%", result);
    }

    [Fact]
    public void FormatNumber_NonEnglishLocale_UsesLocaleFormatting()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("de-DE", provider);

        var result = t.FormatNumber(1234.56);
        // German uses period as thousands separator and comma for decimal
        Assert.Contains("1.234", result);
    }

    // ── FormatDate() ───────────────────────────────────────────────

    [Fact]
    public void FormatDate_Short_ReturnsShortDate()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var date = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var result = t.FormatDate(date, new DateFormatOptions { Style = DateStyle.Short });
        Assert.Equal("1/15/2026", result);
    }

    [Fact]
    public void FormatDate_Long_ReturnsLongDate()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var date = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var result = t.FormatDate(date, new DateFormatOptions { Style = DateStyle.Long });
        Assert.Contains("January", result);
        Assert.Contains("2026", result);
    }

    [Fact]
    public void FormatDate_Full_IncludesTime()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var date = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var result = t.FormatDate(date, new DateFormatOptions { Style = DateStyle.Full });
        Assert.Contains("January", result);
        Assert.Contains("2026", result);
        // Full format includes time component
        Assert.Contains(":", result);
    }

    // ── FormatList() ───────────────────────────────────────────────

    [Fact]
    public void FormatList_Conjunction_ThreeItems()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.FormatList(["A", "B", "C"], ListFormatType.Conjunction);
        Assert.Equal("A, B, and C", result);
    }

    [Fact]
    public void FormatList_Disjunction_ThreeItems()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.FormatList(["A", "B", "C"], ListFormatType.Disjunction);
        Assert.Equal("A, B, or C", result);
    }

    [Fact]
    public void FormatList_TwoItems_Conjunction()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        var result = t.FormatList(["X", "Y"], ListFormatType.Conjunction);
        Assert.Equal("X and Y", result);
    }

    [Fact]
    public void FormatList_SingleItem_ReturnsThatItem()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        Assert.Equal("Only", t.FormatList(["Only"]));
    }

    [Fact]
    public void FormatList_Empty_ReturnsEmptyString()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        Assert.Equal("", t.FormatList([]));
    }

    // ── Direction ──────────────────────────────────────────────────

    [Fact]
    public void Direction_Ltr_ForEnglish()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("en-US", provider);

        Assert.Equal(FlowDirection.LeftToRight, t.Direction);
        Assert.False(t.IsRtl);
    }

    [Fact]
    public void Direction_Rtl_ForArabic()
    {
        var provider = new InMemoryResourceProvider();
        var t = CreateAccessor("ar-SA", provider);

        Assert.Equal(FlowDirection.RightToLeft, t.Direction);
        Assert.True(t.IsRtl);
    }

    // ── Integration: IStringResourceProvider mock → IntlAccessor ──

    [Fact]
    public void Integration_MockProvider_ReturnsCorrectStrings()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Common", "Save", "Save")
            .Add("en-US", "Common", "Cancel", "Cancel")
            .Add("en-US", "Settings", "Title", "Settings")
            .Add("fr-FR", "Common", "Save", "Enregistrer")
            .Add("fr-FR", "Common", "Cancel", "Annuler")
            .Add("fr-FR", "Settings", "Title", "Paramètres");

        var enAccessor = CreateAccessor("en-US", provider);
        var frAccessor = CreateAccessor("fr-FR", provider);

        Assert.Equal("Save", enAccessor.Message(new MessageKey("Common", "Save")));
        Assert.Equal("Enregistrer", frAccessor.Message(new MessageKey("Common", "Save")));
        Assert.Equal("Settings", enAccessor.Message(new MessageKey("Settings", "Title")));
        Assert.Equal("Paramètres", frAccessor.Message(new MessageKey("Settings", "Title")));
    }

    [Fact]
    public void Integration_LocaleSwitch_DirectionFlips()
    {
        var provider = new InMemoryResourceProvider()
            .Add("en-US", "Common", "Hello", "Hello")
            .Add("ar-SA", "Common", "Hello", "مرحبا");

        var enAccessor = CreateAccessor("en-US", provider);
        var arAccessor = CreateAccessor("ar-SA", provider);

        Assert.Equal(FlowDirection.LeftToRight, enAccessor.Direction);
        Assert.Equal(FlowDirection.RightToLeft, arAccessor.Direction);
        Assert.Equal("Hello", enAccessor.Message(new MessageKey("Common", "Hello")));
        Assert.Equal("مرحبا", arAccessor.Message(new MessageKey("Common", "Hello")));
    }
}
