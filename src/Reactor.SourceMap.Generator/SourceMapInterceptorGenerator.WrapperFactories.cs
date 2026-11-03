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
///   every such sub-expression replaced by <c>default(Element)</c> — which the finished
///   record converts to and overloads like — and the result is accepted only when nothing in
///   the call could bind differently once the record is complete (OverloadChoiceIsStable,
///   the accounting invariant); otherwise the call is left unstamped.</item>
/// </list>
///
/// <para>A possible follow-up would bind against a compilation augmented with the wrapper
/// generator's output (re-running its emit here), which would make every such call bind
/// exactly. It was not taken because it duplicates the wrapper generator's emit and costs a
/// second semantic model per affected tree; the placeholder binding is a strict subset of it.</para>
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
    private static bool IsDslFactory(IMethodSymbol method, Compilation compilation, out bool isWrapperFactory)
    {
        isWrapperFactory = false;
        if (method.ContainingType is not { } owner) return false;
        if (owner.ToDisplayString() == FactoriesMetadataName) return true;

        // Only where every overload is known: a factory declared outside this compilation (a
        // referenced library, as metadata or — under `dotnet watch` — as another project's
        // source) or in a tree that is wrapper-generator output fed back in. A hand-written
        // static of the same name on the author's own partial record is not one — in a normal
        // build the generated overloads beside it are still invisible, so which of them the
        // real compilation picks is unknowable here.
        isWrapperFactory = method.IsStatic
            && WrapperFactoryCalls.IsWrapperFactoryOwner(owner, method.Name)
            && method.Locations.All(l => l.IsInMetadata
                || l.SourceTree is null
                || !compilation.ContainsSyntaxTree(l.SourceTree)
                || l.SourceTree.FilePath.EndsWith(".Wrapper.g.cs", System.StringComparison.OrdinalIgnoreCase));
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
    /// True when this compilation declares a <c>[GenerateReactorWrapper]</c> record, whose
    /// generated factory it cannot see — whether or not the record also waits for its
    /// generated <c>: Element</c> base (one that spells the base itself is complete, but its
    /// factory is still invisible). Everything in this file is skipped otherwise, so a
    /// project without wrappers pays nothing for it. Computed once per compilation from the
    /// declarations; no method body is bound.
    /// </summary>
    private static bool HasSourceWrappers(Compilation compilation)
        => !WrapperFactoryCalls.SourceFactoryNames(compilation).IsEmpty;

    /// <summary>
    /// True when the wrapper analysis can matter for <paramref name="node"/>: the compilation
    /// declares wrapper records AND the member containing the call could touch one.
    /// </summary>
    private static bool WrappersInPlay(SemanticModel model, SyntaxNode node, System.Threading.CancellationToken ct)
        => HasSourceWrappers(model.Compilation) && !MemberIsWrapperFree(model, node, ct);

    private const string AnyWrapperOperator = "#operator";

    /// <summary>Members the language calls without their name appearing at the use site.</summary>
    private static readonly ImmutableHashSet<string> ImplicitlyCalledNames = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "Deconstruct", "GetEnumerator", "GetAsyncEnumerator", "Current", "MoveNext", "MoveNextAsync",
        "GetAwaiter", "GetResult", "Add", "Invoke", "Dispose", "DisposeAsync", "GetPinnableReference",
        "Slice", "Length", "Count");

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, ImmutableHashSet<string>> s_wrapperNames = new();

    /// <summary>
    /// Every identifier through which a value typed with a wrapper record can be reached in
    /// source: the records themselves, their factories, aliases of them, and every member,
    /// anywhere in the compilation, whose type or signature mentions one. A user-defined
    /// operator or conversion mentioning one is reachable with no identifier at all, and is
    /// recorded as <see cref="AnyWrapperOperator"/>.
    /// </summary>
    private static ImmutableHashSet<string> WrapperRelatedNames(Compilation compilation)
        => s_wrapperNames.GetValue(compilation, static c =>
        {
            // Purely syntactic, to a fixed point: a declaration can only mention a record by
            // spelling its name, an alias of it, or the name of a type that is itself tainted
            // (a record-closed base, a delegate returning one). Binding every member signature
            // instead was measured at seconds on a large project. Factory names are NOT in
            // this set: MemberIsWrapperFree checks them separately because they often
            // coincide with Reactor's own factories.
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            // Every wrapper record, not only the incomplete ones: a complete one still gets
            // generated members (GeneratedMemberMayWin).
            foreach (var record in WrapperFactoryCalls.SourceWrapperRecords(c)) names.Add(record.Name);
            if (names.Count == 0) return ImmutableHashSet<string>.Empty;

            var declarations = c.SyntaxTrees
                .SelectMany(static t => t.GetRoot().DescendantNodesAndSelf(static n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax))
                .Where(static n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax or DelegateDeclarationSyntax)
                .ToList();
            bool changed;
            do
            {
                changed = false;
                foreach (var node in declarations)
                {
                    foreach (var (name, implicitlyCalled) in Mentions(node, names))
                    {
                        changed |= names.Add(implicitlyCalled ? AnyWrapperOperator : name);
                    }
                }
            }
            while (changed && !names.Contains(AnyWrapperOperator));

            return names.ToImmutableHashSet(System.StringComparer.Ordinal);
        });

    /// <summary>
    /// The names a declaration makes reachable when its signature spells one of
    /// <paramref name="names"/>. <c>ImplicitlyCalled</c> marks a member C# can call without
    /// its name being written (operators, conversions, indexers, deconstruction, foreach,
    /// await, collection initializers, delegate invocation).
    /// </summary>
    private static IEnumerable<(string Name, bool ImplicitlyCalled)> Mentions(SyntaxNode node, HashSet<string> names)
    {
        bool Spells(SyntaxNode? syntax) => syntax is not null
            && syntax.DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && names.Contains(t.ValueText));

        switch (node)
        {
            case DelegateDeclarationSyntax @delegate when Spells(@delegate.ReturnType) || Spells(@delegate.ParameterList):
                yield return (@delegate.Identifier.ValueText, false);
                yield return ("Invoke", true);
                break;
            case TypeDeclarationSyntax type:
                // A constraint (`partial class Screen<T> where T : FooElement`) applies in every
                // partial declaration, like the base list.
                if (Spells(type.BaseList) || type.ConstraintClauses.Any(c => Spells(c))) yield return (type.Identifier.ValueText, false);
                // A primary constructor's parameters are in scope in every partial declaration
                // of the type, which all spell the type's name; a positional record also
                // synthesizes a property per parameter, and a Deconstruct.
                if (type.ParameterList is { } primary && Spells(primary))
                {
                    yield return (type.Identifier.ValueText, false);
                    foreach (var parameter in primary.Parameters)
                    {
                        if (Spells(parameter.Type)) yield return (parameter.Identifier.ValueText, false);
                    }
                    if (type is RecordDeclarationSyntax) yield return ("Deconstruct", true);
                }
                foreach (var member in type.Members)
                {
                    switch (member)
                    {
                        case MethodDeclarationSyntax method when Spells(method.ReturnType) || Spells(method.ParameterList) || method.ConstraintClauses.Any(c => Spells(c)):
                            yield return (method.Identifier.ValueText, ImplicitlyCalledNames.Contains(method.Identifier.ValueText));
                            break;
                        case PropertyDeclarationSyntax property when Spells(property.Type):
                            yield return (property.Identifier.ValueText, ImplicitlyCalledNames.Contains(property.Identifier.ValueText));
                            break;
                        case IndexerDeclarationSyntax indexer when Spells(indexer.Type) || Spells(indexer.ParameterList):
                        case OperatorDeclarationSyntax op when Spells(op.ReturnType) || Spells(op.ParameterList):
                        case ConversionOperatorDeclarationSyntax conversion when Spells(conversion.Type) || Spells(conversion.ParameterList):
                            yield return (string.Empty, true);
                            break;
                        case BaseFieldDeclarationSyntax field when Spells(field.Declaration.Type):
                            foreach (var variable in field.Declaration.Variables) yield return (variable.Identifier.ValueText, false);
                            break;
                        case EventDeclarationSyntax @event when Spells(@event.Type):
                            yield return (@event.Identifier.ValueText, false);
                            break;
                    }
                }
                break;
            case CompilationUnitSyntax unit:
                foreach (var directive in unit.Usings) foreach (var m in AliasMention(directive)) yield return m;
                break;
            case BaseNamespaceDeclarationSyntax ns:
                foreach (var directive in ns.Usings) foreach (var m in AliasMention(directive)) yield return m;
                break;
        }

        IEnumerable<(string, bool)> AliasMention(UsingDirectiveSyntax directive)
        {
            if (!Spells(directive.NamespaceOrType)) yield break;
            if (directive.Alias is { } alias)
            {
                yield return (alias.Name.Identifier.ValueText, false);
            }
            else if (!directive.StaticKeyword.IsKind(SyntaxKind.None))
            {
                // `using static Cache<FooElement>;` brings in members whose declarations only
                // spell `T`, under names no list here can know in advance.
                yield return (string.Empty, true);
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, System.Collections.Concurrent.ConcurrentDictionary<SyntaxNode, bool>> s_wrapperFreeMembers = new();

    /// <summary>
    /// True when nothing in the type declaration containing <paramref name="node"/> (the
    /// outermost one, so base lists, primary constructors and sibling members count; the
    /// whole file for top-level statements) can produce a wrapper-dependent binding: none of
    /// its identifiers is in <see cref="WrapperRelatedNames"/>, and the compilation declares
    /// no operator, conversion, indexer or implicitly called member (deconstruction, foreach,
    /// await, …) involving a record. Every other route to a wrapper value — a factory call
    /// (bound or not), a typed variable or member, a tainted type, a cast, <c>default</c>,
    /// <c>new</c>, a method group — spells one of those names, so such code skips the
    /// per-expression analysis entirely and behaves exactly as in a compilation without
    /// wrappers. A purely syntactic token scan, memoized per scope per compilation: asking
    /// the compiler for diagnostics instead was measured at more than doubling the
    /// generator's run time.
    /// </summary>
    private static bool MemberIsWrapperFree(SemanticModel model, SyntaxNode node, System.Threading.CancellationToken ct)
    {
        var names = WrapperRelatedNames(model.Compilation);
        if (names.Contains(AnyWrapperOperator)) return false;

        // The outermost type declaration, so its base list, primary-constructor parameters and
        // other members are in view (`class Screen(FooElement child) : Component<FooElement>`
        // spells the record only there). Top-level statements share one scope: the file.
        SyntaxNode member = node.SyntaxTree.GetRoot(ct);
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax) member = ancestor;
        }

        var memo = s_wrapperFreeMembers.GetValue(model.Compilation, static _ => new System.Collections.Concurrent.ConcurrentDictionary<SyntaxNode, bool>());
        if (memo.TryGetValue(member, out var known)) return known;

        // A wrapper factory's name (`ToggleSwitch`) can also be a Reactor factory's: only an
        // occurrence that binds to nothing is a call to the invisible generated one.
        var factoryNames = WrapperFactoryCalls.SourceFactoryNames(model.Compilation);
        var free = member.DescendantTokens()
            .Where(static t => t.IsKind(SyntaxKind.IdentifierToken))
            .All(token => !names.Contains(token.ValueText)
                && !(factoryNames.Contains(token.ValueText)
                     && token.Parent is IdentifierNameSyntax name
                     && (model.GetSymbolInfo(name, ct) is { Symbol: null, CandidateSymbols.IsEmpty: true }
                         || WrapperFactoryCalls.InvisibleFactoryOwners(model, NameExpression(name), ct) is not null)));

        memo[member] = free;
        return free;

        // `Name` or `Q.Name`, called or used as a method group.
        static ExpressionSyntax NameExpression(IdentifierNameSyntax name)
            => name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
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

        return DependsOnWrappers(model, invocation.ArgumentList, ct, depth: 0, visiting: null);

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

        // The closing invariant: every part of the arguments is either replaced, on a
        // validated path from a replacement, or free of anything this compilation cannot see.
        // Otherwise the binding — re-bound or original — rests on a guess.
        if (rewriter.Unreliable || !rewriter.IsFullyAccountedFor(invocation.ArgumentList)) return new SpeculativeBinding(true, null);
        if (!rewriter.Changed) return new SpeculativeBinding(false, null);

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

        foreach (var overload in candidates.OfType<IMethodSymbol>())
        {
            foreach (var parameter in overload.Parameters)
            {
                if (ReachableOnlyByTheRecord(parameter.Type, model.Compilation, interfaces, depth: 0)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// True when anything under <paramref name="root"/> can bind differently once the wrapper
    /// records are complete: an error-typed expression at any depth (it can still decide which
    /// overload an enclosing call picks), a value whose type mentions a record, a method group
    /// whose signature does, or an implicitly typed local whose declaring statement does — a
    /// <c>var content = Content(card)</c> is a string here and an Element there. Asked of
    /// expression TYPES rather than of particular syntax shapes, so no way of producing such a
    /// value is missed. Fail-closed past the depth bound.
    /// </summary>
    private static bool DependsOnWrappers(
        SemanticModel model, SyntaxNode root, System.Threading.CancellationToken ct, int depth, HashSet<SyntaxNode>? visiting)
    {
        var memo = s_dependsMemo.GetValue(model.Compilation, static _ => new System.Collections.Concurrent.ConcurrentDictionary<SyntaxNode, bool>());
        visiting ??= new HashSet<SyntaxNode>();
        return Walk(model, root, ct, depth, visiting, memo).Depends;
    }

    /// <summary>
    /// False only for expressions that can never introduce a record, an error or a binding
    /// decision their children do not already show — literals, interpolated-string shells,
    /// parentheses, lambdas, <c>typeof</c>/<c>sizeof</c>. Their
    /// children are still walked. Everything else (operators included: <c>x as FooElement</c>,
    /// a user-defined operator returning a record) is asked. Skipping those few kinds removes
    /// most of the nodes in a typical render body.
    /// </summary>
    private static bool IsValueSource(ExpressionSyntax expression) => expression is not
        (LiteralExpressionSyntax or InterpolatedStringExpressionSyntax or ParenthesizedExpressionSyntax
        or AnonymousFunctionExpressionSyntax
        or TypeOfExpressionSyntax or SizeOfExpressionSyntax or OmittedArraySizeExpressionSyntax);

    /// <summary>
    /// Per-node answers, so nested containers and render lambdas — scanned once for every
    /// enclosing DSL call — cost one walk per compilation rather than one per ancestor.
    /// Keyed on the compilation, so an edit (a new compilation) never sees a stale answer.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, System.Collections.Concurrent.ConcurrentDictionary<SyntaxNode, bool>> s_dependsMemo = new();

    /// <summary>
    /// <see cref="DependsOnWrappers"/> for one node. <c>Incomplete</c> is true when the answer
    /// was cut short — by a cycle back into a declaration still being followed, or by the depth
    /// bound — so it holds for this walk but is not memoized.
    /// </summary>
    private static (bool Depends, bool Incomplete) Walk(
        SemanticModel model,
        SyntaxNode node,
        System.Threading.CancellationToken ct,
        int depth,
        HashSet<SyntaxNode> visiting,
        System.Collections.Concurrent.ConcurrentDictionary<SyntaxNode, bool> memo)
    {
        if (memo.TryGetValue(node, out var known)) return (known, false);
        if (IsNameof(node, model)) return (false, false);
        if (depth > MaxDependencyDepth) return (true, true);
        if (HasWrapperTypeArgument(model, node, ct)
            || (node is ExpressionSyntax competing && WrapperFactoryCalls.HiddenFactoryMayChange(model, competing, ct))
            || (node is InvocationExpressionSyntax constrained && IsWrapperConstrainedCall(model, constrained, ct))
            || (node is ExpressionSyntax onRecord && WrapperFactoryCalls.GeneratedMemberMayWin(model, onRecord, ct)))
        {
            memo[node] = true;
            return (true, false);
        }

        var incomplete = false;
        if (node is ExpressionSyntax expression
            && IsValueSource(expression)
            && !(expression.Parent is MemberAccessExpressionSyntax access && access.Name == expression))
        {
            // One type query per node; a symbol query only where a type alone cannot answer —
            // a typeless name (a method group) or a name that might be an implicitly typed local.
            // This runs over every DSL call's arguments in a compilation with wrapper records.
            var type = SyntaxFacts.IsInTypeOnlyContext(expression) ? null : model.GetTypeInfo(expression, ct).Type;
            if (type is IErrorTypeSymbol)
            {
                memo[node] = true;
                return (true, false);
            }
            if ((type is not null && MentionsIncompleteWrapper(type, model.Compilation) && ValueTypeOf(model, expression, ct) is not null)
                || (type is null && IsWrapperDependentMethodGroup(model, expression, ct)))
            {
                memo[node] = true;
                return (true, false);
            }

            if (expression is IdentifierNameSyntax name && ImplicitlyTypedDeclaration(model, name, ct) is { } sources)
            {
                foreach (var source in sources)
                {
                    if (memo.TryGetValue(source, out var sourceKnown))
                    {
                        if (sourceKnown) return (true, false);
                        continue;
                    }
                    // A declaration already being followed adds nothing new (`out var n` read
                    // inside the statement that declares it), but the answer is then partial.
                    if (!visiting.Add(source))
                    {
                        incomplete = true;
                        continue;
                    }
                    var result = Walk(model, source, ct, depth + 1, visiting, memo);
                    visiting.Remove(source);
                    if (result.Depends) return (true, result.Incomplete);
                    incomplete |= result.Incomplete;
                }
            }
        }

        foreach (var result in node.ChildNodes().Select(child => Walk(model, child, ct, depth, visiting, memo)))
        {
            if (result.Depends)
            {
                if (!result.Incomplete) memo[node] = true;
                return (true, result.Incomplete);
            }
            incomplete |= result.Incomplete;
        }

        if (!incomplete) memo[node] = false;
        return (false, incomplete);
    }

    /// <summary>Bounds how far implicitly typed declarations are followed; fail-closed beyond.</summary>
    private const int MaxDependencyDepth = 8;

    /// <summary>
    /// For a name reading an implicitly typed local (<c>var</c> declarator, <c>foreach (var …)</c>,
    /// deconstruction, <c>out var</c>, <c>var</c> pattern) declared in this tree, the node that
    /// decides its type: the initializer, or the whole declaring statement. Null otherwise —
    /// an explicitly declared type cannot change.
    /// </summary>
    private static IReadOnlyList<SyntaxNode>? ImplicitlyTypedDeclaration(SemanticModel model, IdentifierNameSyntax name, System.Threading.CancellationToken ct)
    {
        var symbol = model.GetSymbolInfo(name, ct).Symbol;

        // An implicitly typed lambda parameter takes its type from the call the lambda is
        // passed to — its receiver and its other arguments (`items.Select(item => …)`), never
        // the lambda itself.
        if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } } parameter)
        {
            if (parameter.DeclaringSyntaxReferences.Length != 1) return null;
            if (parameter.DeclaringSyntaxReferences[0].GetSyntax(ct) is not ParameterSyntax { Type: null } declared) return null;
            var lambda = declared.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
            // `Select((item => …))`: parentheses do not change which call the lambda is passed to.
            SyntaxNode? passedValue = lambda;
            while (passedValue?.Parent is ParenthesizedExpressionSyntax) passedValue = passedValue.Parent;
            if (passedValue?.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax receiving } } passed)
                return new SyntaxNode[] { passedValue?.Parent ?? declared };
            var sources = new List<SyntaxNode> { receiving.Expression };
            sources.AddRange(receiving.ArgumentList.Arguments.Where(a => a != passed));
            // And the lambda itself when more than one candidate could take it: its body then
            // takes part in choosing the overload (one whose body fails to bind, or returns a
            // worse type, loses), and so the parameter's type. Walking it reaches this very
            // parameter again, which the walk's cycle guard cuts.
            if (LambdaCanChooseOverload(model, receiving, passed, lambda!, ct)) sources.Add(lambda!);
            return sources;
        }

        // A query range variable takes its type from the clause that introduces it.
        if (symbol is IRangeVariableSymbol range)
        {
            if (range.DeclaringSyntaxReferences.Length != 1) return null;
            return range.DeclaringSyntaxReferences[0].GetSyntax(ct) switch
            {
                FromClauseSyntax from => new SyntaxNode[] { from.Expression },
                LetClauseSyntax let => new SyntaxNode[] { let.Expression },
                JoinClauseSyntax join => new SyntaxNode[] { join.InExpression },
                { } other => new SyntaxNode[] { other.FirstAncestorOrSelf<QueryExpressionSyntax>() ?? other },
                _ => null,
            };
        }

        if (symbol is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.Length != 1
            || local.DeclaringSyntaxReferences[0].SyntaxTree != name.SyntaxTree)
        {
            return null;
        }

        // The node whose type decides the local's — never the statements that merely use it.
        var declarator = local.DeclaringSyntaxReferences[0].GetSyntax(ct);
        switch (declarator)
        {
            case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } v:
                return declaration.Type.IsVar && v.Initializer is { } initializer ? new SyntaxNode[] { initializer } : null;
            case ForEachStatementSyntax forEach:
                return forEach.Type.IsVar ? new SyntaxNode[] { forEach.Expression } : null;
            case SingleVariableDesignationSyntax { Parent: DeclarationPatternSyntax pattern } when !pattern.Type.IsVar:
                return null;
            case SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax declarationExpression } when !declarationExpression.Type.IsVar:
                return null;
        }

        for (SyntaxNode? node = declarator; node is not null; node = node.Parent)
        {
            switch (node)
            {
                // `out var n`: the call it is passed to — its receiver and its OTHER arguments,
                // which choose the overload and so the out parameter's type. Not the declaring
                // argument itself.
                case ArgumentSyntax { Parent: ArgumentListSyntax { Parent: { } call } list } declaring:
                {
                    var sources = new List<SyntaxNode>();
                    if (call is InvocationExpressionSyntax invocation) sources.Add(invocation.Expression);
                    sources.AddRange(list.Arguments.Where(a => a != declaring));
                    return sources;
                }
                // `var (a, b) = expr`: the right-hand side.
                case AssignmentExpressionSyntax assignment when assignment.Left.Span.Contains(declarator.Span):
                    return new SyntaxNode[] { assignment.Right };
                // `foreach (var (a, b) in xs)`.
                case ForEachVariableStatementSyntax forEachVariable:
                    return new SyntaxNode[] { forEachVariable.Expression };
                // `x is var n`, `x is { } n`: the tested value.
                case IsPatternExpressionSyntax isPattern:
                    return new SyntaxNode[] { isPattern.Expression };
                case SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax switchExpression }:
                    return new SyntaxNode[] { switchExpression.GoverningExpression };
                case SwitchSectionSyntax { Parent: SwitchStatementSyntax switchStatement }:
                    return new SyntaxNode[] { switchStatement.Expression };
                case StatementSyntax statement:
                    // Unrecognised: the enclosing statement, with cycles cut by the caller.
                    return new SyntaxNode[] { statement };
            }
        }
        return null;
    }

    /// <summary>
    /// True when the lambda's body can decide its parameters' types: when the candidates in
    /// <paramref name="receiving"/>'s group that could take it at that position disagree on
    /// them (<c>Use(Func&lt;string, DateTime&gt;)</c> beside <c>Use(Func&lt;Element, int&gt;)</c>, where
    /// the body's return type picks one). Candidates that agree — <c>Mount(Component)</c>
    /// beside <c>Mount(Func&lt;RenderContext, Element&gt;)</c>, or <c>Select</c>'s two overloads of
    /// different arity — leave the parameters' types fixed whatever the body does. A target a
    /// lambda converts to by its natural type (<c>object</c>, <c>Delegate</c>, a type
    /// parameter) is unknown, and so is a group that cannot be read.
    /// </summary>
    private static bool LambdaCanChooseOverload(
        SemanticModel model,
        InvocationExpressionSyntax receiving,
        ArgumentSyntax passed,
        AnonymousFunctionExpressionSyntax lambda,
        System.Threading.CancellationToken ct)
    {
        var group = model.GetMemberGroup(receiving.Expression, ct);
        if (group.IsEmpty || passed.NameColon is not null) return true;
        var index = receiving.ArgumentList.Arguments.IndexOf(passed);
        int? arity = lambda switch
        {
            SimpleLambdaExpressionSyntax => 1,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.Count,
            AnonymousMethodExpressionSyntax { ParameterList: { } list } => list.Parameters.Count,
            _ => null,
        };

        // One entry per possible target: its parameter types, or null when unknown.
        var targets = new List<ImmutableArray<ITypeSymbol>?>();
        foreach (var symbol in group)
        {
            if (symbol is not IMethodSymbol method) return true;
            var parameters = method.Parameters;
            var last = parameters.Length - 1;
            if (index < parameters.Length) Classify(parameters[index].Type);
            if (last >= 0 && parameters[last].IsParams && index >= last && parameters[last].Type is IArrayTypeSymbol array) Classify(array.ElementType);
        }
        if (targets.Count <= 1) return false;
        if (targets.Any(static t => t is null)) return true;
        var first = targets[0]!.Value;
        return targets.Any(t => !System.Linq.Enumerable.SequenceEqual(t!.Value, first, SymbolEqualityComparer.Default));

        void Classify(ITypeSymbol type)
        {
            if (DelegateOf(type) is { } invoke)
            {
                if (arity is null) targets.Add(null);
                else if (invoke.Parameters.Length == arity) targets.Add(invoke.Parameters.Select(static p => p.Type).ToImmutableArray());
                return;
            }
            // Targets of a lambda's natural type; anything else (a class such as Component)
            // cannot take a lambda at all.
            if (type is ITypeParameterSymbol or IDynamicTypeSymbol
                || type.SpecialType is SpecialType.System_Object or SpecialType.System_Delegate or SpecialType.System_MulticastDelegate
                || type.ToDisplayString() is "System.Linq.Expressions.Expression" or "System.Linq.Expressions.LambdaExpression")
            {
                targets.Add(null);
            }
        }

        static IMethodSymbol? DelegateOf(ITypeSymbol type) => type is INamedTypeSymbol named
            ? named.DelegateInvokeMethod
              ?? (named.Name == "Expression" && named.TypeArguments.Length == 1 && named.TypeArguments[0] is INamedTypeSymbol inner ? inner.DelegateInvokeMethod : null)
            : null;
    }

    /// <summary>
    /// A method group (<c>From(MakeGauge)</c>) has no type of its own, so a type check never
    /// sees it; but its candidates' signatures decide which delegate it converts to, and one
    /// naming a wrapper record converts differently once the record is an Element — as does
    /// any method group converted to a delegate type that names one.
    /// </summary>
    private static bool IsWrapperDependentMethodGroup(SemanticModel model, ExpressionSyntax expression, System.Threading.CancellationToken ct)
    {
        if (expression is not (SimpleNameSyntax or MemberAccessExpressionSyntax)) return false;
        if (expression.Parent is InvocationExpressionSyntax call && call.Expression == expression) return false;
        if (expression.Parent is MemberAccessExpressionSyntax member && member.Name == expression) return false;

        var info = model.GetSymbolInfo(expression, ct);
        var compilation = model.Compilation;
        var isMethodGroup = false;
        var symbols = info.Symbol is { } single ? ImmutableArray.Create(single) : info.CandidateSymbols;
        foreach (var method in symbols.OfType<IMethodSymbol>())
        {
            isMethodGroup = true;
            if (MentionsIncompleteWrapper(method.ReturnType, compilation)) return true;
            if (method.Parameters.Any(p => MentionsIncompleteWrapper(p.Type, compilation))) return true;
        }

        // The delegate it converts to: `From(Make)` into `Func<FooElement, object>` asks which
        // `Make` takes a FooElement, and `Make(Element)` starts to once the record is complete.
        if (!isMethodGroup) return false;
        if (model.GetTypeInfo(expression, ct).ConvertedType is { } converted && MentionsIncompleteWrapper(converted, compilation)) return true;

        // `Use(Make<IComparer<Element>>)`: which of Make's methods the group converts with can
        // depend on a constraint that mentions a record.
        var name = expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => null,
        };
        return name is not null
            && WrapperConstrainedMethodNames(compilation).Contains(name)
            && WrapperFactoryCalls.NameReachesWrapperConstraint(model, expression, ct);
    }

    /// <summary>
    /// A call to a method — declared in this compilation, the only place one can name a
    /// wrapper record — with a type-parameter constraint mentioning one (see
    /// <see cref="WrapperFactoryCalls.GroupHasWrapperConstraint"/>). Prefiltered by name, so
    /// only calls to such methods pay for the member-group lookup.
    /// </summary>
    private static bool IsWrapperConstrainedCall(SemanticModel model, InvocationExpressionSyntax invocation, System.Threading.CancellationToken ct)
    {
        var name = invocation.Expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            _ => null,
        };
        return name is not null
            && WrapperConstrainedMethodNames(model.Compilation).Contains(name)
            && WrapperFactoryCalls.GroupHasWrapperConstraint(model, invocation, ct);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, ImmutableHashSet<string>> s_constrainedNames = new();

    /// <summary>Names of source methods and local functions whose constraint clauses spell a wrapper-related name.</summary>
    private static ImmutableHashSet<string> WrapperConstrainedMethodNames(Compilation compilation)
        => s_constrainedNames.GetValue(compilation, static c =>
        {
            var names = WrapperRelatedNames(c);
            if (names.IsEmpty) return ImmutableHashSet<string>.Empty;
            var result = ImmutableHashSet.CreateBuilder<string>(System.StringComparer.Ordinal);
            foreach (var tree in c.SyntaxTrees)
            {
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    var (identifier, clauses) = node switch
                    {
                        MethodDeclarationSyntax m => (m.Identifier, m.ConstraintClauses),
                        LocalFunctionStatementSyntax l => (l.Identifier, l.ConstraintClauses),
                        _ => (default, default),
                    };
                    if (clauses.Count == 0) continue;
                    if (clauses.Any(cl => cl.DescendantTokens().Any(tk => tk.IsKind(SyntaxKind.IdentifierToken) && names.Contains(tk.ValueText))))
                        result.Add(identifier.ValueText);
                }
            }
            return result.ToImmutable();
        });

    /// <summary>
    /// <c>Choose&lt;FooElement&gt;(0)</c>: an explicit type argument naming a wrapper record
    /// decides which candidates satisfy their constraints (<c>where T : Element</c> fails here
    /// and holds once the record is complete), so the call's result type can change even
    /// though no value in it does. Type syntax (<c>new List&lt;FooElement&gt;()</c>) is left
    /// to the value's own type.
    /// </summary>
    private static bool HasWrapperTypeArgument(SemanticModel model, SyntaxNode node, System.Threading.CancellationToken ct)
    {
        if (node is not GenericNameSyntax generic || SyntaxFacts.IsInTypeOnlyContext(generic)) return false;
        foreach (var argument in generic.TypeArgumentList.Arguments)
        {
            if (model.GetTypeInfo(argument, ct).Type is { } type && MentionsIncompleteWrapper(type, model.Compilation)) return true;
        }
        return false;
    }

    /// <summary>
    /// <c>nameof(...)</c>: a compile-time string whose operand is never evaluated, and whose
    /// <c>nameof</c> identifier Roslyn reports as error-typed. Never a dependency. The
    /// operator only when written unescaped and nothing named <c>nameof</c> is in scope —
    /// otherwise (<c>@nameof(x)</c>, a method called <c>nameof</c>) it is an ordinary call.
    /// </summary>
    private static bool IsNameof(SyntaxNode node, SemanticModel model)
        => node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } }
           && model.LookupSymbols(node.SpanStart, name: "nameof").IsEmpty;

    /// <summary>
    /// True when <paramref name="expression"/> failed to bind. Type syntax is excluded: Roslyn
    /// reports the empty <c>[]</c> of <c>string[]</c> (an <c>OmittedArraySizeExpression</c>) as
    /// error-typed although nothing failed, and a type name that genuinely does not exist is
    /// a compile error in its own right.
    /// </summary>
    private static bool HasErrorType(SemanticModel model, ExpressionSyntax expression, System.Threading.CancellationToken ct)
        => expression is not OmittedArraySizeExpressionSyntax
           && !IsNameof(expression, model)
           && !(expression.Parent is InvocationExpressionSyntax nameofCall && nameofCall.Expression == expression && IsNameof(nameofCall, model))
           && !SyntaxFacts.IsInTypeOnlyContext(expression)
           && model.GetTypeInfo(expression, ct).Type is IErrorTypeSymbol;

    /// <summary>The type of <paramref name="expression"/> when it is a value (not a type name, not the name half of <c>x.Name</c>, not a by-ref argument).</summary>
    private static ITypeSymbol? ValueTypeOf(SemanticModel model, ExpressionSyntax expression, System.Threading.CancellationToken ct)
    {
        if (SyntaxFacts.IsInTypeOnlyContext(expression)) return null;
        if (expression.Parent is MemberAccessExpressionSyntax member && member.Name == expression) return null;
        if (expression.Parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None)) return null;
        if (model.GetTypeInfo(expression, ct).Type is not { } type || type is IErrorTypeSymbol) return null;

        // `SettingsCardElement.SettingsCard(...)`: the qualifier names the type, it is not a value.
        return model.GetSymbolInfo(expression, ct).Symbol is INamespaceOrTypeSymbol ? null : type;
    }

    /// <summary>
    /// True when <paramref name="type"/> is "tainted" by a wrapper record — values of it can
    /// carry, produce or convert to a record whose generated half this compilation cannot see:
    /// the record itself; a constructed type with a tainted type argument; an array of one; a
    /// delegate whose signature is tainted (<c>delegate FooElement Make()</c>); or a source type
    /// whose base chain or interfaces are (<c>class Screen : Component&lt;FooElement&gt;</c>,
    /// whose inherited <c>Props</c> is a record) or which declares a conversion to one; or a
    /// type parameter constrained to one.
    /// Cached per compilation.
    /// </summary>
    private static bool MentionsIncompleteWrapper(ITypeSymbol type, Compilation compilation)
        => WrapperFactoryCalls.MentionsIncompleteWrapper(type, compilation);

    private static bool ContainsTypeParameter(ITypeSymbol type, int depth = 0) => depth > 8 || type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType, depth + 1),
        // `Outer<T>.Nested` has no type arguments of its own; its containing type's count.
        INamedTypeSymbol named => named.TypeArguments.Any(a => ContainsTypeParameter(a, depth + 1))
                                  || (named.ContainingType is { } outer && ContainsTypeParameter(outer, depth + 1)),
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
                // `Outer<FooElement>.Nested`: the record is in the containing type's arguments.
                if (named.ContainingType is { } containing
                    && ReachableOnlyByTheRecord(containing, compilation, interfaces, depth + 1, insideConversion))
                {
                    return true;
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
    /// Replaces wrapper values (unbound factory calls, record-typed values) with
    /// <c>(default(global::Microsoft.UI.Reactor.Core.Element))</c> and position-checks each. Visits
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

        /// <summary>Original nodes replaced by a placeholder; their subtrees are not consulted.</summary>
        private readonly HashSet<SyntaxNode> _replaced = new();

        /// <summary>Original nodes on a validated path from a replacement (see CheckPosition).</summary>
        private readonly HashSet<SyntaxNode> _accounted = new();

        /// <summary>
        /// True when no expression under <paramref name="root"/> is left that differs between
        /// this compilation and the real one without being replaced or validated: nothing
        /// error-typed, no value whose type mentions a wrapper record, no call that failed to
        /// bind.
        /// </summary>
        public bool IsFullyAccountedFor(SyntaxNode root)
        {
            var unaccounted = root.DescendantNodesAndSelf(n => !_replaced.Contains(n) && !IsNameof(n, _model))
                .Where(n => !_replaced.Contains(n) && !_accounted.Contains(n))
                .OfType<ExpressionSyntax>();
            foreach (var expression in unaccounted)
            {
                if (HasWrapperTypeArgument(_model, expression, _ct)) return false;
                if (WrapperFactoryCalls.HiddenFactoryMayChange(_model, expression, _ct)) return false;
                if (expression is InvocationExpressionSyntax constrained && IsWrapperConstrainedCall(_model, constrained, _ct)) return false;
                if (WrapperFactoryCalls.GeneratedMemberMayWin(_model, expression, _ct)) return false;
                // The name half of `x.Name` is judged together with its member access.
                if (expression.Parent is MemberAccessExpressionSyntax member && member.Name == expression) continue;
                if (HasErrorType(_model, expression, _ct)) return false;
                if (ValueType(expression) is { } valueType && MentionsIncompleteWrapper(valueType)) return false;
                if (IsWrapperDependentMethodGroup(_model, expression, _ct)) return false;
                if (expression is IdentifierNameSyntax local
                    && ImplicitlyTypedDeclaration(_model, local, _ct) is { } sources
                    && sources.Any(source => DependsOnWrappers(_model, source, _ct, _depth + 1, new HashSet<SyntaxNode> { source })))
                {
                    return false;
                }
                if (expression is InvocationExpressionSyntax call
                    && _model.GetSymbolInfo(call, _ct) is { Symbol: null, CandidateSymbols.IsEmpty: false })
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// True when <paramref name="expression"/> is the record in the real compilation no matter
        /// how anything inside it binds, so it may be replaced whole: a variable, field, property
        /// or parameter declared as the record, <c>default</c>/<c>new</c>/a cast of it, or a call
        /// whose every candidate returns it. Anything else is descended into instead, so the
        /// parts that DO depend on the record are found and position-checked.
        /// </summary>
        private bool CertainlyRecord(ExpressionSyntax expression, INamedTypeSymbol record)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax p:
                    return CertainlyRecord(p.Expression, record);
                case PostfixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    return CertainlyRecord(p.Operand, record);
                case ConditionalExpressionSyntax c:
                    return CertainlyRecord(c.WhenTrue, record) && CertainlyRecord(c.WhenFalse, record);
                case WithExpressionSyntax w:
                    return CertainlyRecord(w.Expression, record);
                case CastExpressionSyntax cast:
                    return SymbolEqualityComparer.Default.Equals(_model.GetTypeInfo(cast.Type, _ct).Type, record);
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression):
                    return SymbolEqualityComparer.Default.Equals(_model.GetTypeInfo(binary.Right, _ct).Type, record);
                case DefaultExpressionSyntax:
                case ObjectCreationExpressionSyntax:
                case ThisExpressionSyntax:
                    return true;
                case SimpleNameSyntax:
                case MemberAccessExpressionSyntax:
                    // `Load(gauge).Child`: which member `Child` is depends on the receiver's type.
                    if (expression is MemberAccessExpressionSyntax { Expression: var owner } && !ReceiverTypeIsFixed(owner)) return false;
                    return _model.GetSymbolInfo(expression, _ct).Symbol switch
                    {
                        ILocalSymbol l => SymbolEqualityComparer.Default.Equals(l.Type, record) && LocalIsCertainlyRecord(l, record),
                        IFieldSymbol f => SymbolEqualityComparer.Default.Equals(f.Type, record),
                        IPropertySymbol p => SymbolEqualityComparer.Default.Equals(p.Type, record),
                        IParameterSymbol p => SymbolEqualityComparer.Default.Equals(p.Type, record) && !IsImplicitlyTypedLambdaParameter(p),
                        _ => false,
                    };
                case InvocationExpressionSyntax call:
                {
                    if (call.Expression is MemberAccessExpressionSyntax { Expression: var callee } && !ReceiverTypeIsFixed(callee)) return false;
                    if (WrapperFactoryCalls.InvisibleFactoryOwners(_model, call, _ct) is { } owners && owners.Any(o => !SymbolEqualityComparer.Default.Equals(o, record))) return false;
                    var receiverRecord = call.Expression is MemberAccessExpressionSyntax m
                        && ValueType(m.Expression) is INamedTypeSymbol r
                        && WrapperFactoryCalls.IsIncompleteWrapperElement(r, _model.Compilation) ? r : null;
                    return CandidateMethods(call, receiverRecord) is { Count: > 0 } methods
                        && methods.All(c => !c.IsGenericMethod && SymbolEqualityComparer.Default.Equals(c.ReturnType, record));
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when <paramref name="receiver"/> has the same type in the real compilation, so
        /// the member looked up on it is the same member: a type or namespace name,
        /// <c>this</c>/<c>base</c>, a field, property, explicitly typed parameter or local
        /// (through a chain of such), a value that is certainly a record, or anything that
        /// does not depend on the records at all.
        /// </summary>
        private bool ReceiverTypeIsFixed(ExpressionSyntax receiver)
        {
            switch (receiver)
            {
                case ThisExpressionSyntax:
                case BaseExpressionSyntax:
                    return true;
                case ParenthesizedExpressionSyntax p:
                    return ReceiverTypeIsFixed(p.Expression);
                case SimpleNameSyntax:
                case MemberAccessExpressionSyntax:
                {
                    var declared = _model.GetSymbolInfo(receiver, _ct).Symbol switch
                    {
                        INamespaceOrTypeSymbol => true,
                        IFieldSymbol or IPropertySymbol => true,
                        IParameterSymbol p => !IsImplicitlyTypedLambdaParameter(p),
                        ILocalSymbol => receiver is not IdentifierNameSyntax name || ImplicitlyTypedDeclaration(_model, name, _ct) is null,
                        _ => false,
                    };
                    if (declared) return receiver is not MemberAccessExpressionSyntax m || ReceiverTypeIsFixed(m.Expression);
                    break;
                }
            }
            if (ValueType(receiver) is INamedTypeSymbol record
                && WrapperFactoryCalls.IsIncompleteWrapperElement(record, _model.Compilation)
                && CertainlyRecord(receiver, record))
            {
                return true;
            }
            return !DependsOnWrappers(_model, receiver, _ct, _depth + 1, visiting: null);
        }

        /// <summary>
        /// <c>item =&gt; …</c>: the parameter's type is inferred from the call the lambda is
        /// passed to, which can resolve differently once the records are complete.
        /// </summary>
        private bool IsImplicitlyTypedLambdaParameter(IParameterSymbol parameter)
            => parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction }
               && (parameter.DeclaringSyntaxReferences.Length != 1
                   || parameter.DeclaringSyntaxReferences[0].GetSyntax(_ct) is ParameterSyntax { Type: null });

        /// <summary>
        /// The wrapper record whose value flows unchanged to the root (an inferred local's
        /// whole initializer), or null. Lets a <c>var</c> local initialized from a wrapper
        /// call stay a record, not decay into a plain <c>Element</c>, so its own uses are
        /// position-checked like any other wrapper value.
        /// </summary>
        public INamedTypeSymbol? RootRecord { get; private set; }

        private bool MentionsIncompleteWrapper(ITypeSymbol type) => SourceMapInterceptorGenerator.MentionsIncompleteWrapper(type, _model.Compilation);

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
            if (original == _root)
            {
                RootRecord = record;
                return;
            }
            SyntaxNode child = original;
            // False once the value has passed a step that keeps it but may widen its type
            // (`flag ? gauge : new object()`): from there on the root is not the record.
            var exact = true;
            for (var node = original.Parent; node is not null; child = node, node = node.Parent)
            {
                // The root's own step is judged like any other: `var boxed = (object)Gauge()`
                // is an object, not the record.
                var atRoot = node == _root;
                if (!atRoot) _accounted.Add(node);
                switch (node)
                {
                    case ConditionalExpressionSyntax c when c.Condition != child:
                    {
                        var other = c.WhenTrue == child ? c.WhenFalse : c.WhenTrue;
                        if (!ArmIsRecord(other, record))
                        {
                            exact = false;
                            // The arms' types decide the conditional's. One the record converts to
                            // and Element does not (a conversion from the record, an interface it
                            // implements, the record itself inside it) types the conditional
                            // differently once the placeholder stands in.
                            if (ValueType(other) is not { } otherType
                                || MentionsIncompleteWrapper(otherType)
                                || ReachableOnlyByTheRecord(otherType, _model.Compilation, InterfacesOf(record), depth: 0))
                            {
                                Unreliable = true;
                                return;
                            }
                        }
                        if (atRoot)
                        {
                            if (exact) RootRecord = record;
                            return;
                        }
                        continue;
                    }
                    case ParenthesizedExpressionSyntax:
                    case PostfixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    case WithExpressionSyntax w when w.Expression == child:
                        if (atRoot)
                        {
                            if (exact) RootRecord = record;
                            return;
                        }
                        continue;
                    case SimpleLambdaExpressionSyntax l when l.ExpressionBody == child:
                    case ParenthesizedLambdaExpressionSyntax pl when pl.ExpressionBody == child:
                    case CollectionExpressionSyntax:
                    case ExpressionElementSyntax:
                    case InitializerExpressionSyntax:
                        // Built from the record, not the record: a root of that shape has a
                        // type this walk does not reconstruct.
                        if (atRoot)
                        {
                            Unreliable = true;
                            return;
                        }
                        continue;
                    case CastExpressionSyntax cast when cast.Expression == child:
                    case ArrayCreationExpressionSyntax:
                    case ObjectCreationExpressionSyntax:
                        return;
                    case ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.None):
                        if (argument.Parent == _root) return;
                        if (argument.Parent?.Parent is ObjectCreationExpressionSyntax creation)
                        {
                            _accounted.Add(creation);
                            return;
                        }
                        if (argument.Parent?.Parent is InvocationExpressionSyntax call
                            && NestedCall(call, receiverPath: false, record) == NestedResult.Deterministic)
                        {
                            // Its result type is fixed whichever candidate wins, even if it does
                            // not bind cleanly here.
                            _accounted.Add(call);
                            return;
                        }
                        Unreliable = true;
                        return;
                    case MemberAccessExpressionSyntax member
                        when !atRoot && member.Expression == child && member.Parent is InvocationExpressionSyntax modifier && modifier.Expression == member:
                        _accounted.Add(modifier);
                        // `Gauge().Identity<object>()`: an explicit type argument decides the
                        // result type, so the call no longer simply passes its receiver on.
                        if (member.Name is GenericNameSyntax)
                        {
                            Unreliable = true;
                            return;
                        }
                        switch (NestedCall(modifier, receiverPath: true, record))
                        {
                            case NestedResult.Deterministic:
                                return;
                            case NestedResult.PlaceholderLike:
                                // The modifier call can itself be the root (`var sized = g.Width(140)`).
                                if (modifier == _root)
                                {
                                    if (exact) RootRecord = record;
                                    return;
                                }
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
        /// True when the other arm of a conditional is the same record in the real
        /// compilation: a call to its wrapper factory, or a value certainly typed as it.
        /// </summary>
        private bool ArmIsRecord(ExpressionSyntax arm, INamedTypeSymbol record)
        {
            while (arm is ParenthesizedExpressionSyntax p) arm = p.Expression;
            // `flag ? gauge : null`: a typeless null or default takes the record's type.
            if (arm.IsKind(SyntaxKind.NullLiteralExpression) || arm.IsKind(SyntaxKind.DefaultLiteralExpression)) return true;
            if (arm is InvocationExpressionSyntax call
                && _model.GetSymbolInfo(call, _ct) is { Symbol: null, CandidateSymbols.IsEmpty: true })
            {
                return SymbolEqualityComparer.Default.Equals(WrapperFactoryCalls.ResolveUnboundCall(_model, call, _ct), record);
            }
            return SymbolEqualityComparer.Default.Equals(ValueType(arm), record) && CertainlyRecord(arm, record);
        }

        private static HashSet<INamedTypeSymbol> InterfacesOf(INamedTypeSymbol record)
        {
            var interfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var implemented in record.AllInterfaces) interfaces.Add(implemented.OriginalDefinition);
            return interfaces;
        }

        /// <summary>
        /// What a call does with a substituted value in its receiver or arguments. Judged
        /// over every candidate visible at the call site, never just the one that binds here.
        /// </summary>
        private NestedResult NestedCall(InvocationExpressionSyntax call, bool receiverPath, INamedTypeSymbol record)
        {
            if (CandidateMethods(call, receiverPath ? record : null) is not { Count: > 0 } methods) return NestedResult.Unsafe;

            // Every candidate returns one fixed type: whichever wins, the caller sees that type.
            var first = methods[0].ReturnType;
            // Nullability is irrelevant here: it never changes which overload binds (object.ToString()
            // is `string?`, a record's override is `string`).
            if (methods.All(m => SymbolEqualityComparer.Default.Equals(m.ReturnType, first))
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

        /// <summary>
        /// Every method the call could resolve to, as open definitions, or null when that set
        /// cannot be determined. With <paramref name="receiverRecord"/>, the receiver is a
        /// wrapper value, so members reachable from the finished record (and so from Element)
        /// are included too.
        /// </summary>
        private List<IMethodSymbol>? CandidateMethods(InvocationExpressionSyntax call, INamedTypeSymbol? receiverRecord)
        {
            var receiverPath = receiverRecord is not null;
            var record = receiverRecord;
            var position = call.SpanStart;
            var symbols = new List<ISymbol>();
            switch (call.Expression)
            {
                case MemberAccessExpressionSyntax member:
                {
                    var name = member.Name.Identifier.ValueText;
                    if (receiverPath)
                    {
                        // A member the wrapper generator adds to the record would beat whatever
                        // extension binds here, and it is not in view.
                        if (WrapperFactoryCalls.MayGainInvocableMember(record!, name)) return null;
                        if (_model.Compilation.GetTypeByMetadataName(ElementMetadataName) is { } element)
                            symbols.AddRange(_model.LookupSymbols(position, element, name, includeReducedExtensionMethods: true));
                        symbols.AddRange(_model.LookupSymbols(position, record, name, includeReducedExtensionMethods: true));
                    }
                    else
                    {
                        var qualifier = _model.GetSymbolInfo(member.Expression, _ct).Symbol as INamespaceOrTypeSymbol
                            ?? _model.GetTypeInfo(member.Expression, _ct).Type;
                        if (qualifier is null or IErrorTypeSymbol) return null;
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

            var invokes = symbols.Select(static symbol => symbol switch
            {
                IMethodSymbol m => m,
                ILocalSymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                IFieldSymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                IPropertySymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                IParameterSymbol { Type: INamedTypeSymbol { DelegateInvokeMethod: { } d } } => d,
                _ => (IMethodSymbol?)null,
            }).ToList();
            // Something invocable whose signature is not a method's: the set is unknown.
            if (invokes.Any(static invoke => invoke is null)) return null;
            return invokes.Select(static invoke => (invoke!.ReducedFrom ?? invoke).OriginalDefinition).ToList();
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
                {
                    if (CertainlyRecord(expression, record)) return Placeholder(record, expression);
                    return base.Visit(node);
                }

                // `new List<SettingsCardElement>()`: its conversions (variance to
                // IEnumerable<Element>, …) do not exist yet and cannot be stood in for.
                if (MentionsIncompleteWrapper(valueType)) Unreliable = true;
            }

            return base.Visit(node);
        }

        private ITypeSymbol? ValueType(ExpressionSyntax expression) => ValueTypeOf(_model, expression, _ct);

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
            }
            else if (ImplicitlyTypedDeclaration(_model, node, _ct) is { } sources
                     && sources.Any(source => DependsOnWrappers(_model, source, _ct, _depth + 1, new HashSet<SyntaxNode> { source })))
            {
                // Typed here, but by a declaration that binds differently once the records are
                // complete: re-derive the type the same way, or give up.
                if (_model.GetSymbolInfo(node, _ct).Symbol is ILocalSymbol typed
                    && sources.Count == 1 && sources[0] is EqualsValueClauseSyntax
                    && InferLocalType(typed) is { } inferred)
                {
                    return Placeholder(inferred, node);
                }
                Unreliable = true;
            }

            return node;
        }

        /// <summary>
        /// A local typed as the record HERE is the record in the real compilation only if its
        /// type was written down, or if it is a <c>var</c> whose initializer itself flows to
        /// the record — <c>var x = Pick(card)</c> is the record here but may be a string once
        /// a better overload of <c>Pick</c> exists.
        /// </summary>
        private bool LocalIsCertainlyRecord(ILocalSymbol local, INamedTypeSymbol record)
        {
            if (local.DeclaringSyntaxReferences.Length != 1) return false;
            switch (local.DeclaringSyntaxReferences[0].GetSyntax(_ct))
            {
                case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } declarator:
                    if (!declaration.Type.IsVar) return true;
                    return declarator.Initializer is not null
                        && InferLocalType(local) is INamedTypeSymbol inferred
                        && SymbolEqualityComparer.Default.Equals(inferred, record);
                case SingleVariableDesignationSyntax { Parent: DeclarationPatternSyntax pattern }:
                    return !pattern.Type.IsVar;
                default:
                    // foreach variables, out vars, deconstructions: not reconstructed here.
                    return false;
            }
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
                if (inner.Unreliable || !inner.IsFullyAccountedFor(initializer)) Unreliable = true;
                if (!inner.Changed || Unreliable) return null;

                // Still the record itself: hand it back as such, so the caller's Placeholder
                // treats this local exactly like a direct wrapper value.
                if (inner.RootRecord is { } record) return record;

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
            _replaced.Add(original);

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
            if (type.IsReferenceType)
            {
                return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.DefaultExpression(typeSyntax))
                    .WithTriviaFrom(original);
            }

            // A value type's `default(T)` is a CONSTANT, which binds differently from the
            // non-constant value it stands for: a constant zero int converts to byte or to any
            // enum, so `Show(n)` would pick `Show(byte)` over the real `Show(long)`. Invoking a
            // delegate yields a T that is not a constant. A ref struct cannot be a type argument.
            if (type.IsRefLikeType || type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
            {
                Unreliable = true;
                return original;
            }
            return SyntaxFactory.ParseExpression($"(((global::System.Func<{typeSyntax}>)null!)())")
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

        // Expanded form unless exactly one argument lands on the params slot and converts to
        // the array type itself — C# then uses the normal form, so the array is the caller's
        // own (an `Element[]`, `null`, a user-defined conversion to one) and is never written
        // into, even when the argument ALSO converts to a single Element.
        var expanded = paramsOrdinal >= 0
            && !(arguments.Count == parameters.Length
                 && model.ClassifyConversion(arguments[paramsOrdinal].Expression, parameters[paramsOrdinal].Type) is { Exists: true, IsImplicit: true });

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
