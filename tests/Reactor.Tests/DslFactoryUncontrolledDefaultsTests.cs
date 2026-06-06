using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Lock-in for the "no-arg factory call = uncontrolled" contract on every
/// element whose underlying controlled prop is <see cref="Optional{T}"/>.
///
/// The bug this is guarding against: prior to the Optional-typed-factory
/// audit, factories like <c>Expander(string, Element, bool isExpanded = false, …)</c>
/// silently wrapped the bool default as <c>Optional&lt;bool&gt;.Of(false)</c>,
/// which put the control in CONTROLLED-TO-FALSE mode. On every parent
/// re-render, the handler would write <c>IsExpanded = false</c> back to the
/// live WinUI control — collapsing whatever the user had clicked to expand.
///
/// The fix: each factory now takes <c>Optional&lt;T&gt; foo = default</c>
/// (i.e. <see cref="Optional{T}.Unset"/>) so omitting the parameter yields
/// "uncontrolled — let the WinUI control own its state". Authors who want
/// controlled behavior pass a value, which goes through the implicit
/// <c>T → Optional&lt;T&gt;</c> conversion and lands in <c>Optional&lt;T&gt;.Of(value)</c>.
///
/// Companion tests (<see cref="DslFactoryOptionalTests"/>) cover the
/// other half of the contract: passing a value or an explicit Unset.
/// </summary>
public class DslFactoryUncontrolledDefaultsTests
{
    // ── Text inputs ────────────────────────────────────────────────

    [Fact] public void TextBox_NoArgs_ValueIsUnset() => Assert.False(TextBox().Value.HasValue);
    [Fact] public void PasswordBox_NoArgs_PasswordIsUnset() => Assert.False(PasswordBox().Password.HasValue);
    [Fact] public void AutoSuggestBox_NoArgs_TextIsUnset() => Assert.False(AutoSuggestBox().Text.HasValue);
    [Fact] public void RichEditBox_NoArgs_TextIsUnset() => Assert.False(RichEditBox().Text.HasValue);

    // ── Numeric inputs ─────────────────────────────────────────────

    [Fact] public void NumberBox_NoArgs_ValueIsUnset() => Assert.False(NumberBox().Value.HasValue);
    [Fact] public void Slider_NoArgs_ValueIsUnset() => Assert.False(Slider().Value.HasValue);
    [Fact] public void RatingControl_NoArgs_ValueIsUnset() => Assert.False(RatingControl().Value.HasValue);

    // ── Toggles ────────────────────────────────────────────────────

    [Fact] public void CheckBox_NoArgs_IsCheckedIsUnset() => Assert.False(CheckBox().IsChecked.HasValue);
    [Fact] public void ThreeStateCheckBox_NoArgs_IsCheckedIsUnset() => Assert.False(ThreeStateCheckBox().IsChecked.HasValue);
    [Fact] public void RadioButton_LabelOnly_IsCheckedIsUnset() => Assert.False(RadioButton("A").IsChecked.HasValue);
    [Fact] public void ToggleSwitch_NoArgs_IsOnIsUnset() => Assert.False(ToggleSwitch().IsOn.HasValue);
    [Fact] public void ToggleSplitButton_LabelOnly_IsCheckedIsUnset() => Assert.False(ToggleSplitButton("A").IsChecked.HasValue);

    // ── Selection collections ──────────────────────────────────────

    [Fact] public void RadioButtons_NoSelectedIndex_IsUnset() => Assert.False(RadioButtons(new[] { "A" }).SelectedIndex.HasValue);
    [Fact] public void ComboBox_NoSelectedIndex_IsUnset() => Assert.False(ComboBox(new[] { "A" }).SelectedIndex.HasValue);
    // ComboBox(Element[], ...) intentionally requires explicit selectedIndex/handler args (no defaults) —
    // see Dsl.cs note about the implicit string→Element conversion. So there's no "no-arg" form to test;
    // covered instead by DslFactoryOptionalTests.SelectionFactories_WrapPlainValues_AndAcceptUnset.
    [Fact] public void ListBox_NoSelectedIndex_IsUnset() => Assert.False(ListBox(new[] { "A" }).SelectedIndex.HasValue);
    [Fact] public void SelectorBar_NoSelectedIndex_IsUnset() => Assert.False(SelectorBar(new[] { SelectorBarItem("A") }).SelectedIndex.HasValue);
    [Fact] public void PipsPager_NoSelectedPageIndex_IsUnset() => Assert.False(PipsPager(3).SelectedPageIndex.HasValue);

    // ── Date / time / color ────────────────────────────────────────

    [Fact] public void CalendarDatePicker_NoArgs_DateIsUnset() => Assert.False(CalendarDatePicker().Date.HasValue);
    [Fact] public void DatePicker_NoArgs_DateIsUnset() => Assert.False(DatePicker().Date.HasValue);
    [Fact] public void TimePicker_NoArgs_TimeIsUnset() => Assert.False(TimePicker().Time.HasValue);
    [Fact] public void ColorPicker_NoArgs_ColorIsUnset() => Assert.False(ColorPicker().Color.HasValue);

    // ── Decorators / containers ────────────────────────────────────

    [Fact]
    public void Expander_NoIsExpanded_IsUnset()
    {
        // The bug that originally motivated this audit: clicking Copy in the
        // gallery's source-code Expander was collapsing the user-opened pane
        // because the bool overload's `isExpanded = false` default silently
        // forced controlled-to-false on every parent re-render.
        var el = Expander("Header", TextBlock("Content"));
        Assert.False(el.IsExpanded.HasValue);
    }
}
