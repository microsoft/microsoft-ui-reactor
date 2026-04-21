using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for ElementExtensions accessibility, transition, and interaction-state
/// modifiers that were uncovered.
/// </summary>
public class ElementModifiersCoverageTests
{
    // ════════════════════════════════════════════════════════════════
    //  Accessibility Tier 1 — inline on ElementModifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void HeadingLevel_Sets_AutomationHeadingLevel()
    {
        var el = TextBlock("Title").HeadingLevel(AutomationHeadingLevel.Level3);
        Assert.Equal(AutomationHeadingLevel.Level3, el.Modifiers!.HeadingLevel);
    }

    [Fact]
    public void SoundMode_Sets_ElementSoundMode()
    {
        var el = Button("click", () => { }).SoundMode(ElementSoundMode.Off);
        Assert.Equal(ElementSoundMode.Off, el.Modifiers!.ElementSoundMode);
    }

    [Fact]
    public void OnMount_Sets_MountAction()
    {
        Action<FrameworkElement> action = fe => { };
        var el = TextBlock("x").OnMount(action);
        Assert.Same(action, el.Modifiers!.OnMountAction);
    }

    // ════════════════════════════════════════════════════════════════
    //  Accessibility Tier 2 — on AccessibilityModifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void AccessibilityHidden_Sets_Raw_View()
    {
        var el = TextBlock("decorative").AccessibilityHidden();
        Assert.Equal(AccessibilityView.Raw, el.Modifiers!.Accessibility!.AccessibilityView);
    }

    [Fact]
    public void AccessibilityView_Sets_Value()
    {
        var el = TextBlock("x").AccessibilityView(AccessibilityView.Content);
        Assert.Equal(AccessibilityView.Content, el.Modifiers!.Accessibility!.AccessibilityView);
    }

    // ════════════════════════════════════════════════════════════════
    //  Semantics wrapping
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Semantics_Wraps_With_Role_And_Value()
    {
        var el = TextBlock("★★★").Semantics(
            role: "slider",
            value: "3 of 5 stars",
            rangeMin: 0,
            rangeMax: 5,
            rangeValue: 3);
        Assert.IsType<SemanticElement>(el);
        var sem = (SemanticElement)el;
        Assert.Equal("slider", sem.Semantics.Role);
        Assert.Equal("3 of 5 stars", sem.Semantics.Value);
        Assert.Equal(0, sem.Semantics.RangeMin);
        Assert.Equal(5, sem.Semantics.RangeMax);
        Assert.Equal(3, sem.Semantics.RangeValue);
    }

    // ════════════════════════════════════════════════════════════════
    //  Translation modifier
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Translation_Sets_Vector3()
    {
        var el = TextBlock("x").Translation(10f, 20f, 30f);
        var v = el.Modifiers!.Translation!.Value;
        Assert.Equal(10f, v.X);
        Assert.Equal(20f, v.Y);
        Assert.Equal(30f, v.Z);
    }

    // ════════════════════════════════════════════════════════════════
    //  Animate modifier
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Animate_Sets_AnimationConfig()
    {
        var curve = Curve.Ease(300);
        var el = TextBlock("x").Animate(curve, AnimateProperty.Opacity | AnimateProperty.Scale);
        Assert.NotNull(el.AnimationConfig);
        Assert.Same(curve, el.AnimationConfig!.Curve);
        Assert.Equal(AnimateProperty.Opacity | AnimateProperty.Scale, el.AnimationConfig.Properties);
    }

    // ════════════════════════════════════════════════════════════════
    //  Transition modifier
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Transition_Sets_ElementTransition()
    {
        var transition = new FadeTransition();
        var curve = Curve.Ease(200);
        var el = TextBlock("x").Transition(transition, curve);
        Assert.NotNull(el.ElementTransition);
        Assert.Same(transition, el.ElementTransition!.Transition);
        Assert.Same(curve, el.ElementTransition.Curve);
    }

    [Fact]
    public void Transition_Without_Curve()
    {
        var transition = new FadeTransition();
        var el = TextBlock("x").Transition(transition);
        Assert.NotNull(el.ElementTransition);
        Assert.Null(el.ElementTransition!.Curve);
    }

    // ════════════════════════════════════════════════════════════════
    //  InteractionStates modifier
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void InteractionStates_Sets_Config()
    {
        var el = TextBlock("x").InteractionStates(b => b.PointerOver(opacity: 0.8f));
        Assert.NotNull(el.InteractionStates);
    }

    [Fact]
    public void InteractionStates_With_Curve()
    {
        var curve = Curve.Ease(150);
        var el = TextBlock("x").InteractionStates(
            b => b.PointerOver(opacity: 0.8f),
            curve);
        Assert.NotNull(el.InteractionStates);
        Assert.Same(curve, el.InteractionStates!.Curve);
    }

    // ════════════════════════════════════════════════════════════════
    //  Popup modifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Popup_LightDismiss()
    {
        var el = new PopupElement(TextBlock("popup content"))
            .LightDismiss(true);
        Assert.True(el.IsLightDismissEnabled);
    }

    [Fact]
    public void Popup_Offset()
    {
        var el = new PopupElement(TextBlock("popup"))
            .Offset(10, 20);
        Assert.Equal(10, el.HorizontalOffset);
        Assert.Equal(20, el.VerticalOffset);
    }

    // ════════════════════════════════════════════════════════════════
    //  Expander Direction modifier
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Expander_Direction_Sets_ExpandDirection()
    {
        var el = Expander("hdr", TextBlock("cnt"))
            .Direction(Microsoft.UI.Xaml.Controls.ExpandDirection.Up);
        Assert.Equal(Microsoft.UI.Xaml.Controls.ExpandDirection.Up, el.ExpandDirection);
    }

    // ════════════════════════════════════════════════════════════════
    //  TitleBar sugar
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TitleBar_Subtitle_Sets_Value()
    {
        var el = TitleBar("App").Subtitle("v1.0");
        Assert.Equal("v1.0", el.Subtitle);
    }

    [Fact]
    public void TitleBar_Set_Appends_Setter()
    {
        var el = TitleBar("App")
            .Set(tb => tb.IsBackButtonVisible = true);
        Assert.Single(el.Setters);
    }

    // ════════════════════════════════════════════════════════════════
    //  RichEditBox Set
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Set_RichEditBox_Appends_Setter()
    {
        var el = RichEditBox("text").Set(reb => reb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    // ════════════════════════════════════════════════════════════════
    //  FontFamily with string overload
    // ════════════════════════════════════════════════════════════════

    // Note: FontFamily(string) and FontFamily(FontFamily) both require WinUI thread
    // to instantiate FontFamily objects — not testable in unit tests.
}
