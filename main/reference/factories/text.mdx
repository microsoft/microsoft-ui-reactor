---
title: "Factories: Text"
description: "Text blocks, rich text and the typography factories."
sidebar:
  label: "Text"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## `Body` [#body]

WinUI 3 `BodyTextBlockStyle` — 14px regular body text.

```csharp
TextBlockElement Body(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `BodyLarge` [#bodylarge]

WinUI 3 `BodyLargeTextBlockStyle` — 18px regular body text.

```csharp
TextBlockElement BodyLarge(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `BodyStrong` [#bodystrong]

WinUI 3 `BodyStrongTextBlockStyle` — 14px Semibold body text.

```csharp
TextBlockElement BodyStrong(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `Caption` [#caption]

Creates a caption-styled [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement) (12px).

```csharp
TextBlockElement Caption(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

**Remarks**

Reactor-original convenience wrapper — there is no WinUI control named `Caption`. Sized for the WinUI Caption type-ramp slot; use for secondary metadata (timestamps, helper text, hints) below primary copy. (spec 039 §0.3)

## `Display` [#display]

WinUI 3 `DisplayTextBlockStyle` — 68px Semibold hero text. Use sparingly: hero banners and splash screens, at most one per page.

```csharp
TextBlockElement Display(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `Heading` [#heading]

Creates a heading-styled [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement) (28px, bold, automation heading level 1).

```csharp
TextBlockElement Heading(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

**Remarks**

Reactor-original convenience wrapper — there is no WinUI control named `Heading`. Sized for the WinUI Title type-ramp slot, with the accessibility heading level set so screen readers announce it as a landmark. Prefer this over hand-styled [`TextBlock`](#textblock) for page / section titles. (spec 039 §0.3)

## `Hyperlink` [#hyperlink]

```csharp
RichTextHyperlink Hyperlink(string text, Action onClick)
RichTextHyperlink Hyperlink(string text, Uri navigateUri)
```

| Overload | Description |
| --- | --- |
| `Hyperlink(string, Action)` | Issue #479 — clickable inline hyperlink: fires `onClick` on click and suppresses platform navigation. Use for in-flow interactive fragments (open a flyout, enter edit mode) inside a virtualized list of [`RichTextBlock`](#richtextblock) rows where giving each fragment its own `UIElement` would be too heavy. |
| `Hyperlink(string, Uri)` |  |

**Returns:** [`RichTextHyperlink`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtexthyperlink).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |
| `onClick` | `Action` |  |
| `navigateUri` | `Uri` |  |

## `InlineUI` [#inlineui]

```csharp
RichTextInlineUIContainer InlineUI(Element child)
RichTextInlineUIContainer InlineUI(Func<FrameworkElement> factory)
```

| Overload | Description |
| --- | --- |
| `InlineUI(Element)` | Issue #480 — embeds a Reactor `child` element inline inside a [`RichTextBlock`](#richtextblock). Mirrors WinUI's `InlineUIContainer`. The child is reconciled as any other Reactor element — descendant hooks, event wiring, and pooling all work as expected. Re-renders use the incremental update path: structurally compatible inlines are mutated in place and embedded children retain their WinUI control identity (Slider drag, focus, and animation state survive). Only structural changes (paragraph count change, inline type change, Route A↔B swap) fall back to a full block rebuild. |
| `InlineUI(Func<FrameworkElement>)` | Issue #480 — embeds a native WinUI control (produced by `factory`) inline inside a [`RichTextBlock`](#richtextblock). Escape hatch for controls without a Reactor element counterpart. The factory is re-invoked only when its delegate identity changes; passing the same factory across renders preserves the control instance. |

**Returns:** [`RichTextInlineUIContainer`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextinlineuicontainer).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `factory` | `Func<FrameworkElement>` |  |

## `Paragraph` [#paragraph]

```csharp
RichTextParagraph Paragraph(params RichTextInline[] inlines)
```

**Returns:** [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `inlines` | [`RichTextInline[]`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextinline) |  |

## `RichTextBlock` [#richtextblock]

```csharp
RichTextBlockElement RichTextBlock(RichTextParagraph[] paragraphs)
RichTextBlockElement RichTextBlock(string text)
```

| Overload | Description |
| --- | --- |
| `RichTextBlock(RichTextParagraph[])` | Creates a [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement) from an array of typed paragraphs. Each paragraph contains a sequence of inline runs / hyperlinks / line breaks built with [`Paragraph`](#paragraph), [`Run`](#run), and [`Hyperlink`](#hyperlink). |
| `RichTextBlock(string)` | Creates a [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement) wrapping a single string of plain text. Use the [`RichTextBlock`](#richtextblock) overload to compose runs, hyperlinks, and inline formatting. |

**Returns:** [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `paragraphs` | [`RichTextParagraph[]`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph) |  |
| `text` | `string` |  |

**Remarks**

Named for parity with WinUI's `Microsoft.UI.Xaml.Controls.RichTextBlock`. (spec 039 §1.3 / §14 #8)

## `Run` [#run]

```csharp
RichTextRun Run(string text)
```

**Returns:** [`RichTextRun`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |

## `SubHeading` [#subheading]

Creates a sub-heading styled [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement) (20px, semi-bold, automation heading level 2).

```csharp
TextBlockElement SubHeading(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

**Remarks**

Reactor-original convenience wrapper — there is no WinUI control named `SubHeading`. Pairs with [`Heading`](#heading) for the secondary section level; sized for the WinUI Subtitle type-ramp slot. (spec 039 §0.3)

## `Subtitle` [#subtitle]

WinUI 3 `SubtitleTextBlockStyle` — 20px Semibold sub-heading.

```csharp
TextBlockElement Subtitle(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `TextBlock` [#textblock]

```csharp
TextBlockElement TextBlock(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `Title` [#title]

WinUI 3 `TitleTextBlockStyle` — 28px Semibold heading.

```csharp
TextBlockElement Title(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `TitleBar` [#titlebar]

```csharp
TitleBarElement TitleBar(string title)
```

**Returns:** [`TitleBarElement`](/microsoft-ui-reactor/main/reference/elements/navigation#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |

## `TitleLarge` [#titlelarge]

WinUI 3 `TitleLargeTextBlockStyle` — 40px Semibold large title. Use for primary page titles on feature or landing pages.

```csharp
TextBlockElement TitleLarge(string content)
```

**Returns:** [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |
