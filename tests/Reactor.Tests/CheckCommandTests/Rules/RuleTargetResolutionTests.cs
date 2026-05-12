// Phase 3.1a CI gate — rule-target resolution. Spec 038 §3.1a.
//
// This test is the load-bearing assertion that protects the rule set against
// silent breakage on a Reactor minor-version churn. Every rule declares the
// fully-qualified type names it binds to via IRulePattern.DeclaredTargets;
// this test constructs a Compilation that references the LIVE Reactor
// assembly (unlike TestCompilation.Create which deliberately excludes it),
// then asserts that every declared target on every registered rule resolves
// to a real ISymbol.
//
// When this fails, the rule's owner picks: (a) update the rule's target
// binding via RuleSymbolResolver, or (b) retire the rule. No silent
// string-swap. (§3.1a API-churn protocol.)
//
// Until Phase-3 rules start landing, this test passes vacuously (no rules
// registered ⇒ no targets to resolve). That's intentional — the test exists
// so the first rule PR doesn't have to ship the assertion alongside the
// rule.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class RuleTargetResolutionTests
{
    [Fact]
    public void Every_registered_rule_resolves_its_declared_targets_against_live_Reactor_compilation()
    {
        var compilation = BuildLiveReactorCompilation();
        var resolver = RuleSymbolResolver.For(compilation);

        var failures = new List<string>();
        foreach (var rule in RuleRegistry.Default.All)
        {
            foreach (var target in rule.DeclaredTargets)
            {
                if (resolver.ResolveType(target) is null)
                    failures.Add($"{rule.Name} → {target}");
            }
        }

        Assert.True(failures.Count == 0,
            "Rule(s) declared targets that did not resolve against the live Reactor compilation. " +
            "Per spec 038 §3.1a, either fix the rule's target binding via RuleSymbolResolver or retire the rule. " +
            "Failures: " + string.Join("; ", failures));
    }

    /// <summary>
    /// Builds a CSharpCompilation that references every currently-loaded
    /// assembly, INCLUDING Reactor / WinUI (the inverse of
    /// TestCompilation.Create). This is the compilation a rule's resolver
    /// would see in production, so target resolution here is the
    /// authoritative gate.
    /// </summary>
    static CSharpCompilation BuildLiveReactorCompilation()
    {
        // Force Reactor.dll into the AppDomain BEFORE enumerating loaded
        // assemblies — otherwise xunit's lazy assembly loading can leave
        // Reactor absent from the enumeration on a cold run, producing
        // false target-resolution failures. Any public type from Reactor's
        // main assembly works as the load anchor; AsyncValue is small,
        // dependency-free, and unlikely to be retired.
        _ = typeof(Microsoft.UI.Reactor.Core.AsyncValue<int>).Assembly;

        var refs = new List<MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            string? loc;
            try { loc = asm.Location; } catch { continue; }
            if (string.IsNullOrEmpty(loc)) continue;
            try { refs.Add(MetadataReference.CreateFromFile(loc)); }
            catch { /* skip unreadable */ }
        }

        return CSharpCompilation.Create(
            "RuleTargetResolutionProbe",
            new[] { CSharpSyntaxTree.ParseText("// probe") },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
