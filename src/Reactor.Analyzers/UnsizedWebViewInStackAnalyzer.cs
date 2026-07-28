using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_MEDIA_001</c> — a <c>WebView2(...)</c> placed as a direct child of an
/// auto-layout stack (<c>HStack</c>/<c>VStack</c>/<c>FlexRow</c>/<c>FlexColumn</c>)
/// without pinning an explicit <c>.Width</c>/<c>.Height</c>.
/// </summary>
/// <remarks>
/// Auto-layout stacks hand each child an indeterminate measure on the stack's main
/// axis. <c>WebView2</c> measures to its web content — which for a real page is the
/// viewport — so with no bounds it grows to fill the available space and triggers a
/// layout oscillation when the page reflows (docs/guide/text-and-media.md §WebView2).
/// The fix is authoring-intent (there is no correct default size), so this ships as an
/// <see cref="DiagnosticSeverity.Info"/> nudge with no code-fix: pin <c>.Width</c> and
/// <c>.Height</c>, or place the control in a fixed-size <c>Grid</c> cell.
///
/// Deliberately conservative to keep false positives out: it only fires on a
/// <c>WebView2(...)</c> that is a <em>direct positional argument</em> of one of the four
/// stack factories and whose fluent chain provably pins no size. It bails when the child
/// is an opaque expression (a variable/method call whose chain it cannot see), when the
/// <c>WebView2</c> is wrapped in another element (e.g. a sized <c>Border</c>/<c>Grid</c>),
/// or when any size is set (<c>.Width</c>/<c>.Height</c>/<c>.Size</c>, or an imperative
/// <c>.Set(w =&gt; w.Width = …)</c>).
///
/// It fires only when the chain pins <em>neither</em> dimension; pinning either
/// <c>.Width</c> <em>or</em> <c>.Height</c> (or <c>.Size</c>) suppresses it. That is a
/// deliberate low-false-positive stance — a partly-sized <c>WebView2</c> already signals
/// the author is controlling its bounds, so the nudge would be noise. The message still
/// advises pinning both, which is the correct guidance for the neither-pinned case it fires on.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsizedWebViewInStackAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_MEDIA_001";

    /// <summary>The <c>WebView2</c> DSL factory name (Dsl.cs) that roots the child chain.</summary>
    private const string WebViewFactory = "WebView2";

    /// <summary>
    /// The four auto-layout stack factories whose children receive an indeterminate
    /// main-axis measure. <c>HStack</c>/<c>VStack</c> → <c>StackElement</c>,
    /// <c>FlexRow</c>/<c>FlexColumn</c> → <c>FlexElement</c> (Dsl.cs). A fixed-track
    /// <c>Grid</c> cell is intentionally excluded — it gives a determinate size.
    /// </summary>
    private static readonly HashSet<string> StackFactories = new(System.StringComparer.Ordinal)
    {
        "HStack", "VStack", "FlexRow", "FlexColumn",
    };

    /// <summary>
    /// Fluent modifiers that pin a dimension. <c>.Size(w, h)</c> pins both
    /// (ElementExtensions.cs). Any one of these in the chain means the author has set
    /// a size, so the child is not a candidate.
    /// </summary>
    private static readonly HashSet<string> SizeModifiers = new(System.StringComparer.Ordinal)
    {
        "Width", "Height", "Size",
    };

    /// <summary>FrameworkElement members whose imperative <c>.Set</c> assignment pins a size.</summary>
    private static readonly HashSet<string> SizeMembers = new(System.StringComparer.Ordinal)
    {
        "Width", "Height",
    };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "WebView2 in an auto-layout stack needs an explicit size",
        "WebView2 is a direct child of '{0}' without an explicit .Width/.Height. In an auto-sized stack it measures to its web content and oscillates as the page reflows. Pin .Width and .Height (or place it in a fixed-size Grid cell).",
        "Reactor.Layout",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An auto-layout stack (HStack/VStack/FlexRow/FlexColumn) gives each child an " +
                     "indeterminate measure on its main axis. WebView2 measures to its web content — " +
                     "the viewport for a real page — so with no bounds it grows to fill the available " +
                     "space and triggers a layout oscillation when the page reflows. Pin an explicit " +
                     ".Width and .Height, or host the control in a fixed-size Grid cell.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        // Syntactic gate: the invocation must be one of the four stack factories,
        // called bare (`using static Factories`) or qualified (`Factories.HStack`).
        var stackName = invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.ValueText,
            _ => null,
        };
        if (stackName is null || !StackFactories.Contains(stackName))
            return;

        // Inspect each direct positional child. A non-element leading `double spacing`
        // argument (the HStack(spacing, …) overload) simply fails the WebView2 peel.
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (TryGetUnsizedWebView(arg.Expression, out var webView))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(Rule, webView!.GetLocation(), stackName));
            }
        }
    }

    /// <summary>
    /// Peels a fluent modifier chain from the outside in. Returns <see langword="true"/>
    /// only when the chain roots at a <c>WebView2(...)</c> factory call and no size is
    /// pinned anywhere in the chain. Any opaque, wrapped, or sized child returns
    /// <see langword="false"/>.
    /// </summary>
    private static bool TryGetUnsizedWebView(ExpressionSyntax expr, out InvocationExpressionSyntax? webView)
    {
        webView = null;
        var current = expr;

        while (current is InvocationExpressionSyntax invocation)
        {
            switch (invocation.Expression)
            {
                // A bare call: the `WebView2(...)` factory root, or — since fluent
                // modifiers are always member-access calls — some other factory
                // (a wrapper like `Border(webView)`), which is not a candidate.
                case IdentifierNameSyntax id:
                    if (id.Identifier.ValueText == WebViewFactory)
                    {
                        webView = invocation;
                        return true;
                    }
                    return false;

                case MemberAccessExpressionSyntax member:
                    var name = member.Name.Identifier.ValueText;

                    // Qualified factory root: `Factories.WebView2(...)`.
                    if (name == WebViewFactory)
                    {
                        webView = invocation;
                        return true;
                    }

                    // A size is pinned (fluent .Width/.Height/.Size, or an imperative
                    // .Set that assigns Width/Height) → the author controls the bounds,
                    // so suppress. Pinning either dimension is enough to bail (see the
                    // deliberate neither-pinned contract in the class remarks).
                    if (SizeModifiers.Contains(name))
                        return false;
                    if (name == "Set" && SetAssignsSize(invocation))
                        return false;

                    // An ordinary modifier (event handler, .Margin, .WithKey, etc.). Fluent
                    // modifiers preserve the element (they return the same Element type), so
                    // peeling through an unrecognized one to reach the WebView2 root is safe:
                    // a true wrapper is a *factory* call taking the element as an argument
                    // (e.g. Border(WebView2())), which fails the WebView2-root checks above
                    // and bails there, not here.
                    current = member.Expression;
                    continue;

                default:
                    return false;
            }
        }

        // Chain did not root at a `WebView2(...)` call (e.g. a bare variable) — opaque.
        return false;
    }

    /// <summary>
    /// True when a <c>.Set(x =&gt; x.Width = …)</c> / <c>.Set(x =&gt; { x.Height = …; })</c>
    /// lambda assigns to the lambda parameter's own <c>Width</c>/<c>Height</c> member — an
    /// imperative size pin. The assignment receiver must be the parameter itself, so sizing
    /// an unrelated object or a nested child (<c>x.Child.Width = …</c>) does not wrongly
    /// suppress the diagnostic for a genuinely unsized <c>WebView2</c>.
    /// </summary>
    private static bool SetAssignsSize(InvocationExpressionSyntax setInvocation)
    {
        var args = setInvocation.ArgumentList.Arguments;
        if (args.Count != 1)
            return false;

        var lambda = args[0].Expression;
        var parameterName = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1
                => paren.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null,
        };
        if (parameterName is null)
            return false;

        foreach (var assignment in lambda.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is MemberAccessExpressionSyntax lhs
                && SizeMembers.Contains(lhs.Name.Identifier.ValueText)
                && lhs.Expression is IdentifierNameSyntax receiver
                && receiver.Identifier.ValueText == parameterName)
            {
                return true;
            }
        }

        return false;
    }
}
