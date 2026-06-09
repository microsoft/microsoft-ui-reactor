using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

public class ModifierReferenceFluentTests
{
    [Fact]
    public void LabeledBy_Fluent_Sets_Accessibility_Reference_Slot()
    {
        var target = TypedElementRef.Create<FrameworkElement>();

        var el = Button("input", () => { }).LabeledBy(target);

        Assert.Same(target, el.Modifiers?.Accessibility?.LabeledByRef);
    }

    [Fact]
    public void DescribedBy_Fluent_Preserves_Declaration_Order()
    {
        var first = TypedElementRef.Create<FrameworkElement>();
        var second = TypedElementRef.Create<FrameworkElement>();

        var el = Button("input", () => { }).DescribedBy(first, second);

        Assert.Equal(new[] { first, second }, el.Modifiers?.Accessibility?.DescribedByRefs);
    }

    [Fact]
    public void XYFocusRight_Fluent_Sets_Modifier_Reference_Slot()
    {
        var target = TypedElementRef.Create<FrameworkElement>();

        var el = Button("left", () => { }).XYFocusRight(target);

        Assert.Same(target, el.Modifiers?.XYFocusRightRef);
    }
}
