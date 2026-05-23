using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 Phase 11.6 — sample-page construction smoke.
///
/// The full Phase 11.6 ask (mount each Phase-8 gallery page under
/// Light/Dark/NightSky and verify callbacks fire) requires the
/// <c>Reactor.AppTests</c> harness. The page classes themselves are
/// <c>internal</c> WinExe-bound types so importing them into a test
/// project is heavyweight.
///
/// In the meantime, this fixture replicates the *factory + fluent*
/// surface the Phase 8 pages exercise and asserts construction does not
/// throw. Catches obvious typos and missing factories — the same defect
/// class the page-mount tests would catch — without the harness cost.
/// One [Fact] per page; one assertion per fluent chain.
///
/// Phase 8 page inventory:
///   - ButtonPage           — .Click / .AccentButton / .SubtleButton / .TextLink
///   - TextFieldPage        — .NumericInput / .EmailInput / .UrlInput / .Description
///   - InfoBarPage          — .Informational / .Success / .Warning / .Error
///   - TypeRampPage         — Title / Subtitle / Body / BodyStrong / BodyLarge
///   - CardPage             — Card(child)
///   - CalendarViewPage     — CalendarView().SelectedDatesChanged(...)
///   - FrameNavigationPage  — Frame.Navigated / Navigating / NavigationFailed
/// </summary>
public class GallerySampleConstructionSmokeTests
{
    [Fact]
    public void ButtonPage_FluentChains_Construct()
    {
        // Mirrors the four sample cards in ButtonPage.cs.
        var basic = Button("Save").Click(() => { });
        var accent = Button("Confirm").Click(() => { }).AccentButton();
        var subtle = Button("Cancel").Click(() => { }).SubtleButton();
        var textLink = Button("Learn more").Click(() => { }).TextLink();

        Assert.NotNull(basic.OnClick);
        Assert.NotNull(accent.Modifiers?.OnMountAction);
        Assert.NotNull(subtle.Modifiers?.OnMountAction);
        Assert.NotNull(textLink.Modifiers?.OnMountAction);
    }

    [Fact]
    public void TextFieldPage_FluentChains_Construct()
    {
        // Mirrors the InputScope + Description chains in TextFieldPage.cs.
        var numeric = TextBox("", _ => { }, "0").Header("Qty").NumericInput().Description("hint");
        var email = TextBox("", _ => { }, "you@x.com").Header("Email").EmailInput();
        var url = TextBox("", _ => { }, "https://").Header("URL").UrlInput();
        Assert.NotNull(numeric);
        Assert.NotNull(email);
        Assert.NotNull(url);
    }

    [Fact]
    public void InfoBarPage_SeverityFluents_Construct()
    {
        // Mirrors the four InfoBarPage cards.
        var info = InfoBar().Informational();
        var success = InfoBar().Success();
        var warning = InfoBar().Warning();
        var error = InfoBar().Error();

        Assert.Equal(InfoBarSeverity.Informational, info.Severity);
        Assert.Equal(InfoBarSeverity.Success, success.Severity);
        Assert.Equal(InfoBarSeverity.Warning, warning.Severity);
        Assert.Equal(InfoBarSeverity.Error, error.Severity);
    }

    [Fact]
    public void TypeRampPage_AllFactories_Construct()
    {
        // Mirrors TypeRampPage's five WinUI-3 type-ramp factories.
        var elements = new TextBlockElement[]
        {
            Title("Title"),
            Subtitle("Subtitle"),
            Body("Body"),
            BodyStrong("BodyStrong"),
            BodyLarge("BodyLarge"),
        };
        foreach (var el in elements)
            Assert.NotNull(el.Modifiers?.OnMountAction);
    }

    [Fact]
    public void CardPage_CardFactory_Constructs_With_ThemeBindings()
    {
        // Mirrors CardPage.cs's mailbox card pattern.
        var el = Card(
            HStack(12,
                TextBlock("M").FontSize(20),
                VStack(4,
                    BodyStrong("Inbox"),
                    Body("3 unread"))));

        Assert.NotNull(el.ThemeBindings);
        Assert.Contains("Background", el.ThemeBindings!.Keys);
        Assert.Contains("BorderBrush", el.ThemeBindings.Keys);
    }

    [Fact]
    public void CalendarViewPage_MultiSelectFluent_Constructs()
    {
        // Mirrors CalendarViewPage.cs's multi-select wiring (Phase 5.8).
        IReadOnlyList<DateTimeOffset>? captured = null;
        var el = CalendarView().SelectedDatesChanged(dates => captured = dates);

        Assert.NotNull(el.OnSelectedDatesChanged);
        // Smoke: invoking the handler bridges captured (the page's reducer
        // would replace this with a snapshot write).
        el.OnSelectedDatesChanged!(new[] { DateTimeOffset.Now });
        Assert.NotNull(captured);
    }

    [Fact]
    public void FrameNavigationPage_FrameEventChain_Constructs()
    {
        // Mirrors FrameNavigationPage.cs's Frame fluent chain.
        var log = new List<string>();
        var el = Frame(typeof(GallerySampleConstructionSmokeTests))
            .Navigating(t => log.Add("Navigating " + t.Name))
            .Navigated(t => log.Add("Navigated " + t.Name))
            .NavigationFailed((t, ex) => log.Add($"Failed {t.Name}: {ex.Message}"));

        Assert.NotNull(el.OnNavigating);
        Assert.NotNull(el.OnNavigated);
        Assert.NotNull(el.OnNavigationFailed);

        // Invoke each handler once to catch reference issues in the captured
        // closures (e.g. a typo'd parameter type would surface as a compile
        // error here, but a logic typo in chained captures would not).
        el.OnNavigating!(typeof(GallerySampleConstructionSmokeTests));
        el.OnNavigated!(typeof(GallerySampleConstructionSmokeTests));
        el.OnNavigationFailed!(typeof(GallerySampleConstructionSmokeTests), new InvalidOperationException("x"));
        Assert.Equal(3, log.Count);
    }
}
