# Parser block and span details

Details the Markdown parser passes for each block and span.

**Learn more:** [Text and Media](../../text-and-media.md)

## `MarkdownAttribute`

String attribute used for propagating strings within detail structures (e.g. link titles, code info strings). May contain mixed substring types.

```csharp
public readonly struct MarkdownAttribute
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `MarkdownAttribute`

```csharp
public MarkdownAttribute(string? text, MarkdownTextType[] substrTypes, int[] substrOffsets)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string?` |  |
| `substrTypes` | [`MarkdownTextType[]`](enums.md#markdowntexttype) |  |
| `substrOffsets` | `int[]` |  |

### Methods

#### `Simple`

Creates a simple attribute with a single normal-text substring.

```csharp
public static MarkdownAttribute Simple(string? text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string?` |  |

### Fields

#### `SubstrOffsets`

```csharp
public readonly int[] SubstrOffsets
```

#### `SubstrTypes`

```csharp
public readonly MarkdownTextType[] SubstrTypes
```

#### `Text`

```csharp
public readonly string? Text
```

## `MarkdownBlockCodeDetail`

Detailed info for [`MarkdownBlockType.Code`](enums.md#values_1).

```csharp
public struct MarkdownBlockCodeDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `FenceChar`

The character used for fenced code block; or '\\0' for indented code block.

```csharp
public char FenceChar
```

#### `Info`

```csharp
public MarkdownAttribute Info
```

#### `Lang`

```csharp
public MarkdownAttribute Lang
```

## `MarkdownBlockHDetail`

Detailed info for [`MarkdownBlockType.H`](enums.md#values_1).

```csharp
public struct MarkdownBlockHDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `Level`

Header level (1-6).

```csharp
public int Level
```

## `MarkdownBlockLiDetail`

Detailed info for [`MarkdownBlockType.Li`](enums.md#values_1).

```csharp
public struct MarkdownBlockLiDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `IsTask`

True if this is a task list item (requires [`MarkdownParserFlags.TaskLists`](enums.md#values_2)).

```csharp
public bool IsTask
```

#### `TaskMark`

If IsTask, one of 'x', 'X' or ' '.

```csharp
public char TaskMark
```

#### `TaskMarkOffset`

If IsTask, offset in input of the char between '\[' and '\]'.

```csharp
public int TaskMarkOffset
```

## `MarkdownBlockOlDetail`

Detailed info for [`MarkdownBlockType.Ol`](enums.md#values_1).

```csharp
public struct MarkdownBlockOlDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `IsTight`

True if tight list, false if loose.

```csharp
public bool IsTight
```

#### `MarkDelimiter`

Character delimiting the item marks, e.g. '.' or ')'.

```csharp
public char MarkDelimiter
```

#### `Start`

Start index of the ordered list.

```csharp
public int Start
```

## `MarkdownBlockTableDetail`

Detailed info for [`MarkdownBlockType.Table`](enums.md#values_1).

```csharp
public struct MarkdownBlockTableDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `BodyRowCount`

Count of rows in the table body.

```csharp
public int BodyRowCount
```

#### `ColCount`

Count of columns in the table.

```csharp
public int ColCount
```

#### `HeadRowCount`

Count of rows in the table header (currently always 1).

```csharp
public int HeadRowCount
```

## `MarkdownBlockTdDetail`

Detailed info for [`MarkdownBlockType.Th`](enums.md#values_1) and [`MarkdownBlockType.Td`](enums.md#values_1).

```csharp
public struct MarkdownBlockTdDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `Align`

```csharp
public MarkdownAlign Align
```

## `MarkdownBlockUlDetail`

Detailed info for [`MarkdownBlockType.Ul`](enums.md#values_1).

```csharp
public struct MarkdownBlockUlDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `IsTight`

True if tight list, false if loose.

```csharp
public bool IsTight
```

#### `Mark`

Item bullet character in Markdown source, e.g. '-', '+', '\*'.

```csharp
public char Mark
```

## `MarkdownSpanADetail`

Detailed info for [`MarkdownSpanType.A`](enums.md#values_3).

```csharp
public struct MarkdownSpanADetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `Href`

```csharp
public MarkdownAttribute Href
```

#### `IsAutolink`

True if this is an autolink.

```csharp
public bool IsAutolink
```

#### `Title`

```csharp
public MarkdownAttribute Title
```

## `MarkdownSpanImgDetail`

Detailed info for [`MarkdownSpanType.Img`](enums.md#values_3).

```csharp
public struct MarkdownSpanImgDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `Src`

```csharp
public MarkdownAttribute Src
```

#### `Title`

```csharp
public MarkdownAttribute Title
```

## `MarkdownSpanWikiLinkDetail`

Detailed info for [`MarkdownSpanType.WikiLink`](enums.md#values_3).

```csharp
public struct MarkdownSpanWikiLinkDetail
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `Target`

```csharp
public MarkdownAttribute Target
```
