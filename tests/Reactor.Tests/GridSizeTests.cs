using System;
using System.Globalization;
using System.Threading;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 033 §1 — strongly-typed grid track value type.
/// </summary>
public class GridSizeTests
{
    [Fact]
    public void Auto_Has_Auto_UnitType()
    {
        Assert.Equal(GridUnitType.Auto, GridSize.Auto.Type);
    }

    [Fact]
    public void Star_Default_Has_Weight_1_And_Star_UnitType()
    {
        var s = GridSize.Star();
        Assert.Equal(1, s.Value);
        Assert.Equal(GridUnitType.Star, s.Type);
    }

    [Fact]
    public void Star_Stores_Custom_Weight()
    {
        var s = GridSize.Star(2.5);
        Assert.Equal(2.5, s.Value);
        Assert.Equal(GridUnitType.Star, s.Type);
    }

    [Fact]
    public void Star_Throws_For_NonPositive_Weight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Star(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Star(-1));
    }

    [Fact]
    public void Px_Stores_Pixel_Value_And_UnitType()
    {
        var p = GridSize.Px(200);
        Assert.Equal(200, p.Value);
        Assert.Equal(GridUnitType.Pixel, p.Type);
    }

    [Fact]
    public void Px_Allows_Zero_But_Throws_For_Negative()
    {
        var zero = GridSize.Px(0);
        Assert.Equal(0, zero.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSize.Px(-1));
    }

    [Fact]
    public void Equality_Same_Value_And_Type_Equal()
    {
        Assert.Equal(GridSize.Star(2), GridSize.Star(2));
        Assert.Equal(GridSize.Auto, GridSize.Auto);
        Assert.Equal(GridSize.Px(10), GridSize.Px(10));
    }

    [Fact]
    public void Equality_Different_Type_NotEqual()
    {
        Assert.NotEqual(GridSize.Star(1), GridSize.Px(1));
    }

    [Fact]
    public void Implicit_Conversion_To_GridLength_Preserves_Value_And_UnitType()
    {
        GridLength px = GridSize.Px(120);
        Assert.Equal(120, px.Value);
        Assert.Equal(GridUnitType.Pixel, px.GridUnitType);

        GridLength star = GridSize.Star(1.5);
        Assert.Equal(1.5, star.Value);
        Assert.Equal(GridUnitType.Star, star.GridUnitType);

        GridLength auto = GridSize.Auto;
        Assert.Equal(GridUnitType.Auto, auto.GridUnitType);
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void Parse_Auto_IsCaseInsensitive(string input)
    {
        Assert.Equal(GridSize.Auto, GridSize.Parse(input));
    }

    [Fact]
    public void Parse_Star_Forms()
    {
        Assert.Equal(GridSize.Star(), GridSize.Parse("*"));
        Assert.Equal(GridSize.Star(1.5), GridSize.Parse("1.5*"));
        Assert.Equal(GridSize.Star(0.33), GridSize.Parse("0.33*"));
    }

    [Fact]
    public void Parse_Pixel_Forms()
    {
        Assert.Equal(GridSize.Px(200), GridSize.Parse("200"));
        Assert.Equal(GridSize.Px(12.5), GridSize.Parse("12.5"));
        Assert.Equal(GridSize.Px(0), GridSize.Parse("0"));
    }

    [Fact]
    public void Parse_Trims_Whitespace()
    {
        Assert.Equal(GridSize.Auto, GridSize.Parse("  Auto  "));
        Assert.Equal(GridSize.Star(2), GridSize.Parse(" 2* "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("*foo")]
    [InlineData("1.2.3")]
    [InlineData("-1")]
    [InlineData("0*")]   // zero star weight is rejected
    public void Parse_Failure_Cases_Throw_FormatException(string input)
    {
        Assert.Throws<FormatException>(() => GridSize.Parse(input));
    }

    [Fact]
    public void Parse_Null_Throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GridSize.Parse(null!));
    }

    [Fact]
    public void Parse_Is_InvariantCulture_Even_Under_DeDe()
    {
        // Spec: track-string format is invariant — never localize. Verify by
        // swapping CurrentCulture to de-DE (decimal-comma) and confirming
        // "1.5*" still parses with a decimal-point.
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(GridSize.Star(1.5), GridSize.Parse("1.5*"));
            Assert.Equal(GridSize.Px(200), GridSize.Parse("200"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void ToString_Roundtrips_For_Canonical_Forms()
    {
        // Note: "1*" round-trips to "*" because weight=1 is the implicit
        // star — this is the documented canonical form.
        Assert.Equal("Auto", GridSize.Auto.ToString());
        Assert.Equal("*", GridSize.Star().ToString());
        Assert.Equal("*", GridSize.Star(1).ToString());
        Assert.Equal("1.5*", GridSize.Star(1.5).ToString());
        Assert.Equal("200", GridSize.Px(200).ToString());
    }

    [Fact]
    public void ToString_Roundtrip_Through_Parse()
    {
        var inputs = new[] { GridSize.Auto, GridSize.Star(), GridSize.Star(2.5), GridSize.Px(100) };
        foreach (var input in inputs)
            Assert.Equal(input, GridSize.Parse(input.ToString()));
    }

    [Fact]
    public void Grid_Typed_Factory_Builds_Same_Definition_As_String_Form()
    {
        var typed = Factories.Grid(
            columns: new[] { GridSize.Auto, GridSize.Star(), GridSize.Px(200) },
            rows: new[] { GridSize.Star() });
#pragma warning disable CS0618 // Comparing typed factory output against legacy string overload
        var stringy = Factories.Grid(
            columns: new[] { "Auto", "*", "200" },
            rows: new[] { "*" });
#pragma warning restore CS0618

        Assert.Equal(typed.Definition.Columns, stringy.Definition.Columns);
        Assert.Equal(typed.Definition.Rows, stringy.Definition.Rows);
    }

    /// <summary>
    /// Element-wise parity across every canonical track shape — the typed factory
    /// and the legacy string factory must produce <see cref="GridDefinition"/>
    /// instances whose <c>Columns</c> and <c>Rows</c> arrays compare element-wise
    /// equal. Spec 033 §5.9 (factory equivalence).
    /// </summary>
    [Fact]
    public void Grid_Typed_And_String_Factories_Produce_ElementWise_Equal_Tracks_For_All_Canonical_Shapes()
    {
        var typedColumns = new[]
        {
            GridSize.Auto,
            GridSize.Star(),       // weight = 1 round-trips to "*"
            GridSize.Star(2),      // explicit weight round-trips to "2*"
            GridSize.Star(0.33),   // fractional star
            GridSize.Px(0),        // zero pixels permitted
            GridSize.Px(120.5),    // fractional pixel
        };
        var typedRows = new[] { GridSize.Auto, GridSize.Star(1.5), GridSize.Px(48) };

        var typed = Factories.Grid(typedColumns, typedRows);
#pragma warning disable CS0618
        var stringy = Factories.Grid(
            columns: new[] { "Auto", "*", "2*", "0.33*", "0", "120.5" },
            rows: new[] { "Auto", "1.5*", "48" });
#pragma warning restore CS0618

        Assert.Equal(typedColumns.Length, typed.Definition.Columns.Length);
        Assert.Equal(typedRows.Length, typed.Definition.Rows.Length);
        for (int i = 0; i < typedColumns.Length; i++)
            Assert.Equal(stringy.Definition.Columns[i], typed.Definition.Columns[i]);
        for (int i = 0; i < typedRows.Length; i++)
            Assert.Equal(stringy.Definition.Rows[i], typed.Definition.Rows[i]);
    }

    /// <summary>
    /// Spec 033 §5.9: the typed Grid factory validates its inputs at the
    /// boundary. Null arrays must throw <see cref="ArgumentNullException"/>
    /// rather than allowing a NRE downstream during reconciliation.
    /// </summary>
    [Fact]
    public void Grid_Typed_Factory_Throws_On_Null_Track_Arrays()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Factories.Grid(columns: null!, rows: new[] { GridSize.Star() }));
        Assert.Throws<ArgumentNullException>(() =>
            Factories.Grid(columns: new[] { GridSize.Star() }, rows: null!));
    }

    /// <summary>
    /// Spec 033 §1: the canonical track string for star-weight 1 is <c>"*"</c>,
    /// not <c>"1*"</c>. Verify the round-trip stays canonical so the typed and
    /// string factories produce byte-identical track arrays in
    /// <see cref="GridDefinition"/> for the most common shape.
    /// </summary>
    [Fact]
    public void Grid_Typed_Factory_Star1_Produces_Canonical_Asterisk_Track()
    {
        var typed = Factories.Grid(
            columns: new[] { GridSize.Star(1) },
            rows: new[] { GridSize.Star(1) });

        Assert.Equal(new[] { "*" }, typed.Definition.Columns);
        Assert.Equal(new[] { "*" }, typed.Definition.Rows);
    }
}
