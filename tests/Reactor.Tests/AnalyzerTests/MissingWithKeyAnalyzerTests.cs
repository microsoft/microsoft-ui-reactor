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
        }.RunAsync();
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
        }.RunAsync();
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
        }.RunAsync();
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
        }.RunAsync();
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
        }.RunAsync();
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
        }.RunAsync();
    }
}
