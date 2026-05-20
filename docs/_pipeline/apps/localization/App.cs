using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<LocalizationApp>("Localization", width: 650, height: 700
#if DEBUG
    , preview: true
#endif
);

// <snippet:resource-provider>
class DemoResourceProvider : IStringResourceProvider
{
    private readonly Dictionary<string, Dictionary<string, string>> _strings = new()
    {
        ["en-US"] = new() {
            ["App.title"] = "My Application",
            ["App.greeting"] = "Hello, {name}!",
        },
        ["fr-FR"] = new() {
            ["App.title"] = "Mon Application",
            ["App.greeting"] = "Bonjour, {name} !",
        },
        ["ar-SA"] = new() {
            ["App.title"] = "\u062a\u0637\u0628\u064a\u0642\u064a",
            ["App.greeting"] = "\u0645\u0631\u062d\u0628\u0627\u060c {name}!",
        }
    };

    public string? GetString(string locale, string ns, string key)
    {
        var fullKey = $"{ns}.{key}";
        return _strings.TryGetValue(locale, out var s)
            && s.TryGetValue(fullKey, out var v) ? v : null;
    }
}
// </snippet:resource-provider>

// <snippet:locale-provider>
class LocaleSwitcher : Component
{
    public override Element Render()
    {
        var (localeIndex, setLocaleIndex) = UseState(0);
        var locales = new[] { "en-US", "fr-FR", "ar-SA" };
        var locale = locales[localeIndex];
        var provider = new DemoResourceProvider();

        return VStack(16,
            ComboBox(["English (US)", "Fran\u00e7ais", "\u0627\u0644\u0639\u0631\u0628\u064a\u0629"],
                localeIndex, setLocaleIndex),
            LocaleProvider(locale,
                Component<LocalizedContent>(),
                resourceProvider: provider,
                defaultLocale: "en-US")
        ).Padding(24);
    }
}
// </snippet:locale-provider>

// <snippet:useintl-messages>
class LocalizedContent : Component
{
    public override Element Render()
    {
        var intl = UseIntl();
        var title = intl.Message(new MessageKey("App", "title"));
        var greeting = intl.Message(
            new MessageKey("App", "greeting"),
            ("name", "Alice"));

        return VStack(12,
            TextBlock(title).FontSize(24).Bold(),
            TextBlock(greeting).FontSize(16),
            TextBlock($"Locale: {intl.Locale}").Opacity(0.6),
            TextBlock($"Direction: {intl.Direction}").Opacity(0.6)
        );
    }
}
// </snippet:useintl-messages>

// <snippet:format-numbers-dates>
class FormattingDemo : Component
{
    public override Element Render()
    {
        var intl = UseIntl();
        var price = intl.FormatNumber(1234.56,
            new NumberFormatOptions { Style = NumberStyle.Currency });
        var percent = intl.FormatNumber(0.875,
            new NumberFormatOptions { Style = NumberStyle.Percent });
        var date = intl.FormatDate(DateTimeOffset.Now,
            new DateFormatOptions { Style = DateStyle.Long });
        var items = intl.FormatList(
            new[] { "Apples", "Bananas", "Cherries" },
            ListFormatType.Conjunction);

        return VStack(8,
            SubHeading("Formatting"),
            TextBlock($"Price: {price}"),
            TextBlock($"Rate: {percent}"),
            TextBlock($"Date: {date}"),
            TextBlock($"List: {items}")
        ).Padding(24);
    }
}
// </snippet:format-numbers-dates>

// <snippet:rtl-detection>
class RtlDemo : Component
{
    public override Element Render()
    {
        var intl = UseIntl();
        var locales = new[] { "en-US", "fr-FR", "ar-SA", "he-IL", "ja-JP" };

        return VStack(8,
            SubHeading("RTL Detection"),
            VStack(4,
                locales.Select(loc =>
                    HStack(8,
                        TextBlock(loc).Width(60),
                        TextBlock(RtlHelper.IsRtlLocale(loc) ? "RTL" : "LTR")
                            .Bold()
                            .Foreground(RtlHelper.IsRtlLocale(loc)
                                ? "#d13438" : "#107c10")
                    )
                ).ToArray()
            ),
            When(intl.IsRtl, () =>
                TextBlock("Current layout is right-to-left")
                    .Foreground("#d13438").SemiBold())
        ).Padding(24);
    }
}
// </snippet:rtl-detection>

// <snippet:pseudo-localization>
class PseudoLocDemo : Component
{
    public override Element Render()
    {
        var (pseudo, setPseudo) = UseState(false);
        var provider = new DemoResourceProvider();

        return VStack(12,
            SubHeading("Pseudo-Localization"),
            ToggleSwitch(pseudo, setPseudo,
                header: "Enable pseudo-localization"),
            LocaleProvider("en-US",
                Func(ctx =>
                {
                    var intl = ctx.UseIntl();
                    var title = intl.Message(new MessageKey("App", "title"));
                    var greeting = intl.Message(
                        new MessageKey("App", "greeting"),
                        ("name", "World"));
                    return VStack(4,
                        TextBlock(title).FontSize(18).Bold(),
                        TextBlock(greeting));
                }),
                resourceProvider: provider,
                pseudoLocalize: pseudo)
        ).Padding(24);
    }
}
// </snippet:pseudo-localization>

// Main app
class LocalizationApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Localization"),
                Component<LocaleSwitcher>(),
                Component<FormattingDemo>(),
                Component<RtlDemo>(),
                Component<PseudoLocDemo>()
            ).Padding(24)
        );
    }
}
