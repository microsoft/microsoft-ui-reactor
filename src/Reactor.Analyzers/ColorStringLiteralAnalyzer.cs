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

    /// <summary>
    /// Property-bag key (FontFamily fix only): the value parameter name of
    /// <c>ResourceBuilder.Set(string, FontFamily)</c>, so a named <c>color:</c> argument can be renamed.
    /// </summary>
    public const string FontFamilyParameterProperty = "FontFamilyParameter";

    private static readonly LocalizableString Title =
        "Unrecognized color string renders gray";
    private static readonly LocalizableString MessageFormat =
        "'{0}' is not a color string Reactor can parse, so it renders gray — {1}";
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
        private Targets(ImmutableHashSet<IMethodSymbol> methods, IMethodSymbol? resourceSet, string? fontFamilyParameterName)
        {
            Methods = methods;
            ResourceSet = resourceSet;
            FontFamilyParameterName = fontFamilyParameterName;
        }

        public ImmutableHashSet<IMethodSymbol> Methods { get; }
        public IMethodSymbol? ResourceSet { get; }

        /// <summary>The value parameter of <c>ResourceBuilder.Set(string, FontFamily)</c>, or null when that overload doesn't exist.</summary>
        public string? FontFamilyParameterName { get; }

        public static Targets? Resolve(Compilation compilation)
        {
            var methods = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
            var str = compilation.GetSpecialType(SpecialType.System_String);

            var brushHelper = compilation.GetTypeByMetadataName(BrushHelperMetadataName);
            if (brushHelper is not null)
            {
                foreach (var m in brushHelper.GetMembers("Parse").OfType<IMethodSymbol>().Where(m => HasStringColorParameter(m, str)))
                    methods.Add(m);
            }

            IMethodSymbol? resourceSet = null;
            string? fontFamilyParameter = null;
            var resourceBuilder = compilation.GetTypeByMetadataName(ResourceBuilderMetadataName);
            if (resourceBuilder is not null)
            {
                foreach (var m in resourceBuilder.GetMembers("Set").OfType<IMethodSymbol>().Where(m => m.Parameters.Length == 2))
                {
                    if (HasStringColorParameter(m, str) && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, str))
                    {
                        resourceSet = m;
                        methods.Add(m);
                    }
                    else if (m.Parameters[1].Type.ToDisplayString() == FontFamilyMetadataName)
                    {
                        fontFamilyParameter = m.Parameters[1].Name;
                    }
                }
            }

            var extensions = compilation.GetTypeByMetadataName(ElementExtensionsMetadataName);
            if (extensions is not null)
            {
                foreach (var name in ModifierNames)
                    foreach (var m in extensions.GetMembers(name).OfType<IMethodSymbol>().Where(m => m.IsExtensionMethod && HasStringColorParameter(m, str)))
                        methods.Add(m);
            }

            return methods.Count == 0 ? null : new Targets(methods.ToImmutable(), resourceSet, fontFamilyParameter);
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
        // Through a user-defined conversion (e.g. a `ColorToken` with `implicit operator string`) the
        // source strings never reach BrushHelper — the operator's result does — so the syntax says nothing.
        if (colorArg.Value is IConversionOperation { Conversion.IsUserDefined: true })
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
            // new FontFamily(...) can only replace the whole argument: inside a ?: branch the other
            // branch would still be a string and the expression would stop type-checking.
            var canUseFontFamily = targets.FontFamilyParameterName is not null && expression.Parent is ArgumentSyntax;
            // A named `color:` argument must be renamed too, or the rewritten call won't bind.
            var renamedArgument = canUseFontFamily && expression.Parent is ArgumentSyntax { NameColon: not null }
                ? targets.FontFamilyParameterName
                : null;
            var finding = ColorStringClassifier.Classify(value, resourceKey, canUseFontFamily, renamedArgument);
            if (finding is not { } f)
                continue;

            var properties = ImmutableDictionary.CreateBuilder<string, string?>();
            properties.Add(IssueProperty, f.Issue.ToString());
            if (f.Replacement is not null)
                properties.Add(ReplacementProperty, f.Replacement);
            if (f.Issue == ColorStringIssue.FontFamilyKey && f.Replacement is not null)
                properties.Add(FontFamilyParameterProperty, targets.FontFamilyParameterName);

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
    /// walked (parentheses, <c>?:</c>, <c>??</c>, switch arms) — never into nested calls. Branches a
    /// constant condition or a constant non-null <c>??</c> left side rules out are skipped, so dead
    /// code (<c>DebugBuild ? "magenta" : color</c> with a <c>const bool</c>) is never reported.
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

        if (!IsPlainString(expression, model, cancellationToken))
            yield break;

        foreach (var child in LiveChildren(expression, model, cancellationToken))
            foreach (var item in ConstantStrings(child, model, cancellationToken))
                yield return item;
    }

    /// <summary>
    /// Whether a non-constant sub-expression is itself a string value that flows on unchanged. A branch
    /// of another type (e.g. <c>token ?? "orange"</c> where <c>token</c> is a <c>ColorToken</c> with
    /// <c>implicit operator string</c>) or one reaching string through a user-defined conversion is a
    /// boundary: whatever strings it contains, the operator's result is what gets parsed.
    /// </summary>
    private static bool IsPlainString(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        var typeInfo = model.GetTypeInfo(expression, cancellationToken);
        if ((typeInfo.Type ?? typeInfo.ConvertedType)?.SpecialType != SpecialType.System_String)
            return false;
        return !model.GetConversion(expression, cancellationToken).IsUserDefined;
    }

    /// <summary>
    /// The sub-expressions a value-selecting shape can actually yield. Empty for any other shape.
    /// </summary>
    private static IEnumerable<ExpressionSyntax> LiveChildren(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken) =>
        expression switch
        {
            ParenthesizedExpressionSyntax p => new[] { p.Expression },
            ConditionalExpressionSyntax c => model.GetConstantValue(c.Condition, cancellationToken) is { HasValue: true, Value: bool taken }
                ? new[] { taken ? c.WhenTrue : c.WhenFalse }
                : new[] { c.WhenTrue, c.WhenFalse },
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) =>
                IsKnownNonNull(b.Left, model, cancellationToken)
                    ? new[] { b.Left }
                    : new[] { b.Left, b.Right },
            // Arms are only pruned when every pattern is a plain constant and the governing value is a
            // constant; anything fancier (relational, type, `when`) keeps every arm.
            SwitchExpressionSyntax sw => LiveArms(sw, model, cancellationToken),
            _ => Array.Empty<ExpressionSyntax>(),
        };

    /// <summary>
    /// Whether the expression provably evaluates to a non-null value: a non-null constant, a
    /// <c>throw</c> (it never yields at all), or a value-selecting shape whose every live branch is one
    /// of those. Unknown (e.g. a variable) is <see langword="false"/>.
    /// </summary>
    private static bool IsKnownNonNull(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        if (expression is ThrowExpressionSyntax)
            return true;
        if (!IsPlainString(expression, model, cancellationToken))
            return false;

        var constant = model.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue)
            return constant.Value is not null;

        if (expression is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.CoalesceExpression))
        {
            // A non-null left value can still become null through a user-defined conversion
            // (`token ?? "red"` with an `implicit operator string` that returns null), so such a
            // coalesce is never provably non-null, whatever its right side is.
            if (model.GetOperation(b, cancellationToken) is ICoalesceOperation { ValueConversion.IsUserDefined: true })
                return false;
            return IsKnownNonNull(b.Left, model, cancellationToken) || IsKnownNonNull(b.Right, model, cancellationToken);
        }

        var any = false;
        foreach (var child in LiveChildren(expression, model, cancellationToken))
        {
            if (!IsKnownNonNull(child, model, cancellationToken))
                return false;
            any = true;
        }
        return any;
    }

    private static IEnumerable<ExpressionSyntax> LiveArms(SwitchExpressionSyntax sw, SemanticModel model, CancellationToken cancellationToken)
    {
        var governing = model.GetConstantValue(sw.GoverningExpression, cancellationToken);
        if (!governing.HasValue)
            return sw.Arms.Select(a => a.Expression);

        foreach (var arm in sw.Arms)
        {
            if (arm.WhenClause is not null)
                return sw.Arms.Select(a => a.Expression);
            switch (arm.Pattern)
            {
                case DiscardPatternSyntax:
                    return new[] { arm.Expression };
                // Compare the pattern constant *converted to the governing type* (`1` against a
                // `const long` is a long 1), not the boxed source literal.
                case ConstantPatternSyntax when model.GetOperation(arm.Pattern, cancellationToken) is IConstantPatternOperation { Value.ConstantValue: { HasValue: true } armValue }:
                    if (Equals(armValue.Value, governing.Value))
                        return new[] { arm.Expression };
                    break;
                default:
                    return sw.Arms.Select(a => a.Expression);
            }
        }
        return Array.Empty<ExpressionSyntax>();
    }
}
