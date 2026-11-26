# MarkdownTextSelection

Message-bounded, opt-in semantic text selection for Markdown and custom content.

```csharp
public static class MarkdownTextSelection
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Text and Media](../../text-and-media.md)

## Methods

### `Scope`

Wrap the complete assembled message once, not each Markdown leaf.

```csharp
public static Element Scope(MarkdownTextSelectionOptions options, Element content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`MarkdownTextSelectionOptions`](MarkdownTextSelectionOptions.md) |  |
| `content` | [`Element`](../elements/Element.md) |  |

### `Surface`

Register exact visible plain text, with an explicit separator to the next surface. Chrome not included in this wrapper is not selectable.

```csharp
public static Element Surface(string text, Element content, string separatorAfter = "\n")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | `string` |  |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `separatorAfter` | `string` | `"\n"` |  |

### `Visibility`

Include or exclude descendant surfaces without changing the app's visual rendering. Nested gates compose. The app owns disclosure state and native Expander policy.

```csharp
public static Element Visibility(bool isVisible, Element content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `isVisible` | `bool` |  |
| `content` | [`Element`](../elements/Element.md) |  |
