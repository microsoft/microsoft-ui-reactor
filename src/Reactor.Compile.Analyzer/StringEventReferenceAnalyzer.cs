using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Compile.Analyzer;

/// <summary>
/// REACTOR1001 — validates string-form event references in Reactor
/// descriptors (e.g., <c>Prop.Controlled(..., changeEvent: "Toggled")</c>)
/// against the control type carried by the descriptor's generic parameter.
///
/// <para>
/// <strong>Status (Phase 1):</strong> <em>provisional</em>. Spec 047 §6
/// descriptor model ships in Phase 2; the Phase 1 protocol surface
/// (<c>ReactorBinding&lt;T&gt;.OnCustomEvent</c>) takes strongly-typed
/// lambdas with no string event name, so there is no concrete source
/// pattern to match against today. The diagnostic descriptor and a
/// no-op syntax-node registration ship here so:
/// <list type="number">
///   <item>the ID (<c>REACTOR1001</c>) is reserved against the analyzer
///         release tracking;</item>
///   <item>downstream tooling and docs can refer to it stably;</item>
///   <item>activation in Phase 2 is a body change, not a new rule.</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>TODO (Phase 2):</strong> activate the rule body by recognizing
/// invocations of <c>Microsoft.UI.Reactor.Core.V1Protocol.Prop.Controlled</c>
/// (or its sibling descriptor factories) whose <c>changeEvent</c> named
/// argument is a string literal, resolving the symbol on the descriptor's
/// generic control type, and reporting <see cref="ReactorCompileDiagnostics.StringEventReference"/>
/// at the string literal's location when the event isn't found.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringEventReferenceAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ReactorCompileDiagnostics.StringEventReference);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Narrowest practical hook — only invocation expressions. Once the
        // Phase 2 descriptor lands, this is where the rule body activates.
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        // Phase 1: no-op. Activation point is documented above. We bail
        // unconditionally rather than partially resolving symbols — that
        // keeps the analyzer warm-up cost negligible until Phase 2.
        _ = ctx;
        return;
    }
}
