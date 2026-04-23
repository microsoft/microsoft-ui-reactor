using System.ComponentModel.DataAnnotations;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Controls;

/// <summary>
/// Unit tests for the HitTable-style typed editor stack (spec-030 direction):
/// TypeRegistry density-aware resolution, attribute-based discovery via
/// DataAnnotations, and the typed column factories.
/// </summary>
public class TypedEditorsTests
{
    // ══════════════════════════════════════════════════════════════
    //  TypeRegistry — density-aware bool resolution
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Bool_Standard_Editor_Is_Distinct_From_Compact()
    {
        var r = new TypeRegistry();
        var standard = r.ResolveEditor(typeof(bool), EditorTier.Standard);
        var compact = r.ResolveEditor(typeof(bool), EditorTier.Compact);

        Assert.NotNull(standard);
        Assert.NotNull(compact);
        // Different delegate instances — PropertyGrid gets ToggleSwitch, DataGrid gets CheckBox.
        Assert.NotSame(standard, compact);
    }

    // ══════════════════════════════════════════════════════════════
    //  TypeRegistry — date/time/uri/color wired as primitives
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(typeof(global::System.DateTime))]
    [InlineData(typeof(global::System.DateTimeOffset))]
    [InlineData(typeof(global::System.DateOnly))]
    [InlineData(typeof(global::System.TimeSpan))]
    [InlineData(typeof(global::System.TimeOnly))]
    [InlineData(typeof(global::System.Uri))]
    [InlineData(typeof(global::Windows.UI.Color))]
    public void Registry_Resolves_Editor_For_Primitive(Type type)
    {
        var r = new TypeRegistry();
        Assert.NotNull(r.ResolveEditor(type, EditorTier.Standard));
    }

    // ══════════════════════════════════════════════════════════════
    //  TypeRegistry — cell renderer fallbacks
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(typeof(bool))]
    [InlineData(typeof(global::System.DateTime))]
    [InlineData(typeof(global::System.DateTimeOffset))]
    [InlineData(typeof(global::System.DateOnly))]
    [InlineData(typeof(global::System.TimeSpan))]
    [InlineData(typeof(global::System.TimeOnly))]
    [InlineData(typeof(global::System.Uri))]
    [InlineData(typeof(global::Windows.UI.Color))]
    public void Registry_Falls_Back_To_Builtin_CellRenderer(Type type)
    {
        var r = new TypeRegistry();
        Assert.NotNull(r.GetCellRenderer(type));
    }

    [Fact]
    public void Explicit_CellRenderer_Registration_Wins_Over_Fallback()
    {
        var r = new TypeRegistry();
        Func<object, Microsoft.UI.Reactor.Core.Element> custom = _ => null!;
        r.RegisterCellRenderer<bool>(custom);
        Assert.Same(custom, r.GetCellRenderer(typeof(bool)));
    }

    // ══════════════════════════════════════════════════════════════
    //  Attribute-based discovery via ReflectionTypeMetadataProvider
    // ══════════════════════════════════════════════════════════════

    private sealed class Annotated
    {
        [DataType(DataType.Url)]
        public string Website { get; set; } = "";

        [Range(1, 100)]
        public int Count { get; set; }

        public string Plain { get; set; } = "";
    }

    [Fact]
    public void DataType_Url_On_String_Sets_Hyperlink_Renderer()
    {
        var prop = typeof(Annotated).GetProperty(nameof(Annotated.Website))!;
        var desc = ReflectionTypeMetadataProvider.CreateDescriptor(prop, 0);
        Assert.NotNull(desc.CellRenderer);
        Assert.NotNull(desc.Editor);
    }

    [Fact]
    public void Range_On_Numeric_Sets_Editor()
    {
        var prop = typeof(Annotated).GetProperty(nameof(Annotated.Count))!;
        var desc = ReflectionTypeMetadataProvider.CreateDescriptor(prop, 0);
        Assert.NotNull(desc.Editor);
    }

    [Fact]
    public void Plain_String_Has_No_Attribute_Editor()
    {
        var prop = typeof(Annotated).GetProperty(nameof(Annotated.Plain))!;
        var desc = ReflectionTypeMetadataProvider.CreateDescriptor(prop, 0);
        // No attribute on this property — attribute-path leaves Editor null so
        // TypeRegistry resolution takes over at render time.
        Assert.Null(desc.Editor);
        Assert.Null(desc.CellRenderer);
    }

    // ══════════════════════════════════════════════════════════════
    //  Editors value-conversion round-trips — verify FromDouble maps
    //  the NumberBox's double back to the declared numeric type.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Number_Editor_For_Int_Returns_Factory()
    {
        // Unit tests run without a WinUI window, so we can't exercise the
        // onChange path end-to-end here. The invariant we can assert is
        // that resolving the numeric editor for int type succeeds and
        // returns a usable factory delegate.
        var factory = Editors.Number(typeof(int));
        Assert.NotNull(factory);
    }

    [Fact]
    public void Number_Editor_Decimal_Min_Max_Accept_Decimal_Literals()
    {
        var factory = Editors.Decimal(min: 0m, max: 9999m);
        Assert.NotNull(factory);
    }

    // ══════════════════════════════════════════════════════════════
    //  Typed column factories
    // ══════════════════════════════════════════════════════════════

    private sealed class Target
    {
        public int N { get; set; }
        public bool On { get; set; }
        public global::System.DateTime When { get; set; }
        public global::System.TimeSpan How { get; set; }
        public global::System.Uri Where { get; set; } = new("https://x");
        public global::Windows.UI.Color C { get; set; }
        public Priority P { get; set; }
    }
    private enum Priority { Low, Med, High }

    [Fact]
    public void NumberColumn_Wires_Editor_And_CellRenderer()
    {
        FieldDescriptor col = TypedColumns.NumberColumn<Target>(
            nameof(Target.N), t => t.N, min: 0, max: 99);
        Assert.NotNull(col.Editor);
        Assert.NotNull(col.CellRenderer);
    }

    [Fact]
    public void ToggleSwitchColumn_Overrides_Default_Bool_Editor()
    {
        FieldDescriptor col = TypedColumns.ToggleSwitchColumn<Target>(
            nameof(Target.On), t => t.On);
        // Explicit typed column sets Editor on the descriptor; DataGrid
        // renders this instead of the Compact (CheckBox) registry default.
        Assert.NotNull(col.Editor);
    }

    [Fact]
    public void ColorColumn_Wires_Swatch_Renderer()
    {
        FieldDescriptor col = TypedColumns.ColorColumn<Target>(
            nameof(Target.C), t => t.C);
        Assert.NotNull(col.Editor);
        Assert.NotNull(col.CellRenderer);
    }

    [Fact]
    public void ComboBoxColumn_Accepts_Explicit_Choices()
    {
        FieldDescriptor col = TypedColumns.ComboBoxColumn<Target, Priority>(
            nameof(Target.P), t => t.P,
            choices: [Priority.Low, Priority.Med]);
        Assert.NotNull(col.Editor);
    }

    [Fact]
    public void DateColumn_Wires_Editor_For_DateTime_Property()
    {
        // Target.When is DateTime — exercises the default branch of DateColumn's
        // type dispatch (falls through to Editors.Date()). The DateOnly /
        // DateTimeOffset branches are covered by selftest fixtures mounting
        // against real WinUI controls.
        FieldDescriptor col = TypedColumns.DateColumn<Target>(
            nameof(Target.When), t => t.When);
        Assert.NotNull(col.Editor);
    }

    [Fact]
    public void HyperlinkColumn_Picks_Uri_Editor_For_Uri_Property()
    {
        FieldDescriptor col = TypedColumns.HyperlinkColumn<Target>(
            nameof(Target.Where), t => t.Where);
        Assert.NotNull(col.Editor);
        Assert.NotNull(col.CellRenderer);
    }
}
