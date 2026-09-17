using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Feature 9: Component Props System.
/// Validates Component&lt;TProps&gt; receives typed props and the DSL supports props.
/// Pure logic tests — no WinUI control creation.
/// </summary>
public class ComponentPropsTests
{
    private class GreetingComponent : Component<string>
    {
        public override Element Render()
        {
            return new TextBlockElement($"Hello, {Props}!");
        }
    }

    private class CounterPropsComponent : Component<int>
    {
        public override Element Render()
        {
            return new TextBlockElement($"Count: {Props}");
        }
    }

    private class NoPropsComponent : Component
    {
        public override Element Render() => new TextBlockElement("no props");
    }

    [Fact]
    public void Component_TProps_Has_Props_Property()
    {
        var comp = new GreetingComponent();
        comp.Props = "World";
        Assert.Equal("World", comp.Props);
    }

    // This test is source-backed into docs/guide/testing.md as the structural-assertion
    // example, so it deliberately stays on Reactor's *public* surface. GreetingComponent
    // renders without hooks, so the internal `Context.BeginRender` lifecycle call its
    // siblings make is unnecessary here — and including it would advertise an internal
    // API that only compiles inside this repo via InternalsVisibleTo.
    // <snippet:structural-render-assertion>
    [Fact]
    public void Component_TProps_Renders_With_Props()
    {
        var comp = new GreetingComponent { Props = "Alice" };
        var el = comp.Render();
        Assert.IsType<TextBlockElement>(el);
        Assert.Equal("Hello, Alice!", ((TextBlockElement)el).Content);
    }
    // </snippet:structural-render-assertion>

    [Fact]
    public void Component_TProps_Int_Props()
    {
        var comp = new CounterPropsComponent { Props = 42 };
        comp.Context.BeginRender(() => { });
        var el = comp.Render();
        Assert.Equal("Count: 42", ((TextBlockElement)el).Content);
    }

    [Fact]
    public void ComponentElement_With_Props_Stores_Props()
    {
        var el = new ComponentElement(typeof(GreetingComponent), "Bob");
        Assert.Equal("Bob", el.Props);
        Assert.Equal(typeof(GreetingComponent), el.ComponentType);
    }

    [Fact]
    public void ComponentElement_Without_Props_Has_Null_Props()
    {
        var el = new ComponentElement(typeof(NoPropsComponent));
        Assert.Null(el.Props);
    }

    [Fact]
    public void DSL_Component_With_Props_Creates_ComponentElement()
    {
        var el = Component<GreetingComponent, string>("Charlie");
        Assert.Equal(typeof(GreetingComponent), el.ComponentType);
        Assert.Equal("Charlie", el.Props);
    }

    [Fact]
    public void DSL_Component_Without_Props_Creates_ComponentElement()
    {
        var el = Component<NoPropsComponent>();
        Assert.Equal(typeof(NoPropsComponent), el.ComponentType);
        Assert.Null(el.Props);
    }

    [Fact]
    public void Component_TProps_Inherits_From_Component()
    {
        Component comp = new GreetingComponent { Props = "Test" };
        Assert.IsAssignableFrom<Component>(comp);
    }

    [Fact]
    public void Component_TProps_Default_Props_Is_Default()
    {
        var comp = new CounterPropsComponent();
        Assert.Equal(0, comp.Props); // default(int)

        var strComp = new GreetingComponent();
        Assert.Null(strComp.Props); // default(string)
    }

    [Fact]
    public void Props_Set_Via_IPropsReceiver_Works()
    {
        // This is how the reconciler sets props (via IPropsReceiver, no reflection)
        var comp = new GreetingComponent();
        Assert.IsAssignableFrom<IPropsReceiver>(comp);
        ((IPropsReceiver)comp).SetProps("ReceiverValue");
        Assert.Equal("ReceiverValue", comp.Props);
    }

    [Fact]
    public void ComponentElement_With_Props_Record_Equality()
    {
        var a = new ComponentElement(typeof(GreetingComponent), "Same");
        var b = new ComponentElement(typeof(GreetingComponent), "Same");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComponentElement_With_Different_Props_Not_Equal()
    {
        var a = new ComponentElement(typeof(GreetingComponent), "A");
        var b = new ComponentElement(typeof(GreetingComponent), "B");
        Assert.NotEqual(a, b);
    }
}
