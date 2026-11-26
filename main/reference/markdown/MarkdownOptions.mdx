---
title: "MarkdownOptions"
description: "Options for customizing how markdown is rendered to Reactor elements. All callback properties are optional — null means use the default rendering."
---

```csharp
public record MarkdownOptions
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## Constructors [#constructors]

### `MarkdownOptions` [#markdownoptions_1]

```csharp
public MarkdownOptions()
```

## Properties [#properties]

### `BlockQuote` [#blockquote]

Override block quote rendering. (defaultElement)

```csharp
public Func<Element, Element>? BlockQuote  // get, init
```

### `CodeBlock` [#codeblock]

Override code block rendering. (code, language)

```csharp
public Func<string, string?, Element>? CodeBlock  // get, init
```

### `CodeFontFamily` [#codefontfamily]

Font family for inline code and code blocks. Default: "Consolas".

```csharp
public string CodeFontFamily  // get, init
```

### `Heading` [#heading]

Override heading rendering. (level 1-6, defaultElement)

```csharp
public Func<int, Element, Element>? Heading  // get, init
```

### `HtmlBlock` [#htmlblock]

Override raw HTML block rendering. (rawHtml)

```csharp
public Func<string, Element>? HtmlBlock  // get, init
```

### `Image` [#image]

Override image rendering. (altText, src)

```csharp
public Func<string, Uri, Element>? Image  // get, init
```

### `LinkBuilder` [#linkbuilder]

Override link rendering. Receives a [`MarkdownLink`](/microsoft-ui-reactor/main/reference/markdown/MarkdownLink) with the link's plain-text label, formatted label, and validated target, and returns the element to show in its place. TASK-048. Called only for links whose target is an absolute `http`, `https`, or `mailto` URI; other links render as plain text. Apps that want to require a confirmation UX or unfurl/origin-preview for cross-site hyperlinks should provide this. The returned non-null element is embedded inline as custom UI with its own selection boundary; the callback owns its accessible name, styling, and interaction. With no override, links remain native text hyperlinks.

```csharp
public Func<MarkdownLink, Element>? LinkBuilder  // get, init
```

### `ListItem` [#listitem]

Override list item rendering. Receives the default Auto/Star Grid containing the marker and content. Content wraps when the document has a finite available width.

```csharp
public Func<Element, Element>? ListItem  // get, init
```

### `MaxInputBytes` [#maxinputbytes]

Hard cap on input markdown size in bytes. Default 4 MiB. Inputs past the cap throw at `MarkdownBuilder.Build`. TASK-049.

```csharp
public int MaxInputBytes  // get, init
```

### `OrderedList` [#orderedlist]

Override ordered list rendering. (startNumber, items)

```csharp
public Func<int, Element[], Element>? OrderedList  // get, init
```

### `Paragraph` [#paragraph]

Override paragraph rendering. (defaultElement)

```csharp
public Func<Element, Element>? Paragraph  // get, init
```

### `ParserFlags` [#parserflags]

Parser flags. Default: DialectGitHub (tables, strikethrough, task lists).

```csharp
public MarkdownParserFlags ParserFlags  // get, init
```

### `Table` [#table]

Override table rendering. (rows, columnAlignments)

```csharp
public Func<Element[], MarkdownAlign[], Element>? Table  // get, init
```

### `TableCellPadding` [#tablecellpadding]

Per-cell padding applied to every cell of the default markdown table renderer. Default `(12, 6, 12, 6)` — wide enough that adjacent numeric columns don't visually touch on Star-sized auto-shrunk tables (as happens when the table is hosted inside an [`RichTextInlineUIContainer`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextinlineuicontainer) under [`MarkdownOptions.UnifiedRichText`](#unifiedrichtext)). Ignored when [`MarkdownOptions.Table`](#table) is overridden.

```csharp
public Thickness TableCellPadding  // get, init
```

### `TableHeaderBackground` [#tableheaderbackground]

Background brush applied to header-row cells (`th`) of the default markdown table renderer. Defaults to [`Theme.SubtleFill`](/microsoft-ui-reactor/main/reference/theming/Theme#subtlefill) so the header is visually distinct in both light and dark themes (the historical hardcoded `#F0F0F0` was invisible in dark mode). Set to `null` to skip the highlight. Ignored when [`MarkdownOptions.Table`](#table) is overridden.

```csharp
public ThemeRef? TableHeaderBackground  // get, init
```

### `TextSelection` [#textselection]

Opt into semantic text surfaces by setting a non-null value, then wrap the complete message with [`MarkdownTextSelection.Scope`](/microsoft-ui-reactor/main/reference/markdown/MarkdownTextSelection#scope). Surfaces belong to the nearest scope by structure, not options identity. Only the scope's options supply copy feedback; callbacks on this parser opt-in are not used. Custom HTML/code renderers should wrap their visible text (not toolbar chrome) with [`MarkdownTextSelection.Surface`](/microsoft-ui-reactor/main/reference/markdown/MarkdownTextSelection#surface).

```csharp
public MarkdownTextSelectionOptions? TextSelection  // get, init
```

### `ThematicBreak` [#thematicbreak]

Override thematic break rendering.

```csharp
public Func<Element>? ThematicBreak  // get, init
```

### `UnifiedRichText` [#unifiedrichtext]

When true, render the entire document as a SINGLE [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement) with one [`RichTextParagraph`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextparagraph) per markdown block. Flow content (paragraphs, headings) becomes native paragraphs so WinUI's text selection works seamlessly across them; block content that doesn't fit the flow model (lists, code blocks, block quotes, tables, thematic breaks, raw HTML, standalone images) is embedded via [`RichTextInlineUIContainer`](/microsoft-ui-reactor/main/reference/controls/rich-text#richtextinlineuicontainer) as its own paragraph.

Default `false` preserves the historical `VStack(per-block elements)` shape so existing apps and tests are unchanged. Apps that render long prose (e.g. assistant chat replies) should opt-in: it enables drag-selection across the whole reply, a shared baseline rhythm, and a single rich-text accessibility surface. Adjacent block-level elements (lists/code/tables) still break selection where they appear, but consecutive paragraphs and headings before and after them remain selectable as continuous text.

```csharp
public bool UnifiedRichText  // get, init
```

### `UnorderedList` [#unorderedlist]

Override unordered list rendering. (items)

```csharp
public Func<Element[], Element>? UnorderedList  // get, init
```
