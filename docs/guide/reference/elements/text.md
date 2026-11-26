# Elements: Text

Text blocks, rich text and the typography factories.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Text and Media](../../text-and-media.md)

## `RichTextBlockElement`

The element record that describes a WinUI [`RichTextBlock`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.richtextblock) control.

```csharp
public record RichTextBlockElement(string Text) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RichTextBlock`](../factories/text.md#richtextblock)

**Modifiers:** [`CharacterSpacing`](../modifiers/typography.md#characterspacing), [`FontFamily`](../modifiers/typography.md#fontfamily), [`FontSize`](../modifiers/typography.md#fontsize), [`FontStretch`](../modifiers/typography.md#fontstretch), [`FontStyle`](../modifiers/typography.md#fontstyle), [`FontWeight`](../modifiers/typography.md#fontweight), [`Foreground`](../modifiers/appearance.md#foreground), [`HorizontalTextAlignment`](../modifiers/typography.md#horizontaltextalignment), [`IsColorFontEnabled`](../modifiers/typography.md#iscolorfontenabled), [`IsTextScaleFactorEnabled`](../modifiers/typography.md#istextscalefactorenabled), [`LineHeight`](../modifiers/typography.md#lineheight), [`LineStackingStrategy`](../modifiers/typography.md#linestackingstrategy), [`MaxLines`](../modifiers/typography.md#maxlines), [`OpticalMarginAlignment`](../modifiers/typography.md#opticalmarginalignment), [`SelectionHighlightColor`](../modifiers/typography.md#selectionhighlightcolor), [`Set`](../modifiers/native-properties.md#set), [`TextAlignment`](../modifiers/typography.md#textalignment), [`TextDecorations`](../modifiers/typography.md#textdecorations), [`TextIndent`](../modifiers/typography.md#textindent), [`TextLineBounds`](../modifiers/typography.md#textlinebounds), [`TextReadingOrder`](../modifiers/typography.md#textreadingorder), [`TextTrimming`](../modifiers/typography.md#texttrimming)

| Property | Type | Description |
| --- | --- | --- |
| `CharacterSpacing` | `int` | Extra spacing between characters, in units of 1/1000em. Defaults to `0`. |
| `FontFamily` | `FontFamily?` |  |
| `FontSize` | `double?` |  |
| `FontStretch` | `FontStretch?` |  |
| `FontStyle` | `FontStyle?` |  |
| `FontWeight` | `FontWeight?` |  |
| `Foreground` | `Brush?` |  |
| `HorizontalTextAlignment` | `TextAlignment?` | Horizontal text alignment using the newer WinUI property. `null` uses the WinUI default. |
| `IsColorFontEnabled` | `bool?` |  |
| `IsTextScaleFactorEnabled` | `bool?` |  |
| `IsTextSelectionEnabled` | `bool` |  |
| `LineHeight` | `double?` | Line height in pixels. `null` uses the WinUI default (proportional to FontSize). |
| `LineStackingStrategy` | `LineStackingStrategy?` |  |
| `MaxLines` | `int` | Maximum number of lines before trimming per [`RichTextBlockElement.TextTrimming`](#richtextblockelement). `0` (default) = no limit. |
| `OpticalMarginAlignment` | `OpticalMarginAlignment?` |  |
| `Paragraphs` | [`RichTextParagraph[]?`](../controls/rich-text.md#richtextparagraph) |  |
| `SelectionHighlightColor` | `SolidColorBrush?` |  |
| `Text` | `string` |  |
| `TextAlignment` | `TextAlignment?` | Horizontal alignment of text within the block. `null` uses the WinUI default (Left). |
| `TextDecorations` | `TextDecorations?` |  |
| `TextIndent` | `double?` |  |
| `TextLineBounds` | `TextLineBounds?` |  |
| `TextReadingOrder` | `TextReadingOrder?` |  |
| `TextTrimming` | `TextTrimming?` | How overflowing text is truncated. `null` uses the WinUI default (None). |
| `TextWrapping` | `TextWrapping?` |  |

## `TextBlockElement`

The element record that describes a WinUI [`TextBlock`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.textblock) control.

```csharp
public record TextBlockElement(string Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Body`](../factories/text.md#body), [`BodyLarge`](../factories/text.md#bodylarge), [`BodyStrong`](../factories/text.md#bodystrong), [`Caption`](../factories/text.md#caption), [`Display`](../factories/text.md#display), [`Heading`](../factories/text.md#heading), [`SubHeading`](../factories/text.md#subheading), [`Subtitle`](../factories/text.md#subtitle), [`TextBlock`](../factories/text.md#textblock), [`Title`](../factories/text.md#title), [`TitleLarge`](../factories/text.md#titlelarge)

**Modifiers:** [`Bold`](../modifiers/typography.md#bold), [`CharacterSpacing`](../modifiers/typography.md#characterspacing), [`FontFamily`](../modifiers/typography.md#fontfamily), [`FontSize`](../modifiers/typography.md#fontsize), [`FontStyle`](../modifiers/typography.md#fontstyle), [`IsTextSelectionEnabled`](../modifiers/typography.md#istextselectionenabled), [`LineHeight`](../modifiers/typography.md#lineheight), [`MaxLines`](../modifiers/typography.md#maxlines), [`Selectable`](../modifiers/typography.md#selectable), [`SemiBold`](../modifiers/typography.md#semibold), [`Set`](../modifiers/native-properties.md#set), [`TextAlignment`](../modifiers/typography.md#textalignment), [`TextDecorations`](../modifiers/typography.md#textdecorations), [`TextTrimming`](../modifiers/typography.md#texttrimming), [`TextWrapping`](../modifiers/typography.md#textwrapping)

| Property | Type | Description |
| --- | --- | --- |
| `CharacterSpacing` | `int` | Extra spacing between characters, in units of 1/1000em. Defaults to `0` (no extra spacing). |
| `Content` | `string` |  |
| `FontFamily` | `FontFamily?` |  |
| `FontSize` | `double?` |  |
| `FontStyle` | `FontStyle?` |  |
| `HorizontalAlignment` | `HorizontalAlignment?` |  |
| `IsTextSelectionEnabled` | `bool?` |  |
| `LineHeight` | `double?` | Line height in pixels. `null` uses the WinUI default (proportional to FontSize). |
| `MaxLines` | `int` | Maximum number of lines to render before truncating per [`TextBlockElement.TextTrimming`](#textblockelement). `0` (default) = no limit. |
| `TextAlignment` | `TextAlignment?` |  |
| `TextDecorations` | `TextDecorations` | Bitmask of underline / strikethrough decorations. Default `None`. |
| `TextTrimming` | `TextTrimming?` |  |
| `TextWrapping` | `TextWrapping?` |  |
| `Weight` | `FontWeight?` |  |
