using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

public class ModifierReferenceFluentTests
{
    [Fact]
    public void LabeledBy_Fluent_Sets_Accessibility_Reference_Slot()
    {
        var typed = TypedElementRef.Create<FrameworkElement>();
        ElementRef target = typed;

        var el = Button("input", () => { }).LabeledBy(typed);

        Assert.Same(target, el.Modifiers?.Accessibility?.LabeledByRef);
    }

    [Fact]
    public void DescribedBy_Fluent_Preserves_Declaration_Order()
    {
        var first = TypedElementRef.Create<FrameworkElement>();
        var second = TypedElementRef.Create<FrameworkElement>();
        ElementRef firstCell = first;
        ElementRef secondCell = second;

        var el = Button("input", () => { }).DescribedBy(first, second);

        Assert.Equal(new[] { firstCell, secondCell }, el.Modifiers?.Accessibility?.DescribedByRefs);
    }

    [Fact]
    public void XYFocusRight_Fluent_Sets_Modifier_Reference_Slot()
    {
        var typed = TypedElementRef.Create<FrameworkElement>();
        ElementRef target = typed;

        var el = Button("left", () => { }).XYFocusRight(typed);

        Assert.Same(target, el.Modifiers?.XYFocusRightRef);
    }

    [Fact]
    public void Typed_Button_Ref_Is_Accepted_By_TeachingTip_Target_Factory()
    {
        var typed = TypedElementRef.Create<WinUI.Button>();
        ElementRef target = typed;

        var el = TeachingTip("tip", target: typed);

        Assert.Same(target, el.Target);
    }

    [Fact]
    public void Typed_Button_Ref_Is_Accepted_By_TeachingTip_Target_Fluent()
    {
        var typed = TypedElementRef.Create<WinUI.Button>();
        ElementRef target = typed;

        var el = TeachingTip("tip").Target(typed);

        Assert.Same(target, el.Target);
    }

    [Fact]
    public void Typed_Button_Ref_Is_Accepted_By_XYFocus_Fluents()
    {
        var typed = TypedElementRef.Create<WinUI.Button>();
        ElementRef target = typed;

        var el = Button("left", () => { })
            .XYFocusUp(typed)
            .XYFocusDown(typed)
            .XYFocusLeft(typed)
            .XYFocusRight(typed);

        Assert.Same(target, el.Modifiers?.XYFocusUpRef);
        Assert.Same(target, el.Modifiers?.XYFocusDownRef);
        Assert.Same(target, el.Modifiers?.XYFocusLeftRef);
        Assert.Same(target, el.Modifiers?.XYFocusRightRef);
    }

    [Fact]
    public void Typed_Button_Ref_Is_Accepted_By_Automation_Relationship_Fluents()
    {
        var typed = TypedElementRef.Create<WinUI.Button>();
        ElementRef target = typed;

        var el = Button("input", () => { })
            .LabeledBy(typed)
            .DescribedBy(typed)
            .FlowsTo(typed)
            .FlowsFrom(typed);

        var accessibility = el.Modifiers?.Accessibility;
        Assert.Same(target, accessibility?.LabeledByRef);
        Assert.Equal(new[] { target }, accessibility?.DescribedByRefs);
        Assert.Equal(new[] { target }, accessibility?.FlowsToRefs);
        Assert.Equal(new[] { target }, accessibility?.FlowsFromRefs);
    }
}
