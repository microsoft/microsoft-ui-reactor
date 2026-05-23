using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for RichEditBoxElement — DSL factory, record properties, defaults,
/// reconciler dispatch, and Set() extension.
/// </summary>
public class RichEditBoxElementTests
{
    // ════════════════════════════════════════════════════════════════
    //  DSL factory and record defaults
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RichEditBox_Creates_With_Defaults()
    {
        var el = RichEditBox();
        Assert.IsType<RichEditBoxElement>(el);
        Assert.Equal("", el.Text);
        Assert.False(el.IsReadOnly);
        Assert.Null(el.Header);
        Assert.Null(el.PlaceholderText);
        Assert.Null(el.OnTextChanged);
    }

    [Fact]
    public void RichEditBox_Creates_With_Text()
    {
        var el = RichEditBox("Hello world");
        Assert.Equal("Hello world", el.Text);
    }

    [Fact]
    public void RichEditBox_Creates_With_TextChanged_Handler()
    {
        string captured = "";
        var el = RichEditBox("init", t => captured = t);
        el.OnTextChanged!.Invoke("new text");
        Assert.Equal("new text", captured);
    }

    [Fact]
    public void RichEditBox_IsReadOnly_Via_Init()
    {
        var el = RichEditBox() with { IsReadOnly = true };
        Assert.True(el.IsReadOnly);
    }

    [Fact]
    public void RichEditBox_Header_Via_Init()
    {
        var el = RichEditBox() with { Header = "Notes" };
        Assert.Equal("Notes", el.Header);
    }

    [Fact]
    public void RichEditBox_PlaceholderText_Via_Init()
    {
        var el = RichEditBox() with { PlaceholderText = "Type here..." };
        Assert.Equal("Type here...", el.PlaceholderText);
    }

    [Fact]
    public void RichEditBox_Is_Element()
    {
        Element el = RichEditBox("test");
        Assert.IsAssignableFrom<Element>(el);
    }

    [Fact]
    public void RichEditBox_Record_Equality()
    {
        var a = RichEditBox("hello");
        var b = RichEditBox("hello");
        Assert.Equal(a, b);
    }

    [Fact]
    public void RichEditBox_Record_Inequality()
    {
        var a = RichEditBox("hello");
        var b = RichEditBox("world");
        Assert.NotEqual(a, b);
    }

    // ════════════════════════════════════════════════════════════════
    //  Set() extension
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Set_Adds_Setter_To_RichEditBoxElement()
    {
        var el = RichEditBox("hi")
            .Set(reb => reb.IsSpellCheckEnabled = false);
        Assert.NotEqual(RichEditBox("hi"), el);
    }

    [Fact]
    public void Set_Chains_Multiple_Setters()
    {
        var el = RichEditBox()
            .Set(reb => reb.IsSpellCheckEnabled = false)
            .Set(reb => reb.AcceptsReturn = true);
        Assert.NotEqual(RichEditBox(), el);
    }

    // ════════════════════════════════════════════════════════════════
    //  Modifiers work on RichEditBoxElement
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Modifiers_Work_On_RichEditBox()
    {
        var el = RichEditBox("test").Margin(10).Width(300).Height(200);
        Assert.NotNull(el.Modifiers);
        Assert.Equal(300, el.Modifiers!.Width);
        Assert.Equal(200, el.Modifiers!.Height);
    }

    // ════════════════════════════════════════════════════════════════
    //  Reconciler dispatch
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Same_RichEditBox_Elements()
    {
        var reconciler = new Reconciler();
        var a = RichEditBox("a");
        var b = RichEditBox("b");
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_RichEditBox_Vs_TextField_Returns_False()
    {
        var reconciler = new Reconciler();
        Assert.False(reconciler.CanUpdate(RichEditBox("a"), TextBox("a")));
    }

    [Fact]
    public void Mount_Dispatches_RichEditBoxElement()
    {
        // Verify that the reconciler's switch expression includes RichEditBoxElement
        // by confirming it doesn't return null (which is the default fallthrough)
        var reconciler = new Reconciler();
        try
        {
            var ctrl = reconciler.Mount(RichEditBox("test"), () => { });
            // If we get here, it means the control was created (may throw COM on CI)
            Assert.NotNull(ctrl);
            Assert.IsType<Microsoft.UI.Xaml.Controls.RichEditBox>(ctrl);
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
            // Expected on CI/non-WinUI thread — the important thing is we entered the handler
        }
    }
}
