using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_NAV_001</c> — a <see cref="Navigation.NavigationHandle{TRoute}"/>
/// returned by <c>UseNavigation</c> must not be stashed in a <c>static</c> field or property.
/// </summary>
/// <remarks>
/// <para>
/// The pitfall (navigation.md §"Treating <c>UseNavigation</c> like a singleton"):
/// </para>
/// <code>
/// public static NavigationHandle&lt;Route&gt;? Nav;
/// // ...
/// var nav = UseNavigation(Route.Home);
/// Nav = nav; // capture for later use from anywhere
/// </code>
/// <para>
/// The handle is bound to the dispatcher of the component that created it. Stashed
/// in a <c>static</c>, it outlives the page and pins (leaks) that dispatcher; once
/// the dispatcher shuts down, its mutators throw. Prefer child-mode
/// <c>UseNavigation&lt;TRoute&gt;()</c> (no initial value) in a descendant to obtain
/// the same handle via context, or pass it through <c>Context</c> explicitly.
/// </para>
/// <para>
/// Detection is a pure symbol gate over <see cref="SymbolKind.Field"/> and
/// <see cref="SymbolKind.Property"/>: any <c>static</c> field or property typed
/// <c>NavigationHandle&lt;&gt;</c> (a static auto-property is the same static-lifetime
/// leak as a field). The handle's constructor is <c>internal</c>, so the only way
/// consumer code can obtain one is <c>UseNavigation</c> — which makes "static
/// <c>NavigationHandle&lt;&gt;</c> storage" equivalent to the spec's "assigned from
/// <c>UseNavigation</c>", and robustly covers the canonical form above where the value
/// flows through an intermediate local. No code-fix (the correct rewrite depends on how
/// the handle is consumed elsewhere).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticNavigationHandleAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_NAV_001";

    private const string NavigationHandleTypeName = "NavigationHandle";
    private const string NavigationNamespace = "Microsoft.UI.Reactor.Navigation";

    private static readonly LocalizableString Title =
        "UseNavigation handle stored in a static field or property";
    private static readonly LocalizableString MessageFormat =
        "Static {0} '{1}' holds a UseNavigation handle that outlives the page and pins its " +
        "dispatcher; access it from a descendant with UseNavigation<TRoute>() or pass it through Context";
    private static readonly LocalizableString Description =
        "A NavigationHandle<TRoute> is bound to the dispatcher of the component that created it. " +
        "Stashing it in static state (a static field or property) keeps it — and its dispatcher — " +
        "alive past the page's lifetime (a leak); after that dispatcher shuts down the handle's " +
        "mutators throw. Access the shared handle from a descendant with child-mode " +
        "UseNavigation<TRoute>() (no initial value), or pass it through Context, instead of static state.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Navigation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;

        // Skip compiler-generated fields (auto-property backing fields, closures,
        // enum value fields, etc.) — the author can't act on those declarations.
        // A static auto-property is caught via AnalyzeProperty instead.
        if (field.IsImplicitlyDeclared)
            return;

        ReportIfStaticHandle(context, field, field.Type, field.IsStatic, "field");
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;

        // A static property holding the handle is the same static-lifetime leak as a
        // field. Skip indexers (never static) and compiler-generated properties.
        if (property.IsImplicitlyDeclared || property.IsIndexer)
            return;

        ReportIfStaticHandle(context, property, property.Type, property.IsStatic, "property");
    }

    private static void ReportIfStaticHandle(
        SymbolAnalysisContext context, ISymbol symbol, ITypeSymbol type, bool isStatic, string kind)
    {
        // Only static storage leaks the handle across page lifetimes.
        if (!isStatic)
            return;

        if (!IsNavigationHandle(type))
            return;

        var location = symbol.Locations.FirstOrDefault() ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, kind, symbol.Name));
    }

    private static bool IsNavigationHandle(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return false;

        var definition = named.OriginalDefinition;
        return definition.Arity == 1
            && definition.Name == NavigationHandleTypeName
            && definition.ContainingNamespace?.ToDisplayString() == NavigationNamespace;
    }
}
