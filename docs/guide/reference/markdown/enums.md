# Enums

The enumerations in this category.

**Learn more:** [Text and Media](../../text-and-media.md)

## `MarkdownAlign`

Table cell alignment.

```csharp
public enum MarkdownAlign
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Default` | `0` |  |
| `Left` | `1` |  |
| `Center` | `2` |  |
| `Right` | `3` |  |

## `MarkdownBlockType`

Block represents a part of document hierarchy structure like a paragraph or list item.

```csharp
public enum MarkdownBlockType
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Doc` | `0` | \<body\>...\</body\> |
| `Quote` | `1` | \<blockquote\>...\</blockquote\> |
| `Ul` | `2` | \<ul\>...\</ul\>. Detail: [`MarkdownBlockUlDetail`](parser-details.md#markdownblockuldetail). |
| `Ol` | `3` | \<ol\>...\</ol\>. Detail: [`MarkdownBlockOlDetail`](parser-details.md#markdownblockoldetail). |
| `Li` | `4` | \<li\>...\</li\>. Detail: [`MarkdownBlockLiDetail`](parser-details.md#markdownblocklidetail). |
| `Hr` | `5` | \<hr\> |
| `H` | `6` | \<h1\>...\</h6\> (levels 1-6). Detail: [`MarkdownBlockHDetail`](parser-details.md#markdownblockhdetail). |
| `Code` | `7` | \<pre\>\<code\>...\</code\>\</pre\>. Detail: [`MarkdownBlockCodeDetail`](parser-details.md#markdownblockcodedetail). |
| `Html` | `8` | Raw HTML block. |
| `P` | `9` | \<p\>...\</p\> |
| `Table` | `10` | \<table\>...\</table\>. Detail: [`MarkdownBlockTableDetail`](parser-details.md#markdownblocktabledetail). |
| `Thead` | `11` | \<thead\>...\</thead\> |
| `Tbody` | `12` | \<tbody\>...\</tbody\> |
| `Tr` | `13` | \<tr\>...\</tr\> |
| `Th` | `14` | \<th\>...\</th\>. Detail: [`MarkdownBlockTdDetail`](parser-details.md#markdownblocktddetail). |
| `Td` | `15` | \<td\>...\</td\>. Detail: [`MarkdownBlockTdDetail`](parser-details.md#markdownblocktddetail). |

## `MarkdownParserFlags`

Parser extension flags.

```csharp
public enum MarkdownParserFlags
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` |  |
| `CollapseWhitespace` | `1` | In MD_TEXT_NORMAL, collapse non-trivial whitespace into single ' '. |
| `PermissiveAtxHeaders` | `2` | Do not require space in ATX headers ( ###header ). |
| `PermissiveUrlAutolinks` | `4` | Recognize URLs as autolinks even without '\<', '\>'. |
| `PermissiveEmailAutolinks` | `8` | Recognize e-mails as autolinks even without '\<', '\>' and 'mailto:'. |
| `NoIndentedCodeBlocks` | `16` | Disable indented code blocks. (Only fenced code works.) |
| `NoHtmlBlocks` | `32` | Disable raw HTML blocks. |
| `NoHtmlSpans` | `64` | Disable raw HTML (inline). |
| `Tables` | `256` | Enable tables extension. |
| `Strikethrough` | `512` | Enable strikethrough extension. |
| `PermissiveWwwAutolinks` | `1024` | Enable WWW autolinks (even without scheme prefix, if they begin with 'www.'). |
| `TaskLists` | `2048` | Enable task list extension. |
| `LatexMathSpans` | `4096` | Enable $ and $$ containing LaTeX equations. |
| `WikiLinks` | `8192` | Enable wiki links extension. |
| `Underline` | `16384` | Enable underline extension (disables '_' for normal emphasis). |
| `HardSoftBreaks` | `32768` | Force all soft breaks to act as hard breaks. |
| `PermissiveAutolinks` | `1036` | All permissive autolinks. |
| `NoHtml` | `96` | Disable all raw HTML. |
| `DialectCommonMark` | `0` | CommonMark dialect (no extensions). |
| `DialectGitHub` | `3852` | GitHub-flavored Markdown dialect. |

## `MarkdownSpanType`

Span represents an in-line piece of a document which should be rendered with the same font, color and other attributes.

```csharp
public enum MarkdownSpanType
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Em` | `0` | \<em\>...\</em\> |
| `Strong` | `1` | \<strong\>...\</strong\> |
| `A` | `2` | \<a href="xxx"\>...\</a\>. Detail: [`MarkdownSpanADetail`](parser-details.md#markdownspanadetail). |
| `Img` | `3` | \<img src="xxx"\>...\</img\>. Detail: [`MarkdownSpanImgDetail`](parser-details.md#markdownspanimgdetail). |
| `Code` | `4` | \<code\>...\</code\> |
| `Del` | `5` | \<del\>...\</del\> (requires [`MarkdownParserFlags.Strikethrough`](#values_2)). |
| `LatexMath` | `6` | Inline LaTeX math (requires [`MarkdownParserFlags.LatexMathSpans`](#values_2)). |
| `LatexMathDisplay` | `7` | Display LaTeX math (requires [`MarkdownParserFlags.LatexMathSpans`](#values_2)). |
| `WikiLink` | `8` | Wiki link (requires [`MarkdownParserFlags.WikiLinks`](#values_2)). |
| `U` | `9` | \<u\>...\</u\> (requires [`MarkdownParserFlags.Underline`](#values_2)). |

## `MarkdownTextType`

Text is the actual textual contents of a span.

```csharp
public enum MarkdownTextType
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Normal` | `0` | Normal text. |
| `NullChar` | `1` | NULL character (should be replaced with U+FFFD). |
| `Br` | `2` | \<br\> (hard break). |
| `SoftBr` | `3` | Soft break ('\\n' in source where not semantically meaningful). |
| `Entity` | `4` | HTML entity (named, numeric, or hex). |
| `Code` | `5` | Text in a code block or inline code. |
| `Html` | `6` | Raw HTML text. |
| `LatexMath` | `7` | Text inside a LaTeX equation. |
