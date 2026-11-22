using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// mount (#1613, #1623). The trigger set is <see cref="ModifierTable.VisualOwningModifiers"/>. A
/// transform modifier with a constant identity value (<c>.Scale(1)</c>, <c>.Rotation(0)</c>) doesn't
/// take the visual and doesn't count, unless the <c>.Set</c> writes that same facade: the identity
/// write then takes the visual to override the setter (spec 066 §3 invariant 2).
/// </para>
/// <para>
/// <c>UIElement.Translation</c> is different: it stays writable after the handoff, so it is only
/// reported when the chain declares <c>.Translation(...)</c>, which conflicts with the facade
/// write and makes WinUI ignore it once translation is enabled on the visual (#1642). That arm
/// has its own message and no code fix.
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

    /// <summary>
    /// Elements whose <c>.Set</c> configures a control other than the one they mount
    /// (<c>OverlayLifecycle.MountPopup</c> wraps the popup in a panel,
    /// <c>MountContentDialog</c> returns a placeholder). Modifiers on them take the mounted
    /// control's visual, so a facade write through <c>.Set</c> never meets it.
    /// </summary>
    private static readonly ImmutableHashSet<string> SplitSetterTargetElements =
        ImmutableHashSet.Create(System.StringComparer.Ordinal, "PopupElement", "ContentDialogElement");

    internal readonly struct FacadeMember
    {
        public FacadeMember(string? modifier, string advice)
        {
            Modifier = modifier;
            Advice = advice;
        }

        /// <summary>
        /// The Reactor modifier the code fix rewrites to (<c>fe.Rotation = e</c> →
        /// <c>.Rotation(e)</c>), or null when no modifier works on <c>main</c> today.
        /// </summary>
        public string? Modifier { get; }

        public string Advice { get; }
    }

    private const string NoModifierAdvice =
        "No modifier covers it, so animate it with '.Keyframes(...)' or '.Animate(...)', or take the visual with ElementCompositionPreview.GetElementVisual in '.OnMount(...)'";

    /// <summary>
    /// <c>UIElement</c> members the rule matches, other than <c>Translation</c>, keyed by member
    /// name. <c>StopAnimation</c> is deliberately absent: WinUI exempts it from facade
    /// strictness, so cleanup such as <c>.Set(b =&gt; b.StopAnimation(a)).LayoutAnimation()</c>
    /// is valid. Spec 066 layer 2 runs the <c>.XxxTransition()</c> modifiers on the visual, so
    /// they are the advice for the matching XAML property.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, FacadeMember> FacadeMembers =
        new Dictionary<string, FacadeMember>(System.StringComparer.Ordinal)
        {
            ["Rotation"] = new("Rotation", "Use '.Rotation(degrees)' instead"),
            ["Scale"] = new("Scale", "Use '.Scale(...)' instead"),
            ["CenterPoint"] = new("CenterPoint", "Use '.CenterPoint(...)' instead"),
            ["OpacityTransition"] = new("OpacityTransition", "Use '.OpacityTransition(duration)' instead"),
            ["RotationTransition"] = new("RotationTransition", "Use '.RotationTransition(duration)' instead"),
            ["ScaleTransition"] = new("ScaleTransition", "Use '.ScaleTransition(...)' instead"),
            ["TranslationTransition"] = new("TranslationTransition", "Use '.TranslationTransition(...)' instead"),
            ["RotationAxis"] = new(null, NoModifierAdvice),
            ["TransformMatrix"] = new(null, NoModifierAdvice),
            [StartAnimationName] = new(null,
                "Start the animation on the visual instead, taken with ElementCompositionPreview.GetElementVisual in '.OnMount(...)', or use '.Keyframes(...)' or '.Animate(...)'"),
        };

    private static readonly LocalizableString Title =
        "Facade transform written through .Set on an element that uses the composition visual";

    private static readonly LocalizableString MessageFormat =
        "'.Set(...)' {0}, but '.{1}(...)' on the same element takes its composition visual, after which WinUI rejects the {2}. {3}.";

    private static readonly LocalizableString Description =
        "Once an element's composition visual has been taken, WinUI throws on writes to the UIElement " +
        "transform facades (Rotation, Scale, CenterPoint, RotationAxis, TransformMatrix, the XAML " +
        "*Transition properties) and on UIElement.StartAnimation. A .Set setter re-runs on every update, " +
        "so the write throws after the handoff, and RotationAxis, TransformMatrix and the transitions also " +
        "block the handoff on mount. Use the matching Reactor modifier where one exists, which writes the visual.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Animation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    private static readonly LocalizableString TranslationTitle =
        "UIElement.Translation written through .Set conflicts with .Translation(...)";

    private static readonly LocalizableString TranslationMessageFormat =
        "'.Set(...)' writes 'UIElement.Translation', which conflicts with '.Translation(...)' on this element. The modifier overrides it on mount, and WinUI ignores it once translation is enabled on the composition visual. Remove the write or merge the value into the existing '.Translation(...)' call.";

    private static readonly LocalizableString TranslationDescription =
        "The '.Translation(...)' modifier and a .Set write fight over the same translation. Reactor applies " +
        "modifiers after .Set setters, so the modifier wins on mount, while the setter re-runs on every update " +
        "and wins whenever the modifier's value is unchanged. Once the modifier has enabled translation on the " +
        "composition visual, WinUI silently ignores facade Translation writes (#1642). Adding another " +
        "'.Translation(...)' would not help, because the modifier merges last-wins, so the value has to be " +
        "merged by hand.";

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

        // Syntactic candidates first: writes whose target is the lambda's own parameter. Compound
        // writes ('fe.Rotation += 5') are reported too, since they write the facade just the same;
        // IsFixable only rewrites a simple '='.
        var candidates = new List<(SyntaxNode Node, ExpressionSyntax Member, string Name, AssignmentExpressionSyntax? Assignment)>();
        var writes = SetLambdaHelpers.GetLambdaAssignments(lambda)
            .Select(assignment => (Assignment: assignment, Left: SetLambdaHelpers.GetAssignedMemberAccess(assignment, paramName)))
            .Where(write => write.Left is { } left && IsAssignedMember(left.Name.Identifier.Text));
        foreach (var (assignment, left) in writes)
            candidates.Add((assignment, left!, left!.Name.Identifier.Text, assignment));

        var startAnimationCalls = SetLambdaHelpers.GetLambdaInvocations(lambda)
            .Where(call => call.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax target,
                Name.Identifier.Text: StartAnimationName,
            } && target.Identifier.Text == paramName);
        foreach (var call in startAnimationCalls)
            candidates.Add((call, call.Expression, StartAnimationName, null));

        // 'fe.Rotation++' writes the facade too, but it is not an assignment.
        var increments = TopLevelExpressions(lambda)
            .Select(expression => expression switch
            {
                PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression) => (Node: expression, Operand: postfix.Operand),
                PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression) => (Node: expression, Operand: prefix.Operand),
                _ => (Node: expression, Operand: (ExpressionSyntax?)null),
            })
            .Where(write => write.Operand is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target } access
                && target.Identifier.Text == paramName
                && IsAssignedMember(access.Name.Identifier.Text));
        foreach (var (node, operand) in increments)
            candidates.Add((node, operand!, ((MemberAccessExpressionSyntax)operand!).Name.Identifier.Text, null));

        if (candidates.Count == 0)
            return;

        var model = context.SemanticModel;
        var ct = context.CancellationToken;

        // A transform modifier with a constant identity value doesn't take the visual (spec 066 §3
        // invariant 2), unless this .Set writes the same facade: the identity write then still
        // takes it, to override the setter's value.
        var written = new HashSet<string>(candidates.Select(static c => c.Name), System.StringComparer.Ordinal);
        var owningModifier = FindChainModifier(invocation, model, ct, ModifierTable.VisualOwningModifiers.ContainsKey,
            skip: link => DuplicateAtomicModifierAnalyzer.GetFluentMethodName(link) is { } name
                && !written.Contains(name) && IsIdentityTransform(link, name, model, ct));
        var hasTranslationModifier = FindChainModifier(invocation, model, ct, static n => n == TranslationName) is not null;
        if (owningModifier is null && !hasTranslationModifier)
            return;

        if (!SetLambdaHelpers.IsReactorSetInvocation(invocation, model, ct))
            return;

        // Overlay elements mount a wrapper (Popup) or a placeholder (ContentDialog) and run their
        // setters on the inner control, so the visual the modifiers take is not the one written.
        if (SetLambdaHelpers.IsCuratedReactorElement(model.GetTypeInfo(setAccess.Expression, ct).Type, SplitSetterTargetElements))
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
                && IsFixable(invocation, setAccess, lambda, paramName, assignment, modifier, model, ct))
            {
                properties = properties.Add(FixModifierKey, modifier);
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                node.GetLocation(),
                properties,
                (name == StartAnimationName ? "calls 'UIElement." : "writes 'UIElement.") + name + "'",
                owningModifier,
                name == StartAnimationName ? "call" : "write",
                info.Modifier is { } transitionModifier && name.EndsWith("Transition", System.StringComparison.Ordinal)
                    && assignment is not null && IsNullValue(assignment.Right)
                    ? "Remove it: an element without '." + transitionModifier + "(...)' has no such transition, so there is nothing to clear"
                    : info.Advice));
        }
    }
    /// <summary>
    /// The first modifier on the <c>.Set</c>'s fluent chain, in either direction, whose name
    /// passes <paramref name="isMatch"/> and, unless <paramref name="requireReactorExtension"/> is false,
    /// binds to a Reactor extension method.
    /// </summary>
    private static string? FindChainModifier(
        InvocationExpressionSyntax setInvocation,
        SemanticModel model,
        CancellationToken ct,
        System.Func<string, bool> isMatch,
        bool requireReactorExtension = true,
        System.Func<InvocationExpressionSyntax, bool>? skip = null)
    {
        // The walk stays on links that return the .Set's own element type. A wrapper such as
        // .Semantics() returns a different element, and the modifiers past it belong to that
        // element's control, not to the one the .Set writes.
        var elementType = model.GetTypeInfo(setInvocation, ct).Type;
        if (elementType is null)
            return null;

        for (var node = DuplicateAtomicModifierAnalyzer.GetReceiverInvocation(setInvocation);
             node is not null && IsSameElement(node);
             node = DuplicateAtomicModifierAnalyzer.GetReceiverInvocation(node))
        {
            if (Matches(node))
                return DuplicateAtomicModifierAnalyzer.GetFluentMethodName(node);
        }

        // Outward: the .Set is the receiver of the next link.
        SyntaxNode current = setInvocation;
        while (current.Parent is MemberAccessExpressionSyntax access
               && access.Expression == current
               && access.Parent is InvocationExpressionSyntax outer
               && IsSameElement(outer))
        {
            if (Matches(outer))
                return DuplicateAtomicModifierAnalyzer.GetFluentMethodName(outer);
            current = outer;
        }

        return null;

        bool IsSameElement(InvocationExpressionSyntax link) =>
            SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(link, ct).Type, elementType);

        bool Matches(InvocationExpressionSyntax link) =>
            DuplicateAtomicModifierAnalyzer.GetFluentMethodName(link) is { } name
            && isMatch(name)
            && (!requireReactorExtension || IsReactorExtension(model.GetSymbolInfo(link, ct).Symbol))
            && (skip is null || !skip(link));
    }

    /// <summary>
    /// True when <paramref name="link"/> is <c>.Scale</c>, <c>.Rotation</c>, <c>.Translation</c> or
    /// <c>.CenterPoint</c> with only constant identity arguments: every number 1 for <c>.Scale</c>
    /// and 0 for the others, or <c>Vector3.One</c> / <c>Vector3.Zero</c>.
    /// </summary>
    private static bool IsIdentityTransform(InvocationExpressionSyntax link, string name, SemanticModel model, CancellationToken ct)
    {
        double identity;
        string vector;
        switch (name)
        {
            case "Scale": identity = 1; vector = "One"; break;
            case "Rotation" or TranslationName or "CenterPoint": identity = 0; vector = "Zero"; break;
            default: return false;
        }
        var arguments = link.ArgumentList.Arguments;
        if (arguments.Count == 0)
            return false;
        foreach (var argument in arguments)
        {
            var value = argument.Expression;
            if (model.GetConstantValue(value, ct) is { HasValue: true, Value: { } constant }
                && constant is int or long or float or double or decimal)
            {
                if (System.Convert.ToDouble(constant, System.Globalization.CultureInfo.InvariantCulture) != identity)
                    return false;
                continue;
            }
            if (model.GetSymbolInfo(value, ct).Symbol is { IsStatic: true } symbol
                && symbol is IPropertySymbol or IFieldSymbol
                && symbol.Name == vector
                && symbol.ContainingType is { Name: "Vector3" } type
                && type.ContainingNamespace?.ToDisplayString() == "System.Numerics")
                continue;
            return false;
        }
        return true;
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
    /// The lambda's expression body, or the expressions of its block's top-level expression
    /// statements, the same scope <see cref="SetLambdaHelpers.GetLambdaAssignments"/> uses.
    /// </summary>
    private static IEnumerable<ExpressionSyntax> TopLevelExpressions(ExpressionSyntax lambda) =>
        SetLambdaHelpers.GetLambdaBody(lambda) switch
        {
            ExpressionSyntax expression => new[] { expression },
            BlockSyntax block => block.Statements.OfType<ExpressionStatementSyntax>().Select(statement => statement.Expression),
            _ => System.Array.Empty<ExpressionSyntax>(),
        };

    /// <summary>A member name the rule matches as an assignment target.</summary>
    private static bool IsAssignedMember(string name) =>
        name != StartAnimationName && (name == TranslationName || FacadeMembers.ContainsKey(name));

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
        string paramName,
        AssignmentExpressionSyntax assignment,
        string modifier,
        SemanticModel model,
        CancellationToken ct)
    {
        // The assignment must be a plain '=' and the lambda's only statement, or the rewrite
        // drops the rest (or turns 'fe.Rotation += 5' into '.Rotation(5)').
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || SetLambdaHelpers.TryGetLambdaAssignment(lambda) != assignment)
        {
            return false;
        }

        // The values that move into the modifier call. A transition object the modifier builds
        // itself is dropped, and only its duration moves.
        if (BuildModifierArguments(assignment, modifier) is not { } arguments)
            return false;
        if (IsScalarTransitionModifier(modifier) && !IsXamlType(model.GetTypeInfo(assignment.Right, ct).Type, "ScalarTransition"))
            return false;
        // The Vector3 modifiers turn a null argument into a default transition, where the write
        // meant none. Only a value the compiler proves non-null moves, and a '!' proves nothing.
        if (modifier is "ScaleTransition" or "TranslationTransition"
            && assignment.Right is not BaseObjectCreationExpressionSyntax
            && (ContainsSuppression(assignment.Right)
                || model.GetTypeInfo(assignment.Right, ct).Nullability.FlowState != NullableFlowState.NotNull))
            return false;
        var moved = arguments.Arguments.Select(static argument => argument.Expression).ToList();

        // A setter runs while the reconciler applies the element; a modifier argument is evaluated
        // while the tree is built. Moving a call, an increment or a nested assignment changes when
        // (and how often) its side effects happen, so only side-effect-free values move.
        if (moved.Any(static value => value.DescendantNodesAndSelf().Any(static node =>
                node is InvocationExpressionSyntax or AwaitExpressionSyntax
                // A member initializer ('new Vector3Transition { Duration = d }') sets the new object only.
                || (node is AssignmentExpressionSyntax && !node.Parent.IsKind(SyntaxKind.ObjectInitializerExpression))
                || node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression)
                || node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression))))
        {
            return false;
        }

        // The lambda is deleted, so a right-hand side that reads its parameter
        // ('fe.CenterPoint = new((float)fe.ActualWidth / 2, ...)') would not compile, or would
        // silently bind to an outer symbol of the same name.
        if (moved.Any(value => SetLambdaHelpers.ReferencesIdentifier(value, paramName)))
            return false;

        // The rewrite deletes the whole invocation; a directive inside it would be unbalanced,
        // and a comment inside it would be lost.
        if (setInvocation.ContainsDirectives || lambda.DescendantTrivia().Any(PoolResetSetCodeFix.IsComment))
            return false;

        // With .Animate on the chain the modifier animates to its value on mount, where the .Set
        // value was migrated to the visual without animating. That is a visible change.
        if (FindChainModifier(setInvocation, model, ct, static n => n == "Animate") is not null)
            return false;

        // The same modifier anywhere on the chain owns the channel already. An earlier one wins today
        // (modifiers run after setters), so moving the value into a later call would make it win
        // instead, and next to a later one the moved call would be dead code: remove the write.
        if (FindChainModifier(setInvocation, model, ct, n => n == modifier) is not null)
        {
            return false;
        }

        // Another .Set on the chain runs in the same setter phase. Moving this write to the
        // modifier phase would let it override (or be read by) the other setter differently, so
        // stay conservative and leave chains with more than one .Set alone.
        if (FindChainModifier(setInvocation, model, ct, static n => n == "Set", requireReactorExtension: false) is not null)
            return false;

        // Bind the rewrite where the .Set stands. This rejects overload ambiguity (target-typed
        // 'new(...)' against .Scale's float and Vector3 overloads), type mismatches, and a
        // receiver type without the modifier.
        var rewritten = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                setAccess.Expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName(modifier)),
            arguments);
        var bound = model.GetSpeculativeSymbolInfo(
            setInvocation.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression).Symbol;
        return bound is IMethodSymbol method && method.Name == modifier && IsReactorExtension(method);
    }

    private static bool IsScalarTransitionModifier(string modifier) =>
        modifier is "OpacityTransition" or "RotationTransition";

    private static bool IsXamlType(ITypeSymbol? type, string name) =>
        type is not null && type.Name == name && type.ContainingNamespace?.ToDisplayString() == XamlNamespace;

    /// <summary>
    /// The modifier's argument list for a fixable write, or null when the value doesn't map onto
    /// the modifier's parameters. Usually the right-hand side, unchanged. The scalar transition
    /// modifiers take a duration and build the transition themselves, so
    /// <c>new ScalarTransition { Duration = d }</c> becomes <c>(d)</c>, and a bare
    /// <c>new ScalarTransition()</c> no argument. A null transition would remove it, which the
    /// modifier can't express. Shared with <see cref="FacadeTransformSetCodeFix"/>.
    /// </summary>
    internal static ArgumentListSyntax? BuildModifierArguments(AssignmentExpressionSyntax assignment, string modifier)
    {
        var right = assignment.Right;
        if (IsScalarTransitionModifier(modifier))
        {
            if (right is not BaseObjectCreationExpressionSyntax creation || creation.ArgumentList is { Arguments.Count: > 0 })
                return null;
            if (creation.Initializer is not { } initializer || initializer.Expressions.Count == 0)
                return SyntaxFactory.ArgumentList();
            if (initializer.Expressions.Count == 1
                && initializer.Expressions[0] is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.Text: "Duration" } } duration
                && duration.IsKind(SyntaxKind.SimpleAssignmentExpression)
                // The modifier's parameter is a TimeSpan?, so a target-typed 'default' or 'null'
                // would become no duration (the 300 ms default) instead of a zero one.
                && !duration.Right.DescendantNodesAndSelf().Any(static node =>
                    node.IsKind(SyntaxKind.DefaultLiteralExpression) || node.IsKind(SyntaxKind.NullLiteralExpression)))
            {
                return SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(duration.Right.WithoutTrivia())));
            }
            return null;
        }
        if (modifier is "ScaleTransition" or "TranslationTransition" && IsNullValue(right))
        {
            return null;
        }
        return SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(right.WithoutTrivia())));
    }

    /// <summary>A <c>!</c> suppression anywhere in the value, which hides a possible null from flow analysis.</summary>
    private static bool ContainsSuppression(ExpressionSyntax value) =>
        value.DescendantNodesAndSelf().Any(static node => node.IsKind(SyntaxKind.SuppressNullableWarningExpression));

    /// <summary>A literal <c>null</c> or <c>default</c>, through any mix of parentheses and <c>!</c> suppressions.</summary>
    private static bool IsNullValue(ExpressionSyntax value)
    {
        while (true)
        {
            if (value is PostfixUnaryExpressionSyntax suppressed && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                value = suppressed.Operand;
            else if (value is ParenthesizedExpressionSyntax parenthesized)
                value = parenthesized.Expression;
            else
                break;
        }
        return value.IsKind(SyntaxKind.NullLiteralExpression) || value.IsKind(SyntaxKind.DefaultLiteralExpression)
            || value is DefaultExpressionSyntax;
    }
}