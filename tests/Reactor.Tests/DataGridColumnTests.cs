using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls.Validation;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the Column DSL and auto-column generation.
/// </summary>
public class DataGridColumnTests
{
    private record TestProduct(int Id, string Name, string Category, double Price, bool InStock);

    private record AnnotatedItem(
        [property: ColumnWidth(80)] int Id,
        [property: PropertyDisplayName("Full Name")] string Name,
        [property: NotSortable] string Notes,
        [property: NotFilterable] double Score);

    // ── Column builder ───────────────────────────────────────────

    [Fact]
    public void Column_Creates_FieldDescriptor_With_Correct_Name()
    {
        FieldDescriptor col = Column<TestProduct>("Name", p => p.Name);
        Assert.Equal("Name", col.Name);
    }

    [Fact]
    public void Column_Sets_DisplayName()
    {
        FieldDescriptor col = Column<TestProduct>("Name", p => p.Name, displayName: "Product Name");
        Assert.Equal("Product Name", col.DisplayName);
    }

    [Fact]
    public void Column_Defaults_DisplayName_To_Name()
    {
        FieldDescriptor col = Column<TestProduct>("Name", p => p.Name);
        Assert.Equal("Name", col.DisplayName);
    }

    [Fact]
    public void Column_Sets_Width()
    {
        FieldDescriptor col = Column<TestProduct>("Id", p => p.Id, width: 80);
        Assert.Equal(80, col.Width);
    }

    [Fact]
    public void Column_Sets_Pin()
    {
        FieldDescriptor col = Column<TestProduct>("Id", p => p.Id, pin: PinPosition.Left);
        Assert.Equal(PinPosition.Left, col.Pin);
    }

    [Fact]
    public void Column_Editable_Sets_IsReadOnly_False()
    {
        FieldDescriptor col = Column<TestProduct>("Name", p => p.Name, editable: true);
        Assert.False(col.IsReadOnly);
    }

    [Fact]
    public void Column_NonEditable_Sets_IsReadOnly_True()
    {
        FieldDescriptor col = Column<TestProduct>("Name", p => p.Name, editable: false);
        Assert.True(col.IsReadOnly);
    }

    [Fact]
    public void Column_Infers_FieldType_From_Property()
    {
        FieldDescriptor col = Column<TestProduct>("Price", p => p.Price);
        Assert.Equal(typeof(double), col.FieldType);
    }

    [Fact]
    public void Column_Format_Creates_FormatValue()
    {
        FieldDescriptor col = Column<TestProduct>("Price", p => p.Price, format: "C2");
        Assert.NotNull(col.FormatValue);
        var formatted = col.FormatValue!(42.5);
        Assert.Contains("42.50", formatted);
    }

    [Fact]
    public void Column_GetValue_Returns_Correct_Value()
    {
        FieldDescriptor col = Column<TestProduct>("Name", p => p.Name);
        var product = new TestProduct(1, "Widget", "Tools", 9.99, true);
        Assert.Equal("Widget", col.GetValue(product));
    }

    // ── Column builder chaining ──────────────────────────────────

    [Fact]
    public void Validate_Adds_Validators()
    {
        var col = Column<TestProduct>("Name", p => p.Name)
            .Validate(Validate.Required())
            .Build();

        Assert.NotNull(col.Validators);
        Assert.Single(col.Validators);
    }

    [Fact]
    public void Validate_Accumulates_Multiple()
    {
        var col = Column<TestProduct>("Name", p => p.Name)
            .Validate(Validate.Required())
            .Validate(Validate.MinLength(2, "Too short"))
            .Build();

        Assert.Equal(2, col.Validators!.Count);
    }

    [Fact]
    public void NotSortable_Sets_Sortable_False()
    {
        var col = Column<TestProduct>("Name", p => p.Name)
            .NotSortable()
            .Build();

        Assert.False(col.Sortable);
    }

    [Fact]
    public void NotFilterable_Sets_Filterable_False()
    {
        var col = Column<TestProduct>("Name", p => p.Name)
            .NotFilterable()
            .Build();

        Assert.False(col.Filterable);
    }

    [Fact]
    public void CellRenderer_Sets_Custom_Renderer()
    {
        var col = Column<TestProduct>("Name", p => p.Name)
            .CellRenderer(v => TextBlock(v.ToString()!))
            .Build();

        Assert.NotNull(col.CellRenderer);
    }

    [Fact]
    public void Implicit_Conversion_To_FieldDescriptor()
    {
        FieldDescriptor col = Column<TestProduct>("Id", p => p.Id, width: 60);
        Assert.Equal("Id", col.Name);
        Assert.Equal(60.0, col.Width);
    }

    // ── Auto-column generation ───────────────────────────────────

    [Fact]
    public void AutoColumns_Generates_All_Public_Properties()
    {
        var columns = AutoColumns<TestProduct>();

        Assert.Equal(5, columns.Count);
        Assert.Equal("Id", columns[0].Name);
        Assert.Equal("Name", columns[1].Name);
        Assert.Equal("Category", columns[2].Name);
        Assert.Equal("Price", columns[3].Name);
        Assert.Equal("InStock", columns[4].Name);
    }

    [Fact]
    public void AutoColumns_Preserves_Field_Types()
    {
        var columns = AutoColumns<TestProduct>();

        Assert.Equal(typeof(int), columns[0].FieldType);
        Assert.Equal(typeof(string), columns[1].FieldType);
        Assert.Equal(typeof(double), columns[3].FieldType);
        Assert.Equal(typeof(bool), columns[4].FieldType);
    }

    [Fact]
    public void AutoColumns_GetValue_Works()
    {
        var columns = AutoColumns<TestProduct>();
        var product = new TestProduct(42, "Widget", "Tools", 9.99, true);

        Assert.Equal(42, columns[0].GetValue(product));
        Assert.Equal("Widget", columns[1].GetValue(product));
        Assert.Equal(9.99, columns[3].GetValue(product));
        Assert.Equal(true, columns[4].GetValue(product));
    }

    [Fact]
    public void AutoColumns_Reads_ColumnWidth_Attribute()
    {
        var columns = AutoColumns<AnnotatedItem>();
        var idCol = columns.First(c => c.Name == "Id");
        Assert.Equal(80, idCol.Width);
    }

    [Fact]
    public void AutoColumns_Reads_NotSortable_Attribute()
    {
        var columns = AutoColumns<AnnotatedItem>();
        var notesCol = columns.First(c => c.Name == "Notes");
        Assert.False(notesCol.Sortable);
    }

    [Fact]
    public void AutoColumns_Reads_NotFilterable_Attribute()
    {
        var columns = AutoColumns<AnnotatedItem>();
        var scoreCol = columns.First(c => c.Name == "Score");
        Assert.False(scoreCol.Filterable);
    }

    [Fact]
    public void AutoColumns_Reads_DisplayName_Attribute()
    {
        var columns = AutoColumns<AnnotatedItem>();
        var nameCol = columns.First(c => c.Name == "Name");
        Assert.Equal("Full Name", nameCol.DisplayName);
    }

    [Fact]
    public void AutoColumns_Applies_Overrides()
    {
        var columns = AutoColumns<TestProduct>(
            overrides: col => col.Name == "Id"
                ? col with { Pin = PinPosition.Left, Width = 80 }
                : col);

        var idCol = columns.First(c => c.Name == "Id");
        Assert.Equal(PinPosition.Left, idCol.Pin);
        Assert.Equal(80, idCol.Width);
    }

    [Fact]
    public void AutoColumns_Uses_Registry_Formatter()
    {
        var registry = new TypeRegistry();
        registry.RegisterFormatter<double>(val => val is null ? "" : $"${(double)val:N2}");

        var columns = AutoColumns<TestProduct>(registry: registry);
        var priceCol = columns.First(c => c.Name == "Price");

        Assert.NotNull(priceCol.FormatValue);
        Assert.Equal("$9.99", priceCol.FormatValue!(9.99));
    }
}
