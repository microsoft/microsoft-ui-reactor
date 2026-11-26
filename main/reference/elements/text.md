---
title: "Elements: Text"
description: "Text blocks, rich text and the typography factories."
sidebar:
  label: "Text"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## `RichTextBlockElement` [#richtextblockelement]

The element record that describes a WinUI [`RichTextBlock`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.richtextblock) control.

```csharp
public record RichTextBlockElement(string Text) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`RichTextBlock`](/microsoft-ui-reactor/main/reference/factories/text#richtextblock)

**Modifiers:** [`CharacterSpacing`](/microsoft-ui-reactor/main/reference/modifiers/typography#characterspacing), [`FontFamily`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontfamily), [`FontSize`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontsize), [`FontStretch`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontstretch), [`FontStyle`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontstyle), [`FontWeight`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontweight), [`Foreground`](/microsoft-ui-reactor/main/reference/modifiers/appearance#foreground), [`HorizontalTextAlignment`](/microsoft-ui-reactor/main/reference/modifiers/typography#horizontaltextalignment), [`IsColorFontEnabled`](/microsoft-ui-reactor/main/reference/modifiers/typography#iscolorfontenabled), [`IsTextScaleFactorEnabled`](/microsoft-ui-reactor/main/reference/modifiers/typography#istextscalefactorenabled), [`LineHeight`](/microsoft-ui-reactor/main/reference/modifiers/typography#lineheight), [`LineStackingStrategy`](/microsoft-ui-reactor/main/reference/modifiers/typography#linestackingstrategy), [`MaxLines`](/microsoft-ui-reactor/main/reference/modifiers/typography#maxlines), [`OpticalMarginAlignment`](/microsoft-ui-reactor/main/reference/modifiers/typography#opticalmarginalignment), [`SelectionHighlightColor`](/microsoft-ui-reactor/main/reference/modifiers/typography#selectionhighlightcolor), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`TextAlignment`](/microsoft-ui-reactor/main/reference/modifiers/typography#textalignment), [`TextDecorations`](/microsoft-ui-reactor/main/reference/modifiers/typography#textdecorations), [`TextIndent`](/microsoft-ui-reactor/main/reference/modifiers/typography#textindent), [`TextLineBounds`](/microsoft-ui-reactor/main/reference/modifiers/typography#textlinebounds), [`TextReadingOrder`](/microsoft-ui-reactor/main/reference/modifiers/typography#textreadingorder), [`TextTrimming`](/microsoft-ui-reactor/main/reference/modifiers/typography#texttrimming)

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
| `Paragraphs` | [`RichTextParagraph[]?`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph) |  |
| `SelectionHighlightColor` | `SolidColorBrush?` |  |
| `Text` | `string` |  |
| `TextAlignment` | `TextAlignment?` | Horizontal alignment of text within the block. `null` uses the WinUI default (Left). |
| `TextDecorations` | `TextDecorations?` |  |
| `TextIndent` | `double?` |  |
| `TextLineBounds` | `TextLineBounds?` |  |
| `TextReadingOrder` | `TextReadingOrder?` |  |
| `TextTrimming` | `TextTrimming?` | How overflowing text is truncated. `null` uses the WinUI default (None). |
| `TextWrapping` | `TextWrapping?` |  |

## `TextBlockElement` [#textblockelement]

The element record that describes a WinUI [`TextBlock`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.textblock) control.

```csharp
public record TextBlockElement(string Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Body`](/microsoft-ui-reactor/main/reference/factories/text#body), [`BodyLarge`](/microsoft-ui-reactor/main/reference/factories/text#bodylarge), [`BodyStrong`](/microsoft-ui-reactor/main/reference/factories/text#bodystrong), [`Caption`](/microsoft-ui-reactor/main/reference/factories/text#caption), [`Display`](/microsoft-ui-reactor/main/reference/factories/text#display), [`Heading`](/microsoft-ui-reactor/main/reference/factories/text#heading), [`SubHeading`](/microsoft-ui-reactor/main/reference/factories/text#subheading), [`Subtitle`](/microsoft-ui-reactor/main/reference/factories/text#subtitle), [`TextBlock`](/microsoft-ui-reactor/main/reference/factories/text#textblock), [`Title`](/microsoft-ui-reactor/main/reference/factories/text#title), [`TitleLarge`](/microsoft-ui-reactor/main/reference/factories/text#titlelarge)

**Modifiers:** [`Bold`](/microsoft-ui-reactor/main/reference/modifiers/typography#bold), [`CharacterSpacing`](/microsoft-ui-reactor/main/reference/modifiers/typography#characterspacing), [`FontFamily`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontfamily), [`FontSize`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontsize), [`FontStyle`](/microsoft-ui-reactor/main/reference/modifiers/typography#fontstyle), [`IsTextSelectionEnabled`](/microsoft-ui-reactor/main/reference/modifiers/typography#istextselectionenabled), [`LineHeight`](/microsoft-ui-reactor/main/reference/modifiers/typography#lineheight), [`MaxLines`](/microsoft-ui-reactor/main/reference/modifiers/typography#maxlines), [`Selectable`](/microsoft-ui-reactor/main/reference/modifiers/typography#selectable), [`SemiBold`](/microsoft-ui-reactor/main/reference/modifiers/typography#semibold), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`TextAlignment`](/microsoft-ui-reactor/main/reference/modifiers/typography#textalignment), [`TextDecorations`](/microsoft-ui-reactor/main/reference/modifiers/typography#textdecorations), [`TextTrimming`](/microsoft-ui-reactor/main/reference/modifiers/typography#texttrimming), [`TextWrapping`](/microsoft-ui-reactor/main/reference/modifiers/typography#textwrapping)

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
