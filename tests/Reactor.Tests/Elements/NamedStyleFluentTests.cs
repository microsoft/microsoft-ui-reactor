using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 §17 named-style fluent helpers and the <c>Card</c> factory.
/// Verifies the fluent surface produces the expected element state without
/// requiring a UI thread (i.e. inspecting modifiers / init properties on
/// the returned record, not the realized control).
/// </summary>
public class NamedStyleFluentTests
{
    // ── §17.1 Button styles ───────────────────────────────────────────

    [Fact]
    public void AccentButton_Attaches_Mount_Action()
    {
        // Style application uses .ApplyStyle, which wires an OnMount action.
        // We can't resolve the actual Style without an app dispatcher, but
        // the modifier presence is a sufficient parity check.
        var el = Button("Save").AccentButton();
        Assert.NotNull(el.Modifiers?.OnMountAction);
    }

    [Fact]
    public void SubtleButton_Attaches_Mount_Action()
    {
        var el = Button("Cancel").SubtleButton();
        Assert.NotNull(el.Modifiers?.OnMountAction);
    }

    [Fact]
    public void AccentButton_Then_Subtle_LastWriteWins()
    {
        // ApplyStyle is set via OnMountAction. Each call overwrites the
        // previous mount action — last write wins, matching the spec's
        // §2.1 contract.
        var first = Button("X").AccentButton();
        var second = first.SubtleButton();
        Assert.NotSame(first.Modifiers!.OnMountAction, second.Modifiers!.OnMountAction);
    }

    [Fact]
    public void AccentButton_Then_ApplyStyle_LastWriteWins()
    {
        var el = Button("X").AccentButton().ApplyStyle("MyCustomStyle");
        Assert.NotNull(el.Modifiers?.OnMountAction);
    }

    // ── §17.2 TextLink ────────────────────────────────────────────────

    [Fact]
    public void TextLink_On_HyperlinkButton_Attaches_Mount_Action()
    {
        var el = HyperlinkButton("Learn more").TextLink();
        Assert.NotNull(el.Modifiers?.OnMountAction);
    }

    [Fact]
    public void TextLink_On_Button_Attaches_Mount_Action()
    {
        var el = Button("Learn more").TextLink();
        Assert.NotNull(el.Modifiers?.OnMountAction);
    }

    // ── §17.3 InputScope fluents ──────────────────────────────────────

    [Fact]
    public void NumericInput_Adds_Setter()
    {
        var el = TextBox("").NumericInput();
        Assert.NotEmpty(GetSetters(el));
    }

    [Fact]
    public void EmailInput_Adds_Setter()
    {
        var el = TextBox("").EmailInput();
        Assert.NotEmpty(GetSetters(el));
    }

    [Fact]
    public void UrlInput_Adds_Setter()
    {
        var el = TextBox("").UrlInput();
        Assert.NotEmpty(GetSetters(el));
    }

    [Fact]
    public void PhoneInput_Adds_Setter()
    {
        var el = TextBox("").PhoneInput();
        Assert.NotEmpty(GetSetters(el));
    }

    [Fact]
    public void SearchInput_Adds_Setter()
    {
        var el = TextBox("").SearchInput();
        Assert.NotEmpty(GetSetters(el));
    }

    [Fact]
    public void Generic_InputScope_Adds_Setter()
    {
        var el = TextBox("").InputScope(Microsoft.UI.Xaml.Input.InputScopeNameValue.Chat);
        Assert.NotEmpty(GetSetters(el));
    }

    // ── §17.4 InfoBar severity ────────────────────────────────────────

    [Theory]
    [InlineData(nameof(InfoBarSeverity.Informational), InfoBarSeverity.Informational)]
    [InlineData(nameof(InfoBarSeverity.Success), InfoBarSeverity.Success)]
    [InlineData(nameof(InfoBarSeverity.Warning), InfoBarSeverity.Warning)]
    [InlineData(nameof(InfoBarSeverity.Error), InfoBarSeverity.Error)]
    public void InfoBar_Severity_Fluents_Map_1To1(string label, InfoBarSeverity expected)
    {
        _ = label; // used only for test naming
        var el = expected switch
        {
            InfoBarSeverity.Informational => InfoBar().Informational(),
            InfoBarSeverity.Success       => InfoBar().Success(),
            InfoBarSeverity.Warning       => InfoBar().Warning(),
            InfoBarSeverity.Error         => InfoBar().Error(),
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(expected, el.Severity);
    }

    [Fact]
    public void InfoBar_Severity_LastWriteWins()
    {
        var el = InfoBar().Error().Success();
        Assert.Equal(InfoBarSeverity.Success, el.Severity);
    }

    // ── §17.5 Card factory ────────────────────────────────────────────

    [Fact]
    public void Card_Wraps_Child_In_Preset_Border()
    {
        var child = TextBlock("hi");
        var el = Card(child);
        Assert.Same(child, el.Child);
        Assert.Equal(new Microsoft.UI.Xaml.CornerRadius(8), el.Modifiers?.CornerRadius);
        Assert.NotNull(el.Modifiers?.Padding);
    }

    [Fact]
    public void Card_Override_Padding_LastWriteWins()
    {
        var el = Card(TextBlock("x")).Padding(24);
        Assert.Equal(new Microsoft.UI.Xaml.Thickness(24), el.Modifiers!.Padding);
    }

    [Fact]
    public void Card_Override_CornerRadius_LastWriteWins()
    {
        var el = Card(TextBlock("x")).CornerRadius(16);
        Assert.Equal(new Microsoft.UI.Xaml.CornerRadius(16), el.Modifiers!.CornerRadius);
    }

    // ── §14 #6 / §17.6 Type-ramp factories ────────────────────────────

    [Theory]
    [InlineData("TitleTextBlockStyle")]
    [InlineData("SubtitleTextBlockStyle")]
    [InlineData("BodyTextBlockStyle")]
    [InlineData("BodyStrongTextBlockStyle")]
    [InlineData("BodyLargeTextBlockStyle")]
    [InlineData("TitleLargeTextBlockStyle")]
    [InlineData("DisplayTextBlockStyle")]
    public void TypeRamp_Factory_Attaches_Mount_Action(string _)
    {
        // We can't resolve Style names without an app dispatcher; the
        // existence of an OnMount action is the parity check the unit
        // layer can perform. That the keys actually resolve to a Style —
        // and to the right one — is covered by the NamedStyleResolution
        // selftest fixture, which mounts these against real controls.
        var elements = new[]
        {
            Title("x"),
            Subtitle("x"),
            Body("x"),
            BodyStrong("x"),
            BodyLarge("x"),
            TitleLarge("x"),
            Display("x"),
        };
        foreach (var el in elements)
        {
            Assert.NotNull(el.Modifiers?.OnMountAction);
        }
    }

    [Fact]
    public void Body_Returns_TextBlockElement_Not_New_Type()
    {
        // Spec §17.6: type-ramp returns TextBlockElement so all TextBlock
        // fluents continue to chain (FontSize, FontFamily, etc.). Avoid
        // .Bold() in unit tests — it touches WinRT FontWeights which needs
        // an apartment-init.
        var el = Body("x").FontSize(20);
        Assert.IsType<TextBlockElement>(el);
        Assert.Equal(20, el.FontSize);
    }

    [Fact]
    public void TitleLarge_Returns_TextBlockElement_Not_New_Type()
    {
        var el = TitleLarge("x").FontSize(20);
        Assert.IsType<TextBlockElement>(el);
        Assert.Equal(20, el.FontSize);
    }

    [Fact]
    public void Display_Returns_TextBlockElement_Not_New_Type()
    {
        var el = Display("x").FontSize(20);
        Assert.IsType<TextBlockElement>(el);
        Assert.Equal(20, el.FontSize);
    }

    [Fact]
    public void TypeRamp_Factories_Use_Distinct_Style_Keys()
    {
        // Each factory must attach its OWN cached applier delegate. StyleApplier
        // caches one delegate per style name, so two factories that (say) both
        // pasted "TitleTextBlockStyle" would share a delegate instance and render
        // identically — a copy/paste slip this assertion catches without needing
        // a live control. Point TitleLarge or Display at an existing key and
        // this reddens.
        var actions = new[]
        {
            ("Title", Title("x").Modifiers!.OnMountAction),
            ("Subtitle", Subtitle("x").Modifiers!.OnMountAction),
            ("Body", Body("x").Modifiers!.OnMountAction),
            ("BodyStrong", BodyStrong("x").Modifiers!.OnMountAction),
            ("BodyLarge", BodyLarge("x").Modifiers!.OnMountAction),
            ("TitleLarge", TitleLarge("x").Modifiers!.OnMountAction),
            ("Display", Display("x").Modifiers!.OnMountAction),
        };

        var seen = new global::System.Collections.Generic.HashSet<object>();
        foreach (var (name, action) in actions)
        {
            Assert.NotNull(action);
            Assert.True(seen.Add(action!), $"{name} shares its style-applier delegate with an earlier factory — duplicate style key.");
        }
    }

    [Fact]
    public void ApplyStyle_DoesNotRetainOverlongStyleNames()
    {
        // The applier cache bounds how many delegates it keeps but not how long
        // each captured key is, so an overlong name must bypass it entirely —
        // otherwise a data-driven caller can root arbitrarily large strings for
        // the process lifetime. Cache identity is the observable: a cached key
        // hands back the same delegate instance, an uncached one does not.
        var longKey = "L" + new string('x', 400);
        var longA = TextBlock("a").ApplyStyle(longKey).Modifiers!.OnMountAction;
        var longB = TextBlock("b").ApplyStyle(longKey).Modifiers!.OnMountAction;
        Assert.NotNull(longA);
        Assert.NotSame(longA, longB);

        // Positive control: without this, the assertion above would also pass if
        // the cache were broken outright, which would silently regress #174.
        const string shortKey = "ShortStyleKey_ForApplierCacheTest";
        var shortA = TextBlock("c").ApplyStyle(shortKey).Modifiers!.OnMountAction;
        var shortB = TextBlock("d").ApplyStyle(shortKey).Modifiers!.OnMountAction;
        Assert.Same(shortA, shortB);
    }

    // ─────────────────────────────────────────────────────────────────

    private static global::System.Collections.IEnumerable GetSetters(TextBoxElement el) =>
        (global::System.Collections.IEnumerable)typeof(TextBoxElement)
            .GetProperty("Setters", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!
            .GetValue(el)!;
}
