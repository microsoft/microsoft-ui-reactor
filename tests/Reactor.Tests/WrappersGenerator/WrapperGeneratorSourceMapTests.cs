using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.UI.Reactor.Wrappers.Generator;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.WrappersGenerator;

/// <summary>
/// Spec 010 — the wrapper generator's own source-map interceptors
/// (<c>WrapperGenerator.SourceMap.cs</c>), driven over an in-memory compilation.
///
/// <para>The live behaviour (stamped lines and columns) is covered end to end by
/// <c>Reactor.SourceMap.Tests/WrapperFactoryInterceptionTests</c>. What only a driver can
/// pin is the GATE: this generator ships in <c>analyzers/dotnet/cs</c> and loads into every
/// consumer compilation, and an <c>[InterceptsLocation]</c> emitted into a compilation that
/// has not listed <c>Microsoft.UI.Reactor.Generated</c> in <c>InterceptorsNamespaces</c> is
/// a hard <c>CS9137</c>. Every in-repo project gets <c>ReactorSourceMap=true</c> by default
/// in Debug without that namespace, so a gate on the property alone would break them.</para>
/// </summary>
public sealed class WrapperGeneratorSourceMapTests
{
    private const string InterceptorsFile = "ReactorWrappers.SourceMap.Interceptors.g.cs";

    // WinUI shape the wrapper generator validates against, plus the slice of the Reactor
    // surface the source-map half keys off (Element.CallSite, SourceLocation, the flag).
    private const string Source = """
        namespace Microsoft.UI.Xaml
        {
            public class DependencyObject {}
            public class UIElement : DependencyObject {}
            public class FrameworkElement : UIElement {}
        }
        namespace Microsoft.UI.Xaml.Controls
        {
            public class Control : Microsoft.UI.Xaml.FrameworkElement {}
        }
        namespace Microsoft.UI.Reactor.Core
        {
            public abstract record Element { public SourceLocation? CallSite { get; init; } }
            public readonly record struct SourceLocation(string FilePath, int LineNumber, int ColumnNumber);
        }
        namespace Microsoft.UI.Reactor.Diagnostics
        {
            public static class ReactorSourceMap { public static bool Enabled { get; set; } }
        }
        namespace App
        {
            public class Gauge : Microsoft.UI.Xaml.Controls.Control
            {
                public double Value { get; set; }
            }

            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Gauge), RegisterAssembly = false)]
            public partial record GaugeElement;
        }
        namespace App.Use
        {
            using static App.GaugeElement;

            public static class Screen
            {
                // EXTRA
                public static object Unqualified() => Gauge(value: 3);
                public static object Qualified() => App.GaugeElement.Gauge();
                public static object Local()
                {
                    var g = Gauge();
                    return g;
                }
            }
        }
        """;

    private static GeneratorDriverRunResult Run(
        bool reactorSourceMap,
        bool interceptorsNamespace,
        string source = Source,
        string? extraSource = null,
        ImmutableArray<KeyValuePair<string, string>> pathMap = default,
        string interceptorsNamespaces = "Some.Other;Microsoft.UI.Reactor.Generated")
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        var refs = tpa.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();

        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        if (interceptorsNamespace)
        {
            parse = parse.WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", interceptorsNamespaces)]);
        }

        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, parse, path: @"C:\proj\Screen.cs") };
        if (extraSource is not null) trees.Add(CSharpSyntaxTree.ParseText(extraSource, parse, path: @"C:\proj\Extra.cs"));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        if (!pathMap.IsDefault)
            options = options.WithSourceReferenceResolver(new SourceFileResolver(ImmutableArray<string>.Empty, null, pathMap));
        var compilation = CSharpCompilation.Create("WrapperSourceMapTests", trees, refs, options);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new WrapperGenerator().AsSourceGenerator()],
            parseOptions: parse,
            optionsProvider: new Options(reactorSourceMap));

        var result = driver.RunGenerators(compilation).GetRunResult();
        // A generator that threw emits nothing, which would satisfy any "no interceptors" assertion.
        Assert.All(result.Results, static r => Assert.Null(r.Exception));
        return result;
    }

    private static string? Interceptors(GeneratorDriverRunResult result)
        => result.GeneratedTrees.SingleOrDefault(t => t.FilePath.EndsWith(InterceptorsFile, StringComparison.Ordinal))?.GetText().ToString();

    [Fact]
    public void Emits_OneInterceptorPerCall_WhenSourceMapAndNamespaceAreOn()
    {
        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true));

        Assert.NotNull(src);
        // Unqualified (using static), qualified, and a `var` local: three calls, three interceptors.
        Assert.Equal(3, src!.Split("[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, ").Length - 1);
        Assert.Contains("public static global::App.GaugeElement __ReactorWrapper_Gauge_0(", src);
        Assert.Contains("var __e = global::App.GaugeElement.Gauge(", src);
        Assert.Contains("if (!global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled) return __e;", src);
        Assert.Contains("CallSite = new global::Microsoft.UI.Reactor.Core.SourceLocation(@\"C:\\proj\\Screen.cs\", ", src);
        Assert.Contains("namespace Microsoft.UI.Reactor.Generated", src);
    }

    [Fact]
    public void Interceptor_RestatesTheFactorySignatureExactly()
    {
        var result = Run(reactorSourceMap: true, interceptorsNamespace: true);
        var wrapper = result.GeneratedTrees.Single(t => t.FilePath.EndsWith("GaugeElement.Wrapper.g.cs", StringComparison.Ordinal)).GetText(TestContext.Current.CancellationToken).ToString();
        var src = Interceptors(result)!;

        // The factory parameter type, taken from the generated factory itself, must be the
        // interceptor's parameter type verbatim — a mismatch is CS9144 in the consumer.
        var factoryLine = wrapper.Split('\n').Select(l => l.Trim()).First(l => l.EndsWith("value = default", StringComparison.Ordinal));
        var parameterType = factoryLine.Substring(0, factoryLine.LastIndexOf(" value = default", StringComparison.Ordinal));
        Assert.Contains($"({parameterType} __a0)", src);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void EmitsNothing_UnlessSourceMapAndNamespaceAreBothOn(bool reactorSourceMap, bool interceptorsNamespace)
    {
        var result = Run(reactorSourceMap, interceptorsNamespace);

        // Positive control: the wrapper itself was generated, so "no interceptors" is the gate
        // speaking and not the generator failing to run.
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("GaugeElement.Wrapper.g.cs", StringComparison.Ordinal));
        Assert.Null(Interceptors(result));
    }

    [Theory]
    [InlineData("Microsoft.UI.Reactor")]
    [InlineData("global")]
    public void AParentNamespaceEntry_EnablesInterceptorsAsTheCompilerDoes(string entry)
    {
        // The compiler accepts an interceptor in any namespace nested in a listed one.
        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true, interceptorsNamespaces: entry));

        Assert.Equal(3, InterceptorCount(src));
    }

    [Fact]
    public void ASiblingNamespaceEntry_DoesNot()
    {
        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true, interceptorsNamespaces: "Microsoft.UI.Reactor.Gen"));

        Assert.Null(src);
    }

    [Fact]
    public void LeavesACallAlone_WhenAVisibleMethodOfThatNameWins()
    {
        // A method named like the factory that is already in scope is what the call binds
        // to; intercepting it with the factory's signature would be CS9144.
        var source = Source.Replace("// EXTRA", "private static object Gauge(double value = 0) => value;", StringComparison.Ordinal);
        Assert.NotEqual(Source, source);

        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true, source));

        // Only the explicitly qualified call still goes to the factory.
        Assert.NotNull(src);
        Assert.Equal(1, src!.Split("[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, ").Length - 1);
    }

    private static int InterceptorCount(string? src)
        => src is null ? 0 : src.Split("[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, ").Length - 1;

    [Fact]
    public void UnqualifiedCallInsideTheElementRecord_IsFoundThroughTheEnclosingType()
    {
        // No `using static` reaches this call; only the enclosing-type lookup does.
        var source = Source.Replace(
            "public partial record GaugeElement;",
            "public partial record GaugeElement { public static object Self() => Gauge(); }",
            StringComparison.Ordinal);
        Assert.NotEqual(Source, source);

        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true, source));

        Assert.Equal(4, InterceptorCount(src));
    }

    [Fact]
    public void GlobalUsingStatic_IsFound()
    {
        var source = Source.Replace("using static App.GaugeElement;", string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(Source, source);

        // Positive control: without any import only the explicitly qualified call resolves.
        Assert.Equal(1, InterceptorCount(Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true, source))));

        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true, source,
            extraSource: "global using static App.GaugeElement;"));

        Assert.Equal(3, InterceptorCount(src));
    }

    [Fact]
    public void AmbiguousImportsInOneScope_AreLeftAlone()
    {
        // Two wrapped controls named Gauge (so both factories are named Gauge), both imported into one scope: the call
        // is ambiguous in the real compilation too, so guessing either would be wrong.
        var source = Source.Replace(
            "using static App.GaugeElement;",
            "using static App.GaugeElement;\n    using static Other.OtherGaugeElement;",
            StringComparison.Ordinal);
        Assert.NotEqual(Source, source);
        const string other = """
            namespace Other
            {
                public class Gauge : Microsoft.UI.Xaml.Controls.Control { }

                [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Other.Gauge), RegisterAssembly = false)]
                public partial record OtherGaugeElement;
            }
            """;

        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true, source, extraSource: other));

        // Only App.GaugeElement.Gauge() — explicitly qualified — remains.
        Assert.Equal(1, InterceptorCount(src));
        Assert.Contains("global::App.GaugeElement.Gauge(", src);
    }

    [Fact]
    public void StampedPath_HonoursThePathMap()
    {
        // What [CallerFilePath] reports under DeterministicSourcePaths (CI): the mapped prefix,
        // with the remainder's separators rewritten to the replacement's.
        var src = Interceptors(Run(reactorSourceMap: true, interceptorsNamespace: true,
            pathMap: [new KeyValuePair<string, string>(@"C:\proj\", "/_/")]));

        Assert.NotNull(src);
        Assert.Contains("SourceLocation(@\"/_/Screen.cs\", ", src);
        Assert.DoesNotContain(@"C:\proj", src);
    }

    private sealed class Options(bool reactorSourceMap) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global = new Values(reactorSourceMap
            ? ImmutableDictionary<string, string>.Empty.Add("build_property.ReactorSourceMap", "true")
            : ImmutableDictionary<string, string>.Empty.Add("build_property.ReactorSourceMap", "false"));

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values(ImmutableDictionary<string, string>.Empty);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values(ImmutableDictionary<string, string>.Empty);

        private sealed class Values(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}