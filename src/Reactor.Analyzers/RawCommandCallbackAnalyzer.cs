using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_CMD_001</c> — flags a raw <c>new …Element(…) { … }</c> / <c>… with { … }</c> that
/// assigns <b>both</b> a <c>Command</c> and the element's own click/toggle callback.
/// </summary>
/// <remarks>
/// In WPF/UWP a <c>Button.Click</c> handler fires <i>alongside</i> its <c>Command</c>. Reactor
/// resolves a single effective callback instead:
/// <c>EffectiveCallback(userCallback, cmd) =&gt; userCallback ?? Invokable(cmd)</c>
/// (<c>CommandBindings.cs</c>). The explicit <c>OnClick</c> / toggle handler <b>wins</b>, so the
/// command's <c>Execute</c>/<c>ExecuteAsync</c> never runs — the command is silently dropped.
///
/// This is reachable <b>only</b> via a hand-written record-init: the fluent <c>.Command(...)</c>
/// modifier already sets <c>OnClick = null</c>, and the <c>Button(Command)</c> factory takes no
/// callback. There is no <c>.OnClick()</c> modifier. Hence <b>Info</b>, not Warning.
///
/// The fix <b>deletes the redundant callback</b> (carried in <see cref="Diagnostic.Properties"/>).
/// Moving the callback body into <c>cmd.Execute</c> is deliberately <i>not</i> offered: it bypasses
/// <c>CanExecute</c>, breaks <c>UseCommand</c>/<c>DebounceMs</c> arming, and can swallow an async
/// <c>ExecuteAsync</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawCommandCallbackAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_CMD_001";

    /// <summary>The namespace every command-capable Reactor element record lives in.</summary>
    private const string ElementNamespace = "Microsoft.UI.Reactor.Core";

    private const string CommandProperty = "Command";

    // Code-fix hand-off keys (data travels in Diagnostic.Properties, never message text).
    internal const string CallbackKindKey = "CallbackKind";
    internal const string CallbackNameKey = "CallbackName";
    internal const string KindInitializer = "Initializer";
    internal const string KindCtorArg = "CtorArg";

    /// <summary>
    /// The own click/toggle callback(s) for one command-capable element.
    /// <see cref="InitializerCallbacks"/> is the set of callback property names settable via an
    /// object initializer / <c>with</c>; the analyzer reports the first such assignment found in
    /// source order. <see cref="CtorCallbackParam"/> is the constructor parameter that <i>is</i>
    /// that callback (the positional shape, e.g. <c>new ButtonElement("Save", DoThing)</c>).
    /// </summary>
    internal readonly struct CallbackInfo
    {
        /// <summary>The set of callback property names settable via an object initializer / <c>with</c>.</summary>
        public readonly ImmutableArray<string> InitializerCallbacks;

        /// <summary>The constructor parameter that <i>is</i> the callback (the positional shape).</summary>
        public readonly string CtorCallbackParam;

        public CallbackInfo(ImmutableArray<string> initializerCallbacks, string ctorCallbackParam)
        {
            InitializerCallbacks = initializerCallbacks;
            CtorCallbackParam = ctorCallbackParam;
        }
    }

    /// <summary>
    /// Per-element callback map, keyed by the element record's simple type name. Grounded in
    /// <c>src/Reactor/Core/Element.cs</c>:
    /// <list type="bullet">
    ///   <item><c>OnClick</c> — Button / HyperlinkButton / RepeatButton / SplitButton.</item>
    ///   <item><c>OnIsCheckedChanged</c> (+ <c>OnCheckedStateChanged</c> on ToggleButton) — ToggleButton / ToggleSplitButton.</item>
    /// </list>
    /// This is intentionally <b>not</b> shared with <c>CommandDebounceAnalyzer</c>: that rule keys on
    /// DSL <i>factory</i> names and includes <c>MenuItem</c>/<c>AppBarButton</c>, which are plain data
    /// records (<c>MenuFlyoutItemData</c>/<c>AppBarButtonData</c>), not command-capable Elements.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, CallbackInfo> CommandElements =
        new Dictionary<string, CallbackInfo>(StringComparer.Ordinal)
        {
            { "ButtonElement",            new(ImmutableArray.Create("OnClick"), "OnClick") },
            { "HyperlinkButtonElement",   new(ImmutableArray.Create("OnClick"), "OnClick") },
            { "RepeatButtonElement",      new(ImmutableArray.Create("OnClick"), "OnClick") },
            { "SplitButtonElement",       new(ImmutableArray.Create("OnClick"), "OnClick") },
            { "ToggleButtonElement",      new(ImmutableArray.Create("OnIsCheckedChanged", "OnCheckedStateChanged"), "OnIsCheckedChanged") },
            { "ToggleSplitButtonElement", new(ImmutableArray.Create("OnIsCheckedChanged"), "OnIsCheckedChanged") },
        };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Command and the element's own callback are both set; the callback wins",
        "'{0}' is set alongside 'Command'; Reactor uses the callback, so the command never runs. Remove '{0}' to run the command (or drop 'Command').",
        "Reactor.Commanding",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A command-capable element resolves a single effective callback (userCallback ?? Invokable(command)), " +
            "so an explicit OnClick / OnIsCheckedChanged / OnCheckedStateChanged set alongside a Command wins and the " +
            "command's Execute/ExecuteAsync never runs. The fluent .Command(...) modifier already nulls the callback, so " +
            "this only happens in a raw record-init. Remove the redundant callback (or drop the Command).");

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

        // Command is only ever an init property, so a shadowing bug requires a Command assignment
        // in this node's initializer. Cheap syntactic gate before any semantic query.
        var initializer = GetInitializer(node);
        if (initializer is null)
            return;

        var commandAssignment = FindInitializerAssignment(initializer, CommandProperty);
        if (commandAssignment is null)
            return;

        // `Command = null` leaves nothing to shadow — the command never runs either way.
        if (IsNullish(commandAssignment.Right))
            return;

        // Confirm the created / `with` type is a known command-capable Reactor element.
        var type = ctx.SemanticModel.GetTypeInfo(node, ctx.CancellationToken).Type;
        if (type is null)
            return;
        if (!CommandElements.TryGetValue(type.Name, out var info))
            return;
        if (type.ContainingNamespace?.ToDisplayString() != ElementNamespace)
            return;

        // A provably metadata-only command — an inline `new Command { … }` / `new() { … }` that
        // assigns neither Execute nor ExecuteAsync — has no delegate to invoke (Invokable(cmd) is
        // null), so the callback is the ONLY executable path and is NOT shadowing the command. Do
        // not fire (and never suggest deleting the sole handler). Opaque commands (a variable,
        // field, or factory result) are undecidable syntactically and stay covered by the match;
        // spec §4.3 rates that residual false positive low.
        if (IsProvablyMetadataOnlyCommand(commandAssignment.Right))
            return;

        // (a) Redundant callback assigned via the object initializer / `with`.
        var initCallback = FindInitializerCallback(initializer, info.InitializerCallbacks);
        if (initCallback is not null)
        {
            Report(ctx, initCallback.GetLocation(), KindInitializer, ((IdentifierNameSyntax)initCallback.Left).Identifier.ValueText);
            return;
        }

        // (b) Redundant callback passed as the constructor's positional/named argument.
        var argumentList = GetArgumentList(node);
        if (argumentList is null)
            return;

        var ctor = ctx.SemanticModel.GetSymbolInfo(node, ctx.CancellationToken).Symbol as IMethodSymbol;
        var callbackArg = FindCtorCallbackArgument(argumentList, ctor, info.CtorCallbackParam);
        if (callbackArg is not null && !IsNullish(callbackArg.Expression))
            Report(ctx, callbackArg.GetLocation(), KindCtorArg, info.CtorCallbackParam);
    }

    private static void Report(SyntaxNodeAnalysisContext ctx, Location location, string kind, string callbackName)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(CallbackKindKey, kind)
            .Add(CallbackNameKey, callbackName);

        ctx.ReportDiagnostic(Diagnostic.Create(Rule, location, properties, callbackName));
    }

    private static InitializerExpressionSyntax? GetInitializer(ExpressionSyntax node) => node switch
    {
        ObjectCreationExpressionSyntax oce => oce.Initializer,
        ImplicitObjectCreationExpressionSyntax ioce => ioce.Initializer,
        WithExpressionSyntax we => we.Initializer,
        _ => null,
    };

    private static ArgumentListSyntax? GetArgumentList(ExpressionSyntax node) => node switch
    {
        ObjectCreationExpressionSyntax oce => oce.ArgumentList,
        ImplicitObjectCreationExpressionSyntax ioce => ioce.ArgumentList,
        _ => null,
    };

    /// <summary>First <c>Name = …</c> assignment in the initializer whose left-hand side is <paramref name="name"/>.</summary>
    private static AssignmentExpressionSyntax? FindInitializerAssignment(InitializerExpressionSyntax initializer, string name) =>
        initializer.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a => a.Left is IdentifierNameSyntax id && id.Identifier.ValueText == name);

    /// <summary>First non-null callback assignment among <paramref name="callbackNames"/>, in source order.</summary>
    private static AssignmentExpressionSyntax? FindInitializerCallback(InitializerExpressionSyntax initializer, ImmutableArray<string> callbackNames)
    {
        foreach (var expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax id &&
                callbackNames.Contains(id.Identifier.ValueText) &&
                !IsNullish(assignment.Right))
            {
                return assignment;
            }
        }

        return null;
    }

    /// <summary>
    /// The argument bound to the callback constructor parameter, or <c>null</c>. A <b>named</b>
    /// callback argument (<c>OnClick: h</c>) is matched regardless of the other arguments. The
    /// <b>positional</b> callback (mapped by the resolved parameter ordinal) is only resolved when
    /// the call is entirely positional; a call that mixes named and positional arguments skips the
    /// positional path — a rare, intentional false negative that avoids brittle positional-slot
    /// accounting.
    /// </summary>
    private static ArgumentSyntax? FindCtorCallbackArgument(ArgumentListSyntax argumentList, IMethodSymbol? constructor, string parameterName)
    {
        var arguments = argumentList.Arguments;

        // Named form — position-independent, so always safe to match.
        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == parameterName)
                return argument;
        }

        // Pure-positional form only.
        if (arguments.Any(a => a.NameColon is not null))
            return null;
        if (constructor is null)
            return null;

        var parameter = constructor.Parameters.FirstOrDefault(p => p.Name == parameterName);
        if (parameter is null)
            return null;

        return parameter.Ordinal < arguments.Count ? arguments[parameter.Ordinal] : null;
    }

    /// <summary>True for <c>null</c> and <c>default</c> — a callback that leaves the command intact.</summary>
    private static bool IsNullish(ExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.NullLiteralExpression) ||
        expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
        expression is DefaultExpressionSyntax;

    /// <summary>
    /// True when <paramref name="commandExpr"/> is an <b>inline</b> command creation
    /// (<c>new Command { … }</c> / <c>new() { … }</c>) that assigns neither <c>Execute</c> nor
    /// <c>ExecuteAsync</c> to a non-null delegate — i.e. a statically-provable metadata-only
    /// command that has nothing to run. Anything else (a variable, field, factory result, or
    /// <c>with</c> expression) is opaque and returns <c>false</c>.
    /// </summary>
    private static bool IsProvablyMetadataOnlyCommand(ExpressionSyntax commandExpr)
    {
        InitializerExpressionSyntax? initializer;
        switch (commandExpr)
        {
            case ObjectCreationExpressionSyntax oce:
                initializer = oce.Initializer;
                break;
            case ImplicitObjectCreationExpressionSyntax ioce:
                initializer = ioce.Initializer;
                break;
            default:
                return false;
        }

        // No initializer (e.g. `new Command()`) => nothing assigned => metadata-only.
        if (initializer is null)
            return true;

        var hasExecute = initializer.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Any(a => a.Left is IdentifierNameSyntax id
                && (id.Identifier.ValueText == "Execute" || id.Identifier.ValueText == "ExecuteAsync")
                && !IsNullish(a.Right));

        return !hasExecute;
    }
}
