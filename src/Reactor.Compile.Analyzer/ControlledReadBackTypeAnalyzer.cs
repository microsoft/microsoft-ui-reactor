using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Compile.Analyzer;

/// <summary>
/// REACTOR1003 — validates that
/// <c>Prop.Controlled(getValue, set, readBack)</c>'s <c>readBack</c>
/// return type matches the value type accepted by the <c>set</c> lambda.
///
/// <para>
/// <strong>Status (Phase 1):</strong> <em>provisional</em>. Same status as
/// <see cref="StringEventReferenceAnalyzer"/> — the <c>Prop.Controlled</c>
/// descriptor lands in Phase 2 with the §6 descriptor model. The ID is
/// reserved here, but the rule body is a no-op until then.
/// </para>
///
/// <para>
/// <strong>TODO (Phase 2):</strong> activate by recognizing invocations of
/// <c>Microsoft.UI.Reactor.Core.V1Protocol.Prop.Controlled</c>, walking
/// the argument list to bind <c>set</c> (an <c>Action&lt;TControl,TValue&gt;</c>
/// lambda) and <c>readBack</c> (a <c>Func&lt;TControl,TValue&gt;</c> lambda),
/// pulling <c>TValue</c> from each via Roslyn's symbol model, and reporting
/// <see cref="ReactorCompileDiagnostics.ControlledReadBackType"/> at the
/// invocation when the two <c>TValue</c>s differ.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControlledReadBackTypeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ReactorCompileDiagnostics.ControlledReadBackType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        // Phase 1: no-op. Activation point documented above.
        _ = ctx;
        return;
    }
}
