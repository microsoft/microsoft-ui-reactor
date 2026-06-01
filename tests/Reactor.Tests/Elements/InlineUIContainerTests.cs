using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Issue #480 — InlineUI factories build a <see cref="RichTextInlineUIContainer"/>
/// inline that can be embedded inside a Reactor <see cref="RichTextBlockElement"/>.
/// Live mount/unmount of the embedded control is exercised by the selftest
/// fixture <c>InlineUIContainer_RouteA_MountsAndUnmounts</c>; these tests
/// cover record shape, factory plumbing, and structural equality which are
/// the contracts the descriptor relies on.
/// </summary>
public class InlineUIContainerTests
{
    [Fact]
    public void InlineUI_Element_Builds_Container_With_Child_Set()
    {
        var btn = Button("ok");
        var iuc = InlineUI(btn);
        Assert.Same(btn, iuc.Child);
        Assert.Null(iuc.Factory);
    }

    [Fact]
    public void InlineUI_Factory_Builds_Container_With_Factory_Set()
    {
        Func<FrameworkElement> factory = () => new ProgressRing();
        var iuc = InlineUI(factory);
        Assert.Same(factory, iuc.Factory);
        Assert.Null(iuc.Child);
    }

    [Fact]
    public void RichTextBlock_Can_Contain_InlineUIContainer()
    {
        var rtb = RichTextBlock(new[]
        {
            Paragraph(Run("before "), InlineUI(Button("ok")), Run(" after"))
        });
        Assert.NotNull(rtb.Paragraphs);
        Assert.Equal(3, rtb.Paragraphs![0].Inlines.Length);
        Assert.IsType<RichTextInlineUIContainer>(rtb.Paragraphs[0].Inlines[1]);
    }

    [Fact]
    public void InlineUIContainer_Record_Equality_Uses_Structural_Equality_For_Element()
    {
        // Same Element reference → equal.
        var btn = Button("ok");
        var a = InlineUI(btn);
        var b = InlineUI(btn);
        Assert.Equal(a, b);

        // Structurally identical (but freshly constructed) elements also
        // compare equal — Element implements structural equality.
        var c = InlineUI(Button("ok"));
        var d = InlineUI(Button("ok"));
        Assert.Equal(c, d);

        // Different label → not equal.
        var e = InlineUI(Button("other"));
        Assert.NotEqual(c, e);
    }

    [Fact]
    public void InlineUIContainer_With_Expression_Preserves_Type()
    {
        var iuc = InlineUI(Button("a")) with { Child = Button("b") };
        Assert.Equal("b", ((ButtonElement)iuc.Child!).Label);
        Assert.IsType<RichTextInlineUIContainer>(iuc);
    }

    [Fact]
    public void InlineUIContainer_Inherits_From_RichTextInline()
    {
        var iuc = InlineUI(Button("ok"));
        Assert.IsAssignableFrom<RichTextInline>(iuc);
    }
}
