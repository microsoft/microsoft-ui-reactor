using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="UnusedGridTrackAnalyzer"/> (<c>REACTOR_GRID_001</c>). Stubs a minimal
/// Reactor-shaped <c>Factories.Grid(GridSize[], GridSize[], params Element[])</c> factory, the
/// <c>GridExtensions.Grid(row:, column:, rowSpan:, columnSpan:)</c> placement modifier, and a
/// couple of element factories so the analyzer's semantic gate resolves without the framework.
/// </summary>
public class UnusedGridTrackAnalyzerTests
{
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Core
{
    public abstract class Element { }
}

namespace Microsoft.UI.Reactor
{
    public readonly struct GridSize
    {
        public static GridSize Auto => default;
        public static GridSize Star(double weight = 1) => default;
        public static GridSize Px(double px) => default;
    }

    public sealed class GridElement : Element { }
    public sealed class TextElement : Element { }

    public static class Factories
    {
        public static GridElement Grid(GridSize[] columns, GridSize[] rows, params Element[] children) => new();

        [Obsolete(""Use the typed overload."", error: false)]
        public static GridElement Grid(string[] columns, string[] rows, params Element[] children) => new();

        public static TextElement Text(string text) => new();
    }

    public static class GridExtensions
    {
        public static T Grid<T>(this T el, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
            where T : Element => el;
    }

    public static class ElementExtensions
    {
        public static T Bold<T>(this T el) where T : Element => el;
        public static T Margin<T>(this T el, double m) where T : Element => el;
    }

    // Same shape as the real factory but a DIFFERENT containing type — must never fire.
    public static class NotFactories
    {
        public static GridElement Grid(GridSize[] columns, GridSize[] rows, params Element[] children) => new();
    }
}
";

    private static Task VerifyAsync(string testBody) =>
        new CSharpAnalyzerTest<UnusedGridTrackAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + testBody,
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positives ───────────────────────────────────────────────────────────

    [Fact]
    public Task Fires_On_Unused_Column() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), {|REACTOR_GRID_001:GridSize.Star()|}],
        [GridSize.Auto],
        Text(""a"").Grid(row: 0, column: 0),
        Text(""b"").Grid(row: 0, column: 1));
}");

    [Fact]
    public Task Fires_On_Multiple_Unused_Columns() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), {|REACTOR_GRID_001:GridSize.Star()|}, {|REACTOR_GRID_001:GridSize.Auto|}],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: 1));
}");

    [Fact]
    public Task Fires_On_Unused_Row() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        [GridSize.Star()],
        [GridSize.Auto, GridSize.Auto, {|REACTOR_GRID_001:GridSize.Auto|}],
        Text(""a"").Grid(row: 0, column: 0),
        Text(""b"").Grid(row: 1, column: 0));
}");

    [Fact]
    public Task Fires_On_Default_Placement_Child() => VerifyAsync(@"
// A child with no .Grid() defaults to (row 0, column 0); the Star column is unused.
class C
{
    Element M() => Grid(
        [GridSize.Auto, {|REACTOR_GRID_001:GridSize.Star()|}],
        [GridSize.Auto],
        Text(""only""));
}");

    [Fact]
    public Task Fires_With_Variable_Receiver_And_Explicit_Grid() => VerifyAsync(@"
// The receiver is a local, but the outermost .Grid() states the cell — still provable.
class C
{
    Element M()
    {
        Element b = Text(""b"");
        return Grid(
            [GridSize.Auto, GridSize.Star(), {|REACTOR_GRID_001:GridSize.Star()|}],
            [GridSize.Auto],
            Text(""a"").Grid(column: 0),
            b.Grid(column: 1));
    }
}");

    [Fact]
    public Task Fires_Through_Trailing_Modifiers_After_Grid() => VerifyAsync(@"
// .Grid() is not the outermost call, but it is the only placement in the chain.
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), {|REACTOR_GRID_001:GridSize.Star()|}],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0).Bold(),
        Text(""b"").Grid(column: 1).Margin(4));
}");

    [Fact]
    public Task Fires_On_Explicit_Array_Tracks() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        new GridSize[] { GridSize.Auto, GridSize.Star(), {|REACTOR_GRID_001:GridSize.Star()|} },
        new GridSize[] { GridSize.Auto },
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: 1));
}");

    [Fact]
    public Task Fires_On_Implicit_Array_Tracks() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        new[] { GridSize.Auto, GridSize.Star(), {|REACTOR_GRID_001:GridSize.Star()|} },
        new[] { GridSize.Auto },
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: 1));
}");

    [Fact]
    public Task Fires_On_Explicit_Children_Array() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), {|REACTOR_GRID_001:GridSize.Star()|}],
        [GridSize.Auto],
        new Element[] { Text(""a"").Grid(column: 0), Text(""b"").Grid(column: 1) });
}");

    [Fact]
    public Task Null_Child_Occupies_Nothing() => VerifyAsync(@"
// A null child is filtered at runtime — it must NOT be treated as occupying (0,0),
// so column 0 is genuinely unused here.
class C
{
    Element M() => Grid(
        [{|REACTOR_GRID_001:GridSize.Auto|}, GridSize.Star()],
        [GridSize.Auto],
        null,
        Text(""a"").Grid(column: 1));
}");

    [Fact]
    public Task Default_Element_Child_Occupies_Nothing() => VerifyAsync(@"
// default(Element) is compile-time null (filtered at runtime) — it must be skipped, not treated
// as opaque, so an otherwise-provable grid is still analyzed and column 0 is reported unused.
class C
{
    Element M() => Grid(
        [{|REACTOR_GRID_001:GridSize.Auto|}, GridSize.Star()],
        [GridSize.Auto],
        default(Element),
        Text(""a"").Grid(column: 1));
}");

    [Fact]
    public Task Fires_On_Last_Grid_Wins() => VerifyAsync(@"
// Two .Grid() calls in one chain: the OUTERMOST (last-applied) wins and resets the column,
// so the child lands in column 1 and column 0 is unused (not column 1).
class C
{
    Element M() => Grid(
        [{|REACTOR_GRID_001:GridSize.Auto|}, GridSize.Star()],
        [GridSize.Auto],
        Text(""x"").Grid(column: 0).Grid(column: 1));
}");

    [Fact]
    public Task Fires_On_Unused_Row_With_Opaque_Columns() => VerifyAsync(@"
// The columns array is a variable (uncountable), but the rows are literal and a row is unused —
// the two axes are judged independently, so the row still fires.
class C
{
    Element M(GridSize[] cols) => Grid(
        cols,
        [GridSize.Auto, GridSize.Auto, {|REACTOR_GRID_001:GridSize.Auto|}],
        Text(""a"").Grid(row: 0, column: 0),
        Text(""b"").Grid(row: 1, column: 0));
}");

    [Fact]
    public Task Fires_On_Unused_Row_With_Empty_Columns() => VerifyAsync(@"
// An empty columns list ([]) is a non-reportable axis and must not suppress the row analysis.
class C
{
    Element M() => Grid(
        [],
        [GridSize.Auto, GridSize.Auto, {|REACTOR_GRID_001:GridSize.Auto|}],
        Text(""a"").Grid(row: 0, column: 0),
        Text(""b"").Grid(row: 1, column: 0));
}");

    // ── Negatives ───────────────────────────────────────────────────────────

    [Fact]
    public Task No_Diagnostic_When_All_Columns_Occupied() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Auto],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: 1),
        Text(""c"").Grid(column: 2));
}");

    [Fact]
    public Task No_Diagnostic_When_ColumnSpan_Covers_Track() => VerifyAsync(@"
// The second child spans columns 1 and 2, so no track is unused.
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Star()],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: 1, columnSpan: 2));
}");

    [Fact]
    public Task No_Diagnostic_When_RowSpan_Covers_Track() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        [GridSize.Star()],
        [GridSize.Auto, GridSize.Auto],
        Text(""a"").Grid(row: 0, rowSpan: 2));
}");

    [Fact]
    public Task No_Diagnostic_For_Variable_Child() => VerifyAsync(@"
// A bare variable child may have been placed with .Grid() elsewhere — not provable, so bail
// (columns 1 and 2 look unused, but the analyzer must stay silent).
class C
{
    Element M(Element extra) => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Star()],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        extra);
}");

    [Fact]
    public Task No_Diagnostic_For_Conditional_Child() => VerifyAsync(@"
// A conditional child has a branch-dependent cell — bail even though column 2 looks unused.
class C
{
    Element M(bool cond) => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Star()],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        cond ? Text(""b"").Grid(column: 1) : Text(""c"").Grid(column: 1));
}");

    [Fact]
    public Task No_Diagnostic_For_Variable_Children_Array() => VerifyAsync(@"
class C
{
    Element M(Element[] kids) => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Star()],
        [GridSize.Auto],
        kids);
}");

    [Fact]
    public Task No_Diagnostic_For_Dynamic_Placement() => VerifyAsync(@"
// A non-constant column could be the very track we would flag — bail.
class C
{
    Element M(int i) => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Star()],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: i));
}");

    [Fact]
    public Task No_Diagnostic_For_Dynamic_Span() => VerifyAsync(@"
class C
{
    Element M(int n) => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Star()],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: 1, columnSpan: n));
}");

    [Fact]
    public Task No_Diagnostic_For_Variable_Track_Array() => VerifyAsync(@"
class C
{
    Element M(GridSize[] cols) => Grid(
        cols,
        [GridSize.Auto],
        Text(""a"").Grid(column: 0));
}");

    [Fact]
    public Task No_Diagnostic_When_No_Children() => VerifyAsync(@"
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star()],
        [GridSize.Auto]);
}");

    [Fact]
    public Task No_Diagnostic_For_Non_Factory_Grid() => VerifyAsync(@"
// Same signature shape, different containing type — the semantic gate must reject it.
class C
{
    Element M() => NotFactories.Grid(
        [GridSize.Auto, GridSize.Star()],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0));
}");

    [Fact]
    public Task No_Diagnostic_For_NonTyped_String_Grid() => VerifyAsync(@"
// A non-typed (string[]) Grid shape is out of scope for this analyzer.
class C
{
    Element M() => Grid(
        new[] { ""Auto"", ""*"" },
        new[] { ""Auto"" },
        Text(""a""));
}");

    [Fact]
    public Task No_Diagnostic_For_Helper_Child() => VerifyAsync(@"
// Cell() is not a Reactor DSL factory — it may hide a .Grid(...) in its body (and here it does),
// so it must be treated as opaque and bail the grid rather than assumed to sit at (0,0).
class C
{
    static Element Cell() => Text(""x"").Grid(column: 1);

    Element M() => Grid(
        [GridSize.Auto, GridSize.Star()],
        [GridSize.Auto],
        Cell());
}");

    [Fact]
    public Task No_Diagnostic_For_Oversized_Span() => VerifyAsync(@"
// An int.MaxValue span must be clamped to the declared track count (no unbounded loop / hang);
// here it covers every column, so nothing is unused.
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Auto],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0, columnSpan: 2147483647));
}");

    [Fact]
    public Task No_Diagnostic_For_Out_Of_Range_Placement() => VerifyAsync(@"
// The second child's column is beyond the declared tracks; WinUI clamps it into the last column,
// so we can no longer prove which track is unused — bail the whole grid.
class C
{
    Element M() => Grid(
        [GridSize.Auto, GridSize.Star(), GridSize.Auto],
        [GridSize.Auto],
        Text(""a"").Grid(column: 0),
        Text(""b"").Grid(column: 9));
}");
}
