using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_WIN2D_001: Detects a Reactor.Advanced Win2D canvas that draws resources
/// produced by the <c>UseCanvasResources</c> hook but never opts into Win2D's
/// process-wide shared device via <c>.UseSharedDevice()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseCanvasResources</c> builds its resources on <c>CanvasDevice.GetSharedDevice()</c>
/// (see <c>src/Reactor.Advanced/Win2D/Hooks/UseCanvasResources.cs</c>). Win2D resources are
/// device-affine: a canvas defaults to its own dedicated device, so drawing a shared-device
/// resource with that canvas raises a cross-device error that surfaces as a <b>fatal stowed
/// exception</b> at runtime. The fix is to opt the canvas into the shared device with
/// <c>.UseSharedDevice()</c> (<c>src/Reactor.Advanced/Win2D/Win2DCanvasModifiers.cs</c>).
/// </para>
/// <para>
/// This is an <see cref="DiagnosticSeverity.Error"/> because the mistake compiles cleanly and
/// only fails — fatally — at draw time. To keep an Error rule from ever firing incorrectly the
/// analyzer is deliberately conservative: it fires only when it can see, in the same render body,
/// (1) an inline canvas element construction whose fluent chain provably lacks
/// <c>.UseSharedDevice()</c>, and (2) that canvas expression referencing a local whose initializer
/// is a <c>UseCanvasResources</c> call — i.e. the canvas actually draws a shared-device resource.
/// It bails on any opaque construction (variable/field capture of the canvas, a raw <c>.Set(...)</c>
/// in the chain, or a <c>with { ... }</c> mutation) where the modifier cannot be proven absent.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Win2DSharedDeviceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_WIN2D_001";

    private const string ManualElementFqn = "Microsoft.UI.Reactor.Advanced.Win2D.Win2DCanvasElement";
    private const string AnimatedElementFqn = "Microsoft.UI.Reactor.Advanced.Win2D.Win2DAnimatedCanvasElement";
    private const string VirtualElementFqn = "Microsoft.UI.Reactor.Advanced.Win2D.Win2DVirtualCanvasElement";
    private const string HookHolderFqn = "Microsoft.UI.Reactor.Advanced.Win2D.UseCanvasResourcesHook";

    private const string SharedDeviceModifier = "UseSharedDevice";
    private const string RawSetter = "Set";
    private const string Hook = "UseCanvasResources";
    private const string DrawCallbackParam = "onDraw";
    private const string RegionDrawCallbackParam = "onRegionDraw";

    private static readonly SymbolDisplayFormat FqnFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly ImmutableHashSet<string> FactoryNames =
        ImmutableHashSet.Create("Win2DCanvas", "Win2DAnimatedCanvas", "Win2DVirtualCanvas");

    private static readonly LocalizableString Title =
        "Win2D canvas drawing UseCanvasResources output must call .UseSharedDevice()";

    private static readonly LocalizableString MessageFormat =
        "This Win2D canvas draws 'UseCanvasResources' output (created on Win2D's shared device) " +
        "without '.UseSharedDevice()'; the cross-device draw raises a fatal stowed exception at " +
        "runtime. Append '.UseSharedDevice()' to the canvas.";

    private static readonly LocalizableString Description =
        "UseCanvasResources creates its resources on Win2D's process-wide shared device " +
        "(CanvasDevice.GetSharedDevice()). Win2D resources are device-affine, so a canvas that " +
        "draws them must opt into that same device via .UseSharedDevice(); a default-device canvas " +
        "drawing a shared-device resource fails with a fatal cross-device stowed exception. See the " +
        "shared-device section of docs/guide/win2d-canvas.md for the opt-in guidance.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Win2D",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Syntactic fast gate: anchor only on the canvas factory call itself (not on a
        // modifier in the chain, which would also return a canvas element type).
        var name = GetInvokedSimpleName(invocation);
        if (name is null || !FactoryNames.Contains(name))
            return;

        // Semantic confirm: the invoked factory returns one of the three Win2D canvas element
        // records. This filters out any unrelated same-named user method.
        if (!ReturnsWin2DCanvasElement(context, invocation))
            return;

        // Walk the fluent chain built directly on the factory call.
        var outer = WalkChain(invocation, out var hasSharedDevice, out var hasRawSetter);

        // Already opted in — nothing to do.
        if (hasSharedDevice)
            return;

        // A raw .Set(...) can reach the underlying control's UseSharedDevice; a `with { }` can set
        // the element's init property; an opaque/variable capture may apply .UseSharedDevice() off
        // the chain we can see. In any of these the modifier cannot be proven absent — bail
        // (the spec's low-FP contract: bail on opaque/variable canvas construction).
        if (hasRawSetter || IsOpaqueContext(outer))
            return;

        // Causal link: this canvas must actually DRAW a shared-device resource, evidenced by the
        // factory's draw callback (onDraw / onRegionDraw) referencing a local whose initializer is a
        // UseCanvasResources call. Restricting to the draw callback (vs. onUpdate, redrawKey, or an
        // event-handler modifier) is what ties the diagnostic to the actual cross-device draw.
        if (!CanvasDrawsSharedResource(context, invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, GetFactoryNameLocation(invocation)));
    }

    /// <summary>
    /// Walks up the fluent-modifier chain rooted at <paramref name="factory"/> and returns the
    /// outermost invocation of that chain (the whole canvas element expression). Shared with the
    /// code fix so both agree on where <c>.UseSharedDevice()</c> is appended.
    /// </summary>
    internal static InvocationExpressionSyntax GetOutermostFluentInvocation(InvocationExpressionSyntax factory) =>
        WalkChain(factory, out _, out _);

    /// <summary>
    /// Walks the fluent-modifier chain over <paramref name="factory"/>, transparently stepping
    /// through enclosing parentheses (<c>(canvas).Modifier()</c>), and reports whether the chain
    /// applies <c>.UseSharedDevice()</c> or a raw <c>.Set(...)</c>. Returns the outermost invocation.
    /// </summary>
    private static InvocationExpressionSyntax WalkChain(InvocationExpressionSyntax factory, out bool hasSharedDevice, out bool hasRawSetter)
    {
        hasSharedDevice = false;
        hasRawSetter = false;

        var outermost = factory;
        SyntaxNode current = factory;
        while (true)
        {
            current = SkipParentheses(current);

            if (current.Parent is MemberAccessExpressionSyntax ma
                && ma.Expression == current
                && ma.Parent is InvocationExpressionSyntax next)
            {
                var modifier = ma.Name.Identifier.ValueText;
                if (modifier == SharedDeviceModifier)
                {
                    // .UseSharedDevice() / .UseSharedDevice(true) opt in. A dynamic argument might be
                    // true at runtime, so treat it as opted-in to avoid a false Error. Only a literal
                    // .UseSharedDevice(false) is a provable opt-out that should still be reported.
                    if (!IsExplicitlyDisabled(next)) hasSharedDevice = true;
                }
                else if (modifier == RawSetter) hasRawSetter = true;

                outermost = next;
                current = next;
                continue;
            }

            break;
        }

        return outermost;
    }

    /// <summary>
    /// True when the canvas expression is consumed opaquely — captured into a variable/field, used
    /// as an assignment RHS, or mutated by a <c>with { }</c> — so a <c>.UseSharedDevice()</c> opt-in
    /// may exist beyond the chain the analyzer can see. Parentheses are transparent.
    /// </summary>
    private static bool IsOpaqueContext(InvocationExpressionSyntax outer)
    {
        var parent = outer.Parent;
        while (parent is ParenthesizedExpressionSyntax paren)
            parent = paren.Parent;

        // A canvas factory call can never be an assignment target, so reaching an assignment or an
        // initializer via the expression parent chain means the canvas is the captured value.
        return parent is WithExpressionSyntax
            or EqualsValueClauseSyntax
            or AssignmentExpressionSyntax;
    }

    private static SyntaxNode SkipParentheses(SyntaxNode node)
    {
        while (node.Parent is ParenthesizedExpressionSyntax paren)
            node = paren;

        return node;
    }

    /// <summary>
    /// True for a literal <c>.UseSharedDevice(false)</c> — the only argument shape that provably
    /// leaves the canvas on its own device (and therefore still crashes when it draws a shared
    /// resource). A missing/true/dynamic argument is treated as opted-in.
    /// </summary>
    private static bool IsExplicitlyDisabled(InvocationExpressionSyntax useSharedDeviceCall)
    {
        var args = useSharedDeviceCall.ArgumentList.Arguments;
        return args.Count == 1 && args[0].Expression.IsKind(SyntaxKind.FalseLiteralExpression);
    }

    private static bool ReturnsWin2DCanvasElement(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return false;

        var returnType = method.ReturnType.ToDisplayString(FqnFormat).Replace("global::", "");
        return returnType == ManualElementFqn
            || returnType == AnimatedElementFqn
            || returnType == VirtualElementFqn;
    }

    /// <summary>
    /// True when the canvas draws a shared-device resource: the factory's draw callback
    /// (the argument bound to the <c>onDraw</c> / <c>onRegionDraw</c> parameter) references a local
    /// whose initializer is a <c>UseCanvasResources</c> hook call. References elsewhere — a scalar
    /// <c>redrawKey:</c>, an animated <c>onUpdate</c> tick, or an event-handler modifier — do not
    /// count, because the cross-device crash happens only when the resource is actually drawn.
    /// </summary>
    private static bool CanvasDrawsSharedResource(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax factory)
    {
        var drawCallback = GetDrawCallbackArgument(context, factory);
        if (drawCallback is null)
            return false;

        var model = context.SemanticModel;
        foreach (var identifier in drawCallback.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not ILocalSymbol local)
                continue;

            if (SymbolInitializerIsHook(model, local, factory, context.CancellationToken))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the expression passed as the canvas factory's draw callback (the argument bound to the
    /// <c>onDraw</c> or <c>onRegionDraw</c> parameter), matched by named argument or positional index.
    /// </summary>
    private static ExpressionSyntax? GetDrawCallbackArgument(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax factory)
    {
        var method = context.SemanticModel.GetSymbolInfo(factory, context.CancellationToken).Symbol as IMethodSymbol;
        var args = factory.ArgumentList.Arguments;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var parameterName = arg.NameColon?.Name.Identifier.ValueText
                ?? (method is not null && i < method.Parameters.Length ? method.Parameters[i].Name : null);

            if (parameterName is DrawCallbackParam or RegionDrawCallbackParam)
                return arg.Expression;
        }

        return null;
    }

    private static bool SymbolInitializerIsHook(SemanticModel model, ISymbol symbol, SyntaxNode contextNode, System.Threading.CancellationToken ct)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            // The idiomatic `var res = ctx.UseCanvasResources(...)` local always declares in the same
            // tree as the canvas that captures it; only inspect same-tree declarations so a single
            // SemanticModel stays valid.
            if (reference.SyntaxTree != contextNode.SyntaxTree)
                continue;

            if (reference.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } init }
                && IsUseCanvasResourcesInvocation(model, init, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUseCanvasResourcesInvocation(SemanticModel model, ExpressionSyntax expression, System.Threading.CancellationToken ct)
    {
        if (expression is not InvocationExpressionSyntax invocation)
            return false;
        if (GetInvokedSimpleName(invocation) != Hook)
            return false;

        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
            return false;

        // Require the exact hook: the UseCanvasResources method on the static UseCanvasResourcesHook
        // class. Matching only the name + a RenderContext receiver would let an unrelated app-defined
        // UseCanvasResources extension trip this Error rule — matching the containing type avoids that.
        return method.Name == Hook
            && method.ContainingType is { } holder
            && holder.ToDisplayString(FqnFormat).Replace("global::", "") == HookHolderFqn;
    }

    private static string? GetInvokedSimpleName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
        _ => null,
    };

    private static Location GetFactoryNameLocation(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
        _ => invocation.Expression.GetLocation(),
    };
}
