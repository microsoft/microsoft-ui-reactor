using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// The component name <c>RenderError</c> (and the other per-component events) report.
/// Before the fix the error path named the ELEMENT, so every class component reported
/// <c>ComponentElement</c> — and <c>ComponentElement`1</c> for <c>Component&lt;T, TProps&gt;</c>
/// — and an inspector could not tell which component threw.
///
/// <para>The element records and component instances here are pure managed objects, so
/// this runs headless; the reconciler/host wiring that calls it is covered by the
/// <c>RenderErrorNames_*</c> selftest fixture.</para>
/// </summary>
public sealed class ComponentNamesTests
{
    private sealed record CounterProps(int Start);

    private sealed class Counter : Component<CounterProps>
    {
        public override Element Render() => TextBlock($"{Props.Start}");
    }

    private sealed class Propless : Component
    {
        public override Element Render() => TextBlock("x");
    }

    private sealed class GenericList<TItem> : Component<IReadOnlyList<TItem>>
    {
        public override Element Render() => TextBlock("list");
    }

    private sealed class Outer<TA>
    {
        public sealed class Inner<TB> : Component
        {
            public override Element Render() => TextBlock("inner");
        }
    }

    [Fact]
    public void GenericName_IsBuiltOnceAndReused()
    {
        // The name is asked for on every traced render and unmount; a generic name is
        // built, so it is cached per type rather than reallocated on each call.
        var first = ComponentNames.For(typeof(GenericList<int>));
        var second = ComponentNames.For(typeof(GenericList<int>));

        Assert.Equal("GenericList<Int32>", first);
        Assert.Same(first, second);
    }

    [Fact]
    public void PropsComponentElement_NamesTheComponentNotTheElement()
    {
        // Component<T, TProps>() builds a ComponentElement<TProps>: the reported case.
        var element = Component<Counter, CounterProps>(new CounterProps(1));
        Assert.Equal("ComponentElement`1", element.GetType().Name); // the old, wrong answer

        Assert.Equal("Counter", ComponentNames.For(instance: null, element));
    }

    [Fact]
    public void ProplessComponentElement_NamesTheComponent()
    {
        var element = Component<Propless>();
        Assert.Equal("ComponentElement", element.GetType().Name);

        Assert.Equal("Propless", ComponentNames.For(instance: null, element));
    }

    [Fact]
    public void LiveInstance_WinsOverTheElement()
    {
        // After a hot-reload type swap the node's instance is the edited type while the
        // element still names the old one; the instance is what actually rendered.
        var element = Component<Propless>();

        Assert.Equal("Counter", ComponentNames.For(new Counter(), element));
    }

    [Fact]
    public void RootInstanceWithoutElement_IsNamed()
        => Assert.Equal("Propless", ComponentNames.For(new Propless(), element: null));

    [Fact]
    public void FunctionAndMemoComponents_KeepTheirElementNames()
    {
        Assert.Equal("FuncElement", ComponentNames.For(null, new FuncElement(_ => TextBlock("f"))));
        Assert.Equal("MemoElement", ComponentNames.For(null, Memo(_ => TextBlock("m"), 1)));
    }

    [Fact]
    public void NonGenericName_IsTheSimpleTypeNameAsBefore()
    {
        // Non-generic components report exactly Type.Name, so existing traces and greps
        // keyed on ComponentRenderStart/ComponentUnmount names do not change.
        Assert.Equal(typeof(Counter).Name, ComponentNames.For(typeof(Counter)));
    }

    [Fact]
    public void GenericComponent_RendersArgumentsInsteadOfTheArity()
    {
        Assert.Equal("GenericList<Int32>", ComponentNames.For(typeof(GenericList<int>)));
        Assert.Equal("GenericList<KeyValuePair<String, Int32>>",
            ComponentNames.For(typeof(GenericList<KeyValuePair<string, int>>)));
    }

    [Fact]
    public void NestedGeneric_ReportsOnlyItsOwnArguments()
    {
        Assert.Equal("Inner<String>", ComponentNames.For(typeof(Outer<int>.Inner<string>)));
    }

    [Fact]
    public void NullEverything_IsUnknownRatherThanAThrow()
        => Assert.Equal("unknown", ComponentNames.For(null, null));
}
