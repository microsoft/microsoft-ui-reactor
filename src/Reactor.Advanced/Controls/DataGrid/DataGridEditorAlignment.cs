using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Elements;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Sizes the DataGrid's inline editors (issue #1340). Kept out of the generic
/// <see cref="DataGridComponent{T}"/> so it's pure element manipulation with no WinUI statics,
/// which lets headless unit tests reach it.
/// </summary>
internal static class DataGridEditorAlignment
{
    private const double CellPadLeft = DataGridComponent<object>.CellPadLeft;
    private const double CellPadRight = DataGridComponent<object>.CellPadRight;

    // TextControlBorderThemeThickness — the text editors draw a 1px border inside their padding.
    private const double TextEditorBorder = 1;

    // Symmetric vertical padding for text editors. With MinHeight relaxed the box sizes to its
    // content, so equal top/bottom padding is what centers the text; 6 keeps the box at roughly
    // WinUI's standard 32px text-control height (1 + 6 + line + 6 + 1).
    private const double TextEditorPadV = 6;

    internal const string MinHeightResourceKey = "TextControlThemeMinHeight";

    // Shared instance so the editing cell's element stays reference-equal across renders.
    private static readonly ResourceOverrides RelaxedTextMinHeight = new(
        new Dictionary<string, object> { [MinHeightResourceKey] = 0d },
        new Dictionary<string, ThemeRef>());

    /// <summary>
    /// Keeps the text of an inline text editor where the display cell drew it, so entering
    /// edit mode doesn't make the text jump. A WinUI TextBox top-anchors its text and enforces
    /// a 32px MinHeight, so with asymmetric/too-small padding the text sat above the
    /// vertically-centered display TextBlock. Relaxing MinHeight and padding symmetrically lets
    /// the editor center its text exactly like the cell does, independent of the TextBox
    /// template version; the horizontal padding matches the cell's (less the border).
    /// <para>Only fills in what the editor left unset, so a custom <c>col.Editor</c> that sizes
    /// its own TextBox/NumberBox keeps that sizing. Non-text editors keep the compact padding.</para>
    /// </summary>
    internal static Element AlignWithCell(Element editor)
    {
        if (editor is not (TextBoxElement or NumberBoxElement))
            return editor.Padding(2);

        if (editor.Padding is null)
            editor = editor.Padding(
                CellPadLeft - TextEditorBorder, TextEditorPadV, CellPadRight - TextEditorBorder, TextEditorPadV);
        // A theme-resource override rather than .MinHeight(0): it also reaches the TextBox inside
        // NumberBox's template, which no NumberBox property forwards to. Merged into any overrides
        // the editor already has, unless it sized itself via .MinHeight or this same key.
        if (editor.Modifiers?.MinHeight is null)
        {
            var existing = editor.ResourceOverrides;
            if (existing is null)
                editor = editor with { ResourceOverrides = RelaxedTextMinHeight };
            else if (!existing.Literals.ContainsKey(MinHeightResourceKey)
                     && !existing.ThemeRefs.ContainsKey(MinHeightResourceKey))
                editor = editor with
                {
                    ResourceOverrides = existing with
                    {
                        Literals = new Dictionary<string, object>(existing.Literals) { [MinHeightResourceKey] = 0d },
                    },
                };
        }
        return editor;
    }
}
