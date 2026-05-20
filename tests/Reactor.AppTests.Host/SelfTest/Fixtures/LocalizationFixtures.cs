using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class LocalizationFixtures
{
    /// <summary>
    /// In-memory resource provider for E2E testing without real .resw files.
    /// </summary>
    private sealed class InMemoryResourceProvider : IStringResourceProvider
    {
        private readonly Dictionary<(string Locale, string Namespace, string Key), string> _strings = new(
            new LocaleKeyComparer());

        public InMemoryResourceProvider Add(string locale, string ns, string key, string value)
        {
            _strings[(locale, ns, key)] = value;
            return this;
        }

        public string? GetString(string locale, string ns, string key) =>
            _strings.TryGetValue((locale, ns, key), out var value) ? value : null;

        private sealed class LocaleKeyComparer : IEqualityComparer<(string Locale, string Namespace, string Key)>
        {
            public bool Equals((string Locale, string Namespace, string Key) x,
                               (string Locale, string Namespace, string Key) y) =>
                StringComparer.OrdinalIgnoreCase.Equals(x.Locale, y.Locale) &&
                StringComparer.OrdinalIgnoreCase.Equals(x.Namespace, y.Namespace) &&
                StringComparer.Ordinal.Equals(x.Key, y.Key);

            public int GetHashCode((string Locale, string Namespace, string Key) obj) =>
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Locale),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Namespace),
                    StringComparer.Ordinal.GetHashCode(obj.Key));
        }
    }

    private static InMemoryResourceProvider BuildResources()
    {
        var p = new InMemoryResourceProvider();

        // -- English (en-US) ---------------------------------------------
        p.Add("en-US", "Resources", "AppTitle", "My Application");
        p.Add("en-US", "Resources", "Welcome", "Welcome to the app");
        p.Add("en-US", "Resources", "Greeting", "Hello, {name}!");
        p.Add("en-US", "Resources", "ItemCount",
            "{count, plural, =0 {No items} one {1 item} other {# items}}");
        p.Add("en-US", "Resources", "SearchResults",
            "Found {count, plural, =0 {no results} one {1 result} other {# results}} for \"{query}\"");
        p.Add("en-US", "Resources", "DirectionLabel", "LTR");

        // -- Korean (ko-KR) ----------------------------------------------
        p.Add("ko-KR", "Resources", "AppTitle", "\ub0b4 \uc560\ud50c\ub9ac\ucf00\uc774\uc158");
        p.Add("ko-KR", "Resources", "Welcome", "\uc571\uc5d0 \uc624\uc2e0 \uac83\uc744 \ud658\uc601\ud569\ub2c8\ub2e4");
        p.Add("ko-KR", "Resources", "Greeting", "\uc548\ub155\ud558\uc138\uc694, {name}!");
        p.Add("ko-KR", "Resources", "ItemCount",
            "{count, plural, =0 {\ud56d\ubaa9 \uc5c6\uc74c} other {# \ud56d\ubaa9}}");
        p.Add("ko-KR", "Resources", "SearchResults",
            "\"{query}\"\uc5d0 \ub300\ud574 {count, plural, =0 {\uacb0\uacfc \uc5c6\uc74c} other {#\uac1c\uc758 \uacb0\uacfc}}\ub97c \ucc3e\uc558\uc2b5\ub2c8\ub2e4");
        p.Add("ko-KR", "Resources", "DirectionLabel", "LTR");

        // -- Arabic (ar-SA) - RTL ----------------------------------------
        p.Add("ar-SA", "Resources", "AppTitle", "\u062a\u0637\u0628\u064a\u0642\u064a");
        p.Add("ar-SA", "Resources", "Welcome", "\u0645\u0631\u062d\u0628\u0627\u064b \u0628\u0643 \u0641\u064a \u0627\u0644\u062a\u0637\u0628\u064a\u0642");
        p.Add("ar-SA", "Resources", "Greeting", "\u0645\u0631\u062d\u0628\u0627\u064b\u060c {name}!");
        p.Add("ar-SA", "Resources", "ItemCount",
            "{count, plural, =0 {\u0644\u0627 \u0639\u0646\u0627\u0635\u0631} one {\u0639\u0646\u0635\u0631 \u0648\u0627\u062d\u062f} two {\u0639\u0646\u0635\u0631\u0627\u0646} few {# \u0639\u0646\u0627\u0635\u0631} many {# \u0639\u0646\u0635\u0631\u064b\u0627} other {# \u0639\u0646\u0635\u0631}}");
        p.Add("ar-SA", "Resources", "SearchResults",
            "\u062a\u0645 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 {count, plural, =0 {\u0644\u0627 \u0646\u062a\u0627\u0626\u062c} one {\u0646\u062a\u064a\u062c\u0629 \u0648\u0627\u062d\u062f\u0629} two {\u0646\u062a\u064a\u062c\u062a\u0627\u0646} few {# \u0646\u062a\u0627\u0626\u062c} many {# \u0646\u062a\u064a\u062c\u0629} other {# \u0646\u062a\u064a\u062c\u0629}} \u0644\u0640 \"{query}\"");
        p.Add("ar-SA", "Resources", "DirectionLabel", "RTL");

        // -- Japanese (ja-JP) --------------------------------------------
        p.Add("ja-JP", "Resources", "AppTitle", "\u30de\u30a4\u30a2\u30d7\u30ea\u30b1\u30fc\u30b7\u30e7\u30f3");
        p.Add("ja-JP", "Resources", "Welcome", "\u30a2\u30d7\u30ea\u3078\u3088\u3046\u3053\u305d");
        p.Add("ja-JP", "Resources", "Greeting", "\u3053\u3093\u306b\u3061\u306f\u3001{name}\u3055\u3093\uff01");
        p.Add("ja-JP", "Resources", "ItemCount",
            "{count, plural, =0 {\u30a2\u30a4\u30c6\u30e0\u306a\u3057} other {#\u500b\u306e\u30a2\u30a4\u30c6\u30e0}}");
        p.Add("ja-JP", "Resources", "SearchResults",
            "\"{query}\"\u306e\u691c\u7d22\u7d50\u679c: {count, plural, =0 {\u7d50\u679c\u306a\u3057} other {#\u4ef6}}");
        p.Add("ja-JP", "Resources", "DirectionLabel", "LTR");

        // -- German (de-DE) ----------------------------------------------
        p.Add("de-DE", "Resources", "AppTitle", "Meine Anwendung");
        p.Add("de-DE", "Resources", "Welcome", "Willkommen in der App");
        p.Add("de-DE", "Resources", "Greeting", "Hallo, {name}!");
        p.Add("de-DE", "Resources", "ItemCount",
            "{count, plural, =0 {Keine Eintr\u00e4ge} one {1 Eintrag} other {# Eintr\u00e4ge}}");
        p.Add("de-DE", "Resources", "SearchResults",
            "{count, plural, =0 {Keine Ergebnisse} one {1 Ergebnis} other {# Ergebnisse}} f\u00fcr \"{query}\" gefunden");
        p.Add("de-DE", "Resources", "DirectionLabel", "LTR");

        return p;
    }

    private static readonly MessageKey AppTitle = new("Resources", "AppTitle");
    private static readonly MessageKey Welcome = new("Resources", "Welcome");
    private static readonly MessageKey Greeting = new("Resources", "Greeting");
    private static readonly MessageKey ItemCount = new("Resources", "ItemCount");
    private static readonly MessageKey SearchResults = new("Resources", "SearchResults");
    private static readonly MessageKey DirectionLabel = new("Resources", "DirectionLabel");

    // -- Fixture: full locale-switching E2E -------------------------------

    internal class LocaleSwitching(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var resources = BuildResources();
            var cache = new MessageCache();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (locale, setLocale) = ctx.UseState("en-US");

                var t = ctx.UseMemo(() =>
                    new IntlAccessor(locale, resources, cache, "en-US"),
                    locale);

                ctx.UseEffect(() => { cache.Flush(); return () => { }; }, locale);

                var languages = new[] { "en-US", "ko-KR", "ar-SA", "ja-JP", "de-DE" };

                return VStack(8,
                    HStack(4,
                        languages.Select(lang =>
                            Button(lang, () => setLocale(lang))
                                .IsEnabled(!(locale == lang))).ToArray()
                    ),

                    VStack(8,
                        TextBlock(t.Message(AppTitle)).Set(tb => tb.FontSize = 24),
                        TextBlock(t.Message(Welcome)),
                        TextBlock(t.Message(Greeting, ("name", "World"))),

                        TextBlock(t.Message(ItemCount, ("count", 0))),
                        TextBlock(t.Message(ItemCount, ("count", 5))),

                        TextBlock(t.Message(SearchResults, ("count", 0), ("query", locale == "ko-KR" ? "\ud14c\uc2a4\ud2b8" : "test"))),
                        TextBlock(t.Message(SearchResults, ("count", 1), ("query", locale == "ko-KR" ? "\ud14c\uc2a4\ud2b8" : "test"))),
                        TextBlock(t.Message(SearchResults, ("count", 42), ("query", locale == "ko-KR" ? "\ud14c\uc2a4\ud2b8" : "test"))),

                        TextBlock(t.Message(DirectionLabel))
                    )
                    .Set(sp => sp.FlowDirection = t.Direction)
                    .Set(sp => sp.Tag = "ContentRoot")
                    .MarginInlineStart(24)
                );
            });

            await Harness.Render();

            // -- English (default) -------------------------------------------
            CheckLocale("en-US", "My Application", "Welcome to the app",
                "Hello, World!", "No items", "5 items",
                "Found no results for \"test\"", "Found 1 result for \"test\"",
                "Found 42 results for \"test\"",
                expectRtl: false);

            // -- Korean ------------------------------------------------------
            H.ClickButton("ko-KR");
            await Harness.Render();

            CheckLocale("ko-KR",
                "\ub0b4 \uc560\ud50c\ub9ac\ucf00\uc774\uc158",
                "\uc571\uc5d0 \uc624\uc2e0 \uac83\uc744 \ud658\uc601\ud569\ub2c8\ub2e4",
                "\uc548\ub155\ud558\uc138\uc694, World!",
                "\ud56d\ubaa9 \uc5c6\uc74c", "5 \ud56d\ubaa9",
                "\"\ud14c\uc2a4\ud2b8\"\uc5d0 \ub300\ud574 \uacb0\uacfc \uc5c6\uc74c\ub97c \ucc3e\uc558\uc2b5\ub2c8\ub2e4",
                "\"\ud14c\uc2a4\ud2b8\"\uc5d0 \ub300\ud574 1\uac1c\uc758 \uacb0\uacfc\ub97c \ucc3e\uc558\uc2b5\ub2c8\ub2e4",
                "\"\ud14c\uc2a4\ud2b8\"\uc5d0 \ub300\ud574 42\uac1c\uc758 \uacb0\uacfc\ub97c \ucc3e\uc558\uc2b5\ub2c8\ub2e4",
                expectRtl: false);

            // -- Arabic (RTL) ------------------------------------------------
            H.ClickButton("ar-SA");
            await Harness.Render();

            CheckLocale("ar-SA",
                "\u062a\u0637\u0628\u064a\u0642\u064a",
                "\u0645\u0631\u062d\u0628\u0627\u064b \u0628\u0643 \u0641\u064a \u0627\u0644\u062a\u0637\u0628\u064a\u0642",
                "\u0645\u0631\u062d\u0628\u0627\u064b\u060c World!",
                "\u0644\u0627 \u0639\u0646\u0627\u0635\u0631", null,
                null, null, null,
                expectRtl: true);

            H.Check("Loc_ar-SA_FlowDirection", () =>
            {
                var panel = H.FindControl<StackPanel>(sp =>
                    sp.Tag is string tag && tag == "ContentRoot");
                return panel is not null && panel.FlowDirection == FlowDirection.RightToLeft;
            });

            // -- Japanese ----------------------------------------------------
            H.ClickButton("ja-JP");
            await Harness.Render();

            CheckLocale("ja-JP",
                "\u30de\u30a4\u30a2\u30d7\u30ea\u30b1\u30fc\u30b7\u30e7\u30f3",
                "\u30a2\u30d7\u30ea\u3078\u3088\u3046\u3053\u305d",
                "\u3053\u3093\u306b\u3061\u306f\u3001World\u3055\u3093\uff01",
                "\u30a2\u30a4\u30c6\u30e0\u306a\u3057", null,
                null, null, null,
                expectRtl: false);

            // -- German ------------------------------------------------------
            H.ClickButton("de-DE");
            await Harness.Render();

            CheckLocale("de-DE",
                "Meine Anwendung",
                "Willkommen in der App",
                "Hallo, World!",
                "Keine Eintr\u00e4ge", "5 Eintr\u00e4ge",
                "Keine Ergebnisse f\u00fcr \"test\" gefunden",
                "1 Ergebnis f\u00fcr \"test\" gefunden",
                "42 Ergebnisse f\u00fcr \"test\" gefunden",
                expectRtl: false);

            // -- Back to English - verify round-trip -------------------------
            H.ClickButton("en-US");
            await Harness.Render();

            H.Check("Loc_en-US_RoundTrip_Title",
                H.FindText("My Application") is not null);
            H.Check("Loc_en-US_RoundTrip_LTR", () =>
            {
                var panel = H.FindControl<StackPanel>(sp =>
                    sp.Tag is string tag && tag == "ContentRoot");
                return panel is not null && panel.FlowDirection == FlowDirection.LeftToRight;
            });
        }

        private void CheckLocale(string locale, string title, string welcome,
            string greeting,
            string? pluralZero, string? pluralFive,
            string? searchZero, string? searchOne, string? searchMany,
            bool expectRtl)
        {
            H.Check($"Loc_{locale}_Title",
                H.FindText(title) is not null);
            H.Check($"Loc_{locale}_Welcome",
                H.FindText(welcome) is not null);
            H.Check($"Loc_{locale}_Greeting",
                H.FindText(greeting) is not null);

            if (pluralZero is not null)
                H.Check($"Loc_{locale}_Plural_Zero",
                    H.FindText(pluralZero) is not null);

            if (pluralFive is not null)
                H.Check($"Loc_{locale}_Plural_Five",
                    H.FindText(pluralFive) is not null);

            if (searchZero is not null)
                H.Check($"Loc_{locale}_Search_Zero",
                    H.FindText(searchZero) is not null);

            if (searchOne is not null)
                H.Check($"Loc_{locale}_Search_One",
                    H.FindText(searchOne) is not null);

            if (searchMany is not null)
                H.Check($"Loc_{locale}_Search_Many",
                    H.FindText(searchMany) is not null);

            var expectedDir = expectRtl ? "RTL" : "LTR";
            H.Check($"Loc_{locale}_Direction_{expectedDir}",
                H.FindText(expectedDir) is not null);
        }
    }
}
