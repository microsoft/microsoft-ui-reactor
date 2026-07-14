using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_STATE_001: Detects a <c>Component</c> subclass that also implements
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/>.
/// </summary>
/// <remarks>
/// A common MVVM habit is to implement <c>INotifyPropertyChanged</c> on a view type
/// and raise <c>PropertyChanged</c> for local state. Reactor's render loop never
/// subscribes to a component's INPC, so that state is invisible to the reconciler and
/// the raised change does nothing — no re-render is scheduled. Reactive local state
/// belongs in <c>UseState</c> (or, for an external observable source, <c>UseObservable</c>),
/// not on the component itself. This is a symbol-level, two-condition match (derives
/// from <c>Component</c> <em>and</em> implements INPC), so the false-positive surface is
/// near zero. There is no auto-fix — removing INPC is a structural change the author must
/// make; the message points them at the hook APIs.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComponentInpcAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_STATE_001";

    private const string ComponentMetadataName = "Microsoft.UI.Reactor.Core.Component";
    private const string InpcMetadataName = "System.ComponentModel.INotifyPropertyChanged";

    private static readonly LocalizableString Title =
        "INotifyPropertyChanged on a Component is invisible to the render loop";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is a Component that implements INotifyPropertyChanged; the render loop never " +
        "subscribes to a component's INPC, so PropertyChanged updates do nothing. Hold reactive " +
        "state with UseState (or wrap an external observable with UseObservable) instead.";

    private static readonly LocalizableString Description =
        "A Component subclass that implements INotifyPropertyChanged out of MVVM habit raises " +
        "PropertyChanged for local state, exactly as a view-model would. The framework never " +
        "subscribes to a component's INPC, so the value is invisible to the render loop and the " +
        "update never re-renders. Use UseState for reactive local state, or UseObservable to " +
        "subscribe an external INotifyPropertyChanged source, rather than implementing INPC on " +
        "the component.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.State",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // Resolve the anchor symbols once. If Reactor (or INPC) isn't referenced there is
        // nothing this rule can match, so we register no per-symbol callback at all.
        var componentType = context.Compilation.GetTypeByMetadataName(ComponentMetadataName);
        var inpcType = context.Compilation.GetTypeByMetadataName(InpcMetadataName);
        if (componentType is null || inpcType is null)
            return;

        context.RegisterSymbolAction(
            symbolContext => AnalyzeNamedType(symbolContext, componentType, inpcType),
            SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol componentType,
        INamedTypeSymbol inpcType)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Condition 1: a class that derives from Reactor's Component base
        // (covers both Component and the generic Component<TProps>).
        if (type.TypeKind != TypeKind.Class)
            return;
        if (!DerivesFrom(type, componentType))
            return;

        // Condition 2: the type implements System.ComponentModel.INotifyPropertyChanged.
        if (!ImplementsInterface(type, inpcType))
            return;

        // Report only where INPC is introduced. If the immediate base type is declared in
        // source and also implements INPC, that base is the mistake site and is flagged on
        // its own, so we skip this derived type to avoid a duplicate cascade. When the base
        // comes from metadata (a referenced assembly) it is not analyzed here — so we still
        // flag the derived source type, otherwise the anti-pattern would produce no warning.
        if (type.BaseType is INamedTypeSymbol baseType
            && baseType.Locations.Any(loc => loc.IsInSource)
            && ImplementsInterface(baseType, inpcType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            type.Locations.FirstOrDefault() ?? Location.None,
            type.Name));
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseCandidate)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseCandidate))
                return true;
        }
        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, interfaceType))
                return true;
        }
        return false;
    }
}
