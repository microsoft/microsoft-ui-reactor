using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.SourceMap.Generator;

/// <summary>
/// Static facts for inspectors — the identifier an element was assigned to ("declared
/// name", like XAML <c>x:Name</c>) and, per component render, the variable each hook was
/// stored in. Both are emitted as a module initializer that hands
/// <c>ReactorSourceMap.RegisterStaticInfo</c> a fill callback; the runtime builds the
/// tables lazily, on the first diagnostics lookup.
/// </summary>
public sealed partial class SourceMapInterceptorGenerator
{
    private const string RenderContextMetadataName = "Microsoft.UI.Reactor.Core.RenderContext";
    private const string ComponentMetadataName = "Microsoft.UI.Reactor.Core.Component";
    private const string ContextExtensionsMetadataName = "Microsoft.UI.Reactor.Core.ContextExtensions";
    private const string StaticInfoBuilderMetadataName = "Microsoft.UI.Reactor.Diagnostics.ReactorStaticInfoBuilder";

    /// <summary>
    /// Built-in hooks that occupy exactly ONE slot in a <c>RenderContext</c>, on
    /// <c>RenderContext</c> itself, its <c>Component</c> forwarders, and
    /// <c>ContextExtensions</c>. Anything else Reactor ships (composite hooks such as
    /// <c>UseCommand</c>, <c>UseResource</c>, the window hooks) has a slot count this
    /// generator does not track, so the slot index of every hook AFTER it is reported as
    /// <c>?</c>. <c>HookSlotTableTests</c> in Reactor.Tests runs each of these against a live
    /// <c>RenderContext</c> and fails if one stops taking exactly one slot.
    /// </summary>
    internal static readonly ImmutableHashSet<string> SingleSlotHooks = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "UseState", "UseReducer", "UseRef", "UseEffect", "UseMemo", "UseCallback", "UseContext");

    private static void InitializeStaticInfo(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<bool> enabled,
        IncrementalValueProvider<ImmutableArray<KeyValuePair<string, string>>> pathMap,
        IncrementalValueProvider<ImmutableArray<CallSite?>> callSites)
    {
        var hasApi = context.CompilationProvider.Select(static (c, _) =>
            c.GetTypeByMetadataName(StaticInfoBuilderMetadataName) is not null);

        var hookCalls = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsHookCandidate(node),
                transform: static (ctx, ct) => TryDescribeHook(ctx, ct))
            .Where(static x => x is not null)
            .Collect();

        // Every class component that overrides Render(), hooks or not: the runtime walks a
        // component's base types to the Render() it actually runs, so a hook-free override
        // must be recorded to stop that walk from borrowing its base class's hooks.
        var renderOwners = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax m
                    && m.Identifier.ValueText == "Render"
                    && m.Modifiers.Any(static t => t.IsKind(SyntaxKind.OverrideKeyword)),
                transform: static (ctx, ct) => RenderOwner(ctx, ct))
            .Where(static x => x is not null)
            .Collect();

        // Where the project (and its solution) live, so the runtime can publish call sites
        // RELATIVE to them rather than as absolute developer paths. Reactor.targets makes
        // both properties compiler-visible.
        var roots = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
        {
            p.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var project);
            p.GlobalOptions.TryGetValue("build_property.SolutionDir", out var solution);
            return (Project: project ?? string.Empty, Solution: solution ?? string.Empty);
        });

        var input = callSites.Combine(hookCalls).Combine(renderOwners).Combine(enabled).Combine(hasApi).Combine(pathMap).Combine(roots);
        context.RegisterSourceOutput(input, static (spc, tuple) =>
        {
            var ((((((sites, hooks), owners), isEnabled), api), map), root) = tuple;
            if (!isEnabled || !api) return;
            var source = EmitStaticInfo(sites, hooks, owners, map, root.Project, root.Solution);
            if (source is not null) spc.AddSource("ReactorSourceMap.StaticInfo.g.cs", source);
        });
    }

    /// <summary>The CLR full name of a <c>Component</c> subclass that declares this <c>Render()</c> override.</summary>
    private static string? RenderOwner(GeneratorSyntaxContext ctx, global::System.Threading.CancellationToken ct)
    {
        var component = ctx.SemanticModel.Compilation.GetTypeByMetadataName(ComponentMetadataName);
        if (component is null
            || ctx.SemanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)ctx.Node, ct) is not { IsOverride: true, ContainingType: { } owner }
            || !DerivesFrom(owner, component))
        {
            return null;
        }
        return ClrFullName(owner);
    }

    // ── Declared names ────────────────────────────────────────────────────

    /// <summary>
    /// The identifier the value of <paramref name="invocation"/> is assigned to, looking
    /// through fluent modifier chains (<c>TextBlock("x").Bold().Margin(4)</c>), parentheses,
    /// <c>!</c>, casts and the arms of a conditional:
    /// <list type="bullet">
    ///   <item>local or field initializer → the variable (<c>var title = …</c>);</item>
    ///   <item>property initializer → the property;</item>
    ///   <item>simple assignment → the assigned identifier or member name;</item>
    ///   <item>expression body of a method, property, accessor or local function → its name
    ///   (except <c>Render</c>, which names the component's output, not an element).</item>
    /// </list>
    /// Anything else — an argument, a collection element, a <c>return</c> — has no name.
    /// </summary>
    internal static string? DeclaredNameOf(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        INamedTypeSymbol elementSymbol,
        System.Threading.CancellationToken ct)
    {
        SyntaxNode node = invocation;
        while (true)
        {
            switch (node.Parent)
            {
                case ParenthesizedExpressionSyntax p:
                    node = p;
                    continue;
                case PostfixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    node = p;
                    continue;
                case CastExpressionSyntax c when c.Expression == node:
                    node = c;
                    continue;
                case ConditionalExpressionSyntax c when c.WhenTrue == node || c.WhenFalse == node:
                    node = c;
                    continue;
                case MemberAccessExpressionSyntax member
                    when member.Expression == node
                         && member.Parent is InvocationExpressionSyntax outer
                         && outer.Expression == member:
                    // A fluent modifier keeps describing the same element only while it
                    // returns one; `.ToString()` at the end of a chain is not the element.
                    if (model.GetTypeInfo(outer, ct).Type is not { } chained || !ReturnsElement(chained, elementSymbol))
                        return null;
                    node = outer;
                    continue;
                case EqualsValueClauseSyntax equals:
                    return equals.Parent switch
                    {
                        VariableDeclaratorSyntax v => Named(v.Identifier),
                        PropertyDeclarationSyntax prop => Named(prop.Identifier),
                        _ => null,
                    };
                case AssignmentExpressionSyntax assignment
                    when assignment.Right == node && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                    return assignment.Left switch
                    {
                        IdentifierNameSyntax id => Named(id.Identifier),
                        MemberAccessExpressionSyntax m => Named(m.Name.Identifier),
                        _ => null,
                    };
                case ArrowExpressionClauseSyntax arrow:
                    return arrow.Parent switch
                    {
                        MethodDeclarationSyntax m when m.Identifier.ValueText != "Render" => Named(m.Identifier),
                        PropertyDeclarationSyntax prop => Named(prop.Identifier),
                        LocalFunctionStatementSyntax local => Named(local.Identifier),
                        AccessorDeclarationSyntax { Parent: AccessorListSyntax { Parent: PropertyDeclarationSyntax prop } }
                            => Named(prop.Identifier),
                        _ => null,
                    };
                default:
                    return null;
            }
        }
    }

    private static string? Named(SyntaxToken identifier)
        => identifier.ValueText is { Length: > 0 } text && text != "_" ? text : null;

    // ── Hooks ─────────────────────────────────────────────────────────────

    private static bool IsHookCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation) return false;
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => null,
        };
        return IsHookName(name);
    }

    private static bool IsHookName(string? name)
        => name is { Length: > 3 } && name.StartsWith("Use", StringComparison.Ordinal) && char.IsUpper(name[3]);

    private static HookCall? TryDescribeHook(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var model = ctx.SemanticModel;
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method) return null;
        method = method.ReducedFrom ?? method;
        method = method.OriginalDefinition;

        var compilation = model.Compilation;
        var renderContext = compilation.GetTypeByMetadataName(RenderContextMetadataName);
        var component = compilation.GetTypeByMetadataName(ComponentMetadataName);
        if (renderContext is null || component is null) return null;
        if (!IsHook(method, renderContext, component)) return null;

        // ── Which render owns this call? The nearest function boundary decides. ──
        bool conditional = false;
        SyntaxNode child = invocation;
        for (var node = invocation.Parent; node is not null; child = node, node = node.Parent)
        {
            if (IsConditionalBoundary(node, child)) conditional = true;

            switch (node)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                {
                    if (model.GetSymbolInfo(lambda, ct).Symbol is not IMethodSymbol lambdaSymbol
                        || !lambdaSymbol.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, renderContext)))
                    {
                        return null; // a hook inside an event handler or effect callback: not render order
                    }
                    if (lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax host } })
                        return null;

                    var hostSpan = host.SyntaxTree.GetMappedLineSpan(host.ArgumentList.OpenParenToken.Span, ct);
                    return Describe(
                        new HookOwner(
                            HookOwnerKind.RenderFunction,
                            ResolveMappedPath(hostSpan, host.SyntaxTree.FilePath),
                            hostSpan.StartLinePosition.Line + 1,
                            CallSiteColumn(host, hostSpan, ct)));
                }
                case LocalFunctionStatementSyntax:
                case AccessorDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case BaseTypeDeclarationSyntax:
                    return null;
                case MethodDeclarationSyntax methodDeclaration:
                {
                    if (methodDeclaration.Identifier.ValueText != "Render"
                        || model.GetDeclaredSymbol(methodDeclaration, ct) is not { IsOverride: true } render
                        || render.ContainingType is not { } owner
                        || !DerivesFrom(owner, component))
                    {
                        return null; // a custom hook's own body, or an ordinary method
                    }
                    return Describe(new HookOwner(HookOwnerKind.Component, ClrFullName(owner), 0, 0));
                }
            }
        }
        return null;

        HookCall Describe(HookOwner owner)
        {
            var span = invocation.SyntaxTree.GetMappedLineSpan(invocation.ArgumentList.OpenParenToken.Span, ct);
            return new HookCall(
                owner,
                invocation.SyntaxTree.FilePath,
                // Ordered by where the call ENDS: C# evaluates arguments first, so in
                // UseState(UseMemo(...)) the inner UseMemo takes the earlier slot.
                invocation.Span.End,
                HookVariableName(invocation) ?? method.Name,
                span.StartLinePosition.Line + 1,
                SlotCount(method, renderContext, component, compilation, depth: 0, ct),
                conditional);
        }
    }

    private static bool IsHook(IMethodSymbol method, INamedTypeSymbol renderContext, INamedTypeSymbol component)
    {
        if (!IsHookName(method.Name)) return false;
        if (method.ContainingType is { } owner
            && (SymbolEqualityComparer.Default.Equals(owner, renderContext) || DerivesFrom(owner, component)))
        {
            return true;
        }
        // Context hooks take the RenderContext; component-extension hooks
        // (`this.UseElementFocus()`, `this.UseElementRef<T>()`) take the Component.
        return method.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, renderContext)
            || (p.Type is INamedTypeSymbol named && DerivesFrom(named, component)));
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, baseType)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="node"/> makes <paramref name="child"/> run conditionally or
    /// repeatedly — a rules-of-hooks violation, after which slot indices are not static.
    /// </summary>
    private static bool IsConditionalBoundary(SyntaxNode node, SyntaxNode child) => node switch
    {
        IfStatementSyntax ifStatement => ifStatement.Condition != child,
        ElseClauseSyntax => true,
        SwitchSectionSyntax or SwitchExpressionArmSyntax => true,
        ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax => true,
        ConditionalExpressionSyntax conditional => conditional.Condition != child,
        BinaryExpressionSyntax binary when binary.Right == child
            && (binary.IsKind(SyntaxKind.LogicalAndExpression)
                || binary.IsKind(SyntaxKind.LogicalOrExpression)
                || binary.IsKind(SyntaxKind.CoalesceExpression)) => true,
        ConditionalAccessExpressionSyntax access => access.WhenNotNull == child,
        CatchClauseSyntax or FinallyClauseSyntax => true,
        _ => false,
    };

    /// <summary>
    /// The variable a hook's result is stored in: the first non-discard of a deconstruction
    /// (<c>var (count, setCount) = UseState(0)</c> → <c>count</c>), a declarator, or an
    /// assignment target. <c>null</c> when the result is not stored (<c>UseEffect(…)</c>).
    /// </summary>
    private static string? HookVariableName(InvocationExpressionSyntax invocation)
    {
        SyntaxNode node = invocation;
        while (true)
        {
            switch (node.Parent)
            {
                case ParenthesizedExpressionSyntax p:
                    node = p;
                    continue;
                case PostfixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    node = p;
                    continue;
                case MemberAccessExpressionSyntax member
                    when member.Expression == node && member.Parent is not InvocationExpressionSyntax:
                    node = member; // `UseState(0).Value`, `.Item1`
                    continue;
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }:
                    return Named(declarator.Identifier);
                case AssignmentExpressionSyntax assignment when assignment.Right == node:
                    return assignment.Left switch
                    {
                        DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax designation }
                            => FirstNamed(designation),
                        DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single }
                            => Named(single.Identifier),
                        TupleExpressionSyntax tuple => tuple.Arguments.Select(a => a.Expression switch
                        {
                            DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single } => Named(single.Identifier),
                            IdentifierNameSyntax id => Named(id.Identifier),
                            _ => null,
                        }).FirstOrDefault(static n => n is not null),
                        IdentifierNameSyntax id => Named(id.Identifier),
                        MemberAccessExpressionSyntax m => Named(m.Name.Identifier),
                        _ => null,
                    };
                default:
                    return null;
            }
        }
    }

    private static string? FirstNamed(ParenthesizedVariableDesignationSyntax designation)
    {
        foreach (var variable in designation.Variables)
        {
            var name = variable switch
            {
                SingleVariableDesignationSyntax single => Named(single.Identifier),
                ParenthesizedVariableDesignationSyntax nested => FirstNamed(nested),
                _ => null,
            };
            if (name is not null) return name;
        }
        return null;
    }

    /// <summary>
    /// How many <c>RenderContext</c> slots a hook call takes: 1 for <see cref="SingleSlotHooks"/>
    /// on Reactor's own types; for a custom hook written in this compilation, the sum over
    /// the hooks its body calls (straight-line only); otherwise <c>-1</c> (unknown) — including
    /// a hook whose body is chosen at run time (abstract / virtual / interface) or absent.
    /// </summary>
    private static int SlotCount(
        IMethodSymbol method,
        INamedTypeSymbol renderContext,
        INamedTypeSymbol component,
        Compilation compilation,
        int depth,
        System.Threading.CancellationToken ct)
    {
        if (IsReactorOwned(method, renderContext, component, compilation))
            return SingleSlotHooks.Contains(method.Name) ? 1 : -1;

        if (depth >= 8 || method.DeclaringSyntaxReferences.IsDefaultOrEmpty) return -1;
        method = method.PartialImplementationPart ?? method;

        // The body that runs is chosen at run time (abstract, or overridable), or there is no
        // body to read (partial / extern): the analyzed declaration says nothing reliable.
        if (method.IsAbstract || method.IsExtern
            || ((method.IsVirtual || method.IsOverride) && !method.IsSealed && method.ContainingType is not { IsSealed: true }))
        {
            return -1;
        }

        int total = 0;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax(ct);
            if (declaration is BaseMethodDeclarationSyntax { Body: null, ExpressionBody: null }) return -1;
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var call in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsHookCandidate(call)) continue;
                // Calls inside a nested lambda / local function run later, not in render order.
                if (call.Ancestors().TakeWhile(a => a != declaration)
                        .Any(static a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                {
                    continue;
                }
                if (model.GetSymbolInfo(call, ct).Symbol is not IMethodSymbol inner) return -1;
                inner = (inner.ReducedFrom ?? inner).OriginalDefinition;
                if (!IsHook(inner, renderContext, component)) continue;

                SyntaxNode child = call;
                for (var node = call.Parent; node is not null && node != declaration; child = node, node = node.Parent)
                {
                    if (IsConditionalBoundary(node, child)) return -1;
                }

                var slots = SlotCount(inner, renderContext, component, compilation, depth + 1, ct);
                if (slots < 0) return -1;
                total += slots;
            }
        }
        return total;
    }

    private static bool IsReactorOwned(
        IMethodSymbol method, INamedTypeSymbol renderContext, INamedTypeSymbol component, Compilation compilation)
    {
        var owner = method.ContainingType;
        if (owner is null) return false;
        if (SymbolEqualityComparer.Default.Equals(owner, renderContext)) return true;
        if (SymbolEqualityComparer.Default.Equals(owner.OriginalDefinition, component)) return true;
        if (owner.ContainingNamespace?.ToDisplayString() == "Microsoft.UI.Reactor.Core"
            && owner.Name == "Component" && owner.IsGenericType)
            return true;
        if (owner.ToDisplayString() == ContextExtensionsMetadataName) return true;
        // Any other hook compiled into Reactor itself (no source in this compilation).
        return method.DeclaringSyntaxReferences.IsDefaultOrEmpty
            && SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, renderContext.ContainingAssembly);
    }

    /// <summary>CLR <c>Type.FullName</c> of a type's open definition: <c>Ns.Outer+Inner`1</c>.</summary>
    internal static string ClrFullName(INamedTypeSymbol type)
    {
        var parts = new Stack<string>();
        for (var t = type.OriginalDefinition; t is not null; t = t.ContainingType) parts.Push(t.MetadataName);
        var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() + "." : string.Empty;
        return ns + string.Join("+", parts);
    }

    // ── Emit ──────────────────────────────────────────────────────────────

    private static string? EmitStaticInfo(
        ImmutableArray<CallSite?> sites,
        ImmutableArray<HookCall?> hooks,
        ImmutableArray<string?> renderOwners,
        ImmutableArray<KeyValuePair<string, string>> pathMap,
        string projectDirectory,
        string solutionDirectory)
    {
        var body = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            var solution = string.IsNullOrWhiteSpace(solutionDirectory) || solutionDirectory == "*Undefined*"
                ? "null"
                : Literal(ApplyPathMap(solutionDirectory, pathMap));
            body.AppendLine($"            b.Roots({Literal(ApplyPathMap(projectDirectory, pathMap))}, {solution});");
        }

        foreach (var site in sites.Where(static s => s?.DeclaredName is not null))
        {
            body.AppendLine(
                $"            b.Name({Literal(ApplyPathMap(site!.FilePath, pathMap))}, {site.Line}, {site.Column}, {Literal(site.DeclaredName!)});");
        }

        var componentsWithHooks = new HashSet<string>(StringComparer.Ordinal);
        // Per source file: every call site with its declared name (or none) and every render
        // function with its hooks (or none). Two assemblies can stamp the same path (both
        // mapping their roots to /_/); the runtime trusts a file's facts only when every
        // assembly claiming that path agrees on all of them, absent facts included.
        var fileFacts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string> FactsOf(string mappedPath)
        {
            if (!fileFacts.TryGetValue(mappedPath, out var list)) fileFacts[mappedPath] = list = new List<string>();
            return list;
        }
        foreach (var site in sites)
        {
            if (site is null) continue;
            FactsOf(ApplyPathMap(site.FilePath, pathMap)).Add(
                $"S{site.Line}:{site.Column}={site.DeclaredName}");
        }

        foreach (var group in hooks.Where(static h => h is not null).GroupBy(static h => h!.Owner))
        {
            var value = HooksValue(group.OrderBy(static h => h!.SortPath, StringComparer.Ordinal)
                .ThenBy(static h => h!.SortPosition)
                .Select(static h => h!));
            var owner = group.Key;
            if (owner.Kind == HookOwnerKind.Component) componentsWithHooks.Add(owner.Key);
            else FactsOf(ApplyPathMap(owner.Key, pathMap)).Add($"H{owner.Line}:{owner.Column}={value}");
            body.AppendLine(owner.Kind == HookOwnerKind.Component
                ? $"            b.ComponentHooks({Literal(owner.Key)}, {Literal(value)});"
                : $"            b.RenderFunctionHooks({Literal(ApplyPathMap(owner.Key, pathMap))}, {owner.Line}, {owner.Column}, {Literal(value)});");
        }

        foreach (var file in fileFacts.OrderBy(static f => f.Key, StringComparer.Ordinal))
        {
            file.Value.Sort(StringComparer.Ordinal);
            body.AppendLine($"            b.Source({Literal(file.Key)}, {Literal(Fingerprint(file.Value))});");
        }

        // A Render() override with no hooks is recorded too (empty), so a component that
        // inherits it is not given a base class's hooks.
        foreach (var owner in renderOwners.Where(o => o is not null && !componentsWithHooks.Contains(o)).Distinct(StringComparer.Ordinal).OrderBy(static o => o, StringComparer.Ordinal))
            body.AppendLine($"            b.ComponentHooks({Literal(owner!)}, {Literal(string.Empty)});");

        if (body.Length == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Reactor source map — static facts for inspectors (declared names, hook names).");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {InterceptorNamespace}");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        sb.AppendLine("    internal static class ReactorSourceMapStaticInfo");
        sb.AppendLine("    {");
        // Registering stores the delegate only; the tables are built on the first lookup.
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Register()");
        sb.AppendLine("            => global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.RegisterStaticInfo(typeof(ReactorSourceMapStaticInfo).Assembly, Fill);");
        sb.AppendLine();
        sb.AppendLine("        private static void Fill(global::Microsoft.UI.Reactor.Diagnostics.ReactorStaticInfoBuilder b)");
        sb.AppendLine("        {");
        sb.Append(body);
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>FNV-1a 64 over the ordered facts: a deterministic, dependency-free file fingerprint.</summary>
    internal static string Fingerprint(IEnumerable<string> facts)
    {
        ulong hash = 14695981039346656037UL;
        foreach (var fact in facts)
        {
            foreach (char c in fact)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            hash ^= '\n';
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>i:name@line;…</c>. The slot index stays known until a hook with an unknown slot
    /// count, or one that runs conditionally, has been passed; from then on it is <c>?</c>.
    /// </summary>
    internal static string HooksValue(IEnumerable<HookCall> ordered)
    {
        var sb = new StringBuilder();
        int? next = 0;
        foreach (var hook in ordered)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(next is int index ? index.ToString(CultureInfo.InvariantCulture) : "?");
            sb.Append(':').Append(hook.Name).Append('@').Append(hook.Line.ToString(CultureInfo.InvariantCulture));

            next = next is int n && !hook.Conditional && hook.Slots >= 0 ? n + hook.Slots : null;
        }
        return sb.ToString();
    }

    // ── Models ────────────────────────────────────────────────────────────

    internal enum HookOwnerKind { Component, RenderFunction }

    /// <summary>A component class (by CLR full name) or a render function (by the call site it is passed to).</summary>
    internal sealed class HookOwner : IEquatable<HookOwner>
    {
        public HookOwner(HookOwnerKind kind, string key, int line, int column)
        {
            Kind = kind;
            Key = key;
            Line = line;
            Column = column;
        }

        public HookOwnerKind Kind { get; }
        public string Key { get; }
        public int Line { get; }
        public int Column { get; }

        public bool Equals(HookOwner? other)
            => other is not null && Kind == other.Kind && Key == other.Key && Line == other.Line && Column == other.Column;

        public override bool Equals(object? obj) => Equals(obj as HookOwner);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind;
                h = (h * 397) ^ Key.GetHashCode();
                h = (h * 397) ^ Line;
                return (h * 397) ^ Column;
            }
        }
    }

    internal sealed class HookCall : IEquatable<HookCall>
    {
        public HookCall(HookOwner owner, string sortPath, int sortPosition, string name, int line, int slots, bool conditional)
        {
            Owner = owner;
            SortPath = sortPath;
            SortPosition = sortPosition;
            Name = name;
            Line = line;
            Slots = slots;
            Conditional = conditional;
        }

        public HookOwner Owner { get; }
        public string SortPath { get; }
        public int SortPosition { get; }
        public string Name { get; }
        public int Line { get; }

        /// <summary>Slots this call takes, or -1 when unknown.</summary>
        public int Slots { get; }

        /// <summary>The call is inside a branch or loop of its render (rules-of-hooks violation).</summary>
        public bool Conditional { get; }

        public bool Equals(HookCall? other)
            => other is not null
               && Owner.Equals(other.Owner)
               && SortPath == other.SortPath
               && SortPosition == other.SortPosition
               && Name == other.Name
               && Line == other.Line
               && Slots == other.Slots
               && Conditional == other.Conditional;

        public override bool Equals(object? obj) => Equals(obj as HookCall);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Owner.GetHashCode();
                h = (h * 397) ^ SortPosition;
                h = (h * 397) ^ Name.GetHashCode();
                return (h * 397) ^ Line;
            }
        }
    }
}
