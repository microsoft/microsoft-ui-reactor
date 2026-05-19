using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_DOC_002</c> — any <c>cref="..."</c> attribute in an XML doc
/// comment must resolve to a real symbol. Roslyn already emits the CS1574
/// warning for unresolved crefs; this analyzer mirrors that finding under
/// a Reactor-specific code so the build can elevate it without touching
/// the global compiler warning level. Spec 041 §10.4 requires every cref
/// to round-trip through the reference generator, so an unresolved cref
/// is a documentation-correctness failure.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class XmlDocCrefAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DOC_002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "XML doc cref does not resolve",
        "XML doc cref '{0}' does not resolve to a known symbol",
        "Reactor.Documentation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Spec 041 §10.4 routes every <see cref=\"...\"/> and <seealso " +
            "cref=\"...\"/> through the reference generator to produce a " +
            "relative MD link. An unresolved cref means the link will be " +
            "missing on the generated page. Roslyn raises CS1574 for the " +
            "same condition; this analyzer surfaces it under a Reactor code " +
            "so doc PRs can elevate severity independently.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCref, SyntaxKind.NameMemberCref, SyntaxKind.QualifiedCref);
    }

    private static void AnalyzeCref(SyntaxNodeAnalysisContext context)
    {
        var node = (CrefSyntax)context.Node;
        // Only check top-level crefs — nested NameCref/QualifiedCref nodes
        // appear as children of the same cref expression and would otherwise
        // raise duplicate diagnostics.
        if (node.Parent is CrefSyntax) return;

        var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol;
        if (symbol is not null) return;

        // Roslyn returned no symbol — the cref is unresolved. Report on the
        // cref's location so authors can click directly to the offending
        // string.
        var text = node.ToString();
        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), text));
    }
}
