using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_DYM_006 — a compile-time-constant color string that <c>BrushHelper</c> cannot parse, so
/// it silently renders gray (e.g. <c>.Background("orange")</c>, <c>.Foreground("#888")</c>, or a font
/// name stored through <c>ResourceBuilder.Set(key, "Consolas")</c>). Paired with
/// <see cref="ColorStringLiteralCodeFix"/>.
/// </summary>
/// <remarks>
/// <para>
/// Covers every Reactor API whose <c>color</c> string goes through <c>BrushHelper.Parse</c>:
/// <c>BrushHelper.Parse</c> itself, <c>ResourceBuilder.Set(string, string)</c>, and the
/// <c>string</c> overloads of the <c>Background</c> / <c>Foreground</c> / <c>BorderBrush</c> /
/// <c>WithBorder</c> modifiers on <c>ElementExtensions</c>. Targets are resolved once per
/// compilation and matched by symbol identity, so a look-alike API in another namespace never fires.
/// </para>
/// <para>
/// Unlike REACTOR_DYM_001–005 this does <em>not</em> ride a compiler error — the code compiles — but
/// the result is always a wrong (gray) brush, so it is a Warning. Only constant values are checked
/// (literals, <c>const</c>s, constant concatenation, and each constant branch of a <c>?:</c> /
/// <c>??</c> / switch expression); runtime strings are out of reach. The <c>mur check</c>
/// <c>ColorStringLiteralRule</c> anchors on this diagnostic's ID and repeats the did-you-mean.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ColorStringLiteralAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DYM_006";

    public const string BrushHelperMetadataName = "Microsoft.UI.Reactor.BrushHelper";
    public const string ResourceBuilderMetadataName = "Microsoft.UI.Reactor.Elements.ResourceBuilder";
    public const string ElementExtensionsMetadataName = "Microsoft.UI.Reactor.ElementExtensions";
    public const string FontFamilyMetadataName = "Microsoft.UI.Xaml.Media.FontFamily";

    /// <summary>The modifier names whose <c>string color</c> overloads parse through BrushHelper.</summary>
    public static readonly ImmutableHashSet<string> ModifierNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Background", "Foreground", "BorderBrush", "WithBorder");

    /// <summary>The parameter name every covered API uses for its color string.</summary>
    public const string ColorParameterName = "color";

    /// <summary>The resource-key parameter name of <c>ResourceBuilder.Set</c>.</summary>
    public const string KeyParameterName = "key";

    /// <summary>Property-bag key: the <see cref="ColorStringIssue"/> name.</summary>
    public const string IssueProperty = "Issue";

    /// <summary>Property-bag key: the replacement value (literal text, or family name). Absent when there is no fix.</summary>
    public const string ReplacementProperty = "Replacement";

    private static readonly LocalizableString Title =
        "Color string that BrushHelper cannot parse renders gray";
    private static readonly LocalizableString MessageFormat =
        "'{0}' is not a color BrushHelper can parse, so it renders gray; {1}";
    private static readonly LocalizableString Description =
        "BrushHelper.Parse understands only red, green, blue, white, black, gray/grey, lightgray/lightgrey, transparent, #RRGGBB and #AARRGGBB. Anything else silently falls back to a gray brush. This covers BrushHelper.Parse, ResourceBuilder.Set(string, string), and the string overloads of the Background/Foreground/BorderBrush/WithBorder modifiers.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.DidYouMean",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var targets = Targets.Resolve(start.Compilation);
            if (targets is null)
                return; // No Reactor color-string API referenced.

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeInvocation(ctx, targets),
                SyntaxKind.InvocationExpression);
        });
    }

    /// <summary>The resolved color-string APIs for one compilation.</summary>
    internal sealed class Targets
    {
        private Targets(ImmutableHashSet<IMethodSymbol> methods, IMethodSymbol? resourceSet, bool fontFamilyOverloadExists)
        {
            Methods = methods;
            ResourceSet = resourceSet;
            FontFamilyOverloadExists = fontFamilyOverloadExists;
        }

        public ImmutableHashSet<IMethodSymbol> Methods { get; }
        public IMethodSymbol? ResourceSet { get; }
        public bool FontFamilyOverloadExists { get; }

        public static Targets? Resolve(Compilation compilation)
        {
            var methods = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
            var str = compilation.GetSpecialType(SpecialType.System_String);

            var brushHelper = compilation.GetTypeByMetadataName(BrushHelperMetadataName);
            if (brushHelper is not null)
            {
                foreach (var m in brushHelper.GetMembers("Parse").OfType<IMethodSymbol>())
                    if (HasStringColorParameter(m, str))
                        methods.Add(m);
            }

            IMethodSymbol? resourceSet = null;
            var fontFamilyOverload = false;
            var resourceBuilder = compilation.GetTypeByMetadataName(ResourceBuilderMetadataName);
            if (resourceBuilder is not null)
            {
                foreach (var m in resourceBuilder.GetMembers("Set").OfType<IMethodSymbol>())
                {
                    if (m.Parameters.Length != 2)
                        continue;
                    if (HasStringColorParameter(m, str) && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, str))
                    {
                        resourceSet = m;
                        methods.Add(m);
                    }
                    else if (m.Parameters[1].Type.ToDisplayString() == FontFamilyMetadataName)
                    {
                        fontFamilyOverload = true;
                    }
                }
            }

            var extensions = compilation.GetTypeByMetadataName(ElementExtensionsMetadataName);
            if (extensions is not null)
            {
                foreach (var name in ModifierNames)
                    foreach (var m in extensions.GetMembers(name).OfType<IMethodSymbol>())
                        if (m.IsExtensionMethod && HasStringColorParameter(m, str))
                            methods.Add(m);
            }

            return methods.Count == 0 ? null : new Targets(methods.ToImmutable(), resourceSet, fontFamilyOverload);
        }

        private static bool HasStringColorParameter(IMethodSymbol method, ITypeSymbol str)
        {
            foreach (var p in method.Parameters)
                if (p.Name == ColorParameterName && SymbolEqualityComparer.Default.Equals(p.Type, str))
                    return true;
            return false;
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, Targets targets)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Cheap syntactic pre-filter before any binding.
        var calledName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            MemberBindingExpressionSyntax b => b.Name.Identifier.ValueText,
            _ => null,
        };
        if (calledName is null || !(calledName == "Parse" || calledName == "Set" || ModifierNames.Contains(calledName)))
            return;
        // The ubiquitous `.Set(c => …)` setter modifier has one argument; ResourceBuilder.Set has two.
        if (calledName == "Set" && invocation.ArgumentList.Arguments.Count != 2)
            return;

        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation)
            return;

        var target = operation.TargetMethod;
        var method = (target.ReducedFrom ?? target).OriginalDefinition;
        if (!targets.Methods.Contains(method))
            return;

        IArgumentOperation? colorArg = null;
        IArgumentOperation? keyArg = null;
        foreach (var arg in operation.Arguments)
        {
            if (arg.Parameter is null)
                continue;
            if (arg.Parameter.Name == ColorParameterName)
                colorArg = arg;
            else if (arg.Parameter.Name == KeyParameterName)
                keyArg = arg;
        }
        if (colorArg is null || colorArg.ArgumentKind != ArgumentKind.Explicit)
            return;

        string? resourceKey = null;
        if (targets.ResourceSet is not null
            && SymbolEqualityComparer.Default.Equals(method, targets.ResourceSet)
            && keyArg?.Value.ConstantValue is { HasValue: true, Value: string key })
        {
            resourceKey = key;
        }

        foreach (var (expression, value) in ConstantStrings(colorArg.Value.Syntax as ExpressionSyntax, context.SemanticModel, context.CancellationToken))
        {
            var finding = ColorStringClassifier.Classify(value, resourceKey, targets.FontFamilyOverloadExists);
            if (finding is not { } f)
                continue;

            var properties = ImmutableDictionary.CreateBuilder<string, string?>();
            properties.Add(IssueProperty, f.Issue.ToString());
            if (f.Replacement is not null)
                properties.Add(ReplacementProperty, f.Replacement);

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                expression.GetLocation(),
                properties.ToImmutable(),
                value,
                f.Detail));
        }
    }

    /// <summary>
    /// The constant strings an argument can evaluate to, each with the expression that produced it.
    /// A wholly-constant expression yields itself; otherwise only value-<em>selecting</em> shapes are
    /// walked (parentheses, <c>?:</c>, <c>??</c>, switch arms) — never into nested calls.
    /// </summary>
    internal static IEnumerable<(ExpressionSyntax Expression, string Value)> ConstantStrings(
        ExpressionSyntax? expression, SemanticModel model, CancellationToken cancellationToken)
    {
        if (expression is null)
            yield break;

        var constant = model.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue)
        {
            if (constant.Value is string s)
                yield return (expression, s);
            yield break;
        }

        IEnumerable<ExpressionSyntax> children = expression switch
        {
            ParenthesizedExpressionSyntax p => new[] { p.Expression },
            ConditionalExpressionSyntax c => new[] { c.WhenTrue, c.WhenFalse },
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) => new[] { b.Left, b.Right },
            SwitchExpressionSyntax sw => sw.Arms.Select(a => a.Expression),
            _ => Array.Empty<ExpressionSyntax>(),
        };
        foreach (var child in children)
            foreach (var item in ConstantStrings(child, model, cancellationToken))
                yield return item;
    }
}
