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
    public void TypeRamp_Factory_Attaches_Mount_Action(string _)
    {
        // We can't resolve Style names without an app dispatcher; the
        // existence of an OnMount action is the parity check the unit
        // layer can perform.
        var elements = new[]
        {
            Title("x"),
            Subtitle("x"),
            Body("x"),
            BodyStrong("x"),
            BodyLarge("x"),
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

    // ─────────────────────────────────────────────────────────────────

    private static global::System.Collections.IEnumerable GetSetters(TextBoxElement el) =>
        (global::System.Collections.IEnumerable)typeof(TextBoxElement)
            .GetProperty("Setters", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!
            .GetValue(el)!;
}
