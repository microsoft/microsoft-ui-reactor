# Modifiers: Typography

Fonts, text alignment, wrapping, trimming and other text formatting.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Text and Media](../../text-and-media.md), [Styling and Theming](../../styling.md)

## `Bold`

```csharp
TextBlockElement Bold(this TextBlockElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBlockElement`](../elements/text.md#textblockelement).

## `CharacterSpacing`

Extra spacing between characters, in units of 1/1000em.

```csharp
RichTextBlockElement CharacterSpacing(this RichTextBlockElement el, int spacing)
RichTextHyperlink CharacterSpacing(this RichTextHyperlink link, int spacing)
RichTextParagraph CharacterSpacing(this RichTextParagraph paragraph, int spacing)
RichTextRun CharacterSpacing(this RichTextRun run, int spacing)
TextBlockElement CharacterSpacing(this TextBlockElement el, int spacing)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `spacing` | `int` |  |

## `FlowDirection`

```csharp
RichTextRun FlowDirection(this RichTextRun run, FlowDirection direction)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextRun`](../controls/rich-text.md#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `direction` | `FlowDirection` |  |

## `FontFamily`

Sets the font family on any FrameworkElement that supports it (Control, TextBlock). For TextBlockElement-specific chaining that preserves the TextBlockElement return type, use the TextBlockElement.FontFamily() overload instead.

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

**Applies to:** any element, [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `family` | `FontFamily` or `string` |  |

## `FontSize`

Sets the root font size on a [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

This overload is required, not sugar. Without it `.FontSize(n)` on a `RichTextBlockElement` binds the generic `FontSize<T>` modifier, which routes through `ElementModifiers` — and `ApplyModifiers` writes FontSize only to a `Control` or a `TextBlock`. `RichTextBlock` is neither, so the call would compile and silently do nothing. Mirrors the sibling `FontFamily`/`FontWeight`/`FontStyle` overloads, which set the record property and reach the control through the generated descriptor's auto-map.

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
| `FontSize(RichTextBlockElement, double)` | Sets the root font size on a [`RichTextBlockElement`](../elements/text.md#richtextblockelement).  This overload is required, not sugar. Without it `.FontSize(n)` on a `RichTextBlockElement` binds the generic `FontSize<T>` modifier, which routes through `ElementModifiers` — and `ApplyModifiers` writes FontSize only to a `Control` or a `TextBlock`. `RichTextBlock` is neither, so the call would compile and silently do nothing. Mirrors the sibling `FontFamily`/`FontWeight`/`FontStyle` overloads, which set the record property and reach the control through the generated descriptor's auto-map. |
| `FontSize(RichTextHyperlink, double)` |  |
| `FontSize(RichTextParagraph, double)` |  |
| `FontSize(RichTextRun, double)` |  |
| `FontSize(TextBlockElement, double)` |  |
| `FontSize<T>(T, double)` | Sets the font size on any FrameworkElement that supports it (Control, TextBlock). For TextBlockElement-specific chaining, use the TextBlockElement.FontSize() overload. |

**Applies to:** any element, [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `size` | `double` |  |

## `FontStretch`

```csharp
RichTextBlockElement FontStretch(this RichTextBlockElement el, FontStretch stretch)
RichTextHyperlink FontStretch(this RichTextHyperlink link, FontStretch stretch)
RichTextParagraph FontStretch(this RichTextParagraph paragraph, FontStretch stretch)
RichTextRun FontStretch(this RichTextRun run, FontStretch stretch)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `stretch` | `FontStretch` |  |

## `FontStyle`

```csharp
RichTextBlockElement FontStyle(this RichTextBlockElement el, FontStyle style)
RichTextHyperlink FontStyle(this RichTextHyperlink link, FontStyle style)
RichTextParagraph FontStyle(this RichTextParagraph paragraph, FontStyle style)
RichTextRun FontStyle(this RichTextRun run, FontStyle style)
TextBlockElement FontStyle(this TextBlockElement el, FontStyle style)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `style` | `FontStyle` |  |

## `FontWeight`

Sets the font weight on any FrameworkElement that supports it (Control, TextBlock).

```csharp
RichTextBlockElement FontWeight(this RichTextBlockElement el, FontWeight weight)
RichTextHyperlink FontWeight(this RichTextHyperlink link, FontWeight weight)
RichTextParagraph FontWeight(this RichTextParagraph paragraph, FontWeight weight)
RichTextRun FontWeight(this RichTextRun run, FontWeight weight)
T FontWeight<T>(this T el, FontWeight weight) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element, [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `weight` | `FontWeight` |  |

## `HorizontalTextAlignment`

```csharp
RichTextBlockElement HorizontalTextAlignment(this RichTextBlockElement el, TextAlignment alignment)
RichTextParagraph HorizontalTextAlignment(this RichTextParagraph paragraph, TextAlignment alignment)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `TextAlignment` |  |

## `IsColorFontEnabled`

```csharp
RichTextBlockElement IsColorFontEnabled(this RichTextBlockElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsTextScaleFactorEnabled`

```csharp
RichTextBlockElement IsTextScaleFactorEnabled(this RichTextBlockElement el, bool enabled = true)
RichTextHyperlink IsTextScaleFactorEnabled(this RichTextHyperlink link, bool enabled = true)
RichTextParagraph IsTextScaleFactorEnabled(this RichTextParagraph paragraph, bool enabled = true)
RichTextRun IsTextScaleFactorEnabled(this RichTextRun run, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsTextSelectionEnabled`

Controls whether text can be selected in a TextBlock.

```csharp
TextBlockElement IsTextSelectionEnabled(this TextBlockElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `Language`

```csharp
RichTextHyperlink Language(this RichTextHyperlink link, string language)
RichTextParagraph Language(this RichTextParagraph paragraph, string language)
RichTextRun Language(this RichTextRun run, string language)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `language` | `string` |  |

## `LineHeight`

Line height in pixels (overrides the WinUI proportional default).

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

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

## `LineStackingStrategy`

```csharp
RichTextBlockElement LineStackingStrategy(this RichTextBlockElement el, LineStackingStrategy strategy)
RichTextParagraph LineStackingStrategy(this RichTextParagraph paragraph, LineStackingStrategy strategy)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `strategy` | `LineStackingStrategy` |  |

## `MaxLines`

Maximum number of lines before trimming. `0` = no limit.

```csharp
RichTextBlockElement MaxLines(this RichTextBlockElement el, int maxLines)
TextBlockElement MaxLines(this TextBlockElement el, int maxLines)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `MaxLines(RichTextBlockElement, int)` | Maximum number of lines before trimming. `0` = no limit. |
| `MaxLines(TextBlockElement, int)` | Maximum number of lines to render before truncating per `TextTrimming`. `0` = no limit. |

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `maxLines` | `int` |  |

## `OpticalMarginAlignment`

```csharp
RichTextBlockElement OpticalMarginAlignment(this RichTextBlockElement el, OpticalMarginAlignment alignment)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `OpticalMarginAlignment` |  |

## `Selectable`

Controls whether text can be selected in a TextBlock.

```csharp
TextBlockElement Selectable(this TextBlockElement el, bool selectable = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsTextSelectionEnabled() — aligns with WinUI naming (see #268).

**Applies to:** [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `selectable` | `bool` | `true` |  |

## `SelectionHighlightColor`

Brush used to render the selection highlight.

```csharp
RichEditBoxElement SelectionHighlightColor(this RichEditBoxElement el, SolidColorBrush brush)
RichTextBlockElement SelectionHighlightColor(this RichTextBlockElement el, SolidColorBrush brush)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichEditBoxElement`](../elements/input.md#richeditboxelement), [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `brush` | `SolidColorBrush` |  |

## `SemiBold`

```csharp
TextBlockElement SemiBold(this TextBlockElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TextBlockElement`](../elements/text.md#textblockelement).

## `TextAlignment`

Horizontal alignment of text within the block.

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

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`TextBlockElement`](../elements/text.md#textblockelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alignment` | `TextAlignment` |  |

## `TextDecorations`

Underline / strikethrough decorations. Combine with bitwise OR.

```csharp
RichTextBlockElement TextDecorations(this RichTextBlockElement el, TextDecorations decorations)
RichTextHyperlink TextDecorations(this RichTextHyperlink link, TextDecorations decorations)
RichTextParagraph TextDecorations(this RichTextParagraph paragraph, TextDecorations decorations)
RichTextRun TextDecorations(this RichTextRun run, TextDecorations decorations)
TextBlockElement TextDecorations(this TextBlockElement el, TextDecorations decorations)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `decorations` | `TextDecorations` |  |

## `TextIndent`

```csharp
RichTextBlockElement TextIndent(this RichTextBlockElement el, double indent)
RichTextParagraph TextIndent(this RichTextParagraph paragraph, double indent)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `indent` | `double` |  |

## `TextLineBounds`

```csharp
RichTextBlockElement TextLineBounds(this RichTextBlockElement el, TextLineBounds bounds)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `bounds` | `TextLineBounds` |  |

## `TextReadingOrder`

```csharp
RichTextBlockElement TextReadingOrder(this RichTextBlockElement el, TextReadingOrder order)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `order` | `TextReadingOrder` |  |

## `TextTrimming`

How overflowing text is truncated.

```csharp
RichTextBlockElement TextTrimming(this RichTextBlockElement el, TextTrimming trimming)
TextBlockElement TextTrimming(this TextBlockElement el, TextTrimming trimming)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`TextBlockElement`](../elements/text.md#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `trimming` | `TextTrimming` |  |

## `TextWrapping`

How text wraps within the editor.

```csharp
RichEditBoxElement TextWrapping(this RichEditBoxElement el, TextWrapping wrapping = (TextWrapping)2)
TextBlockElement TextWrapping(this TextBlockElement el, TextWrapping wrapping = (TextWrapping)2)
TextBoxElement TextWrapping(this TextBoxElement el, TextWrapping wrapping = (TextWrapping)2)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichEditBoxElement`](../elements/input.md#richeditboxelement), [`TextBlockElement`](../elements/text.md#textblockelement), [`TextBoxElement`](../elements/input.md#textboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `wrapping` | `TextWrapping` | `(TextWrapping)2` |  |

## `UnderlineStyle`

```csharp
RichTextHyperlink UnderlineStyle(this RichTextHyperlink link, UnderlineStyle style)
```

Declared in `ElementExtensions`.

**Applies to:** [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `style` | `UnderlineStyle` |  |
