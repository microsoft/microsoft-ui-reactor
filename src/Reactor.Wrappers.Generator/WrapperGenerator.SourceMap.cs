using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.UI.Reactor.SourceMap.Generator;

namespace Microsoft.UI.Reactor.Wrappers.Generator;

/// <summary>
/// Spec 010 source mapping for the factories this generator emits.
///
/// <para>Reactor.SourceMap.Generator stamps a call site onto every <c>Factories.*</c> call
/// with an interceptor, but it cannot do that for <c>SettingsCard(...)</c>: Roslyn runs every
/// generator against the same input compilation, so the factory this generator emits does
/// not exist in its view and an interceptor needs the factory's exact signature. This
/// generator knows that signature — it wrote it — so it intercepts calls to its own
/// factories, with the same position rules and the same stamping body.</para>
///
/// <para><b>Gate.</b> Emits only when <c>ReactorSourceMap</c> is true AND the compiler accepts
/// interceptors in <c>Microsoft.UI.Reactor.Generated</c>. This generator ships in
/// <c>analyzers/dotnet/cs</c> and so loads into every consumer compilation; the second half
/// is what keeps it from ever emitting a <c>CS9137</c> into one that has not opted the
/// namespace in, and the syntax predicate checks it first so a compilation without source
/// mapping does no semantic work here at all.</para>
/// </summary>
public sealed partial class WrapperGenerator
{
    private const string ReactorSourceMapMetadataName = "Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap";
    private const string SourceLocationMetadataName = "Microsoft.UI.Reactor.Core.SourceLocation";
    private const string ElementMetadataName = "Microsoft.UI.Reactor.Core.Element";

    private static void InitializeSourceMap(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<WrapperModel?> models)
    {
        var enabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            p.GlobalOptions.TryGetValue("build_property.ReactorSourceMap", out var v)
            && v.Equals("true", System.StringComparison.OrdinalIgnoreCase));

        var hasApi = context.CompilationProvider.Select(static (c, _) =>
            c.GetTypeByMetadataName(ReactorSourceMapMetadataName) is not null
            && c.GetTypeByMetadataName(SourceLocationMetadataName) is not null);

        var needsPolyfill = context.CompilationProvider.Select(static (c, _) =>
            c.GetTypeByMetadataName("System.Runtime.CompilerServices.InterceptsLocationAttribute") is null);

        var pathMap = context.CompilationProvider.Select(static (c, _) =>
            (c.Options.SourceReferenceResolver as SourceFileResolver)?.PathMap
            ?? ImmutableArray<KeyValuePair<string, string>>.Empty);

        var factories = models
            .Select(static (m, _) => m!.Factory)
            .Where(static f => f is not null)
            .Collect();

        var sites = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax invocation
                    && WrapperFactoryCalls.CandidateName(invocation) is not null
                    && SourceMapPositions.AcceptsReactorInterceptors(node.SyntaxTree.Options),
                transform: static (ctx, ct) => DescribeWrapperCall(ctx, ct))
            .Where(static s => s is not null)
            .Collect();

        var input = sites.Combine(factories).Combine(enabled).Combine(hasApi).Combine(needsPolyfill).Combine(pathMap);
        context.RegisterSourceOutput(input, static (spc, tuple) =>
        {
            var (((((calls, shapes), isEnabled), api), polyfill), map) = tuple;
            if (!isEnabled || !api || calls.IsDefaultOrEmpty || shapes.IsDefaultOrEmpty) return;
            if (EmitSourceMapInterceptors(calls!, shapes!, polyfill, map) is { } source)
                spc.AddSource("ReactorWrappers.SourceMap.Interceptors.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static WrapperCallSite? DescribeWrapperCall(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var model = ctx.SemanticModel;

        // Cheap rejection first: almost every invocation binds (or has candidates), and a
        // call to a generated factory never does in this compilation.
        if (model.GetSymbolInfo(invocation, ct) is not { Symbol: null, CandidateSymbols.IsEmpty: true }) return null;
        if (WrapperFactoryCalls.ResolveUnboundCall(model, invocation, ct) is not { } element) return null;

        // Same [ReactorSourceTransparent] rule the source-map generator applies to every other
        // factory call: inside a transparent helper the element belongs to the helper's caller,
        // whose interceptor stamps it.
        if (model.Compilation.GetTypeByMetadataName(ElementMetadataName) is not { } elementSymbol) return null;
        if (ReactorSourceTransparency.IsInsideTransparentMethod(model, invocation, model.Compilation, elementSymbol, ct)) return null;

        if (model.GetInterceptableLocation(invocation, ct) is not { } location) return null;

        // Position rules shared with the source-map generator: the line follows the argument
        // list's opening paren ([CallerLineNumber] parity) and the column is the name's.
        var lineSpan = invocation.SyntaxTree.GetMappedLineSpan(invocation.ArgumentList.OpenParenToken.Span, ct);
        return new WrapperCallSite(
            element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            WrapperFactoryCalls.CandidateName(invocation)!,
            location.GetInterceptsLocationAttributeSyntax(),
            SourceMapPositions.ResolveMappedPath(lineSpan, invocation.SyntaxTree.FilePath),
            lineSpan.StartLinePosition.Line + 1,
            SourceMapPositions.CallSiteColumn(invocation, lineSpan, ct));
    }

    private static string? EmitSourceMapInterceptors(
        ImmutableArray<WrapperCallSite?> sites,
        ImmutableArray<WrapperFactory?> factories,
        bool needsPolyfill,
        ImmutableArray<KeyValuePair<string, string>> pathMap)
    {
        var byElement = new Dictionary<(string Element, string Method), WrapperFactory>();
        foreach (var factory in factories)
        {
            var key = (factory!.ElementType, factory.MethodName);
            if (!byElement.ContainsKey(key)) byElement[key] = factory;
        }

        var body = new StringBuilder();
        var index = 0;
        foreach (var site in sites)
        {
            // A site whose element produced no factory (an invalid wrapper target) is a
            // compile error in the user's code already; emitting nothing keeps it the only one.
            if (!byElement.TryGetValue((site!.ElementType, site.MethodName), out var factory)) continue;

            var parameters = string.Join(", ", factory.ParameterTypes.Select(static (t, i) => $"{t} __a{i}"));
            var arguments = string.Join(", ", factory.ParameterTypes.Select(static (_, i) => $"__a{i}"));
            var mapped = SourceMapPositions.ApplyPathMap(site.FilePath, pathMap);

            body.AppendLine($"        {site.Attribute}");
            body.AppendLine($"        public static {factory.ElementType} __ReactorWrapper_{factory.MethodName}_{index}({parameters})");
            body.AppendLine("        {");
            body.AppendLine($"            var __e = {factory.ElementType}.{factory.MethodName}({arguments});");
            body.AppendLine("            if (!global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled) return __e;");
            // First stamp wins, as in every Reactor interceptor.
            body.AppendLine("            if (__e.CallSite is not null) return __e;");
            body.AppendLine("            return __e with");
            body.AppendLine("            {");
            body.AppendLine($"                CallSite = new global::Microsoft.UI.Reactor.Core.SourceLocation({SourceMapPositions.Literal(mapped)}, {site.Line}, {site.Column})");
            body.AppendLine("            };");
            body.AppendLine("        }");
            body.AppendLine();
            index++;
        }

        if (index == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Reactor source map — interceptors for factories generated by [GenerateReactorWrapper].");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (needsPolyfill)
        {
            sb.AppendLine("namespace System.Runtime.CompilerServices");
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
            sb.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
            sb.AppendLine("    {");
            sb.AppendLine("        public InterceptsLocationAttribute(int version, string data) { _ = version; _ = data; }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }
        sb.AppendLine($"namespace {SourceMapPositions.InterceptorNamespace}");
        sb.AppendLine("{");
        sb.AppendLine("    file static class ReactorWrapperSourceMapInterceptors");
        sb.AppendLine("    {");
        sb.Append(body);
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// The declared type of one generated factory parameter, from the declaration string
    /// the factory was emitted with: <c>"global::A.B? x = default"</c> → <c>"global::A.B?"</c>,
    /// <c>"params object[] items"</c> → <c>"params object[]"</c>. The name never contains a
    /// space, so the type is everything before the last one.
    /// </summary>
    private static string FactoryParameterType(string declaration)
    {
        var equals = declaration.IndexOf(" = ", System.StringComparison.Ordinal);
        var head = equals >= 0 ? declaration.Substring(0, equals) : declaration;
        return head.Substring(0, head.LastIndexOf(' '));
    }

    /// <summary>One call to a wrapper-generated factory.</summary>
    private sealed record WrapperCallSite(
        string ElementType,
        string MethodName,
        string Attribute,
        string FilePath,
        int Line,
        int Column);

    /// <summary>
    /// The shape of one generated factory: the element type it is declared on (fully
    /// qualified), its name, and its parameter types exactly as declared.
    /// </summary>
    private sealed record WrapperFactory(string ElementType, string MethodName, ImmutableArray<string> ParameterTypes)
    {
        public bool Equals(WrapperFactory? other)
            => other is not null
               && ElementType == other.ElementType
               && MethodName == other.MethodName
               && ParameterTypes.SequenceEqual(other.ParameterTypes);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = ElementType.GetHashCode() * 397 ^ MethodName.GetHashCode();
                foreach (var t in ParameterTypes) h = h * 397 ^ t.GetHashCode();
                return h;
            }
        }
    }
}
