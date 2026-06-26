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

        // DebounceMs = 0 (or a constant that folds to 0) is the documented "off" value — never warn.
        var constant = ctx.SemanticModel.GetConstantValue(debounce.Right, ctx.CancellationToken);
        if (constant.HasValue && constant.Value is int zero && zero == 0) return;

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

    private static AssignmentExpressionSyntax? FindDebounceAssignment(InitializerExpressionSyntax initializer)
    {
        foreach (var expr in initializer.Expressions)
        {
            if (expr is AssignmentExpressionSyntax a
                && a.Left is IdentifierNameSyntax { Identifier.ValueText: "DebounceMs" })
            {
                return a;
            }
        }
        return null;
    }

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
                var refs = new List<ExpressionSyntax>();
                foreach (var id in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (id.Identifier.ValueText != local.Name) continue;
                    var symbol = ctx.SemanticModel.GetSymbolInfo(id, ctx.CancellationToken).Symbol;
                    if (SymbolEqualityComparer.Default.Equals(symbol, local))
                        refs.Add(id);
                }
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
        foreach (var usage in usages)
        {
            // The usage must be passed *directly* as an argument (`f(usage)`), not as part of a
            // larger expression (`f(usage.Label)` reads a member and is not a command bind).
            if (usage.Parent is not ArgumentSyntax arg) continue;
            if (arg.Parent is not ArgumentListSyntax argList) continue;
            if (argList.Parent is not InvocationExpressionSyntax invocation) continue;

            var calleeName = GetInvokedName(invocation);
            var calleeIsUseCommand = calleeName == UseCommandName;

            if (isUseCommand)
            {
                if (calleeIsUseCommand) return true;
            }
            else if (!calleeIsUseCommand && IsReactorBinding(invocation, calleeName, ctx))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsReactorBinding(InvocationExpressionSyntax invocation, string? calleeName, SyntaxNodeAnalysisContext ctx)
    {
        // Preferred: the resolved callee is a Reactor API (factory or `.Command(...)` modifier).
        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is IMethodSymbol method)
        {
            var ns = method.ContainingNamespace?.ToDisplayString();
            if (ns is not null && (ns == ReactorNamespacePrefix || ns.StartsWith(ReactorNamespacePrefix + ".", System.StringComparison.Ordinal)))
                return true;
        }

        // Syntactic fallback when symbol info is unavailable: a known binding factory, or the
        // `.Command(...)` fluent modifier (member-access invocation named Command).
        if (calleeName is not null && KnownBindingFactories.Contains(calleeName)) return true;
        if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Command" }) return true;

        return false;
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
