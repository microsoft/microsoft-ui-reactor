using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the Phase 1 pointer/tap/keyboard/focus modifier extensions
/// (spec 027 §Tier 1). Each new .On* modifier should populate the matching
/// ElementModifiers field and preserve previously-set fields when chained.
/// </summary>
public class InputModifierExtensionsTests
{
    // ── Pointer lifecycle ───────────────────────────────────────────

    [Fact]
    public void OnPointerEntered_SetsField()
    {
        Action<object, PointerRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPointerEntered(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerEntered);
    }

    [Fact]
    public void OnPointerExited_SetsField()
    {
        Action<object, PointerRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPointerExited(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerExited);
    }

    [Fact]
    public void OnPointerCanceled_SetsField()
    {
        Action<object, PointerRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPointerCanceled(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerCanceled);
    }

    [Fact]
    public void OnPointerCaptureLost_SetsField()
    {
        Action<object, PointerRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPointerCaptureLost(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerCaptureLost);
    }

    [Fact]
    public void OnPointerWheelChanged_SetsField()
    {
        Action<object, PointerRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPointerWheelChanged(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerWheelChanged);
    }

    // ── Tap family ──────────────────────────────────────────────────

    [Fact]
    public void OnDoubleTapped_SetsField()
    {
        Action<object, DoubleTappedRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnDoubleTapped(handler);
        Assert.Same(handler, el.Modifiers!.OnDoubleTapped);
    }

    [Fact]
    public void OnRightTapped_SetsField()
    {
        Action<object, RightTappedRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnRightTapped(handler);
        Assert.Same(handler, el.Modifiers!.OnRightTapped);
    }

    [Fact]
    public void OnHolding_SetsField()
    {
        Action<object, HoldingRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnHolding(handler);
        Assert.Same(handler, el.Modifiers!.OnHolding);
    }

    // ── Keyboard ────────────────────────────────────────────────────

    [Fact]
    public void OnKeyUp_SetsField()
    {
        Action<object, KeyRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnKeyUp(handler);
        Assert.Same(handler, el.Modifiers!.OnKeyUp);
    }

    [Fact]
    public void OnPreviewKeyDown_SetsField()
    {
        Action<object, KeyRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPreviewKeyDown(handler);
        Assert.Same(handler, el.Modifiers!.OnPreviewKeyDown);
    }

    [Fact]
    public void OnPreviewKeyUp_SetsField()
    {
        Action<object, KeyRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPreviewKeyUp(handler);
        Assert.Same(handler, el.Modifiers!.OnPreviewKeyUp);
    }

    [Fact]
    public void OnCharacterReceived_SetsField()
    {
        Action<UIElement, CharacterReceivedRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnCharacterReceived(handler);
        Assert.Same(handler, el.Modifiers!.OnCharacterReceived);
    }

    // ── Focus ───────────────────────────────────────────────────────

    [Fact]
    public void OnGotFocus_SetsField()
    {
        Action<object, RoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnGotFocus(handler);
        Assert.Same(handler, el.Modifiers!.OnGotFocus);
    }

    [Fact]
    public void OnLostFocus_SetsField()
    {
        Action<object, RoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnLostFocus(handler);
        Assert.Same(handler, el.Modifiers!.OnLostFocus);
    }

    // ── Chaining preserves previously-set fields ────────────────────

    [Fact]
    public void Chained_InputModifiers_Preserve_Each_Other()
    {
        Action<object, PointerRoutedEventArgs> pEnter = (_, _) => { };
        Action<object, PointerRoutedEventArgs> pExit = (_, _) => { };
        Action<object, DoubleTappedRoutedEventArgs> dTap = (_, _) => { };
        Action<object, KeyRoutedEventArgs> keyUp = (_, _) => { };
        Action<object, RoutedEventArgs> gotFocus = (_, _) => { };

        var el = TextBlock("x")
            .OnPointerEntered(pEnter)
            .OnPointerExited(pExit)
            .OnDoubleTapped(dTap)
            .OnKeyUp(keyUp)
            .OnGotFocus(gotFocus);

        Assert.Same(pEnter, el.Modifiers!.OnPointerEntered);
        Assert.Same(pExit, el.Modifiers!.OnPointerExited);
        Assert.Same(dTap, el.Modifiers!.OnDoubleTapped);
        Assert.Same(keyUp, el.Modifiers!.OnKeyUp);
        Assert.Same(gotFocus, el.Modifiers!.OnGotFocus);
    }

    // ── Merge / equality participation ──────────────────────────────

    [Fact]
    public void ElementModifiers_Equality_Considers_New_Fields()
    {
        Action<object, PointerRoutedEventArgs> handler = (_, _) => { };
        var a = new ElementModifiers { OnPointerEntered = handler };
        var b = new ElementModifiers { OnPointerEntered = handler };
        var c = new ElementModifiers { OnPointerEntered = (_, _) => { } };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ElementModifiers_Merge_Prefers_Right_For_New_Fields()
    {
        Action<object, DoubleTappedRoutedEventArgs> left = (_, _) => { };
        Action<object, DoubleTappedRoutedEventArgs> right = (_, _) => { };

        var merged = new ElementModifiers { OnDoubleTapped = left }
            .Merge(new ElementModifiers { OnDoubleTapped = right });

        Assert.Same(right, merged.OnDoubleTapped);
    }

    [Fact]
    public void ElementModifiers_Merge_Keeps_Left_When_Right_Null()
    {
        Action<object, RoutedEventArgs> gotFocus = (_, _) => { };

        var merged = new ElementModifiers { OnGotFocus = gotFocus }
            .Merge(new ElementModifiers());

        Assert.Same(gotFocus, merged.OnGotFocus);
    }
}
