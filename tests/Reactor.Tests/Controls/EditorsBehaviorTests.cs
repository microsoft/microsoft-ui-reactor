using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Xunit;
using WinUIColor = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.Tests.Controls;

/// <summary>
/// Behavior tests for <see cref="Editors"/> factory catalog. Each test invokes
/// the factory, inspects the returned <see cref="Element"/> record (Reactor
/// elements are pure data — no WinUI activation required), and exercises the
/// <c>onChange</c> path by calling the public <c>OnXxx</c> callback on the
/// element. Every test names a product bug it would catch.
///
/// Bug-shape inventory:
///   • Wrong numeric round-trip (NumberBox emits double, DataGrid hands it
///     to an int property setter → InvalidCastException at runtime).
///   • Date editor crashes on null / unspecified-kind DateTime.
///   • Uri editor commits invalid URLs partway through typing.
///   • Combo / EnumCombo defaults to wrong choice when value is unknown.
///   • Hex color parser silently accepts garbage (e.g. 7-digit hex).
/// </summary>
public class EditorsBehaviorTests
{
    // ══════════════════════════════════════════════════════════════
    //  Numeric editors — ToDouble / FromDouble round-trip
    //
    //  Hot path: NumberBox is internally double. If FromDouble drops a
    //  bit when converting back to the declared CLR type, the property
    //  setter on the model throws InvalidCastException. Each test below
    //  invokes the NumberBoxElement.OnValueChanged callback and asserts
    //  the captured object's RUNTIME TYPE, not just the value.
    // ══════════════════════════════════════════════════════════════

    private static (NumberBoxElement Element, Func<Action<object>> _) NumberCase(
        Func<object, Action<object>, Element> factory, object? value, out Action<object> capture)
    {
        object? captured = null;
        var cap = (object o) => { captured = o; };
        capture = cap;
        var el = factory(value!, cap);
        // Number wraps NumberBox in .Set(...) — Set returns the same element type.
        var nb = Assert.IsType<NumberBoxElement>(el);
        return (nb, () => o => { /* unused */ });
    }

    [Fact]
    public void Number_Int_Reads_Initial_Value_Through_ToDouble()
    {
        // Bug: ToDouble(int) returns 0 → NumberBox starts at 0 instead of 42.
        var factory = Editors.Int();
        var nb = (NumberBoxElement)factory(42, _ => { });
        Assert.Equal(42.0, nb.Value);
    }

    [Fact]
    public void Number_Decimal_Reads_Initial_Value_Through_ToDouble()
    {
        // ToDouble(decimal) must cast — a missing arm sends decimal through
        // Convert.ToDouble which can throw on culture mismatch.
        var factory = Editors.Decimal();
        var nb = (NumberBoxElement)factory(123.45m, _ => { });
        Assert.Equal(123.45, nb.Value);
    }

    [Fact]
    public void Number_Long_Reads_Initial_Value()
    {
        var factory = Editors.Long();
        var nb = (NumberBoxElement)factory(9_999_999_999L, _ => { });
        Assert.Equal(9_999_999_999.0, nb.Value);
    }

    [Fact]
    public void Number_Float_Reads_Initial_Value()
    {
        var factory = Editors.Float();
        var nb = (NumberBoxElement)factory(1.5f, _ => { });
        Assert.Equal(1.5, nb.Value);
    }

    [Fact]
    public void Number_Short_ToDouble_Arm_Hit()
    {
        // Hits the `short s => s` arm of the ToDouble switch.
        var factory = Editors.Number(typeof(short));
        var nb = (NumberBoxElement)factory((short)100, _ => { });
        Assert.Equal(100.0, nb.Value);
    }

    [Fact]
    public void Number_Byte_ToDouble_Arm_Hit()
    {
        var factory = Editors.Number(typeof(byte));
        var nb = (NumberBoxElement)factory((byte)200, _ => { });
        Assert.Equal(200.0, nb.Value);
    }

    [Fact]
    public void Number_UInt_ToDouble_Arm_Hit()
    {
        var factory = Editors.Number(typeof(uint));
        var nb = (NumberBoxElement)factory((uint)42, _ => { });
        Assert.Equal(42.0, nb.Value);
    }

    [Fact]
    public void Number_Null_Defaults_To_Zero()
    {
        // Bug: ToDouble(null) throws → editor cannot bind a property that's
        // currently null (e.g. a nullable int that hasn't been set).
        var factory = Editors.Int();
        var nb = (NumberBoxElement)factory(null!, _ => { });
        Assert.Equal(0.0, nb.Value);
    }

    [Fact]
    public void Number_Unknown_Type_ToDouble_Fallback_Uses_Convert_ChangeType()
    {
        // The `_ => Convert.ToDouble(value, invariant)` arm. A string that
        // looks like a number must still round-trip — otherwise the editor
        // throws on initial load when the property was stored as object.
        var factory = Editors.Number(typeof(int));
        var nb = (NumberBoxElement)factory("42", _ => { });
        Assert.Equal(42.0, nb.Value);
    }

    [Fact]
    public void Number_Int_OnValueChanged_Returns_Int_Not_Double()
    {
        // Bug: FromDouble(int) skipped → onChange hands DataGrid a double.
        // DataGrid then calls (int)(object)d which throws InvalidCastException.
        object? captured = null;
        var factory = Editors.Int();
        var nb = (NumberBoxElement)factory(0, v => captured = v);
        nb.OnValueChanged!.Invoke(99.0);
        Assert.IsType<int>(captured);
        Assert.Equal(99, captured);
    }

    [Fact]
    public void Number_Long_OnValueChanged_Returns_Long()
    {
        object? captured = null;
        var factory = Editors.Long();
        var nb = (NumberBoxElement)factory(0L, v => captured = v);
        nb.OnValueChanged!.Invoke(1_000_000_000_000.0);
        Assert.IsType<long>(captured);
        Assert.Equal(1_000_000_000_000L, captured);
    }

    [Fact]
    public void Number_Decimal_OnValueChanged_Returns_Decimal()
    {
        object? captured = null;
        var factory = Editors.Decimal();
        var nb = (NumberBoxElement)factory(0m, v => captured = v);
        nb.OnValueChanged!.Invoke(7.5);
        Assert.IsType<decimal>(captured);
        Assert.Equal(7.5m, captured);
    }

    [Fact]
    public void Number_Float_OnValueChanged_Returns_Float()
    {
        object? captured = null;
        var factory = Editors.Float();
        var nb = (NumberBoxElement)factory(0f, v => captured = v);
        nb.OnValueChanged!.Invoke(2.25);
        Assert.IsType<float>(captured);
        Assert.Equal(2.25f, captured);
    }

    [Fact]
    public void Number_Double_OnValueChanged_Returns_Double_Directly()
    {
        // FromDouble's `targetType == typeof(double)` short-circuit.
        object? captured = null;
        var factory = Editors.Double();
        var nb = (NumberBoxElement)factory(0.0, v => captured = v);
        nb.OnValueChanged!.Invoke(3.14);
        Assert.IsType<double>(captured);
        Assert.Equal(3.14, captured);
    }

    [Fact]
    public void Number_Short_OnValueChanged_Returns_Short()
    {
        object? captured = null;
        var factory = Editors.Number(typeof(short));
        var nb = (NumberBoxElement)factory((short)0, v => captured = v);
        nb.OnValueChanged!.Invoke(5.0);
        Assert.IsType<short>(captured);
        Assert.Equal((short)5, captured);
    }

    [Fact]
    public void Number_Byte_OnValueChanged_Returns_Byte()
    {
        object? captured = null;
        var factory = Editors.Number(typeof(byte));
        var nb = (NumberBoxElement)factory((byte)0, v => captured = v);
        nb.OnValueChanged!.Invoke(150.0);
        Assert.IsType<byte>(captured);
        Assert.Equal((byte)150, captured);
    }

    [Fact]
    public void Number_UInt_OnValueChanged_Returns_UInt()
    {
        object? captured = null;
        var factory = Editors.Number(typeof(uint));
        var nb = (NumberBoxElement)factory((uint)0, v => captured = v);
        nb.OnValueChanged!.Invoke(1234.0);
        Assert.IsType<uint>(captured);
        Assert.Equal(1234u, captured);
    }

    [Fact]
    public void Number_Min_Max_Set_When_Provided()
    {
        // Bug: Number was returning the base NumberBoxElement without the
        // .Set() wrapper → min/max never applied → user can type out of range.
        var factory = Editors.Number(typeof(int), min: 0, max: 99, smallChange: 5);
        var nb = (NumberBoxElement)factory(50, _ => { });
        // .Set() appends a single configure-lambda. Without it the array is empty.
        Assert.Single(nb.Setters);
    }

    [Fact]
    public void Number_Setter_Always_Present_Even_Without_Min_Max()
    {
        // The lambda still applies SmallChange = 1 unconditionally.
        var factory = Editors.Number(typeof(int));
        var nb = (NumberBoxElement)factory(0, _ => { });
        Assert.Single(nb.Setters);
    }

    // ══════════════════════════════════════════════════════════════
    //  Text editor — value coercion, placeholder, maxLength branch
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Text_Null_Defaults_To_Empty_String()
    {
        // Bug: factory((string?)null, _) would NRE without the ?? "" guard.
        var factory = Editors.Text();
        var el = (TextBoxElement)factory(null!, _ => { });
        Assert.Equal(string.Empty, el.Value);
    }

    [Fact]
    public void Text_Value_Pass_Through()
    {
        var factory = Editors.Text();
        var el = (TextBoxElement)factory("hello", _ => { });
        Assert.Equal("hello", el.Value);
    }

    [Fact]
    public void Text_Placeholder_Propagates()
    {
        var factory = Editors.Text(placeholder: "type here");
        var el = (TextBoxElement)factory("", _ => { });
        Assert.Equal("type here", el.Placeholder);
    }

    [Fact]
    public void Text_MaxLength_Null_Branch_Returns_No_Setter()
    {
        // Branch coverage: the `maxLength is { } max` ternary's false arm.
        var factory = Editors.Text();
        var el = (TextBoxElement)factory("x", _ => { });
        Assert.Empty(el.Setters);
    }

    [Fact]
    public void Text_MaxLength_Set_Branch_Appends_Setter()
    {
        // True arm of the ternary. The setter mutates TextBox.MaxLength, but
        // we can't invoke it without a TextBox — counting setters is enough
        // to catch a regression that drops the .Set call.
        var factory = Editors.Text(maxLength: 50);
        var el = (TextBoxElement)factory("x", _ => { });
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Text_OnChange_Forwards_String()
    {
        object? captured = null;
        var factory = Editors.Text();
        var el = (TextBoxElement)factory("", v => captured = v);
        el.OnChanged!.Invoke("typed");
        Assert.Equal("typed", captured);
    }

    // ══════════════════════════════════════════════════════════════
    //  CheckBox / Toggle
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void CheckBox_Null_Defaults_To_False()
    {
        var factory = Editors.CheckBox();
        var el = (CheckBoxElement)factory(null!, _ => { });
        Assert.False(el.IsChecked);
    }

    [Fact]
    public void CheckBox_True_Value_Pass_Through()
    {
        var factory = Editors.CheckBox();
        var el = (CheckBoxElement)factory(true, _ => { });
        Assert.True(el.IsChecked);
    }

    [Fact]
    public void CheckBox_OnChange_Forwards_Bool()
    {
        object? captured = null;
        var factory = Editors.CheckBox();
        var el = (CheckBoxElement)factory(false, v => captured = v);
        el.OnIsCheckedChanged!.Invoke(true);
        Assert.IsType<bool>(captured);
        Assert.Equal(true, captured);
    }

    [Fact]
    public void Toggle_Null_Defaults_To_Off()
    {
        var factory = Editors.Toggle();
        var el = (ToggleSwitchElement)factory(null!, _ => { });
        Assert.False(el.IsOn);
    }

    [Fact]
    public void Toggle_OnOffContent_Propagates()
    {
        // Bug: if the on/offContent named-args weren't forwarded, the toggle
        // would render the WinUI default "On"/"Off" instead of localized text.
        var factory = Editors.Toggle(onContent: "Yes", offContent: "No");
        var el = (ToggleSwitchElement)factory(true, _ => { });
        Assert.Equal("Yes", el.OnContent);
        Assert.Equal("No", el.OffContent);
    }

    // ══════════════════════════════════════════════════════════════
    //  Date / DateOffset / DateOnly
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Date_DateTime_Unspecified_Kind_Specifies_Local()
    {
        // The branch that treats DateTimeKind.Unspecified as Local. Bug if
        // missed: passing a DateTime from JSON (Unspecified) shifts hours
        // on systems that aren't UTC, because DateTimeOffset assumes Utc.
        var factory = Editors.Date();
        var input = new DateTime(2026, 5, 17, 9, 0, 0, DateTimeKind.Unspecified);
        var el = (DatePickerElement)factory(input, _ => { });
        // The DateTimeOffset's Offset must match the local time zone for
        // this DateTime, not UTC's (0).
        var expected = new DateTimeOffset(DateTime.SpecifyKind(input, DateTimeKind.Local));
        Assert.Equal(expected.Offset, el.Date.Offset);
        Assert.Equal(input, el.Date.DateTime);
    }

    [Fact]
    public void Date_DateTime_Local_Kind_Passes_Through()
    {
        var factory = Editors.Date();
        var input = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        var el = (DatePickerElement)factory(input, _ => { });
        Assert.Equal(input, el.Date.DateTime);
    }

    [Fact]
    public void Date_DateTimeOffset_Pass_Through()
    {
        var factory = Editors.Date();
        var input = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.FromHours(-7));
        var el = (DatePickerElement)factory(input, _ => { });
        Assert.Equal(input, el.Date);
    }

    [Fact]
    public void Date_Null_Defaults_To_Now()
    {
        // Bug: if the `else` arm threw, the editor would crash when a model
        // property is currently null (common for nullable DateTime fields).
        var factory = Editors.Date();
        var before = DateTimeOffset.Now.AddSeconds(-1);
        var el = (DatePickerElement)factory(null!, _ => { });
        var after = DateTimeOffset.Now.AddSeconds(1);
        Assert.InRange(el.Date.DateTime.Ticks, before.DateTime.Ticks, after.DateTime.Ticks);
    }

    [Fact]
    public void Date_OnChange_Returns_DateTime_Not_DateTimeOffset()
    {
        // Bug shape: editor emits DTO but the model field is DateTime —
        // InvalidCastException when the setter assigns DTO to DateTime.
        object? captured = null;
        var factory = Editors.Date();
        var el = (DatePickerElement)factory(new DateTime(2020, 1, 1), v => captured = v);
        el.OnDateChanged!.Invoke(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.IsType<DateTime>(captured);
    }

    [Fact]
    public void DateOffset_Value_Pass_Through_And_OnChange_Returns_DTO()
    {
        object? captured = null;
        var factory = Editors.DateOffset();
        var input = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.FromHours(2));
        var el = (DatePickerElement)factory(input, v => captured = v);
        Assert.Equal(input, el.Date);

        var picked = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);
        el.OnDateChanged!.Invoke(picked);
        Assert.IsType<DateTimeOffset>(captured);
        Assert.Equal(picked, captured);
    }

    [Fact]
    public void DateOffset_Null_Defaults_To_Now()
    {
        var factory = Editors.DateOffset();
        var before = DateTimeOffset.Now.AddSeconds(-1);
        var el = (DatePickerElement)factory(null!, _ => { });
        Assert.InRange(el.Date.Ticks, before.Ticks, DateTimeOffset.Now.AddSeconds(1).Ticks);
    }

    [Fact]
    public void DateOnly_Round_Trips_Through_OnChange()
    {
        // The picker is internally DateTimeOffset; DateOnly editor must
        // convert both directions. Regression: emitting DateTime to a
        // DateOnly property throws InvalidCastException.
        object? captured = null;
        var factory = Editors.DateOnly();
        var input = new DateOnly(2026, 5, 17);
        var el = (DatePickerElement)factory(input, v => captured = v);
        Assert.Equal(input, DateOnly.FromDateTime(el.Date.DateTime));

        el.OnDateChanged!.Invoke(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero));
        Assert.IsType<DateOnly>(captured);
        Assert.Equal(new DateOnly(2026, 12, 25), captured);
    }

    [Fact]
    public void DateOnly_Null_Defaults_To_Today()
    {
        // Capture the date range before AND after invoking the factory so a
        // midnight-cross during the call doesn't flake. The factory's
        // "default to today" contract is met as long as the result is one
        // of {before, after} — those can differ by at most one day.
        var before = DateOnly.FromDateTime(DateTime.Today);
        var factory = Editors.DateOnly();
        var el = (DatePickerElement)factory(null!, _ => { });
        var after = DateOnly.FromDateTime(DateTime.Today);
        var actual = DateOnly.FromDateTime(el.Date.DateTime);
        Assert.True(actual == before || actual == after,
            $"Expected today (before={before}, after={after}), got {actual}.");
    }

    // ══════════════════════════════════════════════════════════════
    //  TimeOfDay / TimeSpanEditor / TimeOnlyEditor
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void TimeOfDay_TimeSpan_Branch()
    {
        var factory = Editors.TimeOfDay();
        var input = TimeSpan.FromMinutes(90);
        var el = (TimePickerElement)factory(input, _ => { });
        Assert.Equal(input, el.Time);
    }

    [Fact]
    public void TimeOfDay_TimeOnly_Branch_Converts_To_TimeSpan()
    {
        var factory = Editors.TimeOfDay();
        var input = new TimeOnly(14, 30);
        var el = (TimePickerElement)factory(input, _ => { });
        Assert.Equal(input.ToTimeSpan(), el.Time);
    }

    [Fact]
    public void TimeOfDay_Null_Branch_Defaults_To_Zero()
    {
        var factory = Editors.TimeOfDay();
        var el = (TimePickerElement)factory(null!, _ => { });
        Assert.Equal(TimeSpan.Zero, el.Time);
    }

    [Fact]
    public void TimeOfDay_Unknown_Type_Branch_Defaults_To_Zero()
    {
        // Catch-all `_ => TimeSpan.Zero` arm — e.g. a stale string value.
        var factory = Editors.TimeOfDay();
        var el = (TimePickerElement)factory("not a time", _ => { });
        Assert.Equal(TimeSpan.Zero, el.Time);
    }

    [Fact]
    public void TimeOfDay_OnChange_Returns_TimeOnly_When_Source_Was_TimeOnly()
    {
        // The factory captures the ORIGINAL value type — TimeOnly emits TimeOnly.
        object? captured = null;
        var factory = Editors.TimeOfDay();
        var el = (TimePickerElement)factory(new TimeOnly(8, 0), v => captured = v);
        el.OnTimeChanged!.Invoke(TimeSpan.FromHours(15));
        Assert.IsType<TimeOnly>(captured);
        Assert.Equal(new TimeOnly(15, 0), captured);
    }

    [Fact]
    public void TimeOfDay_OnChange_Returns_TimeSpan_When_Source_Was_TimeSpan()
    {
        object? captured = null;
        var factory = Editors.TimeOfDay();
        var el = (TimePickerElement)factory(TimeSpan.FromHours(8), v => captured = v);
        el.OnTimeChanged!.Invoke(TimeSpan.FromHours(15));
        Assert.IsType<TimeSpan>(captured);
        Assert.Equal(TimeSpan.FromHours(15), captured);
    }

    [Fact]
    public void TimeSpanEditor_Pass_Through_And_Null_Defaults_To_Zero()
    {
        var factory = Editors.TimeSpanEditor();
        var ts = TimeSpan.FromMinutes(45);
        var el1 = (TimePickerElement)factory(ts, _ => { });
        Assert.Equal(ts, el1.Time);

        var el2 = (TimePickerElement)factory(null!, _ => { });
        Assert.Equal(TimeSpan.Zero, el2.Time);
    }

    [Fact]
    public void TimeSpanEditor_OnChange_Returns_TimeSpan_Directly()
    {
        object? captured = null;
        var factory = Editors.TimeSpanEditor();
        var el = (TimePickerElement)factory(TimeSpan.Zero, v => captured = v);
        el.OnTimeChanged!.Invoke(TimeSpan.FromHours(2));
        Assert.IsType<TimeSpan>(captured);
    }

    [Fact]
    public void TimeOnlyEditor_Round_Trips_Through_OnChange()
    {
        object? captured = null;
        var factory = Editors.TimeOnlyEditor();
        var input = new TimeOnly(9, 15);
        var el = (TimePickerElement)factory(input, v => captured = v);
        Assert.Equal(input.ToTimeSpan(), el.Time);

        el.OnTimeChanged!.Invoke(TimeSpan.FromHours(17));
        Assert.IsType<TimeOnly>(captured);
        Assert.Equal(new TimeOnly(17, 0), captured);
    }

    [Fact]
    public void TimeOnlyEditor_Null_Defaults_To_MinValue()
    {
        var factory = Editors.TimeOnlyEditor();
        var el = (TimePickerElement)factory(null!, _ => { });
        Assert.Equal(TimeOnly.MinValue.ToTimeSpan(), el.Time);
    }

    // ══════════════════════════════════════════════════════════════
    //  Uri — value coercion + TryCreate gating
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Uri_Uri_Object_Stringifies()
    {
        var factory = Editors.Uri();
        var el = (TextBoxElement)factory(new global::System.Uri("https://example.com/path"), _ => { });
        Assert.Equal("https://example.com/path", el.Value.TrimEnd('/'));
    }

    [Fact]
    public void Uri_Null_Defaults_To_Empty_String()
    {
        var factory = Editors.Uri();
        var el = (TextBoxElement)factory(null!, _ => { });
        Assert.Equal(string.Empty, el.Value);
    }

    [Fact]
    public void Uri_Non_Uri_Object_ToStrings()
    {
        var factory = Editors.Uri();
        var el = (TextBoxElement)factory(42, _ => { });
        Assert.Equal("42", el.Value);
    }

    [Fact]
    public void Uri_OnChange_Commits_Valid_Url()
    {
        // Bug: a regression that drops the Uri.TryCreate gate would commit
        // garbage strings as Uri instances, breaking model invariants.
        object? captured = null;
        var factory = Editors.Uri();
        var el = (TextBoxElement)factory("", v => captured = v);
        el.OnChanged!.Invoke("https://docs.microsoft.com");
        Assert.IsType<global::System.Uri>(captured);
        Assert.Equal("https://docs.microsoft.com/", ((global::System.Uri)captured!).ToString());
    }

    [Fact]
    public void Uri_OnChange_RelativeOrAbsolute_Accepts_Relative()
    {
        // The factory uses UriKind.RelativeOrAbsolute. A regression that
        // tightened it to Absolute would silently drop relative inputs.
        object? captured = null;
        var factory = Editors.Uri();
        var el = (TextBoxElement)factory("", v => captured = v);
        el.OnChanged!.Invoke("/relative/path");
        Assert.IsType<global::System.Uri>(captured);
    }

    [Fact]
    public void Uri_OnChange_Truly_Invalid_String_Does_Not_Commit()
    {
        // Strings that fail TryCreate (with RelativeOrAbsolute) are rare —
        // they need an embedded null or control character. The contract is
        // "silent no-op for partial input"; this test pins it.
        object? captured = null;
        bool changed = false;
        var factory = Editors.Uri();
        var el = (TextBoxElement)factory("", v => { captured = v; changed = true; });
        // Use explicit \x## escapes for the control characters so the test
        // input is visible in source (embedded raw bytes get mangled by
        // editors and are invisible in reviews).
        el.OnChanged!.Invoke("http://exa\x01mple.com\x00");
        Assert.False(changed, "Invalid URI must not call onChange");
        Assert.Null(captured);
    }

    // ══════════════════════════════════════════════════════════════
    //  Combo / EnumCombo
    // ══════════════════════════════════════════════════════════════

    private enum Color { Red, Green, Blue }

    [Fact]
    public void Combo_SelectedIndex_Matches_Current_Value()
    {
        var factory = Editors.Combo(new[] { "a", "b", "c" });
        var el = (ComboBoxElement)factory("b", _ => { });
        Assert.Equal(1, el.SelectedIndex);
        Assert.Equal(new[] { "a", "b", "c" }, el.Items);
    }

    [Fact]
    public void Combo_Unknown_Value_Defaults_To_First()
    {
        // Bug: if the loop's default-idx-0 were replaced with -1, the combo
        // would render "no selection" for any value not in the list,
        // confusing the user when stale data is loaded.
        var factory = Editors.Combo(new[] { "a", "b", "c" });
        var el = (ComboBoxElement)factory("zzz", _ => { });
        Assert.Equal(0, el.SelectedIndex);
    }

    [Fact]
    public void Combo_OnChange_Returns_Strongly_Typed_Choice()
    {
        // The factory's `onChange(choices[i]!)` must hand back the typed
        // value, not the index — otherwise property setters take int and
        // throw on the model's enum field.
        object? captured = null;
        var factory = Editors.Combo(new[] { Color.Red, Color.Green, Color.Blue });
        var el = (ComboBoxElement)factory(Color.Red, v => captured = v);
        el.OnSelectedIndexChanged!.Invoke(2);
        Assert.IsType<Color>(captured);
        Assert.Equal(Color.Blue, captured);
    }

    [Fact]
    public void Combo_NameMapping_Uses_ToString()
    {
        var factory = Editors.Combo(new object?[] { 1, 2.5, "x" });
        var el = (ComboBoxElement)factory(2.5, _ => { });
        // ToString() on each → "1", "2.5", "x". Verifies the projection
        // doesn't crash on heterogeneous lists.
        Assert.Equal(3, el.Items.Length);
        Assert.Equal("2.5", el.Items[1]);
    }

    [Fact]
    public void Combo_NameMapping_Null_Choice_Renders_Empty()
    {
        var factory = Editors.Combo(new string?[] { "a", null, "c" });
        var el = (ComboBoxElement)factory("a", _ => { });
        Assert.Equal(string.Empty, el.Items[1]);
    }

    [Fact]
    public void EnumCombo_SelectedIndex_From_Enum_Value()
    {
        var factory = Editors.EnumCombo(typeof(Color));
        var el = (ComboBoxElement)factory(Color.Green, _ => { });
        Assert.Equal(1, el.SelectedIndex);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, el.Items);
    }

    [Fact]
    public void EnumCombo_Null_Value_Defaults_To_First()
    {
        // Array.IndexOf returns -1 for missing → falls through to idx=0.
        var factory = Editors.EnumCombo(typeof(Color));
        var el = (ComboBoxElement)factory(null!, _ => { });
        Assert.Equal(0, el.SelectedIndex);
    }

    [Fact]
    public void EnumCombo_Unknown_String_Defaults_To_First()
    {
        var factory = Editors.EnumCombo(typeof(Color));
        var el = (ComboBoxElement)factory("Purple", _ => { });
        Assert.Equal(0, el.SelectedIndex);
    }

    [Fact]
    public void EnumCombo_OnChange_Parses_Back_To_Enum()
    {
        object? captured = null;
        var factory = Editors.EnumCombo(typeof(Color));
        var el = (ComboBoxElement)factory(Color.Red, v => captured = v);
        el.OnSelectedIndexChanged!.Invoke(2);
        Assert.IsType<Color>(captured);
        Assert.Equal(Color.Blue, captured);
    }

    // ══════════════════════════════════════════════════════════════
    //  Color editors
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Color_Null_Defaults_To_Transparent()
    {
        var factory = Editors.Color();
        var el = (ColorPickerElement)factory(null!, _ => { });
        Assert.Equal(global::Microsoft.UI.Colors.Transparent, el.Color);
    }

    [Fact]
    public void Color_Value_Pass_Through_And_OnChange_Returns_Color()
    {
        object? captured = null;
        var factory = Editors.Color();
        var input = global::Windows.UI.Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        var el = (ColorPickerElement)factory(input, v => captured = v);
        Assert.Equal(input, el.Color);

        var picked = global::Windows.UI.Color.FromArgb(0x80, 0x11, 0x22, 0x33);
        el.OnColorChanged!.Invoke(picked);
        Assert.IsType<WinUIColor>(captured);
        Assert.Equal(picked, captured);
    }

    // Note: ColorCompact() builds the swatch via .Background(hex) which
    // constructs a WinUI SolidColorBrush — host-bound, throws COMException
    // in headless xUnit. The hex-parser branches in TryParseHexColor are
    // therefore not reachable from a unit test. A selftest fixture that
    // mounts a ColorCompact cell in a real DataGrid would be the right
    // way to cover those paths. Flagged for the worklist.
}
