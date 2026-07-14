using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="DuplicateAtomicModifierAnalyzer"/> (<c>REACTOR_MOD_001</c>)
/// and its <see cref="DuplicateAtomicModifierCodeFix"/>. Stubs the minimum
/// Reactor surface — the four atomic-replace placement modifiers, one additive
/// modifier, one unrelated generic modifier, and an unrelated non-Reactor type —
/// so both the syntactic detection and the semantic confirmation exercise real
/// binding without pulling the framework in.
/// </summary>
public class DuplicateAtomicModifierAnalyzerTests
{
    // Extension classes live in `Microsoft.UI.Reactor` (matching source) so the
    // analyzer's containing-type/namespace confirmation binds; Element lives in
    // `Microsoft.UI.Reactor.Core`.
    private const string Stubs = @"
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element { }
    public sealed record TextBlockElement(string Text) : Element { }

    public static class Factories
    {
        public static TextBlockElement Text(string s) => new(s);
    }
}

namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;

    public static class GridExtensions
    {
        public static T Grid<T>(this T el, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : Element => el;
    }

    public static class CanvasExtensions
    {
        public static T Canvas<T>(this T el, double left = 0, double top = 0) where T : Element => el;
        public static T Canvas<T>(this T el, double left, double top, double anchorX, double anchorY) where T : Element => el;
    }

    public static class RelativePanelExtensions
    {
        public static T RelativePanel<T>(this T el, string name, string alignTopWith = """", string below = """") where T : Element => el;
    }

    public static class FlexExtensions
    {
        public static T Flex<T>(this T el, double grow = 0, double shrink = 1) where T : Element => el;
    }

    // Additive modifier (accumulates) — must NOT be flagged.
    public static class ValidateExtensions
    {
        public static T Validate<T>(this T el, string fieldName) where T : Element => el;
    }

    // Unrelated generic modifier — different name, must NOT be flagged.
    public static class MarginExtensions
    {
        public static T Margin<T>(this T el, double all = 0) where T : Element => el;
    }
}

namespace Other
{
    // Unrelated type that happens to expose a fluent `Grid` — the semantic gate
    // must reject it (not a Reactor extension).
    public sealed class Widget
    {
        public Widget Grid(int row = 0, int column = 0) => this;
    }
}
";

    private static string App(string body) => Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public static class C
    {
        internal static int Val() => 1;
        internal static int Fld = 1;
        internal static int Prop => 1;
" + body + @"
    }
}";

    // ── Positive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Fires_On_Duplicate_Grid_In_One_Chain()
    {
        var source = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: 1).Grid(column: 2)|};");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Duplicate_Canvas_And_Flex()
    {
        var source = App(@"
        public static Element A()
            => {|REACTOR_MOD_001:Text(""hi"").Canvas(left: 10).Canvas(top: 20)|};
        public static Element B()
            => {|REACTOR_MOD_001:Text(""hi"").Flex(grow: 1).Flex(shrink: 0)|};");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Three_Occurrences_Once()
    {
        // Non-adjacent + three occurrences: still exactly one diagnostic, anchored
        // at the outermost `.Grid`.
        var source = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: 1).Margin(4).Grid(column: 2).Grid(rowSpan: 3)|};");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative ────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_On_Single_Grid()
    {
        var source = App(@"
        public static Element Build()
            => Text(""hi"").Grid(row: 1, column: 2);");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_On_Two_Different_Modifiers()
    {
        var source = App(@"
        public static Element Build()
            => Text(""hi"").Grid(row: 1).Margin(8);");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_On_Repeated_Additive_Modifier()
    {
        // `.Validate(...)` accumulates (reads + merges the existing attached
        // value), so repeating it is legitimate — must not be flagged.
        var source = App(@"
        public static Element Build()
            => Text(""hi"").Validate(""first"").Validate(""second"");");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss (trips the syntactic fast path, rejected by a later gate) ──

    [Fact]
    public async Task No_Diagnostic_When_Grids_Are_On_Separate_Chains()
    {
        // The name `Grid` appears twice in the file, but on two different
        // elements — never twice in one linear chain.
        var source = App(@"
        public static Element A() => Text(""a"").Grid(row: 1);
        public static Element B() => Text(""b"").Grid(column: 2);");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_On_NonReactor_Grid_Method()
    {
        // Same syntactic shape (`.Grid(...).Grid(...)`) but the receiver is an
        // unrelated type — the semantic confirmation must reject it.
        var source = App(@"
        public static object Build()
            => new global::Other.Widget().Grid(1).Grid(2);");

        await new CSharpAnalyzerTest<DuplicateAtomicModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code-fix round-trips ────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Merges_Named_Grid_Calls()
    {
        var before = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: 1).Grid(column: 2)|};");

        var after = App(@"
        public static Element Build()
            => Text(""hi"").Grid(row: 1, column: 2);");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{DuplicateAtomicModifierAnalyzer.DiagnosticId}_Merge",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Merges_Positional_And_Named_With_Later_Wins()
    {
        // First call sets row positionally + column; second call overrides column.
        // Expect the later value for column and row preserved, all emitted named.
        var before = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(1, column: 5).Grid(column: 2)|};");

        var after = App(@"
        public static Element Build()
            => Text(""hi"").Grid(row: 1, column: 2);");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{DuplicateAtomicModifierAnalyzer.DiagnosticId}_Merge",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Merges_Three_Adjacent_Grid_Calls()
    {
        var before = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: 1).Grid(column: 2).Grid(rowSpan: 3)|};");

        var after = App(@"
        public static Element Build()
            => Text(""hi"").Grid(row: 1, column: 2, rowSpan: 3);");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{DuplicateAtomicModifierAnalyzer.DiagnosticId}_Merge",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Merges_Duplicate_RelativePanel_Calls()
    {
        // Required positional/name parameter present in both calls; later wins for
        // shared params, distinct optional placement params preserved.
        var before = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").RelativePanel(name: ""a"", alignTopWith: ""x"").RelativePanel(name: ""a"", below: ""y"")|};");

        var after = App(@"
        public static Element Build()
            => Text(""hi"").RelativePanel(name: ""a"", alignTopWith: ""x"", below: ""y"");");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{DuplicateAtomicModifierAnalyzer.DiagnosticId}_Merge",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Withheld_For_NonAdjacent_Duplicates()
    {
        // The two `.Grid` calls are separated by `.Margin(4)`. Merging would move an
        // argument across the intervening call, so the diagnostic fires but no fix
        // is offered (FixedCode == TestCode).
        var source = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: 1).Margin(4).Grid(column: 2)|};");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Merges_When_Argument_Is_Field()
    {
        // A field read is side-effect-free, so the fix still applies.
        var before = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: Fld).Grid(column: 2)|};");

        var after = App(@"
        public static Element Build()
            => Text(""hi"").Grid(row: Fld, column: 2);");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{DuplicateAtomicModifierAnalyzer.DiagnosticId}_Merge",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Withheld_When_Argument_Is_Property()
    {
        // `Prop` is a property whose getter could run user code; the later call
        // overrides `row`, so a naive merge would drop that getter call. Withhold.
        var source = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: Prop).Grid(row: 2)|};");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Withheld_When_Argument_Has_Side_Effect()
    {
        // `Val()` is dropped by a naive merge (later call overrides `row`); its call
        // could have side effects, so the fix is withheld (diagnostic still fires).
        var source = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: Val()).Grid(row: 2)|};");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Withheld_When_Argument_Has_Comment()
    {
        // The merge rebuilds arguments without trivia, which would delete the
        // comment — withhold rather than silently drop it.
        var source = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Grid(row: 1).Grid(column: /* keep me */ 2)|};");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Withheld_Across_Different_Canvas_Overloads()
    {
        // The two `.Canvas` calls bind to different overloads (2-arg vs 4-arg);
        // a single merged call can't be produced safely, so the diagnostic fires
        // but no fix is offered (FixedCode == TestCode).
        var source = App(@"
        public static Element Build()
            => {|REACTOR_MOD_001:Text(""hi"").Canvas(10, 20).Canvas(30, 40, 0.5, 0.5)|};");

        await new CSharpCodeFixTest<DuplicateAtomicModifierAnalyzer, DuplicateAtomicModifierCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
