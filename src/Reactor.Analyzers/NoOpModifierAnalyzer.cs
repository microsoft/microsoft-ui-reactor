using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_MOD_003 — a <b>generic common modifier</b> applied to an element whose mounted control
/// is outside the set of WinUI types <c>Reconciler.ApplyModifiers</c> writes that modifier to. The
/// call compiles, chains, type-checks, and is then silently dropped at reconcile time.
/// </summary>
/// <remarks>
/// <para>
/// The motivating case is <c>Rectangle().Size(80, 80).Background("#FF6B6B")</c>, which renders an
/// invisible shape: <c>ApplyModifiers</c> writes <c>Background</c> only to a <c>Panel</c>,
/// <c>Control</c>, or <c>Border</c>, and a <c>Rectangle</c> is a
/// <c>Microsoft.UI.Xaml.Shapes.Shape</c>. Shapes are painted with <c>Fill</c>/<c>Stroke</c>. The
/// <c>ThemeRef</c> overload drops through a second, independent copy of the same allow-list in
/// <c>Reconciler.GetDependencyPropertyName</c>, so every <c>Background</c> overload is affected.
/// </para>
/// <para>
/// This is the reverse direction of <see cref="PoolResetSetAnalyzer"/>'s <c>REACTOR_MOD_002</c>:
/// that rule asks "you wrote <c>.Set</c>, would the modifier work here?", this one asks "you wrote
/// the modifier, does it reach this control?". Both answer from the same
/// <see cref="ModifierTable"/> allow-lists, so the two cannot disagree about what
/// <c>ApplyModifiers</c> does.
/// </para>
/// <para><b>Precision.</b> Every step below is a hard gate, and any uncertainty resolves to
/// <em>no diagnostic</em>:</para>
/// <list type="number">
/// <item><description>The modifier must carry a <see cref="ModifierInfo.ControlGate"/>. A
/// <see langword="null"/> gate is <b>never</b> read as "applies everywhere" here — see the note on
/// <c>IsEnabled</c> below.</description></item>
/// <item><description>The call must bind to the <em>generic</em>
/// <c>Microsoft.UI.Reactor.ElementExtensions</c> modifier (its <c>this</c> parameter is a type
/// parameter). A type-specific overload such as <c>RichTextBlockElement.FontSize(double)</c> writes
/// the element record directly and is sound, so it is skipped — this is the same
/// <see cref="ModifierInfo.ElementTypes"/> escape route <c>REACTOR_MOD_002</c> OR-combines, obtained
/// here for free from overload resolution.</description></item>
/// <item><description>The receiver must be a concrete element type. A type parameter
/// (<c>T Style&lt;T&gt;(T el) where T : Element =&gt; el.Background(…)</c>) or a receiver typed as
/// <c>Element</c> could be anything at runtime.</description></item>
/// <item><description>The element type must expose Reactor's <c>Set(this TElement,
/// Action&lt;TControl&gt;)</c> overload, whose <c>Action</c> type argument <em>is</em> the control
/// the descriptor mounts and therefore the one <c>ApplyModifiers</c> receives. This is the same
/// signal <c>REACTOR_MOD_002</c> reads off the <c>.Set</c> lambda parameter, and it is public API —
/// unlike the <c>[GenerateReactorWrapper]</c> / <c>[GenerateReactorDescriptor]</c> attributes, which
/// live in <c>Reactor.Wrappers.Abstractions</c> and do <b>not</b> flow to consumers
/// (<c>PrivateAssets="all"</c> in <c>Reactor.csproj</c>), so they are unresolvable in exactly the
/// compilations this rule has to work in. Elements with no <c>Set</c> overload are skipped.
/// <c>ModifierTableIntegrityTests</c> pins the equivalence: for every element carrying a generator
/// attribute, the <c>Set</c> overload's control must equal the attribute's.</description></item>
/// <item><description><c>XamlInterop</c>'s host element takes
/// <c>Action&lt;FrameworkElement&gt;</c> but hosts an arbitrary pre-built XAML element, which at
/// runtime may well <em>be</em> a <c>Panel</c> or <c>Control</c>. Those two polymorphic bases are
/// excluded outright.</description></item>
/// </list>
/// <para>
/// <b>Why a <see langword="null"/> <see cref="ModifierInfo.ControlGate"/> is not "ungated".</b>
/// <c>IsEnabled</c> / <c>HorizontalContentAlignment</c> / <c>VerticalContentAlignment</c> <em>are</em>
/// <c>Control</c>-gated in <c>ApplyModifiers</c>, but the table leaves their gate null because in the
/// <c>.Set</c> direction the receiver is already a <c>Control</c> (WinUI declares those dependency
/// properties only there), so no predicate is needed. In <em>this</em> direction
/// <c>.IsEnabled(false)</c> is callable on any <c>Element</c>, so treating null as "reaches
/// everything" would silently drop real findings. Reporting is therefore restricted to entries with
/// an explicit gate, and <c>ModifierTable.GateOnlyInReconciler</c> plus its integrity test keep the
/// remainder recorded rather than invisible.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoOpModifierAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_MOD_003";

    /// <summary>Code-fix payload: the modifier name to rewrite the call to.</summary>
    internal const string ReplacementKey = "Replacement";

    /// <summary>
    /// Code-fix payload: how the arguments must be carried across — <c>rename</c> passes them
    /// through unchanged onto an identically-shaped overload, <c>string</c> wraps a single colour
    /// string in <c>BrushHelper.Parse(…)</c> because the shape modifiers take a <c>Brush</c>. Absent
    /// when no mechanical rewrite is sound (e.g. the <c>ThemeRef</c> overload), and the fix is then
    /// not registered.
    /// </summary>
    internal const string ArgumentKindKey = "ArgumentKind";

    internal const string RenameArgument = "rename";
    internal const string StringArgument = "string";

    private const string ReactorNamespace = "Microsoft.UI.Reactor";
    private const string ReactorCoreNamespace = "Microsoft.UI.Reactor.Core";
    private const string XamlNamespace = "Microsoft.UI.Xaml";
    private const string XamlControlsNamespace = "Microsoft.UI.Xaml.Controls";
    private const string XamlShapesNamespace = "Microsoft.UI.Xaml.Shapes";
    private const string XamlMediaNamespace = "Microsoft.UI.Xaml.Media";

    private const string ElementExtensionsTypeName = "ElementExtensions";
    private const string ElementTypeName = "Element";
    private const string ShapeTypeName = "Shape";
    private const string BrushTypeName = "Brush";
    private const string SetMethodName = "Set";

    /// <summary>
    /// Modifier → the shape modifier(s) that carry the same intent, most specific first. Only
    /// consulted when the receiver's control is a <c>Shape</c>, and only after the candidate is
    /// confirmed to resolve on that element type — so <c>LineElement</c>, which has no <c>Fill</c>,
    /// falls through to <c>Stroke</c>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> ShapeReplacements =
        new Dictionary<string, string[]>(System.StringComparer.Ordinal)
        {
            { "Background", new[] { "Fill", "Stroke" } },
            { "Foreground", new[] { "Fill", "Stroke" } },
            { "BorderBrush", new[] { "Stroke" } },
            { "BorderThickness", new[] { "StrokeThickness" } },
        };

    /// <summary>
    /// <c>element|modifier</c> → the element-specific modifier that carries the same intent, for
    /// receivers that are not shapes. <c>FlexPanel</c> is a <c>Panel</c> but not a
    /// <c>StackPanel</c>, so <c>ApplyModifiers</c> drops <c>Padding</c> on it; the Yoga box model
    /// exposes the equivalent as <c>FlexPadding</c>, whose three overloads mirror
    /// <c>Padding</c>'s exactly.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ElementReplacements =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "Microsoft.UI.Reactor.Core.FlexElement|Padding", "FlexPadding" },
        };

    /// <summary>
    /// <c>element|modifier</c> pairs where the element's own generated descriptor reads the common
    /// modifier slot and writes it to the control itself, so <c>ApplyModifiers</c>' gate is not the
    /// authority and the value is <em>not</em> dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RichTextBlockElement</c>'s <c>Customize</c> hook declares
    /// <c>get: e =&gt; e.Padding …, set: (c, v) =&gt; c.Padding = v, dp: RichTextBlock.PaddingProperty</c>
    /// — <c>e.Padding</c> being the base <c>Element.Padding</c> shim over the common modifier bag.
    /// A <c>RichTextBlock</c> is none of <c>Control</c>, <c>Border</c>, <c>StackPanel</c> or
    /// <c>TextBlock</c>, so the control gate says "dropped" while the descriptor in fact applies it.
    /// Reporting there would be a false positive on correct code, which is the one outcome worse
    /// than the bug this rule exists to catch.
    /// </para>
    /// <para>
    /// This is the only such entry in the framework today. A <c>ModifierTableIntegrityTests</c>
    /// guard parses every descriptor <c>Customize</c> hook and fails if another one starts (or stops)
    /// consuming a gated common modifier, so the exception list cannot go stale in either direction.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyCollection<string> DescriptorAppliedModifiers =
        new HashSet<string>(System.StringComparer.Ordinal)
        {
            "Microsoft.UI.Reactor.Core.RichTextBlockElement|Padding",
        };

    /// <summary>Key shared by <see cref="ElementReplacements"/> and <see cref="DescriptorAppliedModifiers"/>.</summary>
    internal static string ElementModifierKey(string elementFullName, string modifier) =>
        elementFullName + "|" + modifier;

    /// <summary>
    /// Control bases that stand in for "whatever was handed to us" rather than naming a real
    /// control, so their declared type says nothing about what <c>ApplyModifiers</c> will see.
    /// </summary>
    private static readonly string[] PolymorphicHostControls = { "FrameworkElement", "UIElement" };

    private static readonly LocalizableString Title =
        "Modifier is silently dropped for this element's control type";

    private static readonly LocalizableString MessageFormat =
        "'{0}' has no effect on '{1}' — Reactor only applies '{0}' to {2}{3}";

    private static readonly LocalizableString Description =
        "Reconciler.ApplyModifiers writes each of these common modifiers only to specific WinUI " +
        "control types. On anything else the call compiles and is silently discarded, so the value " +
        "never reaches the control — for example '.Background(...)' on a Rectangle renders an " +
        "invisible shape, because shapes are painted with '.Fill(...)'. Use the modifier the target " +
        "control actually supports, or host the element in a container that does (a Border for " +
        "Background).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Modifier",
        DiagnosticSeverity.Warning,
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

        // Shape: `<receiver>.Modifier(args)`.
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleMemberAccessExpression,
            } memberAccess)
            return;

        // Cheap syntactic gate before any semantic work.
        var modifierName = memberAccess.Name.Identifier.ValueText;
        if (!ModifierTable.Properties.TryGetValue(modifierName, out var info)
            || info.ControlGate is not { } gate)
            return;

        var model = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || !IsGenericReactorModifier(method))
            return;

        // A constant-null argument is inert regardless of the receiver, so the control gate is not
        // what makes it do nothing — and the rewrite would not be equivalent. See
        // HasConstantNullArgument.
        if (HasConstantNullArgument(invocation, model, cancellationToken))
            return;

        if (model.GetTypeInfo(memberAccess.Expression, cancellationToken).Type is not INamedTypeSymbol receiver
            || !IsConcreteReactorElement(receiver))
            return;

        if (!TryGetMountedControl(model, invocation.SpanStart, receiver, out var control))
            return;

        // The XamlInterop host: declared as a base, mounted as whatever the caller supplied.
        if (PolymorphicHostControls.Contains(control.Name)
            && control.ContainingNamespace?.ToDisplayString() == XamlNamespace)
            return;

        // Reconciler.ApplyModifiers writes this modifier to one of these — nothing to report.
        if (gate.Any(allowed => SetLambdaHelpers.InheritsFrom(control, allowed, XamlControlsNamespace)))
            return;

        // …and neither is the gate the authority when the element's own descriptor consumes the
        // common-modifier slot and writes it to the control itself.
        var elementKey = ElementModifierKey(receiver.ToDisplayString(), modifierName);
        if (DescriptorAppliedModifiers.Contains(elementKey))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;
        var hint = string.Empty;

        if (TryGetReplacement(context, invocation, receiver, control, method, modifierName, elementKey,
                out var replacement, out var argumentKind))
        {
            var family = SetLambdaHelpers.InheritsFrom(control, ShapeTypeName, XamlShapesNamespace)
                ? $"{control.Name} is a Shape, which is painted with '{replacement}'"
                : $"'{replacement}' is the equivalent on {receiver.Name}";

            hint = argumentKind is null
                ? $". {family}"
                : $". {family} — did you mean '.{replacement}(...)'?";

            if (argumentKind is not null)
            {
                properties = properties
                    .Add(ReplacementKey, replacement)
                    .Add(ArgumentKindKey, argumentKind);
            }
        }
        else if (modifierName == "Background")
        {
            hint = ". Wrap it in a Border(...) to paint a background behind this element";
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            memberAccess.Name.GetLocation(),
            properties,
            modifierName,
            receiver.Name,
            Humanize(gate),
            hint));
    }

    /// <summary>
    /// True when an argument is a compile-time constant <see langword="null"/> — <c>(Brush)null</c>,
    /// <c>default</c>, <c>default(Brush)</c>. Such a call is skipped entirely: no diagnostic, no fix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why no diagnostic.</b> The common modifiers funnel through
    /// <c>VisualModifiers.Merge</c>, which is <c>Background = other.Background ?? Background</c> —
    /// a null delta reads as "not supplied" and leaves the previous value alone. So
    /// <c>.Background((Brush)null)</c> does nothing on <em>any</em> receiver, not because
    /// <c>ApplyModifiers</c>' control gate rejected it. Reporting it would name the wrong cause,
    /// and this rule's contract is that anything uncertain produces no diagnostic.
    /// </para>
    /// <para>
    /// <b>Why no fix, even more importantly.</b> The shape modifiers assign the element record
    /// directly (<c>el with { Fill = brush }</c>), and for a reference-typed property backed by a
    /// dependency property the generated descriptor takes the <c>Optional&lt;T&gt;</c> + dp channel.
    /// <c>Optional&lt;T&gt;</c> is explicit that <c>with { X = null }</c> becomes
    /// <c>Optional&lt;T&gt;.Of(null)</c> and <b>not</b> <c>Unset</c> — an explicit set-to-null, which clears
    /// the brush. Rewriting <c>.Background(null)</c> to <c>.Fill(null)</c> would therefore turn a
    /// no-op into an active clear: a behaviour-changing auto-fix, which is the exact failure this
    /// analyzer exists to prevent.
    /// </para>
    /// <para>
    /// Only <em>syntactically</em> constant nulls are caught. An expression that merely happens to
    /// evaluate to null at runtime is undecidable here and is left alone — the same line
    /// <c>REACTOR_MOD_002</c> draws for its own null/default check.
    /// </para>
    /// </remarks>
    private static bool HasConstantNullArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        global::System.Threading.CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var constant = model.GetConstantValue(argument.Expression, cancellationToken);
            if (constant.HasValue && constant.Value is null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True for the generic common modifier <c>ElementExtensions.M&lt;T&gt;(this T el, …)</c>. The
    /// <c>this</c> parameter being a type parameter is what separates it from a type-specific
    /// overload like <c>RichTextBlockElement.FontSize(double)</c>, which writes the record property
    /// directly and therefore never goes through <c>ApplyModifiers</c>' control gate.
    /// </summary>
    private static bool IsGenericReactorModifier(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        var containingType = definition.ContainingType;

        return containingType is { Name: ElementExtensionsTypeName }
            && containingType.ContainingNamespace?.ToDisplayString() == ReactorNamespace
            && definition.IsExtensionMethod
            && definition.Parameters.Length > 0
            && definition.Parameters[0].Type.TypeKind == TypeKind.TypeParameter;
    }

    /// <summary>
    /// The receiver must be a concrete Reactor element record. <c>Element</c> itself is excluded:
    /// its runtime type is unknown, so nothing can be said about the control it mounts.
    /// </summary>
    private static bool IsConcreteReactorElement(INamedTypeSymbol receiver) =>
        SetLambdaHelpers.InheritsFrom(receiver, ElementTypeName, ReactorCoreNamespace)
        && !(receiver.Name == ElementTypeName
             && receiver.ContainingNamespace?.ToDisplayString() == ReactorCoreNamespace);

    /// <summary>
    /// The WinUI control the element mounts, read off Reactor's <c>Set(this TElement,
    /// Action&lt;TControl&gt;)</c> overload — the <c>Action</c> type argument is the control type the
    /// generated <c>ControlDescriptor&lt;TElement, TControl&gt;</c> was built for, so it is the one
    /// <c>ApplyModifiers</c> is handed at reconcile time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator attributes would be the more direct source, but they live in
    /// <c>Reactor.Wrappers.Abstractions</c>, which <c>Reactor.csproj</c> references with
    /// <c>PrivateAssets="all"</c> — so in a consumer compilation the attribute type does not resolve
    /// and the element looks unannotated. <c>Set</c> is public, is generated/declared alongside the
    /// descriptor, and is the same fact <c>REACTOR_MOD_002</c> reads off its <c>.Set</c> lambda
    /// parameter.
    /// </para>
    /// <para>
    /// Bails on an ambiguous element (two <c>Set</c> overloads naming different controls) and on an
    /// element with none, so an unknown control never produces a diagnostic. The <c>Set</c> must be
    /// declared <b>for this exact element type</b>: an element record derived from a wrapped one
    /// inherits the base's <c>Set</c>, but nothing stops it being registered against a different
    /// control, so the inherited signature is not evidence about what it mounts.
    /// </para>
    /// </remarks>
    private static bool TryGetMountedControl(
        SemanticModel model,
        int position,
        INamedTypeSymbol receiver,
        out INamedTypeSymbol control)
    {
        INamedTypeSymbol? found = null;

        // Filtered in the pipeline rather than with an in-loop `continue` (CodeQL
        // cs/linq/missed-where): everything here is a predicate on the symbol's shape, and only the
        // ambiguity check needs the loop.
        var mountedControls = model
            .LookupSymbols(position, receiver, SetMethodName, includeReducedExtensionMethods: true)
            .OfType<IMethodSymbol>()
            .Where(method =>
                method is { MethodKind: MethodKind.ReducedExtension, Parameters.Length: 1, ReducedFrom: not null }
                && IsDeclaredByReactorForExactly(method.ReducedFrom!, receiver))
            .Select(method => method.Parameters[0].Type as INamedTypeSymbol)
            .Where(action =>
                action is { Name: "Action", TypeArguments.Length: 1 }
                && action.ContainingNamespace?.ToDisplayString() == "System")
            .Select(action => action!.TypeArguments[0] as INamedTypeSymbol)
            .Where(candidate => candidate is not null);

        foreach (var candidate in mountedControls)
        {
            if (found is not null && !SymbolEqualityComparer.Default.Equals(found, candidate))
            {
                control = null!;
                return false;
            }

            found = candidate;
        }

        control = found!;
        return found is not null;
    }

    /// <summary>
    /// The method is declared on <c>Microsoft.UI.Reactor.ElementExtensions</c> — the one surface
    /// this analyzer treats as authoritative, and the only one
    /// <c>ModifierTableIntegrityTests</c> cross-checks against the descriptors.
    /// </summary>
    /// <remarks>
    /// Applied to both the <c>Set</c> lookup (which decides the mounted control) and the
    /// replacement lookup (which decides what the fix rewrites to). Without it, a user-defined
    /// <c>Fill(this RectangleElement, Brush)</c> or <c>Set(this SomeElement, Action&lt;…&gt;)</c>
    /// that happens to be in scope would be taken as framework truth — the fix could rewrite to
    /// someone else's method, or emit a call that is ambiguous and does not compile.
    /// </remarks>
    private static bool IsElementExtensionsMember(IMethodSymbol method)
    {
        var containingType = (method.ReducedFrom ?? method).ContainingType;

        return containingType is { Name: ElementExtensionsTypeName }
            && containingType.ContainingNamespace?.ToDisplayString() == ReactorNamespace;
    }

    /// <summary>
    /// The <c>Set</c> overload is Reactor's own — declared on
    /// <c>Microsoft.UI.Reactor.ElementExtensions</c> — and is declared for
    /// <paramref name="receiver"/> itself, not inherited from a base element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declaring type is pinned rather than just the namespace root, so a user-defined
    /// <c>Set(this SomeElement, Action&lt;SomeControl&gt;)</c> helper that happens to live under
    /// <c>Microsoft.UI.Reactor.*</c> cannot be mistaken for the framework's control-type evidence.
    /// This is also exactly the surface
    /// <c>ModifierTableIntegrityTests.Every_Element_Set_Overload_Names_The_Control_Its_Descriptor_Mounts</c>
    /// cross-checks against the descriptors, so the analyzer trusts nothing that guard does not
    /// verify. (The only other <c>Set</c> overloads in the framework are the three Win2D canvas ones
    /// in <c>Reactor.Advanced</c>; their controls are <c>Control</c>-derived, so every gate passes
    /// and they would never be reported anyway.)
    /// </para>
    /// <para>
    /// The receiver comparison runs on the original definitions: <c>ReducedFrom</c> does not carry
    /// the type arguments inferred during reduction, so <c>Set&lt;T&gt;(this ItemsViewElement&lt;T&gt;, …)</c>
    /// reduced against <c>ItemsViewElement&lt;Foo&gt;</c> still reports <c>ItemsViewElement&lt;T&gt;</c>.
    /// A base and a derived element still have different original definitions, so the
    /// inherited-only case stays excluded.
    /// </para>
    /// </remarks>
    private static bool IsDeclaredByReactorForExactly(IMethodSymbol declared, INamedTypeSymbol receiver)
    {
        return IsElementExtensionsMember(declared)
            && SymbolEqualityComparer.Default.Equals(
                declared.Parameters[0].Type.OriginalDefinition, receiver.OriginalDefinition);
    }

    /// <summary>
    /// Resolves the modifier this call should have used, and how (if at all) the code fix can carry
    /// the arguments across.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Candidates come from <see cref="ElementReplacements"/> (element-specific, e.g.
    /// <c>FlexElement.Padding</c> → <c>FlexPadding</c>) or, for a <c>Shape</c> receiver, from
    /// <see cref="ShapeReplacements"/>. A candidate is only reported at all once it resolves as an
    /// invocable member on the receiver, so <c>LineElement</c> — which has no <c>Fill</c> — falls
    /// through to <c>Stroke</c>.
    /// </para>
    /// <para>
    /// <paramref name="argumentKind"/> is <see langword="null"/> when the replacement exists but no
    /// mechanical rewrite is sound; the caller then names the replacement without offering a fix and
    /// without the "did you mean" phrasing, because there is nothing to click. Two rewrites are
    /// sound:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Rename</b> — some overload of the replacement has exactly the bound
    /// modifier's parameter types, in order.</description></item>
    /// <item><description><b>Parse</b> — the modifier took a colour <c>string</c> and the
    /// replacement has a single-<c>Brush</c> overload, so the value goes through
    /// <c>BrushHelper.Parse</c> exactly as <c>Background(string)</c> does internally.</description></item>
    /// </list>
    /// <para>
    /// Both require every argument to be positional and to fill the whole replacement parameter
    /// list. Named arguments are refused because the parameter names differ
    /// (<c>Background(color:)</c> vs <c>Fill(brush:)</c>), and a partially-applied optional list is
    /// refused because the replacement's parameters may have no defaults — <c>Padding(top: 8)</c>
    /// binds the four-parameter <c>Padding</c> overload, but <c>FlexPadding</c>'s four-parameter
    /// overload is not optional.
    /// </para>
    /// </remarks>
    private static bool TryGetReplacement(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol receiver,
        INamedTypeSymbol control,
        IMethodSymbol method,
        string modifierName,
        string elementKey,
        out string replacement,
        out string? argumentKind)
    {
        replacement = null!;
        argumentKind = null;

        string[] candidates;
        if (ElementReplacements.TryGetValue(elementKey, out var elementSpecific))
            candidates = new[] { elementSpecific };
        else if (SetLambdaHelpers.InheritsFrom(control, ShapeTypeName, XamlShapesNamespace)
                 && ShapeReplacements.TryGetValue(modifierName, out var shapeCandidates))
            candidates = shapeCandidates;
        else
            return false;

        var positional = invocation.ArgumentList.Arguments;
        var allPositional = method.MethodKind == MethodKind.ReducedExtension
            && positional.All(argument => argument.NameColon is null && argument.RefKindKeyword.IsKind(SyntaxKind.None));

        foreach (var candidate in candidates)
        {
            var overloads = context.SemanticModel
                .LookupSymbols(invocation.SpanStart, receiver, candidate, includeReducedExtensionMethods: true)
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.ReducedExtension && IsElementExtensionsMember(m))
                .ToArray();

            if (overloads.Length == 0)
                continue;

            // First resolvable candidate wins the hint, fix or not.
            replacement ??= candidate;

            if (!allPositional)
                continue;

            if (overloads.Any(overload =>
                    overload.Parameters.Length == positional.Count
                    && SignaturesMatch(overload, method)))
            {
                replacement = candidate;
                argumentKind = RenameArgument;
                return true;
            }

            if (positional.Count == 1
                && method.Parameters.Length == 1
                && method.Parameters[0].Type.SpecialType == SpecialType.System_String
                && overloads.Any(overload =>
                    overload.Parameters.Length == 1
                    && SymbolEqualityComparer.Default.Equals(overload.ReturnType, method.ReturnType)
                    && SetLambdaHelpers.InheritsFrom(overload.Parameters[0].Type, BrushTypeName, XamlMediaNamespace)))
            {
                replacement = candidate;
                argumentKind = StringArgument;
                return true;
            }
        }

        return replacement is not null;
    }

    /// <summary>
    /// The two reduced methods take exactly the same parameter types, in order, and return the same
    /// type.
    /// </summary>
    /// <remarks>
    /// The return-type check keeps the fluent chain intact. The generic modifier returns the
    /// receiver's own type, while a replacement declared on a base element returns that base — so
    /// rewriting <c>Derived().Background(b)</c> to <c>Derived().Fill(b)</c> would narrow the
    /// expression to the base type and break a subsequent derived-only modifier, or a method whose
    /// declared return type is the derived element.
    /// </remarks>
    private static bool SignaturesMatch(IMethodSymbol replacement, IMethodSymbol original)
    {
        if (replacement.Parameters.Length != original.Parameters.Length
            || !SymbolEqualityComparer.Default.Equals(replacement.ReturnType, original.ReturnType))
            return false;

        for (var i = 0; i < replacement.Parameters.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(replacement.Parameters[i].Type, original.Parameters[i].Type))
                return false;
        }

        return true;
    }

    /// <summary>"Panel, Control, or Border".</summary>
    internal static string Humanize(string[] gate) => gate.Length switch
    {
        0 => "no control type",
        1 => gate[0],
        2 => $"{gate[0]} or {gate[1]}",
        _ => string.Join(", ", gate.Take(gate.Length - 1)) + ", or " + gate[gate.Length - 1],
    };
}
