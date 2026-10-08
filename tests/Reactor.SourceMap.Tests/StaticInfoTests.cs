using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Declared names and hook names end to end: the real generator emits the module
/// initializer into this assembly, the runtime builds its tables on first lookup, and an
/// element's <see cref="SourceLocation.DeclaredName"/> / a component's hook string come back
/// out. The line oracle is this file's own text, found by marker comments.
/// </summary>
[Collection("SourceMap")]
public sealed class StaticInfoTests : IDisposable
{
    public StaticInfoTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int LineOf(string marker, [CallerFilePath] string _ = "")
    {
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = global::System.IO.Path.Combine(dir.FullName, "tests", "Reactor.SourceMap.Tests", "StaticInfoTests.cs");
            if (global::System.IO.File.Exists(candidate))
            {
                var lines = global::System.IO.File.ReadAllLines(candidate);
                var hits = Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains("// " + marker, StringComparison.Ordinal)).ToList();
                Assert.True(hits.Count == 1, $"marker '{marker}' must appear exactly once");
                return hits[0] + 1;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("could not locate StaticInfoTests.cs");
    }

    // Constructed inside the test: a field initializer on the test class itself would run
    // before the constructor turns source mapping on.
    private sealed class Holder
    {
        public readonly Element _header = TextBlock("header");

        public Element Footer => TextBlock("footer").Margin(4);
    }

    [Fact]
    public void DeclaredName_Local()
    {
        var title = TextBlock("t");
        Assert.Equal("title", title.CallSite!.Value.DeclaredName);
    }

    [Fact]
    public void DeclaredName_ThroughAFluentChain()
    {
        var bold = TextBlock("b").Margin(2).Padding(3);
        Assert.Equal("bold", bold.CallSite!.Value.DeclaredName);
    }

    [Fact]
    public void DeclaredName_FieldAndExpressionBodiedProperty()
    {
        var holder = new Holder();
        Assert.Equal("_header", holder._header.CallSite!.Value.DeclaredName);
        Assert.Equal("Footer", holder.Footer.CallSite!.Value.DeclaredName);
    }

    [Fact]
    public void DeclaredName_InlineChildIsUnnamed_ContainerIsNamed()
    {
        var stack = VStack(TextBlock("child"));
        var child = global::System.Linq.Enumerable.Single(stack.Children)!;

        Assert.Equal("stack", stack.CallSite!.Value.DeclaredName);
        Assert.NotNull(child.CallSite); // positive control: it was stamped
        Assert.Null(child.CallSite!.Value.DeclaredName);
    }

    [Fact]
    public void DeclaredName_AnUnknownLocationHasNone()
        => Assert.Null(new SourceLocation("Nowhere.cs", 1, 1).DeclaredName);

    [Fact]
    public void PublishedPath_IsRelativeToThisProject()
    {
        // The build passes MSBuildProjectDirectory to the generator; the published `at=`
        // path drops it (local and deterministic builds alike), while CallSite keeps it.
        var probe = TextBlock("relative");
        var full = probe.CallSite!.Value.FilePath;

        Assert.NotEqual("StaticInfoTests.cs", full); // positive control: the stamp is a full path
        Assert.Equal("StaticInfoTests.cs", ReactorSourceMap.ToPublishedPath(full, out var marker));
        Assert.Null(marker);
    }

    private sealed class HookProbe : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0); // hook:count
            var density = UseRef(1.0); // hook:density
            UseEffect(() => setCount(count + 1), count); // hook:effect
            return TextBlock($"{count} {density.Current}");
        }
    }

    private sealed class GenericHookProbe<T> : Component
    {
        public override Element Render()
        {
            var (items, _) = UseState(0); // hook:items
            return TextBlock($"{items}");
        }
    }

    [Fact]
    public void Hooks_ClassComponent()
    {
        Assert.Equal(
            $"0:count@{LineOf("hook:count")};1:density@{LineOf("hook:density")};2:UseEffect@{LineOf("hook:effect")}",
            ReactorSourceMap.GetComponentHooks(typeof(HookProbe)));
    }

    [Fact]
    public void Hooks_GenericComponent_ResolvesFromAClosedType()
        => Assert.Equal($"0:items@{LineOf("hook:items")}", ReactorSourceMap.GetComponentHooks(typeof(GenericHookProbe<string>)));

    [Fact]
    public void Hooks_RenderFunction_ResolvesFromTheElementsCallSite()
    {
        var memo = Memo(ctx =>
        {
            var (n, setN) = ctx.UseState(0); // hook:n
            var flag = ctx.UseRef(false); // hook:flag
            return TextBlock($"{n}");
        }, 1);

        Assert.Equal(
            $"0:n@{LineOf("hook:n")};1:flag@{LineOf("hook:flag")}",
            ReactorSourceMap.GetRenderFunctionHooks(memo.CallSite!.Value));
    }

    [Fact]
    public void Hooks_NoneForAComponentTheGeneratorDidNotSee()
        => Assert.Null(ReactorSourceMap.GetComponentHooks(typeof(StaticInfoTests)));

    private class BaseHookProbe : Component
    {
        public override Element Render()
        {
            var (taps, _) = UseState(0); // hook:taps
            return TextBlock($"{taps}");
        }
    }

    private sealed class InheritsRenderProbe : BaseHookProbe;

    private sealed class HookFreeOverrideProbe : BaseHookProbe
    {
        public override Element Render() => TextBlock("no hooks");
    }

    [Fact]
    public void Hooks_InheritedRender_ResolveFromTheDeclaringBase()
    {
        var expected = $"0:taps@{LineOf("hook:taps")}";
        Assert.Equal(expected, ReactorSourceMap.GetComponentHooks(typeof(BaseHookProbe)));
        Assert.Equal(expected, ReactorSourceMap.GetComponentHooks(typeof(InheritsRenderProbe)));
    }

    [Fact]
    public void Hooks_HookFreeOverride_DoesNotBorrowTheBaseHooks()
    {
        Assert.NotNull(ReactorSourceMap.GetComponentHooks(typeof(BaseHookProbe))); // positive control
        Assert.Null(ReactorSourceMap.GetComponentHooks(typeof(HookFreeOverrideProbe)));
    }

    [Fact]
    public void Hooks_AreKeyedByAssembly()
    {
        // Full names are only unique within one assembly: two source-mapped libraries can each
        // define App.Counter. Their entries must not overwrite each other.
        var table = new ReactorStaticInfoBuilder();
        var first = typeof(object).Assembly;
        var second = typeof(StaticInfoTests).Assembly;
        table.CurrentAssembly = first;
        table.ComponentHooks("App.Counter", "0:a@1");
        table.CurrentAssembly = second;
        table.ComponentHooks("App.Counter", "0:b@2");

        Assert.Equal("0:a@1", table.ComponentHookTable[(first, "App.Counter")]);
        Assert.Equal("0:b@2", table.ComponentHookTable[(second, "App.Counter")]);
    }

    [Fact]
    public void LocationKeyedFacts_ConflictingAcrossAssemblies_AreUnknown()
    {
        // Two libraries that both map their roots to /_/ can stamp the same path, line and
        // column. Neither library's name or hooks may be reported on the other's controls.
        var table = new ReactorStaticInfoBuilder();
        table.CurrentAssembly = typeof(object).Assembly;
        table.Name("/_/Page.cs", 10, 5, "title");
        table.RenderFunctionHooks("/_/Page.cs", 20, 9, "0:a@21");
        table.Name("/_/Shared.cs", 3, 1, "same");
        table.CurrentAssembly = typeof(StaticInfoTests).Assembly;
        table.Name("/_/Page.cs", 10, 5, "header");
        table.RenderFunctionHooks("/_/Page.cs", 20, 9, "0:b@21");
        table.Name("/_/Shared.cs", 3, 1, "same");
        table.Name("/_/Page.cs", 10, 5, "title"); // a third claim does not revive either

        Assert.Null(table.NameTable[new SourceLocation("/_/Page.cs", 10, 5)]);
        Assert.Null(table.RenderFunctionHookTable[new SourceLocation("/_/Page.cs", 20, 9)]);
        // Positive control: agreeing claims (one file compiled into both) keep their fact.
        Assert.Equal("same", table.NameTable[new SourceLocation("/_/Shared.cs", 3, 1)]);
    }

    [Fact]
    public void SharedPath_WithDisagreeingFingerprints_IsUnknownEvenWhereOnlyOneClaimsAFact()
    {
        // Assembly A names the call at /_/Clash/Page.cs:10:5; assembly B has an UNNAMED call at
        // the same path, line and column, so it records no name at all. Only the per-file
        // fingerprints disagree, and that is what must make A's name unknown there.
        ReactorSourceMap.RegisterStaticInfo(typeof(object).Assembly, b =>
        {
            b.Name("/_/Clash/Page.cs", 10, 5, "title");
            b.RenderFunctionHooks("/_/Clash/Page.cs", 20, 9, "0:n@21");
            b.Source("/_/Clash/Page.cs", "aaaaaaaaaaaaaaaa");
            b.Name("/_/Agree/Page.cs", 3, 1, "kept");
            b.Source("/_/Agree/Page.cs", "cccccccccccccccc");
        });
        ReactorSourceMap.RegisterStaticInfo(typeof(global::System.Uri).Assembly, b =>
        {
            b.Source("/_/Clash/Page.cs", "bbbbbbbbbbbbbbbb");
            b.Name("/_/Agree/Page.cs", 3, 1, "kept");
            b.Source("/_/Agree/Page.cs", "cccccccccccccccc");
        });

        Assert.Null(ReactorSourceMap.GetDeclaredName(new SourceLocation("/_/Clash/Page.cs", 10, 5)));
        Assert.Null(ReactorSourceMap.GetRenderFunctionHooks(new SourceLocation("/_/Clash/Page.cs", 20, 9)));
        // Positive control: the same file compiled into both assemblies keeps its facts.
        Assert.Equal("kept", ReactorSourceMap.GetDeclaredName(new SourceLocation("/_/Agree/Page.cs", 3, 1)));
    }
}
