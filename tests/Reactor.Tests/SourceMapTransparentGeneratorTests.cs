using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.SourceMap.Generator;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — drives <see cref="SourceMapInterceptorGenerator"/> directly to cover the
/// <c>[ReactorSourceTransparent]</c> shapes that cannot be exercised from a live
/// interception suite.
///
/// <para><b>Why a driver and not more live tests.</b> Every unusable annotation reports
/// <c>REACTOR_SOURCEMAP_001</c>, and Release builds this repo with warnings as errors — so
/// a suite containing one annotated instance method, one annotated local function, one
/// annotated generic-type member and so on would need a <c>#pragma</c> for each just to
/// compile, and would still only prove that a warning appeared somewhere. Running the
/// generator over a synthetic compilation asserts the exact id, the exact target, and the
/// exact reason.</para>
///
/// <para><b>The snippet declares its own Reactor surface</b> rather than referencing the
/// real assembly: the generator matches types by metadata name, so a self-contained
/// snippet exercises the same code paths without dragging WinUI into a headless suite.
/// <see cref="AttributeMetadataName_MatchesTheRealAttribute"/> is the drift guard that
/// makes that safe — if the shipping attribute is ever renamed or moved, these hermetic
/// tests would otherwise keep passing against a name nothing uses.</para>
/// </summary>
public sealed class SourceMapTransparentGeneratorTests
{
    /// <summary>
    /// A minimal stand-in for the Reactor DSL surface the generator keys off. Only the
    /// metadata names matter.
    /// </summary>
    private const string ReactorSurface = """
        namespace Microsoft.UI.Reactor.Core
        {
            public abstract record Element
            {
                public SourceLocation? CallSite { get; init; }
            }
            public record TextBlockElement(string Content) : Element;
            public record EmptyElement : Element;
            public readonly record struct SourceLocation(string FilePath, int LineNumber);
        }
        namespace Microsoft.UI.Reactor
        {
            public static class Factories
            {
                public static Microsoft.UI.Reactor.Core.TextBlockElement TextBlock(string content)
                    => new(content);
            }
        }
        namespace Microsoft.UI.Reactor.Diagnostics
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
            public sealed class ReactorSourceTransparentAttribute : global::System.Attribute { }
            public static class ReactorSourceMap { public static bool Enabled { get; set; } }
        }
        """;

    private static readonly ImmutableArray<MetadataReference> s_references = BuildReferences();

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        return tpa
            .Split(global::System.IO.Path.PathSeparator)
            .Where(static p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && global::System.IO.File.Exists(p))
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
    }

    /// <summary>
    /// Runs the generator over <paramref name="userCode"/> plus the stand-in surface and
    /// returns what it produced.
    /// </summary>
    private static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) Run(
        string userCode, bool enabled = true)
    {
        var compilation = CSharpCompilation.Create(
            "SourceMapDriverProbe",
            new[]
            {
                CSharpSyntaxTree.ParseText(ReactorSurface, path: "Surface.cs"),
                CSharpSyntaxTree.ParseText(userCode, path: "User.cs"),
            },
            s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // The snippet must bind, or an "expected diagnostic missing" result would just be
        // measuring a broken probe. Binding errors are a test bug, not a product signal.
        var bindErrors = compilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(bindErrors.Count == 0, "probe snippet failed to compile: " + string.Join("; ", bindErrors));

        var driver = CSharpGeneratorDriver.Create(
            new[] { new SourceMapInterceptorGenerator().AsSourceGenerator() },
            optionsProvider: new StubOptions(enabled));

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();

        var generated = string.Concat(result.GeneratedTrees.Select(static t => t.ToString()));
        return (result.Diagnostics, generated);
    }

    /// <summary>
    /// Counts attribute APPLICATIONS, not every mention of the name. The generated file
    /// also declares a file-local <c>InterceptsLocationAttribute</c> polyfill (the .NET 10
    /// BCL has none), whose class header and constructor would otherwise be counted as two
    /// phantom interceptors and make every expectation here off by two.
    /// </summary>
    private static int InterceptorCount(string generated)
        => generated.Split(
            new[] { "[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(" },
            StringSplitOptions.None).Length - 1;

    // ── Drift guard ───────────────────────────────────────────────────────

    [Fact]
    public void AttributeMetadataName_MatchesTheRealAttribute()
    {
        // Everything in this file is hermetic, so a rename of the shipping attribute would
        // leave these tests green while the feature silently stopped working for every
        // real consumer. This is the one assertion that ties the two together.
        Assert.Equal(
            SourceMapInterceptorGenerator.TransparentAttributeMetadataName,
            typeof(ReactorSourceTransparentAttribute).FullName);
    }

    // ── Unusable annotations report REACTOR_SOURCEMAP_001 ─────────────────

    [Theory]
    [InlineData("private", "generated code cannot reach it")]
    [InlineData("protected", "generated code cannot reach it")]
    public void InaccessibleHelper_IsReported(string accessibility, string expectedReason)
    {
        var (diagnostics, _) = Run($$"""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                {{accessibility}} static Element Helper(string s) => TextBlock(s);
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("REACTOR_SOURCEMAP_001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.DefaultSeverity);
        Assert.Contains(expectedReason, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Helper", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void InstanceHelper_IsReported()
    {
        var (diagnostics, _) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                public Element Helper(string s) => TextBlock(s);
            }
            """);

        Assert.Contains("instance method", Assert.Single(diagnostics).GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void LocalFunction_IsReported()
    {
        // C# interceptors cannot intercept local functions at all, so the annotation can
        // never be honoured there however it is written.
        var (diagnostics, _) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                public static Element Render()
                {
                    [ReactorSourceTransparent]
                    static Element Helper(string s) => TextBlock(s);
                    return Helper("x");
                }
            }
            """);

        Assert.Contains("local function", Assert.Single(diagnostics).GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelperInAGenericType_IsReported()
    {
        // "Interceptors cannot be declared in generic types at any level of nesting", and
        // an interceptor for a member of one would additionally have to restate the
        // containing type's arity.
        var (diagnostics, _) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host<T>
            {
                [ReactorSourceTransparent]
                public static Element Helper(string s) => TextBlock(s);
            }
            """);

        Assert.Contains("generic type", Assert.Single(diagnostics).GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelperInAFileLocalType_IsReported()
    {
        var (diagnostics, _) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            file class Host
            {
                [ReactorSourceTransparent]
                public static Element Helper(string s) => TextBlock(s);
            }
            """);

        Assert.Contains("file-local type", Assert.Single(diagnostics).GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void NonElementReturningHelper_IsReported()
    {
        var (diagnostics, _) = Run("""
            using Microsoft.UI.Reactor.Diagnostics;

            public class Host
            {
                [ReactorSourceTransparent]
                public static string Helper(string s) => s;
            }
            """);

        Assert.Contains("does not return", Assert.Single(diagnostics).GetMessage(), StringComparison.Ordinal);
    }

    // ── Usable annotations report nothing ─────────────────────────────────

    [Fact]
    public void UsableHelper_IsNotReported()
    {
        // The positive control for every assertion above. Without it, a generator that
        // reported REACTOR_SOURCEMAP_001 unconditionally — or one whose reason strings had
        // drifted into always matching — would look identical.
        var (diagnostics, generated) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                internal static Element Helper(string s) => TextBlock(s);

                public static Element Render() => Helper("x");
            }
            """);

        Assert.Empty(diagnostics);

        // And it really was honoured: the call TO the helper is intercepted, the call
        // inside it is not. One interceptor, not two, and not zero.
        Assert.Equal(1, InterceptorCount(generated));
        Assert.Contains("Host.Helper", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableElementReturningHelper_IsNotReported()
    {
        // Regression guard for the display-string comparison that used to back the
        // "returns an Element" test: `Element?` rendered as "…Element?", never matched the
        // metadata name, and the base-type walk went straight past Element to object. A
        // conditional helper declared to return Element? was therefore reported as not
        // returning an Element, and silently skipped.
        var (diagnostics, generated) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                internal static Element? Helper(bool show, string s) => show ? TextBlock(s) : null;

                public static Element? Render() => Helper(true, "x");
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Equal(1, InterceptorCount(generated));
    }

    // ── Rule 1 is conditioned on forwardability ───────────────────────────

    [Fact]
    public void ForwardableTransparentHelper_SuppressesTheInterceptorInsideItsBody()
    {
        var (_, generated) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                internal static Element Helper(string s) => TextBlock(s);
            }
            """);

        // The helper is never called here, so the ONLY candidate call site in the file is
        // the TextBlock inside its body — and rule 1 removes it.
        Assert.Equal(0, InterceptorCount(generated));
    }

    [Fact]
    public void NonForwardableTransparentHelper_KeepsTheInterceptorInsideItsBody()
    {
        // The asymmetry that makes a bad annotation harmless. Same body, same attribute,
        // only the accessibility differs — and because no rule-2 interceptor can exist to
        // re-stamp at the caller, suppressing here would trade "the helper's line" for no
        // line at all. Paired with the test above, this is a differential oracle: one
        // input character apart, opposite expected outputs.
        var (diagnostics, generated) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                private static Element Helper(string s) => TextBlock(s);
            }
            """);

        Assert.Equal(1, InterceptorCount(generated));
        Assert.Single(diagnostics);
    }

    [Fact]
    public void NestedTransparentHelpers_EmitOnlyTheOutermostInterceptor()
    {
        var (_, generated) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                internal static Element Inner(string s) => TextBlock(s);

                [ReactorSourceTransparent]
                internal static Element Outer(string s) => Inner(s);

                public static Element Render() => Outer("x");
            }
            """);

        // Three candidate calls — TextBlock inside Inner, Inner inside Outer, Outer inside
        // Render — and only the last one survives, because the first two are inside a
        // transparent method. That is rule 3 in one number.
        Assert.Equal(1, InterceptorCount(generated));
        Assert.Contains("Host.Outer", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Host.Inner", generated, StringComparison.Ordinal);
    }

    // ── The opt-in gate covers diagnostics too ────────────────────────────

    [Fact]
    public void DisabledGenerator_ReportsNothingAndEmitsNothing()
    {
        // In a compilation where no interceptors are generated the attribute is inert by
        // design, so warning about it would be pure noise.
        var (diagnostics, generated) = Run("""
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Reactor.Diagnostics;
            using static Microsoft.UI.Reactor.Factories;

            public class Host
            {
                [ReactorSourceTransparent]
                private static Element Helper(string s) => TextBlock(s);
            }
            """, enabled: false);

        Assert.Empty(diagnostics);
        Assert.Equal(string.Empty, generated);
    }

    /// <summary>
    /// Supplies <c>build_property.ReactorSourceMap</c>, which is normally provided by
    /// <c>CompilerVisibleProperty</c> in the consuming project.
    /// </summary>
    private sealed class StubOptions : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global;

        public StubOptions(bool enabled) => _global = new Options(enabled);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options : AnalyzerConfigOptions
        {
            internal static readonly Options Empty = new(null);

            private readonly string? _reactorSourceMap;

            internal Options(bool enabled) => _reactorSourceMap = enabled ? "true" : "false";

            private Options(string? value) => _reactorSourceMap = value;

            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.ReactorSourceMap" && _reactorSourceMap is not null)
                {
                    value = _reactorSourceMap;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
