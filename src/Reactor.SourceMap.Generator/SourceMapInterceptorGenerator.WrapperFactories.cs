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

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, System.Runtime.CompilerServices.StrongBox<bool>> s_hasIncompleteWrappers = new();

    /// <summary>
    /// True when this compilation declares a <c>[GenerateReactorWrapper]</c> record whose
    /// generated half it cannot see. Everything in this file is skipped otherwise, so a
    /// project without wrappers pays nothing for it. Computed once per compilation from the
    /// declaration tables; no method body is bound.
    /// </summary>
    private static bool HasIncompleteWrappers(Compilation compilation)
        => s_hasIncompleteWrappers.GetValue(compilation, static c =>
            new System.Runtime.CompilerServices.StrongBox<bool>(Declares(c.SourceModule.GlobalNamespace, c))).Value;

    private static bool Declares(INamespaceSymbol ns, Compilation compilation)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            if (type.IsRecord && WrapperFactoryCalls.IsIncompleteWrapperElement(type, compilation)) return true;
        }
        foreach (var child in ns.GetNamespaceMembers())
        {
            if (Declares(child, compilation)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when an argument of an already-resolved call has a type the real compilation
    /// may disagree with: an error type, or a type parameter that is not in scope (the type
    /// <c>RadialGauge(...).Width(140)</c> reports, since <c>Width&lt;T&gt;</c> could not infer
    /// <c>T</c> from an error-typed receiver) — or when any value anywhere in the arguments
    /// is typed as a wrapper record, which binds here as something that is not an Element yet
    /// (<c>Present(card)</c> resolves to <c>Present(object)</c>, the real compilation to
    /// <c>Present(Element)</c>). Reads cached binding results only.
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

        foreach (var node in invocation.ArgumentList.DescendantNodes())
        {
            ITypeSymbol? valueType = node switch
            {
                SimpleNameSyntax name => model.GetSymbolInfo(name, ct).Symbol switch
                {
                    ILocalSymbol l => l.Type,
                    IFieldSymbol f => f.Type,
                    IPropertySymbol p => p.Type,
                    IParameterSymbol p => p.Type,
                    IMethodSymbol m => m.ReturnType,
                    _ => null,
                },
                ObjectCreationExpressionSyntax creation => model.GetTypeInfo(creation, ct).Type,
                _ => null,
            };
            if (valueType is not null && MentionsIncompleteWrapper(valueType, model.Compilation, 0)) return true;
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
    /// record, which derives from <c>Element</c> directly. Everything <c>Element</c> converts to,
    /// the record converts to as well; what only the record reaches is a parameter type that
    /// mentions the record (<c>FooElement</c>, <c>IEquatable&lt;FooElement&gt;</c>,
    /// <c>Func&lt;FooElement&gt;</c>), an interface the record implements, or a type parameter
    /// inferred from it. An interceptor for the wrong overload is a hard CS9144 in the
    /// consumer's build, so a call whose method group — every candidate visible at the call
    /// site, not just the speculative winner's type — contains such a parameter anywhere in
    /// its type is not re-bound.</para>
    /// </summary>
    private static SpeculativeBinding BindAroundWrapperFactoryCalls(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        System.Threading.CancellationToken ct)
    {
        var substituted = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var rewriter = new WrapperFactoryCallRewriter(model, ct, depth: 0, visiting: new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default), substituted, invocation.ArgumentList);
        var rewritten = invocation.WithArgumentList((ArgumentListSyntax)rewriter.Visit(invocation.ArgumentList)!);
        if (rewriter.Unreliable) return new SpeculativeBinding(true, null);
        if (!rewriter.Changed) return new SpeculativeBinding(false, null);
        if (rewriter.ResidualError) return new SpeculativeBinding(true, null);

        var info = model.GetSpeculativeSymbolInfo(invocation.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression);
        if (info.Symbol is not IMethodSymbol method || !OverloadChoiceIsStable(model, invocation, method, substituted))
            return new SpeculativeBinding(true, null);
        return new SpeculativeBinding(true, method);
    }

    /// <summary>See <see cref="BindAroundWrapperFactoryCalls"/>: false when some candidate at
    /// this call site could be preferred for a wrapper-typed argument.</summary>
    private static bool OverloadChoiceIsStable(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        HashSet<INamedTypeSymbol> substituted)
    {
        if (method.ContainingType is not { } owner) return false;

        // Every same-named method the real call could resolve to: the winner's type and its
        // bases; for `Q.M(...)`, Q and ITS bases (a more-derived overload hides nothing from
        // overload resolution); for an unqualified `M(...)`, enclosing types and every
        // `using static` import.
        var candidates = new List<ISymbol>();
        AddWithBases(candidates, owner, method.Name);
        switch (invocation.Expression)
        {
            case SimpleNameSyntax:
                candidates.AddRange(model.LookupSymbols(invocation.SpanStart, name: method.Name));
                break;
            case MemberAccessExpressionSyntax member:
                if ((model.GetSymbolInfo(member.Expression).Symbol as ITypeSymbol ?? model.GetTypeInfo(member.Expression).Type) is INamedTypeSymbol qualifier)
                    AddWithBases(candidates, qualifier, method.Name);
                break;
        }

        var interfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var wrapper in substituted)
        {
            foreach (var implemented in wrapper.AllInterfaces) interfaces.Add(implemented.OriginalDefinition);
            // A conversion operator declared on the record reaches types Element cannot.
            if (wrapper.GetMembers().Any(static m => m is IMethodSymbol { MethodKind: MethodKind.Conversion })) return false;
        }

        foreach (var candidate in candidates)
        {
            if (candidate is not IMethodSymbol overload) continue;
            foreach (var parameter in overload.Parameters)
            {
                if (ReachableOnlyByTheRecord(parameter.Type, model.Compilation, interfaces, depth: 0)) return false;
            }
        }
        return true;
    }

    private static bool MentionsIncompleteWrapper(ITypeSymbol type, Compilation compilation, int depth) => depth <= 8 && type switch
    {
        IArrayTypeSymbol array => MentionsIncompleteWrapper(array.ElementType, compilation, depth + 1),
        INamedTypeSymbol named => WrapperFactoryCalls.IsIncompleteWrapperElement(named, compilation)
            || named.TypeArguments.Any(a => MentionsIncompleteWrapper(a, compilation, depth + 1)),
        _ => false,
    };

    private static bool ContainsTypeParameter(ITypeSymbol type, int depth = 0) => depth > 8 || type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType, depth + 1),
        INamedTypeSymbol named => named.TypeArguments.Any(a => ContainsTypeParameter(a, depth + 1)),
        _ => false,
    };

    private static void AddWithBases(List<ISymbol> candidates, INamedTypeSymbol type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType) candidates.AddRange(t.GetMembers(name));
    }

    /// <summary>
    /// True when <paramref name="type"/>, at any depth (array element, type argument, delegate
    /// signature), is a type parameter, an incomplete wrapper record, or an interface such a
    /// record implements — anything that could accept the record more specifically than it
    /// accepts <c>Element</c>.
    /// </summary>
    private static bool ReachableOnlyByTheRecord(
        ITypeSymbol type, Compilation compilation, HashSet<INamedTypeSymbol> interfaces, int depth, bool insideConversion = false)
    {
        if (depth > 8) return true;
        switch (type)
        {
            case ITypeParameterSymbol:
                return true;
            case IArrayTypeSymbol array:
                return ReachableOnlyByTheRecord(array.ElementType, compilation, interfaces, depth + 1, insideConversion);
            case INamedTypeSymbol named:
                if (WrapperFactoryCalls.IsIncompleteWrapperElement(named, compilation)) return true;
                if (named.TypeKind == TypeKind.Interface && interfaces.Contains(named.OriginalDefinition)) return true;
                // `implicit operator P(FooElement)` declared on P: the record converts, Element does not.
                // Not followed further: C# applies at most one user-defined conversion.
                foreach (var member in insideConversion ? ImmutableArray<ISymbol>.Empty : named.GetMembers())
                {
                    if (member is IMethodSymbol { MethodKind: MethodKind.Conversion } conversion
                        && conversion.Parameters.Length == 1
                        && ReachableOnlyByTheRecord(conversion.Parameters[0].Type, compilation, interfaces, depth + 1, insideConversion: true))
                    {
                        return true;
                    }
                }
                foreach (var argument in named.TypeArguments)
                {
                    if (ReachableOnlyByTheRecord(argument, compilation, interfaces, depth + 1, insideConversion)) return true;
                }
                // The CONSTRUCTED signature: `delegate FooElement Maker<T>()` mentions the record
                // only there, never in `Maker<int>`'s type arguments.
                if (named.DelegateInvokeMethod is { } invoke)
                {
                    if (ReachableOnlyByTheRecord(invoke.ReturnType, compilation, interfaces, depth + 1, insideConversion)) return true;
                    foreach (var p in invoke.Parameters)
                    {
                        if (ReachableOnlyByTheRecord(p.Type, compilation, interfaces, depth + 1, insideConversion)) return true;
                    }
                }
                return false;
            default:
                return false;
        }
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
        private readonly HashSet<INamedTypeSymbol> _substituted;
        private readonly SyntaxNode _root;

        public WrapperFactoryCallRewriter(
            SemanticModel model,
            System.Threading.CancellationToken ct,
            int depth,
            HashSet<ILocalSymbol> visiting,
            HashSet<INamedTypeSymbol> substituted,
            SyntaxNode root)
        {
            _model = model;
            _ct = ct;
            _depth = depth;
            _visiting = visiting;
            _substituted = substituted;
            _root = root;
        }

        public bool Changed { get; private set; }

        /// <summary>
        /// True when a substitution could not be made faithfully, so the re-bound result must
        /// not be trusted. Fail-closed: declining costs a location, a wrong binding costs the
        /// consumer's build.
        /// </summary>
        public bool Unreliable { get; private set; }

        /// <summary>True when an error-typed value was left in place; harmless only if nothing else changed.</summary>
        public bool ResidualError { get; private set; }

        private bool MentionsIncompleteWrapper(ITypeSymbol type) => SourceMapInterceptorGenerator.MentionsIncompleteWrapper(type, _model.Compilation, 0);

        private enum NestedResult { Deterministic, PlaceholderLike, Unsafe }

        /// <summary>
        /// Walks from a substituted wrapper value up to the root, checking that every step
        /// between them gives the same answer for an <c>Element</c> placeholder as for the
        /// finished record. A step that preserves the value (parentheses, a conditional arm,
        /// <c>with</c>, a lambda's expression body, a collection element) passes it upward; a
        /// step whose result type is fixed whichever it is (a cast, an explicitly typed
        /// creation, a call whose every candidate returns the same type) ends the walk; a
        /// <c>&lt;T&gt;(this T) =&gt; T</c> modifier passes it upward. Anything else is a step
        /// whose overload choice or result type could differ, and abandons the binding.
        /// </summary>
        private void CheckPosition(ExpressionSyntax original, INamedTypeSymbol record)
        {
            if (original == _root) return;
            SyntaxNode child = original;
            for (var node = original.Parent; node is not null; child = node, node = node.Parent)
            {
                if (node == _root) return;
                switch (node)
                {
                    case ParenthesizedExpressionSyntax:
                    case PostfixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    case ConditionalExpressionSyntax c when c.Condition != child:
                    case WithExpressionSyntax w when w.Expression == child:
                    case SimpleLambdaExpressionSyntax l when l.ExpressionBody == child:
                    case ParenthesizedLambdaExpressionSyntax pl when pl.ExpressionBody == child:
                    case CollectionExpressionSyntax:
                    case ExpressionElementSyntax:
                    case InitializerExpressionSyntax:
                        continue;
                    case CastExpressionSyntax cast when cast.Expression == child:
                    case ArrayCreationExpressionSyntax:
                    case ObjectCreationExpressionSyntax:
                        return;
                    case ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.None):
                        if (argument.Parent == _root) return;
                        if (argument.Parent?.Parent is ObjectCreationExpressionSyntax) return;
                        if (argument.Parent?.Parent is InvocationExpressionSyntax call
                            && NestedCall(call, receiverPath: false, record) == NestedResult.Deterministic)
                        {
                            return;
                        }
                        Unreliable = true;
                        return;
                    case MemberAccessExpressionSyntax member
                        when member.Expression == child && member.Parent is InvocationExpressionSyntax modifier && modifier.Expression == member:
                        switch (NestedCall(modifier, receiverPath: true, record))
                        {
                            case NestedResult.Deterministic:
                                return;
                            case NestedResult.PlaceholderLike:
                                node = modifier;
                                continue;
                            default:
                                Unreliable = true;
                                return;
                        }
                    default:
                        Unreliable = true;
                        return;
                }
            }
            Unreliable = true;
        }

        /// <summary>
        /// What a call does with a substituted value in its receiver or arguments. Judged
        /// over every candidate visible at the call site, never just the one that binds here.
        /// </summary>
        private NestedResult NestedCall(InvocationExpressionSyntax call, bool receiverPath, INamedTypeSymbol record)
        {
            var position = call.SpanStart;
            var symbols = new List<ISymbol>();
            switch (call.Expression)
            {
                case MemberAccessExpressionSyntax member:
                {
                    var name = member.Name.Identifier.ValueText;
                    if (receiverPath)
                    {
                        if (_model.Compilation.GetTypeByMetadataName(ElementMetadataName) is { } element)
                            symbols.AddRange(_model.LookupSymbols(position, element, name, includeReducedExtensionMethods: true));
                        symbols.AddRange(_model.LookupSymbols(position, record, name, includeReducedExtensionMethods: true));
                    }
                    else
                    {
                        var qualifier = _model.GetSymbolInfo(member.Expression, _ct).Symbol as INamespaceOrTypeSymbol
                            ?? _model.GetTypeInfo(member.Expression, _ct).Type;
                        if (qualifier is null or IErrorTypeSymbol) return NestedResult.Unsafe;
                        symbols.AddRange(_model.LookupSymbols(position, qualifier, name, includeReducedExtensionMethods: true));
                    }
                    break;
                }
                case SimpleNameSyntax simple:
                    symbols.AddRange(_model.LookupSymbols(position, name: simple.Identifier.ValueText));
                    break;
                default:
                    if (_model.GetTypeInfo(call.Expression, _ct).Type is INamedTypeSymbol { DelegateInvokeMethod: { } direct })
                        symbols.Add(direct);
                    break;
            }

            var methods = new List<IMethodSymbol>();
            foreach (var symbol in symbols)
            {
                var invoke = symbol switch
                {
                    IMethodSymbol m => m,
                    ILocalSymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                    IFieldSymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                    IPropertySymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                    IParameterSymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                    _ => null,
                };
                if (invoke is null) return NestedResult.Unsafe;
                methods.Add((invoke.ReducedFrom ?? invoke).OriginalDefinition);
            }
            if (methods.Count == 0) return NestedResult.Unsafe;

            // Every candidate returns one fixed type: whichever wins, the caller sees that type.
            var first = methods[0].ReturnType;
            if (methods.All(m => SymbolEqualityComparer.IncludeNullability.Equals(m.ReturnType, first))
                && !ContainsTypeParameter(first) && !MentionsIncompleteWrapper(first))
            {
                return NestedResult.Deterministic;
            }

            // A fluent modifier, `T M<T>(this T, ...)`: it returns its receiver's type, so the
            // placeholder's Element and the record's own type flow through unchanged.
            if (receiverPath && methods.All(static m => m.IsExtensionMethod
                    && m.Parameters.Length > 0
                    && m.Parameters[0].Type is ITypeParameterSymbol receiver
                    && SymbolEqualityComparer.Default.Equals(m.ReturnType, receiver)
                    && m.Parameters.Skip(1).All(p => !ContainsTypeParameter(p.Type))))
            {
                return methods.Any(m => m.Parameters.Skip(1).Any(p => MentionsIncompleteWrapper(p.Type)))
                    ? NestedResult.Unsafe
                    : NestedResult.PlaceholderLike;
            }

            return NestedResult.Unsafe;
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            // A value already typed as a wrapper record — an explicitly typed local, a field,
            // a helper returning `SettingsCardElement` — binds fine here, but to a record that
            // does not yet derive from Element (that base is generator-emitted too), so the
            // containing DSL call still fails. Replace the whole value, not its parts.
            if (node is ExpressionSyntax expression && ValueType(expression) is { } valueType)
            {
                if (valueType is INamedTypeSymbol record && WrapperFactoryCalls.IsIncompleteWrapperElement(record, _model.Compilation))
                    return Placeholder(record, expression);

                // `new List<SettingsCardElement>()`: its conversions (variance to
                // IEnumerable<Element>, …) do not exist yet and cannot be stood in for.
                if (MentionsIncompleteWrapper(valueType)) Unreliable = true;
            }

            return base.Visit(node);
        }

        /// <summary>The type of <paramref name="expression"/> when it is a value (not a type name, not the name half of <c>x.Name</c>).</summary>
        private ITypeSymbol? ValueType(ExpressionSyntax expression)
        {
            if (SyntaxFacts.IsInTypeOnlyContext(expression)) return null;
            if (expression.Parent is MemberAccessExpressionSyntax member && member.Name == expression) return null;
            if (expression.Parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None)) return null;
            if (_model.GetTypeInfo(expression, _ct).Type is not { } type || type is IErrorTypeSymbol) return null;

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

            if (_model.GetSymbolInfo(node, _ct).Symbol is ILocalSymbol { Type: IErrorTypeSymbol } local)
            {
                if (InferLocalType(local) is { } type) return Placeholder(type, node);
                ResidualError = true;
            }

            return node;
        }

        /// <summary>
        /// The type a <c>var</c> local will have once its initializer's wrapper calls exist,
        /// found by speculatively binding that initializer the same way.
        /// </summary>
        private ITypeSymbol? InferLocalType(ILocalSymbol local)
        {
            if (_depth >= MaxLocalInferenceDepth || !_visiting.Add(local))
            {
                Unreliable = true;
                return null;
            }
            try
            {
                if (local.DeclaringSyntaxReferences.Length != 1
                    || local.DeclaringSyntaxReferences[0] is not { } reference
                    || reference.SyntaxTree != _model.SyntaxTree
                    || reference.GetSyntax(_ct) is not VariableDeclaratorSyntax { Initializer.Value: { } initializer })
                {
                    return null;
                }

                var inner = new WrapperFactoryCallRewriter(_model, _ct, _depth + 1, _visiting, _substituted, initializer);
                var rewritten = (ExpressionSyntax)inner.Visit(initializer)!;
                if (inner.Unreliable || (inner.Changed && inner.ResidualError)) Unreliable = true;
                if (!inner.Changed || Unreliable) return null;

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
                _substituted.Add(named);
                CheckPosition(original, named);
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
