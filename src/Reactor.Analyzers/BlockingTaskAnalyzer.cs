using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_THREAD_002: Detects blocking on a <c>Task</c>/<c>ValueTask</c> — a
/// <c>.Result</c> read, a <c>.Wait()</c> call, or a <c>.GetAwaiter().GetResult()</c>
/// call — lexically inside a component <c>Render()</c> override or a <c>UseEffect</c>
/// effect lambda. Both run on the UI thread, so blocking there deadlocks or freezes
/// the reconciler and the dispatcher together.
/// </summary>
/// <remarks>
/// This is the WinForms/WPF "just block for the result" reflex. There is no mechanical
/// fix (the correct rewrite is <c>UseResource</c> / an async effect, which restructures
/// the method) so the diagnostic only points at the async-data recipe.
///
/// Detection is a cheap syntactic gate (member name <c>Result</c>/<c>Wait</c>/
/// <c>GetResult</c>) followed by a semantic receiver-type confirmation, so false
/// positives are near zero: the receiver must resolve to <c>Task</c>/<c>Task&lt;T&gt;</c>
/// or <c>ValueTask</c>/<c>ValueTask&lt;T&gt;</c>. A block reached only through a nested
/// lambda (e.g. <c>Task.Run(() =&gt; t.Result)</c>) is intentionally ignored — it no
/// longer runs on the render/effect thread — which also covers every other deferred
/// callback (event handlers, LINQ projections, continuations).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlockingTaskAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_THREAD_002";

    private const string ComponentType = "Microsoft.UI.Reactor.Core.Component";
    private const string RenderContextType = "Microsoft.UI.Reactor.Core.RenderContext";

    private static readonly LocalizableString Title =
        "Blocking a Task on the UI thread in Render or an effect";

    private static readonly LocalizableString MessageFormat =
        "Blocking a Task with '{0}' in {1} freezes the UI thread. Use UseResource or an async effect ('await') instead of blocking.";

    private static readonly LocalizableString Description =
        "Render() and UseEffect effects run on the UI thread. Reading Task.Result or calling " +
        ".Wait()/.GetAwaiter().GetResult() there blocks the dispatcher and the reconciler, " +
        "deadlocking or freezing the app. Fetch async data with UseResource, or do the work in " +
        "an async effect that awaits and then sets state.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Threading",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <summary>Whether — and how — a blocking expression is inside a flagged context.</summary>
    private enum FlagContext
    {
        None,
        Render,
        Effect,
    }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var known = TaskTypes.Resolve(start.Compilation);
            if (!known.Any)
            {
                // No Task/ValueTask in this compilation — nothing this rule can flag.
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeMemberAccess(ctx, known), SyntaxKind.SimpleMemberAccessExpression);
            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeInvocation(ctx, known), SyntaxKind.InvocationExpression);
            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeConditionalAccess(ctx, known), SyntaxKind.ConditionalAccessExpression);
        });
    }

    /// <summary>Handles the <c>task.Result</c> property-read form.</summary>
    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context, TaskTypes known)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Syntactic gate first (cheap): only `.Result`.
        if (memberAccess.Name.Identifier.Text != "Result")
            return;

        // `Task<T>.Result` is a property, never invoked. If this member access is the
        // callee of an invocation it is a method named `Result`, not the task property.
        if (memberAccess.Parent is InvocationExpressionSyntax invParent && invParent.Expression == memberAccess)
            return;

        // Semantic receiver-type confirmation.
        if (!known.IsTaskLike(context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type))
            return;

        var flag = ClassifyContext(context.SemanticModel, memberAccess);
        if (flag == FlagContext.None)
            return;

        Report(context, memberAccess, ".Result", flag);
    }

    /// <summary>Handles the <c>task.Wait()</c> and <c>task.GetAwaiter().GetResult()</c> forms.</summary>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, TaskTypes known)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var name = memberAccess.Name.Identifier.Text;

        ExpressionSyntax? taskReceiver;
        string blockingForm;
        var allowConfiguredAwaitable = false;

        if (name == "Wait")
        {
            // Match `.Wait()` exactly. Timeout overloads (`Wait(int)`/`Wait(TimeSpan)`)
            // return a bool and include non-blocking poll shapes (`Wait(0)`), so they
            // are intentionally out of scope.
            if (invocation.ArgumentList.Arguments.Count != 0)
                return;
            taskReceiver = memberAccess.Expression;
            blockingForm = ".Wait()";
        }
        else if (name == "GetResult")
        {
            // Require the exact zero-arg `<task>.GetAwaiter().GetResult()` idiom (both calls
            // parameterless) so we never fire on an unrelated `GetResult(x)`. The task
            // receiver is what `.GetAwaiter()` is called on.
            if (invocation.ArgumentList.Arguments.Count != 0 ||
                memberAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation ||
                getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess ||
                getAwaiterAccess.Name.Identifier.Text != "GetAwaiter" ||
                getAwaiterInvocation.ArgumentList.Arguments.Count != 0)
            {
                return;
            }

            taskReceiver = getAwaiterAccess.Expression;
            blockingForm = ".GetAwaiter().GetResult()";
            // The GetAwaiter receiver may be a `ConfigureAwait(...)` wrapper over a Task/
            // ValueTask; GetResult() still blocks, so accept that wrapper here too.
            allowConfiguredAwaitable = true;
        }
        else
        {
            return;
        }

        var receiverType = context.SemanticModel.GetTypeInfo(taskReceiver).Type;
        var isBlockingReceiver = allowConfiguredAwaitable
            ? known.IsBlockingAwaiterReceiver(receiverType)
            : known.IsTaskLike(receiverType);
        if (!isBlockingReceiver)
            return;

        var flag = ClassifyContext(context.SemanticModel, invocation);
        if (flag == FlagContext.None)
            return;

        Report(context, invocation, blockingForm, flag);
    }

    /// <summary>
    /// Handles the null-conditional forms — <c>task?.Result</c>, <c>task?.Wait()</c>,
    /// and <c>task?.GetAwaiter().GetResult()</c> — where the blocking member binds
    /// directly to the null-conditioned receiver. These are <c>ConditionalAccessExpression</c>
    /// nodes (the member is a <c>MemberBindingExpression</c>, not a <c>MemberAccessExpression</c>),
    /// so the two handlers above never see them.
    /// </summary>
    private static void AnalyzeConditionalAccess(SyntaxNodeAnalysisContext context, TaskTypes known)
    {
        var conditional = (ConditionalAccessExpressionSyntax)context.Node;

        var blockingForm = ClassifyConditionalWhenNotNull(conditional.WhenNotNull);
        if (blockingForm is null)
            return;

        // The receiver whose type must be Task-like is the null-conditioned expression.
        if (!known.IsTaskLike(context.SemanticModel.GetTypeInfo(conditional.Expression).Type))
            return;

        var flag = ClassifyContext(context.SemanticModel, conditional);
        if (flag == FlagContext.None)
            return;

        Report(context, conditional, blockingForm, flag);
    }

    /// <summary>
    /// Returns the blocking-form label when a conditional-access continuation is a
    /// blocking member bound directly to the receiver, else <c>null</c>. Only the direct
    /// binding is matched — a longer chain like <c>x?.Foo.Result</c> is left to the
    /// non-conditional handlers on its inner nodes.
    /// </summary>
    private static string? ClassifyConditionalWhenNotNull(ExpressionSyntax whenNotNull) =>
        whenNotNull switch
        {
            // x?.Result
            MemberBindingExpressionSyntax { Name.Identifier.Text: "Result" } => ".Result",

            // x?.Wait()  (zero-arg only — timeout overloads are out of scope)
            InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax { Name.Identifier.Text: "Wait" },
                ArgumentList.Arguments.Count: 0,
            } => ".Wait()",

            // x?.GetAwaiter().GetResult()
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "GetResult",
                    Expression: InvocationExpressionSyntax
                    {
                        Expression: MemberBindingExpressionSyntax { Name.Identifier.Text: "GetAwaiter" },
                        ArgumentList.Arguments.Count: 0,
                    },
                },
                ArgumentList.Arguments.Count: 0,
            } => ".GetAwaiter().GetResult()",

            _ => null,
        };

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode location, string blockingForm, FlagContext flag)
    {
        var contextLabel = flag == FlagContext.Effect ? "a UseEffect effect" : "Render()";
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            location.GetLocation(),
            blockingForm,
            contextLabel));
    }

    /// <summary>
    /// Walks up from a blocking expression to decide whether it runs inside a component
    /// <c>Render()</c> override or a <c>UseEffect</c> effect lambda. The first execution
    /// boundary (lambda, local function, or method declaration) is decisive:
    /// <list type="bullet">
    /// <item>a <c>UseEffect</c> effect lambda → <see cref="FlagContext.Effect"/>;</item>
    /// <item>any other lambda or local function → <see cref="FlagContext.None"/>;</item>
    /// <item>a <c>Render()</c> override reached with no intervening lambda →
    /// <see cref="FlagContext.Render"/>.</item>
    /// </list>
    /// <para>
    /// Treating <b>every</b> non-effect nested function as a boundary (rather than only
    /// <c>Task.Run</c>) is deliberate. It is what excludes the spec's named case —
    /// <c>Task.Run(() =&gt; t.Result)</c> — but it also covers the other background-dispatch
    /// forms (<c>Task.Factory.StartNew</c>, <c>ThreadPool.QueueUserWorkItem</c>) and every
    /// deferred callback whose execution timing is decoupled from render (event handlers,
    /// LINQ projections, stored delegates). A syntactic analyzer cannot prove whether such a
    /// lambda runs synchronously during render or later on another thread, so blocking inside
    /// one is left unflagged to keep false positives near zero — the accepted cost is a false
    /// negative on a helper that <i>is</i> invoked synchronously in the render path. This
    /// mirrors the deferred-execution-boundary convention in <c>HookRulesAnalyzer</c>.
    /// </para>
    /// </summary>
    private static FlagContext ClassifyContext(SemanticModel model, SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case SimpleLambdaExpressionSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    return IsUseEffectEffectLambda(model, current) ? FlagContext.Effect : FlagContext.None;

                case LocalFunctionStatementSyntax:
                    return FlagContext.None;

                case MethodDeclarationSyntax method:
                    return IsRenderOverride(model, method) ? FlagContext.Render : FlagContext.None;

                // Any other member body (property/indexer/accessor/ctor/operator/field
                // initializer) is not a render/effect context.
                case AccessorDeclarationSyntax:
                case PropertyDeclarationSyntax:
                case IndexerDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case DestructorDeclarationSyntax:
                case OperatorDeclarationSyntax:
                case ConversionOperatorDeclarationSyntax:
                case BaseTypeDeclarationSyntax:
                    return FlagContext.None;
            }
        }

        return FlagContext.None;
    }

    /// <summary>
    /// True when <paramref name="lambda"/> is the effect argument of a <c>UseEffect</c>
    /// call bound to <c>RenderContext.UseEffect</c> or a <c>Component</c> wrapper. The
    /// effect is parameter 0 of every overload, but named-argument reordering
    /// (<c>UseEffect(dependencies: d, effect: () =&gt; ...)</c>) means it is not always the
    /// first syntactic argument. Mirrors the Component-or-RenderContext anchoring the
    /// shipped hook analyzer uses.
    /// </summary>
    private static bool IsUseEffectEffectLambda(SemanticModel model, SyntaxNode lambda)
    {
        if (lambda.Parent is not ArgumentSyntax argument ||
            argument.Parent is not ArgumentListSyntax argumentList ||
            argumentList.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        // Cheap name gate before touching the semantic model.
        if (GetInvokedSimpleName(invocation) != "UseEffect")
            return false;

        if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol symbol)
        {
            if (symbol.Name != "UseEffect")
                return false;

            // Confirm the lambda binds to the effect parameter (index 0), honoring
            // named-argument reordering.
            if (!TargetsFirstParameter(argument, argumentList, symbol))
                return false;

            if (symbol.ContainingType is INamedTypeSymbol containing &&
                (IsOrDerivesFrom(containing, ComponentType) || IsOrDerivesFrom(containing, RenderContextType)))
            {
                return true;
            }

            if (symbol.IsExtensionMethod && symbol.ReceiverType is INamedTypeSymbol receiver &&
                (IsOrDerivesFrom(receiver, RenderContextType) || IsOrDerivesFrom(receiver, ComponentType)))
            {
                return true;
            }

            return false;
        }

        // Fallback for an unresolved/overload-ambiguous call (incremental/errored compile):
        // the effect is the first positional argument, and the call is either a
        // `receiver.UseEffect(...)` whose receiver derives from RenderContext/Component, or a
        // bare implicit-receiver `UseEffect(...)` inside a Component-derived class. Mirrors
        // HookRulesAnalyzer.IsLikelyReactorHook so the rule stays useful mid-edit.
        if (argumentList.Arguments.IndexOf(argument) != 0)
            return false;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            model.GetTypeInfo(memberAccess.Expression).Type is INamedTypeSymbol receiverType &&
            (IsOrDerivesFrom(receiverType, RenderContextType) || IsOrDerivesFrom(receiverType, ComponentType)))
        {
            return true;
        }

        // Implicit receiver (`UseEffect(...)` inside a Component) — the symbol may be null
        // while the build is mid-type-binding; anchor on the enclosing class instead.
        if (invocation.Expression is IdentifierNameSyntax &&
            invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>() is { } enclosingClass &&
            model.GetDeclaredSymbol(enclosingClass) is INamedTypeSymbol classSymbol &&
            IsOrDerivesFrom(classSymbol, ComponentType))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="argument"/> binds to parameter index 0 of
    /// <paramref name="symbol"/> — the first positional argument, or a named argument
    /// whose name matches parameter 0. Handles named-argument reordering.
    /// </summary>
    private static bool TargetsFirstParameter(
        ArgumentSyntax argument, ArgumentListSyntax argumentList, IMethodSymbol symbol)
    {
        if (argument.NameColon is { Name.Identifier.Text: var argName })
        {
            return symbol.Parameters.Length > 0
                && string.Equals(argName, symbol.Parameters[0].Name, System.StringComparison.Ordinal);
        }

        return argumentList.Arguments.IndexOf(argument) == 0;
    }

    /// <summary>
    /// True when <paramref name="method"/> is a <c>Render()</c> override on a
    /// <c>Component</c>-derived type. Falls back to the syntactic <c>override</c> modifier
    /// when the symbol cannot be resolved (incremental compile).
    /// </summary>
    private static bool IsRenderOverride(SemanticModel model, MethodDeclarationSyntax method)
    {
        if (method.Identifier.Text != "Render")
            return false;

        if (model.GetDeclaredSymbol(method) is IMethodSymbol symbol)
        {
            return symbol.IsOverride && IsOrDerivesFrom(symbol.ContainingType, ComponentType);
        }

        // Fallback: the method symbol is unresolved (mid-edit). Require the `override`
        // modifier, and still anchor on the enclosing class — a Component is a class, so a
        // non-class parent or a resolvable non-Component class rules the override out. Only
        // when even the class symbol is unavailable do we keep the loose name heuristic.
        if (!method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            return false;

        if (method.Parent is not ClassDeclarationSyntax enclosingClass)
            return false;

        if (model.GetDeclaredSymbol(enclosingClass) is INamedTypeSymbol classSymbol)
            return IsOrDerivesFrom(classSymbol, ComponentType);

        return true;
    }

    private static string? GetInvokedSimpleName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            _ => null,
        };

    private static bool IsOrDerivesFrom(INamedTypeSymbol? type, string fullyQualifiedName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var name = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "");
            if (name == fullyQualifiedName)
                return true;
            // Accept generic forms: Component<T> derives from Component.
            if (name.StartsWith(fullyQualifiedName + "<", System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Well-known blocking receiver types, resolved once per compilation.</summary>
    private readonly struct TaskTypes
    {
        private readonly INamedTypeSymbol? _task;
        private readonly INamedTypeSymbol? _taskOfT;
        private readonly INamedTypeSymbol? _valueTask;
        private readonly INamedTypeSymbol? _valueTaskOfT;
        private readonly INamedTypeSymbol? _configuredTaskAwaitable;
        private readonly INamedTypeSymbol? _configuredTaskAwaitableOfT;
        private readonly INamedTypeSymbol? _configuredValueTaskAwaitable;
        private readonly INamedTypeSymbol? _configuredValueTaskAwaitableOfT;

        private TaskTypes(
            INamedTypeSymbol? task,
            INamedTypeSymbol? taskOfT,
            INamedTypeSymbol? valueTask,
            INamedTypeSymbol? valueTaskOfT,
            INamedTypeSymbol? configuredTaskAwaitable,
            INamedTypeSymbol? configuredTaskAwaitableOfT,
            INamedTypeSymbol? configuredValueTaskAwaitable,
            INamedTypeSymbol? configuredValueTaskAwaitableOfT)
        {
            _task = task;
            _taskOfT = taskOfT;
            _valueTask = valueTask;
            _valueTaskOfT = valueTaskOfT;
            _configuredTaskAwaitable = configuredTaskAwaitable;
            _configuredTaskAwaitableOfT = configuredTaskAwaitableOfT;
            _configuredValueTaskAwaitable = configuredValueTaskAwaitable;
            _configuredValueTaskAwaitableOfT = configuredValueTaskAwaitableOfT;
        }

        public bool Any => _task is not null || _taskOfT is not null
            || _valueTask is not null || _valueTaskOfT is not null;

        public static TaskTypes Resolve(Compilation compilation) => new(
            compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
            compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"),
            compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask"),
            compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"),
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredTaskAwaitable"),
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1"),
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable"),
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1"));

        /// <summary>
        /// True when <paramref name="type"/> is (or, for <c>Task</c>, derives from)
        /// one of the four awaitable task types.
        /// </summary>
        public bool IsTaskLike(ITypeSymbol? type)
        {
            if (type is null)
                return false;

            var definition = type.OriginalDefinition;
            if (Matches(definition, _task) || Matches(definition, _taskOfT)
                || Matches(definition, _valueTask) || Matches(definition, _valueTaskOfT))
            {
                return true;
            }

            // Custom Task subclasses (rare). ValueTask is a sealed struct — no subclassing.
            for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                var baseDefinition = baseType.OriginalDefinition;
                if (Matches(baseDefinition, _task) || Matches(baseDefinition, _taskOfT))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="type"/> is a valid receiver for the blocking
        /// <c>.GetAwaiter().GetResult()</c> form: a <c>Task</c>/<c>ValueTask</c>, OR a
        /// <c>ConfigureAwait(...)</c> wrapper over one
        /// (<c>ConfiguredTaskAwaitable[&lt;T&gt;]</c> / <c>ConfiguredValueTaskAwaitable[&lt;T&gt;]</c>).
        /// <c>ConfigureAwait</c> only affects continuation scheduling — <c>GetResult()</c>
        /// still blocks the calling thread — and these awaitable types are produced solely by
        /// <c>Task</c>/<c>ValueTask.ConfigureAwait</c>, so treating them as blocking is FP-free.
        /// </summary>
        public bool IsBlockingAwaiterReceiver(ITypeSymbol? type)
        {
            if (IsTaskLike(type))
                return true;

            if (type is null)
                return false;

            var definition = type.OriginalDefinition;
            return Matches(definition, _configuredTaskAwaitable)
                || Matches(definition, _configuredTaskAwaitableOfT)
                || Matches(definition, _configuredValueTaskAwaitable)
                || Matches(definition, _configuredValueTaskAwaitableOfT);
        }

        private static bool Matches(ITypeSymbol candidate, INamedTypeSymbol? wellKnown) =>
            wellKnown is not null && SymbolEqualityComparer.Default.Equals(candidate, wellKnown);
    }
}
