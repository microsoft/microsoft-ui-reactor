using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for ThreeStateCheckBox DSL factory and CheckBoxElement three-state properties.
/// </summary>
public class ThreeStateCheckBoxTests
{
    // ════════════════════════════════════════════════════════════════
    //  Two-state CheckBox (existing behavior preserved)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CheckBox_TwoState_Defaults()
    {
        var el = CheckBox(false, label: "Accept");
        Assert.False(el.IsChecked.Value.GetValueOrDefault());
        Assert.False(el.IsThreeState);
        Assert.Null(el.CheckedState);
        Assert.Null(el.OnCheckedStateChanged);
    }

    [Fact]
    public void CheckBox_TwoState_OnIsCheckedChanged_Fires()
    {
        bool? result = null;
        var el = CheckBox(false, v => result = v);
        el.OnIsCheckedChanged!.Invoke(true);
        Assert.True(result);
    }

    // ════════════════════════════════════════════════════════════════
    //  ThreeStateCheckBox factory
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ThreeStateCheckBox_Creates_With_Null_State()
    {
        var el = ThreeStateCheckBox(null, label: "Select all");
        Assert.IsType<CheckBoxElement>(el);
        Assert.True(el.IsThreeState);
        Assert.Null(el.CheckedState);
        Assert.Equal("Select all", el.Label);
    }

    [Fact]
    public void ThreeStateCheckBox_Creates_With_True_State()
    {
        var el = ThreeStateCheckBox(true);
        Assert.True(el.IsThreeState);
        Assert.True(el.CheckedState);
        Assert.True(el.IsChecked.Value.GetValueOrDefault()); // fallback for compat
    }

    [Fact]
    public void ThreeStateCheckBox_Creates_With_False_State()
    {
        var el = ThreeStateCheckBox(false);
        Assert.True(el.IsThreeState);
        Assert.False(el.CheckedState);
        Assert.False(el.IsChecked.Value.GetValueOrDefault());
    }

    [Fact]
    public void ThreeStateCheckBox_OnCheckedStateChanged_Fires_With_Null()
    {
        bool? captured = true; // start non-null to verify it gets set to null
        var el = ThreeStateCheckBox(false, v => captured = v);
        el.OnCheckedStateChanged!.Invoke(null);
        Assert.Null(captured);
    }

    [Fact]
    public void ThreeStateCheckBox_OnCheckedStateChanged_Fires_With_True()
    {
        bool? captured = null;
        var el = ThreeStateCheckBox(false, v => captured = v);
        el.OnCheckedStateChanged!.Invoke(true);
        Assert.True(captured);
    }

    [Fact]
    public void ThreeStateCheckBox_OnCheckedStateChanged_Fires_With_False()
    {
        bool? captured = true;
        var el = ThreeStateCheckBox(true, v => captured = v);
        el.OnCheckedStateChanged!.Invoke(false);
        Assert.False(captured);
    }

    // ════════════════════════════════════════════════════════════════
    //  Record equality
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ThreeStateCheckBox_Record_Equality()
    {
        var a = ThreeStateCheckBox(null, label: "All");
        var b = ThreeStateCheckBox(null, label: "All");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ThreeStateCheckBox_Record_Inequality_Different_State()
    {
        var a = ThreeStateCheckBox(true);
        var b = ThreeStateCheckBox(false);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ThreeStateCheckBox_Vs_TwoState_Not_Equal()
    {
        var threeState = ThreeStateCheckBox(true, label: "X");
        var twoState = CheckBox(true, label: "X");
        Assert.NotEqual(threeState, twoState);
    }

    // ════════════════════════════════════════════════════════════════
    //  Modifiers and Set work
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ThreeStateCheckBox_Margin_Works()
    {
        var el = ThreeStateCheckBox(null).Margin(24, 0, 0, 0);
        Assert.NotNull(el.Modifiers);
    }

    [Fact]
    public void ThreeStateCheckBox_Set_Works()
    {
        var el = ThreeStateCheckBox(null)
            .Set(cb => cb.MinWidth = 100);
        Assert.NotEqual(ThreeStateCheckBox(null), el);
    }

    // ════════════════════════════════════════════════════════════════
    //  CanUpdate
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_ThreeState_And_TwoState_Same_Type()
    {
        // Both are CheckBoxElement records, so CanUpdate should return true
        var reconciler = new Reconciler();
        var threeState = ThreeStateCheckBox(null);
        var twoState = CheckBox(false);
        Assert.True(reconciler.CanUpdate(threeState, twoState));
    }
}
