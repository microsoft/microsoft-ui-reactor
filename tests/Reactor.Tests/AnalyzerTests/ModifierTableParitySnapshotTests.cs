using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 §4.5 / §10.1, layer 1: a literal snapshot of every <see cref="ModifierInfo"/> and of
/// the <see cref="ModifierTable.GateOnlyInReconciler"/> key set, captured from the hand-written
/// <see cref="ModifierTable"/> before its gates moved to <c>ModifierSchema</c>.
/// </summary>
/// <remarks>
/// The analyzer tests cover only selected modifiers, and the gate-drift tests skip null gates, so
/// a gate wrongly declared (or dropped) in the schema could pass both. This snapshot compares
/// every row in both directions. A deliberate analyzer change updates the snapshot in the same PR.
/// The poolable-type mirror is pinned to the runtime list by
/// <c>Analyzer_Poolable_Type_Mirror_Matches_ElementPool</c> instead.
/// </remarks>
public class ModifierTableParitySnapshotTests
{
    // Key | ControlGate | ThemeControlGate | PoolResetGate | PoolReset | ElementTypes. Gates keep
    // their array order, which REACTOR_MOD_003 prints in its message; "-" is null.
    internal static readonly string[] Snapshot =
    [
        "AcceptsReturn | - | - | - | - | TextBoxElement,RichEditBoxElement",
        "AccessKey | - | - | - | poolReset | -",
        "Background | Panel,Control,Border | - | - | poolReset | -",
        "BorderBrush | Control,Border,Grid,StackPanel,RelativePanel | - | Control,Border,Grid,StackPanel | poolReset | -",
        "BorderThickness | Control,Border,Grid,StackPanel,RelativePanel | - | Control,Border,Grid,StackPanel | poolReset | -",
        "CharacterCasing | - | - | - | - | TextBoxElement",
        "CharacterSpacing | - | - | - | - | TextBlockElement,RichTextBlockElement",
        "ContentTransitions | - | - | - | - | ExpanderElement",
        "CornerRadius | Control,Border,Grid,StackPanel,RelativePanel | - | Control,Border,Grid,StackPanel | poolReset | -",
        "FontFamily | Control,TextBlock | - | - | - | TextBlockElement,RichTextBlockElement",
        "FontSize | Control,TextBlock | - | - | - | TextBlockElement,RichTextBlockElement",
        "FontStretch | - | - | - | - | RichTextBlockElement,RichTextParagraph,RichTextRun,RichTextHyperlink",
        "FontStyle | - | - | - | - | TextBlockElement,RichTextBlockElement",
        "FontWeight | Control,TextBlock | - | - | - | RichTextBlockElement",
        "Foreground | Control,TextBlock,RichTextBlock,IconElement | - | - | - | RichTextBlockElement",
        "Height | - | - | - | poolReset | -",
        "HorizontalAlignment | - | - | - | poolReset | -",
        "HorizontalContentAlignment | - | - | - | - | -",
        "HorizontalTextAlignment | - | - | - | - | RichTextBlockElement,RichTextParagraph",
        "IsActive | - | - | - | - | ProgressRingElement",
        "IsColorFontEnabled | - | - | - | - | RichTextBlockElement",
        "IsEnabled | - | - | Control | poolReset | -",
        "IsHitTestVisible | - | - | - | - | -",
        "IsReadOnly | - | - | - | - | TextBoxElement,RatingControlElement",
        "IsSpellCheckEnabled | - | - | - | - | TextBoxElement,RichEditBoxElement",
        "IsTabStop | - | - | - | poolReset | -",
        "IsTextSelectionEnabled | - | - | - | - | TextBlockElement",
        "Language | - | - | - | - | RichTextParagraph,RichTextRun,RichTextHyperlink",
        "LineHeight | - | - | - | - | TextBlockElement,RichTextBlockElement",
        "LineStackingStrategy | - | - | - | - | RichTextBlockElement,RichTextParagraph",
        "Margin | - | - | - | poolReset | -",
        "MaxHeight | - | - | - | poolReset | -",
        "MaxLength | - | - | - | - | TextBoxElement,RichEditBoxElement,PasswordBoxElement",
        "MaxLines | - | - | - | - | TextBlockElement,RichTextBlockElement",
        "MaxWidth | - | - | - | poolReset | -",
        "MinHeight | - | - | - | poolReset | -",
        "MinWidth | - | - | - | poolReset | -",
        "Opacity | - | - | - | poolReset | -",
        "OpticalMarginAlignment | - | - | - | - | RichTextBlockElement",
        "Padding | Control,Border,Grid,StackPanel,RelativePanel,TextBlock | - | Control,Border,Grid,StackPanel,TextBlock | poolReset | -",
        "PasswordRevealMode | - | - | - | - | PasswordBoxElement",
        "PlaceholderText | - | - | - | - | TextBoxElement,PasswordBoxElement,NumberBoxElement,ComboBoxElement,AutoSuggestBoxElement,CalendarDatePickerElement,RichEditBoxElement",
        "SelectionHighlightColor | - | - | - | - | RichTextBlockElement,RichEditBoxElement",
        "SelectionMode | - | - | - | - | ListViewElement,GridViewElement",
        "Stretch | - | - | - | - | ViewboxElement",
        "StretchDirection | - | - | - | - | ViewboxElement",
        "TextAlignment | - | - | - | - | TextBlockElement,RichTextBlockElement,TextBoxElement",
        "TextDecorations | - | - | - | - | TextBlockElement,RichTextBlockElement,RichTextParagraph,RichTextRun,RichTextHyperlink",
        "TextLineBounds | - | - | - | - | RichTextBlockElement",
        "TextReadingOrder | - | - | - | - | RichTextBlockElement",
        "TextTrimming | - | - | - | - | TextBlockElement,RichTextBlockElement",
        "TextWrapping | - | - | - | - | TextBlockElement,TextBoxElement,RichEditBoxElement",
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

    private static string Row(string key, ModifierInfo info) =>
        string.Join(" | ", key, Gate(info.ControlGate), Gate(info.ThemeControlGate), Gate(info.PoolResetGate),
            info.PoolReset ? "poolReset" : "-", Gate(info.ElementTypes)) + (info.Modifier == key ? "" : " | modifier=" + info.Modifier);

    private static string Gate(string[]? gate) =>
        gate is null ? "-" : string.Join(",", gate);

    private static string Changed(string list, List<string> actual) =>
        $"The analyzer's modifier table changed. If that is deliberate, update {nameof(ModifierTableParitySnapshotTests)}.{list}. Actual:\n"
        + string.Join(",\n", actual.Select(a => $"\"{a}\""));
}
