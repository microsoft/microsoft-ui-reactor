using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1340: <c>DataGridEditorAlignment.AlignWithCell</c> sizes the inline text editors so their text lands where
/// the display cell drew it. The pixel result is covered by the <c>DataGrid_EditorTextAlignment</c>
/// selftest; these pin the element contract, in particular that an editor which already sizes
/// itself (a custom <c>col.Editor</c>) is left alone.
/// </summary>
public class DataGridEditorAlignmentTests
{
    private static Element Align(Element editor) => DataGridEditorAlignment.AlignWithCell(editor);

    private const string MinHeightKey = DataGridEditorAlignment.MinHeightResourceKey;

    [Fact]
    public void Default_TextBox_Gets_Cell_Padding_And_Relaxed_MinHeight()
    {
        var aligned = Align(TextBox("x", _ => { }));

        Assert.Equal(new Thickness(7, 6, 11, 6), aligned.Padding);
        Assert.Equal(0d, aligned.ResourceOverrides?.Literals[MinHeightKey]);
    }

    [Fact]
    public void Default_NumberBox_Gets_Cell_Padding_And_Relaxed_MinHeight()
    {
        var aligned = Align(NumberBox(1.5, _ => { }));

        Assert.Equal(new Thickness(7, 6, 11, 6), aligned.Padding);
        Assert.Equal(0d, aligned.ResourceOverrides?.Literals[MinHeightKey]);
    }

    [Fact]
    public void Custom_TextBox_Sizing_Is_Preserved()
    {
        var custom = TextBox("x", _ => { }).Padding(1).MinHeight(40);

        var aligned = Align(custom);

        Assert.Equal(new Thickness(1), aligned.Padding);
        Assert.Equal(40d, aligned.Modifiers?.MinHeight);
        Assert.Null(aligned.ResourceOverrides);
    }

    [Fact]
    public void Custom_NumberBox_Other_Resource_Overrides_Are_Kept_And_MinHeight_Merged_In()
    {
        var custom = NumberBox(1.5, _ => { })
            .Resources(r => r.Set("TextControlBorderThemeThickness", 2d).Set("TextControlForeground", new ThemeRef("AccentKey")));

        var aligned = Align(custom);

        Assert.Equal(2d, aligned.ResourceOverrides!.Literals["TextControlBorderThemeThickness"]);
        Assert.Equal(new ThemeRef("AccentKey"), aligned.ResourceOverrides.ThemeRefs["TextControlForeground"]);
        Assert.Equal(0d, aligned.ResourceOverrides.Literals[MinHeightKey]);
        Assert.False(custom.ResourceOverrides!.Literals.ContainsKey(MinHeightKey));
    }

    [Fact]
    public void Custom_MinHeight_Resource_Override_Is_Preserved()
    {
        var custom = TextBox("x", _ => { }).Resources(r => r.Set(MinHeightKey, 40d));

        var aligned = Align(custom);

        Assert.Same(custom.ResourceOverrides, aligned.ResourceOverrides);
        Assert.Equal(40d, aligned.ResourceOverrides!.Literals[MinHeightKey]);
    }

    [Fact]
    public void Non_Text_Editor_Keeps_Compact_Padding()
    {
        var aligned = Align(CheckBox(true, _ => { }));

        Assert.Equal(new Thickness(2), aligned.Padding);
        Assert.Null(aligned.ResourceOverrides);
    }
}
