using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_DOC_001</c> — every <see cref="Accessibility.Public"/> type or
/// member must carry an XML doc <c>&lt;summary&gt;</c>. Drives spec 041
/// §10.4 reference generation: a missing summary is a missing reference
/// page (or a placeholder one). The analyzer checks for the presence of
/// <c>&lt;summary&gt;</c> on the declaration's documentation comment;
/// overrides inherit and are skipped, as is anything generated.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class XmlDocSummaryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DOC_001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Public API missing XML doc summary",
        "Public {0} '{1}' has no <summary> XML doc",
        "Reactor.Documentation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Spec 041 §7.1.2 builds the auto-generated reference docset from " +
            "XML doc <summary> tags. Public API without a summary emits a " +
            "placeholder page and warns at build time. Once Phase 4 elevates " +
            "the severity to Error, the build will block on missing summaries.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol,
            SymbolKind.NamedType,
            SymbolKind.Method,
            SymbolKind.Property,
            SymbolKind.Field,
            SymbolKind.Event);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;

        // Only public API surface — spec §10.4 generates reference pages for
        // public members only.
        if (symbol.DeclaredAccessibility != Accessibility.Public) return;

        // Property/event accessors inherit XML doc from the parent; only
        // check the parent member itself.
        if (symbol is IMethodSymbol method &&
            method.AssociatedSymbol is IPropertySymbol or IEventSymbol)
            return;

        // Compiler-generated members (e.g. records' default ctors, anonymous
        // type members). The author can't attach doc to these.
        if (symbol.IsImplicitlyDeclared) return;

        // Overrides inherit summary text from the base by Roslyn convention;
        // don't fail the override.
        if (symbol is IMethodSymbol m2 && m2.IsOverride) return;
        if (symbol is IPropertySymbol p2 && p2.IsOverride) return;
        if (symbol is IEventSymbol e2 && e2.IsOverride) return;

        // Skip explicit interface implementations — the interface declaration
        // already carries the doc.
        if (symbol is IMethodSymbol m3 && m3.ExplicitInterfaceImplementations.Length > 0) return;
        if (symbol is IPropertySymbol p3 && p3.ExplicitInterfaceImplementations.Length > 0) return;
        if (symbol is IEventSymbol e3 && e3.ExplicitInterfaceImplementations.Length > 0) return;

        // Skip [GeneratedCode] attributed types and members.
        if (HasGeneratedCodeAttribute(symbol)) return;

        // For types, also skip nested types when the parent is non-public
        // (Roslyn already filtered by DeclaredAccessibility, but be defensive).
        if (symbol is INamedTypeSymbol nt && nt.ContainingType is { DeclaredAccessibility: not Accessibility.Public })
            return;

        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: context.CancellationToken);
        if (HasSummary(xml)) return;

        // Pick the first declaration location as the diagnostic anchor.
        var location = symbol.Locations.FirstOrDefault() ?? Location.None;
        var kind = KindLabel(symbol);
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, kind, symbol.ToDisplayString()));
    }

    private static bool HasGeneratedCodeAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString() ?? string.Empty;
            if (name.EndsWith(".GeneratedCodeAttribute", StringComparison.Ordinal)) return true;
            if (name == "System.CodeDom.Compiler.GeneratedCodeAttribute") return true;
        }
        return symbol.ContainingType is { } ct && HasGeneratedCodeAttribute(ct);
    }

    private static bool HasSummary(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return false;
        // Cheap string check — the surrounding compiler emits a `<member>`
        // wrapper but the body uses `<summary>...</summary>`.
        return xml!.IndexOf("<summary>", StringComparison.Ordinal) >= 0;
    }

    private static string KindLabel(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Class } => "class",
        INamedTypeSymbol { TypeKind: TypeKind.Struct } => "struct",
        INamedTypeSymbol { TypeKind: TypeKind.Interface } => "interface",
        INamedTypeSymbol { TypeKind: TypeKind.Enum } => "enum",
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } => "delegate",
        INamedTypeSymbol => "type",
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        _ => "member",
    };
}
