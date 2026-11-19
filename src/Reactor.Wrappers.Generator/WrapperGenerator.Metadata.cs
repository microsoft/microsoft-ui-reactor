using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Microsoft.UI.Reactor.Wrappers.Generator;

public sealed partial class WrapperGenerator
{
    private static readonly DiagnosticDescriptor UnsupportedMetadataRegistration = new(
        id: "REACTORGEN016",
        title: "XAML metadata provider requires explicit registration",
        messageFormat: "Assembly '{0}' has XAML metadata providers but no unique publicly constructible provider. Automatic discovery is not trimming/NativeAOT-safe. Set RegisterAssembly = false and register the intended provider instance with ReactorApp.RegisterControlAssembly.",
        category: "Reactor.Wrappers",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static (string? TypeName, bool Unsupported) FindMetadataProvider(
        IAssemblySymbol assembly, Compilation compilation)
    {
        var contract = compilation.GetTypeByMetadataName("Microsoft.UI.Xaml.Markup.IXamlMetadataProvider");
        if (contract is null) return default;

        var providers = EnumerateTypes(assembly.GlobalNamespace)
            .Where(t => t.TypeKind == TypeKind.Class && !t.IsAbstract &&
                t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, contract)) &&
                !IsApplication(t))
            .ToArray();
        if (providers.Length == 0) return default;

        // Do not guess between custom providers or pick one merely because the others
        // are inaccessible. The explicit instance overload remains the escape hatch.
        if (providers.Length != 1 || !IsPublicNonGeneric(providers[0]) ||
            !providers[0].InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0))
            return (null, true);

        var provider = providers[0];
        var ctor = provider.InstanceConstructors.Single(c => c.Parameters.Length == 0);
        if (!ctor.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
        {
            for (var type = provider; type is not null; type = type.BaseType)
                if (type.GetMembers().Any(m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
                    return (null, true);
        }

        return (provider.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), false);
    }

    private static bool IsPublicNonGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public || current.Arity != 0)
                return false;
        return true;
    }

    private static bool IsApplication(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "Microsoft.UI.Xaml.Application")
                return true;
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol scope)
    {
        foreach (var member in scope.GetMembers())
        {
            if (member is INamedTypeSymbol type)
                yield return type;
            if (member is INamespaceOrTypeSymbol nested)
                foreach (var child in EnumerateTypes(nested))
                    yield return child;
        }
    }
}
