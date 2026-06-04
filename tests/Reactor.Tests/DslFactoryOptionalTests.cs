using System;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

public class DslFactoryOptionalTests
{
    [Fact]
    public void TextInputFactories_WrapPlainValues_AndAcceptUnset()
    {
        AssertOptional("text", TextBox("text").Value);
        AssertUnset(TextBox(Optional<string>.Unset).Value);

        AssertOptional("secret", PasswordBox("secret").Password);
        AssertUnset(PasswordBox(Optional<string>.Unset).Password);

        AssertOptional("query", AutoSuggestBox("query").Text);
        AssertUnset(AutoSuggestBox(Optional<string>.Unset).Text);

        AssertOptional("rich", RichEditBox("rich").Text);
        AssertUnset(RichEditBox(Optional<string>.Unset).Text);
    }

    [Fact]
    public void NumericFactories_WrapPlainValues_AndAcceptUnset()
    {
        AssertOptional(12.5, NumberBox(12.5).Value);
        AssertUnset(NumberBox(Optional<double>.Unset).Value);

        AssertOptional(50.0, Slider(50.0).Value);
        AssertUnset(Slider(Optional<double>.Unset).Value);

        AssertOptional(4.0, RatingControl(4.0).Value);
        AssertUnset(RatingControl(Optional<double>.Unset).Value);
    }

    [Fact]
    public void ToggleFactories_WrapPlainValues_AndAcceptUnset()
    {
        AssertOptional<bool?>(true, CheckBox(true).IsChecked);
        AssertUnset(CheckBox(Optional<bool?>.Unset).IsChecked);

        var threeStateNull = ThreeStateCheckBox(null).IsChecked;
        Assert.True(threeStateNull.HasValue);
        Assert.Null(threeStateNull.Value);
        AssertUnset(ThreeStateCheckBox(Optional<bool?>.Unset).IsChecked);

        AssertOptional(true, RadioButton("A", true).IsChecked);
        AssertUnset(RadioButton("A", Optional<bool>.Unset).IsChecked);

        AssertOptional(true, ToggleSwitch(true).IsOn);
        AssertUnset(ToggleSwitch(Optional<bool>.Unset).IsOn);

        AssertOptional(true, ToggleSplitButton("A", true).IsChecked);
        AssertUnset(ToggleSplitButton("A", Optional<bool>.Unset).IsChecked);

        AssertOptional(true, Expander("H", TextBlock("C"), true).IsExpanded);
        AssertUnset(Expander("H", TextBlock("C"), Optional<bool>.Unset).IsExpanded);
    }

    [Fact]
    public void SelectionFactories_WrapPlainValues_AndAcceptUnset()
    {
        AssertOptional(2, RadioButtons(new[] { "A", "B", "C" }, 2).SelectedIndex);
        AssertUnset(RadioButtons(new[] { "A" }, Optional<int>.Unset).SelectedIndex);

        AssertOptional(1, ComboBox(new[] { "A", "B" }, 1).SelectedIndex);
        AssertUnset(ComboBox(new[] { "A" }, Optional<int>.Unset).SelectedIndex);

        AssertOptional(1, ComboBox(new Element[] { TextBlock("A"), TextBlock("B") }, 1, null).SelectedIndex);
        AssertUnset(ComboBox(new Element[] { TextBlock("A") }, Optional<int>.Unset, null).SelectedIndex);

        AssertOptional(1, ListBox(new[] { "A", "B" }, 1).SelectedIndex);
        AssertUnset(ListBox(new[] { "A" }, Optional<int>.Unset).SelectedIndex);

        AssertOptional(1, SelectorBar(new[] { SelectorBarItem("A"), SelectorBarItem("B") }, 1).SelectedIndex);
        AssertUnset(SelectorBar(new[] { SelectorBarItem("A") }, Optional<int>.Unset).SelectedIndex);

        AssertOptional(1, PipsPager(3, 1).SelectedPageIndex);
        AssertUnset(PipsPager(3, Optional<int>.Unset).SelectedPageIndex);
    }

    [Fact]
    public void NavigationSelectionFactories_MapNullableNullToUnset_AndAcceptOptionalUnset()
    {
        var tab = Tab("A", TextBlock("A"));
        AssertOptional(1, TabView((int?)1, null, tab).SelectedIndex);
        AssertUnset(TabView((int?)null, null, tab).SelectedIndex);
        AssertUnset(TabView(Optional<int>.Unset, null, tab).SelectedIndex);

        var pivotItem = PivotItem("A", TextBlock("A"));
        AssertOptional(1, Pivot((int?)1, null, pivotItem).SelectedIndex);
        AssertUnset(Pivot((int?)null, null, pivotItem).SelectedIndex);
        AssertUnset(Pivot(Optional<int>.Unset, null, pivotItem).SelectedIndex);

        AssertOptional(1, ListView((int?)1, null, TextBlock("A")).SelectedIndex);
        AssertUnset(ListView((int?)null, null, TextBlock("A")).SelectedIndex);
        AssertUnset(ListView(Optional<int>.Unset, null, TextBlock("A")).SelectedIndex);

        AssertOptional(1, GridView((int?)1, null, TextBlock("A")).SelectedIndex);
        AssertUnset(GridView((int?)null, null, TextBlock("A")).SelectedIndex);
        AssertUnset(GridView(Optional<int>.Unset, null, TextBlock("A")).SelectedIndex);

        AssertOptional(1, FlipView((int?)1, null, TextBlock("A")).SelectedIndex);
        AssertUnset(FlipView((int?)null, null, TextBlock("A")).SelectedIndex);
        AssertUnset(FlipView(Optional<int>.Unset, null, TextBlock("A")).SelectedIndex);
    }

    [Fact]
    public void DateAndColorFactories_WrapPlainValues_AndAcceptUnset()
    {
        var color = global::Windows.UI.Color.FromArgb(255, 1, 2, 3);
        AssertOptional(color, ColorPicker(color).Color);
        AssertUnset(ColorPicker(Optional<global::Windows.UI.Color>.Unset).Color);

        var nullableDate = new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero);
        AssertOptional<DateTimeOffset?>(nullableDate, CalendarDatePicker(nullableDate).Date);
        var explicitNull = CalendarDatePicker(null).Date;
        Assert.True(explicitNull.HasValue);
        Assert.Null(explicitNull.Value);
        AssertUnset(CalendarDatePicker(Optional<DateTimeOffset?>.Unset).Date);

        var date = new DateTimeOffset(2024, 5, 7, 0, 0, 0, TimeSpan.Zero);
        AssertOptional(date, DatePicker(date).Date);
        AssertUnset(DatePicker(Optional<DateTimeOffset>.Unset).Date);

        var time = TimeSpan.FromHours(9);
        AssertOptional(time, TimePicker(time).Time);
        AssertUnset(TimePicker(Optional<TimeSpan>.Unset).Time);
    }

    private static void AssertOptional<T>(T expected, Optional<T> actual)
    {
        Assert.True(actual.HasValue);
        Assert.Equal(expected, actual.Value);
    }

    private static void AssertUnset<T>(Optional<T> actual) => Assert.False(actual.HasValue);
}
