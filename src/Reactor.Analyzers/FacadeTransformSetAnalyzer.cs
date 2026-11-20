using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_ANIM_004: a facade transform is written through <c>.Set(...)</c> on an element
/// whose fluent chain also uses a modifier that takes the composition visual (spec 066 §9.1).
/// </summary>
/// <remarks>
/// <para>
/// Once <c>ElementCompositionPreview.GetElementVisual</c> has handed out an element's visual,
/// WinUI rejects writes to the <c>UIElement</c> transform facades. A <c>.Set</c> setter re-runs
/// on every update, so a facade write there throws after the handoff. <c>RotationAxis</c>,
/// <c>TransformMatrix</c> and the XAML transition properties also block the handoff itself on
/// mount (#1613, #1623). The trigger set is <see cref="ModifierTable.VisualOwningModifiers"/>.
/// </para>
/// <para>
/// <c>UIElement.Translation</c> is different: it stays writable after the handoff, so it is only
/// reported when the chain declares <c>.Translation(...)</c>, which overrides the facade write
/// (#1642). That arm has its own message and no code fix.
/// </para>
/// <para>
/// Matching is limited to what the <c>.Set</c> chain shows. A visual-owning modifier on a
/// wrapper or on a chain split across variables, facade writes through
/// <c>ElementRef.Current</c> or event handlers, and <c>.OnMount</c> callbacks are accepted
/// false negatives.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FacadeTransformSetAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_ANIM_004";

    /// <summary>
    /// Diagnostic-property key carrying the modifier the code fix rewrites to. Present only when
    /// the analyzer proved the rewrite mechanical and compilable.
    /// </summary>
    internal const string FixModifierKey = "ReactorFixModifier";

    private const string UIElementName = "UIElement";
    private const string XamlNamespace = "Microsoft.UI.Xaml";
    private const string TranslationName = "Translation";
    private const string StartAnimationName = "StartAnimation";

    /// <summary>How a facade write maps onto its modifier's arguments.</summary>
    internal enum FixShape
    {
        /// <summary>No modifier exists; the diagnostic carries advice only.</summary>
        None,

        /// <summary>The right-hand side is the modifier's argument: <c>fe.Rotation = e</c> → <c>.Rotation(e)</c>.</summary>
        Value,

        /// <summary>
        /// <c>fe.OpacityTransition = new ScalarTransition { Duration = d }</c> → <c>.OpacityTransition(d)</c>.
        /// </summary>
        ScalarTransitionDuration,
    }

    internal readonly struct FacadeMember
    {
        public FacadeMember(string? modifier, string advice, FixShape shape)
        {
            Modifier = modifier;
            Advice = advice;
            Shape = shape;
        }

        /// <summary>The Reactor modifier to rewrite to, or null when there is none.</summary>
        public string? Modifier { get; }

        public string Advice { get; }

        public FixShape Shape { get; }
    }

    private const string NoModifierAdvice =
        "No modifier covers it: animate it with '.Keyframes(...)' or '.Animate(...)', or take the visual with ElementCompositionPreview.GetElementVisual in '.OnMount(...)'";

    /// <summary>
    /// <c>UIElement</c> members the rule matches, other than <c>Translation</c>, keyed by member
    /// name. <c>StopAnimation</c> is deliberately absent: WinUI exempts it from facade
    /// strictness, so cleanup such as <c>.Set(b =&gt; b.StopAnimation(a)).LayoutAnimation()</c>
    /// is valid.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, FacadeMember> FacadeMembers =
        new Dictionary<string, FacadeMember>(System.StringComparer.Ordinal)
        {
            ["Rotation"] = new("Rotation", "Use '.Rotation(degrees)' instead", FixShape.Value),
            ["Scale"] = new("Scale", "Use '.Scale(...)' instead", FixShape.Value),
            ["CenterPoint"] = new("CenterPoint", "Use '.CenterPoint(...)' instead", FixShape.Value),
            ["OpacityTransition"] = new("OpacityTransition", "Use '.OpacityTransition(duration)' instead", FixShape.ScalarTransitionDuration),
            ["RotationTransition"] = new("RotationTransition", "Use '.RotationTransition(duration)' instead", FixShape.ScalarTransitionDuration),
            ["ScaleTransition"] = new("ScaleTransition", "Use '.ScaleTransition(...)' instead", FixShape.Value),
            ["TranslationTransition"] = new("TranslationTransition", "Use '.TranslationTransition(...)' instead", FixShape.Value),
            ["RotationAxis"] = new(null, NoModifierAdvice, FixShape.None),
            ["TransformMatrix"] = new(null, NoModifierAdvice, FixShape.None),
            [StartAnimationName] = new(null, NoModifierAdvice, FixShape.None),
        };

    private static readonly LocalizableString Title =
        "Facade transform written through .Set on an element that uses the composition visual";

    private static readonly LocalizableString MessageFormat =
        "'.Set(...)' {0}, but '.{1}(...)' on the same element takes its composition visual, after which WinUI rejects that facade write. {2}.";

    private static readonly LocalizableString Description =
        "Once an element's composition visual has been taken, WinUI throws on writes to the UIElement " +
        "transform facades (Rotation, Scale, CenterPoint, RotationAxis, TransformMatrix, the XAML " +
        "*Transition properties) and on UIElement.StartAnimation. A .Set setter re-runs on every update, " +
        "so the write throws after the handoff, and RotationAxis, TransformMatrix and the transitions also " +
        "block the handoff on mount. Use the matching Reactor modifier, which writes the visual.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Animation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    private static readonly LocalizableString TranslationTitle =
        "UIElement.Translation written through .Set is ignored next to .Translation(...)";

    private static readonly LocalizableString TranslationMessageFormat =
        "'.Set(...)' writes 'UIElement.Translation', which is ignored because '.Translation(...)' owns translation on this element. Remove the write or merge the value into the existing '.Translation(...)' call.";

    private static readonly LocalizableString TranslationDescription =
        "The '.Translation(...)' modifier owns the element's translation: Reactor applies it after .Set " +
        "setters, and once it has enabled translation on the composition visual WinUI silently ignores " +
        "facade Translation writes (#1642). Adding another '.Translation(...)' would not help, because " +
        "the modifier merges last-wins, so the value has to be merged by hand.";

    private static readonly DiagnosticDescriptor TranslationRule = new(
        DiagnosticId,
        TranslationTitle,
        TranslationMessageFormat,
        "Reactor.Animation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: TranslationDescription);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, TranslationRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!SetLambdaHelpers.IsSetInvocation(invocation, out var setAccess))
            return;

        var lambda = invocation.ArgumentList.Arguments[0].Expression;
        var parameter = SetLambdaHelpers.GetSingleLambdaParameter(lambda);
        if (parameter is null)
            return;
        var paramName = parameter.Identifier.Text;

        // Syntactic candidates first: writes whose target is the lambda's own parameter.
        var candidates = new List<(SyntaxNode Node, ExpressionSyntax Member, string Name, AssignmentExpressionSyntax? Assignment)>();
        foreach (var assignment in SetLambdaHelpers.GetLambdaAssignments(lambda))
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                continue;
            var left = SetLambdaHelpers.GetAssignedMemberAccess(assignment, paramName);
            if (left is null)
                continue;
            var name = left.Name.Identifier.Text;
            if (name == StartAnimationName || (name != TranslationName && !FacadeMembers.ContainsKey(name)))
                continue;
            candidates.Add((assignment, left, name, assignment));
        }

        foreach (var call in SetLambdaHelpers.GetLambdaInvocations(lambda))
        {
            if (call.Expression is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax target,
                    Name.Identifier.Text: StartAnimationName,
                } access
                && target.Identifier.Text == paramName)
            {
                candidates.Add((call, access, StartAnimationName, null));
            }
        }

        if (candidates.Count == 0)
            return;

        var model = context.SemanticModel;
        var ct = context.CancellationToken;

        var owningModifier = FindChainModifier(invocation, model, ct, ModifierTable.VisualOwningModifiers.ContainsKey);
        var hasTranslationModifier = FindChainModifier(invocation, model, ct, static n => n == TranslationName) is not null;
        if (owningModifier is null && !hasTranslationModifier)
            return;

        if (!SetLambdaHelpers.IsReactorSetInvocation(invocation, model, ct))
            return;

        foreach (var (node, member, name, assignment) in candidates)
        {
            if (!IsUIElementMember(model.GetSymbolInfo(member, ct).Symbol))
                continue;

            if (name == TranslationName)
            {
                if (hasTranslationModifier)
                    context.ReportDiagnostic(Diagnostic.Create(TranslationRule, node.GetLocation()));
                continue;
            }

            if (owningModifier is null)
                continue;

            var info = FacadeMembers[name];
            var properties = ImmutableDictionary<string, string?>.Empty;
            if (assignment is not null
                && info.Modifier is { } modifier
                && IsFixable(invocation, setAccess, lambda, assignment, info, model, ct))
            {
                properties = properties.Add(FixModifierKey, modifier);
            }

            var what = assignment is null
                ? "calls 'UIElement." + name + "'"
                : "writes 'UIElement." + name + "'";

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                node.GetLocation(),
                properties,
                what,
                owningModifier,
                info.Advice));
        }
    }

    /// <summary>
    /// The first modifier on the <c>.Set</c>'s fluent chain, in either direction, whose name
    /// passes <paramref name="isMatch"/> and which binds to a Reactor extension method.
    /// </summary>
    private static string? FindChainModifier(
        InvocationExpressionSyntax setInvocation,
        SemanticModel model,
        CancellationToken ct,
        System.Func<string, bool> isMatch)
    {
        for (var node = DuplicateAtomicModifierAnalyzer.GetReceiverInvocation(setInvocation);
             node is not null;
             node = DuplicateAtomicModifierAnalyzer.GetReceiverInvocation(node))
        {
            if (Matches(node))
                return DuplicateAtomicModifierAnalyzer.GetFluentMethodName(node);
        }

        // Outward: the .Set is the receiver of the next link.
        SyntaxNode current = setInvocation;
        while (current.Parent is MemberAccessExpressionSyntax access
               && access.Expression == current
               && access.Parent is InvocationExpressionSyntax outer)
        {
            if (Matches(outer))
                return DuplicateAtomicModifierAnalyzer.GetFluentMethodName(outer);
            current = outer;
        }

        return null;

        bool Matches(InvocationExpressionSyntax link) =>
            DuplicateAtomicModifierAnalyzer.GetFluentMethodName(link) is { } name
            && isMatch(name)
            && IsReactorExtension(model.GetSymbolInfo(link, ct).Symbol);
    }

    private static bool IsReactorExtension(ISymbol? symbol)
    {
        if (symbol is not IMethodSymbol { ReducedFrom: { } extension })
            return false;
        var ns = extension.ContainingNamespace?.ToDisplayString();
        return ns is not null
            && (ns == "Microsoft.UI.Reactor" || ns.StartsWith("Microsoft.UI.Reactor.", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// True when the member is declared on <c>Microsoft.UI.Xaml.UIElement</c>, so a user type
    /// with its own <c>Rotation</c> does not match.
    /// </summary>
    private static bool IsUIElementMember(ISymbol? symbol)
    {
        var containing = symbol?.OriginalDefinition.ContainingType;
        return containing is not null
            && containing.Name == UIElementName
            && containing.ContainingNamespace?.ToDisplayString() == XamlNamespace;
    }

    /// <summary>
    /// True when the <c>.Set</c> can be rewritten to the modifier without changing anything but
    /// the channel the value is written to.
    /// </summary>
    private static bool IsFixable(
        InvocationExpressionSyntax setInvocation,
        MemberAccessExpressionSyntax setAccess,
        ExpressionSyntax lambda,
        AssignmentExpressionSyntax assignment,
        FacadeMember info,
        SemanticModel model,
        CancellationToken ct)
    {
        // The assignment must be the lambda's only statement, or the rewrite drops the rest.
        if (SetLambdaHelpers.TryGetLambdaAssignment(lambda) != assignment)
            return false;

        // The rewrite deletes the whole invocation; a directive inside it would be unbalanced.
        if (setInvocation.ContainsDirectives)
            return false;

        // A null write clears the property, but the transition modifiers treat null as "use the
        // default transition", so the rewrite would turn a clear into an enable.
        if (PoolResetSetAnalyzer.IsNullOrDefault(assignment.Right))
            return false;
        if (info.Shape == FixShape.Value
            && model.GetTypeInfo(assignment.Right, ct).Nullability.FlowState == NullableFlowState.MaybeNull)
        {
            return false;
        }

        if (info.Shape == FixShape.ScalarTransitionDuration && !IsScalarTransition(assignment.Right, model, ct))
            return false;

        // Modifiers run after setters, so an earlier same-name modifier currently wins. Moving
        // the value into a later modifier call would make it win instead.
        if (ReceiverCalls(setAccess.Expression, info.Modifier!))
            return false;

        var arguments = BuildModifierArguments(assignment, info.Shape);
        if (arguments is null)
            return false;

        // Bind the rewrite where the .Set stands. This rejects overload ambiguity (target-typed
        // 'new(...)' against .Scale's float and Vector3 overloads), type mismatches, and a
        // receiver type without the modifier.
        var rewritten = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                setAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName(info.Modifier!)),
            arguments);
        var bound = model.GetSpeculativeSymbolInfo(
            setInvocation.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression).Symbol;
        return bound is IMethodSymbol method && method.Name == info.Modifier && IsReactorExtension(method);
    }

    private static bool IsScalarTransition(ExpressionSyntax value, SemanticModel model, CancellationToken ct)
    {
        var type = model.GetTypeInfo(value, ct).Type;
        return type is not null
            && type.Name == "ScalarTransition"
            && type.ContainingNamespace?.ToDisplayString() == XamlNamespace;
    }

    private static bool ReceiverCalls(ExpressionSyntax receiver, string modifier)
    {
        for (var node = receiver as InvocationExpressionSyntax;
             node is not null;
             node = DuplicateAtomicModifierAnalyzer.GetReceiverInvocation(node))
        {
            if (DuplicateAtomicModifierAnalyzer.GetFluentMethodName(node) == modifier)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The modifier's argument list for a fixable write, or null when the right-hand side has no
    /// mechanical mapping. Shared with <see cref="FacadeTransformSetCodeFix"/>.
    /// </summary>
    internal static ArgumentListSyntax? BuildModifierArguments(AssignmentExpressionSyntax assignment, FixShape shape)
    {
        switch (shape)
        {
            case FixShape.Value:
                return SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(assignment.Right.WithoutTrivia())));

            case FixShape.ScalarTransitionDuration:
                // Only 'new ScalarTransition()' / 'new()' with no constructor arguments and an
                // initializer that sets nothing but Duration. Anything else is left to the author.
                if (assignment.Right is not BaseObjectCreationExpressionSyntax creation
                    || creation.ArgumentList is { Arguments.Count: > 0 })
                {
                    return null;
                }

                var initializer = creation.Initializer;
                if (initializer is null || initializer.Expressions.Count == 0)
                    return SyntaxFactory.ArgumentList();

                if (initializer.Expressions.Count != 1
                    || initializer.Expressions[0] is not AssignmentExpressionSyntax
                    {
                        Left: IdentifierNameSyntax { Identifier.Text: "Duration" },
                    } duration
                    || !duration.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    return null;
                }

                return SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(duration.Right.WithoutTrivia())));

            default:
                return null;
        }
    }
}
