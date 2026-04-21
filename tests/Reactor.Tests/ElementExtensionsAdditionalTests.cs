using System.Numerics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Additional tests for ElementExtensions modifiers that were previously uncovered.
/// Each test verifies that a specific fluent modifier correctly sets the expected
/// property on the element's Modifiers record.
/// </summary>
public class ElementExtensionsAdditionalTests
{
    // ════════════════════════════════════════════════════════════════
    //  Layout modifiers — uncovered overloads
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Margin_Horizontal_Vertical_Overload()
    {
        var el = TextBlock("x").Margin(10.0, 20.0);
        Assert.Equal(new Thickness(10, 20, 10, 20), el.Modifiers!.Margin);
    }

    [Fact]
    public void Center_Sets_Both_Alignments()
    {
        var el = TextBlock("x").Center();
        Assert.Equal(HorizontalAlignment.Center, el.Modifiers!.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, el.Modifiers!.VerticalAlignment);
    }

    [Fact]
    public void RequestedTheme_Sets_Theme()
    {
        var el = TextBlock("x").RequestedTheme(ElementTheme.Dark);
        Assert.Equal(ElementTheme.Dark, el.Modifiers!.RequestedTheme);
    }

    [Fact]
    public void Scale_Vector3_Sets_Scale()
    {
        var el = TextBlock("x").Scale(new Vector3(2, 2, 1));
        Assert.Equal(new Vector3(2, 2, 1), el.Modifiers!.Scale);
    }

    [Fact]
    public void CenterPoint_Sets_CenterPoint()
    {
        var el = TextBlock("x").CenterPoint(new Vector3(50, 50, 0));
        Assert.Equal(new Vector3(50, 50, 0), el.Modifiers!.CenterPoint);
    }

    // ════════════════════════════════════════════════════════════════
    //  Typography modifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FontSize_Sets_Size()
    {
        // Use Button (not TextBlock, which has its own FontSize property)
        var el = Button("x", () => { }).FontSize(24);
        Assert.Equal(24, el.Modifiers!.FontSize);
    }

    [Fact]
    public void FontWeight_Sets_Weight()
    {
        var weight = new global::Windows.UI.Text.FontWeight { Weight = 700 };
        var el = TextBlock("x").FontWeight(weight);
        Assert.Equal(700, el.Modifiers!.FontWeight!.Value.Weight);
    }

    // ════════════════════════════════════════════════════════════════
    //  Event handler modifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void OnSizeChanged_Attaches_Handler()
    {
        Action<object, SizeChangedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnSizeChanged(handler);
        Assert.Same(handler, el.Modifiers!.OnSizeChanged);
    }

    [Fact]
    public void OnPointerMoved_Attaches_Handler()
    {
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPointerMoved(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerMoved);
    }

    [Fact]
    public void OnPointerReleased_Attaches_Handler()
    {
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler = (_, _) => { };
        var el = TextBlock("x").OnPointerReleased(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerReleased);
    }

    // ════════════════════════════════════════════════════════════════
    //  Decoration modifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithContextFlyout_Sets_Flyout()
    {
        var flyout = TextBlock("Menu");
        var el = TextBlock("x").WithContextFlyout(flyout);
        Assert.Same(flyout, el.Modifiers!.ContextFlyout);
    }

    [Fact]
    public void WithToolTip_Sets_RichToolTip()
    {
        var tip = TextBlock("Rich tooltip content");
        var el = TextBlock("x").WithToolTip(tip);
        Assert.Same(tip, el.Modifiers!.RichToolTip);
    }

    // ════════════════════════════════════════════════════════════════
    //  CornerRadius modifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CornerRadius_FourCorners()
    {
        var el = TextBlock("x").CornerRadius(1, 2, 3, 4);
        var cr = el.Modifiers!.CornerRadius!.Value;
        Assert.Equal(1, cr.TopLeft);
        Assert.Equal(2, cr.TopRight);
        Assert.Equal(3, cr.BottomRight);
        Assert.Equal(4, cr.BottomLeft);
    }

    // ════════════════════════════════════════════════════════════════
    //  Set() extensions — strongly-typed native property access
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Set_TextBlock_Appends_Setter()
    {
        var el = TextBlock("hello")
            .Set(tb => tb.TextWrapping = TextWrapping.Wrap);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Button_Appends_Setter()
    {
        var el = Button("click", () => { })
            .Set(b => b.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_HyperlinkButton_Appends_Setter()
    {
        var el = HyperlinkButton("link")
            .Set(hb => hb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_RepeatButton_Appends_Setter()
    {
        var el = RepeatButton("repeat", () => { })
            .Set(rb => rb.Delay = 500);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_ToggleButton_Appends_Setter()
    {
        var el = ToggleButton("toggle")
            .Set(tb => tb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_DropDownButton_Appends_Setter()
    {
        var el = DropDownButton("drop")
            .Set(db => db.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_SplitButton_Appends_Setter()
    {
        var el = SplitButton("split")
            .Set(sb => sb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_ToggleSplitButton_Appends_Setter()
    {
        var el = ToggleSplitButton("tsplit")
            .Set(tb => tb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_RichTextBlock_Appends_Setter()
    {
        var el = RichText("sample")
            .Set(rtb => rtb.IsTextSelectionEnabled = true);
        Assert.Single(el.Setters);
    }

    // ════════════════════════════════════════════════════════════════
    //  Modifier chaining preserves type
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Chained_Modifiers_Preserve_Concrete_Type()
    {
        var el = TextBlock("hello")
            .Margin(8)
            .Padding(4)
            .HAlign(HorizontalAlignment.Center)
            .Opacity(0.5);

        Assert.IsType<TextBlockElement>(el);
        Assert.Equal(new Thickness(8), el.Modifiers!.Margin);
        Assert.Equal(0.5, el.Modifiers!.Opacity);
    }

    [Fact]
    public void Multiple_Setters_Are_Accumulated()
    {
        var el = TextBlock("x")
            .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
            .Set(tb => tb.MaxLines = 3);
        Assert.Equal(2, el.Setters.Length);
    }
}
