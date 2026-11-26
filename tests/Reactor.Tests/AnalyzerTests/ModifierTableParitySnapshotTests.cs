using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 §4.5 / §10.1, layer 1: a literal snapshot of every <see cref="ModifierInfo"/> and of
/// the <see cref="ModifierTable.GateOnlyInReconciler"/> and poolable-type sets, captured from the
/// hand-written <see cref="ModifierTable"/> before its gates moved to <c>ModifierSchema</c>.
/// </summary>
/// <remarks>
/// The analyzer tests cover only selected modifiers, and the gate-drift tests skip null gates, so
/// a gate wrongly declared (or dropped) in the schema could pass both. This snapshot compares
/// every row in both directions. A deliberate analyzer change updates the snapshot in the same PR.
/// </remarks>
public class ModifierTableParitySnapshotTests
{
    // Key | ControlGate | ThemeControlGate | PoolResetGate | PoolReset | ElementTypes. Gates are
    // sorted; "-" is null.
    internal static readonly string[] Snapshot =
    [
        "AcceptsReturn | - | - | - | - | RichEditBoxElement,TextBoxElement",
        "AccessKey | - | - | - | poolReset | -",
        "Background | Border,Control,Panel | - | - | poolReset | -",
        "BorderBrush | Border,Control,Grid,RelativePanel,StackPanel | - | Border,Control,Grid,StackPanel | poolReset | -",
        "BorderThickness | Border,Control,Grid,RelativePanel,StackPanel | - | Border,Control,Grid,StackPanel | poolReset | -",
        "CharacterCasing | - | - | - | - | TextBoxElement",
        "CharacterSpacing | - | - | - | - | RichTextBlockElement,TextBlockElement",
        "ContentTransitions | - | - | - | - | ExpanderElement",
        "CornerRadius | Border,Control,Grid,RelativePanel,StackPanel | - | Border,Control,Grid,StackPanel | poolReset | -",
        "FontFamily | Control,TextBlock | - | - | - | RichTextBlockElement,TextBlockElement",
        "FontSize | Control,TextBlock | - | - | - | RichTextBlockElement,TextBlockElement",
        "FontStretch | - | - | - | - | RichTextBlockElement,RichTextHyperlink,RichTextParagraph,RichTextRun",
        "FontStyle | - | - | - | - | RichTextBlockElement,TextBlockElement",
        "FontWeight | Control,TextBlock | - | - | - | RichTextBlockElement",
        "Foreground | Control,TextBlock | Control,RichTextBlock,TextBlock | - | - | RichTextBlockElement",
        "Height | - | - | - | poolReset | -",
        "HorizontalAlignment | - | - | - | poolReset | -",
        "HorizontalContentAlignment | - | - | - | - | -",
        "HorizontalTextAlignment | - | - | - | - | RichTextBlockElement,RichTextParagraph",
        "IsActive | - | - | - | - | ProgressRingElement",
        "IsColorFontEnabled | - | - | - | - | RichTextBlockElement",
        "IsEnabled | - | - | Control | poolReset | -",
        "IsHitTestVisible | - | - | - | - | -",
        "IsReadOnly | - | - | - | - | RatingControlElement,TextBoxElement",
        "IsSpellCheckEnabled | - | - | - | - | RichEditBoxElement,TextBoxElement",
        "IsTabStop | - | - | - | poolReset | -",
        "IsTextSelectionEnabled | - | - | - | - | TextBlockElement",
        "Language | - | - | - | - | RichTextHyperlink,RichTextParagraph,RichTextRun",
        "LineHeight | - | - | - | - | RichTextBlockElement,TextBlockElement",
        "LineStackingStrategy | - | - | - | - | RichTextBlockElement,RichTextParagraph",
        "Margin | - | - | - | poolReset | -",
        "MaxHeight | - | - | - | poolReset | -",
        "MaxLength | - | - | - | - | PasswordBoxElement,RichEditBoxElement,TextBoxElement",
        "MaxLines | - | - | - | - | RichTextBlockElement,TextBlockElement",
        "MaxWidth | - | - | - | poolReset | -",
        "MinHeight | - | - | - | poolReset | -",
        "MinWidth | - | - | - | poolReset | -",
        "Opacity | - | - | - | poolReset | -",
        "OpticalMarginAlignment | - | - | - | - | RichTextBlockElement",
        "Padding | Border,Control,Grid,RelativePanel,StackPanel,TextBlock | - | Border,Control,Grid,StackPanel,TextBlock | poolReset | -",
        "PasswordRevealMode | - | - | - | - | PasswordBoxElement",
        "PlaceholderText | - | - | - | - | AutoSuggestBoxElement,CalendarDatePickerElement,ComboBoxElement,NumberBoxElement,PasswordBoxElement,RichEditBoxElement,TextBoxElement",
        "SelectionHighlightColor | - | - | - | - | RichEditBoxElement,RichTextBlockElement",
        "SelectionMode | - | - | - | - | GridViewElement,ListViewElement",
        "Stretch | - | - | - | - | ViewboxElement",
        "StretchDirection | - | - | - | - | ViewboxElement",
        "TextAlignment | - | - | - | - | RichTextBlockElement,TextBlockElement,TextBoxElement",
        "TextDecorations | - | - | - | - | RichTextBlockElement,RichTextHyperlink,RichTextParagraph,RichTextRun,TextBlockElement",
        "TextLineBounds | - | - | - | - | RichTextBlockElement",
        "TextReadingOrder | - | - | - | - | RichTextBlockElement",
        "TextTrimming | - | - | - | - | RichTextBlockElement,TextBlockElement",
        "TextWrapping | - | - | - | - | RichEditBoxElement,TextBlockElement,TextBoxElement",
        "VerticalAlignment | - | - | - | poolReset | -",
        "VerticalContentAlignment | - | - | - | - | -",
        "Width | - | - | - | poolReset | -",
    ];

    internal static readonly string[] GateOnlyInReconcilerKeys =
    [
        "BorderInlineStart",
        "ElementSoundMode",
        "IsEnabled",
        "PaddingInlineEnd",
        "PaddingInlineStart",
        "TabIndex",
    ];

    internal static readonly string[] PoolableTypeNames =
    [
        "Border",
        "Button",
        "Canvas",
        "Grid",
        "Image",
        "InfoBadge",
        "ProgressBar",
        "ProgressRing",
        "RichTextBlock",
        "ScrollViewer",
        "StackPanel",
        "TextBlock",
        "TextBox",
        "ToggleSwitch",
        "Viewbox",
    ];

    [Fact]
    public void Every_ModifierInfo_Matches_The_Snapshot()
    {
        var actual = ModifierTable.Properties
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => Row(p.Key, p.Value))
            .ToList();
        Assert.True(actual.SequenceEqual(Snapshot), Changed(nameof(Snapshot), actual));
    }

    [Fact]
    public void GateOnlyInReconciler_Keys_Match_The_Snapshot()
    {
        var actual = ModifierTable.GateOnlyInReconciler.Keys.Order(StringComparer.Ordinal).ToList();
        Assert.True(actual.SequenceEqual(GateOnlyInReconcilerKeys), Changed(nameof(GateOnlyInReconcilerKeys), actual));
        Assert.All(ModifierTable.GateOnlyInReconciler.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact]
    public void Poolable_Type_Names_Match_The_Snapshot()
    {
        var actual = ModifierTable.PoolableTypeNames.Order(StringComparer.Ordinal).ToList();
        Assert.True(actual.SequenceEqual(PoolableTypeNames), Changed(nameof(PoolableTypeNames), actual));
    }

    private static string Row(string key, ModifierInfo info) =>
        string.Join(" | ", key, Gate(info.ControlGate), Gate(info.ThemeControlGate), Gate(info.PoolResetGate),
            info.PoolReset ? "poolReset" : "-", Gate(info.ElementTypes)) + (info.Modifier == key ? "" : " | modifier=" + info.Modifier);

    private static string Gate(string[]? gate) =>
        gate is null ? "-" : string.Join(",", gate.Order(StringComparer.Ordinal));

    private static string Changed(string list, List<string> actual) =>
        $"The analyzer's modifier table changed. If that is deliberate, update {nameof(ModifierTableParitySnapshotTests)}.{list}. Actual:\n"
        + string.Join(",\n", actual.Select(a => $"\"{a}\""));
}
