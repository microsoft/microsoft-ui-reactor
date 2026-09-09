using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_ICON_001</c> — flags a <see cref="WindowIconKind"/> handed to a shell surface that
/// cannot consume it, where the value is silently dropped at runtime.
/// </summary>
/// <remarks>
/// <para>Every <c>WindowIcon</c> factory type-checks against every icon-taking surface, but the
/// surfaces need different primitives and quietly skip what they cannot use:</para>
/// <list type="bullet">
///   <item><description>The tray icon, taskbar overlay and thumbnail-toolbar button need a raw
///   <c>HICON</c> — from <c>LoadImageW</c> on a file or <c>CreateIconFromResourceEx</c> on
///   in-memory data. Neither reads an <c>ms-appx:</c> URI, so <c>FromResource</c> is
///   skipped.</description></item>
///   <item><description>The window caption needs a filesystem path, because that is what
///   <c>AppWindow.SetIcon</c> takes, so a binary source is reported as not applied and the window
///   falls back to its convention/PE icon.</description></item>
///   <item><description>Jump lists resolve a logo by <c>Uri</c> (packaged) or filesystem path
///   (unpackaged), so a binary source works on neither.</description></item>
/// </list>
/// <para>Each of those is a <c>Debug.WriteLine</c> and a missing glyph — invisible in a Release
/// build. Reactor's own design spec carried the <c>FromResource</c>-for-a-tray-icon mistake in its
/// worked example until issue #1185, which is the case for surfacing it in the editor.</para>
/// <para><b>What this deliberately does not flag.</b> Whether a jump list takes a path or a URI
/// depends on package identity, which is a runtime property — so <c>FromPath</c> and
/// <c>FromResource</c> on a jump-list entry are never reported. And the rule only fires when the
/// icon's kind is <i>provably</i> known at the use site (see <see cref="TryResolveKind"/>);
/// anything opaque — a conditional, a field, a parameter, a method result — is left alone.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WindowIconSurfaceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_ICON_001";

    private const string WindowIconType = "Microsoft.UI.Reactor.WindowIcon";
    private const string IconMemberName = "Icon";
    private const string RunIconParameterName = "icon";

    // Code-fix / tooling hand-off keys (data travels in Diagnostic.Properties, never message text).
    internal const string FactoryKey = "Factory";
    internal const string SurfaceKey = "Surface";

    /// <summary>The four <c>WindowIcon</c> factories, mapped to the kind each produces.</summary>
    private enum IconKind { Path, Resource, Binary }

    private static readonly IReadOnlyDictionary<string, IconKind> Factories =
        new Dictionary<string, IconKind>(System.StringComparer.Ordinal)
        {
            { "FromPath", IconKind.Path },
            { "FromResource", IconKind.Resource },
            { "FromBytes", IconKind.Binary },
            { "FromRgba", IconKind.Binary },
        };

    /// <summary>
    /// One icon-consuming surface: the kind it cannot use, and the remedy to name in the message.
    /// </summary>
    private readonly struct Surface
    {
        public readonly IconKind Rejected;
        public readonly string Because;
        public readonly string Remedy;

        public Surface(IconKind rejected, string because, string remedy)
        {
            Rejected = rejected;
            Because = because;
            Remedy = remedy;
        }
    }

    /// <summary>
    /// Surfaces keyed by the fully-qualified type that owns the <c>Icon</c> member. Grounded in
    /// <c>ReactorTrayIcon.LoadHIcon</c>, <c>TaskbarOverlay.LoadIconFor</c>,
    /// <c>ThumbnailToolbarState.LoadIconFor</c>, <c>WindowIcon.TryResolvePath</c> and
    /// <c>JumpListComInterop.BuildShellLinkArray</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Surface> Surfaces =
        new Dictionary<string, Surface>(System.StringComparer.Ordinal)
        {
            ["Microsoft.UI.Reactor.TrayIconSpec"] = new(
                IconKind.Resource,
                "the notification area needs a raw HICON, which cannot be loaded from an ms-appx: URI",
                "WindowIcon.FromPath, WindowIcon.FromBytes or WindowIcon.FromRgba"),

            ["Microsoft.UI.Reactor.TaskbarOverlay"] = new(
                IconKind.Resource,
                "the taskbar overlay needs a raw HICON, which cannot be loaded from an ms-appx: URI",
                "WindowIcon.FromPath, WindowIcon.FromBytes or WindowIcon.FromRgba"),

            ["Microsoft.UI.Reactor.ThumbnailToolbarButton"] = new(
                IconKind.Resource,
                "a thumbnail-toolbar button needs a raw HICON, which cannot be loaded from an ms-appx: URI",
                "WindowIcon.FromPath, WindowIcon.FromBytes or WindowIcon.FromRgba"),

            ["Microsoft.UI.Reactor.WindowSpec"] = new(
                IconKind.Binary,
                "AppWindow.SetIcon needs a filesystem path, so the window falls back to its convention or PE icon",
                "WindowIcon.FromPath or WindowIcon.FromResource"),

            ["Microsoft.UI.Reactor.JumpListItem"] = new(
                IconKind.Binary,
                "a jump-list logo is resolved by Uri or filesystem path, and binary data is neither",
                "WindowIcon.FromResource (packaged) or WindowIcon.FromPath (unpackaged)"),
        };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "WindowIcon source kind is not usable by this surface",
        "'{0}' is not usable as {1} — {2}. Use {3}.",
        "Reactor.Windowing",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every WindowIcon factory type-checks against every icon-taking surface, but the surfaces need " +
            "different primitives and silently skip what they cannot use — a Debug.WriteLine and a missing glyph. " +
            "The tray icon, taskbar overlay and thumbnail-toolbar button need a raw HICON and cannot read an " +
            "ms-appx: resource URI; the window caption needs a filesystem path and cannot take in-memory bytes; a " +
            "jump-list logo needs a Uri or a path. Only reported when the icon's kind is provably known at the use " +
            "site.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // `new TrayIconSpec(Icon: …)`, `new WindowSpec { Icon = … }`, `spec with { Icon = … }`.
        context.RegisterSyntaxNodeAction(
            AnalyzeConstruction,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.WithExpression);

        // `window.Overlay.Icon = …` — the overlay has no constructor an app can call.
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);

        // `ReactorApp.Run<App>("t", icon: …)` and `JumpListItem.ForUri(…, icon: …)`.
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // ══════════════════════════════════════════════════════════════
    //  Entry points
    // ══════════════════════════════════════════════════════════════

    private static void AnalyzeConstruction(SyntaxNodeAnalysisContext ctx)
    {
        var node = (ExpressionSyntax)ctx.Node;

        // Cheap syntactic gate: an Icon must be supplied somewhere on this node.
        var initializer = GetInitializer(node);
        var argumentList = GetArgumentList(node);
        if (initializer is null && argumentList is null)
            return;

        var type = ctx.SemanticModel.GetTypeInfo(node, ctx.CancellationToken).Type;
        if (type is null || !Surfaces.TryGetValue(type.ToDisplayString(), out var surface))
            return;

        // (a) `Icon = …` in an object initializer or a `with` expression.
        if (initializer is not null)
        {
            var assignment = initializer.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(a => a.Left is IdentifierNameSyntax id && id.Identifier.ValueText == IconMemberName);

            if (assignment is not null)
            {
                Check(ctx, assignment.Right, surface, $"{type.Name}.{IconMemberName}");
                return;
            }
        }

        // (b) `Icon:` as a constructor argument (named, or positional on a record's primary ctor).
        if (argumentList is not null)
        {
            var ctor = ctx.SemanticModel.GetSymbolInfo(node, ctx.CancellationToken).Symbol as IMethodSymbol;
            var argument = FindArgument(argumentList, ctor, IconMemberName);
            if (argument is not null)
                Check(ctx, argument.Expression, surface, $"{type.Name}.{IconMemberName}");
        }
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext ctx)
    {
        var assignment = (AssignmentExpressionSyntax)ctx.Node;
        if (assignment.Left is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: IconMemberName } member)
            return;

        if (ctx.SemanticModel.GetSymbolInfo(member, ctx.CancellationToken).Symbol is not IPropertySymbol property)
            return;

        var owner = property.ContainingType?.ToDisplayString();
        if (owner is null || !Surfaces.TryGetValue(owner, out var surface))
            return;

        Check(ctx, assignment.Right, surface, $"{property.ContainingType!.Name}.{IconMemberName}");
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        var container = method.ContainingType?.ToDisplayString();
        if (container is null)
            return;

        // ReactorApp.Run(..., icon: ...) feeds WindowSpec.Icon.
        if (container == "Microsoft.UI.Reactor.ReactorApp" && method.Name == "Run")
        {
            var argument = FindArgument(invocation.ArgumentList, method, RunIconParameterName);
            if (argument is not null && Surfaces.TryGetValue("Microsoft.UI.Reactor.WindowSpec", out var windowSurface))
                Check(ctx, argument.Expression, windowSurface, "the window icon");
            return;
        }

        // JumpListItem.ForUri / ForCommandLine take the same icon the record does.
        if (container == "Microsoft.UI.Reactor.JumpListItem" &&
            (method.Name == "ForUri" || method.Name == "ForCommandLine"))
        {
            var argument = FindArgument(invocation.ArgumentList, method, RunIconParameterName);
            if (argument is not null && Surfaces.TryGetValue(container, out var jumpSurface))
                Check(ctx, argument.Expression, jumpSurface, $"JumpListItem.{IconMemberName}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Matching
    // ══════════════════════════════════════════════════════════════

    private static void Check(SyntaxNodeAnalysisContext ctx, ExpressionSyntax value, Surface surface, string surfaceLabel)
    {
        if (!TryResolveKind(ctx, value, out var kind, out var factoryName))
            return;
        if (kind != surface.Rejected)
            return;

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(FactoryKey, factoryName)
            .Add(SurfaceKey, surfaceLabel);

        ctx.ReportDiagnostic(Diagnostic.Create(
            Rule, value.GetLocation(), properties,
            $"WindowIcon.{factoryName}", surfaceLabel, surface.Because, surface.Remedy));
    }

    /// <summary>
    /// Resolves the <see cref="IconKind"/> of <paramref name="value"/>, but only when it is
    /// <b>provably</b> known. Two shapes qualify, and they are the two that occur in practice:
    /// <list type="number">
    ///   <item><description>a direct <c>WindowIcon.FromX(…)</c> call at the use site;</description></item>
    ///   <item><description>a local whose single initializer is such a call, or is
    ///   <c>UseMemo(() =&gt; WindowIcon.FromX(…))</c> — the idiom the windowing guide
    ///   documents — and which is never reassigned.</description></item>
    /// </list>
    /// Everything else returns <c>false</c>. That is what keeps a conditional
    /// (<c>packaged ? FromResource(…) : FromPath(…)</c>), a field, a parameter or a method result
    /// from being reported on a guess.
    /// </summary>
    private static bool TryResolveKind(
        SyntaxNodeAnalysisContext ctx, ExpressionSyntax value, out IconKind kind, out string factoryName)
    {
        kind = default;
        factoryName = string.Empty;

        if (TryMatchFactoryCall(ctx, value, out kind, out factoryName))
            return true;

        // A bare identifier — follow it to its declaration, once, within the same method.
        if (value is not IdentifierNameSyntax identifier)
            return false;

        if (ctx.SemanticModel.GetSymbolInfo(identifier, ctx.CancellationToken).Symbol is not ILocalSymbol local)
            return false;

        var declarator = local.DeclaringSyntaxReferences.Length == 1
            ? local.DeclaringSyntaxReferences[0].GetSyntax(ctx.CancellationToken) as VariableDeclaratorSyntax
            : null;

        if (declarator?.Initializer is null)
            return false;

        // A local that is assigned again anywhere could hold a different kind by the time it is
        // used. Bail rather than reason about order.
        var body = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (body is null || IsReassigned(ctx, body, local))
            return false;

        var initializer = declarator.Initializer.Value;

        if (TryMatchFactoryCall(ctx, initializer, out kind, out factoryName))
            return true;

        // `UseMemo(() => WindowIcon.FromX(...))` — the documented idiom. The memo is transparent:
        // it returns exactly what the lambda returns.
        if (TryUnwrapUseMemo(ctx, initializer) is { } memoized)
            return TryMatchFactoryCall(ctx, memoized, out kind, out factoryName);

        return false;
    }

    /// <summary>True when <paramref name="value"/> is a direct call to a <c>WindowIcon</c> factory.</summary>
    private static bool TryMatchFactoryCall(
        SyntaxNodeAnalysisContext ctx, ExpressionSyntax value, out IconKind kind, out string factoryName)
    {
        kind = default;
        factoryName = string.Empty;

        if (value is not InvocationExpressionSyntax invocation)
            return false;

        // Cheap syntactic pre-check before asking for a symbol.
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
            return false;
        if (!Factories.ContainsKey(member.Name.Identifier.ValueText))
            return false;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method)
            return false;
        if (!method.IsStatic || method.ContainingType?.ToDisplayString() != WindowIconType)
            return false;

        if (!Factories.TryGetValue(method.Name, out kind))
            return false;

        factoryName = method.Name;
        return true;
    }

    /// <summary>
    /// The lambda body of a <c>UseMemo(() =&gt; …)</c> call, or <c>null</c>. Only the
    /// expression-bodied form is unwrapped — a block-bodied memo can return different values on
    /// different paths, which is exactly the ambiguity this rule refuses to guess through.
    /// </summary>
    private static ExpressionSyntax? TryUnwrapUseMemo(SyntaxNodeAnalysisContext ctx, ExpressionSyntax value)
    {
        if (value is not InvocationExpressionSyntax invocation)
            return null;

        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null,
        };

        if (name != "UseMemo")
            return null;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method)
            return null;
        if (!CommandDebounceAnalyzer.IsReactorNamespace(method.ContainingNamespace?.ToDisplayString()))
            return null;

        var first = invocation.ArgumentList.Arguments.FirstOrDefault();
        return first?.Expression switch
        {
            ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } body } => body,
            SimpleLambdaExpressionSyntax { ExpressionBody: { } body } => body,
            _ => null,
        };
    }

    /// <summary>
    /// True when <paramref name="local"/> is the target of any assignment, or is passed by
    /// <c>ref</c>/<c>out</c>, anywhere inside <paramref name="scope"/>.
    /// </summary>
    private static bool IsReassigned(SyntaxNodeAnalysisContext ctx, SyntaxNode scope, ILocalSymbol local)
    {
        foreach (var node in scope.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment
                    when SymbolEquals(ctx, assignment.Left, local):
                    return true;

                case ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None } argument
                    when SymbolEquals(ctx, argument.Expression, local):
                    return true;
            }
        }

        return false;
    }

    private static bool SymbolEquals(SyntaxNodeAnalysisContext ctx, ExpressionSyntax expression, ILocalSymbol local) =>
        expression is IdentifierNameSyntax
        && SymbolEqualityComparer.Default.Equals(
            ctx.SemanticModel.GetSymbolInfo(expression, ctx.CancellationToken).Symbol, local);

    // ══════════════════════════════════════════════════════════════
    //  Syntax helpers (mirrors RawCommandCallbackAnalyzer)
    // ══════════════════════════════════════════════════════════════

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

    /// <summary>
    /// The argument bound to <paramref name="parameterName"/>. A <b>named</b> argument matches
    /// regardless of the rest; the <b>positional</b> slot is only resolved when the call is
    /// entirely positional, so a mixed call is skipped rather than mis-counted.
    /// </summary>
    private static ArgumentSyntax? FindArgument(ArgumentListSyntax argumentList, IMethodSymbol? method, string parameterName)
    {
        var arguments = argumentList.Arguments;

        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == parameterName)
                return argument;
        }

        if (arguments.Any(a => a.NameColon is not null))
            return null;
        if (method is null)
            return null;

        var parameter = method.Parameters.FirstOrDefault(p => p.Name == parameterName);
        if (parameter is null)
            return null;

        return parameter.Ordinal < arguments.Count ? arguments[parameter.Ordinal] : null;
    }
}
