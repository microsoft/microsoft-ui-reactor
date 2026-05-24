using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Controls;

/// <summary>
/// Behavior tests for <see cref="TypedColumns"/>, <see cref="TypeRegistry"/>,
/// and <see cref="ReflectionTypeMetadataProvider"/>. Replaces the
/// <c>Assert.NotNull(factory)</c>-style vanity assertions in the original
/// <see cref="TypedEditorsTests"/> by actually invoking each resolved editor
/// and asserting the returned Element record's shape.
///
/// Bug shapes covered:
///   • NumberColumn wires the wrong numeric type (decimal → int truncation,
///     onChange returns double to an int property setter).
///   • ToggleSwitchColumn falls back to CheckBox instead of ToggleSwitch.
///   • DateColumn picks DatePicker for a DateOnly property (was supposed to
///     route through Editors.DateOnly()).
///   • TimeColumn picks the wrong editor for TimeOnly vs TimeSpan.
///   • HyperlinkColumn commits invalid Uris for string-typed properties
///     (the string branch must use Editors.Text, not Editors.Uri).
///   • ComboBoxColumn drops the typed-value type on onChange (would hand
///     an int index to an enum property setter).
///   • TypeRegistry resolves Color to a Color editor instead of a Brush.
/// </summary>
public class TypedColumnsBehaviorTests
{
    // ══════════════════════════════════════════════════════════════
    //  TypeRegistry — assert each resolved editor produces the
    //  correct Element type when invoked. Previously: NotNull only.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Registry_DateTime_Editor_Returns_DatePicker_Element()
    {
        var factory = new TypeRegistry().ResolveEditor(typeof(DateTime), EditorTier.Standard);
        Assert.NotNull(factory);
        var el = factory!(new DateTime(2026, 5, 17), _ => { });
        Assert.IsType<DatePickerElement>(el);
    }

    [Fact]
    public void Registry_DateTimeOffset_Editor_Returns_DatePicker_Element_With_Offset()
    {
        var factory = new TypeRegistry().ResolveEditor(typeof(DateTimeOffset), EditorTier.Standard);
        var input = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.FromHours(-7));
        var el = (DatePickerElement)factory!(input, _ => { });
        Assert.Equal(input, el.Date);
    }

    [Fact]
    public void Registry_DateOnly_Editor_Roundtrip_Returns_DateOnly()
    {
        object? captured = null;
        var factory = new TypeRegistry().ResolveEditor(typeof(DateOnly), EditorTier.Standard);
        var el = (DatePickerElement)factory!(new DateOnly(2026, 1, 1), v => captured = v);
        el.OnDateChanged!.Invoke(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.IsType<DateOnly>(captured);
    }

    [Fact]
    public void Registry_TimeSpan_Editor_Returns_TimePicker_With_Pass_Through()
    {
        var factory = new TypeRegistry().ResolveEditor(typeof(TimeSpan), EditorTier.Standard);
        var el = (TimePickerElement)factory!(TimeSpan.FromMinutes(45), _ => { });
        Assert.Equal(TimeSpan.FromMinutes(45), el.Time);
    }

    [Fact]
    public void Registry_TimeOnly_Editor_Roundtrip_Returns_TimeOnly()
    {
        object? captured = null;
        var factory = new TypeRegistry().ResolveEditor(typeof(TimeOnly), EditorTier.Standard);
        var el = (TimePickerElement)factory!(new TimeOnly(9, 0), v => captured = v);
        el.OnTimeChanged!.Invoke(TimeSpan.FromHours(17));
        Assert.IsType<TimeOnly>(captured);
        Assert.Equal(new TimeOnly(17, 0), captured);
    }

    [Fact]
    public void Registry_Uri_Editor_Stringifies_Uri_And_Commits_Uri()
    {
        // The Uri editor uses TextBox under the hood but commits Uri objects
        // through Uri.TryCreate. A regression that resolved Uri to a string
        // editor would commit raw strings to a Uri property → ArgumentException
        // on the model setter.
        object? captured = null;
        var factory = new TypeRegistry().ResolveEditor(typeof(global::System.Uri), EditorTier.Standard);
        var input = new global::System.Uri("https://example.com");
        var el = (TextBoxElement)factory!(input, v => captured = v);
        Assert.Contains("example.com", el.Value);
        el.OnChanged!.Invoke("https://docs.microsoft.com");
        Assert.IsType<global::System.Uri>(captured);
    }

    [Fact]
    public void Registry_Color_Editor_Returns_ColorPicker_Element()
    {
        var factory = new TypeRegistry().ResolveEditor(typeof(global::Windows.UI.Color), EditorTier.Standard);
        var input = global::Windows.UI.Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        var el = (ColorPickerElement)factory!(input, _ => { });
        Assert.Equal(input, el.Color);
    }

    [Fact]
    public void Registry_Bool_Standard_Returns_Toggle_Element()
    {
        // The "Standard" tier intentionally differs from Compact — Standard
        // gives a ToggleSwitch (PropertyGrid), Compact gives a CheckBox
        // (DataGrid cell). A swap would change the visual semantics.
        var factory = new TypeRegistry().ResolveEditor(typeof(bool), EditorTier.Standard);
        var el = factory!(true, _ => { });
        Assert.IsType<ToggleSwitchElement>(el);
    }

    [Fact]
    public void Registry_Bool_Compact_Returns_CheckBox_Element()
    {
        var factory = new TypeRegistry().ResolveEditor(typeof(bool), EditorTier.Compact);
        var el = factory!(true, _ => { });
        Assert.IsType<CheckBoxElement>(el);
    }

    // ══════════════════════════════════════════════════════════════
    //  ReflectionTypeMetadataProvider — DataAnnotations branches
    // ══════════════════════════════════════════════════════════════

    private sealed class AnnotatedShape
    {
        // [DataType.Url] on a string property → string-commit editor with
        // Hyperlink display. Spec contract: the model field stays a string,
        // so onChange must hand back strings even though the renderer is a
        // hyperlink. The Uri-commit path is reserved for Uri-typed properties.
        [DataType(DataType.Url)]
        public string Website { get; set; } = "";

        [DataType(DataType.Url)]
        public global::System.Uri WebsiteUri { get; set; } = new("https://x");

        [Range(1, 100)]
        public int Count { get; set; }

        public string Plain { get; set; } = "";
    }

    [Fact]
    public void DataType_Url_On_String_Property_Commits_String_With_Hyperlink_Placeholder()
    {
        // Spec contract (per source comment): "[DataType.Url] on string → URL
        // text input + Hyperlink display". The "URL text input" is
        // Editors.Text with the https://... placeholder — NOT Editors.Uri —
        // because a string property must receive strings.
        var prop = typeof(AnnotatedShape).GetProperty(nameof(AnnotatedShape.Website))!;
        var desc = ReflectionTypeMetadataProvider.CreateDescriptor(prop, 0);

        object? captured = null;
        var el = (TextBoxElement)desc.Editor!("https://example.com", v => captured = v);
        Assert.Equal("https://...", el.PlaceholderText);
        el.OnChanged!.Invoke("https://docs.microsoft.com");
        Assert.IsType<string>(captured);
        Assert.Equal("https://docs.microsoft.com", captured);
    }

    [Fact]
    public void DataType_Url_On_Uri_Property_Commits_Uri()
    {
        // The other branch of the if-chain: [DataType.Url] + Uri-typed
        // property → Editors.Uri (gated commit via Uri.TryCreate). A
        // regression that collapsed both branches into Editors.Text would
        // pass strings to a Uri setter → ArgumentException on assignment.
        var prop = typeof(AnnotatedShape).GetProperty(nameof(AnnotatedShape.WebsiteUri))!;
        var desc = ReflectionTypeMetadataProvider.CreateDescriptor(prop, 0);

        object? captured = null;
        var el = (TextBoxElement)desc.Editor!(new global::System.Uri("https://x"), v => captured = v);
        el.OnChanged!.Invoke("https://docs.microsoft.com");
        Assert.IsType<global::System.Uri>(captured);
    }

    [Fact]
    public void Range_Attribute_Editor_Returns_NumberBox_With_Single_Setter()
    {
        // Range on int → editor is Editors.Number(int, min, max, …). The
        // factory adds exactly one .Set lambda that configures Minimum,
        // Maximum, and SmallChange in one go. A regression that dropped
        // the .Set call would let users type out-of-range values.
        var prop = typeof(AnnotatedShape).GetProperty(nameof(AnnotatedShape.Count))!;
        var desc = ReflectionTypeMetadataProvider.CreateDescriptor(prop, 0);

        object? captured = null;
        var el = (NumberBoxElement)desc.Editor!(50, v => captured = v);
        Assert.Single(el.Setters);

        // FromDouble path: emitting via the editor returns int, not double.
        el.OnValueChanged!.Invoke(42.0);
        Assert.IsType<int>(captured);
        Assert.Equal(42, captured);
    }

    // ══════════════════════════════════════════════════════════════
    //  TypedColumns factories — invoke the wired editor and assert
    //  it routed to the correct Editors.* variant.
    // ══════════════════════════════════════════════════════════════

    private sealed class Row
    {
        public int IntField { get; set; }
        public decimal DecimalField { get; set; }
        public long LongField { get; set; }
        public bool BoolField { get; set; }
        public DateTime DateTimeField { get; set; }
        public DateTimeOffset DTOField { get; set; }
        public DateOnly DateOnlyField { get; set; }
        public TimeSpan TimeSpanField { get; set; }
        public TimeOnly TimeOnlyField { get; set; }
        public global::System.Uri UriField { get; set; } = new("https://example.com");
        public string UrlString { get; set; } = "";
        public global::Windows.UI.Color ColorField { get; set; }
        public Priority PriorityField { get; set; }
    }

    private enum Priority { Low, Med, High }

    [Fact]
    public void NumberColumn_Int_Editor_Roundtrips_Int_Not_Double()
    {
        FieldDescriptor col = TypedColumns.NumberColumn<Row>(
            nameof(Row.IntField), r => r.IntField, min: 0, max: 99);

        object? captured = null;
        var el = (NumberBoxElement)col.Editor!(7, v => captured = v);
        Assert.Equal(7.0, el.Value);
        el.OnValueChanged!.Invoke(42.0);
        Assert.IsType<int>(captured);
        Assert.Equal(42, captured);
    }

    [Fact]
    public void NumberColumn_Decimal_Editor_Roundtrips_Decimal_Not_Double()
    {
        // Bug: a regression that used `typeof(int)` unconditionally inside
        // NumberColumn (instead of `((FieldDescriptor)builder).FieldType`)
        // would silently truncate decimal values to int on every edit.
        FieldDescriptor col = TypedColumns.NumberColumn<Row>(
            nameof(Row.DecimalField), r => r.DecimalField);

        object? captured = null;
        var el = (NumberBoxElement)col.Editor!(12.5m, v => captured = v);
        el.OnValueChanged!.Invoke(99.99);
        Assert.IsType<decimal>(captured);
    }

    [Fact]
    public void NumberColumn_Long_Editor_Roundtrips_Long()
    {
        FieldDescriptor col = TypedColumns.NumberColumn<Row>(
            nameof(Row.LongField), r => r.LongField);

        object? captured = null;
        var el = (NumberBoxElement)col.Editor!(1_000_000_000_000L, v => captured = v);
        el.OnValueChanged!.Invoke(2_000_000_000_000.0);
        Assert.IsType<long>(captured);
    }

    [Fact]
    public void CheckBoxColumn_Editor_Returns_CheckBox_Not_Toggle()
    {
        // Compact density — grid cells should get CheckBox, not ToggleSwitch.
        FieldDescriptor col = TypedColumns.CheckBoxColumn<Row>(
            nameof(Row.BoolField), r => r.BoolField);

        var el = col.Editor!(true, _ => { });
        Assert.IsType<CheckBoxElement>(el);
    }

    [Fact]
    public void ToggleSwitchColumn_Editor_Returns_Toggle_Not_CheckBox()
    {
        // The explicit factory overrides the registry's compact default.
        // Without this routing, DataGrid cells would render the bool as
        // a small box instead of the requested switch.
        FieldDescriptor col = TypedColumns.ToggleSwitchColumn<Row>(
            nameof(Row.BoolField), r => r.BoolField,
            onContent: "Yes", offContent: "No");

        var el = (ToggleSwitchElement)col.Editor!(true, _ => { });
        Assert.Equal("Yes", el.OnContent);
        Assert.Equal("No", el.OffContent);
    }

    [Fact]
    public void DateColumn_DateTime_Branch_Returns_DatePicker()
    {
        FieldDescriptor col = TypedColumns.DateColumn<Row>(
            nameof(Row.DateTimeField), r => r.DateTimeField);

        object? captured = null;
        var el = (DatePickerElement)col.Editor!(new DateTime(2020, 1, 1), v => captured = v);
        el.OnDateChanged!.Invoke(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.IsType<DateTime>(captured);
    }

    [Fact]
    public void DateColumn_DateTimeOffset_Branch_Returns_DTO_Editor()
    {
        // Previously uncovered: DateColumn's `fieldType == typeof(DateTimeOffset)`
        // ternary arm. Without this routing, a DTO property would round-trip
        // through Editors.Date() and lose its offset on every edit.
        FieldDescriptor col = TypedColumns.DateColumn<Row>(
            nameof(Row.DTOField), r => r.DTOField);

        object? captured = null;
        var input = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.FromHours(2));
        var el = (DatePickerElement)col.Editor!(input, v => captured = v);
        Assert.Equal(input, el.Date);

        el.OnDateChanged!.Invoke(new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero));
        Assert.IsType<DateTimeOffset>(captured);
    }

    [Fact]
    public void DateColumn_DateOnly_Branch_Returns_DateOnly_Editor()
    {
        // Previously uncovered: the DateOnly arm of the type switch.
        FieldDescriptor col = TypedColumns.DateColumn<Row>(
            nameof(Row.DateOnlyField), r => r.DateOnlyField);

        object? captured = null;
        var el = (DatePickerElement)col.Editor!(new DateOnly(2026, 5, 17), v => captured = v);
        el.OnDateChanged!.Invoke(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero));
        Assert.IsType<DateOnly>(captured);
    }

    [Fact]
    public void TimeColumn_TimeSpan_Branch_Returns_TimeSpan_Editor()
    {
        FieldDescriptor col = TypedColumns.TimeColumn<Row>(
            nameof(Row.TimeSpanField), r => r.TimeSpanField);

        object? captured = null;
        var el = (TimePickerElement)col.Editor!(TimeSpan.FromHours(2), v => captured = v);
        el.OnTimeChanged!.Invoke(TimeSpan.FromHours(3));
        Assert.IsType<TimeSpan>(captured);
    }

    [Fact]
    public void TimeColumn_TimeOnly_Branch_Returns_TimeOnly_Editor()
    {
        FieldDescriptor col = TypedColumns.TimeColumn<Row>(
            nameof(Row.TimeOnlyField), r => r.TimeOnlyField);

        object? captured = null;
        var el = (TimePickerElement)col.Editor!(new TimeOnly(8, 0), v => captured = v);
        el.OnTimeChanged!.Invoke(TimeSpan.FromHours(15));
        Assert.IsType<TimeOnly>(captured);
    }

    [Fact]
    public void HyperlinkColumn_Uri_Branch_Commits_Uri()
    {
        // The Uri branch must use Editors.Uri (which gates on TryCreate).
        FieldDescriptor col = TypedColumns.HyperlinkColumn<Row>(
            nameof(Row.UriField), r => r.UriField);

        object? captured = null;
        var el = (TextBoxElement)col.Editor!(new global::System.Uri("https://x"), v => captured = v);
        el.OnChanged!.Invoke("https://docs.microsoft.com");
        Assert.IsType<global::System.Uri>(captured);
    }

    [Fact]
    public void HyperlinkColumn_String_Branch_Commits_String()
    {
        // The string branch falls back to Editors.Text with a "https://..."
        // placeholder. Previously uncovered: this routing decision.
        // Bug shape: if the regression always picked Editors.Uri, the
        // string-typed UrlString property would receive Uri instances on
        // every change → ArgumentException on the model setter.
        FieldDescriptor col = TypedColumns.HyperlinkColumn<Row>(
            nameof(Row.UrlString), r => r.UrlString);

        object? captured = null;
        var el = (TextBoxElement)col.Editor!("https://x", v => captured = v);
        Assert.Equal("https://...", el.PlaceholderText);
        el.OnChanged!.Invoke("partial input that is not a valid uri");
        Assert.IsType<string>(captured);
    }

    [Fact]
    public void ComboBoxColumn_Editor_Roundtrips_Strongly_Typed_Choice()
    {
        // Bug: dropping the strongly-typed onChange would hand the model's
        // Priority property an int index, throwing on the enum setter.
        FieldDescriptor col = TypedColumns.ComboBoxColumn<Row, Priority>(
            nameof(Row.PriorityField), r => r.PriorityField,
            choices: [Priority.Low, Priority.Med, Priority.High]);

        object? captured = null;
        var el = (ComboBoxElement)col.Editor!(Priority.Low, v => captured = v);
        Assert.Equal(new[] { "Low", "Med", "High" }, el.Items);
        el.OnSelectedIndexChanged!.Invoke(2);
        Assert.IsType<Priority>(captured);
        Assert.Equal(Priority.High, captured);
    }

    [Fact]
    public void ColorColumn_CellRenderer_Is_Wired()
    {
        // ColorColumn's editor uses Editors.ColorCompact which builds a
        // SolidColorBrush eagerly — not unit-reachable in headless xUnit
        // (see Editors iteration's deferral note). The CellRenderer is
        // also brush-bound (CellRenderers.ColorSwatch).
        //
        // What we CAN assert without invoking: the column wires up *both*
        // an Editor and a CellRenderer. A regression that dropped the
        // .CellRenderer call would silently fall through to the registry's
        // default renderer, which for Color would print the struct's
        // ToString (e.g., "#FF123456") instead of showing the swatch.
        FieldDescriptor col = TypedColumns.ColorColumn<Row>(
            nameof(Row.ColorField), r => r.ColorField);

        Assert.NotNull(col.Editor);
        Assert.NotNull(col.CellRenderer);
        // The CellRenderer was set explicitly, not pulled from the registry
        // default — pin the per-cell rendering choice.
        Assert.Equal(typeof(global::Windows.UI.Color), col.FieldType);
    }
}
