using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_PERF_FUNCREF</c> — flags a <c>new Command { … }</c> / <c>new Command&lt;T&gt; { … }</c>
/// object-creation evaluated directly in a component's render path (a <c>Render()</c> override or a
/// custom <c>Use*</c> hook method) that is <b>not</b> wrapped in <c>UseMemo</c>/<c>UseCommand</c>.
/// </summary>
/// <remarks>
/// <para>
/// Constructing a <c>Command</c> inline in the render path allocates a fresh command record — and
/// its captured closures — on every render. Wrapping the construction in
/// <c>UseMemo(() =&gt; new Command { … }, deps)</c> keeps a stable instance across renders until its
/// dependencies change. This is a pure allocation/identity-hygiene nudge, directly analogous to
/// <see cref="HookRulesAnalyzer"/>'s <c>REACTOR_HOOKS_004</c> (a freshly-allocated value in a
/// render-path expression). It ships at <see cref="DiagnosticSeverity.Info"/>.
/// </para>
/// <para>
/// The message is deliberately scoped to the allocation/identity concern and does <b>not</b> claim
/// keyboard-accelerator "rewire churn" or an unbounded accelerator-table "leak": the framework
/// diffs a bound command by <em>value</em> (<c>CommandBindings.CommandModuloDelegatesComparer</c>,
/// applied through the <c>OneWay</c> descriptor which skips its setter when the comparer reports
/// equal), so a fresh-but-equal command does not re-apply accelerator metadata; and where a host
/// does rebuild accelerators each reconcile (<c>CompositeLifecycle.UpdateCommandHost</c>) it clears
/// and re-adds — bounded, not a leak, and unaffected by memoizing the command. The verified,
/// defensible win of memoizing is avoiding the per-render allocation. (spec 060 §12 / commanding.md.)
/// </para>
/// <para>
/// Detection is syntactic-first: only an explicit <c>new Command</c>/<c>new Command&lt;T&gt;</c> (by
/// name) or an implicit <c>new() { … }</c> that resolves to the Reactor <c>Command</c> type, lexically
/// inside a <c>Render()</c>/<c>Use*</c> method and <b>not</b> nested inside a deferred lambda /
/// local-function (event handlers, effect bodies, and the <c>UseMemo</c> factory lambda itself all
/// run off the render tick — so a command built there is not a per-render allocation, and the
/// <c>UseMemo</c> factory case is exactly the recommended fix). Commands that set an active
/// <c>DebounceMs</c> are ceded to <c>REACTOR_HOOKS_009</c> (they must go through <c>UseCommand</c>,
/// not <c>UseMemo</c>), and a command that is a direct argument to <c>UseCommand(...)</c> is left
/// alone so the two command rules never give conflicting advice.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MemoizeCommandAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "REACTOR_PERF_FUNCREF";

    private const string CommandNamespace = "Microsoft.UI.Reactor.Core";
    private const string UseCommandName = "UseCommand";

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "Command constructed in the render path should be memoized",
        "This {0} is constructed in the render path and re-allocated every render; wrap it in UseMemo(() => new {0} {{ … }}, deps) to keep a stable instance across renders",
        "Reactor.Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Constructing a Command with new Command { … } inside a component's render path (a Render() override or a custom Use* hook) allocates a fresh command record — and its captured closures — on every render. Wrapping the construction in UseMemo(() => new Command { … }, deps) keeps a stable instance across renders until its dependencies change (analogous to REACTOR_HOOKS_004). Commands already wrapped in UseMemo/UseCommand, deferred inside an event handler or effect lambda, or built outside the render path are left alone.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        var node = (ExpressionSyntax)ctx.Node;

        // Cheap syntactic gate: an explicit `new Command`/`new Command<T>` by name. An implicit
        // `new() { … }` carries no type syntax, so it falls through to the semantic check below.
        if (node is ObjectCreationExpressionSyntax oce && !IsCommandTypeName(oce.Type))
            return;

        // Must be evaluated directly in the render path: inside a Render()/Use* method body, and in a
        // position where a hook could legally be introduced — i.e. not deferred into a nested lambda /
        // local function (event handlers, effect bodies, LINQ projections and the UseMemo factory
        // lambda itself run off the render tick) and not inside a conditional / loop / try where a
        // UseMemo would violate the rules-of-hooks (REACTOR_HOOKS_001). This keeps the offered fix
        // legal and mirrors HookRulesAnalyzer.FindConditionalAncestor's boundary set.
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (!IsRenderOrCustomHook(method)) return;
        if (CrossesHookIllegalBoundary(node, method!)) return;

        // Confirm the Reactor Command type — the single semantic call, after the cheap syntactic gates.
        var type = ctx.SemanticModel.GetTypeInfo(node, ctx.CancellationToken).Type;
        if (!IsReactorCommandType(type)) return;

        // Anchor to an actual Reactor render context so a non-Reactor helper that merely happens to be
        // named Render()/UseXxx and build a Reactor Command isn't flagged. Battle-tested rule (task
        // contract §2.1 / HookRulesAnalyzer.IsLikelyReactorHook): accept a Component OR RenderContext
        // enclosing type, plus RenderContext-extension custom hooks. Left permissive only when symbols
        // don't resolve (incomplete code mid-edit), so the diagnostic isn't lost.
        if (!IsInReactorRenderContext(method!, ctx)) return;

        // Cede debounced commands to REACTOR_HOOKS_009 (CommandDebounceAnalyzer): a non-zero
        // DebounceMs only works through UseCommand — not UseMemo — so that rule owns them. Without
        // this, `new Command { …, DebounceMs = 1500 }` bound raw in Render would draw BOTH a HOOKS_009
        // warning (route through UseCommand) and this Info nudge (memoize), i.e. conflicting advice.
        if (SetsActiveDebounce(GetInitializer(node), ctx)) return;

        // A command that is a direct argument to a Reactor UseCommand(...) is left alone: the author
        // has engaged the command lifecycle, and HOOKS_009's own code fix produces exactly this shape.
        // Accepted false negative: UseCommand returns a plain sync command UNCHANGED
        // (RenderContext.UseCommand), so the allocation technically remains — but suppressing keeps the
        // two command rules from fighting, which is the lower-false-positive, more-consistent choice.
        if (IsDirectArgumentToUseCommand(node, ctx)) return;

        var label = type is INamedTypeSymbol { IsGenericType: true } ? "Command<T>" : "Command";
        ctx.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), label));
    }

    // Explicit `new Command { … }` / `new Command<T> { … }` — match the simple, generic, qualified
    // (`Core.Command`), and alias-qualified spellings by their final identifier.
    private static bool IsCommandTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText == "Command",
        GenericNameSyntax g => g.Identifier.ValueText == "Command",
        QualifiedNameSyntax q => IsCommandTypeName(q.Right),
        AliasQualifiedNameSyntax a => IsCommandTypeName(a.Name),
        _ => false,
    };

    private static bool IsRenderOrCustomHook(MethodDeclarationSyntax? method)
    {
        if (method is null) return false;
        var name = method.Identifier.ValueText;
        // A Render() override, or a custom-hook method by convention (UseXxx). Mirrors
        // HookRulesAnalyzer.IsRenderOrCustomHook / LooksLikeHook.
        return name == "Render" || (name.Length > 3 && name.StartsWith("Use", System.StringComparison.Ordinal) && char.IsUpper(name[3]));
    }

    // True when any ancestor between <paramref name="node"/> and its enclosing <paramref name="method"/>
    // is a construct where a hook could not legally be introduced — a lambda / anonymous method /
    // local function (deferred, so the construction is not a per-render allocation), or a conditional
    // / loop / switch / try (where wrapping in UseMemo would violate the rules-of-hooks). Mirrors the
    // boundary set of HookRulesAnalyzer.FindConditionalAncestor (extended with switch-expression, LINQ
    // query, and the conditionally-evaluated right operand of ?? / && / ||) so the rule fires only
    // where the offered UseMemo fix is both meaningful and legal.
    private static bool CrossesHookIllegalBoundary(SyntaxNode node, MethodDeclarationSyntax method)
    {
        for (SyntaxNode? child = node, n = node.Parent; n is not null && n != method; child = n, n = n.Parent)
        {
            switch (n)
            {
                case SimpleLambdaExpressionSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                case LocalFunctionStatementSyntax:
                case IfStatementSyntax:
                case ElseClauseSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case SwitchStatementSyntax:
                case SwitchSectionSyntax:
                case SwitchExpressionSyntax:
                case SwitchExpressionArmSyntax:
                case TryStatementSyntax:
                case CatchClauseSyntax:
                case FinallyClauseSyntax:
                case QueryExpressionSyntax:
                case ConditionalExpressionSyntax:
                    return true;
                // The right operand of ?? / && / || is evaluated conditionally (e.g.
                // `existing ?? new Command { … }`); the left operand always runs, so only suppress
                // when we ascended from the right side.
                case BinaryExpressionSyntax bin
                    when (bin.IsKind(SyntaxKind.CoalesceExpression)
                        || bin.IsKind(SyntaxKind.LogicalAndExpression)
                        || bin.IsKind(SyntaxKind.LogicalOrExpression))
                        && ReferenceEquals(child, bin.Right):
                    return true;
            }
        }
        return false;
    }

    // Anchors the enclosing Render()/Use* method to a genuine Reactor render context: an instance
    // method on a Component / RenderContext-derived type (the Render override or an instance custom
    // hook), or a RenderContext-extension custom hook (`static UseXxx(this RenderContext ctx, …)`).
    // Mirrors HookRulesAnalyzer.IsLikelyReactorHook. Returns true when the method symbol can't be
    // resolved (incomplete code mid-edit) so the diagnostic isn't lost on a transient bind failure.
    private static bool IsInReactorRenderContext(MethodDeclarationSyntax method, SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(method, ctx.CancellationToken) is not IMethodSymbol symbol)
            return true;

        if (DerivesFromComponentOrRenderContext(symbol.ContainingType)) return true;

        if (symbol.IsExtensionMethod && symbol.Parameters.Length > 0)
            return DerivesFromComponentOrRenderContext(symbol.Parameters[0].Type as INamedTypeSymbol);

        return false;
    }

    private static bool DerivesFromComponentOrRenderContext(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            // Fully-qualified (global::-stripped) so the compare never depends on a minimally-qualified
            // display name — mirrors HookRulesAnalyzer.IsOrDerivesFrom.
            var name = t.OriginalDefinition
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "");
            if (name is "Microsoft.UI.Reactor.Core.Component" or "Microsoft.UI.Reactor.Core.RenderContext"
                || name.StartsWith("Microsoft.UI.Reactor.Core.Component<", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsReactorCommandType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Command" } named
            && named.ContainingNamespace?.ToDisplayString() == CommandNamespace;

    private static InitializerExpressionSyntax? GetInitializer(ExpressionSyntax node) => node switch
    {
        ObjectCreationExpressionSyntax o => o.Initializer,
        ImplicitObjectCreationExpressionSyntax i => i.Initializer,
        _ => null,
    };

    // True when the initializer sets a DebounceMs that is not a constant &lt;= 0 — i.e. the command
    // is in REACTOR_HOOKS_009's domain (a non-zero or dynamic leading-edge debounce that only works
    // through UseCommand). Mirrors CommandDebounceAnalyzer's DebounceMs gate so the two rules divide
    // cleanly. DebounceMs absent, or an explicit constant 0/negative (no debounce), does not suppress.
    private static bool SetsActiveDebounce(InitializerExpressionSyntax? initializer, SyntaxNodeAnalysisContext ctx)
    {
        if (initializer is null) return false;
        var debounce = initializer.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(static a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "DebounceMs" });
        if (debounce is null) return false;

        var constant = ctx.SemanticModel.GetConstantValue(debounce.Right, ctx.CancellationToken);
        if (constant.HasValue && constant.Value is int ms && ms <= 0) return false;
        return true;
    }

    // True when <paramref name="node"/> is passed directly as an argument to a Reactor UseCommand(...)
    // call — `UseCommand(new Command { … })`. Climbs enclosing parentheses so `UseCommand((cmd))` still
    // counts. When Roslyn resolves the callee it must live in a Reactor namespace (the Component /
    // RenderContext hook); when symbol info is unavailable (incomplete code mid-edit) we fall back to
    // trusting the name match, matching CommandDebounceAnalyzer's conservative behaviour.
    private static bool IsDirectArgumentToUseCommand(ExpressionSyntax node, SyntaxNodeAnalysisContext ctx)
    {
        var expr = node;
        while (expr.Parent is ParenthesizedExpressionSyntax parens)
            expr = parens;

        if (expr.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } })
            return false;

        if (GetInvokedName(invocation) != UseCommandName) return false;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is IMethodSymbol method)
            return CommandDebounceAnalyzer.IsReactorNamespace(method.ContainingNamespace?.ToDisplayString());

        return true;
    }

    private static string? GetInvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name switch
        {
            GenericNameSyntax gn => gn.Identifier.ValueText,
            { } simple => simple.Identifier.ValueText,
        },
        MemberBindingExpressionSyntax mb => mb.Name.Identifier.ValueText,
        _ => null,
    };
}
