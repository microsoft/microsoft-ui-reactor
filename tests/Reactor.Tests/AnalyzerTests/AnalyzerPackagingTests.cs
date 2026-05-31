using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Regression guard for the analyzer-packaging boundary.
///
/// <para>
/// <c>Reactor.Analyzers.dll</c> is bundled into <c>Microsoft.UI.Reactor.nupkg</c>
/// (see <c>src/Reactor/Reactor.csproj</c>) and therefore runs on every
/// consumer's build. <c>Reactor.Analyzers.Internal.dll</c> hosts the
/// <c>REACTOR_DOC_*</c> rules that only make sense on the framework's own
/// public-API XML doc (spec 041 §10.4) and is deliberately NOT packed. The
/// asymmetry is intentional and easily broken: if someone moves
/// <c>XmlDocSummaryAnalyzer</c> or <c>XmlDocCrefAnalyzer</c> back into
/// <c>Reactor.Analyzers</c>, every customer build starts warning about
/// missing summaries on their own types.
/// </para>
///
/// <para>
/// This test reflects over the shipped <c>Reactor.Analyzers.dll</c>,
/// instantiates every <see cref="DiagnosticAnalyzer"/> it exports, and
/// asserts none of them advertise a <c>REACTOR_DOC_*</c> diagnostic ID.
/// </para>
/// </summary>
public class AnalyzerPackagingTests
{
    [Fact]
    public void ShippedAnalyzerAssembly_DoesNotExport_DocCoverageRules()
    {
        // Pick any concrete analyzer that lives in the shipped assembly and
        // use its assembly handle. MissingWithKeyAnalyzer is one of the
        // canonical consumer-facing rules; if it ever moves the test will
        // fail compilation, which is the right signal.
        var shipped = typeof(MissingWithKeyAnalyzer).Assembly;
        Assert.Equal("Reactor.Analyzers", shipped.GetName().Name);

        var leakedIds = shipped
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .SelectMany(a => a.SupportedDiagnostics)
            .Select(d => d.Id)
            .Where(id => id.StartsWith("REACTOR_DOC_", StringComparison.Ordinal))
            .OrderBy(id => id)
            .ToArray();

        Assert.True(
            leakedIds.Length == 0,
            "REACTOR_DOC_* rules must not ship in Reactor.Analyzers.dll — they " +
            "would fire on every consumer's public API and produce build-time " +
            "noise. Move them to Reactor.Analyzers.Internal (not packed into the " +
            "nupkg). Leaked IDs: " + string.Join(", ", leakedIds));
    }

    [Fact]
    public void InternalAnalyzerAssembly_Hosts_DocCoverageRules()
    {
        // Symmetric companion: ensure REACTOR_DOC_001 / _002 still exist
        // (and live in the internal-only assembly). Catches accidental
        // deletion of the analyzers themselves.
        var @internal = typeof(XmlDocSummaryAnalyzer).Assembly;
        Assert.Equal("Reactor.Analyzers.Internal", @internal.GetName().Name);

        var docIds = @internal
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .SelectMany(a => a.SupportedDiagnostics)
            .Select(d => d.Id)
            .Where(id => id.StartsWith("REACTOR_DOC_", StringComparison.Ordinal))
            .ToHashSet();

        Assert.Contains("REACTOR_DOC_001", docIds);
        Assert.Contains("REACTOR_DOC_002", docIds);
    }
}
