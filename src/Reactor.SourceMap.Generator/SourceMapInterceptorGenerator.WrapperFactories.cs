using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.SourceMap.Generator;

/// <summary>
/// Calls that involve a factory <c>Reactor.Wrappers.Generator</c> emits for a
/// <c>[GenerateReactorWrapper]</c> element.
///
/// <para>Roslyn runs every generator against the same input compilation, so here such a
/// factory does not exist: <c>SettingsCard(...)</c> binds to nothing and has an error type.
/// That has two consequences, handled separately:</para>
/// <list type="bullet">
///   <item>the wrapper call itself cannot be intercepted by this generator, which does not
///   know the factory's signature. <c>Reactor.Wrappers.Generator</c> does, and intercepts
///   it; this generator only records the site for the static facts;</item>
///   <item>a DSL call that takes one as an argument — <c>VStack(8, SettingsCard(...))</c>,
///   <c>HStack(8, gauge)</c> where <c>var gauge = RadialGauge(...).Width(140)</c> — either
///   fails overload resolution or resolves against an error type. It is bound again with
///   every such sub-expression replaced by <c>default(TheElement)</c>, which has exactly
///   the type the real compilation will see.</item>
/// </list>
/// </summary>
public sealed partial class SourceMapInterceptorGenerator
{
    /// <summary>Bounds how far a <c>var</c> local's initializer is followed to find its type.</summary>
    private const int MaxLocalInferenceDepth = 4;

    /// <summary>
    /// True for a method on <c>Microsoft.UI.Reactor.Factories</c>, and for a wrapper-generated
    /// factory in the (unusual) compilation where it is visible — see the comment at the use
    /// site in <c>TryDescribe</c>. <paramref name="isWrapperFactory"/> tells the two apart.
    /// </summary>
    private static bool IsDslFactory(IMethodSymbol method, out bool isWrapperFactory)
    {
        isWrapperFactory = false;
        if (method.ContainingType is not { } owner) return false;
        if (owner.ToDisplayString() == FactoriesMetadataName) return true;

        // Only the GENERATED factory: one that comes from metadata (a referenced library) or
        // from a tree that is itself wrapper-generator output fed back in. A hand-written
        // static of the same name on the author's partial record is not one — in a normal
        // build the generated overloads beside it are still invisible, so which of them the
        // real compilation picks is unknowable here.
        isWrapperFactory = method.IsStatic
            && WrapperFactoryCalls.IsWrapperFactoryOwner(owner, method.Name)
            && method.Locations.All(static l => l.IsInMetadata
                || (l.SourceTree?.FilePath.EndsWith(".Wrapper.g.cs", System.StringComparison.OrdinalIgnoreCase) ?? false));
        return isWrapperFactory;
    }
    /// <summary>
    /// A call to a wrapper-generated factory, recorded without a signature: its declared
    /// name and position feed the static facts, and the interceptor comes from
    /// <c>Reactor.Wrappers.Generator</c>.
    /// </summary>
    private static CallSite? DescribeWrapperFactorySite(
        GeneratorSyntaxContext ctx,
        InvocationExpressionSyntax invocation,
        System.Threading.CancellationToken ct)
    {
        var compilation = ctx.SemanticModel.Compilation;
        if (compilation.GetTypeByMetadataName(ElementMetadataName) is not { } elementSymbol) return null;

        // The wrapper generator applies the same transparency rule, so a call it will not
        // stamp is not recorded here either.
        if (IsInsideTransparentMethod(ctx.SemanticModel, invocation, compilation, elementSymbol, ct)) return null;

        if (ctx.SemanticModel.GetInterceptableLocation(invocation, ct) is not { } location) return null;

        var lineSpan = invocation.SyntaxTree.GetMappedLineSpan(invocation.ArgumentList.OpenParenToken.Span, ct);
        return new CallSite(
            attribute: location.GetInterceptsLocationAttributeSyntax(),
            filePath: ResolveMappedPath(lineSpan, invocation.SyntaxTree.FilePath),
            line: lineSpan.StartLinePosition.Line + 1,
            column: CallSiteColumn(invocation, lineSpan, ct),
            declaredName: DeclaredNameOf(invocation, ctx.SemanticModel, elementSymbol, ct),
            signature: null,
            argumentStamps: ImmutableArray<ArgumentStamp>.Empty);
    }

    /// <summary>
    /// True when an argument of an already-resolved call has a type the real compilation
    /// may disagree with: an error type, or a type parameter that is not in scope (the type
    /// <c>RadialGauge(...).Width(140)</c> reports, since <c>Width&lt;T&gt;</c> could not infer
    /// <c>T</c> from an error-typed receiver). Reads cached binding results only.
    /// </summary>
    private static bool HasUnresolvedArgument(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        System.Threading.CancellationToken ct)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            switch (model.GetTypeInfo(argument.Expression, ct).Type)
            {
                case IErrorTypeSymbol:
                    return true;
                case ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } tp
                    when !IsInScope(tp, model, invocation.SpanStart, ct):
                    return true;
            }
        }
        return false;

        static bool IsInScope(ITypeParameterSymbol tp, SemanticModel model, int position, System.Threading.CancellationToken ct)
        {
            for (var symbol = model.GetEnclosingSymbol(position, ct); symbol is not null; symbol = symbol.ContainingSymbol)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, tp.DeclaringMethod)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Binds <paramref name="invocation"/> again with every wrapper-factory call (and every
    /// <c>var</c> local initialized from one) in its arguments replaced by a value of the
    /// element type. <see cref="SpeculativeBinding.Changed"/> is false when there was
    /// nothing to replace, so the original binding stands.
    ///
    /// <para><see cref="SpeculativeBinding.Method"/> is null (decline) whenever the
    /// substitution could steer overload resolution somewhere the real compilation will not
    /// go. The placeholder is typed <c>Element</c>, while the real argument is the wrapper
    /// record, which derives from <c>Element</c> directly; the only overloads that record can
    /// reach and <c>Element</c> cannot are ones taking the record itself or a type parameter.
    /// An interceptor for the wrong overload is a hard CS9144 in the consumer's build, so a
    /// method group containing either shape is not re-bound.</para>
    /// </summary>
    private static SpeculativeBinding BindAroundWrapperFactoryCalls(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        System.Threading.CancellationToken ct)
    {
        var rewriter = new WrapperFactoryCallRewriter(model, ct, depth: 0, visiting: new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
        var rewritten = invocation.WithArgumentList((ArgumentListSyntax)rewriter.Visit(invocation.ArgumentList)!);
        if (!rewriter.Changed) return new SpeculativeBinding(false, null);
        if (rewriter.Unreliable) return new SpeculativeBinding(true, null);

        var info = model.GetSpeculativeSymbolInfo(invocation.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression);
        if (info.Symbol is not IMethodSymbol method || !OverloadChoiceIsStable(method, model.Compilation))
            return new SpeculativeBinding(true, null);
        return new SpeculativeBinding(true, method);
    }

    /// <summary>See <see cref="BindAroundWrapperFactoryCalls"/>: false when some overload of
    /// <paramref name="method"/> could be preferred for a wrapper-typed argument.</summary>
    private static bool OverloadChoiceIsStable(IMethodSymbol method, Compilation compilation)
    {
        if (method.ContainingType is not { } owner) return false;
        foreach (var member in owner.GetMembers(method.Name))
        {
            if (member is not IMethodSymbol overload) continue;
            foreach (var parameter in overload.Parameters)
            {
                var type = parameter.Type is IArrayTypeSymbol array ? array.ElementType : parameter.Type;
                if (type is ITypeParameterSymbol) return false;
                if (type is INamedTypeSymbol named && WrapperFactoryCalls.IsIncompleteWrapperElement(named, compilation)) return false;
            }
        }
        return true;
    }

    private readonly struct SpeculativeBinding
    {
        public SpeculativeBinding(bool changed, IMethodSymbol? method)
        {
            Changed = changed;
            Method = method;
        }

        public bool Changed { get; }
        public IMethodSymbol? Method { get; }
    }

    /// <summary>
    /// Replaces wrapper-factory calls with <c>(default(global::Ns.FooElement))</c>. Visits
    /// ORIGINAL nodes (the rewriter is handed the original tree), so every semantic
    /// question it asks is answered from the real compilation.
    /// </summary>
    private sealed class WrapperFactoryCallRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _model;
        private readonly System.Threading.CancellationToken _ct;
        private readonly int _depth;
        private readonly HashSet<ILocalSymbol> _visiting;

        public WrapperFactoryCallRewriter(SemanticModel model, System.Threading.CancellationToken ct, int depth, HashSet<ILocalSymbol> visiting)
        {
            _model = model;
            _ct = ct;
            _depth = depth;
            _visiting = visiting;
        }

        public bool Changed { get; private set; }

        /// <summary>True when a substitution could not be made faithfully; the binding must not be trusted.</summary>
        public bool Unreliable { get; private set; }

        private bool MentionsIncompleteWrapper(ITypeSymbol type) => type switch
        {
            IArrayTypeSymbol array => MentionsIncompleteWrapper(array.ElementType),
            INamedTypeSymbol named => WrapperFactoryCalls.IsIncompleteWrapperElement(named, _model.Compilation)
                || named.TypeArguments.Any(MentionsIncompleteWrapper),
            _ => false,
        };

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            // A value already typed as a wrapper record — an explicitly typed local, a field,
            // a helper returning `SettingsCardElement` — binds fine here, but to a record that
            // does not yet derive from Element (that base is generator-emitted too), so the
            // containing DSL call still fails. Replace the whole value, not its parts.
            if (node is ExpressionSyntax expression && IncompleteWrapperValueType(expression) is { } type)
            {
                return Placeholder(type, expression);
            }

            return base.Visit(node);
        }

        private INamedTypeSymbol? IncompleteWrapperValueType(ExpressionSyntax expression)
        {
            if (SyntaxFacts.IsInTypeOnlyContext(expression)) return null;
            if (expression.Parent is MemberAccessExpressionSyntax member && member.Name == expression) return null;
            if (expression.Parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None)) return null;
            if (_model.GetTypeInfo(expression, _ct).Type is not INamedTypeSymbol type
                || !WrapperFactoryCalls.IsIncompleteWrapperElement(type, _model.Compilation))
            {
                return null;
            }

            // `SettingsCardElement.SettingsCard(...)`: the qualifier names the type, it is not a value.
            return _model.GetSymbolInfo(expression, _ct).Symbol is INamespaceOrTypeSymbol ? null : type;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (_model.GetSymbolInfo(node, _ct) is { Symbol: null, CandidateSymbols.IsEmpty: true }
                && WrapperFactoryCalls.ResolveUnboundCall(_model, node, _ct) is { } element)
            {
                return Placeholder(element, node);
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Only a value read: not the name half of `x.Name`, not a by-ref argument.
            if (node.Parent is MemberAccessExpressionSyntax member && member.Name == node) return node;
            if (node.Parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None)) return node;

            if (_model.GetSymbolInfo(node, _ct).Symbol is ILocalSymbol { Type: IErrorTypeSymbol } local
                && InferLocalType(local) is { } type)
            {
                return Placeholder(type, node);
            }

            return node;
        }

        /// <summary>
        /// The type a <c>var</c> local will have once its initializer's wrapper calls exist,
        /// found by speculatively binding that initializer the same way.
        /// </summary>
        private ITypeSymbol? InferLocalType(ILocalSymbol local)
        {
            if (_depth >= MaxLocalInferenceDepth || !_visiting.Add(local)) return null;
            try
            {
                if (local.DeclaringSyntaxReferences.Length != 1
                    || local.DeclaringSyntaxReferences[0] is not { } reference
                    || reference.SyntaxTree != _model.SyntaxTree
                    || reference.GetSyntax(_ct) is not VariableDeclaratorSyntax { Initializer.Value: { } initializer })
                {
                    return null;
                }

                var inner = new WrapperFactoryCallRewriter(_model, _ct, _depth + 1, _visiting);
                var rewritten = (ExpressionSyntax)inner.Visit(initializer)!;
                if (inner.Unreliable) Unreliable = true;
                if (!inner.Changed || inner.Unreliable) return null;

                var type = _model.GetSpeculativeTypeInfo(initializer.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression).Type;
                return type is null or IErrorTypeSymbol ? null : type;
            }
            finally
            {
                _visiting.Remove(local);
            }
        }

        private ExpressionSyntax Placeholder(ITypeSymbol type, ExpressionSyntax original)
        {
            Changed = true;

            // The element record's `: Element` base is ALSO emitted by the wrapper generator,
            // so here a bare `partial record SettingsCardElement;` is not an element at all.
            // Stand in with Element itself; OverloadChoiceIsStable rejects the method groups
            // where that could resolve differently from the finished record.
            //
            // Only the record ITSELF is replaced. Any other inferred type (`string` from
            // `card.ToString()`) is kept verbatim, because coercing it would pick a different
            // overload (`Button(Element, …)` over `Button(string, …)`). A type that merely
            // CONTAINS the record (`List<SettingsCardElement>`) is not faithfully expressible
            // here — its variance conversions do not exist yet — so the binding is abandoned.
            if (type is INamedTypeSymbol named && WrapperFactoryCalls.IsIncompleteWrapperElement(named, _model.Compilation))
            {
                type = _model.Compilation.GetTypeByMetadataName(ElementMetadataName) ?? type;
            }
            else if (MentionsIncompleteWrapper(type))
            {
                Unreliable = true;
            }

            var typeSyntax = SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.DefaultExpression(typeSyntax))
                .WithTriviaFrom(original);
        }
    }

    /// <summary>
    /// Mechanism 2 (argument-position stamping) for a call this generator resolved
    /// speculatively, where there is no <c>IInvocationOperation</c> to read the conversions
    /// from. Covers the shape that matters — positional arguments, a <c>params Element?[]</c>
    /// in expanded form — and asks the compiler to classify each candidate argument's
    /// conversion to <c>Element</c> directly. Anything it cannot map with certainty is left
    /// unstamped, which reports "unknown" rather than a wrong line.
    /// </summary>
    private static ImmutableArray<ArgumentStamp> DescribeArgumentStampsWithoutOperation(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        INamedTypeSymbol elementSymbol,
        System.Threading.CancellationToken ct)
    {
        if (!CouldHaveConvertedArguments(method, elementSymbol)) return ImmutableArray<ArgumentStamp>.Empty;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Any(static a => a.NameColon is not null || !a.RefKindKeyword.IsKind(SyntaxKind.None)))
            return ImmutableArray<ArgumentStamp>.Empty;

        var parameters = method.Parameters;
        var paramsOrdinal = parameters.Length > 0 && parameters[parameters.Length - 1].IsParams ? parameters.Length - 1 : -1;

        // Expanded form unless exactly one argument lands on the params slot and it is
        // already an array — the caller's own array, which is never written into.
        var expanded = paramsOrdinal >= 0
            && !(arguments.Count == parameters.Length
                 && model.GetTypeInfo(arguments[paramsOrdinal].Expression, ct).Type is IArrayTypeSymbol);

        ImmutableArray<ArgumentStamp>.Builder? builder = null;
        for (int i = 0; i < arguments.Count; i++)
        {
            int ordinal;
            int arrayIndex;
            ITypeSymbol? target;
            if (paramsOrdinal >= 0 && i >= paramsOrdinal && expanded)
            {
                ordinal = paramsOrdinal;
                arrayIndex = i - paramsOrdinal;
                target = (parameters[paramsOrdinal].Type as IArrayTypeSymbol)?.ElementType;
            }
            else if (i < parameters.Length)
            {
                ordinal = i;
                arrayIndex = -1;
                target = parameters[i].Type;
            }
            else
            {
                break;
            }

            if (!IsElementItself(target, elementSymbol)) continue;

            var expression = arguments[i].Expression;
            var conversion = model.ClassifyConversion(expression, elementSymbol);
            if (!conversion.Exists || !conversion.IsImplicit || !conversion.IsUserDefined || conversion.MethodSymbol is null)
                continue;
            if (!IsElementItself(conversion.MethodSymbol.ReturnType, elementSymbol)) continue;

            var lineSpan = expression.SyntaxTree.GetMappedLineSpan(expression.Span, ct);
            (builder ??= ImmutableArray.CreateBuilder<ArgumentStamp>()).Add(new ArgumentStamp(
                ordinal,
                arrayIndex,
                ResolveMappedPath(lineSpan, expression.SyntaxTree.FilePath),
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1));
        }

        return builder is null ? ImmutableArray<ArgumentStamp>.Empty : builder.ToImmutable();
    }
}
