using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="MissingWithKeyAnalyzer"/> (<c>REACTOR_DSL_001</c>) and
/// its <see cref="MissingWithKeyCodeFix"/>. Stubs the minimum Reactor surface
/// needed so the analyzer's textual heuristic + the codefix's semantic
/// IReactorKeyed / Id / Key lookups fire without pulling the framework in.
/// </summary>
public class MissingWithKeyAnalyzerTests
{
    // `IsExternalInit` is required for `record` types under older runtime
    // metadata — supply a stub so test sources can use records freely.
    private const string Stubs = @"
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Reactor.Core
{
    public interface IReactorKeyed { string Key { get; } }
    public abstract record Element { }
    public sealed record TextBlockElement(string Text) : Element { }

    public static class Factories
    {
        public static Element VStack(params Element[] children) => null!;
        public static Element HStack(params Element[] children) => null!;
        public static Element FlexColumn(params Element[] children) => null!;
        public static Element FlexRow(params Element[] children) => null!;
        public static Element Grid(params Element[] children) => null!;
        public static TextBlockElement TextBlock(string s) => new(s);

        // Reactor's ForEach factory (Dsl.cs) — a static entry point called as a
        // bare identifier via `using static ...Factories`, unlike LINQ Select.
        public static Element ForEach<T>(System.Collections.Generic.IEnumerable<T> items, System.Func<T, Element> render) => null!;
        public static Element ForEach<T>(System.Collections.Generic.IEnumerable<T> items, System.Func<T, int, Element> render) => null!;
    }

    public static class ElementExtensions
    {
        public static T WithKey<T>(this T el, string key) where T : Element => el;
        public static T WithKey<T, TKey>(this T el, TKey item)
            where T : Element where TKey : IReactorKeyed => el;
    }
}
";

    // ── Analyzer-only assertions ───────────────────────────────────────

    [Fact]
    public async Task Fires_On_Select_Into_FlexColumn_Without_WithKey()
    {
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn({|REACTOR_DSL_001:rows.Select(r => TextBlock(r.Text))|}.ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_WithKey_Already_Present()
    {
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey(r.Id)).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_ForEach_Into_FlexColumn_Without_WithKey()
    {
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn({|REACTOR_DSL_001:ForEach(rows, (r, i) => TextBlock(r.Text))|});
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Qualified_Factories_ForEach_Without_WithKey()
    {
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => Factories.FlexColumn({|REACTOR_DSL_001:Factories.ForEach(rows, (r, i) => Factories.TextBlock(r.Text))|});
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_WithKey_Already_Present_On_ForEach()
    {
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(ForEach(rows, (r, i) => TextBlock(r.Text).WithKey(r.Id)));
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Explicitly_Generic_ForEach()
    {
        // `ForEach<T>(...)` is a GenericNameSyntax, not an IdentifierNameSyntax —
        // a shape that slipped past the name extraction until SimpleName started
        // covering SimpleNameSyntax.
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn({|REACTOR_DSL_001:ForEach<Row>(rows, r => TextBlock(r.Text))|});
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_ForEach_Is_Not_Consumed_As_Layout_Children()
    {
        // Same layout-children gate the Select arm uses: a projection that isn't
        // handed to a layout factory is left alone.
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => ForEach(rows, r => TextBlock(r.Text));
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_001_Does_Not_Fire_On_A_NonReactor_Static_ForEach()
    {
        // Why the ForEach arm resolves the symbol instead of trusting syntax.
        // A bare `ForEach(items, lambda)` from someone else's `using static` is
        // syntactically identical to Reactor's factory, sits in a layout-child
        // position, and returns an Element — every syntactic gate passes. Only
        // the namespace check keeps DSL_001 (a Warning, and an error under
        // TreatWarningsAsErrors) off it.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static TestApp.Helpers;

    public record Row(string Id, string Text);

    public static class Helpers
    {
        public static Element ForEach<T>(IEnumerable<T> items, Func<T, Element> render) => null!;
    }

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => Factories.FlexColumn(ForEach(rows, r => Factories.TextBlock(r.Text)));
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_001_Does_Not_Fire_On_A_Custom_Receiver_ForEach()
    {
        // `X.ForEach(items, lambda)` — right argument shape, right return type,
        // right position, wrong receiver. Excluded by the receiver check alone,
        // so this reddens if that check is ever loosened.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class Helpers
    {
        public static Element ForEach<T>(IEnumerable<T> items, Func<T, Element> render) => null!;
    }

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(Helpers.ForEach(rows, r => TextBlock(r.Text)));
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_001_Does_Not_Fire_On_The_Bcl_ForEach_Shapes()
    {
        // `List<T>.ForEach` returns void and `Parallel.ForEach` returns
        // ParallelLoopResult, so neither can occupy a layout-child slot at all —
        // they are excluded by type before any gate runs. Pinned here anyway
        // because DSL_001 and DSL_002 now share one receiver check: this is the
        // DSL_001 half of the pair that DSL_002_Does_Not_Fire_On_Bcl_List_ForEach
        // and DSL_002_Does_Not_Fire_On_Parallel_ForEach hold up from the other side.
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(List<Row> rows)
        {
            rows.ForEach(r => { _ = TextBlock(r.Text); });
            Parallel.ForEach(rows, r => { _ = TextBlock(r.Text); });
            return VStack();
        }
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Select_Goes_To_Plain_List()
    {
        // The result of Select is materialized to List<Element>, not consumed
        // by a layout factory — the analyzer must not fire here.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static IReadOnlyList<Element> Project(IReadOnlyList<Row> rows)
            => rows.Select(r => TextBlock(r.Text)).ToList();
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code-fix offers ─────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Offers_WithKey_Item_When_Type_Is_IReactorKeyed()
    {
        var before = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn({|REACTOR_DSL_001:rows.Select(r => TextBlock(r.Text))|}.ToArray());
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey(r)).ToArray());
    }
}";

        await new CSharpCodeFixTest<MissingWithKeyAnalyzer, MissingWithKeyCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{MissingWithKeyAnalyzer.Id}_WithKey_Item",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Offers_WithKey_ItemId_When_Type_Has_Id_Property()
    {
        var before = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn({|REACTOR_DSL_001:rows.Select(r => TextBlock(r.Text))|}.ToArray());
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey(r.Id)).ToArray());
    }
}";

        await new CSharpCodeFixTest<MissingWithKeyAnalyzer, MissingWithKeyCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{MissingWithKeyAnalyzer.Id}_WithKey_Item_Id",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Offers_WithKey_Item_On_ForEach()
    {
        var before = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn({|REACTOR_DSL_001:ForEach(rows, (r, i) => TextBlock(r.Text))|});
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(ForEach(rows, (r, i) => TextBlock(r.Text).WithKey(r)));
    }
}";

        await new CSharpCodeFixTest<MissingWithKeyAnalyzer, MissingWithKeyCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{MissingWithKeyAnalyzer.Id}_WithKey_Item",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Offers_WithKey_ItemKey_When_Type_Has_Key_Property()
    {
        // A type with a public `Key` property but not implementing
        // IReactorKeyed — codefix should still discover the property.
        var before = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Key, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn({|REACTOR_DSL_001:rows.Select(r => TextBlock(r.Text))|}.ToArray());
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Key, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey(r.Key)).ToArray());
    }
}";

        await new CSharpCodeFixTest<MissingWithKeyAnalyzer, MissingWithKeyCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = $"{MissingWithKeyAnalyzer.Id}_WithKey_Item_Key",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── REACTOR_DSL_002 — present-but-non-stable key ────────────────────

    [Fact]
    public async Task DSL_002_Fires_On_Select_Index_Key()
    {
        // Shape 1: the key is the Select index parameter (`i`), never the item.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select((r, i) => TextBlock(r.Text).WithKey({|REACTOR_DSL_002:i.ToString()|})).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Fires_On_ForEach_Index_Key()
    {
        // Shape 1 via the static `ForEach` factory (a bare identifier call, not
        // a member-access like LINQ Select).
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => ForEach(rows, (r, i) => TextBlock(r.Text).WithKey({|REACTOR_DSL_002:i.ToString()|}));
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Fires_On_GuidNewGuid_Key()
    {
        // Shape 2: a per-render-random key. Single-param lambda — no index needed.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey({|REACTOR_DSL_002:Guid.NewGuid().ToString()|})).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Fires_On_DateTimeNow_Key()
    {
        // Shape 2: DateTime.Now nested inside an interpolation.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey({|REACTOR_DSL_002:$""row-{DateTime.Now.Ticks}""|})).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_On_Stable_Item_Key()
    {
        // Negative: even in the 2-parameter (item, index) form, a key off the
        // item's Id references the item and never the index — a stable key.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select((r, i) => TextBlock(r.Text).WithKey(r.Id)).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_On_Composite_Index_Key()
    {
        // Near-miss: `$""{r.Id}-{i}""` references the index but ALSO the item, so
        // it carries real identity — must not be flagged as positional.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select((r, i) => TextBlock(r.Text).WithKey($""{r.Id}-{i}"")).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_Outside_A_Projection_Lambda()
    {
        // Scope guard: a non-stable-looking key on a single, static element is
        // not a list item — DSL_002 only applies inside Select/ForEach.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public static class C
    {
        public static Element One()
            => TextBlock(""x"").WithKey(Guid.NewGuid().ToString());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("System.Guid.NewGuid().ToString()")]        // qualified Guid.NewGuid
    [InlineData(@"$""k-{DateTime.UtcNow.Ticks}""")]          // DateTime.UtcNow
    [InlineData("Environment.TickCount.ToString()")]          // Environment.TickCount
    [InlineData("Environment.TickCount64.ToString()")]        // Environment.TickCount64
    [InlineData("new Random().Next().ToString()")]            // new Random()
    [InlineData("new System.Random().Next().ToString()")]     // new (qualified) Random()
    [InlineData("Random.Shared.Next().ToString()")]           // Random.Shared
    public async Task DSL_002_Fires_On_All_PerRender_Sources(string key)
    {
        // Every per-render source the analyzer claims to catch, pinned.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey({|REACTOR_DSL_002:" + key + @"|})).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_On_Local_Named_Random()
    {
        // A local variable named `Random` is a value, not the Random type — the
        // syntactic per-render check must not flag it.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
        {
            var Random = ""stable"";
            return FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey(Random)).ToArray());
        }
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Fires_On_Inner_Select_Index_Key_Only()
    {
        // Nested Selects: the INNER positional key is flagged and attributed to
        // the inner (cell, j) lambda; the outer row is stably keyed by row.Id and
        // is not flagged. Exactly one diagnostic.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, IReadOnlyList<string> Cells);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select((row, i) =>
                   FlexRow(row.Cells.Select((cell, j) => TextBlock(cell).WithKey({|REACTOR_DSL_002:j.ToString()|})).ToArray())
                       .WithKey(row.Id)).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_On_Nested_Select_Inner_Stable_Key()
    {
        // Same nesting, but the inner key references the inner item (cell) — a
        // stable key — so neither the inner nor the outer WithKey is flagged.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, IReadOnlyList<string> Cells);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select((row, i) =>
                   FlexRow(row.Cells.Select((cell, j) => TextBlock(cell).WithKey(cell)).ToArray())
                       .WithKey(row.Id)).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_On_Bcl_List_ForEach()
    {
        // The BCL List<T>.ForEach(Action<T>) is not Reactor's ForEach factory:
        // its lambda is the sole/first argument and nothing it produces is a
        // keyed projection, so a per-render key inside it must not be flagged.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static void Build(List<Row> rows)
            => rows.ForEach(r => { _ = TextBlock(r.Text).WithKey(Guid.NewGuid().ToString()); });
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_On_Non_Shared_Random_Member()
    {
        // A type named Random accessed via a non-Shared member is not a
        // per-render source — only `Random.Shared` and `new Random(...)` are.
        var source = Stubs + @"
namespace TestApp
{
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);
    public static class Random { public static string Value => ""stable""; }

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => FlexColumn(rows.Select(r => TextBlock(r.Text).WithKey(Random.Value)).ToArray());
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Fires_On_Qualified_Factories_ForEach_Index_Key()
    {
        // The qualified `Factories.ForEach(items, lambda)` receiver is Reactor's
        // factory too — its inner positional key is flagged.
        var source = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;

    public record Row(string Id, string Text);

    public static class C
    {
        public static Element Build(IReadOnlyList<Row> rows)
            => Factories.ForEach(rows, (r, i) => Factories.TextBlock(r.Text).WithKey({|REACTOR_DSL_002:i.ToString()|}));
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DSL_002_Does_Not_Fire_On_Parallel_ForEach()
    {
        // Parallel.ForEach(source, body) is not Reactor's ForEach factory (its
        // receiver is Parallel, not Factories), so a per-render key inside its
        // body must not be flagged.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Row(string Id, string Text);

    public static class C
    {
        public static void Build(IReadOnlyList<Row> rows)
            => Parallel.ForEach(rows, r => { _ = TextBlock(r.Text).WithKey(Guid.NewGuid().ToString()); });
    }
}";

        await new CSharpAnalyzerTest<MissingWithKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
