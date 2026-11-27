# MarkdownLink

A Markdown link passed to [`MarkdownOptions.LinkBuilder`](MarkdownOptions.md#linkbuilder).

```csharp
public sealed record MarkdownLink
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Text and Media](../../text-and-media.md)

## Constructors

### `MarkdownLink`

```csharp
public MarkdownLink()
```

## Properties

### `Label`

The label as one selectable rich-text element preserving inline formatting, default heading/table-header typography, and images, or `null` for an empty label.

```csharp
public RichTextBlockElement? Label  // get, init
```

### `Text`

The label as plain text: its text runs concatenated, the alternative text of any rendered image in place of the image, and line breaks as spaces. Use it for an accessible name or tooltip. Empty for an empty label.

```csharp
public required string Text  // get, init
```

**Remarks**

The label is written by the document author and can claim any destination (for example `[https://bank.example](https://other.example)`). Anything that tells the user where the link goes, such as a confirmation prompt, must show [`MarkdownLink.Uri`](#uri), not this text.

### `Uri`

The link target. Always absolute with an `http`, `https`, or `mailto` scheme.

```csharp
public required Uri Uri  // get, init
```
