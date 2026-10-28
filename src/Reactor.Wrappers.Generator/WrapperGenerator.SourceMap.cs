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

        // Cheap rejections first, in cost order: a name no wrapper declared here will get
        // (no binding at all), then a call that binds or has candidates, which a call to a
        // generated factory never does in this compilation.
        if (!WrapperFactoryCalls.SourceFactoryNames(model.Compilation).Contains(WrapperFactoryCalls.CandidateName(invocation)!)) return null;
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
        var arguments = invocation.ArgumentList.Arguments;
        return new WrapperCallSite(
            element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            WrapperFactoryCalls.CandidateName(invocation)!,
            location.GetInterceptsLocationAttributeSyntax(),
            SourceMapPositions.ResolveMappedPath(lineSpan, invocation.SyntaxTree.FilePath),
            lineSpan.StartLinePosition.Line + 1,
            SourceMapPositions.CallSiteColumn(invocation, lineSpan, ct),
            arguments.Count,
            arguments.Count > 0
                && model.ClassifyConversion(arguments[arguments.Count - 1].Expression, model.Compilation.CreateArrayTypeSymbol(elementSymbol)) is { Exists: true, IsImplicit: true },
            ConvertedArguments(model, arguments, elementSymbol, ct));
    }

    /// <summary>
    /// The arguments that reach an <c>Element</c> through an implicit user-defined conversion
    /// (<c>FakePanel("label")</c>: the string becomes a TextBlock), which the interceptor
    /// stamps with the argument's own position — the same mechanism the source-map generator
    /// applies to every other factory. Which parameter each lands on is only known once the
    /// factory's shape is (<see cref="StampsFor"/>); an argument passed by reference is
    /// never written back, so a call with one gets none.
    /// </summary>
    private static ImmutableArray<ConvertedArgument> ConvertedArguments(
        SemanticModel model,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        INamedTypeSymbol elementSymbol,
        CancellationToken ct)
    {
        if (arguments.Any(static a => !a.RefKindKeyword.IsKind(SyntaxKind.None))) return ImmutableArray<ConvertedArgument>.Empty;

        ImmutableArray<ConvertedArgument>.Builder? builder = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            var expression = arguments[i].Expression;
            var conversion = model.ClassifyConversion(expression, elementSymbol);
            // Exactly Element out, as the source-map generator requires: the stamped value is
            // written back into an Element-typed slot.
            if (!conversion.Exists || !conversion.IsImplicit || !conversion.IsUserDefined
                || conversion.MethodSymbol is not { } op
                || !SymbolEqualityComparer.Default.Equals(op.ReturnType, elementSymbol)
                || !TypeIsSettled(model, expression, ct))
            {
                continue;
            }
            var span = expression.SyntaxTree.GetMappedLineSpan(expression.Span, ct);
            (builder ??= ImmutableArray.CreateBuilder<ConvertedArgument>()).Add(new ConvertedArgument(
                i,
                arguments[i].NameColon?.Name.Identifier.ValueText,
                SourceMapPositions.ResolveMappedPath(span, expression.SyntaxTree.FilePath),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1));
        }
        return builder?.ToImmutable() ?? ImmutableArray<ConvertedArgument>.Empty;
    }

    /// <summary>
    /// True when nothing in <paramref name="argument"/> can bind differently once the wrapper
    /// records are complete, so its type — and with it the conversion to Element, and whether
    /// it is a whole params array — is the real compilation's. Fails closed on anything this
    /// compilation cannot see (an error type, a type tainted by a wrapper record, a method
    /// group naming one, a failed overload resolution) and on anything whose type is inferred
    /// from elsewhere (an implicitly typed local or lambda parameter, a query variable):
    /// <c>FakePanel(Children(gauge))</c> is a string here and may be an <c>Element[]</c> there.
    /// </summary>
    private static bool TypeIsSettled(SemanticModel model, ExpressionSyntax argument, CancellationToken ct)
    {
        var compilation = model.Compilation;
        foreach (var expression in argument.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            var info = model.GetTypeInfo(expression, ct);
            if (info.Type is IErrorTypeSymbol && expression is not OmittedArraySizeExpressionSyntax) return false;
            if (info.Type is { } type && WrapperFactoryCalls.MentionsIncompleteWrapper(type, compilation)) return false;
            if (info.ConvertedType is { } converted && WrapperFactoryCalls.MentionsIncompleteWrapper(converted, compilation)) return false;

            if (WrapperFactoryCalls.HiddenFactoryMayChange(model, expression, ct)
                || WrapperFactoryCalls.GeneratedMemberMayWin(model, expression, ct)
                || (expression is InvocationExpressionSyntax call && WrapperFactoryCalls.GroupHasWrapperConstraint(model, call, ct)))
            {
                return false;
            }

            var symbols = model.GetSymbolInfo(expression, ct);
            if (symbols.Symbol is null && !symbols.CandidateSymbols.IsEmpty) return false;
            foreach (var symbol in symbols.Symbol is { } single ? ImmutableArray.Create(single) : symbols.CandidateSymbols)
            {
                switch (symbol)
                {
                    case ILocalSymbol local when !IsExplicitlyTyped(local, ct):
                    case IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } } parameter
                        when parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is not ParameterSyntax { Type: not null }:
                    case IRangeVariableSymbol:
                        return false;
                    // A method group whose constraints mention a record converts differently once
                    // the record is complete (`Read(Kids<IComparer<Element>>)`).
                    case IMethodSymbol when expression is SimpleNameSyntax or MemberAccessExpressionSyntax
                                            && expression.Parent is not InvocationExpressionSyntax
                                            && WrapperFactoryCalls.NameReachesWrapperConstraint(model, expression, ct):
                    case IMethodSymbol method
                        when WrapperFactoryCalls.MentionsIncompleteWrapper(method.ReturnType, compilation)
                             || method.Parameters.Any(p => WrapperFactoryCalls.MentionsIncompleteWrapper(p.Type, compilation))
                             || method.TypeArguments.Any(a => WrapperFactoryCalls.MentionsIncompleteWrapper(a, compilation)):
                        return false;
                }
            }
        }
        return true;

        // Only a declaration that writes the type down; `var`, deconstruction, foreach and
        // pattern variables take theirs from an expression.
        static bool IsExplicitlyTyped(ILocalSymbol local, CancellationToken ct)
            => local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) switch
            {
                VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } => !declaration.Type.IsVar,
                SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax declaration } => !declaration.Type.IsVar,
                SingleVariableDesignationSyntax { Parent: DeclarationPatternSyntax pattern } => !pattern.Type.IsVar,
                ForEachStatementSyntax forEach => !forEach.Type.IsVar,
                _ => false,
            };
    }

    /// <summary>
    /// Where each converted argument of <paramref name="site"/> lands in
    /// <paramref name="factory"/>'s parameters: <c>(ordinal, -1)</c> for an <c>Element</c>
    /// parameter, <c>(ordinal, slot)</c> for an element of a <c>params Element[]</c> passed in
    /// expanded form. Normal form — one argument on the params slot that converts to the
    /// array itself — is the caller's own array, and is never written into.
    /// </summary>
    private static List<(int Ordinal, int Slot, ConvertedArgument Argument)> StampsFor(WrapperCallSite site, WrapperFactory factory)
    {
        var stamps = new List<(int, int, ConvertedArgument)>();
        var types = factory.Parameters.Select(FactoryParameterType).ToList();
        var names = factory.Parameters.Select(FactoryParameterName).ToList();
        var count = types.Count;
        var paramsOrdinal = count > 0 && types[count - 1].StartsWith("params ", System.StringComparison.Ordinal) ? count - 1 : -1;
        var expanded = paramsOrdinal >= 0 && !(site.ArgumentCount == count && site.LastArgumentIsAnElementArray);

        foreach (var argument in site.ConvertedArguments)
        {
            if (argument.Name is { } name)
            {
                var ordinal = names.IndexOf(name);
                if (ordinal >= 0 && ordinal != paramsOrdinal && IsElement(types[ordinal])) stamps.Add((ordinal, -1, argument));
            }
            else if (paramsOrdinal >= 0 && argument.Index >= paramsOrdinal && expanded)
            {
                if (IsElement(types[paramsOrdinal].Substring("params ".Length).TrimEnd('[', ']')))
                    stamps.Add((paramsOrdinal, argument.Index - paramsOrdinal, argument));
            }
            else if (argument.Index < count && argument.Index != paramsOrdinal && IsElement(types[argument.Index]))
            {
                stamps.Add((argument.Index, -1, argument));
            }
        }
        return stamps;

        static bool IsElement(string type) => type is "global::Microsoft.UI.Reactor.Core.Element" or "global::Microsoft.UI.Reactor.Core.Element?";
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
        var needsArgumentHelper = false;
        // A site whose element produced no factory (an invalid wrapper target) is a compile
        // error in the user's code already; emitting nothing keeps it the only one.
        var resolved = sites
            .Select(s => (Site: s!, Factory: byElement.TryGetValue((s!.ElementType, s.MethodName), out var f) ? f : null))
            .Where(static p => p.Factory is not null);
        foreach (var (site, resolvedFactory) in resolved)
        {
            var factory = resolvedFactory!;

            var parameters = string.Join(", ", factory.Parameters.Select(static (p, i) => $"{FactoryParameterType(p)} __a{i}"));
            var arguments = string.Join(", ", factory.Parameters.Select(static (_, i) => $"__a{i}"));
            var mapped = SourceMapPositions.ApplyPathMap(site.FilePath, pathMap);
            var stamps = StampsFor(site, factory);

            body.AppendLine($"        {site.Attribute}");
            body.AppendLine($"        public static {factory.ElementType} __ReactorWrapper_{factory.MethodName}_{index}({parameters})");
            body.AppendLine("        {");
            if (stamps.Count == 0)
            {
                body.AppendLine($"            var __e = {factory.ElementType}.{factory.MethodName}({arguments});");
                body.AppendLine("            if (!global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled) return __e;");
            }
            else
            {
                // As in the source-map generator: arguments are stamped before the factory
                // copies them, under one read of the process-global flag.
                needsArgumentHelper = true;
                body.AppendLine("            var __on = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled;");
                body.AppendLine("            if (__on)");
                body.AppendLine("            {");
                foreach (var (ordinal, slot, argument) in stamps)
                {
                    var target = slot < 0 ? $"__a{ordinal}" : $"__a{ordinal}[{slot}]";
                    var path = SourceMapPositions.Literal(SourceMapPositions.ApplyPathMap(argument.FilePath, pathMap));
                    body.AppendLine($"                {target} = __ReactorStampArgument({target}, {path}, {argument.Line}, {argument.Column})!;");
                }
                body.AppendLine("            }");
                body.AppendLine($"            var __e = {factory.ElementType}.{factory.MethodName}({arguments});");
                body.AppendLine("            if (!__on) return __e;");
            }
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

        if (needsArgumentHelper)
        {
            // Same helper the source-map generator emits: first stamp wins, and the shared
            // EmptyElement singleton is never cloned.
            body.AppendLine("        private static global::Microsoft.UI.Reactor.Core.Element? __ReactorStampArgument(");
            body.AppendLine("            global::Microsoft.UI.Reactor.Core.Element? __value, string __file, int __line, int __column)");
            body.AppendLine("        {");
            body.AppendLine("            if (__value is null) return __value;");
            body.AppendLine("            if (__value.CallSite is not null) return __value;");
            body.AppendLine("            if (__value is global::Microsoft.UI.Reactor.Core.EmptyElement) return __value;");
            body.AppendLine("            return __value with");
            body.AppendLine("            {");
            body.AppendLine("                CallSite = new global::Microsoft.UI.Reactor.Core.SourceLocation(__file, __line, __column)");
            body.AppendLine("            };");
            body.AppendLine("        }");
            body.AppendLine();
        }

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
        var head = DeclarationHead(declaration);
        return head.Substring(0, head.LastIndexOf(' '));
    }

    /// <summary>The parameter's name, from the same declaration string: <c>"global::A.B? x = default"</c> → <c>"x"</c>.</summary>
    private static string FactoryParameterName(string declaration)
    {
        var head = DeclarationHead(declaration);
        var name = head.Substring(head.LastIndexOf(' ') + 1);
        return name.StartsWith("@", System.StringComparison.Ordinal) ? name.Substring(1) : name;
    }

    private static string DeclarationHead(string declaration)
    {
        var equals = declaration.IndexOf(" = ", System.StringComparison.Ordinal);
        return equals >= 0 ? declaration.Substring(0, equals) : declaration;
    }

    /// <summary>One argument that reaches an Element through a user-defined conversion.</summary>
    private sealed record ConvertedArgument(int Index, string? Name, string FilePath, int Line, int Column);

    /// <summary>One call to a wrapper-generated factory.</summary>
    private sealed record WrapperCallSite(
        string ElementType,
        string MethodName,
        string Attribute,
        string FilePath,
        int Line,
        int Column,
        int ArgumentCount,
        bool LastArgumentIsAnElementArray,
        ImmutableArray<ConvertedArgument> ConvertedArguments)
    {
        public bool Equals(WrapperCallSite? other)
            => other is not null
               && ElementType == other.ElementType
               && MethodName == other.MethodName
               && Attribute == other.Attribute
               && FilePath == other.FilePath
               && Line == other.Line
               && Column == other.Column
               && ArgumentCount == other.ArgumentCount
               && LastArgumentIsAnElementArray == other.LastArgumentIsAnElementArray
               && ConvertedArguments.SequenceEqual(other.ConvertedArguments);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Attribute.GetHashCode() * 397 ^ Line;
                foreach (var a in ConvertedArguments) h = h * 397 ^ a.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// The shape of one generated factory: the element type it is declared on (fully
    /// qualified), its name, and its parameter declarations exactly as emitted.
    /// </summary>
    private sealed record WrapperFactory(string ElementType, string MethodName, ImmutableArray<string> Parameters)
    {
        public bool Equals(WrapperFactory? other)
            => other is not null
               && ElementType == other.ElementType
               && MethodName == other.MethodName
               && Parameters.SequenceEqual(other.Parameters);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = ElementType.GetHashCode() * 397 ^ MethodName.GetHashCode();
                foreach (var t in Parameters) h = h * 397 ^ t.GetHashCode();
                return h;
            }
        }
    }
}
