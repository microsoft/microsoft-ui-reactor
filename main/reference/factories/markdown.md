---
title: "Factories: Markdown"
description: "Rendered Markdown, from the Microsoft.UI.Reactor.Advanced package."
sidebar:
  label: "Markdown"
---

Each factory method returns an element record. These are on `Microsoft.UI.Reactor.Advanced.Factories` in the `Microsoft.UI.Reactor.Advanced` package, so add `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## `Markdown` [#markdown]

```csharp
Element Markdown(string markdown)
Element Markdown(string markdown, MarkdownOptions options)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

| Overload | Description |
| --- | --- |
| `Markdown(string)` | Render a markdown string as a Reactor element tree. |
| `Markdown(string, MarkdownOptions)` | Render a markdown string as a Reactor element tree with custom rendering options. |

**Returns:** [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `markdown` | `string` |  |
| `options` | [`MarkdownOptions`](/microsoft-ui-reactor/main/reference/markdown/MarkdownOptions) |  |
