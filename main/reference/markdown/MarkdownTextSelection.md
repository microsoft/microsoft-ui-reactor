---
title: "MarkdownTextSelection"
description: "Message-bounded, opt-in semantic text selection for Markdown and custom content."
---

```csharp
public static class MarkdownTextSelection
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## Methods [#methods]

### `Scope` [#scope]

Wrap the complete assembled message once, not each Markdown leaf.

```csharp
public static Element Scope(MarkdownTextSelectionOptions options, Element content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`MarkdownTextSelectionOptions`](/microsoft-ui-reactor/main/reference/markdown/MarkdownTextSelectionOptions) |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### `Surface` [#surface]

Register exact visible plain text, with an explicit separator to the next surface. Chrome not included in this wrapper is not selectable.

```csharp
public static Element Surface(string text, Element content, string separatorAfter = "\n")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | `string` |  |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `separatorAfter` | `string` | `"\n"` |  |

### `Visibility` [#visibility]

Include or exclude descendant surfaces without changing the app's visual rendering. Nested gates compose. The app owns disclosure state and native Expander policy.

```csharp
public static Element Visibility(bool isVisible, Element content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `isVisible` | `bool` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
