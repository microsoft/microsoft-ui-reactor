---
title: "Modifiers: Typography"
description: "Fonts, text alignment, wrapping, trimming and other text formatting."
sidebar:
  label: "Typography"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media), [Styling and Theming](/microsoft-ui-reactor/main/guides/styling)

## `Bold` [#bold]

```csharp
TextBlockElement Bold(this TextBlockElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

## `CharacterSpacing` [#characterspacing]

```csharp
RichTextBlockElement CharacterSpacing(this RichTextBlockElement el, int spacing)
RichTextHyperlink CharacterSpacing(this RichTextHyperlink link, int spacing)
RichTextParagraph CharacterSpacing(this RichTextParagraph paragraph, int spacing)
RichTextRun CharacterSpacing(this RichTextRun run, int spacing)
TextBlockElement CharacterSpacing(this TextBlockElement el, int spacing)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `CharacterSpacing(RichTextBlockElement, int)` | Extra spacing between characters, in units of 1/1000em. |
| `CharacterSpacing(RichTextHyperlink, int)` |  |
| `CharacterSpacing(RichTextParagraph, int)` |  |
| `CharacterSpacing(RichTextRun, int)` |  |
| `CharacterSpacing(TextBlockElement, int)` | Extra spacing between characters, in units of 1/1000em. |

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `spacing` | `int` |  |

## `FlowDirection` [#flowdirection]

```csharp
RichTextRun FlowDirection(this RichTextRun run, FlowDirection direction)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `direction` | `FlowDirection` |  |

## `FontFamily` [#fontfamily]

```csharp
RichTextBlockElement FontFamily(this RichTextBlockElement el, FontFamily family)
RichTextBlockElement FontFamily(this RichTextBlockElement el, string family)
RichTextHyperlink FontFamily(this RichTextHyperlink link, string family)
RichTextParagraph FontFamily(this RichTextParagraph paragraph, string family)
RichTextRun FontFamily(this RichTextRun run, string family)
TextBlockElement FontFamily(this TextBlockElement el, FontFamily family)
TextBlockElement FontFamily(this TextBlockElement el, string family)
T FontFamily<T>(this T el, FontFamily family) where T : Element
T FontFamily<T>(this T el, string family) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `FontFamily(RichTextBlockElement, FontFamily)` |  |
| `FontFamily(RichTextBlockElement, string)` |  |
| `FontFamily(RichTextHyperlink, string)` |  |
| `FontFamily(RichTextParagraph, string)` |  |
| `FontFamily(RichTextRun, string)` |  |
| `FontFamily(TextBlockElement, FontFamily)` |  |
| `FontFamily(TextBlockElement, string)` |  |
| `FontFamily<T>(T, FontFamily)` |  |
| `FontFamily<T>(T, string)` | Sets the font family on any FrameworkElement that supports it (Control, TextBlock). For TextBlockElement-specific chaining that preserves the TextBlockElement return type, use the TextBlockElement.FontFamily() overload instead. |

**Applies to:** any element, [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `family` | `FontFamily` or `string` |  |

## `FontSize` [#fontsize]

```csharp
RichTextBlockElement FontSize(this RichTextBlockElement el, double size)
RichTextHyperlink FontSize(this RichTextHyperlink link, double size)
RichTextParagraph FontSize(this RichTextParagraph paragraph, double size)
RichTextRun FontSize(this RichTextRun run, double size)
TextBlockElement FontSize(this TextBlockElement el, double size)
T FontSize<T>(this T el, double size) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `FontSize(RichTextBlockElement, double)` | Sets the root font size on a [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).  This overload is required, not sugar. Without it `.FontSize(n)` on a `RichTextBlockElement` binds the generic `FontSize<T>` modifier, which routes through `ElementModifiers` — and `ApplyModifiers` writes FontSize only to a `Control` or a `TextBlock`. `RichTextBlock` is neither, so the call would compile and silently do nothing. Mirrors the sibling `FontFamily`/`FontWeight`/`FontStyle` overloads, which set the record property and reach the control through the generated descriptor's auto-map. |
| `FontSize(RichTextHyperlink, double)` |  |
| `FontSize(RichTextParagraph, double)` |  |
| `FontSize(RichTextRun, double)` |  |
| `FontSize(TextBlockElement, double)` |  |
| `FontSize<T>(T, double)` | Sets the font size on any FrameworkElement that supports it (Control, TextBlock). For TextBlockElement-specific chaining, use the TextBlockElement.FontSize() overload. |

**Applies to:** any element, [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `size` | `double` |  |

## `FontStretch` [#fontstretch]

```csharp
RichTextBlockElement FontStretch(this RichTextBlockElement el, FontStretch stretch)
RichTextHyperlink FontStretch(this RichTextHyperlink link, FontStretch stretch)
RichTextParagraph FontStretch(this RichTextParagraph paragraph, FontStretch stretch)
RichTextRun FontStretch(this RichTextRun run, FontStretch stretch)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `stretch` | `FontStretch` |  |

## `FontStyle` [#fontstyle]

```csharp
RichTextBlockElement FontStyle(this RichTextBlockElement el, FontStyle style)
RichTextHyperlink FontStyle(this RichTextHyperlink link, FontStyle style)
RichTextParagraph FontStyle(this RichTextParagraph paragraph, FontStyle style)
RichTextRun FontStyle(this RichTextRun run, FontStyle style)
TextBlockElement FontStyle(this TextBlockElement el, FontStyle style)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `style` | `FontStyle` |  |

## `FontWeight` [#fontweight]

```csharp
RichTextBlockElement FontWeight(this RichTextBlockElement el, FontWeight weight)
RichTextHyperlink FontWeight(this RichTextHyperlink link, FontWeight weight)
RichTextParagraph FontWeight(this RichTextParagraph paragraph, FontWeight weight)
RichTextRun FontWeight(this RichTextRun run, FontWeight weight)
T FontWeight<T>(this T el, FontWeight weight) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `FontWeight(RichTextBlockElement, FontWeight)` |  |
| `FontWeight(RichTextHyperlink, FontWeight)` |  |
| `FontWeight(RichTextParagraph, FontWeight)` |  |
| `FontWeight(RichTextRun, FontWeight)` |  |
| `FontWeight<T>(T, FontWeight)` | Sets the font weight on any FrameworkElement that supports it (Control, TextBlock). |

**Applies to:** any element, [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `weight` | `FontWeight` |  |

## `HorizontalTextAlignment` [#horizontaltextalignment]

```csharp
RichTextBlockElement HorizontalTextAlignment(this RichTextBlockElement el, TextAlignment alignment)
RichTextParagraph HorizontalTextAlignment(this RichTextParagraph paragraph, TextAlignment alignment)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `TextAlignment` |  |

## `IsColorFontEnabled` [#iscolorfontenabled]

```csharp
RichTextBlockElement IsColorFontEnabled(this RichTextBlockElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsTextScaleFactorEnabled` [#istextscalefactorenabled]

```csharp
RichTextBlockElement IsTextScaleFactorEnabled(this RichTextBlockElement el, bool enabled = true)
RichTextHyperlink IsTextScaleFactorEnabled(this RichTextHyperlink link, bool enabled = true)
RichTextParagraph IsTextScaleFactorEnabled(this RichTextParagraph paragraph, bool enabled = true)
RichTextRun IsTextScaleFactorEnabled(this RichTextRun run, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsTextSelectionEnabled` [#istextselectionenabled]

Controls whether text can be selected in a TextBlock.

```csharp
TextBlockElement IsTextSelectionEnabled(this TextBlockElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `Language` [#language]

```csharp
RichTextHyperlink Language(this RichTextHyperlink link, string language)
RichTextParagraph Language(this RichTextParagraph paragraph, string language)
RichTextRun Language(this RichTextRun run, string language)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `language` | `string` |  |

## `LineHeight` [#lineheight]

```csharp
RichTextBlockElement LineHeight(this RichTextBlockElement el, double height)
RichTextParagraph LineHeight(this RichTextParagraph paragraph, double height)
TextBlockElement LineHeight(this TextBlockElement el, double height)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `LineHeight(RichTextBlockElement, double)` | Line height in pixels (overrides the WinUI proportional default). |
| `LineHeight(RichTextParagraph, double)` |  |
| `LineHeight(TextBlockElement, double)` | Sets the line height in pixels (overrides the proportional WinUI default). |

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

## `LineStackingStrategy` [#linestackingstrategy]

```csharp
RichTextBlockElement LineStackingStrategy(this RichTextBlockElement el, LineStackingStrategy strategy)
RichTextParagraph LineStackingStrategy(this RichTextParagraph paragraph, LineStackingStrategy strategy)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `strategy` | `LineStackingStrategy` |  |

## `MaxLines` [#maxlines]

```csharp
RichTextBlockElement MaxLines(this RichTextBlockElement el, int maxLines)
TextBlockElement MaxLines(this TextBlockElement el, int maxLines)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `MaxLines(RichTextBlockElement, int)` | Maximum number of lines before trimming. `0` = no limit. |
| `MaxLines(TextBlockElement, int)` | Maximum number of lines to render before truncating per `TextTrimming`. `0` = no limit. |

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `maxLines` | `int` |  |

## `OpticalMarginAlignment` [#opticalmarginalignment]

```csharp
RichTextBlockElement OpticalMarginAlignment(this RichTextBlockElement el, OpticalMarginAlignment alignment)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `OpticalMarginAlignment` |  |

## `Selectable` [#selectable]

Controls whether text can be selected in a TextBlock.

```csharp
TextBlockElement Selectable(this TextBlockElement el, bool selectable = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsTextSelectionEnabled() — aligns with WinUI naming (see #268).

**Applies to:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `selectable` | `bool` | `true` |  |

## `SelectionHighlightColor` [#selectionhighlightcolor]

```csharp
RichEditBoxElement SelectionHighlightColor(this RichEditBoxElement el, SolidColorBrush brush)
RichTextBlockElement SelectionHighlightColor(this RichTextBlockElement el, SolidColorBrush brush)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `SelectionHighlightColor(RichEditBoxElement, SolidColorBrush)` | Brush used to render the selection highlight. |
| `SelectionHighlightColor(RichTextBlockElement, SolidColorBrush)` |  |

**Applies to:** [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement), [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `brush` | `SolidColorBrush` |  |

## `SemiBold` [#semibold]

```csharp
TextBlockElement SemiBold(this TextBlockElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

## `TextAlignment` [#textalignment]

```csharp
RichTextBlockElement TextAlignment(this RichTextBlockElement el, TextAlignment alignment)
RichTextParagraph TextAlignment(this RichTextParagraph paragraph, TextAlignment alignment)
TextBlockElement TextAlignment(this TextBlockElement el, TextAlignment alignment)
TextBoxElement TextAlignment(this TextBoxElement el, TextAlignment alignment)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `TextAlignment(RichTextBlockElement, TextAlignment)` | Horizontal alignment of text within the block. |
| `TextAlignment(RichTextParagraph, TextAlignment)` |  |
| `TextAlignment(TextBlockElement, TextAlignment)` |  |
| `TextAlignment(TextBoxElement, TextAlignment)` | Sets horizontal text alignment within the box. |

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `TextAlignment` |  |

## `TextDecorations` [#textdecorations]

```csharp
RichTextBlockElement TextDecorations(this RichTextBlockElement el, TextDecorations decorations)
RichTextHyperlink TextDecorations(this RichTextHyperlink link, TextDecorations decorations)
RichTextParagraph TextDecorations(this RichTextParagraph paragraph, TextDecorations decorations)
RichTextRun TextDecorations(this RichTextRun run, TextDecorations decorations)
TextBlockElement TextDecorations(this TextBlockElement el, TextDecorations decorations)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `TextDecorations(RichTextBlockElement, TextDecorations)` |  |
| `TextDecorations(RichTextHyperlink, TextDecorations)` |  |
| `TextDecorations(RichTextParagraph, TextDecorations)` |  |
| `TextDecorations(RichTextRun, TextDecorations)` |  |
| `TextDecorations(TextBlockElement, TextDecorations)` | Underline / strikethrough decorations. Combine with bitwise OR. |

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph), [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `decorations` | `TextDecorations` |  |

## `TextIndent` [#textindent]

```csharp
RichTextBlockElement TextIndent(this RichTextBlockElement el, double indent)
RichTextParagraph TextIndent(this RichTextParagraph paragraph, double indent)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `indent` | `double` |  |

## `TextLineBounds` [#textlinebounds]

```csharp
RichTextBlockElement TextLineBounds(this RichTextBlockElement el, TextLineBounds bounds)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `bounds` | `TextLineBounds` |  |

## `TextReadingOrder` [#textreadingorder]

```csharp
RichTextBlockElement TextReadingOrder(this RichTextBlockElement el, TextReadingOrder order)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `order` | `TextReadingOrder` |  |

## `TextTrimming` [#texttrimming]

```csharp
RichTextBlockElement TextTrimming(this RichTextBlockElement el, TextTrimming trimming)
TextBlockElement TextTrimming(this TextBlockElement el, TextTrimming trimming)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `TextTrimming(RichTextBlockElement, TextTrimming)` | How overflowing text is truncated. |
| `TextTrimming(TextBlockElement, TextTrimming)` |  |

**Applies to:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `trimming` | `TextTrimming` |  |

## `TextWrapping` [#textwrapping]

```csharp
RichEditBoxElement TextWrapping(this RichEditBoxElement el, TextWrapping wrapping = (TextWrapping)2)
TextBlockElement TextWrapping(this TextBlockElement el, TextWrapping wrapping = (TextWrapping)2)
TextBoxElement TextWrapping(this TextBoxElement el, TextWrapping wrapping = (TextWrapping)2)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `TextWrapping(RichEditBoxElement, TextWrapping)` | How text wraps within the editor. |
| `TextWrapping(TextBlockElement, TextWrapping)` |  |
| `TextWrapping(TextBoxElement, TextWrapping)` |  |

**Applies to:** [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `wrapping` | `TextWrapping` | `(TextWrapping)2` |  |

## `UnderlineStyle` [#underlinestyle]

```csharp
RichTextHyperlink UnderlineStyle(this RichTextHyperlink link, UnderlineStyle style)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `style` | `UnderlineStyle` |  |
