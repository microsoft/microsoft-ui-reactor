// Phase 3.1a — symbol-binding tests. Spec 038 §3.1a.

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class RuleSymbolResolverTests
{
    const string Stub = @"
namespace Acme
{
    public sealed class Widget
    {
        public string Label { get; set; } = """";
        public void Click() {}
        public void Click(int times) {}
    }
}";

    [Fact]
    public void ResolveType_returns_the_type_when_present()
    {
        var c = TestCompilation.Create(Stub);
        var r = RuleSymbolResolver.For(c);
        var t = r.ResolveType("Acme.Widget");
        Assert.NotNull(t);
        Assert.Equal("Widget", t!.Name);
    }

    [Fact]
    public void ResolveType_returns_null_when_target_evaporates()
    {
        var c = TestCompilation.Create(Stub);
        var r = RuleSymbolResolver.For(c);
        Assert.Null(r.ResolveType("Acme.MissingType"));
    }

    [Fact]
    public void ResolveType_is_cached_per_compilation()
    {
        var c = TestCompilation.Create(Stub);
        var r1 = RuleSymbolResolver.For(c);
        var r2 = RuleSymbolResolver.For(c);
        Assert.Same(r1, r2);

        var first = r1.ResolveType("Acme.Widget");
        var second = r1.ResolveType("Acme.Widget");
        Assert.Same(first, second);
    }

    [Fact]
    public void Distinct_compilations_get_distinct_resolvers()
    {
        var c1 = TestCompilation.Create(Stub);
        var c2 = TestCompilation.Create(Stub);
        var r1 = RuleSymbolResolver.For(c1);
        var r2 = RuleSymbolResolver.For(c2);
        Assert.NotSame(r1, r2);
    }

    [Fact]
    public void ResolveMethod_returns_first_match_by_name()
    {
        var c = TestCompilation.Create(Stub);
        var r = RuleSymbolResolver.For(c);
        var widget = r.ResolveType("Acme.Widget")!;
        var click = r.ResolveMethod(widget, "Click");
        Assert.NotNull(click);
        Assert.Equal("Click", click!.Name);
    }

    [Fact]
    public void ResolveMethod_returns_null_when_method_absent()
    {
        var c = TestCompilation.Create(Stub);
        var r = RuleSymbolResolver.For(c);
        var widget = r.ResolveType("Acme.Widget")!;
        Assert.Null(r.ResolveMethod(widget, "DoesNotExist"));
    }

    [Fact]
    public void ResolveMember_returns_non_method_member()
    {
        var c = TestCompilation.Create(Stub);
        var r = RuleSymbolResolver.For(c);
        var widget = r.ResolveType("Acme.Widget")!;
        var label = r.ResolveMember(widget, "Label");
        Assert.NotNull(label);
        Assert.Equal("Label", label!.Name);
    }
}
