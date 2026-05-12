// Spec 038 §3.1a — per-rule perf bound. The §3.1a contract is
// "symbol-resolution adds ≤ 0.5 ms median per rule per diagnostic, captured
// in the perf-trait suite." This test was deferred until the first rule
// landed (you can't measure a delta against zero rules); three Class-B rules
// have shipped, so it's now load-bearing.
//
// What the test measures: the cost of a single RuleRegistry.BestMatch call on
// the canonical Phase-3 fixture (CS1061 on ButtonElement.OnClick). The
// per-diagnostic budget is the spec budget × rule count, so the assertion
// scales automatically as the rule set grows. Failure mode the test is
// designed to catch: a rule (or the resolver cache) regressing so that
// per-rule per-diagnostic work goes superlinear with rule count.
//
// Tagged [Trait("Category", "Perf")] so it's filtered out of the default
// `dotnet test` run — same pattern as CompilationLoaderTests' cold/warm load
// perf assertion. Run via `dotnet test --filter Category=Perf`.

using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Cli.Check;
using Microsoft.UI.Reactor.Cli.Check.Rules;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests.Rules;

public class RulePerformanceTests
{
    // Combined stub: every DeclaredTarget across ALL six rules in
    // RuleRegistry.Default — three Class-B (ThemeBackgroundSuffixRule,
    // AlignmentShortcutRule, ButtonOnClickFactoryMoveRule) plus three
    // Class-A (GridSizeFactoryParensRule, GridSizePxRenameRule,
    // TextBlockStyleHintRule). All six must pass RuleRegistry.TargetsResolve
    // and reach TryMatch; if any self-disables, the perf budget assertion
    // measures a smaller set than RuleRegistry.Default.All.Length and the
    // scaling becomes misleading (Copilot CR feedback on this file).
    //
    // The diagnostic is shaped so exactly one rule
    // (ButtonOnClickFactoryMoveRule) matches — the other five short-circuit
    // early inside TryMatch (different code, different receiver, different
    // missing-member name). That mix is the representative cost shape:
    // every rule pays the symbol-resolution gate, most rules then bail
    // before doing rule-specific work.
    const string CombinedStub = @"
namespace Microsoft.UI.Reactor.Core
{
    public static class Theme
    {
        public static object SolidBackground => null!;
    }
    public abstract record Element {}
    public sealed record ButtonElement(string Label = """") : Element;
    public sealed record TextBlockElement(string Text = """") : Element;
}
namespace Microsoft.UI.Reactor
{
    using System;
    using Microsoft.UI.Reactor.Core;
    public readonly record struct GridSize(double Value, int Type)
    {
        public static GridSize Auto { get; } = new(1, 0);
        public static GridSize Star(double weight = 1) => new(weight, 1);
        public static GridSize Px(double pixels) => new(pixels, 2);
    }
    public static class ElementExtensions
    {
        public static T HAlign<T>(this T el, int alignment) where T : Element => el;
        public static T VAlign<T>(this T el, int alignment) where T : Element => el;
    }
    public static class Factories
    {
        public static ButtonElement Button(string label, Action? onClick = null) => new(label);
    }
}";

    const string TestSource = @"
using Microsoft.UI.Reactor.Core;
class Test {
    void M() {
        var b = new ButtonElement(""x"");
        var x = b.OnClick;
    }
}";

    [Fact]
    [Trait("Category", "Perf")]
    public void BestMatch_median_under_per_rule_budget()
    {
        var (registry, context) = BuildHotContext();

        // Stub-coverage guard: every rule in the production registry must
        // resolve its declared targets against CombinedStub. Without this
        // check the perf budget silently degrades when someone adds a new
        // rule whose targets the stub doesn't carry — the rule self-
        // disables, the loop iterates over a smaller effective set than
        // `registry.All.Length`, and the budget scaling lies.
        // (Copilot CR feedback on this file, addressed.)
        foreach (var rule in registry.All)
        {
            foreach (var target in rule.DeclaredTargets)
            {
                Assert.True(context.Resolver.ResolveType(target) is not null,
                    $"Stub missing target '{target}' for rule {rule.Name}. " +
                    $"Add the type to CombinedStub so the perf budget covers all rules.");
            }
        }

        // Warm: prime the RuleSymbolResolver caches and the JIT. Without the
        // warm-up the first BestMatch eats the cold-cache cost on every
        // ResolveType plus first-tier JIT work, and the median is undefined
        // on n < 10. 200 iters comfortably exceeds JIT tier-1 → tier-2
        // promotion (typically ~30 calls) on hot paths.
        for (int i = 0; i < 200; i++)
            registry.BestMatch(in context);

        // Measure: 1000 iters give a stable median. Use a single Stopwatch
        // across all iters to keep per-sample overhead negligible compared to
        // start/stop ticks — the loop body itself is the cost, not the timer.
        const int iters = 1000;
        var samples = new long[iters];
        var sw = new Stopwatch();
        for (int i = 0; i < iters; i++)
        {
            sw.Restart();
            registry.BestMatch(in context);
            sw.Stop();
            samples[i] = sw.ElapsedTicks;
        }

        Array.Sort(samples);
        var medianTicks = samples[iters / 2];
        var medianMs = medianTicks * 1000.0 / Stopwatch.Frequency;

        // Spec budget: ≤ 0.5 ms median per rule per diagnostic. Total budget
        // per BestMatch call = 0.5 × ruleCount ms. Apply a 4× CI noise
        // allowance — same slack as CompilationLoaderTests' cold/warm
        // assertions for similar reasons (Windows Defender, shared-runner
        // CPU contention, GC tail latency).
        var ruleCount = registry.All.Length;
        var budgetMs = 0.5 * ruleCount;
        var allowance = 4.0;
        Assert.True(medianMs <= budgetMs * allowance,
            $"BestMatch median {medianMs:F3} ms across {ruleCount} rule(s) " +
            $"exceeds {budgetMs:F3} ms × {allowance:F0}× CI slack " +
            $"= {budgetMs * allowance:F3} ms. Per-rule median: " +
            $"{medianMs / ruleCount:F3} ms (spec budget 0.5 ms).");
    }

    static (RuleRegistry registry, RuleContext context) BuildHotContext()
    {
        var compilation = TestCompilation.Create(new[]
        {
            (CombinedStub, "Stub.cs"),
            (TestSource, "Test.cs"),
        });

        var roslynDiag = compilation.GetDiagnostics().First(d => d.Id == "CS1061");
        var span = roslynDiag.Location.SourceSpan;
        var tree = roslynDiag.Location.SourceTree!;
        var root = tree.GetRoot();
        var node = SuggesterOrchestrator.PickRelevantNode(root, span)!;
        var sm = compilation.GetSemanticModel(tree);
        var receiver = SuggesterOrchestrator.ResolveReceiver(sm, node);
        var resolver = RuleSymbolResolver.For(compilation);

        // The diagnostic input shape mirrors what SuggesterOrchestrator
        // builds before handing off to BestMatch in production (synthetic
        // Diagnostic carrying the same code).
        var descriptor = new DiagnosticDescriptor(
            id: "CS1061",
            title: "CS1061",
            messageFormat: "{0}",
            category: "compiler",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        var diag = Diagnostic.Create(descriptor, Location.None, roslynDiag.GetMessage());

        var context = new RuleContext(node, diag, receiver, sm, compilation, resolver);
        return (RuleRegistry.Default, context);
    }
}
