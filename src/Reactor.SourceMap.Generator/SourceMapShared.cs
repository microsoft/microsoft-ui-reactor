using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.SourceMap.Generator;

// Compiled into BOTH Reactor.SourceMap.Generator and Reactor.Wrappers.Generator (linked
// from the latter's csproj). The two generators run against the same input compilation
// and neither can see the other's output, so the facts they must agree on — where a call
// site is, and which calls go to a factory [GenerateReactorWrapper] emits — live in one
// file rather than in two copies that could drift.

/// <summary>
/// Position helpers shared by every Reactor interceptor generator, so an element stamped
/// by the wrapper generator reports its location exactly the way one stamped by the
/// source-map generator does.
/// </summary>
internal static class SourceMapPositions
{
    /// <summary>The namespace every Reactor interceptor is declared in.</summary>
    internal const string InterceptorNamespace = "Microsoft.UI.Reactor.Generated";

    /// <summary>
    /// True when the compiler will accept an interceptor declared in
    /// <see cref="InterceptorNamespace"/> for this tree — the same switch
    /// (<c>InterceptorsNamespaces</c>) whose absence makes an emitted interceptor a hard
    /// <c>CS9137</c>. Read from the parse options because that is where the compiler itself
    /// reads it, so the answer cannot disagree with the build.
    /// </summary>
    internal static bool AcceptsReactorInterceptors(ParseOptions? options)
    {
        if (options is null) return false;
        // Asked once per invocation node by the wrapper generator's syntax predicate, and
        // every tree in a compilation shares one options instance.
        return s_acceptsInterceptors.GetValue(options, static o => new StrongBox<bool>(Compute(o))).Value;
    }

    private static readonly ConditionalWeakTable<ParseOptions, StrongBox<bool>> s_acceptsInterceptors = new();

    private static bool Compute(ParseOptions options)
    {
        var features = options.Features;
        return Lists(features, "InterceptorsNamespaces") || Lists(features, "InterceptorsPreviewNamespaces");

        static bool Lists(IReadOnlyDictionary<string, string> features, string key)
        {
            if (!features.TryGetValue(key, out var value) || string.IsNullOrEmpty(value)) return false;
            // The compiler accepts an interceptor in a listed namespace or any namespace nested
            // in one (and everywhere for `global`), so the same rule is applied here.
            return value.Split(';')
                .Select(static part => part.Trim())
                .Any(static entry => entry.Length != 0
                    && (string.Equals(entry, "global", StringComparison.Ordinal)
                        || string.Equals(entry, InterceptorNamespace, StringComparison.Ordinal)
                        || InterceptorNamespace.StartsWith(entry + ".", StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// The path <c>[CallerFilePath]</c> would report for a position: the syntax tree's
    /// own path normally, or a <c>#line</c> directive's file resolved against that
    /// tree's directory when one is in effect.
    /// </summary>
    internal static string ResolveMappedPath(FileLinePositionSpan span, string treePath)
    {
        if (!span.HasMappedPath || string.IsNullOrEmpty(span.Path)) return treePath;
        if (System.IO.Path.IsPathRooted(span.Path)) return span.Path;

        var dir = System.IO.Path.GetDirectoryName(treePath);
        return string.IsNullOrEmpty(dir)
            ? span.Path
            : System.IO.Path.Combine(dir, span.Path);
    }

    /// <summary>
    /// The 1-based column to stamp alongside the paren-derived line: the first character
    /// of the invoked method's NAME, or the paren's own column when the name sits on a
    /// different mapped line. See <c>SourceMapInterceptorGenerator.CallSiteColumn</c>.
    /// </summary>
    internal static int CallSiteColumn(
        InvocationExpressionSyntax invocation,
        FileLinePositionSpan parenLineSpan,
        CancellationToken ct)
    {
        SyntaxToken? name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier,
            SimpleNameSyntax simple => simple.Identifier,
            _ => null,
        };

        if (name is { } token)
        {
            var nameSpan = invocation.SyntaxTree.GetMappedLineSpan(token.Span, ct);
            if (nameSpan.StartLinePosition.Line == parenLineSpan.StartLinePosition.Line
                && nameSpan.Path == parenLineSpan.Path)
            {
                return nameSpan.StartLinePosition.Character + 1;
            }
        }

        return parenLineSpan.StartLinePosition.Character + 1;
    }

    /// <summary>
    /// Applies the compiler's <c>PathMap</c> to a source path so the emitted literal
    /// matches what <c>[CallerFilePath]</c> would have produced. See
    /// <c>SourceMapInterceptorGenerator.ApplyPathMap</c> for the measured details.
    /// </summary>
    internal static string ApplyPathMap(string path, ImmutableArray<KeyValuePair<string, string>> pathMap)
    {
        if (pathMap.IsDefaultOrEmpty || string.IsNullOrEmpty(path)) return path;
        foreach (var entry in pathMap.Where(entry => path.StartsWith(entry.Key, StringComparison.Ordinal)))
        {
            var suffix = path.Substring(entry.Key.Length);
            if (entry.Value.IndexOf('/') >= 0 && entry.Value.IndexOf('\\') < 0)
                suffix = suffix.Replace('\\', '/');
            else if (entry.Value.IndexOf('\\') >= 0 && entry.Value.IndexOf('/') < 0)
                suffix = suffix.Replace('/', '\\');

            return entry.Value + suffix;
        }
        return path;
    }

    internal static string Literal(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";
}

/// <summary>
/// Recognises a call to a factory that <c>Reactor.Wrappers.Generator</c> emits for a
/// <c>[GenerateReactorWrapper]</c> element (<c>SettingsCardElement.SettingsCard(...)</c>,
/// usually reached through <c>using static</c>).
///
/// <para><b>Why this has to be syntactic-plus-lookup rather than binding.</b> Roslyn runs
/// every source generator against the same input compilation, so in any generator's view
/// the wrapper factory does not exist yet: the call binds to nothing. What DOES exist is
/// the author's partial element record and its attribute, which name the wrapped control —
/// and the factory is always a static on that record named after the control. So a call is
/// a wrapper-factory call when its name finds nothing invocable in scope and the record
/// the name would be looked up on (an enclosing type, a <c>using static</c> import, or an
/// explicit qualifier) is a wrapper element whose control has that name.</para>
/// </summary>
internal static class WrapperFactoryCalls
{
    internal const string WrapperAttributeMetadataName = "Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapperAttribute";

    // Variants of [GenerateReactorWrapper] that emit a decorator instead of a factory.
    private const string PolymorphicAttributeMetadataName = "Microsoft.UI.Reactor.Wrappers.WrapPolymorphicAttribute";
    private const string DecoratorAttributeMetadataName = "Microsoft.UI.Reactor.Wrappers.WrapDecoratorAttribute";

    /// <summary>
    /// The invoked name when the invocation has a shape a wrapper factory call can take
    /// (<c>Name(...)</c> or <c>Qualifier.Name(...)</c>), otherwise null. Purely syntactic.
    /// </summary>
    internal static string? CandidateName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } member
            when member.IsKind(SyntaxKind.SimpleMemberAccessExpression) => name.Identifier.ValueText,
        _ => null,
    };

    /// <summary>
    /// The wrapper element type whose generated (and therefore invisible) factory this
    /// invocation calls, or null when it calls anything else.
    /// </summary>
    internal static INamedTypeSymbol? ResolveUnboundCall(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken ct)
    {
        if (CandidateName(invocation) is not { Length: > 0 } name) return null;

        // A dynamic argument makes the call dynamically dispatched once the factory exists,
        // and a dynamic call cannot be intercepted.
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (model.GetTypeInfo(argument.Expression, ct).Type is { TypeKind: TypeKind.Dynamic }) return null;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax member)
        {
            if (model.GetSymbolInfo(member.Expression, ct).Symbol is not INamedTypeSymbol qualifier) return null;
            // A visible member of that name is what the call binds to.
            if (!qualifier.GetMembers(name).IsEmpty) return null;
            return IsWrapperFactoryOwner(qualifier, name) ? qualifier : null;
        }

        var position = invocation.SpanStart;

        // Anything invocable already visible under this name wins over the generated factory
        // (or competes with it in overload resolution); either way it is not a call this
        // resolver can vouch for. Types and namespaces are not invocable, so a
        // `using CommunityToolkit.WinUI.Controls;` that brings the CONTROL type named
        // SettingsCard into scope does not hide the factory call.
        foreach (var symbol in model.LookupSymbols(position, name: name))
        {
            if (symbol is not (INamespaceOrTypeSymbol)) return null;
        }

        // Same order C# looks names up in: enclosing types, innermost first, then the
        // `using static` imports of each enclosing scope.
        var enclosing = model.GetEnclosingSymbol(position, ct);
        // An enclosing type's inherited static members are found before any import.
        for (var outer = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType; outer is not null; outer = outer.ContainingType)
        for (var type = outer; type is not null; type = type.BaseType)
        {
            if (IsWrapperFactoryOwner(type, name)) return type;
        }

        foreach (var scope in model.GetImportScopes(position, ct))
        {
            INamedTypeSymbol? match = null;
            foreach (var imported in scope.Imports.Select(static i => i.NamespaceOrType).OfType<INamedTypeSymbol>().Where(i => IsWrapperFactoryOwner(i, name)))
            {
                // Two imports in one scope offering the same factory name would be an
                // overload resolution this resolver does not attempt.
                if (match is not null && !SymbolEqualityComparer.Default.Equals(match, imported)) return null;
                match = imported;
            }
            if (match is not null) return match;
        }

        return null;
    }

    /// <summary>
    /// The <c>[GenerateReactorWrapper]</c> records whose generated factory — invisible here —
    /// is also a candidate for <paramref name="invocation"/> in the real compilation, beside
    /// whatever it binds to here (a hand-written <c>Card(Element, params object[])</c> imported
    /// next to a generated <c>Card(Element? content = null)</c>, which wins once it exists).
    /// Null when there is none, which is every call whose name no source wrapper's factory
    /// has. Over-approximates C#'s name lookup (every enclosing type and every import scope),
    /// so it can only make a caller more conservative.
    /// </summary>
    internal static List<INamedTypeSymbol>? InvisibleFactoryOwners(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken ct)
        => InvisibleFactoryOwners(model, invocation.Expression, ct);

    /// <summary>
    /// <see cref="InvisibleFactoryOwners(SemanticModel, InvocationExpressionSyntax, CancellationToken)"/>
    /// for the name itself (<c>Gauge</c> or <c>Q.Gauge</c>), whether it is called or used as
    /// a method group.
    /// </summary>
    internal static List<INamedTypeSymbol>? InvisibleFactoryOwners(
        SemanticModel model,
        ExpressionSyntax nameExpression,
        CancellationToken ct)
    {
        var name = nameExpression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax memberName } access
                when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) => memberName.Identifier.ValueText,
            _ => null,
        };
        if (name is not { Length: > 0 } || !SourceFactoryNames(model.Compilation).Contains(name)) return null;

        List<INamedTypeSymbol>? owners = null;
        if (nameExpression is MemberAccessExpressionSyntax member)
        {
            if (model.GetSymbolInfo(member.Expression, ct).Symbol is INamedTypeSymbol qualifier)
            {
                for (var type = qualifier; type is not null; type = type.BaseType) Consider(type);
            }
            return owners;
        }

        var position = nameExpression.SpanStart;
        var enclosing = model.GetEnclosingSymbol(position, ct);
        for (var outer = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            // Inherited static members too (`record Derived : FooElement` calling `Foo()`).
            for (var type = outer; type is not null; type = type.BaseType) Consider(type);
        }
        foreach (var scope in model.GetImportScopes(position, ct))
        {
            foreach (var imported in scope.Imports.Select(static i => i.NamespaceOrType).OfType<INamedTypeSymbol>()) Consider(imported);
        }
        return owners;

        void Consider(INamedTypeSymbol type)
        {
            if (!IsWrapperFactoryOwner(type, name)) return;
            // Already visible (the generated file fed back in): an ordinary candidate, not a hidden one.
            if (type.GetMembers(name).Any(static m => m.Locations.Any(static l => l.SourceTree?.FilePath.EndsWith(".Wrapper.g.cs", StringComparison.OrdinalIgnoreCase) == true)))
                return;
            if (owners?.Contains(type, SymbolEqualityComparer.Default) == true) return;
            (owners ??= new List<INamedTypeSymbol>()).Add(type);
        }
    }

    /// <summary>
    /// True when a hidden factory candidate (<see cref="InvisibleFactoryOwners"/>) could give
    /// <paramref name="invocation"/> a different type in the real compilation: the generated
    /// factory returns its own record, so a call already typed as that record is unaffected.
    /// </summary>
    internal static bool ResultCanChangeByInvisibleFactory(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        if (InvisibleFactoryOwners(model, invocation, ct) is not { } owners) return false;
        var type = model.GetTypeInfo(invocation, ct).Type;
        return owners.Any(o => !SymbolEqualityComparer.Default.Equals(o, type));
    }

    /// <summary>
    /// True when a hidden generated factory could change how <paramref name="expression"/>
    /// binds: a call it could outrank (<see cref="ResultCanChangeByInvisibleFactory"/>), or a
    /// METHOD GROUP of that name (<c>Apply(Gauge)</c>), whose conversion it joins.
    /// </summary>
    internal static bool HiddenFactoryMayChange(SemanticModel model, ExpressionSyntax expression, CancellationToken ct)
    {
        if (expression is InvocationExpressionSyntax invocation) return ResultCanChangeByInvisibleFactory(model, invocation, ct);
        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)) return false;
        // The callee of a call is judged with the call; the name half of `Q.Name` with `Q.Name`.
        if (expression.Parent is InvocationExpressionSyntax call && call.Expression == expression) return false;
        if (expression.Parent is MemberAccessExpressionSyntax outer && outer.Name == expression) return false;
        if (SyntaxFacts.IsInTypeOnlyContext(expression)) return false;
        if (InvisibleFactoryOwners(model, expression, ct) is null) return false;
        // Only where it can be a method group: not a type, namespace, variable or member value.
        var info = model.GetSymbolInfo(expression, ct);
        return info.Symbol is null or IMethodSymbol;
    }

    /// <summary>
    /// True when the wrapper generator may give <paramref name="record"/> an INVOCABLE instance
    /// member named <paramref name="name"/> — <c>Set(Action&lt;TControl&gt;)</c>, or a delegate
    /// property <c>On{Event}</c> / <c>On{Property}Changed</c> — which, once it exists, wins over
    /// an extension method that binds here (<c>gauge.Set(...)</c> resolving to some visible
    /// <c>Set(this object, …)</c>). A superset, from every event and property of the control
    /// and its bases, so it can only make a caller more conservative.
    /// </summary>
    internal static bool MayGainInvocableMember(INamedTypeSymbol record, string name)
    {
        if (string.Equals(name, "Set", StringComparison.Ordinal)) return true;

        // Names the record's own attributes spell — [WrapAlias("Level", "Value")] makes the
        // generated property Level and its callback OnLevelChanged, [WrapControlled],
        // Include = … — are generated names too.
        foreach (var spelled in record.GetAttributes().SelectMany(static a => AttributeStrings(a)))
        {
            if (string.Equals(name, spelled, StringComparison.Ordinal)
                || string.Equals(name, "On" + spelled, StringComparison.Ordinal)
                || string.Equals(name, "On" + spelled + "Changed", StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var attribute in record.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != WrapperAttributeMetadataName
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol control)
            {
                continue;
            }
            for (var type = control; type is not null; type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    switch (member)
                    {
                        case IEventSymbol e when string.Equals("On" + e.Name, name, StringComparison.Ordinal):
                            return true;
                        case IPropertySymbol p when string.Equals("On" + p.Name + "Changed", name, StringComparison.Ordinal)
                                                    || (string.Equals(p.Name, name, StringComparison.Ordinal) && p.Type.TypeKind == TypeKind.Delegate):
                            return true;
                    }
                }
            }
        }
        return false;
    }

    private static IEnumerable<string> AttributeStrings(AttributeData attribute)
        => attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(static n => n.Value))
            .SelectMany(static c => c.Kind == TypedConstantKind.Array ? c.Values : ImmutableArray.Create(c))
            .Select(static c => c.Value as string)
            .Where(static s => !string.IsNullOrEmpty(s))!;

    /// <summary>
    /// True when <paramref name="expression"/> is <c>x.Name(...)</c>, <c>x?.Name(...)</c> or the
    /// method group <c>x.Name</c>, and the wrapper generator may add an invocable instance
    /// member <c>Name</c> (<see cref="MayGainInvocableMember"/>) that beats what binds here
    /// (<c>dial.Set(...)</c> resolving to a visible extension). It may when <c>x</c>'s type is
    /// a source wrapper record — complete or not — or derives from one, or is a type parameter
    /// constrained to one; and when <c>x</c> is itself a call to an invisible wrapper factory
    /// or does not bind at all, whose type is unknown here.
    /// </summary>
    internal static bool GeneratedMemberMayWin(SemanticModel model, ExpressionSyntax expression, CancellationToken ct)
    {
        var access = expression is InvocationExpressionSyntax invocation ? invocation.Expression : expression;
        var (receiver, name) = access switch
        {
            MemberAccessExpressionSyntax member => (member.Expression, member.Name.Identifier.ValueText),
            MemberBindingExpressionSyntax binding when binding.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>() is { } conditional
                => (conditional.Expression, binding.Name.Identifier.ValueText),
            _ => (null, null),
        };
        if (receiver is null && access is SimpleNameSyntax simple
            && !(access.Parent is MemberAccessExpressionSyntax outerAccess && outerAccess.Name == access)
            && access.Parent is not MemberBindingExpressionSyntax)
        {
            return ImplicitReceiverMayGain(model, expression, simple, ct);
        }
        if (receiver is null || name is null) return false;
        if (!string.Equals(name, "Set", StringComparison.Ordinal) && !name.StartsWith("On", StringComparison.Ordinal)) return false;
        // The method-group form is judged at its call when it has one.
        if (expression is not InvocationExpressionSyntax && expression.Parent is InvocationExpressionSyntax call && call.Expression == expression) return false;

        var receiverType = model.GetTypeInfo(receiver, ct).Type;
        if (receiverType is null or IErrorTypeSymbol)
        {
            return receiver is not InvocationExpressionSyntax receiverCall
                   || ResolveUnboundCall(model, receiverCall, ct) is not null
                   || receiverType is IErrorTypeSymbol;
        }

        var sources = SourceWrapperRecords(model.Compilation);
        if (sources.IsEmpty) return false;
        var record = WrapperBehind(receiverType, sources, name, depth: 0);
        if (record is null) return false;

        // Already the generated member itself (the generated file fed back in): nothing hidden.
        // A hand-written member of the same name on the record is not evidence: the generated
        // one may still outrank it.
        return model.GetSymbolInfo(access, ct).Symbol is not { } bound
               || !bound.Locations.Any(static l => l.SourceTree?.FilePath.EndsWith(".Wrapper.g.cs", StringComparison.OrdinalIgnoreCase) == true);

        // `Set(f)` written inside a wrapper record (or a type deriving from one): `this.Set`
        // once the generated member exists, whatever binds here.
        static bool ImplicitReceiverMayGain(SemanticModel model, ExpressionSyntax expression, SimpleNameSyntax simple, CancellationToken ct)
        {
            var name = simple.Identifier.ValueText;
            if (!string.Equals(name, "Set", StringComparison.Ordinal) && !name.StartsWith("On", StringComparison.Ordinal)) return false;
            if (expression is not InvocationExpressionSyntax && expression.Parent is InvocationExpressionSyntax call && call.Expression == expression) return false;
            var sources = SourceWrapperRecords(model.Compilation);
            if (sources.IsEmpty) return false;
            var enclosing = model.GetEnclosingSymbol(simple.SpanStart, ct);
            for (var outer = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType; outer is not null; outer = outer.ContainingType)
            {
                if (WrapperBehind(outer, sources, name, depth: 0) is not { } record) continue;
                var bound = model.GetSymbolInfo(simple, ct).Symbol;
                if (bound is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol) return false;
                return bound is null
                       || !bound.Locations.Any(static l => l.SourceTree?.FilePath.EndsWith(".Wrapper.g.cs", StringComparison.OrdinalIgnoreCase) == true);
            }
            return false;
        }

        static INamedTypeSymbol? WrapperBehind(ITypeSymbol type, ImmutableHashSet<INamedTypeSymbol> sources, string name, int depth)
        {
            if (depth > 8) return null;
            if (type is ITypeParameterSymbol parameter)
            {
                return parameter.ConstraintTypes.Select(c => WrapperBehind(c, sources, name, depth + 1)).FirstOrDefault(static r => r is not null);
            }
            for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            {
                if (sources.Contains(current.OriginalDefinition) && MayGainInvocableMember(current.OriginalDefinition, name)) return current.OriginalDefinition;
            }
            return null;
        }
    }

    /// <summary>
    /// True when a method <paramref name="invocation"/>'s name can reach has a type-parameter
    /// constraint that mentions a wrapper record: whether a type argument satisfies it
    /// (<c>where T : IComparer&lt;FooElement&gt;</c> with <c>T = IComparer&lt;Element&gt;</c>) can change
    /// once the record is an Element, and with it the candidates and the result.
    /// </summary>
    internal static bool GroupHasWrapperConstraint(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
        => NameReachesWrapperConstraint(model, invocation.Expression, ct);

    /// <summary>
    /// <see cref="GroupHasWrapperConstraint"/> for a method name that may also be a method
    /// GROUP (<c>Use(Make&lt;IComparer&lt;Element&gt;&gt;)</c>): which of its methods the group
    /// converts with depends on the same constraints.
    /// </summary>
    internal static bool NameReachesWrapperConstraint(SemanticModel model, ExpressionSyntax name, CancellationToken ct)
    {
        // Looked up by name: the member group the compiler reports has already dropped the
        // candidates whose constraints fail here, which are exactly the ones that matter.
        var position = name.SpanStart;
        IEnumerable<ISymbol> candidates = model.GetMemberGroup(name, ct);
        switch (name)
        {
            case SimpleNameSyntax simple:
                candidates = candidates.Concat(model.LookupSymbols(position, name: simple.Identifier.ValueText));
                break;
            case MemberAccessExpressionSyntax member
                when (model.GetSymbolInfo(member.Expression, ct).Symbol as INamespaceOrTypeSymbol ?? model.GetTypeInfo(member.Expression, ct).Type) is { } qualifier:
                candidates = candidates.Concat(model.LookupSymbols(position, qualifier, member.Name.Identifier.ValueText, includeReducedExtensionMethods: true));
                break;
        }
        foreach (var method in candidates.OfType<IMethodSymbol>())
        {
            var definition = (method.ReducedFrom ?? method).OriginalDefinition;
            foreach (var typeParameter in definition.TypeParameters)
            {
                if (typeParameter.ConstraintTypes.Any(c => MentionsIncompleteWrapper(c, model.Compilation))) return true;
            }
        }
        return false;
    }

    private static readonly ConditionalWeakTable<Compilation, ImmutableHashSet<string>> s_sourceFactoryNames = new();

    /// <summary>
    /// The names of the factories <c>[GenerateReactorWrapper]</c> records DECLARED IN THIS
    /// compilation will get — the only factories that are invisible here. Computed once per
    /// compilation from declarations alone, so a call whose name is not in it is rejected
    /// before any binding, and a project with no wrappers pays one namespace walk per edit.
    /// </summary>
    internal static ImmutableHashSet<string> SourceFactoryNames(Compilation compilation)
        => s_sourceFactoryNames.GetValue(compilation, static c =>
        {
            var names = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            Collect(c.SourceModule.GlobalNamespace, names);
            return names.ToImmutable();
        });

    private static void Collect(INamespaceSymbol ns, ImmutableHashSet<string>.Builder names)
    {
        foreach (var type in ns.GetTypeMembers().Where(static t => t.IsRecord && !t.IsGenericType))
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == WrapperAttributeMetadataName
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is INamedTypeSymbol control
                    && IsWrapperFactoryOwner(type, control.Name))
                {
                    names.Add(control.Name);
                }
            }
        }
        foreach (var child in ns.GetNamespaceMembers()) Collect(child, names);
    }

    /// <summary>
    /// True for a <c>[GenerateReactorWrapper]</c> element record that, in this compilation,
    /// does not derive from <c>Element</c> yet: the wrapper generator emits that base along
    /// with the factory, so neither is visible to any other generator.
    /// </summary>
    internal static bool IsIncompleteWrapperElement(INamedTypeSymbol type, Compilation compilation)
        => type.IsRecord && IncompleteWrappers(compilation).Contains(type.OriginalDefinition);

    private static readonly ConditionalWeakTable<Compilation, ImmutableHashSet<INamedTypeSymbol>> s_incompleteWrappers = new();

    /// <summary>
    /// Every such record declared in this compilation. Asked for every type the source-map
    /// generator inspects in a compilation with wrappers, so it is computed once, from the
    /// declaration tables, and answered by a set lookup.
    /// </summary>
    internal static ImmutableHashSet<INamedTypeSymbol> IncompleteWrappers(Compilation compilation)
        => s_incompleteWrappers.GetValue(compilation, static c =>
        {
            if (c.GetTypeByMetadataName("Microsoft.UI.Reactor.Core.Element") is not { } element) return ImmutableHashSet<INamedTypeSymbol>.Empty;
            return SourceWrapperRecords(c)
                .Where(type => !ReactorSourceTransparency.ReturnsElement(type, element))
                .ToImmutableHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        });

    private static readonly ConditionalWeakTable<Compilation, ImmutableHashSet<INamedTypeSymbol>> s_sourceWrappers = new();

    /// <summary>
    /// Every <c>[GenerateReactorWrapper]</c> record declared in this compilation, whether or
    /// not it spells its own <c>: Element</c> base: all of them get generated members this
    /// compilation cannot see.
    /// </summary>
    internal static ImmutableHashSet<INamedTypeSymbol> SourceWrapperRecords(Compilation compilation)
        => s_sourceWrappers.GetValue(compilation, static c =>
        {
            var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            // Syntax first: only a namespace-level record declaration carrying attributes can be
            // one, so only those are bound (the attribute itself is matched semantically, so an
            // alias of it counts). Walking every type symbol instead was measured at most of a
            // second on a large project.
            foreach (var tree in c.SyntaxTrees)
            {
                var attributed = tree.GetRoot()
                    .DescendantNodes(static n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                    .OfType<RecordDeclarationSyntax>()
                    .Where(static r => r.AttributeLists.Count != 0);
                builder.UnionWith(attributed
                    .Select(record => c.GetSemanticModel(tree).GetDeclaredSymbol(record))
                    .OfType<INamedTypeSymbol>()
                    .Where(static type => type.TypeKind != TypeKind.Error
                        && type.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == WrapperAttributeMetadataName)));
            }
            return builder.ToImmutable();
        });

    /// <summary>
    /// True when values of <paramref name="type"/> can carry, produce or convert to a wrapper
    /// record whose generated half this compilation cannot see (see the forwarding member on
    /// <c>SourceMapInterceptorGenerator</c> for the full list). Cached per compilation.
    /// </summary>
    internal static bool MentionsIncompleteWrapper(ITypeSymbol type, Compilation compilation)
    {
        var cache = s_tainted.GetValue(compilation, static _ => new System.Collections.Concurrent.ConcurrentDictionary<ITypeSymbol, bool>(SymbolEqualityComparer.Default));
        return IsTainted(type, compilation, cache, new TaintWalk());
    }

    /// <summary>
    /// The types being followed, and whether the walk ran out of depth. A generic type can
    /// expand without ever repeating (<c>class Loop&lt;T&gt; : IMarker&lt;Loop&lt;Loop&lt;T&gt;&gt;&gt;</c>), so the
    /// walk is bounded; running out is answered "tainted", the fail-closed side.
    /// </summary>
    private sealed class TaintWalk
    {
        public HashSet<ITypeSymbol> InProgress { get; } = new(SymbolEqualityComparer.Default);

        public bool Exhausted { get; set; }
    }

    private const int MaxTaintDepth = 16;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, System.Collections.Concurrent.ConcurrentDictionary<ITypeSymbol, bool>> s_tainted = new();

    private static bool IsTainted(
        ITypeSymbol type,
        Compilation compilation,
        System.Collections.Concurrent.ConcurrentDictionary<ITypeSymbol, bool> cache,
        TaintWalk walk)
    {
        if (cache.TryGetValue(type, out var known)) return known;
        // A cycle (`class A : IComparable<A>`) adds nothing; the outer frame decides. A
        // `false` reached through a cut cycle depends on where the walk started.
        if (walk.InProgress.Count >= MaxTaintDepth)
        {
            walk.Exhausted = true;
            return true;
        }
        if (!walk.InProgress.Add(type)) return false;
        try
        {
            var tainted = type switch
            {
                IArrayTypeSymbol array => IsTainted(array.ElementType, compilation, cache, walk),
                IPointerTypeSymbol pointer => IsTainted(pointer.PointedAtType, compilation, cache, walk),
                // `where T : FooElement`: a T is the record, and binds like one once it is complete.
                ITypeParameterSymbol typeParameter => typeParameter.ConstraintTypes.Any(c => IsTainted(c, compilation, cache, walk)),
                INamedTypeSymbol named => IsTaintedNamed(named, compilation, cache, walk),
                _ => false,
            };
            // The outermost answer is always kept, a bound-cut "true" included; an inner one only
            // when the walk neither cut a cycle against it nor ran out of depth.
            if (walk.InProgress.Count == 1 || (tainted && !walk.Exhausted)) cache[type] = tainted;
            return tainted;
        }
        finally
        {
            walk.InProgress.Remove(type);
        }
    }

    private static bool IsTaintedNamed(
        INamedTypeSymbol named,
        Compilation compilation,
        System.Collections.Concurrent.ConcurrentDictionary<ITypeSymbol, bool> cache,
        TaintWalk walk)
    {
        if (WrapperFactoryCalls.IsIncompleteWrapperElement(named, compilation)) return true;
        // `Dictionary<int, FooElement>.ValueCollection`: the containing types' arguments are
        // not in TypeArguments.
        for (var outer = named.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            if (outer.TypeArguments.Any(a => IsTainted(a, compilation, cache, walk))) return true;
        }
        foreach (var argument in named.TypeArguments)
        {
            if (IsTainted(argument, compilation, cache, walk)) return true;
        }
        if (named.DelegateInvokeMethod is { } invoke)
        {
            if (IsTainted(invoke.ReturnType, compilation, cache, walk)) return true;
            foreach (var p in invoke.Parameters)
            {
                if (IsTainted(p.Type, compilation, cache, walk)) return true;
            }
        }
        // Only a source type can inherit from a record-closed base: metadata cannot name a
        // record declared in this compilation.
        if (SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly))
        {
            if (named.BaseType is { } baseType && IsTainted(baseType, compilation, cache, walk)) return true;
            foreach (var implemented in named.Interfaces)
            {
                if (IsTainted(implemented, compilation, cache, walk)) return true;
            }
            // `implicit operator FooElement(Box)`: a Box converts to the record — and, once the
            // record is complete, through it to Element.
            foreach (var member in named.GetMembers())
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Conversion } conversion
                    && !SymbolEqualityComparer.Default.Equals(conversion.ReturnType, named)
                    && IsTainted(conversion.ReturnType, compilation, cache, walk))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="type"/> is a <c>[GenerateReactorWrapper]</c> element whose
    /// generated factory is named <paramref name="name"/>.
    /// </summary>
    internal static bool IsWrapperFactoryOwner(INamedTypeSymbol type, string name)
    {
        // The wrapper generator emits a namespace-level `partial record`, so a nested or
        // generic element never gets a factory of its own.
        if (!type.IsRecord || type.IsGenericType || type.ContainingType is not null) return false;

        var found = false;
        foreach (var attribute in type.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case PolymorphicAttributeMetadataName:
                case DecoratorAttributeMetadataName:
                    return false;
                case WrapperAttributeMetadataName
                    when attribute.ConstructorArguments.Length == 1
                         && attribute.ConstructorArguments[0].Value is INamedTypeSymbol control
                         && string.Equals(control.Name, name, StringComparison.Ordinal):
                    found = true;
                    break;
            }
        }
        return found;
    }
}

/// <summary>
/// Spec 010 mechanism 1 — the <c>[ReactorSourceTransparent]</c> rules. Shared so a call to a
/// wrapper-generated factory written inside a transparent helper defers to the helper's
/// caller exactly like a call to any other factory does. The rationale for each rule is
/// documented on the forwarding members of <c>SourceMapInterceptorGenerator</c>.
/// </summary>
internal static class ReactorSourceTransparency
{
    internal const string TransparentAttributeMetadataName =
        "Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparentAttribute";

    internal static bool IsTransparent(IMethodSymbol method)
        => method.GetAttributes()
            .Any(static a => a.AttributeClass?.ToDisplayString() == TransparentAttributeMetadataName);

    internal static bool HasUnrenderableConstraint(ITypeParameterSymbol tp) => false;

    internal static bool ReturnsElement(ITypeSymbol type, INamedTypeSymbol elementSymbol)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, elementSymbol)) return true;
        }
        return false;
    }

    internal static string? ForwardabilityProblem(
        IMethodSymbol method, Compilation compilation, INamedTypeSymbol elementSymbol)
    {
        if (method.MethodKind == MethodKind.LocalFunction)
            return "C# interceptors cannot intercept calls to local functions";
        if (method.MethodKind != MethodKind.Ordinary)
            return "C# interceptors can only intercept calls to ordinary methods";
        if (!method.IsStatic)
        {
            // Intercepting an instance method requires the interceptor to be an
            // extension method whose `this` parameter matches the receiver. Supportable,
            // but it is a separate shape from the static forwarding used here, so it is
            // an explicit gap rather than something that silently half-works.
            return "it is an instance method, and only static helpers are supported";
        }
        if (!ReturnsElement(method.ReturnType, elementSymbol))
            return "it does not return a Microsoft.UI.Reactor.Core.Element";
        if (method.Parameters.Any(p => p.RefKind != RefKind.None))
            return "it has a by-ref parameter";
        if (method.IsGenericMethod && method.TypeParameters.Any(HasUnrenderableConstraint))
            return "its type parameters cannot be restated on an interceptor";

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsFileLocal)
                return "it is declared in a file-local type, which generated code cannot name";
            // "Interceptors cannot be declared in generic types at any level of nesting",
            // and an interceptor for a method in a generic type would additionally have to
            // restate the containing type's arity. Out of scope.
            if (type.IsGenericType)
                return "it is declared in a generic type";
        }

        // The interceptor lives in a generated file, so a private or protected member is
        // out of reach however the call site is written. Asking the compilation settles
        // internal-across-assemblies (InternalsVisibleTo) correctly too.
        if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
            return "generated code cannot reach it; make it internal or public";

        return null;
    }

    internal static bool IsInsideTransparentMethod(
        SemanticModel model,
        SyntaxNode node,
        Compilation compilation,
        INamedTypeSymbol elementSymbol,
        CancellationToken ct)
    {
        for (var symbol = model.GetEnclosingSymbol(node.SpanStart, ct);
             symbol is not null;
             symbol = symbol.ContainingSymbol)
        {
            if (symbol is ITypeSymbol or INamespaceSymbol) return false;
            if (symbol is IMethodSymbol enclosing
                && IsTransparent(enclosing)
                && ForwardabilityProblem(enclosing, compilation, elementSymbol) is null)
            {
                return true;
            }
        }

        return false;
    }
}