using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Controls;

/// <summary>
/// Unit tests for <see cref="PropertyGridDefaults"/> templates. All four
/// public templates return pure Reactor Element records (FlexRow,
/// TextBlock, Button) — no WinUI activation required. Each template
/// encodes ~5-10 branches (indent levels, expanded toggle icons,
/// optional move/remove buttons) that a regression could silently flip.
/// </summary>
public class PropertyGridDefaultsTests
{
    private static FieldDescriptor Field(string name, string? displayName = null, string? description = null) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            FieldType = typeof(string),
            GetValue = _ => null,
        };

    // ══════════════════════════════════════════════════════════════
    //  PropertyLabelTemplate
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void PropertyLabel_Uses_DisplayName_When_Set()
    {
        // Bug shape: a regression that fell back to Name unconditionally
        // would leak internal field identifiers into the UI even when the
        // app supplied a user-friendly DisplayName.
        var desc = Field("internalName", displayName: "Friendly Name");
        var el = (TextBlockElement)PropertyGridDefaults.PropertyLabelTemplate(desc, 0);
        Assert.Equal("Friendly Name", el.Content);
    }

    [Fact]
    public void PropertyLabel_Falls_Back_To_Name_When_DisplayName_Null()
    {
        var desc = Field("rawName");
        var el = (TextBlockElement)PropertyGridDefaults.PropertyLabelTemplate(desc, 0);
        Assert.Equal("rawName", el.Content);
    }

    [Fact]
    public void PropertyLabel_AutomationName_Has_Label_Prefix()
    {
        // Pin: screen-reader users distinguish the label from the editor
        // by the "Label: " / "Editor: " prefix. A regression that
        // collapsed the two prefixes would be invisible visually but
        // break a11y immediately.
        var desc = Field("x", displayName: "X");
        var el = (TextBlockElement)PropertyGridDefaults.PropertyLabelTemplate(desc, 0);
        Assert.Equal("Label: X", el.Modifiers?.AutomationName);
    }

    [Fact]
    public void PropertyLabel_Indent_Scales_Margin_By_4()
    {
        // The template uses `indentLevel * 4` for the label margin.
        // Bug shape: a regression that swapped to `* 16` (the row's
        // indent) would over-indent labels relative to editors.
        var desc = Field("x");
        var el = (TextBlockElement)PropertyGridDefaults.PropertyLabelTemplate(desc, 3);
        var margin = el.Modifiers?.Margin;
        Assert.NotNull(margin);
        Assert.Equal(12.0, margin!.Value.Left);
        Assert.Equal(0, margin.Value.Top);
        Assert.Equal(0, margin.Value.Right);
        Assert.Equal(0, margin.Value.Bottom);
    }

    [Fact]
    public void PropertyLabel_Description_Becomes_Tooltip()
    {
        // The ToolTip is set from descriptor.Description. Without it, the
        // empty-string is set instead — which still gets a tooltip shape
        // applied to the modifier chain but renders nothing.
        var desc = Field("x", description: "Helpful info");
        var el = (TextBlockElement)PropertyGridDefaults.PropertyLabelTemplate(desc, 0);
        // The ToolTip extension stores into Modifiers.ToolTip
        Assert.Equal("Helpful info", el.Modifiers?.ToolTip);
    }

    // ══════════════════════════════════════════════════════════════
    //  PropertyRowTemplate — FlexRow with 160-px label + grow-1 editor
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void PropertyRow_Returns_FlexRow_With_Two_Children()
    {
        var desc = Field("x");
        var label = (Element)Microsoft.UI.Reactor.Factories.TextBlock("L");
        var editor = (Element)Microsoft.UI.Reactor.Factories.TextBox("v");
        var row = PropertyGridDefaults.PropertyRowTemplate(desc, label, editor, 0);

        var flex = Assert.IsType<FlexElement>(row);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexDirection.Row, flex.Direction);
        Assert.Equal(Microsoft.UI.Reactor.Layout.FlexAlign.Center, flex.AlignItems);
        Assert.Equal(8.0, flex.ColumnGap);
        Assert.Equal(2, flex.Children.Length);
    }

    [Fact]
    public void PropertyRow_Editor_AutomationName_Has_Editor_Prefix()
    {
        // The complement to the label's "Label: " prefix — pin so the
        // two prefixes never collide.
        var desc = Field("x", displayName: "Counter");
        var label = (Element)Microsoft.UI.Reactor.Factories.TextBlock("L");
        var editor = (Element)Microsoft.UI.Reactor.Factories.TextBox("v");
        var row = PropertyGridDefaults.PropertyRowTemplate(desc, label, editor, 0);

        // Children[1] is the editor, with AutomationName modifier applied.
        var editorOut = ((FlexElement)row).Children[1];
        Assert.Equal("Editor: Counter", editorOut.Modifiers?.AutomationName);
    }

    [Fact]
    public void PropertyRow_Indent_Padding_Scales_Editor_Padding_By_16()
    {
        // The template applies `indentLevel * 16` as left-padding on the
        // label slot — this is the visible nesting indent in the grid.
        var desc = Field("x");
        var label = (Element)Microsoft.UI.Reactor.Factories.TextBlock("L");
        var editor = (Element)Microsoft.UI.Reactor.Factories.TextBox("v");
        var row = PropertyGridDefaults.PropertyRowTemplate(desc, label, editor, 2);

        var labelOut = ((FlexElement)row).Children[0];
        var pad = labelOut.Modifiers?.Padding;
        Assert.NotNull(pad);
        Assert.Equal(32.0, pad!.Value.Left); // 2 * 16
    }

    // ══════════════════════════════════════════════════════════════
    //  ArrayToolbarTemplate — NOT unit-testable in headless xUnit.
    //  The template calls `.SemiBold()` which dereferences
    //  `Microsoft.UI.Text.FontWeights.SemiBold` — a WinRT activation
    //  factory that throws COMException without a packaged WinUI
    //  runtime. Same trap class as Editors.ColorCompact's
    //  `.Background(hex)` (iteration 4) and CellRenderers'
    //  brush-shaped renderers (iteration 7). Skipping ArrayToolbar
    //  here; a selftest fixture that mounts the toolbar is the right
    //  home for its shape pin.
    // ══════════════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════════════
    //  ArrayItemTemplate — many conditional children
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ArrayItem_Collapsed_Uses_Right_Triangle_Glyph()
    {
        // Branch: `isExpanded ? "▼" : "▶"`. Visual regression catch.
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "—", isExpanded: false,
            onExpandedChanged: _ => { }, onMoveUp: null, onMoveDown: null, onRemove: null);
        var toggle = Assert.IsType<ButtonElement>(row.Children[0]);
        Assert.Equal("▶", toggle.Label); // ▶
    }

    [Fact]
    public void ArrayItem_Expanded_Uses_Down_Triangle_Glyph()
    {
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "—", isExpanded: true,
            onExpandedChanged: _ => { }, onMoveUp: null, onMoveDown: null, onRemove: null);
        var toggle = Assert.IsType<ButtonElement>(row.Children[0]);
        Assert.Equal("▼", toggle.Label); // ▼
    }

    [Fact]
    public void ArrayItem_Toggle_Inverts_IsExpanded_On_Click()
    {
        // The onExpandedChanged callback receives the *new* value, not
        // the old one. Pin: a regression that passed the unchanged
        // `isExpanded` would freeze the expander.
        bool? captured = null;
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "—", isExpanded: false,
            onExpandedChanged: v => captured = v, onMoveUp: null, onMoveDown: null, onRemove: null);
        var toggle = (ButtonElement)row.Children[0];
        toggle.OnClick!.Invoke();
        Assert.True(captured);
    }

    [Fact]
    public void ArrayItem_Index_Bracket_Format()
    {
        // The `[{index}]` text. A regression that emitted `{index}` or
        // `(index)` would visually break the list layout.
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 7, summary: "summary", isExpanded: false,
            onExpandedChanged: _ => { }, onMoveUp: null, onMoveDown: null, onRemove: null);
        // Children[1] is the [N] bracket.
        var bracket = Assert.IsType<TextBlockElement>(row.Children[1]);
        Assert.Equal("[7]", bracket.Content);
    }

    [Fact]
    public void ArrayItem_Summary_Goes_In_Grow_Slot()
    {
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "Item summary here", isExpanded: false,
            onExpandedChanged: _ => { }, onMoveUp: null, onMoveDown: null, onRemove: null);
        var summary = Assert.IsType<TextBlockElement>(row.Children[2]);
        Assert.Equal("Item summary here", summary.Content);
    }

    [Fact]
    public void ArrayItem_No_Optional_Actions_Yields_Three_Children()
    {
        // 0 actions → toggle, index, summary, only.
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "—", isExpanded: false,
            onExpandedChanged: _ => { }, onMoveUp: null, onMoveDown: null, onRemove: null);
        Assert.Equal(3, row.Children.Length);
    }

    [Fact]
    public void ArrayItem_All_Optional_Actions_Yields_Six_Children()
    {
        // All 3 actions → toggle, index, summary, up, down, remove = 6.
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "—", isExpanded: false,
            onExpandedChanged: _ => { },
            onMoveUp: () => { },
            onMoveDown: () => { },
            onRemove: () => { });
        Assert.Equal(6, row.Children.Length);
    }

    [Fact]
    public void ArrayItem_MoveUp_Only_Yields_Four_Children()
    {
        // Each optional callback adds independently. Pin the FilterChildren
        // semantics: only non-null entries become children.
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "—", isExpanded: false,
            onExpandedChanged: _ => { },
            onMoveUp: () => { },
            onMoveDown: null,
            onRemove: null);
        Assert.Equal(4, row.Children.Length);
        // Children[3] is the move-up button (glyph ▲).
        var btn = Assert.IsType<ButtonElement>(row.Children[3]);
        Assert.Equal("▲", btn.Label); // ▲
    }

    [Fact]
    public void ArrayItem_Remove_Button_Wires_OnRemove_Callback()
    {
        bool removed = false;
        var row = (FlexElement)PropertyGridDefaults.ArrayItemTemplate(
            index: 0, summary: "—", isExpanded: false,
            onExpandedChanged: _ => { },
            onMoveUp: null, onMoveDown: null,
            onRemove: () => removed = true);
        // Last child is the remove button (×).
        var btn = Assert.IsType<ButtonElement>(row.Children[^1]);
        Assert.Equal("✕", btn.Label); // ✕
        btn.OnClick!.Invoke();
        Assert.True(removed);
    }
}
