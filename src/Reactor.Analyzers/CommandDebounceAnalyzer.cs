using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_HOOKS_009</c> — flags a <c>Command</c> / <c>Command&lt;T&gt;</c> that sets a
/// non-zero <c>DebounceMs</c> and is bound to a control <b>without</b> being routed through
/// <c>UseCommand(...)</c> first.
/// </summary>
/// <remarks>
/// The leading-edge debounce state (last-accepted-fire timestamp, in-window flag, re-enable
/// timer) lives in the <c>UseCommand</c> hook store on <c>RenderContext</c> — the only place
/// that persists across renders. A plain <c>Command</c> record is immutable and reconstructed
/// every render, so <c>new Command { DebounceMs = … }</c> bound directly to a control has
/// nowhere to persist that state and is therefore <b>inert: it does not debounce</b> (issue
/// #136 / #636). The runtime gives no error or warning, so this analyzer makes the footgun
/// visible and the code fix wraps the command in <c>UseCommand(...)</c>.
///
/// Heuristic (conservative, local-dataflow): anchor on a <c>new Command { … DebounceMs =
/// &lt;non-zero&gt; … }</c> / <c>new() { … }</c> / <c>cmd with { DebounceMs = … }</c> whose
/// resolved type is the Reactor <c>Command</c>/<c>Command&lt;T&gt;</c>. Report only when the
/// value (inline, or via the local it initializes) flows into a Reactor binding factory /
/// <c>.Command(...)</c> modifier and never passes through a <c>UseCommand</c> call. A command
/// that is returned, stored, or otherwise not obviously bound is left alone.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommandDebounceAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "REACTOR_HOOKS_009";

    private const string CommandNamespace = "Microsoft.UI.Reactor.Core";
    private const string ReactorNamespacePrefix = "Microsoft.UI.Reactor";
    private const string UseCommandName = "UseCommand";

    // Syntactic fallback for the binding sink when full symbol info is unavailable.
    // Mirrors the Command-accepting factories in src/Reactor/Elements/Dsl.cs; the
    // `.Command(...)` fluent modifier (ElementExtensions.cs) is matched by name separately.
    private static readonly HashSet<string> KnownBindingFactories = new(System.StringComparer.Ordinal)
    {
        "Button", "HyperlinkButton", "RepeatButton", "ToggleButton",
        "SplitButton", "ToggleSplitButton", "MenuItem", "AppBarButton",
    };

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "Command.DebounceMs is inert unless routed through UseCommand",
        "This {0} sets a non-zero DebounceMs but is bound without UseCommand; the debounce state lives in the UseCommand hook store, so the command will not debounce. Route it through UseCommand(...).",
        "Reactor.Hooks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DebounceMs only takes effect through RenderContext.UseCommand — the hook store is the only place the debounce window can persist across renders. A raw Command bound directly to a control is reconstructed every render and silently does not debounce. Wrap the command: var cmd = UseCommand(new Command { …, DebounceMs = 1500 }); (issue #136).");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.WithExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        var node = (ExpressionSyntax)ctx.Node;

        // Cheap syntactic gate first — an initializer that assigns a non-zero DebounceMs.
        var initializer = GetInitializer(node);
        if (initializer is null) return;

        var debounce = FindDebounceAssignment(initializer);
        if (debounce is null) return;

        // DebounceMs <= 0 is a no-op at runtime (only > 0 debounces — see RenderContext.UseCommand),
        // so a constant that folds to zero or negative is never a footgun and must never warn. A
        // non-constant expression can't be proven <= 0 at compile time, so it falls through and is
        // judged by the binding: a dynamic value bound directly still warns. We favor surfacing the
        // footgun (a runtime value that turns out > 0 is exactly the inert case this rule targets)
        // over staying silent on the chance it happens to be 0.
        var constant = ctx.SemanticModel.GetConstantValue(debounce.Right, ctx.CancellationToken);
        if (constant.HasValue && constant.Value is int ms && ms <= 0) return;

        // Confirm this is actually the Reactor Command type (avoid matching unrelated `Command`s).
        var type = ctx.SemanticModel.GetTypeInfo(node, ctx.CancellationToken).Type;
        if (!IsReactorCommandType(type)) return;

        // Collect the expressions that represent "this command value": the creation expression
        // itself when used inline, or every reference to the local it initializes.
        var usages = CollectUsages(node, ctx);

        // Routed through UseCommand anywhere → correct usage, suppress.
        if (AnyArgumentTo(usages, isUseCommand: true, ctx)) return;

        // Bound to a Reactor control binding without UseCommand → report at the initializer.
        if (AnyArgumentTo(usages, isUseCommand: false, ctx))
        {
            var commandTypeLabel = type is INamedTypeSymbol { IsGenericType: true } ? "Command<T>" : "Command";
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), commandTypeLabel));
        }
    }

    private static InitializerExpressionSyntax? GetInitializer(ExpressionSyntax node) => node switch
    {
        ObjectCreationExpressionSyntax oce => oce.Initializer,
        ImplicitObjectCreationExpressionSyntax ioce => ioce.Initializer,
        WithExpressionSyntax we => we.Initializer,
        _ => null,
    };

    private static AssignmentExpressionSyntax? FindDebounceAssignment(InitializerExpressionSyntax initializer) =>
        initializer.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(static a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "DebounceMs" });

    private static bool IsReactorCommandType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named) return false;
        // Command or Command<T>, in Microsoft.UI.Reactor.Core.
        var name = named.Name;
        if (name != "Command") return false;
        return named.ContainingNamespace?.ToDisplayString() == CommandNamespace;
    }

    /// <summary>
    /// The set of syntax nodes that carry the command value forward: the creation expression
    /// itself, or — when it is the initializer of <c>var x = …</c> — every in-scope reference
    /// to <c>x</c>. The local case is what lets the rule distinguish
    /// <c>var c = new Command{…}; UseCommand(c);</c> (routed, no warning) from
    /// <c>var c = new Command{…}; Button(c);</c> (bound directly, warning).
    /// </summary>
    private static List<ExpressionSyntax> CollectUsages(ExpressionSyntax node, SyntaxNodeAnalysisContext ctx)
    {
        // `var x = <node>;` — node.Parent is EqualsValueClause, whose parent is the declarator.
        if (node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            && ctx.SemanticModel.GetDeclaredSymbol(declarator, ctx.CancellationToken) is ILocalSymbol local)
        {
            var scope = node.FirstAncestorOrSelf<BlockSyntax>();
            if (scope is not null)
            {
                // Cheap syntactic name match first, then the semantic symbol check — the .Where
                // order preserves that short-circuit so GetSymbolInfo only runs on name matches.
                var refs = scope.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(id => id.Identifier.ValueText == local.Name)
                    .Where(id => SymbolEqualityComparer.Default.Equals(
                        ctx.SemanticModel.GetSymbolInfo(id, ctx.CancellationToken).Symbol, local))
                    .Cast<ExpressionSyntax>()
                    .ToList();
                if (refs.Count > 0) return refs;
            }
        }

        return new List<ExpressionSyntax> { node };
    }

    /// <summary>
    /// True when any usage is a direct argument to an invocation that is (or is not, per
    /// <paramref name="isUseCommand"/>) a <c>UseCommand</c> call. For the non-UseCommand arm
    /// the invocation must additionally look like a Reactor control binding.
    /// </summary>
    private static bool AnyArgumentTo(List<ExpressionSyntax> usages, bool isUseCommand, SyntaxNodeAnalysisContext ctx)
    {
        // The usage must be passed *directly* as an argument (`f(usage)`), not as part of a
        // larger expression (`f(usage.Label)` reads a member and is not a command bind). Project
        // each usage to its enclosing invocation and filter out the ones that aren't direct args.
        // ClimbParentheses keeps `f((usage))` recognized as a direct bind.
        return usages
            .Select(static usage => ClimbParentheses(usage).Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } }
                ? invocation
                : null)
            .Where(static invocation => invocation is not null)
            .Any(invocation =>
            {
                var calleeName = GetInvokedName(invocation!);
                var calleeIsUseCommand = calleeName == UseCommandName;

                return isUseCommand
                    ? calleeIsUseCommand && IsReactorUseCommand(invocation!, ctx)
                    : !calleeIsUseCommand && IsReactorBinding(invocation!, calleeName, ctx);
            });
    }

    // Walks up out of any enclosing parentheses so `Button((cmd))` is still seen as a direct
    // argument bind (the inner expression's immediate parent would otherwise be a
    // ParenthesizedExpression, not the ArgumentSyntax). Mirrors the repo's StripParentheses
    // convention (e.g. ReferenceCurrentReadAnalyzer), but climbs upward rather than downward.
    private static ExpressionSyntax ClimbParentheses(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parens)
            expression = parens;
        return expression;
    }

    // Mirror of IsReactorBinding for the suppression arm. A UseCommand call only counts as routing
    // through the hook when Roslyn resolves the callee to a method under the Microsoft.UI.Reactor
    // namespace (the Component / RenderContext hook). An unrelated helper or local function that
    // merely shares the name must NOT suppress the diagnostic — that would be a false negative
    // (the command is still bound raw and still does not debounce). When symbol resolution fails
    // (incomplete code mid-edit) we fall back to trusting the name match the caller already made,
    // so a genuinely-routed command still suppresses.
    private static bool IsReactorUseCommand(InvocationExpressionSyntax invocation, SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is IMethodSymbol method)
            return IsReactorNamespace(method.ContainingNamespace?.ToDisplayString());

        return true;
    }

    private static bool IsReactorBinding(InvocationExpressionSyntax invocation, string? calleeName, SyntaxNodeAnalysisContext ctx)
    {
        // When Roslyn can resolve the callee, trust it: a binding only counts if the method lives
        // in a Reactor namespace (a Dsl factory or the `.Command(...)` modifier). A resolved callee
        // in any other namespace is an unrelated API that merely shares a name (someone else's
        // `Button`/`MenuItem`/`.Command(...)`) — not a Reactor bind — so we must not warn on it.
        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is IMethodSymbol method)
            return IsReactorNamespace(method.ContainingNamespace?.ToDisplayString());

        // Symbol info unavailable (unresolved / incomplete code mid-edit) — fall back to a
        // conservative syntactic match so the footgun is still surfaced: a known binding factory,
        // or the `.Command(...)` fluent modifier (member-access invocation named Command).
        if (calleeName is not null && KnownBindingFactories.Contains(calleeName)) return true;
        if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Command" }) return true;

        return false;
    }

    // True when ns is the Reactor root namespace or a descendant of it. Shared by the binding-sink
    // check, the UseCommand routing check, and the code fix's UseCommand-in-scope gate so all three
    // judge "is this Reactor's API" identically.
    internal static bool IsReactorNamespace(string? ns) =>
        ns is not null
            && (ns == ReactorNamespacePrefix
                || ns.StartsWith(ReactorNamespacePrefix + ".", System.StringComparison.Ordinal));

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
