using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Tests.CheckCommandTests;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

public class CombinedSizeModifierAnalyzerTests
{
    private const string Stubs = """
        #nullable enable
        namespace System.Runtime.CompilerServices
        {
            public static class IsExternalInit { }
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class CallerArgumentExpressionAttribute : System.Attribute
            {
                public CallerArgumentExpressionAttribute(string parameterName) { }
            }
        }
        namespace Microsoft.UI.Reactor.Core
        {
            public abstract record Element { public ElementModifiers? Modifiers { get; init; } }
            public record ElementModifiers { public LayoutModifiers? Layout { get; init; } }
            public record LayoutModifiers;
            public record ButtonElement : Element;
            public record HasSizeElement : Element
            {
                public HasSizeElement Size(double width, double height) => this;
            }
        }
        namespace Microsoft.UI.Reactor
        {
            using Microsoft.UI.Reactor.Core;
            public static class Factories
            {
                public static ButtonElement Button() => new();
                public static HasSizeElement HasSize() => new();
            }
            public static class ElementExtensions
            {
                public static T Width<T>(this T el, double width) where T : Element => el;
                public static T Height<T>(this T el, double height) where T : Element => el;
                public static T Size<T>(this T el, double width, double height) where T : Element => el;
                public static T Margin<T>(this T el, double margin) where T : Element => el;
            }
        }
        namespace App
        {
            using System.Linq;
            using Microsoft.UI.Reactor;
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;
            public record CustomElement : Element
            {
                public static int Copies;
                public CustomElement() { }
                protected CustomElement(CustomElement source) : base(source) { Copies++; }
            }
            public record CustomModifiers : ElementModifiers
            {
                public static int Copies;
                public CustomModifiers() { }
                protected CustomModifiers(CustomModifiers source) : base(source) { Copies++; }
            }
            public record CustomLayout : LayoutModifiers
            {
                public static int Copies;
                public CustomLayout() { }
                protected CustomLayout(CustomLayout source) : base(source) { Copies++; }
            }
            public class Widget
            {
                public Widget Width(double width) => this;
                public Widget Height(double height) => this;
            }
            public static class C
            {
                public static double Next() => 5;
                public static double Measure(int value,
                    [System.Runtime.CompilerServices.CallerArgumentExpression("value")] string text = "") => text.Length;
                public static object Build(Element element, double w, double h) => BODY;
            }
        }
        """;

    [Theory]
    [InlineData("Button().Width(20).Height(30)", true)]
    [InlineData("Button().Height(h).Width(w)", true)]
    [InlineData("Button().Margin(4).Width(w).Height(h)", true)]
    [InlineData("Button().Width(width: w).Height(height: h)", true)]
    [InlineData("Button().Width<ButtonElement>(w).Height<ButtonElement>(h)", true)]
    [InlineData("Button().Height(Next()).Width(Next())", true)]
    [InlineData("new ButtonElement().Width(w).Height(h)", true)]
    [InlineData("element.Width(w).Height(h)", false)]
    [InlineData("new CustomElement().Width(w).Height(h)", false)]
    [InlineData("new ButtonElement { Modifiers = new CustomModifiers() }.Width(w).Height(h)", false)]
    [InlineData("new ButtonElement { Modifiers = new ElementModifiers { Layout = new CustomLayout() } }.Width(w).Height(h)", false)]
    [InlineData("HasSize().Width(w).Height(h)", false)]
    [InlineData("Button().Width(w).Margin(4).Height(h)", false)]
    [InlineData("Button().Width(w).Width(h)", false)]
    [InlineData("Button().Size(w, h)", false)]
    [InlineData("new Widget().Width(w).Height(h)", false)]
    [InlineData("element?.Width(w).Height(h)", false)]
    [InlineData("ElementExtensions.Height(Button().Width(w), h)", false)]
    [InlineData("Button().Width(w) /*keep*/.Height(h)", false)]
    [InlineData("Button(). /*keep*/ Width(w).Height(h)", false)]
    [InlineData("Button().Width(w).Height(/*keep*/ h)", false)]
    [InlineData("Button().Width<ButtonElement>(w).Height<Element>(h)", false)]
    public async Task SemanticMatching(string expression, bool expected)
    {
        var compilation = TestCompilation.Create(Stubs.Replace("BODY", expression));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var build = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "Build");
        var combined = build.ExpressionBody!.Expression is InvocationExpressionSyntax invocation
            ? CombinedSizeModifierAnalyzer.TryCombine(invocation, model, TestContext.Current.CancellationToken)
            : null;
        Assert.Equal(expected, combined is not null);
        var diagnostics = await compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new CombinedSizeModifierAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected ? 1 : 0, diagnostics.Length);
        if (expected)
        {
            Assert.Equal(CombinedSizeModifierAnalyzer.DiagnosticId, diagnostics[0].Id);
            Assert.Equal(DiagnosticSeverity.Info, diagnostics[0].Severity);
        }
    }

    [Fact]
    public void SemanticSpike_Rejects_Missing_Size()
    {
        var source = Stubs.Replace(
            "public static T Size<T>(this T el, double width, double height) where T : Element => el;", "")
            .Replace("BODY", "Button().Width(w).Height(h)");
        AssertNoMatches(source);
    }

    [Fact]
    public void SemanticSpike_Rejects_Competing_Extension()
    {
        var source = Stubs.Replace("BODY", "Button().Width(w).Height(h)") + """
            namespace App
            {
                public static class OtherExtensions
                {
                    public static Microsoft.UI.Reactor.Core.ButtonElement Size(
                        this Microsoft.UI.Reactor.Core.ButtonElement el, double width, double height) => el;
                }
            }
            """;
        AssertNoMatches(source);
    }

    [Fact]
    public void SemanticMatching_Rejects_Foreign_Dimension_Extension()
    {
        var source = Stubs.Replace("BODY", "Button().Width(w).Height(h)") + """
            namespace App
            {
                public static class OtherExtensions
                {
                    public static Microsoft.UI.Reactor.Core.ButtonElement Width(
                        this Microsoft.UI.Reactor.Core.ButtonElement el, double width) => el;
                }
            }
            """;
        AssertNoMatches(source);
    }

    [Fact]
    public void SemanticSpike_Rejects_Expression_Tree()
    {
        var source = Stubs.Replace("BODY",
            "(System.Linq.Expressions.Expression<System.Func<ButtonElement>>)(() => Button().Width(w).Height(h))");
        AssertNoMatches(source);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".AsQueryable()")]
    public void SemanticMatching_Rejects_Query_Selectors(string conversion)
    {
        AssertNoMatches(Stubs.Replace("BODY",
            $"from x in new[] {{ 1, 2 }}{conversion} select Button().Width(x).Height(h)"));
    }

    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Roslyn test compilation binds against the built Reactor assembly; this host is not single-file published.")]
    public void SemanticSpike_Binds_Real_Reactor_Overloads()
    {
        var ct = TestContext.Current.CancellationToken;
        var compilation = TestCompilation.Create("""
            using Microsoft.UI.Reactor;
            class C { object Build() => Factories.Button("x").Width(20).Height(30); }
            """);
        if (compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.ElementExtensions") is null)
            compilation = compilation.AddReferences(MetadataReference.CreateFromFile(typeof(Factories).Assembly.Location));
        Assert.Empty(compilation.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error));
        var tree = compilation.SyntaxTrees.Single();
        var body = tree.GetRoot(ct).DescendantNodes().OfType<ArrowExpressionClauseSyntax>().Single();
        Assert.NotNull(CombinedSizeModifierAnalyzer.TryCombine(
            Assert.IsType<InvocationExpressionSyntax>(body.Expression), compilation.GetSemanticModel(tree), ct));
    }

    [Theory]
    [InlineData("Button().Width(20).Height(30)", "Button().Size(width: 20, height: 30)")]
    [InlineData("Button().Height(h).Width(w)", "Button().Size(height: h, width: w)")]
    [InlineData("Button().Width(width: w).Height(height: h)", "Button().Size(width: w, height: h)")]
    [InlineData("Button().Width<ButtonElement>(w).Height<ButtonElement>(h)", "Button().Size<ButtonElement>(width: w, height: h)")]
    [InlineData("Button().Width(w).Height<ButtonElement>(h)", "Button().Size<ButtonElement>(width: w, height: h)")]
    [InlineData("Button().Width(Next()).Height(Next())", "Button().Size(width: Next(), height: Next())")]
    [InlineData("Button().Height(Next()).Width(Next())", "Button().Size(height: Next(), width: Next())")]
    [InlineData("Button().Margin(4).Width(w).Height(h)", "Button().Margin(4).Size(width: w, height: h)")]
    [InlineData("Button().Width(Measure(1+2)).Height(30)", "Button().Size(width: Measure(1+2), height: 30)")]
    [InlineData("Button().Height(Measure(1+2)).Width(Measure(3*4))", "Button().Size(height: Measure(1+2), width: Measure(3*4))")]
    public async Task CodeFix_Combines_Without_Reordering_Arguments(string before, string after)
    {
        await new CSharpCodeFixTest<CombinedSizeModifierAnalyzer, CombinedSizeModifierCodeFix, DefaultVerifier>
        {
            TestCode = Stubs.Replace("BODY", "{|REACTOR_PERF_001:" + before + "|}"),
            FixedCode = Stubs.Replace("BODY", after),
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Preserves_Surrounding_Modifiers()
    {
        await new CSharpCodeFixTest<CombinedSizeModifierAnalyzer, CombinedSizeModifierCodeFix, DefaultVerifier>
        {
            TestCode = Stubs.Replace("BODY", "{|REACTOR_PERF_001:Button().Width(w).Height(h)|}.Margin(4)"),
            FixedCode = Stubs.Replace("BODY", "Button().Size(width: w, height: h).Margin(4)"),
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Handles_Overlapping_Dimension_Pairs()
    {
        await new CSharpCodeFixTest<CombinedSizeModifierAnalyzer, CombinedSizeModifierCodeFix, DefaultVerifier>
        {
            TestCode = Stubs.Replace("BODY", "{|REACTOR_PERF_001:Button().Width(w).Height(h).Width(30)|}"),
            FixedCode = Stubs.Replace("BODY", "Button().Width(w).Size(height: h, width: 30)"),
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_FixAll_Combines_Independent_Pairs()
    {
        await new CSharpCodeFixTest<CombinedSizeModifierAnalyzer, CombinedSizeModifierCodeFix, DefaultVerifier>
        {
            TestCode = Stubs.Replace("BODY", """
                new[] { {|REACTOR_PERF_001:Button().Width(w).Height(h)|}, {|REACTOR_PERF_001:Button().Height(h).Width(w)|} }
                """),
            FixedCode = Stubs.Replace("BODY", """
                new[] { Button().Size(width: w, height: h), Button().Size(height: h, width: w) }
                """),
            NumberOfIncrementalIterations = 2,
            NumberOfFixAllIterations = 1,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_FixAll_Combines_Nested_Pairs()
    {
        await new CSharpCodeFixTest<CombinedSizeModifierAnalyzer, CombinedSizeModifierCodeFix, DefaultVerifier>
        {
            TestCode = Stubs.Replace("BODY",
                "{|REACTOR_PERF_001:{|REACTOR_PERF_001:Button().Width(w).Height(h)|}.Margin(4).Width(30).Height(40)|}"),
            FixedCode = Stubs.Replace("BODY",
                "Button().Size(width: w, height: h).Margin(4).Size(width: 30, height: 40)"),
            NumberOfIncrementalIterations = 2,
            NumberOfFixAllIterations = 1,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unbound_Calls_Do_Not_Report()
    {
        await new CSharpAnalyzerTest<CombinedSizeModifierAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs.Replace("BODY", """Button().Width("invalid").Height(h)"""),
            CompilerDiagnostics = CompilerDiagnostics.None,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Roslyn test compilation requires on-disk framework references; this test host is not single-file published.")]
    private static void AssertNoMatches(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var compilation = TestCompilation.Create(source);
        if (compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1") is null)
            compilation = compilation.AddReferences(MetadataReference.CreateFromFile(
                typeof(global::System.Linq.Expressions.Expression).Assembly.Location));
        if (compilation.GetTypeByMetadataName("System.Linq.Queryable") is null)
            compilation = compilation.AddReferences(MetadataReference.CreateFromFile(
                typeof(global::System.Linq.Queryable).Assembly.Location));
        Assert.Empty(compilation.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        foreach (var invocation in tree.GetRoot(ct).DescendantNodes().OfType<InvocationExpressionSyntax>())
            Assert.Null(CombinedSizeModifierAnalyzer.TryCombine(invocation, model, ct));
    }
}
