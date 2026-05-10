// Tier-2 FactoryIndex tests. Spec 038 §1.3.

using Microsoft.UI.Reactor.Cli.Check;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class FactoryIndexTests
{
    const string FactoriesStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public sealed class ButtonElement { }
    public abstract class Element { }
    public sealed class TextBlockElement : Element { }
}

namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    using System;

    public static class Factories
    {
        public static ButtonElement Button(string label, Action? onClick = null) => new();
        public static ButtonElement Button(Element content, Action? onClick = null) => new();
        public static ButtonElement Button(int command) => new();

        public static TextBlockElement TextBlock(string content) => new();
        public static TextBlockElement Heading(string content) => new();
        public static TextBlockElement Caption(string content) => new();
    }
}";

    [Fact]
    public void Build_returns_Empty_when_compilation_does_not_reference_Reactor()
    {
        var c = TestCompilation.Create("class X {}");
        var idx = FactoryIndex.Build(c);
        Assert.Same(FactoryIndex.Empty, idx);
    }

    [Fact]
    public void Indexes_Button_with_three_overloads()
    {
        var c = TestCompilation.Create(FactoriesStub);
        var idx = FactoryIndex.Build(c);

        Assert.True(idx.ByName.TryGetValue("Button", out var buttons));
        Assert.Equal(3, buttons!.Count);
    }

    [Fact]
    public void At_least_one_Button_overload_has_an_onClick_parameter()
    {
        var c = TestCompilation.Create(FactoriesStub);
        var idx = FactoryIndex.Build(c);

        var hit = idx.ByName["Button"]
            .SelectMany(o => o.ParameterNames)
            .Any(n => n == "onClick");
        Assert.True(hit);
    }

    [Fact]
    public void TryFindParameter_finds_named_argument_target()
    {
        var c = TestCompilation.Create(FactoriesStub);
        var idx = FactoryIndex.Build(c);

        Assert.True(idx.TryFindParameter("onClick", out var owner, out var p));
        Assert.Equal("Button", owner.Method.Name);
        Assert.Equal("onClick", p.Name);

        Assert.False(idx.TryFindParameter("notARealParameter", out _, out _));
    }

    [Fact]
    public void Includes_only_public_static_methods()
    {
        var src = @"
namespace Microsoft.UI.Reactor
{
    public static class Factories
    {
        public static int Public(int x) => 0;
        internal static int Internal(int x) => 0;
        private static int Private(int x) => 0;
    }
}";
        var c = TestCompilation.Create(src);
        var idx = FactoryIndex.Build(c);

        Assert.True(idx.ByName.ContainsKey("Public"));
        Assert.False(idx.ByName.ContainsKey("Internal"));
        Assert.False(idx.ByName.ContainsKey("Private"));
    }
}
