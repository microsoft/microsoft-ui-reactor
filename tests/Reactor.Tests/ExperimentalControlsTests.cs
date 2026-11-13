// Unit coverage for the pure-managed parts of Reactor.Advanced's TableView and Chart
// mappings (Windows App SDK experimental controls): series value normalization, default
// X-axis inference, axis-slot identity, and the column/element helpers. Everything that
// touches a live WinUI object is covered by the TableView_*/Chart_* selftest fixtures.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Reactor.Advanced.Charts;
using Microsoft.UI.Reactor.Advanced.Tabular;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Tabular;
using Xunit;
using static Microsoft.UI.Reactor.Advanced.Factories;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

public class ExperimentalControlsTests
{
    private sealed record Person(string Name, int Age);

    // ── Chart value normalization ────────────────────────────────────────

    [Fact]
    public void Normalize_PassesSupportedListsThroughByReference()
    {
        double[] doubles = [1, 2];
        var strings = new List<string> { "a", "b" };
        var observable = new ObservableCollection<double> { 3 };
        var dates = new List<DateTimeOffset> { DateTimeOffset.UnixEpoch };

        Assert.Same(doubles, ChartValues.Normalize(doubles));
        Assert.Same(strings, ChartValues.Normalize(strings));
        // An observable collection must survive untouched so in-place appends keep updating the chart.
        Assert.Same(observable, ChartValues.Normalize(observable));
        Assert.Same(dates, ChartValues.Normalize(dates));
    }

    [Fact]
    public void Normalize_MaterializesOtherNumericTypesAsDoubles()
    {
        var result = ChartValues.Normalize(new[] { 1, 2, 3 });
        Assert.Equal(new double[] { 1, 2, 3 }, Assert.IsType<double[]>(result));

        var withNull = ChartValues.Normalize(new int?[] { 4, null });
        var values = Assert.IsType<double[]>(withNull);
        Assert.Equal(4, values[0]);
        Assert.True(double.IsNaN(values[1]));
    }

    [Fact]
    public void Normalize_ConvertsDateTimeToDateTimeOffset()
    {
        var when = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
        var result = Assert.IsType<DateTimeOffset[]>(ChartValues.Normalize(new[] { when }));
        Assert.Equal(new DateTimeOffset(when), result[0]);
    }

    [Fact]
    public void Normalize_MaterializesLazyStringSequences()
    {
        var lazy = new[] { "x", "y" }.Select(s => s.ToUpperInvariant());
        Assert.Equal(new[] { "X", "Y" }, Assert.IsType<string[]>(ChartValues.Normalize(lazy)));
    }

    [Fact]
    public void Normalize_EmptySequenceIsEmptyDoubles() =>
        Assert.Empty(Assert.IsType<double[]>(ChartValues.Normalize(Enumerable.Empty<int>())));

    // ── Default axis inference ───────────────────────────────────────────

    [Fact]
    public void ForValues_DatesInferTimeAxis()
    {
        Assert.IsType<ChartDateTimeAxis>(DefaultAxes.ForValues(new[] { DateTimeOffset.UnixEpoch }));
        Assert.IsType<ChartDateTimeAxis>(DefaultAxes.ForValues(new[] { DateTime.UnixEpoch }));
    }

    [Fact]
    public void ForValues_StringsAndNumbersInferCategoryAxis()
    {
        // The native line/bar/area X slot rejects a linear axis, so numbers are categories too.
        Assert.IsType<ChartCategoryAxis>(DefaultAxes.ForValues(new[] { "Jan" }));
        Assert.IsType<ChartCategoryAxis>(DefaultAxes.ForValues(new[] { 1.0, 2.0 }));
        Assert.IsType<ChartCategoryAxis>(DefaultAxes.ForValues(new object?[] { null, 3 }));
        Assert.IsType<ChartCategoryAxis>(DefaultAxes.ForValues(Array.Empty<double>()));
    }

    // ── Axis identity ────────────────────────────────────────────────────

    [Fact]
    public void AxisRecords_CompareByValue()
    {
        Assert.Equal(LinearAxis("Units", minimum: 0), LinearAxis("Units", minimum: 0));
        Assert.NotEqual(LinearAxis("Units"), LinearAxis("Units", minimum: 0));
        Assert.NotEqual<ChartAxis>(CategoryAxis("A"), DateTimeAxis("A"));
    }

    [Fact]
    public void AxisKey_SeparatesRoleAndLayout()
    {
        var axis = CategoryAxis("Month");
        var x = new ChartHandler.AxisKey(true, false, axis);

        Assert.Equal(x, new ChartHandler.AxisKey(true, false, CategoryAxis("Month")));
        Assert.NotEqual(x, new ChartHandler.AxisKey(false, false, axis));
        Assert.NotEqual(x, new ChartHandler.AxisKey(true, true, axis));
    }

    // ── Chart factories / modifiers ──────────────────────────────────────

    [Fact]
    public void ChartFactories_CarrySeriesAndModifiers()
    {
        string[] months = ["Jan", "Feb"];
        double[] values = [1, 2];
        var chart = Chart(
                LineSeries(months, values, "Line").Markers(Microsoft.UI.Xaml.Controls.Charts.MarkerShape.Diamond),
                BarSeries(months, values, "Bar").Vertical())
            .Legend("Legend");

        Assert.Equal(2, chart.Series.Count);
        var line = Assert.IsType<ChartLineSeries>(chart.Series[0]);
        Assert.True(line.ShowDataMarkers);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Charts.MarkerShape.Diamond, line.MarkerShape);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Charts.BarOrientation.Vertical,
            Assert.IsType<ChartBarSeries>(chart.Series[1]).Orientation);
        Assert.True(chart.ShowLegend);
        Assert.Equal("Legend", chart.LegendTitle);
        Assert.Equal("Legend", (chart with { }).Legend(show: false).LegendTitle);
    }

    // ── TableView columns ────────────────────────────────────────────────

    [Fact]
    public void BoundColumn_MapsWidthToPixels()
    {
        var sized = BoundColumn("Name", "Name", width: 120);
        Assert.Equal(new GridLength(120), sized.Width);
        Assert.Equal("Name", sized.Path);
        Assert.Null(BoundColumn("Name", "Name").Width);
        Assert.Equal(new GridLength(2, GridUnitType.Star), sized.Star(2).Width);
    }

    [Fact]
    public void TextColumn_ReadsTypedRowsAndRoutesEditsToTheCallback()
    {
        (Person Row, string Text)? edited = null;
        var column = TextColumn<Person>("Name", p => p.Name, (p, text) => edited = (p, text), width: 80);
        var ada = new Person("Ada", 36);

        Assert.Equal("Ada", column.Text(ada));
        Assert.Null(column.Text("not a person"));
        Assert.Equal(new GridLength(80), column.Width);

        column.OnEdit!(ada, "Ada L.");
        Assert.Equal((ada, "Ada L."), edited);
        column.OnEdit!("not a person", "ignored");
        Assert.Equal((ada, "Ada L."), edited);

        Assert.Null(TextColumn<Person>("Name", p => p.Name).OnEdit);
    }

    [Theory]
    [InlineData("Ada L.", true)] // changed text
    [InlineData("Ada", false)]   // unchanged text
    [InlineData(null, false)]    // nothing reported
    public void TextColumnEdit_CommitsOnlyChangedText(string? text, bool expected)
    {
        var column = TextColumn<Person>("Name", p => p.Name, (_, _) => { });
        Assert.Equal(expected, TextColumnEdit.ShouldCommit(column, new Person("Ada", 1), text));
    }

    [Fact]
    public void TextColumnEdit_NeverCommitsWithoutACallbackOrRow()
    {
        var readOnly = TextColumn<Person>("Name", p => p.Name);
        Assert.False(TextColumnEdit.ShouldCommit(readOnly, new Person("Ada", 1), "x"));
        var editable = TextColumn<Person>("Name", p => p.Name, (_, _) => { });
        Assert.False(TextColumnEdit.ShouldCommit(editable, null, "x"));
    }
    // ── Snapshot diffing ─────────────────────────────────────────────────

    private sealed record Row(int Id, string Name);

    private static (ObservableCollection<object?> Rows, List<string> Log) Tracked(params object?[] initial)
    {
        var rows = new ObservableCollection<object?>(initial);
        var log = new List<string>();
        rows.CollectionChanged += (_, e) => log.Add(e.Action.ToString());
        return (rows, log);
    }

    [Fact]
    public void RowDiff_KeyedReplaceTouchesOnlyTheChangedRow()
    {
        Row a = new(1, "a"), b = new(2, "b"), c = new(3, "c");
        var (rows, log) = Tracked(a, b, c);
        var renamed = b with { Name = "B" };

        RowDiff.Apply(rows, new[] { a, renamed, c }, r => ((Row)r).Id);

        Assert.Equal(new object?[] { a, renamed, c }, rows);
        Assert.Equal(["Replace"], log);
    }

    [Fact]
    public void RowDiff_KeyedInsertRemoveAndMove()
    {
        Row a = new(1, "a"), b = new(2, "b"), c = new(3, "c"), d = new(4, "d");
        var (rows, log) = Tracked(a, b, c);

        RowDiff.Apply(rows, new[] { c, a, d }, r => ((Row)r).Id);

        Assert.Equal(new object?[] { c, a, d }, rows);
        Assert.DoesNotContain("Reset", log);
        Assert.Contains("Remove", log);
        Assert.Contains("Add", log);
    }

    [Fact]
    public void RowDiff_UnchangedSnapshotIsANoOp()
    {
        Row a = new(1, "a"), b = new(2, "b");
        var (rows, log) = Tracked(a, b);

        RowDiff.Apply(rows, new[] { a, b }, r => ((Row)r).Id);
        RowDiff.Apply(rows, new[] { a, b }, key: null);

        Assert.Empty(log);
    }

    [Fact]
    public void RowDiff_PositionalReplacesAppendsAndTrims()
    {
        Row a = new(1, "a"), b = new(2, "b"), c = new(3, "c");
        var (rows, _) = Tracked(a, b);

        RowDiff.Apply(rows, new[] { a, c, b }, key: null);
        Assert.Equal(new object?[] { a, c, b }, rows);

        RowDiff.Apply(rows, new[] { b }, key: null);
        Assert.Equal(new object?[] { b }, rows);
    }

    [Fact]
    public void RowDiff_DuplicateKeysStillConverge()
    {
        Row a = new(1, "a"), a2 = new(1, "a2"), b = new(2, "b");
        var (rows, _) = Tracked(a, a2, b);

        RowDiff.Apply(rows, new[] { b, a }, r => ((Row)r).Id);

        Assert.Equal(new object?[] { b, a }, rows);
    }

    [Fact]
    public void TemplateColumn_RendersOnlyRowsOfItsType()
    {
        var column = TemplateColumn<Person>("Name", p => TextBlock(p.Name));

        var element = Assert.IsType<TextBlockElement>(column.Cell(new Person("Ada", 36)));
        Assert.Equal("Ada", element.Content);
        Assert.Null(column.Cell("not a person"));
        Assert.Null(column.Cell(null));
    }

    [Fact]
    public void SortBy_TypedComparisonOrdersNullsFirst()
    {
        var comparer = BoundColumn("Age", "Age").SortBy<Person>((a, b) => a.Age.CompareTo(b.Age)).SortComparer!;
        object?[] rows = [new Person("b", 40), null, new Person("a", 20)];

        var sorted = rows.OrderBy(r => r, Comparer<object?>.Create((a, b) => comparer(a, b))).ToArray();

        Assert.Null(sorted[0]);
        Assert.Equal(20, ((Person)sorted[1]!).Age);
        Assert.Equal(40, ((Person)sorted[2]!).Age);
    }

    [Fact]
    public void Editing_WrapsTypedEditor()
    {
        var column = BoundColumn("Name", "Name").Editing<Person>(p => TextBlock($"edit {p.Name}"));
        Assert.NotNull(column.EditingCell!(new Person("Ada", 1)));
        Assert.Null(column.EditingCell!(42));
    }

    [Fact]
    public void FilterRowsAndGroupRows_WrapTypedDelegatesAndClearOnNull()
    {
        var table = TableView(Array.Empty<Person>(), BoundColumn("Name", "Name"))
            .FilterRows<Person>(p => p.Age > 30)
            .GroupRows<Person>(p => p.Name[0]);

        Assert.True(table.Filter!(new Person("x", 31)));
        Assert.False(table.Filter!(new Person("x", 29)));
        Assert.False(table.Filter!("other row type"));
        Assert.Equal('x', table.GroupBy!(new Person("xy", 1)));
        Assert.Null(table.GroupBy!(5));

        var cleared = table.FilterRows<Person>(null).GroupRows<Person>(null);
        Assert.Null(cleared.Filter);
        Assert.Null(cleared.GroupBy);
    }

    [Fact]
    public void TableViewModifiers_SetTypedProperties()
    {
        var table = TableView(Array.Empty<Person>())
            .SelectionMode(TableViewSelectionMode.Single)
            .SelectedIndex(2)
            .Density(TableViewDensity.Compact)
            .GridLines(TableViewGridLinesVisibility.Horizontal)
            .ReadOnly();

        Assert.Equal(TableViewSelectionMode.Single, table.SelectionMode);
        Assert.Equal(2, table.SelectedIndex);
        Assert.Equal(TableViewDensity.Compact, table.Density);
        Assert.Equal(TableViewGridLinesVisibility.Horizontal, table.GridLinesVisibility);
        Assert.True(table.IsReadOnly);
        Assert.Empty(table.Columns);
    }
}
